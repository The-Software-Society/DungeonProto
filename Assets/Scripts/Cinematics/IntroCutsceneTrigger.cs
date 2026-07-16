using System.Collections;
using Mirror;
using UnityEngine;

namespace AldenFall.Cinematics
{
    /// <summary>
    /// Plays the intro cutscene on FIRST LOGIN only (PlayerPrefs flag), for the
    /// local player only. Waits a beat after scene load so the camera, HUD, and
    /// mobs are settled first. The intro ends on Aldric's weapon-choice beat, so
    /// the "seen" flag is only committed once the scene fully finishes, quitting
    /// mid-scene lets you choose again next login.
    ///
    /// The seen-key is VERSIONED: bumping it makes the reworked intro replay once
    /// for everyone, even players whose old key was already set from an earlier build.
    /// The F9 "reset intro" cheat deletes this exact key (see DebugCheatPanel).
    ///
    /// IMPORTANT (why the intro stopped playing live): the Editor and a same-machine
    /// standalone build SHARE one PlayerPrefs store. Previously the seen-flag was written
    /// on completion even during Editor play, and the QA harness set it directly, so a
    /// real build on the dev machine read "seen" and skipped. Fixes: (a) never persist the
    /// flag during an Editor replay-bypass, (b) QA/batchmode skips WITHOUT touching the
    /// flag, (c) bumped the key so stale flags are invalidated.
    /// </summary>
    [RequireComponent(typeof(CutsceneDirector))]
    public class IntroCutsceneTrigger : MonoBehaviour
    {
        // v6: intro v2 (black-video fix + canon dialogue + deeper voice + no weapon panel). Bump so
        // it replays once for testers who already watched v5. BUMP THIS EVERY TIME THE INTRO CHANGES,
        // or anyone who saw the previous version has the seen-flag set and the scene is skipped.
        public const string SeenKey = "cutscene_intro_seen_v6";

        // DEV-ONLY skip so repeated dev testing does not replay the intro every single login. Set by the
        // `-nointro` launch flag OR a persistent toggle in the DebugCheatPanel ("Intro on login"). Real
        // players on the shipped build never set this, so they still get the intro. See DebugCheatPanel.
        public const string DevSkipKey = "dev_skip_intro";
        public static bool DevSkipRequested =>
            System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nointro") >= 0 ||
            PlayerPrefs.GetInt(DevSkipKey, 0) != 0;

        private IEnumerator Start()
        {
            // Networked avatars: only the local player watches their own intro.
            // On a fresh client spawn Start can run the same frame the object
            // arrives, before Mirror has finished assigning local authority, so
            // wait a short window for the identity to spawn before deciding.
            var identity = GetComponent<NetworkIdentity>();
            if (identity != null)
            {
                // Wait on isLocalPlayer itself, not just netId: authority can be
                // assigned a frame after the netId lands, and a one-shot check at
                // that moment would permanently skip the intro for the owner.
                float spawnWait = 0f;
                while (!identity.isLocalPlayer && spawnWait < 5f)
                {
                    spawnWait += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (!identity.isLocalPlayer) yield break;
            }

            // QA / headless never plays the intro (it would hijack the camera during the shot tour).
            if (Application.isBatchMode) yield break;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-qashots") >= 0) yield break;

            // DEV-ONLY skip (see DevSkipKey): still hand the player their first quest so they are not
            // left questless, but do not seize the camera for the cinematic.
            if (DevSkipRequested)
            {
                GetComponent<Quests.QuestLog>()?.Accept("q_intro");
                yield break;
            }

            // NO once-per-machine gate: there is no account persistence yet, so every login is a
            // FRESH account and the intro plays EVERY time (SPACE/ESC skips). When Supabase account
            // data lands, gate this on a per-account "seen intro" flag instead of always playing.

            // Let the camera, HUD, and mobs settle before the director seizes the
            // camera. The director reads Camera.main in TakeControl, so make sure
            // one exists first, a null main camera would break the scripted shots.
            yield return new WaitForSeconds(0.6f);
            float camWait = 0f;
            while (Camera.main == null && camWait < 5f)
            {
                camWait += Time.unscaledDeltaTime;
                yield return null;
            }
            if (Camera.main == null)
            {
                // Playing without a camera would NRE mid-scene and strand the player
                // on a black screen with input disabled. Skip and leave the seen-flag
                // unset so the intro retries next login.
                Debug.LogWarning("IntroCutsceneTrigger: no main camera after 5s, skipping intro this session.");
                yield break;
            }

            var director = GetComponent<CutsceneDirector>();
            if (director == null || !director.Play("intro")) yield break;

            // Commit "seen" only after the scene (and the mandatory weapon choice) completes.
            while (director.IsPlaying)
                yield return null;

            // Lead straight into the first quest: offer q_intro (talk-to-Aldric) so it is already
            // tracked the instant the player gains control, standing right in front of Aldric.
            // Idempotent (Accept -> CanAccept guard); runs whether the scene played or was skipped.
            GetComponent<Quests.QuestLog>()?.Accept("q_intro");
            // No seen-flag persisted: every login replays the intro until per-account data exists.
        }
    }
}
