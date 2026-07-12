using System.Collections;
using Mirror;
using UnityEngine;

namespace AldenFall.Cinematics
{
    /// <summary>
    /// Plays the intro cutscene on FIRST LOGIN only (PlayerPrefs flag), for the
    /// local player only. Waits a beat after scene load so the camera, HUD, and
    /// mobs are settled first. The intro ends on Aldric's weapon-choice beat, so
    /// the "seen" flag is only committed once the scene fully finishes — quitting
    /// mid-scene lets you choose again next login.
    ///
    /// The seen-key is VERSIONED: bumping it (v1 -> v2) makes the reworked intro
    /// replay once for everyone, even players whose old "cutscene_intro_seen" key
    /// was already set from an earlier build.
    /// F9 cheat mindset: delete the pref to rewatch ("cutscene_intro_seen_v2").
    /// </summary>
    [RequireComponent(typeof(CutsceneDirector))]
    public class IntroCutsceneTrigger : MonoBehaviour
    {
        // v2: reworked "meet Aldric / choose a starting weapon" intro. A new key
        // guarantees the new scene shows once even for veterans of older builds.
        private const string SeenKey = "cutscene_intro_seen_v2";

        [SerializeField] private bool replayEveryEditorPlay = true;

        private IEnumerator Start()
        {
            // Networked avatars: only the local player watches their own intro.
            // On a fresh client spawn Start can run the same frame the object
            // arrives, before Mirror has finished assigning local authority — so
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

#if UNITY_EDITOR
            if (!replayEveryEditorPlay && PlayerPrefs.GetInt(SeenKey, 0) != 0) yield break;
#else
            if (PlayerPrefs.GetInt(SeenKey, 0) != 0) yield break;
#endif

            // Let the camera, HUD, and mobs settle before the director seizes the
            // camera. The director reads Camera.main in TakeControl, so make sure
            // one exists first — a null main camera would break the scripted shots.
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
                Debug.LogWarning("IntroCutsceneTrigger: no main camera after 5s — skipping intro this session.");
                yield break;
            }

            var director = GetComponent<CutsceneDirector>();
            if (director == null || !director.Play("intro")) yield break;

            // Commit "seen" only after the scene (and the mandatory weapon choice) completes.
            while (director.IsPlaying)
                yield return null;

            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
        }
    }
}
