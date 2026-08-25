using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// IST-100 Image Empties must NOT spawn quads.
    /// Decal empties are parented under Main (localScale 100) — a 1m quad becomes a hangar-sized slab.
    /// </summary>
    internal static class VisualDecals
    {
        internal static void Attach(GameObject root)
        {
            // Intentionally empty. Extra polygons in hangar were these quads.
        }
    }
}
