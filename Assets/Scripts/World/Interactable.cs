using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// Base for world objects the player can press E on (fishing spots,
    /// campfires). Keeps a static registry so PlayerInteractor can poll the
    /// nearest one without physics queries. Lives on the Interactable layer.
    /// </summary>
    public class Interactable : MonoBehaviour
    {
        private static readonly List<Interactable> active = new List<Interactable>();
        public static IReadOnlyList<Interactable> Active => active;

        [SerializeField] private string prompt = "Interact";
        [SerializeField] private float radius = 3f;

        public string Prompt { get => prompt; set => prompt = value; }
        public float Radius { get => radius; set => radius = value; }

        protected virtual void OnEnable() => active.Add(this);
        protected virtual void OnDisable() => active.Remove(this);

        public virtual void Interact(GameObject player) { }

        public static Interactable Nearest(Vector3 position, out float distance)
        {
            Interactable best = null;
            float bestDist = float.MaxValue;
            foreach (var candidate in active)
            {
                float d = Vector3.Distance(position, candidate.transform.position);
                if (d <= candidate.radius && d < bestDist)
                {
                    bestDist = d;
                    best = candidate;
                }
            }
            distance = bestDist;
            return best;
        }
    }
}
