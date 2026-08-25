using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Clean VLSB mesh separation (vanilla VLSBooster.Burnout style).
    /// Never put Rigidbody on FBX scale-100 node — that teleports/lags.
    /// </summary>
    internal static class CrosswimVlsbShed
    {
        internal static void Detach(Transform? visual, Vector3 missileVel, Vector3 aftWorld, float dryMassKg)
        {
            if (visual == null)
                return;

            Transform? vlsb = CrosswimVisualParts.FindExact(visual, "VLSB");
            if (vlsb == null)
                vlsb = CrosswimVisualParts.FindByAliases(visual, CrosswimConstants.VlsbAliases);
            if (vlsb == null)
            {
                CrosswimVisualParts.KillVlsbFx(visual);
                return;
            }

            CrosswimVisualParts.KillVlsbFxSubtree(vlsb);

            Vector3 pos = vlsb.position;
            Quaternion rot = vlsb.rotation;

            // Unscaled world root — Rigidbody on FBX×100 child warps COM and teleports forward.
            GameObject debris = new GameObject("CrosswimVlsbDebris");
            debris.transform.SetPositionAndRotation(pos, rot);

            vlsb.SetParent(debris.transform, true);

            Collider[] cols = debris.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = false;
            }

            Rigidbody rb = debris.AddComponent<Rigidbody>();
            rb.mass = dryMassKg > 1f ? dryMassKg : CrosswimConstants.VlsbDryMassKg;
            rb.drag = 0.1f;
            rb.angularDrag = 0.01f;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.detectCollisions = false;
            // Drop slightly aft of body — same idea as booster burnout separation.
            Vector3 sep = aftWorld.sqrMagnitude > 0.01f ? aftWorld.normalized : Vector3.back;
            rb.velocity = missileVel - sep * CrosswimConstants.VlsbShedSepMps + Vector3.down * 1.5f;
            rb.angularVelocity = Vector3.zero;

            Object.Destroy(debris, CrosswimConstants.DockDestroyS);
            CrosswimVisualParts.KillVlsbFx(visual);
        }
    }
}
