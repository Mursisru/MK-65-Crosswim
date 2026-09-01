using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Hull Probe (ships/statics) + proximity fuse vs other torpedoes (IgnoreCollisions layer).
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

        /// <summary>
        /// Torpedoes sit on IgnoreCollisions — SphereCast never sees them. Proximity vs UnitRegistry.
        /// </summary>
        internal static bool ProbeMissile(Missile self, out Vector3 hitNormal, out Missile? other)
        {
            hitNormal = Vector3.up;
            other = null;
            if (self == null)
                return false;

            Vector3 pos = self.transform.position;
            float prox = CrosswimConstants.DetonateProximityM;
            float proxSq = prox * prox;

            Unit? assigned = CrosswimHoming.ResolveAssigned(self);
            if (assigned is Missile assignedMis && !assignedMis.disabled &&
                !CrosswimBootstrap.IsOurMissile(assignedMis))
            {
                float sq = (assignedMis.transform.position - pos).sqrMagnitude;
                if (sq <= proxSq)
                {
                    other = assignedMis;
                    hitNormal = (assignedMis.transform.position - pos).normalized;
                    if (hitNormal.sqrMagnitude < 0.01f)
                        hitNormal = self.transform.forward;
                    return true;
                }
            }

            FactionHQ? hq = self.NetworkHQ;
            var units = UnitRegistry.allUnits;
            float bestSq = proxSq;
            Missile? best = null;
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u is not Missile m || m.disabled || ReferenceEquals(m, self))
                    continue;
                if (CrosswimBootstrap.IsOurMissile(m))
                    continue;
                if (hq != null && m.NetworkHQ != null && m.NetworkHQ == hq)
                    continue;

                float sq = (m.transform.position - pos).sqrMagnitude;
                if (sq > bestSq)
                    continue;
                bestSq = sq;
                best = m;
            }

            if (best == null)
                return false;

            other = best;
            hitNormal = (best.transform.position - pos).normalized;
            if (hitNormal.sqrMagnitude < 0.01f)
                hitNormal = self.transform.forward;
            return true;
        }

        internal static bool ProbeAny(Missile missile, out RaycastHit hit, out string reason, out Missile? victim)
        {
            hit = default;
            reason = "hull";
            victim = null;

            if (ProbeHull(missile, out hit))
            {
                if (IsShip(hit))
                    reason = "ship";
                else
                {
                    Missile? m = hit.collider != null
                        ? hit.collider.GetComponentInParent<Missile>()
                        : null;
                    if (m != null)
                    {
                        reason = "missile";
                        victim = m;
                    }
                }
                return true;
            }

            if (ProbeMissile(missile, out Vector3 n, out Missile? other))
            {
                hit.normal = n;
                hit.point = other != null ? other.transform.position : missile.transform.position;
                reason = "missile";
                victim = other;
                return true;
            }

            return false;
        }

        internal static void DetonateNow(Missile missile, Vector3 normal, string reason, Missile? victim = null)
        {
            if (missile == null || missile.disabled)
                return;

            Vector3 pos = missile.transform.position;
            bool under = pos.y < Datum.LocalSeaY + 0.1f;
            bool shipHit = reason.IndexOf("ship", System.StringComparison.OrdinalIgnoreCase) >= 0;

            CrosswimPlugin.ModLog?.LogInfo(
                $"Crosswim impact '{reason}' pos={pos} under={under} victim={(victim != null ? victim.unitName : "-")}");

            // Intercept kill first — MK-88 needs TakeDamage to open DetonateGate.
            if (victim != null && !victim.disabled)
                CrosswimBlast.KillMissile(victim, missile.persistentID);

            CrosswimShellPrep.EnsureBlastYield(missile);
            CrosswimWarheadFx.Ensure(missile);
            CrosswimShellPrep.Arm(missile);
            CrosswimDetonateGate.Allow = true;
            CrosswimWarheadFx.FxGate++;
            try
            {
                missile.Detonate(normal, shipHit && !under, false);
            }
            finally
            {
                CrosswimWarheadFx.FxGate--;
                CrosswimDetonateGate.Allow = false;
            }

            // Yield &lt;200 kg: vanilla skips Shockwave damage — scaled CrosswimBlast instead.
            CrosswimBlast.Apply(missile, pos);
        }

        private static bool IsIgnoredHit(Missile missile, RaycastHit hit)
        {
            if (hit.collider == null)
                return true;
            if (hit.distance < 0.04f)
                return true;

            Missile? otherMis = hit.collider.GetComponentInParent<Missile>();
            if (otherMis != null)
            {
                if (CrosswimBootstrap.IsOurMissile(otherMis) || ReferenceEquals(otherMis, missile))
                    return true;
                if (missile.NetworkHQ != null && otherMis.NetworkHQ != null &&
                    missile.NetworkHQ == otherMis.NetworkHQ)
                    return true;
                return false;
            }

            if (IsHarmlessWater(hit))
                return true;
            if (hit.point.y > missile.transform.position.y + 0.15f && !IsShip(hit))
                return true;
            if (hit.collider.transform.IsChildOf(missile.transform))
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
