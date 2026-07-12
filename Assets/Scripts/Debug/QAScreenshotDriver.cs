using System.Collections;
using System.IO;
using Mirror;
using UnityEngine;

namespace AldenFall.DebugTools
{
    /// <summary>
    /// Automated visual-QA pass. ONLY active when the player is launched with the
    /// -qashots command-line flag (never in normal play): boots a local HOST
    /// session (full server+client, every system live), teleports the avatar
    /// through the key areas of the V2 world, flips the eruption world-state, and
    /// writes a screenshot per stop via ScreenCapture. Exits the app when done.
    ///
    /// Output dir: -qadir &lt;absolute path&gt; (default: persistentDataPath/qa).
    /// </summary>
    public static class QAScreenshotBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-qashots") < 0)
                return;
            var go = new GameObject("QAScreenshotDriver");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<QAScreenshotDriver>();
        }
    }

    public class QAScreenshotDriver : MonoBehaviour
    {
        private string outDir;

        // (shot name, teleport position, facing yaw — the camera snaps behind the avatar)
        private static readonly (string name, Vector3 pos, float yaw)[] Shots =
        {
            ("01_glade_spawn",    new Vector3(0f, 1.5f, 84f),     0f),    // face Aldric
            ("02_city_north_gate",new Vector3(0f, 1.5f, 62f),     180f),  // approach the city wall
            ("03_city_plaza",     new Vector3(0f, 1.5f, 14f),     180f),  // fountain + streets
            ("04_city_market",    new Vector3(24f, 1.5f, 20f),    20f),   // market row + vendor
            ("05_lake_bank",      new Vector3(-100f, 1.5f, 148f), 315f),  // lake + goblins + fishing
            ("06_dark_woods",     new Vector3(0f, 1.5f, 180f),    0f),
            ("07_goblin_camp",    new Vector3(-180f, 1.5f, 0f),   270f),
            ("08_orc_warcamp",    new Vector3(-98f, 1.5f, -108f), 225f),  // grukmar + cage
            ("09_ember_approach", new Vector3(0f, 1.5f, -150f),   180f),  // soot gradient toward volcano
            ("10_volcano_mouth",  new Vector3(0f, 1.5f, -300f),   180f),
            ("11_dungeon_lavahall", new Vector3(0f, 1.5f, -360f), 180f),  // lava channels
            ("12_boss_chamber",   new Vector3(0f, 1.5f, -426f),   180f),  // boss + sealed door behind
        };

        private IEnumerator Start()
        {
            // Never let the intro cutscene hijack the QA run.
            PlayerPrefs.SetInt("cutscene_intro_seen_v2", 1);
            PlayerPrefs.Save();

            var args = System.Environment.GetCommandLineArgs();
            int dirArg = System.Array.IndexOf(args, "-qadir");
            outDir = dirArg >= 0 && dirArg + 1 < args.Length
                ? args[dirArg + 1]
                : Path.Combine(Application.persistentDataPath, "qa");
            Directory.CreateDirectory(outDir);
            Debug.Log($"QAScreenshotDriver: writing shots to {outDir}");

            while (NetworkManager.singleton == null) yield return null;
            var manager = NetworkManager.singleton as Multiplayer.AldenNetworkManager;
            if (manager == null) { Fail("no AldenNetworkManager"); yield break; }

            manager.SetLocalLogin("localhost", manager.RequiredPassword,
                new Multiplayer.AldenPlayerProfile { playerName = "QA", gender = 0, skinTone = 0, hairStyle = 1, hairColor = 0 });
            manager.StartHost();

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 30f)
            {
                wait += Time.deltaTime;
                yield return null;
            }
            if (NetworkClient.localPlayer == null) { Fail("player never spawned"); yield break; }

            var player = NetworkClient.localPlayer.gameObject;
            player.GetComponent<Combat.Health>()?.SetInvulnerable(true); // QA rig doesn't die mid-shoot

            // Let the runtime NavMesh bake, spawners, and atmosphere settle.
            yield return new WaitForSeconds(5f);

            foreach (var (name, pos, yaw) in Shots)
            {
                Teleport(player, pos, yaw);
                yield return new WaitForSeconds(2.2f);   // camera SmoothDamp + LODs settle
                yield return Capture(name);
            }

            // ---- Eruption sequence: phase flips fire the real climax cutscenes ----
            var world = World.EmberfallWorldState.Instance;
            var director = player.GetComponent<Cinematics.CutsceneDirector>();
            if (world != null)
            {
                Teleport(player, new Vector3(0f, 1.5f, -150f), 180f);
                yield return new WaitForSeconds(1f);
                world.DebugForcePhase(World.EmberfallWorldState.Phase.Erupting);
                yield return new WaitForSeconds(4f);     // inside the volcano establishing shot
                yield return Capture("13_cutscene_eruption");
                yield return WaitForCutsceneEnd(director);

                Teleport(player, new Vector3(0f, 1.5f, 14f), 180f);
                yield return new WaitForSeconds(2.5f);
                yield return Capture("14_city_under_eruption");

                world.DebugForcePhase(World.EmberfallWorldState.Phase.Cleared);
                yield return new WaitForSeconds(4f);
                yield return Capture("15_cutscene_floor_cleared");
                yield return WaitForCutsceneEnd(director);

                Teleport(player, new Vector3(0f, 1.5f, -438f), 180f);  // stairway beyond the boss
                yield return new WaitForSeconds(2.2f);
                yield return Capture("16_floor2_gate_open");
            }

            Debug.Log("QAScreenshotDriver: done.");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        private IEnumerator WaitForCutsceneEnd(Cinematics.CutsceneDirector director)
        {
            float t = 0f;
            while (director != null && director.IsPlaying && t < 60f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(1f);
        }

        private void Teleport(GameObject player, Vector3 pos, float yaw)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (cc != null) cc.enabled = true;

            Camera.main?.GetComponent<Player.ThirdPersonCamera>()?.SnapToAngle(yaw, 14f);
        }

        private IEnumerator Capture(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
            yield return new WaitForSeconds(0.6f);       // capture lands end-of-frame; give it air
            Debug.Log($"QAScreenshotDriver: captured {name}");
        }

        private void Fail(string why)
        {
            Debug.LogError($"QAScreenshotDriver: FAILED — {why}");
            Application.Quit(2);
        }
    }
}
