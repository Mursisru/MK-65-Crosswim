using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Hangar = FBX imported local TRS (Blender bind). No dump overwrite.
    /// </summary>
    internal static class CrosswimCubeClosed
    {
        internal static void Apply(Transform? vis)
        {
            if (vis == null)
                return;
            CrosswimCubeDriver driver = vis.GetComponent<CrosswimCubeDriver>();
            if (driver == null)
                driver = vis.gameObject.AddComponent<CrosswimCubeDriver>();
            driver.CaptureBindIfNeeded();
            driver.StopClosed();
        }

        internal static Transform? FindExact(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && string.Equals(all[i].name, name, System.StringComparison.Ordinal))
                    return all[i];
            }
            return null;
        }
    }
}
