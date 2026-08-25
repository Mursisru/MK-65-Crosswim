using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Crosswim.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Crosswim.Patches
{
    /// <summary>
    /// bomb=false → vanilla MissileUI. Extra BombingUI = CCIP.
    /// HUD range via WeaponInfo (shared AShM prefab must not drive CalcRange).
    /// </summary>
    internal static class CrosswimHudDual
    {
        private static readonly FieldInfo? BombingUiField =
            AccessTools.Field(typeof(CombatHUD), "BombingUI");
        private static readonly FieldInfo? TargetDesignatorField =
            AccessTools.Field(typeof(CombatHUD), "targetDesignator");
        private static readonly FieldInfo? AircraftField =
            AccessTools.Field(typeof(CombatHUD), "aircraft");

        private static HUDBombingState? _ccip;

        internal static void ClearCcip()
        {
            if (_ccip == null)
                return;
            Object.Destroy(_ccip.gameObject);
            _ccip = null;
        }

        internal static void TryAttachCcip(CombatHUD hud, WeaponStation? station)
        {
            ClearCcip();
            if (hud == null || station == null || !CrosswimBootstrap.IsOurInfo(station.WeaponInfo))
                return;
            if (BombingUiField?.GetValue(hud) is not GameObject prefab || prefab == null)
                return;
            if (TargetDesignatorField?.GetValue(hud) is not Image designator)
                return;
            if (AircraftField?.GetValue(hud) is not Aircraft ac || ac == null)
                return;
            if (SceneSingleton<FlightHud>.i == null)
                return;

            GameObject go = Object.Instantiate(prefab, SceneSingleton<FlightHud>.i.GetHUDCenter());
            _ccip = go.GetComponent<HUDBombingState>();
            if (_ccip == null)
            {
                Object.Destroy(go);
                return;
            }
            _ccip.SetHUDWeaponState(designator, ac, station);
        }

        internal static void TickCcip(CombatHUD hud)
        {
            if (_ccip == null || hud == null)
                return;
            if (AircraftField?.GetValue(hud) is not Aircraft ac || ac == null)
                return;
            List<Unit> list = ac.weaponManager != null
                ? ac.weaponManager.GetTargetList()
                : new List<Unit>();
            _ccip.UpdateWeaponDisplay(ac, list);
        }
    }

    /// <summary>
    /// Swim-only MAX on vanilla ladder — ignore AShM CalcRange on shared unitPrefab.
    /// </summary>
    internal static class CrosswimHudRange
    {
        private static readonly FieldInfo? WeaponInfoField =
            AccessTools.Field(typeof(HUDWeaponState), "weaponInfo");
        private static readonly FieldInfo? AircraftField =
            AccessTools.Field(typeof(HUDMissileState), "aircraft");
        private static readonly FieldInfo? FarTargetField =
            AccessTools.Field(typeof(HUDMissileState), "farTarget");
        private static readonly FieldInfo? CloseTargetField =
            AccessTools.Field(typeof(HUDMissileState), "closeTarget");
        private static readonly FieldInfo? MaxTargetDistField =
            AccessTools.Field(typeof(HUDMissileState), "maxTargetDist");
        private static readonly FieldInfo? MinTargetDistField =
            AccessTools.Field(typeof(HUDMissileState), "minTargetDist");
        private static readonly FieldInfo? MaxTargetSpeedField =
            AccessTools.Field(typeof(HUDMissileState), "maxTargetSpeed");
        private static readonly FieldInfo? MaxTargetAngleField =
            AccessTools.Field(typeof(HUDMissileState), "maxTargetAngle");
        private static readonly FieldInfo? MaxRangeField =
            AccessTools.Field(typeof(HUDMissileState), "maxRange");
        private static readonly FieldInfo? NoEscapeField =
            AccessTools.Field(typeof(HUDMissileState), "noEscapeRange");
        private static readonly FieldInfo? LastCalcField =
            AccessTools.Field(typeof(HUDMissileState), "lastWeaponRangeCalc");
        private static readonly FieldInfo? TargetListField =
            AccessTools.Field(typeof(HUDMissileState), "targetList");
        private static readonly FieldInfo? StationField =
            AccessTools.Field(typeof(HUDMissileState), "weaponStation");

        internal static bool CalcWeaponRangePrefix(HUDMissileState hud)
        {
            if (WeaponInfoField?.GetValue(hud) is not WeaponInfo wi || !CrosswimBootstrap.IsOurInfo(wi))
                return true;

            if (TargetListField?.GetValue(hud) is not IList list || list.Count == 0)
                return false;
            if (StationField?.GetValue(hud) is WeaponStation st && st.Ammo == 0)
                return false;

            float last = LastCalcField?.GetValue(hud) is float l ? l : 0f;
            if (last > 0f && Time.timeSinceLevelLoad - last < 1f)
                return false;

            if (AircraftField?.GetValue(hud) is not Aircraft ac || ac == null)
                return true;

            ScanTargets(hud, ac, list);

            float swim = CrosswimConstants.SwimFuelRangeM;
            MaxRangeField?.SetValue(hud, swim);
            NoEscapeField?.SetValue(hud, swim);
            LastCalcField?.SetValue(hud, Time.timeSinceLevelLoad);
            return false;
        }

        private static void ScanTargets(HUDMissileState hud, Aircraft ac, IList list)
        {
            GlobalPosition acPos = ac.GlobalPosition();
            float farSq = 0f;
            float closeSq = float.MaxValue;
            Unit? far = null;
            Unit? close = null;
            float maxAngle = 0f;
            float maxSpd = 0f;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not Unit unit || unit == null)
                    continue;
                if (ac.NetworkHQ == null || !ac.NetworkHQ.TryGetKnownPosition(unit, out GlobalPosition gp))
                    continue;

                float sq = FastMath.SquareDistance(gp, acPos);
                if (sq > farSq)
                {
                    farSq = sq;
                    far = unit;
                }
                if (sq < closeSq)
                {
                    closeSq = sq;
                    close = unit;
                }
                maxAngle = Mathf.Max(maxAngle, Vector3.Angle(gp - acPos, ac.transform.forward));
                maxSpd = Mathf.Max(unit.speed, maxSpd);
            }

            FarTargetField?.SetValue(hud, far);
            CloseTargetField?.SetValue(hud, close);
            MaxTargetAngleField?.SetValue(hud, maxAngle);
            MaxTargetSpeedField?.SetValue(hud, maxSpd);

            float farDist = farSq > 0f ? Mathf.Sqrt(farSq) : 0f;
            MaxTargetDistField?.SetValue(hud, farDist);
            if (list.Count <= 1)
                MinTargetDistField?.SetValue(hud, farDist);
            else if (closeSq < float.MaxValue)
                MinTargetDistField?.SetValue(hud, Mathf.Sqrt(closeSq));
        }
    }

    [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.ShowWeaponStation))]
    internal static class CombatHudShowWeaponPatch
    {
        private static void Prefix() => CrosswimHudDual.ClearCcip();

        private static void Postfix(CombatHUD __instance, WeaponStation weaponStation)
        {
            CrosswimHudDual.TryAttachCcip(__instance, weaponStation);
        }
    }

    [HarmonyPatch(typeof(CombatHUD), "LateUpdate")]
    internal static class CombatHudLateUpdatePatch
    {
        private static void Postfix(CombatHUD __instance) =>
            CrosswimHudDual.TickCcip(__instance);
    }

    [HarmonyPatch(typeof(HUDMissileState), "CalcWeaponRange")]
    internal static class HudMissileCalcRangePatch
    {
        private static bool Prefix(HUDMissileState __instance) =>
            CrosswimHudRange.CalcWeaponRangePrefix(__instance);
    }

    [HarmonyPatch(typeof(HUDMissileState), nameof(HUDMissileState.SetHUDWeaponState))]
    internal static class HudMissileSetStatePatch
    {
        private static void Postfix(HUDMissileState __instance, WeaponStation weaponStation)
        {
            if (weaponStation == null || !CrosswimBootstrap.IsOurInfo(weaponStation.WeaponInfo))
                return;
            float swim = CrosswimConstants.SwimFuelRangeM;
            AccessTools.Field(typeof(HUDMissileState), "maxRange")?.SetValue(__instance, swim);
            AccessTools.Field(typeof(HUDMissileState), "noEscapeRange")?.SetValue(__instance, swim);
            AccessTools.Field(typeof(HUDMissileState), "minRange")?.SetValue(__instance, 0f);
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.CalcRange))]
    internal static class MissileCalcRangeSwimPatch
    {
        private static bool Prefix(Missile __instance, out float noEscapeDistance, ref float __result)
        {
            // Live Crosswim only — shared AShM HUD prefab stays vanilla.
            if (__instance == null || __instance.GetComponent<Crosswim.Runtime.CrosswimTag>() == null)
            {
                noEscapeDistance = 0f;
                return true;
            }
            __result = CrosswimConstants.SwimFuelRangeM;
            noEscapeDistance = CrosswimConstants.SwimFuelRangeM;
            return false;
        }
    }
}
