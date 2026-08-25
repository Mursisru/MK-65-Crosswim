using System;
using System.Collections.Generic;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>Blender UCUPaint look table (tint / metal / roughness) for GlossyMetal/Metal/Metal2/LightMaterial.</summary>
    internal static class CrosswimLook
    {
        private struct Entry
        {
            public Color color;
            public float metallic;
            public float roughness;
        }

        private static readonly Dictionary<string, Entry> Table =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    // Painted body: Color bake owns hue. Metal=0 kills green env gloss in URP Lit.
                    "GlossyMetal",
                    new Entry
                    {
                        color = new Color(1f, 1f, 1f, 1f),
                        metallic = 0f,
                        roughness = 0.62f
                    }
                },
                {
                    "Metal",
                    new Entry
                    {
                        color = new Color(0.8f, 0.8f, 0.8f, 1f),
                        metallic = 0.75f,
                        roughness = 0.45f
                    }
                },
                {
                    "Metal2",
                    new Entry
                    {
                        color = new Color(0.15763f, 0.15763f, 0.15763f, 1f),
                        metallic = 0.55f,
                        roughness = 0.55f
                    }
                },
                {
                    "LightMaterial",
                    new Entry
                    {
                        color = new Color(0.7991f, 0.49102f, 0f, 1f),
                        metallic = 0f,
                        roughness = 0.5f
                    }
                }
            };

        internal static bool Known(string name) =>
            !string.IsNullOrEmpty(name) && Table.ContainsKey(name);

        internal static float Metallic(string blenderName, Material? baked)
        {
            if (Table.TryGetValue(blenderName ?? string.Empty, out Entry e))
                return e.metallic;
            return PeekFloat(baked, "_Metallic", 0.5f);
        }

        /// <param name="albedoOwnsColor">True when disk/baked albedo already has paint — tint white to avoid double multiply.</param>
        internal static void Apply(Material mat, string blenderName, Material? baked, bool albedoOwnsColor)
        {
            if (mat == null)
                return;

            Entry e;
            if (!Table.TryGetValue(blenderName ?? string.Empty, out e))
            {
                e = new Entry
                {
                    color = PeekTint(baked),
                    metallic = PeekFloat(baked, "_Metallic", 0.5f),
                    roughness = 1f - PeekFloat(baked, "_Smoothness", PeekFloat(baked, "_Glossiness", 0.5f))
                };
            }

            WriteTint(mat, albedoOwnsColor ? Color.white : e.color);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", e.metallic);
            float smooth = Mathf.Clamp01(1f - e.roughness);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smooth);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", smooth);
            // Dielectric paint: kill specular boost that turns dark green into chrome gloss.
            if (e.metallic <= 0.01f)
            {
                if (mat.HasProperty("_SpecColor"))
                    mat.SetColor("_SpecColor", new Color(0.2f, 0.2f, 0.2f, 1f));
                if (mat.HasProperty("_Specular"))
                    mat.SetFloat("_Specular", 0.2f);
                if (mat.HasProperty("_SpecularColor"))
                    mat.SetColor("_SpecularColor", new Color(0.2f, 0.2f, 0.2f, 1f));
                mat.DisableKeyword("_SPECULAR_SETUP");
                mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                mat.DisableKeyword("_METALLICGLOSSMAP");
                if (mat.HasProperty("_MetallicGlossMap"))
                    mat.SetTexture("_MetallicGlossMap", null);
                if (mat.HasProperty("_MaskMap"))
                    mat.SetTexture("_MaskMap", null);
            }
        }

        private static Color PeekTint(Material? mat)
        {
            if (mat == null)
                return Color.white;
            if (mat.HasProperty("_BaseColor"))
                return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color"))
                return mat.GetColor("_Color");
            return Color.white;
        }

        private static void WriteTint(Material mat, Color tint)
        {
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
        }

        private static float PeekFloat(Material? mat, string prop, float fallback)
        {
            if (mat != null && mat.HasProperty(prop))
                return mat.GetFloat(prop);
            return fallback;
        }
    }
}
