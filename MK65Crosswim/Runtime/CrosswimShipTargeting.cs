using System.Collections.Generic;
using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Wet missiles (inbound-free) first; else nearest hostile ship in engage perimeter.
    /// </summary>
    internal static class CrosswimShipTargeting
    {
        internal static Unit? PickWet(Ship self)
        {
            if (self == null)
                return null;

            Unit? best = null;
            float bestScore = float.MinValue;
            float rangeSq = CrosswimConstants.ShipEngageRangeM * CrosswimConstants.ShipEngageRangeM;
            Vector3 selfPos = self.transform.position;
            float sea = Datum.LocalSeaY;
            FactionHQ? hq = self.NetworkHQ;

            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u is not Missile m || m.disabled)
                    continue;
                if (CrosswimBootstrap.IsOurMissile(m))
                    continue;
                if (!IsHostile(self, m))
                    continue;
                if (m.transform.position.y > sea - 0.25f)
                    continue;
                float sq = (m.transform.position - selfPos).sqrMagnitude;
                if (sq > rangeSq)
                    continue;
                if (!CrosswimInbound.HasRoom(m))
                    continue;

                float dist = Mathf.Sqrt(sq);
                float score = TargetsFriendlyShip(self, hq, m) ? 50000f - dist : 20000f - dist;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }

            return best;
        }

        internal static Unit? PickShip(Ship self)
        {
            if (self == null)
                return null;

            Unit? best = null;
            float bestDist = float.MaxValue;
            float rangeSq = CrosswimConstants.ShipEngageRangeM * CrosswimConstants.ShipEngageRangeM;
            float minSq = CrosswimConstants.ShipShipMinRangeM * CrosswimConstants.ShipShipMinRangeM;
            Vector3 selfPos = self.transform.position;

            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u is not Ship s || s.disabled)
                    continue;
                if (!IsHostile(self, s))
                    continue;
                float sq = (s.transform.position - selfPos).sqrMagnitude;
                if (sq > rangeSq || sq < minSq)
                    continue;
                if (sq < bestDist)
                {
                    bestDist = sq;
                    best = s;
                }
            }

            return best;
        }

        private static bool TargetsFriendlyShip(Ship self, FactionHQ? hq, Missile m)
        {
            if (m == null || !m.targetID.IsValid)
                return false;
            if (!UnitRegistry.TryGetUnit(new PersistentID?(m.targetID), out Unit t) || t == null)
                return false;
            if (t.disabled || !(t is Ship))
                return false;
            if (ReferenceEquals(t, self))
                return true;
            return hq != null && t.NetworkHQ != null && t.NetworkHQ == hq;
        }

        private static bool IsHostile(Ship self, Unit other)
        {
            if (self == null || other == null || other.disabled)
                return false;
            if (self.NetworkHQ != null && other.NetworkHQ != null)
                return self.NetworkHQ != other.NetworkHQ;
            return other != self;
        }
    }
}
