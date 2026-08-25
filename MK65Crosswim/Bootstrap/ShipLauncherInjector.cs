using System;
using System.Reflection;
using UnityEngine;

namespace Crosswim.Bootstrap
{
    /// <summary>Add-only clone of a Dynamo/Argus MissileLauncher pointing at Crosswim. Does not mutate donor missile SO.</summary>
    internal static class ShipLauncherInjector
    {
        private static readonly FieldInfo? MissileField =
            typeof(MissileLauncher).GetField("missile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static void InjectDynamoArgus(Encyclopedia enc, MissileDefinition? ours)
        {
            if (enc?.ships == null || ours == null)
                return;

            int added = 0;
            foreach (ShipDefinition ship in enc.ships)
            {
                if (ship?.unitPrefab == null || !IsTargetShip(ship))
                    continue;
                added += InjectOnShipPrefab(ship.unitPrefab, ours, ship.unitName ?? ship.jsonKey);
            }
            CrosswimPlugin.ModLog?.LogInfo($"ShipLauncherInjector: cloned {added} launcher(s) on Dynamo/Argus.");
        }

        private static bool IsTargetShip(ShipDefinition ship)
        {
            return NameHasToken(ship.unitName) || NameHasToken(ship.jsonKey) || NameHasToken(ship.unitPrefab != null ? ship.unitPrefab.name : null);
        }

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

        private static int InjectOnShipPrefab(GameObject prefab, MissileDefinition ours, string shipTag)
        {
            MissileLauncher[] launchers = prefab.GetComponentsInChildren<MissileLauncher>(true);
            int added = 0;
            for (int i = 0; i < launchers.Length; i++)
            {
                MissileLauncher src = launchers[i];
                if (src == null)
                    continue;
                if (src.GetComponent<CrosswimLauncherTag>() != null)
                    continue;

                GameObject cloneGo = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
                cloneGo.name = src.gameObject.name + "_MK65";
                CrosswimLauncherTag tag = cloneGo.AddComponent<CrosswimLauncherTag>();
                tag.hideFlags = HideFlags.None;

                MissileLauncher clone = cloneGo.GetComponent<MissileLauncher>();
                if (clone == null)
                {
                    UnityEngine.Object.DestroyImmediate(cloneGo);
                    continue;
                }
                if (MissileField != null)
                    MissileField.SetValue(clone, ours);
                else
                    clone.missile = ours;
                added++;
                CrosswimPlugin.ModLog?.LogInfo($"Ship launcher clone on '{shipTag}' from '{src.gameObject.name}'.");
            }
            return added;
        }
    }

    internal sealed class CrosswimLauncherTag : MonoBehaviour
    {
    }
}
