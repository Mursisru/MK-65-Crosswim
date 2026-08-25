using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Homing strictly on the unit assigned at fire (missile.targetID). No opportunistic scan.
    /// </summary>
    internal static class CrosswimHoming
    {
        internal static Unit? ResolveAssigned(Missile self)
        {
            if (self == null)
                return null;
            if (!self.targetID.IsValid)
                return null;
            if (!UnitRegistry.TryGetUnit(new PersistentID?(self.targetID), out Unit t) || t == null)
                return null;
            if (t.disabled)
                return null;
            return t;
        }

        internal static Vector3 InterceptPoint(Vector3 pos, Vector3 vel, Unit? target, Vector3 fallbackHeading, out Vector3 lead)
        {
            lead = Vector3.zero;
            if (target == null)
            {
                Vector3 head = Horiz(fallbackHeading);
                if (head.sqrMagnitude < 0.01f)
                    head = Horiz(vel);
                if (head.sqrMagnitude < 0.01f)
                    head = Vector3.forward;
                return pos + head.normalized * 200f;
            }

            Vector3 tgtPos = target.transform.position;
            Rigidbody? trb = target.rb != null ? target.rb : target.GetComponent<Rigidbody>();
            Vector3 tgtVel = trb != null ? trb.velocity : Vector3.zero;
            float speed = Mathf.Max(vel.magnitude, CrosswimConstants.SwimSpeedMps * 0.35f);
            Vector3 flat = Horiz(tgtPos - pos);
            float dist = flat.magnitude;
            float t = Mathf.Clamp(dist / Mathf.Max(speed, 1f), 0f, CrosswimConstants.InterceptLeadMaxS);
            lead = tgtVel * t;
            return tgtPos + lead;
        }

        private static Vector3 Horiz(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
