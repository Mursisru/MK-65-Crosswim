using System.Reflection;
using HarmonyLib;
using Crosswim.Bootstrap;
using Crosswim.Runtime;
using UnityEngine;

namespace Crosswim.Patches
{
    [HarmonyPatch(typeof(Missile), nameof(Missile.GetYield))]
    internal static class MissileGetYieldPatch
    {
        private static void Postfix(Missile __instance, ref float __result)
        {
            if (CrosswimBootstrap.IsOurMissile(__instance))
                __result = CrosswimConstants.BlastYieldKg;
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.GetSeekerType))]
    internal static class MissileGetSeekerTypePatch
    {
        private static bool Prefix(Missile __instance, ref string __result)
        {
            if (!CrosswimBootstrap.IsOurMissile(__instance))
                return true;
            __result = CrosswimConstants.SeekerTypeName;
            return false;
        }
    }

    [HarmonyPatch(typeof(MissileDefinition), nameof(MissileDefinition.GetMass))]
    internal static class MissileDefinitionGetMassPatch
    {
        private static void Postfix(MissileDefinition __instance, ref float __result)
        {
            if (__instance != null &&
                string.Equals(__instance.jsonKey, CrosswimConstants.MissileJsonKey, System.StringComparison.Ordinal))
                __result = CrosswimConstants.MassKg;
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.GetMass))]
    internal static class MissileGetMassPatch
    {
        private static void Postfix(Missile __instance, ref float __result)
        {
            if (CrosswimBootstrap.IsOurMissile(__instance))
                __result = CrosswimConstants.MassKg;
        }
    }

    [HarmonyPatch(typeof(UnitRegistry), nameof(UnitRegistry.RegisterUnit))]
    internal static class CrosswimPersistentIdentityPatch
    {
        private static void Postfix(Unit unit)
        {
            if (unit is Missile missile && CrosswimBootstrap.IsOurMissile(missile))
                CrosswimSpawnGate.ApplyDisplayIdentity(missile);
        }
    }

    [HarmonyPatch(typeof(WeaponMount), nameof(WeaponMount.Initialize))]
    internal static class WeaponMountInitializeCrosswimPatch
    {
        private static void Postfix(WeaponMount __instance)
        {
            if (__instance == null ||
                !string.Equals(__instance.jsonKey, CrosswimConstants.MountJsonKey, System.StringComparison.Ordinal))
                return;
            WeaponInfo? info = CrosswimBootstrap.Info ?? __instance.info;
            if (info == null)
                return;
            __instance.info = info;
            __instance.sortWeapons = true;
            info.weaponName = CrosswimConstants.WeaponInfoName;
            info.shortName = CrosswimConstants.ShortName;
            Sprite? preview = CrosswimWeaponIcon.Get();
            if (preview != null)
                info.weaponIcon = preview;
            info.massPerRound = CrosswimConstants.MassKg;
            info.blastDamage = CrosswimConstants.BlastYieldKg;
            info.fireInterval = CrosswimConstants.FireIntervalS;
            info.costPerRound = CrosswimConstants.Cost;
            info.gravMult = 1f;
            info.missile = false;
            info.bomb = false;
            info.glideBomb = false;
            if (CrosswimBootstrap.Definition?.unitPrefab != null)
                info.weaponPrefab = CrosswimBootstrap.Definition.unitPrefab;
            __instance.mountName = CrosswimConstants.MountDisplayName;
        }
    }

    [HarmonyPatch(typeof(AircraftSelectionMenu), nameof(AircraftSelectionMenu.DisplayInfo))]
    internal static class AircraftSelectionCrosswimDisplayPatch
    {
        private static void Postfix(AircraftSelectionMenu __instance, WeaponInfo weaponInfo)
        {
            if (weaponInfo == null ||
                (weaponInfo.weaponName != CrosswimConstants.WeaponInfoName &&
                 weaponInfo.shortName != CrosswimConstants.ShortName))
                return;
            weaponInfo.costPerRound = CrosswimConstants.Cost;
            weaponInfo.blastDamage = CrosswimConstants.BlastYieldKg;
            weaponInfo.massPerRound = CrosswimConstants.MassKg;
            SetTmp(__instance, "weaponSeeker", CrosswimConstants.SeekerTypeName);
        }

        internal static void SetTmp(object host, string field, string value)
        {
            FieldInfo? f = host.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            object? tmp = f?.GetValue(host);
            tmp?.GetType().GetProperty("text")?.SetValue(tmp, value);
        }
    }
}
