using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>Hull sonar — detects submerged Crosswim (RpcUpdateTrackingInfo), same as MK-88 HydraShipSonar.</summary>
    internal sealed class CrosswimShipSonar : MonoBehaviour
    {
        private Ship? _ship;
        private float _rangeM;
        private float _nextScan;

        internal static void AttachIfNeeded(Ship ship)
        {
            if (ship == null || ship.GetComponent<CrosswimShipSonar>() != null)
                return;
            if (CrosswimSonarRegistry.IsExcluded(ship.definition))
                return;

            CrosswimShipSonar sonar = ship.gameObject.AddComponent<CrosswimShipSonar>();
            sonar.Init(ship);
        }

        private void Init(Ship ship)
        {
            _ship = ship;
            _rangeM = CrosswimSonarRegistry.ComputeRangeM(ship.definition);
            CrosswimPlugin.ModLog?.LogInfo(
                $"Crosswim sonar on '{ship.definition?.unitName ?? ship.name}' range={_rangeM / 1000f:F1}km");
        }

        private void Update()
        {
            if (_ship == null || _ship.disabled || !_ship.IsServer)
                return;
            if (Time.time < _nextScan)
                return;

            _nextScan = Time.time + CrosswimConstants.SonarScanIntervalS;
            Scan();
        }

        private void Scan()
        {
            if (_ship == null)
                return;

            FactionHQ? hq = _ship.NetworkHQ;
            if (hq == null)
                return;

            Vector3 scanPos = _ship.transform.position;
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (u == null || u.disabled || u.NetworkHQ == hq)
                    continue;
                if (u is not Missile missile || !CrosswimBootstrap.IsOurMissile(missile))
                    continue;
                if (!CrosswimStealth.IsSubmerged(missile))
                    continue;

                float dist = Vector3.Distance(scanPos, missile.transform.position);
                if (dist > _rangeM)
                    continue;

                float signal = CrosswimStealth.GetAcousticSignal(missile, dist, _rangeM);
                if (signal < CrosswimConstants.SonarMinSignal)
                    continue;

                hq.RpcUpdateTrackingInfo(missile.persistentID);
            }
        }
    }
}
