using System.Collections;
using UnityEngine;

namespace AldenFall.Cinematics
{
    /// <summary>
    /// Plays the intro cutscene once per save (PlayerPrefs flag). Waits a beat
    /// after scene load so the camera, HUD, and mobs are settled first.
    /// F9 cheat mindset: delete the pref to rewatch ("cutscene_intro_seen").
    /// </summary>
    [RequireComponent(typeof(CutsceneDirector))]
    public class IntroCutsceneTrigger : MonoBehaviour
    {
        private const string SeenKey = "cutscene_intro_seen";

        [SerializeField] private bool replayEveryEditorPlay = true;

        private IEnumerator Start()
        {
#if UNITY_EDITOR
            if (!replayEveryEditorPlay && PlayerPrefs.GetInt(SeenKey, 0) != 0) yield break;
#else
            if (PlayerPrefs.GetInt(SeenKey, 0) != 0) yield break;
#endif

            yield return new WaitForSeconds(0.6f);

            if (GetComponent<CutsceneDirector>().Play("intro"))
                PlayerPrefs.SetInt(SeenKey, 1);
        }
    }
}
