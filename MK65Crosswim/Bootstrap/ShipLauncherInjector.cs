using System;
using System.Collections.Generic;
using Crosswim.Runtime;
using HarmonyLib;
using UnityEngine;

namespace Crosswim.Bootstrap
{
    /// <summary>
    /// Dynamo/Argus: strip broken prefab VLS mutations, attach runtime defense only.
    /// Never rewrite Turret.weaponStations (that wiped vanilla loadout).
    /// </summary>
    internal static class ShipLauncherInjector
    {
        private static readonly System.Reflection.FieldInfo? TurretStationsField =
            AccessTools.Field(typeof(Turret), "weaponStations");

        internal static void InjectDynamoArgus(Encyclopedia enc, MissileDefinition? ours)
        {
            if (enc?.ships == null || ours == null || CrosswimBootstrap.Info == null)
                return;

            int ships = 0;
            foreach (ShipDefinition ship in enc.ships)
            {
                if (ship?.unitPrefab == null || !IsTargetShip(ship))
                    continue;
                RepairPrefab(ship.unitPrefab);
                if (ship.unitPrefab.GetComponent<CrosswimShipDefense>() == null)
                    ship.unitPrefab.AddComponent<CrosswimShipDefense>();
                ships++;
            }
            CrosswimPlugin.ModLog?.LogInfo(
                $"ShipLauncherInjector: runtime Crosswim VLS x{CrosswimConstants.ShipVlsAmmo} marked on {ships} Dynamo/Argus (turrets untouched).");
        }

        private static bool IsTargetShip(ShipDefinition ship) =>
            NameHasToken(ship.unitName) ||
            NameHasToken(ship.jsonKey) ||
            NameHasToken(ship.unitPrefab != null ? ship.unitPrefab.name : null);

        private static bool NameHasToken(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            for (int i = 0; i < CrosswimConstants.ShipNameTokens.Length; i++)
            {
                if (s!.IndexOf(CrosswimConstants.ShipNameTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>Remove our cloned launchers and any Crosswim stations we stuffed into Turret arrays.</summary>
        private static void RepairPrefab(GameObject prefab)
        {
            CrosswimLauncherTag[] tags = prefab.GetComponentsInChildren<CrosswimLauncherTag>(true);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] != null)
                    UnityEngine.Object.DestroyImmediate(tags[i].gameObject);
            }

            Transform[] all = prefab.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform t = all[i];
                if (t == null)
                    continue;
                if (t.name.IndexOf("MK65_Crosswim", StringComparison.OrdinalIgnoreCase) >= 0)
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            }

            Turret[] turrets = prefab.GetComponentsInChildren<Turret>(true);
            for (int t = 0; t < turrets.Length; t++)
            {
                Turret turret = turrets[t];
                if (turret == null || TurretStationsField == null)
                    continue;
                if (TurretStationsField.GetValue(turret) is not WeaponStation[] stations || stations.Length == 0)
                    continue;

                List<WeaponStation> keep = new List<WeaponStation>(stations.Length);
                bool removed = false;
                for (int i = 0; i < stations.Length; i++)
                {
                    WeaponStation? ws = stations[i];
                    if (ws != null && CrosswimBootstrap.IsOurInfo(ws.WeaponInfo))
                    {
                        removed = true;
                        continue;
                    }
                    if (ws != null)
                        keep.Add(ws);
                }
                if (removed)
                    TurretStationsField.SetValue(turret, keep.ToArray());
            }
        }
    }

    internal sealed class CrosswimLauncherTag : MonoBehaviour
    {
    }
}
