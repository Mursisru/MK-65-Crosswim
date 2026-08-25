using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// DockingPort mesh: Warewind-style detach — unparent, scale=1, then DestroyImmediate same frame.
    /// </summary>
    internal static class CrosswimDockEject
    {
        internal static bool TryEject(Missile? missile, Transform? visual)
        {
            if (missile == null)
                return false;

            Transform? vis = visual;
            if (vis == null)
                vis = PrefabFactory.FindVisual(missile.transform);
            if (vis == null)
                return false;

            int killed = 0;
            // Multiple passes — hierarchy mutates while destroying.
            for (int pass = 0; pass < 4; pass++)
            {
                Transform? dock = FindDockingPortMesh(vis);
                if (dock == null)
                    dock = FindDockingPortMesh(missile.transform);
                if (dock == null)
                    break;

                // Same-frame removal. Physics eject left a scale-100 ghost on the hull.
                Renderer[] rs = dock.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    if (rs[i] != null)
                        rs[i].enabled = false;
                }
                dock.SetParent(null, true);
                dock.localScale = Vector3.one;
                dock.gameObject.SetActive(false);
                Object.DestroyImmediate(dock.gameObject);
                killed++;
            }

            if (killed > 0)
                CrosswimPlugin.ModLog?.LogInfo($"Crosswim DockingPort DestroyImmediate x{killed}");
            else
                CrosswimPlugin.ModLog?.LogWarning("Crosswim DockingPort mesh not found under visual.");

            return killed > 0 || !HasDockingPortLeft(missile.transform);
        }

        internal static bool HasDockingPortLeft(Transform root)
        {
            return FindDockingPortMesh(root) != null;
        }

        private static Transform? FindDockingPortMesh(Transform root)
        {
            if (root == null)
                return null;

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            Transform? fallback = null;
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null)
                    continue;
                string n = t.name;
                if (n.IndexOf("DockingPlace", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (n.IndexOf("DockingPort", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Prefer the object that actually draws.
                if (t.GetComponent<MeshFilter>() != null || t.GetComponent<MeshRenderer>() != null)
                    return t;
                fallback ??= t;
            }
            return fallback;
        }
    }
}
