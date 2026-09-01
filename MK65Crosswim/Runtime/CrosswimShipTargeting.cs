using System.Collections.Generic;
using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Wet / torpedo threats (inbound-free) first; else nearest hostile ship in engage perimeter.
    /// </summary>
    internal static class CrosswimShipTargeting
    {
        private static readonly string[] TorpedoKeyTokens =
        {
            "torpedo", "mk54", "mk88", "hydra", "mk65", "crosswim"
        };

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
                if (!IsTorpedoThreat(m, self, hq, sea))
                    continue;
                float sq = (m.transform.position - selfPos).sqrMagnitude;
                if (sq > rangeSq)
                    continue;
                if (!CrosswimInbound.HasRoom(m))
                    continue;

                float dist = Mathf.Sqrt(sq);
                bool inboundFriendly = TargetsFriendlyShip(self, hq, m);
                bool wet = m.transform.position.y < sea - 0.1f;
                float score = (inboundFriendly ? 50000f : 20000f) + (wet ? 10000f : 0f) - dist;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }

            return best;
        }

        /// <summary>
        /// Submerged missiles, Sonar-seeker / known torpedo keys, or airborne inbound on friendlies.
        /// </summary>
        internal static bool IsTorpedoThreat(Missile m, Ship self, FactionHQ? hq, float sea)
        {
            if (m == null)
                return false;

            float y = m.transform.position.y;
            if (y < sea - 0.1f)
                return true;

            string seeker = m.GetSeekerType() ?? string.Empty;
            bool sonarLike = seeker.IndexOf("Sonar", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool keyTorp = KeyLooksTorpedo(m);
            if (!sonarLike && !keyTorp)
                return false;

            // Air / surface torpedo — only if inbound on us or friendly ships.
            if (y < sea + 120f && TargetsFriendlyShip(self, hq, m))
                return true;

            // Any sonar-keyed hostile in perimeter (even without lock yet).
            return sonarLike || keyTorp;
        }

        private static bool KeyLooksTorpedo(Missile m)
        {
            string key = m.definition != null ? (m.definition.jsonKey ?? string.Empty) : string.Empty;
            string name = m.definition != null ? (m.definition.unitName ?? string.Empty) : string.Empty;
            for (int i = 0; i < TorpedoKeyTokens.Length; i++)
            {
                string tok = TorpedoKeyTokens[i];
                if (key.IndexOf(tok, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf(tok, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
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
