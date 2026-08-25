using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// MK-88 hull Probe fuse: PhysX off, SphereCast body-sized along velocity into Ships/Statics.
    /// </summary>
    internal static class CrosswimImpact
    {
        private const float SeaHitSlackM = 0.75f;

        private static readonly int LethalMask =
            PhysicsLayers.StaticsMask.value |
            PhysicsLayers.ShipsMask.value |
            PhysicsLayers.DefaultMask.value;

        internal static bool ProbeHull(Missile missile, out RaycastHit hit)
        {
            hit = default;
            if (missile?.rb == null)
                return false;

            Transform xform = missile.transform;
            Vector3 vel = missile.rb.velocity;
            float speed = vel.magnitude;
            Vector3 dir = speed > 0.2f ? vel / speed : xform.forward;
            if (dir.sqrMagnitude < 0.01f)
                return false;

            float radius = CrosswimConstants.WidthM * 0.45f;
            float halfLen = CrosswimConstants.LengthM * 0.5f;
            Vector3 nose = xform.position + xform.forward * halfLen;
            float look = Mathf.Max(radius * 0.5f, speed * Time.fixedDeltaTime * 1.35f);

            if (!Physics.SphereCast(
                    nose,
                    radius,
                    dir,
                    out hit,
                    look,
                    LethalMask,
                    QueryTriggerInteraction.Ignore))
                return false;

            return !IsIgnoredHit(missile, hit);
        }

        internal static void DetonateNow(Missile missile, Vector3 normal, string reason)
        {
            if (missile == null || missile.disabled)
                return;

            Vector3 pos = missile.transform.position;
            bool under = pos.y < Datum.LocalSeaY + 0.1f;
            bool shipHit = reason.IndexOf("ship", System.StringComparison.OrdinalIgnoreCase) >= 0;

            CrosswimPlugin.ModLog?.LogInfo(
                $"Crosswim impact '{reason}' pos={pos} under={under}");

            CrosswimShellPrep.Arm(missile);
            CrosswimDetonateGate.Allow = true;
            try
            {
                missile.Detonate(normal, shipHit && !under, false);
            }
            finally
            {
                CrosswimDetonateGate.Allow = false;
            }
        }

        private static bool IsIgnoredHit(Missile missile, RaycastHit hit)
        {
            if (hit.collider == null)
                return true;
            if (hit.distance < 0.04f)
                return true;
            if (IsHarmlessWater(hit))
                return true;
            if (hit.point.y > missile.transform.position.y + 0.15f && !IsShip(hit))
                return true;
            if (hit.collider.transform.IsChildOf(missile.transform))
                return true;
            // Other Crosswim / own units already covered; ignore friendly missiles.
            Missile? other = hit.collider.GetComponentInParent<Missile>();
            if (other != null && CrosswimBootstrap.IsOurMissile(other))
                return true;
            if (missile.ownerID.IsValid &&
                UnitRegistry.TryGetUnit(new PersistentID?(missile.ownerID), out Unit owner) &&
                owner != null &&
                hit.collider.transform.IsChildOf(owner.transform))
                return true;
            return false;
        }

        private static bool IsHarmlessWater(RaycastHit hit)
        {
            if (hit.collider != null && hit.collider.gameObject.layer == PhysicsLayers.Water)
                return true;
            // Ship hull below waterline is a real hit — only ignore sea/static surface clutter.
            if (IsShip(hit))
                return false;
            return hit.point.y <= Datum.LocalSeaY + SeaHitSlackM;
        }

        private static bool IsShip(RaycastHit hit)
        {
            if (hit.collider == null)
                return false;
            if (hit.collider.gameObject.layer == PhysicsLayers.Ships)
                return true;
            return hit.collider.GetComponentInParent<Ship>() != null;
        }
    }
}
