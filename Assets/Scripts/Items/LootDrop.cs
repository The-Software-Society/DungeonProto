using UnityEngine;
using Mirror;
using AldenFall.Multiplayer;

namespace AldenFall.Items
{
    /// <summary>
    /// A networked world-item pickup: bobbing, spinning, glowing, auto-collected when
    /// a player walks close. The WORLD drop is server-authoritative (spawned via
    /// NetworkServer.Spawn) so every player sees it; inventories stay client-local, so
    /// pickup routes through the server which grants the item to the requesting player.
    /// First requester in range wins — no double pickups.
    /// </summary>
    public class LootDrop : NetworkBehaviour
    {
        private const float PickupRange = 1.8f;
        private const float Lifetime = 120f;
        private const float OwnershipSeconds = 60f;   // killer (+party) exclusive window

        [SyncVar(hook = nameof(OnItemIdSync))] private string itemId;
        [SyncVar] private float pickupDelay;   // grace so a just-dropped item isn't re-vacuumed

        // Ownership: for the first 60s only the killer or their party (snapshot taken at
        // spawn, server-only) may pick this up. Synced so clients can stop asking for
        // loot that isn't theirs; the SERVER check in CmdTryPickup is authoritative.
        [SyncVar] private uint ownerNetId;             // 0 = unowned (public immediately)
        [SyncVar] private double protectedUntil;       // NetworkTime.time when it goes public
        private System.Collections.Generic.List<uint> allowedNetIds; // server-only party snapshot

        private Transform player;
        private float baseY;
        private float age;
        private Light glow;
        private bool visualBuilt;
        private float nextPickupRequestTime;   // client: throttle pickup requests
        private bool claimed;                  // server: first requester wins

        public string ItemId => itemId;

        // ---------------- spawning ----------------

        private static GameObject cachedPrefab;
        private static GameObject Prefab =>
            cachedPrefab != null ? cachedPrefab : (cachedPrefab = Resources.Load<GameObject>("NetLootDrop"));

        /// <summary>
        /// Server-authoritative spawn — a networked world drop visible to everyone.
        /// Pass <paramref name="owner"/> (the killer) to reserve the drop for them +
        /// their party for the first 60s; null = public immediately (bag drops, chests).
        /// </summary>
        public static LootDrop ServerSpawn(string itemId, Vector3 position, float pickupDelay = 0f,
            GameObject owner = null)
        {
            if (!NetworkServer.active) return null;
            if (ItemDatabase.Get(itemId) == null) return null;

            var prefab = Prefab;
            if (prefab == null)
            {
                Debug.LogWarning("LootDrop: NetLootDrop prefab missing from Resources — rebuild the demo (AldenFall > Build Emberfall Demo).");
                return null;
            }

            var obj = Instantiate(prefab, position + Vector3.up * 0.5f, Quaternion.identity);
            var drop = obj.GetComponent<LootDrop>();
            drop.itemId = itemId;          // SyncVar set before Spawn → carried in the spawn payload
            drop.pickupDelay = pickupDelay;

            var ownerIdentity = owner != null ? owner.GetComponent<NetworkIdentity>() : null;
            if (ownerIdentity != null)
            {
                drop.ownerNetId = ownerIdentity.netId;
                drop.protectedUntil = NetworkTime.time + OwnershipSeconds;
                // Party snapshot at SPAWN time — joining a party later doesn't grant
                // access to drops that were already on the ground.
                drop.allowedNetIds = PartySystem.SnapshotMemberIds(ownerIdentity.netId);
            }

            NetworkServer.Spawn(obj);
            return drop;
        }

        /// <summary>
        /// Back-compat entry used by every legacy call site (mob death, chests, bag drops,
        /// cooking overflow). On the server it spawns directly; on a client it asks the
        /// server to spawn via the local player's relay. Returns null on the client path
        /// (the networked spawn is asynchronous).
        /// </summary>
        public static LootDrop Spawn(string itemId, Vector3 position, float pickupDelay = 0f,
            GameObject owner = null)
        {
            if (NetworkServer.active)
                return ServerSpawn(itemId, position, pickupDelay, owner);

            var localPlayer = LocalPlayerLocator.Find();
            var combat = localPlayer != null ? localPlayer.GetComponent<AldenFall.Combat.PlayerCombat>() : null;
            combat?.RequestDrop(itemId, position, pickupDelay);
            return null;
        }

        /// <summary>
        /// Add to the bag if there's room, otherwise drop at the owner's feet — a created
        /// item must NEVER silently vanish (cooking with a full bag). Returns true when it
        /// went into the bag.
        /// </summary>
        public static bool GiveOrDrop(Inventory inventory, string itemId, Transform owner)
        {
            if (inventory != null && inventory.Add(itemId)) return true;

            Vector3 pos = owner.position + owner.forward * 1.2f;
            Spawn(itemId, pos, pickupDelay: 2f);
            return false;
        }

        // ---------------- visual (client) ----------------

        public override void OnStartClient()
        {
            baseY = transform.position.y;
            EnsureVisual();
        }

        private void OnItemIdSync(string _, string __)
        {
            EnsureVisual();
        }

