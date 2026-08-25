using System;
using System.Text;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// X-77 ApplyFbxLook: URP Lit per Blender slot, tint/maps from bake, disk Textures/ albedo by mat name.
    /// </summary>
    internal static class VisualMaterials
    {
        internal static void StripSceneJunk(GameObject root)
        {
            if (root == null)
                return;

            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light != null)
                    UnityEngine.Object.DestroyImmediate(light.gameObject);
            }
            foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
            {
                if (cam != null)
                    UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform t = all[i];
                if (t == null || t == root.transform)
                    continue;
                string n = t.name;
                if (n.Equals("Light", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Camera", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("Light.", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("Camera.", StringComparison.OrdinalIgnoreCase))
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
        }

        internal static void DestroySpawnedJunk(GameObject root)
        {
            if (root == null)
                return;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform t = all[i];
                if (t == null)
                    continue;
                string n = t.name;
                if (n.IndexOf("CrosswimDecal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("DecalQuad", StringComparison.OrdinalIgnoreCase) >= 0)
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
        }

        internal static void MatchHostDrawState(GameObject vis, GameObject host)
        {
            if (vis == null || host == null)
                return;

            int layer = host.layer;
            uint mask = 1u;
            Renderer? donor = null;
            Renderer[] hostRs = host.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < hostRs.Length; i++)
            {
                Renderer r = hostRs[i];
                if (r == null)
                    continue;
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                    continue;
                if (IsUnderVisual(r.transform))
                    continue;
                donor = r;
                layer = r.gameObject.layer;
                mask = r.renderingLayerMask;
                break;
            }

            Transform[] all = vis.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                    all[i].gameObject.layer = layer;
            }

            Renderer[] visRs = vis.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < visRs.Length; i++)
            {
                Renderer r = visRs[i];
                if (r == null)
                    continue;
                r.renderingLayerMask = mask;
                if (donor != null)
                {
                    r.lightProbeUsage = donor.lightProbeUsage;
                    r.reflectionProbeUsage = donor.reflectionProbeUsage;
                }
            }
        }

        private static bool IsUnderVisual(Transform t)
        {
            while (t != null)
            {
                if (t.name == CrosswimConstants.VisualRootName)
                    return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>X-77 path: clone URP Lit, bake tint/maps, force disk albedo by Blender mat name.</summary>
        internal static void ApplyFbxLook(GameObject root)
        {
            if (root == null)
                return;

            StripSceneJunk(root);
            int n = 0;
            int texOk = 0;
            var summary = new StringBuilder(160);

            Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)))
                    continue;

                Material[] src = r.sharedMaterials;
                if (src == null || src.Length == 0)
                    continue;

                Material[] dst = new Material[src.Length];
                for (int m = 0; m < src.Length; m++)
                {
                    Material? old = src[m];
                    if (old == null)
                    {
                        dst[m] = null!;
                        continue;
                    }

                    string matName = ResolveMatKey(old, r.gameObject.name, m);
                    Material mat = VisualShader.Make(matName + "_cw", cull: 0f);

                    // Disk UCUPaint bake wins; else FBX maps. Mask (Solid Color)* is never albedo.
                    Texture? albedo = CrosswimMaps.Albedo(matName) ?? PeekAlbedo(old);
                    CrosswimLook.Apply(mat, matName, old, albedoOwnsColor: albedo != null);
                    if (albedo != null)
                    {
                        WriteAlbedo(mat, albedo);
                        texOk++;
                    }
                    else
                        ClearAlbedo(mat);

                    Texture2D? nml = CrosswimMaps.Normal(matName);
                    if (nml != null)
                        WriteNormal(mat, nml);
                    else
                    {
                        CopyMap(old, mat, "_BumpMap", "_BumpMap");
                        CopyMap(old, mat, "_BumpMap", "_NormalMap");
                    }

                    float met = CrosswimLook.Metallic(matName, old);
                    Texture2D? metGloss = met > 0.01f ? CrosswimMaps.MetallicGloss(matName, met) : null;
                    if (metGloss != null)
                        WriteMetGloss(mat, metGloss);
                    else if (met > 0.01f)
                        CopyMap(old, mat, "_MetallicGlossMap", "_MetallicGlossMap");
                    else
                    {
                        if (mat.HasProperty("_MetallicGlossMap"))
                            mat.SetTexture("_MetallicGlossMap", null);
                        if (mat.HasProperty("_MaskMap"))
                            mat.SetTexture("_MaskMap", null);
                    }

                    // AO optional — Color bake is source of truth for body paint.
                    Texture2D? ao = CrosswimMaps.Occlusion(matName);
                    if (ao != null)
                        WriteOcclusion(mat, ao);

                    KillEmission(mat);

                    dst[m] = mat;
                    n++;
                    if (summary.Length > 0)
                        summary.Append(',');
                    summary.Append(r.gameObject.name).Append(':').Append(matName);
                    if (albedo is Texture2D td)
                        summary.Append(' ').Append(td.width).Append('x').Append(td.height);
                    else
                        summary.Append(":notex");
                }

                r.sharedMaterials = dst;
                r.enabled = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
                r.receiveShadows = true;
            }

            CrosswimPlugin.ModLog?.LogInfo(
                $"VisualMaterials FBX-look '{root.name}' slots={n} texOk={texOk} shader={VisualShader.Lit.name} [{summary}]");
        }

        private static string ResolveMatKey(Material? old, string goName, int slot)
        {
            if (old != null && !string.IsNullOrEmpty(old.name))
            {
                string name = StripInstance(old.name);
                if (CrosswimLook.Known(name))
                    return name;
                if (!IsGeneric(name))
                    return name;
            }
            return FallbackByMesh(goName, slot);
        }

        private static string FallbackByMesh(string goName, int slot)
        {
            if (!string.IsNullOrEmpty(goName) &&
                goName.StartsWith("Cube", StringComparison.OrdinalIgnoreCase))
                return slot == 0 ? "Metal2" : "LightMaterial";
            if (slot == 1 && CrosswimLook.Known("Metal"))
                return "Metal";
            return CrosswimLook.Known("GlossyMetal") ? "GlossyMetal" : (string.IsNullOrEmpty(goName) ? "mesh" : goName);
        }

        private static bool IsGeneric(string n) =>
            n.StartsWith("Material", StringComparison.OrdinalIgnoreCase)
            || n.Equals("DefaultMaterial", StringComparison.OrdinalIgnoreCase)
            || n.Equals("No Name", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Default-Material", StringComparison.OrdinalIgnoreCase);

        private static string StripInstance(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "mesh";
            const string inst = " (Instance)";
            if (name.EndsWith(inst, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - inst.Length);
            if (name.EndsWith("_cw", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 3);
            if (name.EndsWith("_ww", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 3);
            return name;
        }

        private static void CopyMap(Material? src, Material dst, string srcProp, string dstProp)
        {
            if (src == null || dst == null || !src.HasProperty(srcProp) || !dst.HasProperty(dstProp))
                return;
            Texture t = src.GetTexture(srcProp);
            if (t != null)
                dst.SetTexture(dstProp, t);
        }

        private static Texture? PeekAlbedo(Material? mat)
        {
            if (mat == null)
                return null;
            if (mat.HasProperty("_BaseMap"))
            {
                Texture t = mat.GetTexture("_BaseMap");
                if (t != null)
                    return t;
            }
            if (mat.HasProperty("_MainTex"))
            {
                Texture t = mat.GetTexture("_MainTex");
                if (t != null)
                    return t;
            }
            if (mat.HasProperty("_BaseColorMap"))
            {
                Texture t = mat.GetTexture("_BaseColorMap");
                if (t != null)
                    return t;
            }
            return null;
        }

        private static void WriteAlbedo(Material mat, Texture tex)
        {
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
                VisualShader.ResetSt(mat, "_BaseMap");
            }
            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", tex);
                VisualShader.ResetSt(mat, "_MainTex");
            }
            if (mat.HasProperty("_BaseColorMap"))
            {
                mat.SetTexture("_BaseColorMap", tex);
                VisualShader.ResetSt(mat, "_BaseColorMap");
            }
            mat.EnableKeyword("_BASEMAP");
            mat.EnableKeyword("_MAINTEX");
            VisualShader.EnableBaseMap(mat);
        }

        private static void ClearAlbedo(Material mat)
        {
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", null);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", null);
            if (mat.HasProperty("_BaseColorMap"))
                mat.SetTexture("_BaseColorMap", null);
        }

        private static void WriteNormal(Material mat, Texture tex)
        {
            if (mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", tex);
                VisualShader.ResetSt(mat, "_BumpMap");
            }
            if (mat.HasProperty("_NormalMap"))
            {
                mat.SetTexture("_NormalMap", tex);
                VisualShader.ResetSt(mat, "_NormalMap");
            }
            mat.EnableKeyword("_NORMALMAP");
            if (mat.HasProperty("_BumpScale"))
                mat.SetFloat("_BumpScale", 1f);
        }

        private static void WriteMetGloss(Material mat, Texture tex)
        {
            if (mat.HasProperty("_MetallicGlossMap"))
            {
                mat.SetTexture("_MetallicGlossMap", tex);
                VisualShader.ResetSt(mat, "_MetallicGlossMap");
            }
            if (mat.HasProperty("_MaskMap"))
            {
                mat.SetTexture("_MaskMap", tex);
                VisualShader.ResetSt(mat, "_MaskMap");
            }
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.EnableKeyword("_METALLICGLOSSMAP");
        }

        private static void WriteOcclusion(Material mat, Texture tex)
        {
            if (mat.HasProperty("_OcclusionMap"))
            {
                mat.SetTexture("_OcclusionMap", tex);
                VisualShader.ResetSt(mat, "_OcclusionMap");
            }
            if (mat.HasProperty("_OcclusionStrength"))
                mat.SetFloat("_OcclusionStrength", 1f);
            mat.EnableKeyword("_OCCLUSIONMAP");
        }

        private static void KillEmission(Material mat)
        {
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", Color.black);
            if (mat.HasProperty("_EmissiveColor"))
                mat.SetColor("_EmissiveColor", Color.black);
            if (mat.HasProperty("_EmissionMap"))
                mat.SetTexture("_EmissionMap", null);
            if (mat.HasProperty("_EmissiveColorMap"))
                mat.SetTexture("_EmissiveColorMap", null);
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
    }
}
