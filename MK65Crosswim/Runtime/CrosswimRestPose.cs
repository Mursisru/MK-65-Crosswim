using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Frame-0 Cube/OP from crosswim_dump.json.
    /// Positions: Blender (x,y,z) → Unity (−x, z, y).
    /// Rotations: Blender XYZ euler → quat, then (−x,−z,−y,w), then * Euler(−90,0,0) like FBX import.
    /// </summary>
    internal static class CrosswimRestPose
    {
        private struct Rest
        {
            public string name;
            public Vector3 blenderLoc;
            public Vector3 blenderEulerRad;
        }

        private static readonly Rest[] Opening =
        {
            new Rest { name = "Cube", blenderLoc = new Vector3(-1.69196f, 0.00028f, 0.68979f), blenderEulerRad = new Vector3(0f, -1.57036f, 1.5708f) },
            new Rest { name = "Cube.001", blenderLoc = new Vector3(-1.69196f, -0.68979f, 0.00028f), blenderEulerRad = new Vector3(3.14159f, -0.00044f, -1.5708f) },
            new Rest { name = "Cube.002", blenderLoc = new Vector3(-1.69196f, 0.00028f, -0.68979f), blenderEulerRad = new Vector3(0f, 1.46512f, 1.5708f) },
            new Rest { name = "Cube.003", blenderLoc = new Vector3(-1.69196f, 0.68979f, 0.00028f), blenderEulerRad = new Vector3(0f, -0.10567f, 1.5708f) },
            new Rest { name = "OP", blenderLoc = new Vector3(5.10305f, 0f, 0f), blenderEulerRad = new Vector3(0f, -0.05262f, 0f) },
            new Rest { name = "OP.001", blenderLoc = new Vector3(5.10305f, 0f, 0f), blenderEulerRad = new Vector3(1.5708f, 0f, -0.05262f) },
            new Rest { name = "OP.002", blenderLoc = new Vector3(5.10305f, 0f, 0f), blenderEulerRad = new Vector3(0f, -0.05262f, 0f) },
            new Rest { name = "OP.003", blenderLoc = new Vector3(5.10305f, 0f, 0f), blenderEulerRad = new Vector3(1.5708f, 0f, -0.05262f) }
        };

        internal static void ApplyClosed(Transform vis)
        {
            if (vis == null)
                return;

            for (int i = 0; i < Opening.Length; i++)
            {
                Rest r = Opening[i];
                Transform? t = FindExact(vis, r.name);
                if (t == null)
                    continue;
                t.localPosition = BlenderLocToUnity(r.blenderLoc);
                t.localRotation = BlenderEulerToUnity(r.blenderEulerRad);
                t.localScale = Vector3.one * CrosswimConstants.FbxChildScale;
            }
        }

        internal static Vector3 BlenderLocToUnity(Vector3 blender) =>
            new Vector3(-blender.x, blender.z, blender.y);

        internal static Quaternion BlenderEulerToUnity(Vector3 rad)
        {
            Quaternion b = Quaternion.Euler(
                rad.x * Mathf.Rad2Deg,
                rad.y * Mathf.Rad2Deg,
                rad.z * Mathf.Rad2Deg);
            // Blender quat → Unity (same remap as Unity's Blender FBX importer).
            Quaternion mapped = new Quaternion(-b.x, -b.z, -b.y, b.w);
            return Quaternion.Euler(-90f, 0f, 0f) * mapped;
        }

        private static Transform? FindExact(Transform root, string name)
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