        private void EnsureVisual()
        {
            if (visualBuilt || string.IsNullOrEmpty(itemId)) return;
            var def = ItemDatabase.Get(itemId);
            if (def == null) return;
            BuildVisual(def);
            visualBuilt = true;
        }

        private void BuildVisual(ItemDef def)
        {
            var library = ItemVisualLibrary.Instance;
            var modelPrefab = library != null ? library.GetModel(def.Id) : null;

            GameObject visual;
            if (modelPrefab != null)
            {
                visual = Instantiate(modelPrefab, transform);
                // Normalize to pickup size
                var renderers = visual.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.001f);
                    visual.transform.localScale *= 0.7f / longest;
                }
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(visual.GetComponent<Collider>());
                visual.transform.SetParent(transform, false);
                visual.transform.localScale = Vector3.one * 0.35f;
                var renderer = visual.GetComponent<MeshRenderer>();
                renderer.material.color = def.RarityColor;
                if (renderer.material.HasProperty("_EmissionColor"))
                {
                    renderer.material.EnableKeyword("_EMISSION");
                    renderer.material.SetColor("_EmissionColor", def.RarityColor * 1.5f);
                }
            }
            visual.transform.localPosition = Vector3.zero;

            // Rarity glow
            var glowObj = new GameObject("Glow");
            glowObj.transform.SetParent(transform, false);
            glow = glowObj.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = def.RarityColor;
            glow.intensity = 1.6f;
            glow.range = 3.5f;
            glow.shadows = LightShadows.None;

            baseY = transform.position.y;
        }

        private void Update()
        {
            age += Time.deltaTime;

            // Server owns lifetime despawn.
            if (isServer && age > Lifetime)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            if (!isClient) return;

            // Cosmetic bob + spin + glow pulse (local only — position isn't networked past spawn).
            transform.position = new Vector3(
                transform.position.x,
                baseY + Mathf.Sin(Time.time * 2.2f) * 0.15f,
                transform.position.z);
            transform.Rotate(Vector3.up, 55f * Time.deltaTime, Space.World);
            if (glow != null)
                glow.intensity = 1.6f + Mathf.Sin(Time.time * 3f) * 0.35f;

            TryPickup();
        }

        private void TryPickup()
        {
            if (age < pickupDelay) return;
            if (Time.time < nextPickupRequestTime) return;
            if (IsProtectedFromLocalPlayer()) return;   // someone else's drop (first 60s)

            if (player == null)
            {
                var playerObj = LocalPlayerLocator.Find();
                if (playerObj == null) return;
                player = playerObj.transform;
            }

            if (Vector3.Distance(transform.position, player.position + Vector3.up) > PickupRange)
                return;

            // Ask the server to grant it. Retry a couple times a second until granted (the
            // object despawns on success) — a denial (someone else grabbed it) despawns it too.
            nextPickupRequestTime = Time.time + 0.4f;
            CmdTryPickup();
        }

        /// <summary>
        /// CLIENT: cheap pre-check so we never spam pickup requests for a drop that is
        /// still inside someone else's 60s ownership window. Approximate (uses the local
        /// CURRENT party roster, not the server's spawn-time snapshot) — the server-side
        /// check in CmdTryPickup is the authoritative one.
        /// </summary>
        private bool IsProtectedFromLocalPlayer()
        {
            if (ownerNetId == 0 || NetworkTime.time >= protectedUntil) return false;

            var local = NetworkClient.localPlayer;
            if (local == null) return true;              // can't prove it's ours yet
            if (local.netId == ownerNetId) return false; // our own kill

            var partyMember = local.GetComponent<Multiplayer.PartyMember>();
            return partyMember == null || !partyMember.IsPartiedWith(ownerNetId);
        }

        /// <summary>SERVER: may this netId take the drop right now?</summary>
        private bool ServerMayPickup(uint requesterNetId)
        {
            if (ownerNetId == 0 || NetworkTime.time >= protectedUntil) return true; // public
            if (requesterNetId == ownerNetId) return true;
            return allowedNetIds != null && allowedNetIds.Contains(requesterNetId);
        }

        [Command(requiresAuthority = false)]
        private void CmdTryPickup(NetworkConnectionToClient sender = null)
        {
            if (claimed || sender == null || sender.identity == null) return;
            if (age < pickupDelay) return;   // server-side grace re-check
            if (!ServerMayPickup(sender.identity.netId)) return;   // owned by someone else (60s)

            // Range check against the requesting player (generous, for client/server lag).
            float dist = Vector3.Distance(transform.position, sender.identity.transform.position + Vector3.up);
            if (dist > PickupRange + 1.5f) return;

            claimed = true;
            TargetGrant(sender, itemId);
            NetworkServer.Destroy(gameObject);
        }

        [TargetRpc]
        private void TargetGrant(NetworkConnection target, string grantedItemId)
        {
            var playerObj = LocalPlayerLocator.Find();
            var inventory = playerObj != null ? playerObj.GetComponent<Inventory>() : null;
            inventory?.Add(grantedItemId);
        }
    }
}
