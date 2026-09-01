using Crosswim.Bootstrap;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>Submerged Crosswim: radar-invisible, sonar-only — same model as MK-88 Hydra.</summary>
    internal static class CrosswimStealth
    {
        internal static bool IsSubmerged(Missile? missile)
        {
            if (missile == null)
                return false;

            CrosswimFlight? flight = missile.GetComponent<CrosswimFlight>();
            if (flight != null && flight.Phase == CrosswimPhase.Swim)
                return true;

            return missile.transform.position.y < Datum.LocalSeaY - 0.25f;
        }

        internal static void OnSubmerged(Missile missile)
        {
            if (missile == null)
                return;
            missile.RCS = 0f;
            missile.radarAlt = -CrosswimConstants.SwimDepthM;
        }

        internal static void EnsureAirRadarSignature(Missile missile)
        {
            if (missile == null || IsSubmerged(missile))
                return;
            missile.RCS = CrosswimConstants.RadarSize;
        }

        internal static void Tick(Missile missile)
        {
            if (!CrosswimBootstrap.IsOurMissile(missile))
                return;

            if (IsSubmerged(missile))
            {
                missile.RCS = 0f;
                missile.radarAlt = Mathf.Min(missile.radarAlt, -1f);
                TickFriendlyTrack(missile);
                return;
            }

            missile.RCS = CrosswimConstants.RadarSize;
        }

        internal static void TickFriendlyTrack(Missile missile)
        {
            if (missile == null || !IsSubmerged(missile))
                return;

            FactionHQ? hq = missile.NetworkHQ;
            if (hq == null)
                return;

            hq.RpcUpdateTrackingInfo(missile.persistentID);
        }

        /// <summary>0..1 acoustic strength for ship sonar at distance.</summary>
        internal static float GetAcousticSignal(Missile missile, float distM, float maxRangeM)
        {
            if (!IsSubmerged(missile) || maxRangeM <= 1f)
                return 0f;

            float norm = Mathf.Clamp01(1f - distM / maxRangeM);
            float body = CrosswimConstants.RadarSize / 0.45f;
            return norm * norm * Mathf.Max(body, 0.35f) * CrosswimConstants.SonarTargetStrength;
        }
    }
}
