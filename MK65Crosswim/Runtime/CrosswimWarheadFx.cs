using System.Reflection;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// AShM warhead FX is cruise-missile scale. Stamp AGM/bomb-class FX so 70 kg looks right.
    /// </summary>
    internal static class CrosswimWarheadFx
    {
        private static readonly FieldInfo? WarheadField =
            typeof(Missile).GetField("warhead", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? AirEffectField =
            typeof(Missile.Warhead).GetField("airEffect", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? ArmorEffectField =
            typeof(Missile.Warhead).GetField("armorEffect", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? TerrainEffectField =
            typeof(Missile.Warhead).GetField("terrainEffect", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? WaterSurfaceEffectField =
            typeof(Missile.Warhead).GetField("waterSurfaceEffect", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? UnderwaterEffectField =
            typeof(Missile.Warhead).GetField("underwaterEffect", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? BlastYieldField =
            typeof(Missile).GetField("blastYield", BindingFlags.Instance | BindingFlags.NonPublic);

        private static GameObject? _air;
        private static GameObject? _armor;
        private static GameObject? _terrain;
        private static GameObject? _water;
        private static GameObject? _under;
        private static bool _captured;

        internal static void Capture(Encyclopedia enc)
        {
            if (_captured || enc?.missiles == null)
                return;

            Missile? donor = null;
            int best = -1;
            for (int i = 0; i < enc.missiles.Count; i++)
            {
                MissileDefinition? def = enc.missiles[i];
                if (def?.unitPrefab == null || string.IsNullOrEmpty(def.jsonKey))
                    continue;
                string k = def.jsonKey;
                int s = Score(k);
                if (s <= best)
                    continue;
                Missile? m = def.unitPrefab.GetComponent<Missile>()
                             ?? def.unitPrefab.GetComponentInChildren<Missile>(true);
                if (m == null)
                    continue;
                best = s;
                donor = m;
            }

            if (donor == null || WarheadField?.GetValue(donor) is not Missile.Warhead wh)
            {
                CrosswimPlugin.ModLog?.LogWarning("Crosswim warhead FX: no AGM/bomb donor.");
                return;
            }

            _air = AirEffectField?.GetValue(wh) as GameObject;
            _armor = ArmorEffectField?.GetValue(wh) as GameObject;
            _terrain = TerrainEffectField?.GetValue(wh) as GameObject;
            _water = WaterSurfaceEffectField?.GetValue(wh) as GameObject;
            _under = UnderwaterEffectField?.GetValue(wh) as GameObject;
            // Prefer air Shockwave for underwater path when UW FX missing/weak.
            if (_under == null)
                _under = _air;
            _captured = _air != null || _under != null;
            CrosswimPlugin.ModLog?.LogInfo(
                $"Crosswim warhead FX donor score={best} air={(_air != null)} under={(_under != null)}");
        }

        internal static void Ensure(Missile missile)
        {
            if (missile == null)
                return;
            BlastYieldField?.SetValue(missile, CrosswimConstants.BlastYieldKg);
            if (!_captured || WarheadField == null)
                return;
            if (WarheadField.GetValue(missile) is not Missile.Warhead wh)
                return;
            if (_air != null)
                AirEffectField?.SetValue(wh, _air);
            if (_armor != null)
                ArmorEffectField?.SetValue(wh, _armor);
            if (_terrain != null)
                TerrainEffectField?.SetValue(wh, _terrain);
            if (_water != null)
                WaterSurfaceEffectField?.SetValue(wh, _water);
            if (_under != null)
                UnderwaterEffectField?.SetValue(wh, _under);
        }

        private static int Score(string k)
        {
            if (k.StartsWith("AGM", System.StringComparison.OrdinalIgnoreCase))
                return 100;
            if (k.IndexOf("bomb", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return 80;
            if (k.StartsWith("AAM", System.StringComparison.OrdinalIgnoreCase))
                return 40;
            return 0;
        }
    }
}
