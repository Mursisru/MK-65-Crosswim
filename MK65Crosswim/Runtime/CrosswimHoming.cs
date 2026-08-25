using System.Collections.Generic;
using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Target pick without FindObjectsOfType (that was 1 FPS during swim / early water).
    /// Scans UnitRegistry.allUnits on a throttle.
    /// </summary>
    internal static class CrosswimHoming
    {
        private const float RescanS = 0.35f;
        private static float _nextScan;
        private static Unit? _cachedTorpedo;
        private static Unit? _cachedShip;
        private static Missile? _cachedFor;

        internal static Unit? SelectTarget(Missile self, Unit? current)
        {
            if (self == null)
                return current;

            if (!ReferenceEquals(_cachedFor, self) || Time.time >= _nextScan)
            {
                _cachedFor = self;
                _nextScan = Time.time + RescanS;
                Scan(self, out _cachedTorpedo, out _cachedShip);
            }

            if (_cachedTorpedo != null && IsUseful(self, _cachedTorpedo))
                return _cachedTorpedo;
            if (IsUseful(self, current))
                return current;
            if (_cachedShip != null && IsUseful(self, _cachedShip))
                return _cachedShip;
            return null;
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

        private static void Scan(Missile self, out Unit? torpedo, out Unit? ship)
        {
            torpedo = null;
            ship = null;
            float bestTorpSq = CrosswimConstants.TorpedoScanRangeM * CrosswimConstants.TorpedoScanRangeM;
            float bestShipSq = CrosswimConstants.ShipScanRangeM * CrosswimConstants.ShipScanRangeM;
            float sea = Datum.LocalSeaY;
            Vector3 selfPos = self.transform.position;
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u == null || u == self || !IsHostile(self, u))
                    continue;

                float sq = (u.transform.position - selfPos).sqrMagnitude;
                if (u is Missile m)
                {
                    if (CrosswimBootstrap.IsOurMissile(m))
                        continue;
                    if (m.transform.position.y > sea - 0.5f)
                        continue;
                    if (sq >= bestTorpSq)
                        continue;
                    bestTorpSq = sq;
                    torpedo = m;
                    continue;
                }

                if (u is Ship)
                {
                    if (sq >= bestShipSq)
                        continue;
                    bestShipSq = sq;
                    ship = u;
                }
            }
        }

        private static bool IsUseful(Missile self, Unit? u) =>
            u != null && !u.disabled && IsHostile(self, u);

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
