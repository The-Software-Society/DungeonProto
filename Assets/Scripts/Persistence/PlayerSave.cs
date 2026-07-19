using Mirror;
using UnityEngine;
using AldenFall.Items;
using AldenFall.Progression;

namespace AldenFall.Persistence
{
    /// <summary>
    /// Server-side per-player persistence. Added to the player at runtime by
    /// NetworkPlayerAvatar.EnsureGameplayComponents - a PLAIN component (never a scene
    /// network object), so it cannot perturb host boot. On the server it loads the
    /// owning account's saved profile shortly after spawn (gold, all skills, inventory,
    /// equipment) and writes it back on a debounced timer and on teardown.
    ///
    /// Account scope: keyed by the owning client's account id, plumbed server-side through
    /// the login profile onto NetworkPlayerAvatar.AccountId (Phase 13). This lets the
    /// dedicated server persist EVERY player under their own id, not just the host. Falls
    /// back to the local PlayerIdentity for the host's own player, and no-ops when no
    /// account id is known (so nothing is ever saved to a shared "anon" bucket).
    /// </summary>
    public class PlayerSave : MonoBehaviour
    {
        public const float SaveInterval = 30f;

        private NetworkIdentity netId;
        private Wallet wallet;
        private Skills skills;
        private Inventory inventory;
        private Equipment equipment;
        private Quests.QuestLog quests;
        private Progression.PlayerProgress progress;
        private World.VistaProgress vistas;
        private Multiplayer.NetworkPlayerAvatar avatar;

        private bool loaded;
        private float nextSaveAt;

        private void Awake()
        {
            netId = GetComponent<NetworkIdentity>();
            wallet = GetComponent<Wallet>();
            skills = GetComponent<Skills>();
            inventory = GetComponent<Inventory>();
            equipment = GetComponent<Equipment>();
            quests = GetComponent<Quests.QuestLog>();
            progress = GetComponent<Progression.PlayerProgress>();
            vistas = GetComponent<World.VistaProgress>();
            avatar = GetComponent<Multiplayer.NetworkPlayerAvatar>();
        }

        /// <summary>The owning account's id: the client's plumbed profile id, or the local
        /// PlayerIdentity for the host's own player. Empty when unknown (then we do not save).</summary>
        private string ResolveAccountId()
        {
            if (avatar != null && !string.IsNullOrEmpty(avatar.AccountId)) return avatar.AccountId;
            if (netId != null && netId.isLocalPlayer) return PlayerIdentity.AccountId;
            return "";
        }

        /// <summary>The server persists every player that has a known account id.</summary>
        private bool ShouldPersist => netId != null && netId.isServer && !string.IsNullOrEmpty(ResolveAccountId());

        private void Update()
        {
            if (!ShouldPersist) return;

            if (!loaded)
            {
                ForceLoad();
                loaded = true;
                nextSaveAt = Time.time + SaveInterval;
                return;
            }

            if (Time.time >= nextSaveAt)
            {
                nextSaveAt = Time.time + SaveInterval;
                ForceSave();
            }
        }

        private void OnDestroy()
        {
            if (loaded && ShouldPersist)
                ForceSave();
        }

        public string AccountId => ResolveAccountId();

        /// <summary>Snapshot the live player into a save record.</summary>
        public PlayerSaveData Capture()
        {
            var data = new PlayerSaveData { accountId = AccountId };
            if (wallet != null) data.gold = wallet.Gold;
            if (progress != null) data.floor1Cleared = progress.Floor1Cleared;
            if (vistas != null) foreach (var id in vistas.Discovered) data.discoveredVistas.Add(id);

            data.skillLevels = new int[Skills.SkillCount];
            data.skillXp = new float[Skills.SkillCount];
            if (skills != null)
                for (int i = 0; i < Skills.SkillCount; i++)
                {
                    data.skillLevels[i] = skills.Level((SkillType)i);
                    data.skillXp[i] = skills.Xp((SkillType)i);
                }

            if (inventory != null)
                foreach (var e in inventory.Slots)
                {
                    data.invItems.Add(e.ItemId);
                    data.invCounts.Add(e.Count);
                }

            if (equipment != null)
                foreach (var slot in Equipment.Slots)
                {
                    var id = equipment.GetEquipped(slot);
                    if (string.IsNullOrEmpty(id)) continue;
                    data.equipSlots.Add(slot.ToString());
                    data.equipItems.Add(id);
                }

            if (quests != null)
                foreach (var kv in quests.States)
                {
                    data.questIds.Add(kv.Key);
                    data.questStatus.Add((int)kv.Value.Status);
                }

            return data;
        }

        /// <summary>Apply a loaded save onto the live player, rebuilding bag/gear from scratch.</summary>
        public void Apply(PlayerSaveData data)
        {
            if (data == null) return;

            wallet?.SetGold(data.gold);
            progress?.Restore(data.floor1Cleared);
            vistas?.Restore(data.discoveredVistas);

            if (skills != null && data.skillLevels != null)
            {
                int n = Mathf.Min(data.skillLevels.Length, Skills.SkillCount);
                for (int i = 0; i < n; i++)
                {
                    float xp = (data.skillXp != null && i < data.skillXp.Length) ? data.skillXp[i] : 0f;
                    skills.Restore((SkillType)i, data.skillLevels[i], xp);
                }
            }

            // Rebuild the bag + gear from scratch so a returning player does not stack on
            // the starter loadout that spawn already granted. Guarded on a non-empty save so
            // a first-ever load never wipes the fresh starter kit to nothing.
            if (inventory != null && (data.invItems.Count > 0 || data.equipItems.Count > 0))
            {
                equipment?.ClearAll();
                inventory.Clear();

                for (int i = 0; i < data.invItems.Count; i++)
                {
                    int count = (i < data.invCounts.Count) ? data.invCounts[i] : 1;
                    for (int c = 0; c < count; c++) inventory.Add(data.invItems[i]);
                }

                for (int i = 0; i < data.equipItems.Count; i++)
                {
                    inventory.Add(data.equipItems[i]);   // Equip pulls it from the bag
                    equipment?.Equip(data.equipItems[i]);
                }
            }
            // Quest restore intentionally deferred (needs QuestLog state reconstruction);
            // captured above for forward-compat only.
        }

        public void ForceSave()
        {
            PlayerPersistence.Save(Capture());
        }

        public void ForceLoad()
        {
            var data = PlayerPersistence.Load(AccountId);
            if (data != null) Apply(data);
        }
    }
}
