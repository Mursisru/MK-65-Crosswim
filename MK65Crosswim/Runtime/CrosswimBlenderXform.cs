using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Blender local → Unity FBX child. Absolute Map includes −90X (importer).
    /// Deltas use swizzle only — rest already has importer −90X.
    /// </summary>
    internal static class CrosswimBlenderXform
    {
        internal static Vector3 Pos(float bx, float by, float bz) =>
            new Vector3(-bx, bz, by);

        internal static Quaternion Rot(float qw, float qx, float qy, float qz)
        {
            Quaternion mapped = new Quaternion(-qx, -qz, -qy, qw);
            return Quaternion.Euler(-90f, 0f, 0f) * mapped;
        }

        internal static Quaternion Delta(float qw, float qx, float qy, float qz) =>
            new Quaternion(-qx, -qz, -qy, qw);
    }
}
