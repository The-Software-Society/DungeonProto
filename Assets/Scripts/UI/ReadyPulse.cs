using UnityEngine;

namespace AldenFall.UI
{
    /// <summary>Slow breathing pulse — used on ability slots that are ready.</summary>
    public class ReadyPulse : MonoBehaviour
    {
        public UnityEngine.UI.Graphic target;
        public float speed = 2.6f;
        public float min = 0.0f;
        public float max = 0.45f;
        public bool active;

        private void Update()
        {
            if (target == null) return;
            var c = target.color;
            c.a = active ? Mathf.Lerp(min, max, (Mathf.Sin(Time.unscaledTime * speed) + 1f) * 0.5f) : 0f;
            target.color = c;
        }
    }
}
