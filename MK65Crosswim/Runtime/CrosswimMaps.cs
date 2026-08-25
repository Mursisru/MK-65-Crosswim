using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Disk maps next to DLL (Textures/).
    /// GlossyMetal = UCUPaint Bake All Channels Color (GlossyMetal_Color.png).
    /// Mask (Solid Color)* are paint masks — never albedo.
    /// </summary>
    internal static class CrosswimMaps
    {
        private static readonly Dictionary<string, string> AlbedoFile =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Official UCUPaint Color bake from MK-65-Bake-All-Channels.
                { "GlossyMetal", "GlossyMetal_Color.png" },
                { "Metal", "Metal_Albedo.png" },
                // Solid slots: Principled tint only.
                { "Metal2", "" },
                { "LightMaterial", "" }
            };

        // Color bake already carries paint; AO as occlusion only if present (optional).
        private static readonly Dictionary<string, string> OcclusionFile =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
            };

        private static readonly Dictionary<string, string> RoughnessFile =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Metal", "Metal_Roughness.png" }
            };

        private static readonly Dictionary<string, string> NormalFile =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Metal", "Metal_Normal.png" }
            };

        private static readonly Dictionary<string, Texture2D> Cache =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private static bool _loggedRoot;

        internal static Texture2D? Albedo(string blenderMatName) =>
            LoadNamed(blenderMatName, AlbedoFile, linear: false, suffix: "_albedo");

        internal static Texture2D? Occlusion(string blenderMatName) =>
            LoadNamed(blenderMatName, OcclusionFile, linear: true, suffix: "_ao");

        internal static Texture2D? Normal(string blenderMatName)
        {
            Texture2D? tex = LoadNamed(blenderMatName, NormalFile, linear: true, suffix: "_nml");
            if (tex != null)
                PackNormalAg(tex);
            return tex;
        }

        internal static Texture2D? MetallicGloss(string blenderMatName, float metallic)
        {
            string key = StripMatSuffix(blenderMatName);
            string cacheKey = key + "_MetGloss_" + metallic.ToString("0.###");
            if (Cache.TryGetValue(cacheKey, out Texture2D hit))
                return hit;

            if (!RoughnessFile.TryGetValue(key, out string? file) || string.IsNullOrEmpty(file))
                return null;

            Texture2D? rough = LoadFile(file, key + "_rough", linear: true);
            if (rough == null)
                return null;

            try
            {
                Color32[] px = rough.GetPixels32();
                byte met = (byte)Mathf.Clamp(Mathf.RoundToInt(metallic * 255f), 0, 255);
                for (int i = 0; i < px.Length; i++)
                {
                    byte smooth = (byte)(255 - px[i].r);
                    px[i] = new Color32(met, met, met, smooth);
                }

                var tex = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, true, linear: true);
                tex.name = cacheKey;
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;
                tex.anisoLevel = 4;
                tex.SetPixels32(px);
                tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
                Cache[cacheKey] = tex;
                return tex;
            }
            catch (Exception ex)
            {
                CrosswimPlugin.ModLog?.LogWarning($"CrosswimMaps metGloss '{key}': {ex.Message}");
                return null;
            }
        }

        private static Texture2D? LoadNamed(
            string blenderMatName,
            Dictionary<string, string> table,
            bool linear,
            string suffix)
        {
            if (string.IsNullOrEmpty(blenderMatName))
                return null;
            string key = StripMatSuffix(blenderMatName);
            string cacheKey = key + suffix;
            if (Cache.TryGetValue(cacheKey, out Texture2D hit))
                return hit;
            if (!table.TryGetValue(key, out string? file) || string.IsNullOrEmpty(file))
                return null;

            Texture2D? tex = LoadFile(file, cacheKey, linear);
            if (tex == null)
                CrosswimPlugin.ModLog?.LogWarning($"CrosswimMaps missing '{file}' for '{key}'");
            return tex;
        }

        private static string StripMatSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            if (name.EndsWith(" (Instance)", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 11);
            if (name.EndsWith("_cw", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 3);
            return name;
        }

        private static Texture2D? LoadFile(string fileName, string cacheKey, bool linear)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;
            if (Cache.TryGetValue(cacheKey, out Texture2D cached))
                return cached;

            byte[]? bytes = ReadFile(fileName);
            if (bytes == null || bytes.Length == 0)
                return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!ImageConversion.LoadImage(tex, bytes, markNonReadable: false))
            {
                UnityEngine.Object.Destroy(tex);
                CrosswimPlugin.ModLog?.LogWarning($"CrosswimMaps LoadImage failed '{fileName}'");
                return null;
            }

            tex.name = cacheKey;
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            Cache[cacheKey] = tex;
            CrosswimPlugin.ModLog?.LogInfo(
                $"CrosswimMaps loaded '{cacheKey}' {tex.width}x{tex.height} ({fileName})");
            return tex;
        }

        private static void PackNormalAg(Texture2D tex)
        {
            try
            {
                Color32[] px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    byte x = px[i].r;
                    byte y = px[i].g;
                    px[i] = new Color32(255, y, 255, x);
                }
                tex.SetPixels32(px);
                tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            }
            catch (Exception ex)
            {
                CrosswimPlugin.ModLog?.LogWarning($"CrosswimMaps PackNormalAg: {ex.Message}");
            }
        }

        private static byte[]? ReadFile(string fileName)
        {
            try
            {
                string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!_loggedRoot)
                {
                    _loggedRoot = true;
                    CrosswimPlugin.ModLog?.LogInfo($"CrosswimMaps pluginDir={dir} bepinex={Paths.PluginPath}");
                }

                var candidates = new List<string>(8);
                if (!string.IsNullOrEmpty(dir))
                {
                    candidates.Add(Path.Combine(dir, "Textures", fileName));
                    candidates.Add(Path.Combine(dir, "Textures", "Crosswim", fileName));
                    candidates.Add(Path.Combine(dir, fileName));
                }
                candidates.Add(Path.Combine(Paths.PluginPath, "MK-65-Crosswim", "Textures", fileName));
                candidates.Add(Path.Combine(Paths.PluginPath, "MK-65-Crosswim", "Textures", "Crosswim", fileName));

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (File.Exists(candidates[i]))
                        return File.ReadAllBytes(candidates[i]);
                }
            }
            catch (Exception ex)
            {
                CrosswimPlugin.ModLog?.LogWarning($"CrosswimMaps: {ex.Message}");
            }
            return null;
        }
    }
}
