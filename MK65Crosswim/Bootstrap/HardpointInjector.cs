using System;
using System.Collections.Generic;
using UnityEngine;

namespace Crosswim.Bootstrap
{
    internal static class HardpointInjector
    {
        internal static void InjectAshmSlots(Encyclopedia enc, WeaponMount mount)
        {
            if (enc == null || mount == null)
                return;
            if (IsAshmKey(mount.jsonKey))
            {
                CrosswimPlugin.ModLog?.LogError("Refusing inject: Crosswim mount still has AShM jsonKey.");
                return;
            }

            int injected = 0;
            if (enc.aircraft == null)
                return;
            foreach (AircraftDefinition ad in enc.aircraft)
            {
                if (ad?.unitPrefab == null)
                    continue;
                injected += InjectOnPrefab(ad.unitPrefab, mount);
            }
            CrosswimPlugin.ModLog?.LogInfo($"HardpointInjector: added Crosswim to {injected} AShM hardpoint set(s).");
        }

        private static int InjectOnPrefab(GameObject aircraftPrefab, WeaponMount mount)
        {
            int count = 0;
            WeaponManager[] managers = aircraftPrefab.GetComponentsInChildren<WeaponManager>(true);
            foreach (WeaponManager wm in managers)
            {
                if (wm?.hardpointSets == null)
                    continue;
                foreach (HardpointSet set in wm.hardpointSets)
                {
                    if (set == null)
                        continue;
                    set.weaponOptions ??= new List<WeaponMount>();
                    if (!HasAshmOption(set.weaponOptions))
                        continue;
                    if (ContainsRef(set.weaponOptions, mount))
                        continue;
                    set.weaponOptions.Add(mount);
                    count++;
                }
            }
            return count;
        }

        private static bool HasAshmOption(List<WeaponMount> options)
        {
            foreach (WeaponMount o in options)
            {
                if (IsAshmKey(o?.jsonKey))
                    return true;
            }
            return false;
        }

        internal static bool IsAshmKey(string? jsonKey)
        {
            if (string.IsNullOrEmpty(jsonKey))
                return false;
            return jsonKey!.StartsWith(CrosswimConstants.AshmPrefix1, StringComparison.OrdinalIgnoreCase) ||
                   jsonKey.StartsWith(CrosswimConstants.AshmPrefix2, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsRef(List<WeaponMount> options, WeaponMount mount)
        {
            if (mount == null || string.IsNullOrEmpty(mount.jsonKey))
                return false;
            for (int i = 0; i < options.Count; i++)
            {
                if (ReferenceEquals(options[i], mount))
                    return true;
                if (options[i] != null && string.Equals(options[i].jsonKey, mount.jsonKey, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
