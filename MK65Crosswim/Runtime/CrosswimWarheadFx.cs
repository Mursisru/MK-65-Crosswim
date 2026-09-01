using System.Reflection;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Light bomb/AGM FX only — never TBM Shockwave prefab (baked kt yield looks nuclear).
    /// Damage is CrosswimBlast; Shockwave components are stripped / forced to 30 kg.
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
        private static readonly FieldInfo? YieldKtField =
            typeof(Shockwave).GetField("yieldKilotons", BindingFlags.Instance | BindingFlags.NonPublic);

        private static GameObject? _air;
        private static GameObject? _armor;
        private static GameObject? _terrain;
        private static GameObject? _water;
        private static GameObject? _under;
        private static bool _captured;

        /// <summary>Set while Crosswim Warhead.Detonate runs — scale any Shockwave Start.</summary>
        internal static int FxGate;

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
                // Prefer small bomb/AGM — never BallisticMissile / TBM (Shockwave kt bake).
                int s = 0;
                if (k.Equals("bomb_250", System.StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("bomb_glide1", System.StringComparison.OrdinalIgnoreCase))
                    s = 100;
                else if (k.StartsWith("bomb_", System.StringComparison.OrdinalIgnoreCase))
                    s = 80;
                else if (k.StartsWith("AGM", System.StringComparison.OrdinalIgnoreCase))
                    s = 60;
                else if (k.StartsWith("AShM", System.StringComparison.OrdinalIgnoreCase))
                    s = 40;
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
                CrosswimPlugin.ModLog?.LogWarning("Crosswim warhead FX: no bomb/AGM donor.");
                return;
            }

            _air = StripShockwaveClone(AirEffectField?.GetValue(wh) as GameObject);
            _armor = StripShockwaveClone(ArmorEffectField?.GetValue(wh) as GameObject);
            _terrain = StripShockwaveClone(TerrainEffectField?.GetValue(wh) as GameObject);
            _water = StripShockwaveClone(WaterSurfaceEffectField?.GetValue(wh) as GameObject);
            _under = StripShockwaveClone(UnderwaterEffectField?.GetValue(wh) as GameObject);
            if (_under == null)
                _under = _water ?? _air;
            _captured = _air != null || _under != null;
            CrosswimPlugin.ModLog?.LogInfo(
                $"Crosswim warhead FX light score={best} air={(_air != null)} under={(_under != null)}");
        }

        private static GameObject? StripShockwaveClone(GameObject? src)
        {
            if (src == null)
                return null;
            GameObject go = Object.Instantiate(src);
            go.name = "CrosswimFx_" + src.name;
            go.SetActive(false);
            Object.DontDestroyOnLoad(go);
            Shockwave[] sw = go.GetComponentsInChildren<Shockwave>(true);
            for (int i = 0; i < sw.Length; i++)
            {
                if (sw[i] != null)
                    Object.DestroyImmediate(sw[i]);
            }
            return go;
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

        /// <summary>Force any leftover Shockwave to 30 kg TNT during our detonation window.</summary>
        internal static void ForceLightYield(Shockwave sw)
        {
            if (sw == null || FxGate <= 0 || YieldKtField == null)
                return;
            YieldKtField.SetValue(sw, CrosswimConstants.BlastYieldKg * 1e-6f);
        }
    }
}
