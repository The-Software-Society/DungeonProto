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
    /// F9 cheat mindset: delete the pref to rewatch ("cutscene_intro_seen").
    /// </summary>
    [RequireComponent(typeof(CutsceneDirector))]
    public class IntroCutsceneTrigger : MonoBehaviour
    {
        private const string SeenKey = "cutscene_intro_seen";

        [SerializeField] private bool replayEveryEditorPlay = true;

        private IEnumerator Start()
        {
            // Networked avatars: only the local player watches their own intro.
            var identity = GetComponent<NetworkIdentity>();
            if (identity != null && !identity.isLocalPlayer) yield break;

#if UNITY_EDITOR
            if (!replayEveryEditorPlay && PlayerPrefs.GetInt(SeenKey, 0) != 0) yield break;
#else
            if (PlayerPrefs.GetInt(SeenKey, 0) != 0) yield break;
#endif

            yield return new WaitForSeconds(0.6f);

            var director = GetComponent<CutsceneDirector>();
            if (!director.Play("intro")) yield break;

            // Commit "seen" only after the scene (and the mandatory weapon choice) completes.
            while (director.IsPlaying)
                yield return null;

            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
        }
    }
}
