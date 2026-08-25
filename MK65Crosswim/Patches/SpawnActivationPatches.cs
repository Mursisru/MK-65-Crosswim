using HarmonyLib;
using Crosswim.Bootstrap;
using Crosswim.Runtime;
using UnityEngine;

namespace Crosswim.Patches
{
    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.SpawnMount))]
    internal static class HardpointSpawnMountPatch
    {
        private static void Prefix(WeaponMount weaponMount)
        {
            if (!IsOurs(weaponMount) || weaponMount.prefab == null)
                return;
            WeaponInfo? shared = CrosswimBootstrap.Info ?? weaponMount.info;
            if (shared != null)
            {
                weaponMount.info = shared;
                weaponMount.sortWeapons = true;
                foreach (MountedMissile mm in weaponMount.prefab.GetComponentsInChildren<MountedMissile>(true))
                {
                    if (mm != null)
                        mm.info = shared;
                }
            }
            PrefabFactory.FreezeTemplatePhysics(weaponMount.prefab);
            weaponMount.prefab.SetActive(true);
        }

        private static void Postfix(Hardpoint __instance, WeaponMount weaponMount, GameObject __result)
        {
            if (!IsOurs(weaponMount))
                return;
            if (weaponMount.prefab != null)
            {
                PrefabFactory.FreezeTemplatePhysics(weaponMount.prefab);
                weaponMount.prefab.SetActive(false);
            }
            if (__result == null)
                return;
            bool bay = __instance != null && __instance.bayDoors != null && __instance.bayDoors.Length > 0;
            PrefabFactory.ActivateMountedInstance(__result, bay);
        }

        private static bool IsOurs(WeaponMount? weaponMount)
        {
            return weaponMount != null &&
                   string.Equals(weaponMount.jsonKey, CrosswimConstants.MountJsonKey, System.StringComparison.Ordinal);
        }
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnMissile), new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
    internal static class SpawnerSpawnMissileGoPatch
    {
        private static void Prefix(GameObject missile, out bool __state)
        {
            if (CrosswimSpawnGate.IsOurFlyPrefab(missile) && CrosswimSpawnGate.Pending > 0)
                CrosswimSpawnGate.BeginPrefabStamp(missile);
            __state = CrosswimSpawnGate.TryBegin();
        }

        private static void Postfix(bool __state, Unit target, Missile __result)
        {
            try
            {
                CrosswimSpawnGate.EndPrefabStamp();
                if (!__state || __result == null)
                    return;
                CrosswimSpawnGate.Claim(__result, target);
                CrosswimSpawnGate.FinishVisual(__result);
            }
            finally
            {
                CrosswimSpawnGate.EndPrefabStamp();
            }
        }
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnMissile), new[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
    internal static class SpawnerSpawnMissileDefPatch
    {
        private static void Prefix(MissileDefinition missile, out bool __state)
        {
            __state = missile != null &&
                      string.Equals(missile.jsonKey, CrosswimConstants.MissileJsonKey, System.StringComparison.Ordinal);
            if (__state)
                CrosswimSpawnGate.BeginPrefabStamp(missile!.unitPrefab);
        }

        private static void Postfix(bool __state, Unit target, Missile __result)
        {
            try
            {
                CrosswimSpawnGate.EndPrefabStamp();
                if (!__state || __result == null)
                    return;
                CrosswimSpawnGate.Claim(__result, target);
                CrosswimSpawnGate.FinishVisual(__result);
            }
            finally
            {
                CrosswimSpawnGate.EndPrefabStamp();
            }
        }
    }

    [HarmonyPatch(typeof(Spawner), nameof(Spawner.SpawnMissileEncyclopedia))]
    internal static class SpawnerSpawnMissileEncyclopediaPatch
    {
        private static void Prefix(MissileDefinition missile, out bool __state)
        {
            __state = missile != null &&
                      string.Equals(missile.jsonKey, CrosswimConstants.MissileJsonKey, System.StringComparison.Ordinal);
            if (__state)
                CrosswimSpawnGate.BeginPrefabStamp(missile!.unitPrefab);
        }

        private static void Postfix(bool __state, Missile __result)
        {
            try
            {
                CrosswimSpawnGate.EndPrefabStamp();
                if (!__state || __result == null)
                    return;
                CrosswimSpawnGate.Claim(__result);
                CrosswimSpawnGate.FinishVisual(__result);
            }
            finally
            {
                CrosswimSpawnGate.EndPrefabStamp();
            }
        }
    }

    [HarmonyPatch(typeof(MountedMissile), nameof(MountedMissile.Fire))]
    internal static class MountedMissileFirePatch
    {
        private static void Prefix(MountedMissile __instance)
        {
            if (__instance?.info == null)
                return;
            if (__instance.info.weaponName != CrosswimConstants.WeaponInfoName &&
                __instance.info.shortName != CrosswimConstants.ShortName)
                return;
            if (CrosswimBootstrap.Definition?.unitPrefab != null)
                __instance.info.weaponPrefab = CrosswimBootstrap.Definition.unitPrefab;
            CrosswimSpawnGate.NoteFire();
        }
    }

    [HarmonyPatch(typeof(MissileLauncher), nameof(MissileLauncher.Fire))]
    internal static class MissileLauncherFirePatch
    {
        private static void Prefix(MissileLauncher __instance)
        {
            if (__instance == null || CrosswimBootstrap.Definition == null)
                return;
            if (__instance.missile != CrosswimBootstrap.Definition)
                return;
            CrosswimSpawnGate.NoteFire();
        }
    }
}
