using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// Applies the tiered, data-driven view distances (config/world.json via GameConfig.World) to the
    /// camera: the far clip covers the baked environment (buildings, mountains) for vistas, while the
    /// "Detail" layer (small foliage) and "Prop" layer (trees/boulders) cull at closer distances so
    /// the whole map's scatter doesn't render at once. Living things stream in per-locale via Mirror
    /// interest management (a separate system); this is purely the client's render culling.
    /// Re-reads on Start so editing world.json needs no rebuild.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(50)]
    public class CameraViewDistance : MonoBehaviour
    {
        private void Start() => Apply();

        public void Apply()
        {
            var cam = GetComponent<Camera>();
            if (cam == null) return;
            var w = Data.GameConfig.World;

            cam.farClipPlane = Mathf.Max(cam.farClipPlane, w.environmentDistance);

            // layerCullDistances is a per-layer float[32]; 0 = use the far clip. Set only our two
            // scatter layers shorter; everything else (ground, buildings, mountains, water) stays far.
            float[] dists = cam.layerCullDistances;
            SetLayer(dists, "Detail", w.detailDistance);
            SetLayer(dists, "Prop", w.propDistance);
            cam.layerCullDistances = dists;

            // Spherical (distance-based), not planar, so props don't pop when you rotate the camera.
            cam.layerCullSpherical = true;
        }

        private static void SetLayer(float[] dists, string layerName, float distance)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0 && layer < dists.Length && distance > 0f)
                dists[layer] = distance;
        }
    }
}
