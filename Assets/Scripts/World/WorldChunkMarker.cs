using UnityEngine;

namespace AldenFall.World
{
    /// <summary>Per-chunk marker consumed by WorldChunkStreamer. MUST live in its own file named
    /// after the class: a MonoBehaviour in a mismatched file bakes into the scene as a MISSING
    /// script, which the editor tolerates but corrupts the standalone build's scene load
    /// ("level0 corrupted" + "[Position out of bounds!]").</summary>
    public sealed class WorldChunkMarker : MonoBehaviour
    {
        public Vector2 center;
        public float halfDiagonal = 46f;
    }
}
