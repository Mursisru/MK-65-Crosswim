using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Snapshot of vanilla AShM-300 VLSBooster + air turn limits (Missile.maxTurnRate / gLimit).
    /// </summary>
    internal static class CrosswimAshmVls
    {
        private static readonly FieldInfo? ThrustField = AccessTools.Field(typeof(VLSBooster), "thrust");
        private static readonly FieldInfo? BurnTimeField = AccessTools.Field(typeof(VLSBooster), "burnTime");
        private static readonly FieldInfo? FuelMassField = AccessTools.Field(typeof(VLSBooster), "fuelMass");
        private static readonly FieldInfo? DryMassField = AccessTools.Field(typeof(VLSBooster), "dryMass");
        private static readonly FieldInfo? MaxTurnField = AccessTools.Field(typeof(Missile), "maxTurnRate");
        private static readonly FieldInfo? GLimitField = AccessTools.Field(typeof(Missile), "gLimit");

        // Fallbacks if AShM prefab missing fields.
        internal static float BurnTimeS = 8f;
        internal static float ThrustN = 28000f;
        internal static float FuelMassKg = 180f;
        internal static float DryMassKg = 220f;
        // Missile.ApplyAero turn envelope (°/s + g).
        internal static float MaxTurnRateDegS = 45f;
        internal static float GLimit = 8f;
        internal static bool Captured;

        internal static float LaunchExtraMassKg => FuelMassKg + DryMassKg;

        // Soften AShM booster thrust — Crosswim stack is lighter / no cruise motor load.
        internal static float EffectiveThrustN => ThrustN * CrosswimConstants.VlsbThrustScale;

        internal static void Capture(Encyclopedia enc)
        {
            if (enc?.missiles == null)
                return;

            for (int i = 0; i < enc.missiles.Count; i++)
            {
                MissileDefinition? def = enc.missiles[i];
                if (def?.unitPrefab == null)
                    continue;
                string key = def.jsonKey ?? string.Empty;
                string name = def.unitName ?? string.Empty;
                if (!key.StartsWith("AShM", System.StringComparison.OrdinalIgnoreCase) &&
                    name.IndexOf("AShM", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                VLSBooster? booster = def.unitPrefab.GetComponentInChildren<VLSBooster>(true);
                if (booster == null)
                    continue;

                float burn = BurnTimeField != null ? (float)BurnTimeField.GetValue(booster)! : 0f;
                float thrust = ThrustField != null ? (float)ThrustField.GetValue(booster)! : 0f;
                float fuel = FuelMassField != null ? (float)FuelMassField.GetValue(booster)! : 0f;
                float dry = DryMassField != null ? (float)DryMassField.GetValue(booster)! : 0f;

                if (burn > 0.5f)
                    BurnTimeS = burn;
                if (thrust > 100f)
                    ThrustN = thrust;
                if (fuel > 1f)
                    FuelMassKg = fuel;
                if (dry > 1f)
                    DryMassKg = dry;

                Missile? body = def.unitPrefab.GetComponent<Missile>();
                if (body == null)
                    body = def.unitPrefab.GetComponentInChildren<Missile>(true);
                if (body != null)
                {
                    float turn = MaxTurnField != null ? (float)MaxTurnField.GetValue(body)! : 0f;
                    float g = GLimitField != null ? (float)GLimitField.GetValue(body)! : 0f;
                    if (turn > 1f)
                        MaxTurnRateDegS = turn;
                    if (g > 0.5f)
                        GLimit = g;
                }

                Captured = true;
                CrosswimPlugin.ModLog?.LogInfo(
                    $"CrosswimAshmVls from '{key}': burn={BurnTimeS:F2}s thrust={ThrustN:F0}N " +
                    $"(×{CrosswimConstants.VlsbThrustScale:F2}→{EffectiveThrustN:F0}) " +
                    $"fuel={FuelMassKg:F0} dry={DryMassKg:F0} turn={MaxTurnRateDegS:F0}°/s g={GLimit:F1}");
                return;
            }

            CrosswimPlugin.ModLog?.LogWarning(
                $"CrosswimAshmVls: no AShM VLSBooster — fallback burn={BurnTimeS:F1}s thrust={ThrustN:F0}N");
        }
    }
}
