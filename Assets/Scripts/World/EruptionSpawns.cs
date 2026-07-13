using System.Collections;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// Near-city eruption consequences. Holds a pre-built, disabled "Content" child
    /// (lava pools with LavaHazard) that is switched on only while the floor is in
    /// the Erupting phase and off again on Calm/Cleared. This is how the city visibly
    /// suffers once the volcano wakes. Lava damages (visible lava only); the ambient
    /// soot and embers stay cosmetic and never hurt. Subscribes to the networked
    /// EmberfallWorldState so it reacts on every client.
    /// </summary>
    public class EruptionSpawns : MonoBehaviour
    {
        private Transform content;
        private EmberfallWorldState world;

        private IEnumerator Start()
        {
            content = transform.Find("Content");
            if (content != null) content.gameObject.SetActive(false);

            while (EmberfallWorldState.Instance == null)
                yield return null;

            world = EmberfallWorldState.Instance;
            world.OnPhaseChanged += Apply;
            Apply(world.CurrentPhase);
        }

        private void OnDestroy()
        {
            if (world != null) world.OnPhaseChanged -= Apply;
        }

        private void Apply(EmberfallWorldState.Phase phase)
        {
            if (content != null)
                content.gameObject.SetActive(phase == EmberfallWorldState.Phase.Erupting);
        }
    }
}
