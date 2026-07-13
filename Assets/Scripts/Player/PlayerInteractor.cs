using UnityEngine;
using AldenFall.World;

namespace AldenFall.Player
{
    /// <summary>
    /// The game's first interaction hook: polls the nearest Interactable in
    /// range, surfaces its prompt to the HUD ("E, Fish"), and fires it on E.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private KeyCode interactKey = KeyCode.E;
        [SerializeField] private float pollInterval = 0.2f;

        private PlayerController controller;
        private Combat.Health health;
        private Interactable current;
        private float nextPollAt;

        /// <summary>Prompt text for the HUD chip ("[E] Fish"), null when nothing is in range.</summary>
        public string CurrentPrompt =>
            current != null ? $"[{interactKey}] {current.Prompt}" : null;

        public Interactable Current => current;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            health = GetComponent<Combat.Health>();
        }

        private void Update()
        {
            bool blocked = (health != null && health.IsDead) ||
                           (controller != null && controller.IsSwimming);
            if (blocked)
            {
                current = null;
                return;
            }

            if (Time.time >= nextPollAt)
            {
                nextPollAt = Time.time + pollInterval;
                current = Interactable.Nearest(transform.position, out _);
            }

            if (current != null && Input.GetKeyDown(interactKey))
                current.Interact(gameObject);
        }
    }
}
