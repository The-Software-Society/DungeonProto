using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>Client visual-chunk activation. The authored world may contain thousands of static
    /// instances, but only chunks intersecting the normal 140m visual envelope stay active. Terrain,
    /// network simulation and encounter authority remain separate from these visual groups.
    /// (Chunk markers live in WorldChunkMarker.cs -- file name MUST match the class.)</summary>
    public sealed class WorldChunkStreamer : MonoBehaviour
    {
        public float activeRadius = 140f;
        public float refreshSeconds = 0.2f;
        readonly List<WorldChunkMarker> chunks = new();
        float nextRefresh;

        void Start() => GetComponentsInChildren(true, chunks);

        void LateUpdate()
        {
            if (Application.isBatchMode || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshSeconds;
            var cam = Camera.main; if (cam == null) return;
            Vector2 p = new Vector2(cam.transform.position.x, cam.transform.position.z);
            foreach (var chunk in chunks)
            {
                if (chunk == null) continue;
                float reach = activeRadius + chunk.halfDiagonal;
                bool should = (chunk.center - p).sqrMagnitude <= reach * reach;
                if (chunk.gameObject.activeSelf != should) chunk.gameObject.SetActive(should);
            }
        }
    }
}
