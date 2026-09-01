using HarmonyLib;
using Crosswim.Bootstrap;
using Crosswim.Runtime;
using UnityEngine;

namespace Crosswim.Patches
{
    /// <summary>Submerged Crosswim: no enemy radar; friendly track + ship sonar detect.</summary>
    [HarmonyPatch(typeof(Missile), nameof(Missile.GetRadarReturn))]
    internal static class MissileGetRadarReturnCrosswimStealthPatch
    {
        private static void Postfix(Missile __instance, ref float __result)
        {
            if (!CrosswimBootstrap.IsOurMissile(__instance))
                return;
            if (CrosswimStealth.IsSubmerged(__instance))
                __result = 0f;
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.UpdateRadarAlt))]
    internal static class MissileUpdateRadarAltCrosswimStealthPatch
    {
        private static void Postfix(Missile __instance) =>
            CrosswimStealth.Tick(__instance);
    }

    [HarmonyPatch(typeof(Turret), "Turret_OnDetectTarget")]
    internal static class TurretDetectTargetCrosswimStealthPatch
    {
        private static bool Prefix(Unit unit)
        {
            if (unit is Missile missile &&
                CrosswimBootstrap.IsOurMissile(missile) &&
                CrosswimStealth.IsSubmerged(missile))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.InitializeUnit))]
    internal static class UnitInitializeCrosswimShipSonarPatch
    {
        private static void Postfix(Unit __instance)
        {
            if (__instance is not Ship ship)
                return;
            CrosswimShipSonar.AttachIfNeeded(ship);
        }
    }
}
