using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    internal static class CrosswimHoming
    {
        internal static Unit? SelectTarget(Missile self, Unit? current)
        {
            if (self == null)
                return current;
            Unit? torpedo = FindHostileUnderwaterMissile(self);
            if (torpedo != null)
                return torpedo;
            if (IsUseful(self, current))
                return current;
            return FindHostileShip(self);
        }

        internal static Vector3 InterceptPoint(Vector3 pos, Vector3 vel, Unit? target, out Vector3 lead)
        {
            lead = Vector3.zero;
            if (target == null)
                return pos + vel;
            Vector3 tgtPos = target.transform.position;
            Rigidbody? trb = target.rb != null ? target.rb : target.GetComponent<Rigidbody>();
            Vector3 tgtVel = trb != null ? trb.velocity : Vector3.zero;
            float speed = Mathf.Max(vel.magnitude, CrosswimConstants.SwimSpeedMps * 0.5f);
            float dist = Vector3.Distance(pos, tgtPos);
            float t = Mathf.Clamp(dist / Mathf.Max(speed, 1f), 0f, CrosswimConstants.InterceptLeadMaxS);
            lead = tgtVel * t;
            return tgtPos + lead;
        }

        private static Unit? FindHostileUnderwaterMissile(Missile self)
        {
            Unit? best = null;
            float bestSq = CrosswimConstants.TorpedoScanRangeM * CrosswimConstants.TorpedoScanRangeM;
            Missile[] all = Object.FindObjectsOfType<Missile>();
            for (int i = 0; i < all.Length; i++)
            {
                Missile m = all[i];
                if (m == null || m == self || !IsHostile(self, m))
                    continue;
                if (m.transform.position.y > Datum.LocalSeaY - 0.5f)
                    continue;
                if (CrosswimBootstrap.IsOurMissile(m))
                    continue;
                float sq = (m.transform.position - self.transform.position).sqrMagnitude;
                if (sq >= bestSq)
                    continue;
                bestSq = sq;
                best = m;
            }
            return best;
        }

        private static Unit? FindHostileShip(Missile self)
        {
            Unit? best = null;
            float bestSq = CrosswimConstants.ShipScanRangeM * CrosswimConstants.ShipScanRangeM;
            Ship[] ships = Object.FindObjectsOfType<Ship>();
            for (int i = 0; i < ships.Length; i++)
            {
                Ship s = ships[i];
                if (s == null || !IsHostile(self, s))
                    continue;
                float sq = (s.transform.position - self.transform.position).sqrMagnitude;
                if (sq >= bestSq)
                    continue;
                bestSq = sq;
                best = s;
            }
            return best;
        }

        private static bool IsUseful(Missile self, Unit? u)
        {
            return u != null && !u.disabled && IsHostile(self, u);
        }

        private static bool IsHostile(Missile self, Unit other)
        {
            if (self == null || other == null || other.disabled)
                return false;
            if (self.NetworkHQ == null || other.NetworkHQ == null)
                return self.owner == null || other != self.owner;
            return self.NetworkHQ != other.NetworkHQ;
        }
    }
}
