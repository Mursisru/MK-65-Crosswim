using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Crosswim.UnityBake
{
    public static class NobpBundleBuilder
    {
        private const string PrefabName = "CrosswimVisual";
        private const string OutputName = "MK65Crosswim.nobp";
        private const string FbxName = "MK-65-Crosswim.fbx";

        [MenuItem("Crosswim/Build Nobp Bundle")]
        public static void Build()
        {
            string assetsRoot = "Assets/MissilePack";
            string buildDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Build"));
            Directory.CreateDirectory(buildDir);

            EnsurePrefab(assetsRoot);
            EnsureManifest(assetsRoot);

            string prefabPath = $"{assetsRoot}/{PrefabName}.prefab";
            string manifestPath = $"{assetsRoot}/patch_manifest.txt";
            var assetNames = new List<string> { prefabPath, manifestPath };

            string matFolder = $"{assetsRoot}/Materials/Crosswim";
            if (AssetDatabase.IsValidFolder(matFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { matFolder }))
                    assetNames.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            string texFolder = $"{assetsRoot}/Textures/Crosswim";
            if (AssetDatabase.IsValidFolder(texFolder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texFolder }))
                    assetNames.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            string fbx = $"{assetsRoot}/{FbxName}";
            if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), fbx.Replace('/', Path.DirectorySeparatorChar))))
                assetNames.Add(fbx);

            var build = new AssetBundleBuild
            {
                assetBundleName = OutputName,
                assetNames = assetNames.ToArray()
            };

            BuildPipeline.BuildAssetBundles(
                buildDir,
                new[] { build },
                BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64);

            string produced = Path.Combine(buildDir, OutputName);
            string alt = Path.Combine(buildDir, OutputName.ToLowerInvariant());
            string src = File.Exists(alt) ? alt : produced;

            string pluginRes = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "MK65Crosswim", "Resources"));
            Directory.CreateDirectory(pluginRes);
            if (File.Exists(src))
            {
                File.Copy(src, Path.Combine(pluginRes, OutputName), true);
                string binRel = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "MK65Crosswim", "bin", "Release"));
                Directory.CreateDirectory(binRel);
                File.Copy(src, Path.Combine(binRel, OutputName), true);
            }

            string deploy = @"C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\BepInEx\plugins\MK-65-Crosswim";
            Directory.CreateDirectory(deploy);
            if (File.Exists(src))
            {
                File.Copy(src, Path.Combine(deploy, OutputName), true);
                File.Copy(src, Path.Combine(deploy, OutputName.ToLowerInvariant()), true);
            }

            Debug.Log($"Crosswim: built {src}");
            AssetDatabase.Refresh();
        }

        private static void EnsureManifest(string assetsRoot)
        {
            string json =
@"{
  ""modName"": ""MK65Crosswim"",
  ""schemaVersion"": 3,
  ""modVersion"": ""0.0.0"",
  ""Patches"": [],
  ""Ops"": [],
  ""Addressables"": []
}";
            string txtPath = Path.Combine(Application.dataPath, "MissilePack", "patch_manifest.txt");
            File.WriteAllText(txtPath, json);
            AssetDatabase.ImportAsset($"{assetsRoot}/patch_manifest.txt");
        }

        private static void EnsurePrefab(string assetsRoot)
        {
            string fbxPath = $"{assetsRoot}/{FbxName}";
            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), fbxPath.Replace('/', Path.DirectorySeparatorChar))))
            {
                Debug.LogError("MK-65-Crosswim.fbx not found");
                return;
            }

            // Sample opening from Legacy clips, then None for hangar rest (same FBX axes as OP).
            ConfigureImporter(fbxPath, ModelImporterAnimationType.Legacy);
            CubeKeyBaker.BindPose[] cubeBinds = CubeKeyBaker.BakeAndWriteKeys(fbxPath);
            ConfigureImporter(fbxPath, ModelImporterAnimationType.None);
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null)
            {
                Debug.LogError("Failed to load Crosswim FBX");
                return;
            }

            GameObject root = UnityEngine.Object.Instantiate(fbx);
            root.name = PrefabName;
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
            // Blender empties named Light/Camera (no component) cause hangar glare if left in.
            Transform[] junk = root.GetComponentsInChildren<Transform>(true);
            for (int i = junk.Length - 1; i >= 0; i--)
            {
                Transform t = junk[i];
                if (t == null || t == root.transform)
                    continue;
                string n = t.name;
                if (n.Equals("Light", StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Camera", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("Light.", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("Camera.", StringComparison.OrdinalIgnoreCase))
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            }

            Shader lit = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            string matFolder = $"{assetsRoot}/Materials/Crosswim";
            if (!AssetDatabase.IsValidFolder($"{assetsRoot}/Materials"))
                AssetDatabase.CreateFolder(assetsRoot, "Materials");
            if (!AssetDatabase.IsValidFolder(matFolder))
                AssetDatabase.CreateFolder($"{assetsRoot}/Materials", "Crosswim");

            Dictionary<string, Look> looks = LoadLooks($"{assetsRoot}/Textures/Crosswim/crosswim_look.json");
            var shared = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null)
                    continue;
                Material[] src = r.sharedMaterials;
                Material[] dst = new Material[Mathf.Max(1, src != null ? src.Length : 1)];
                for (int i = 0; i < dst.Length; i++)
                {
                    Material imported = src != null && i < src.Length ? src[i] : null;
                    string blenderName = ResolveBlenderMatName(imported, r.gameObject.name, looks);
                    if (!shared.TryGetValue(blenderName, out Material mat) || mat == null)
                    {
                        string matPath = $"{matFolder}/{Sanitize(blenderName)}.mat";
                        mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                        if (mat == null)
                        {
                            mat = imported != null ? new Material(imported) : new Material(lit);
                            mat.name = blenderName;
                            AssetDatabase.CreateAsset(mat, matPath);
                        }
                        shared[blenderName] = mat;
                    }
                    if (mat.shader == null || mat.shader.name.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
                        mat.shader = lit;
                    ApplyLook(mat, blenderName, looks, assetsRoot);
                    EditorUtility.SetDirty(mat);
                    dst[i] = mat;
                }
                r.sharedMaterials = dst;
            }

            CubeKeyBaker.ApplyBinds(root, cubeBinds);
            PoseClosed(root);
            LogFinRest(root);
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root, $"{assetsRoot}/{PrefabName}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static string ResolveBlenderMatName(Material imported, string goName, Dictionary<string, Look> looks)
        {
            if (imported != null && !string.IsNullOrEmpty(imported.name))
            {
                string n = imported.name.Replace(" (Instance)", "");
                if (looks.ContainsKey(n) || !IsGenericMatName(n))
                    return n;
            }
            if (looks.ContainsKey("GlossyMetal"))
                return "GlossyMetal";
            return string.IsNullOrEmpty(goName) ? "mesh" : goName;
        }

        private static bool IsGenericMatName(string n)
        {
            return n.StartsWith("Material", StringComparison.OrdinalIgnoreCase)
                || n.Equals("DefaultMaterial", StringComparison.OrdinalIgnoreCase)
                || n.Equals("No Name", StringComparison.OrdinalIgnoreCase);
        }

        private static void PoseClosed(GameObject root)
        {
            root.SetActive(true);
            DisableAnimations(root);
        }

        private static void LogFinRest(GameObject root)
        {
            string[] names = { "Cube", "Cube.001", "Cube.002", "Cube.003", "OP", "OP.001", "OP.002", "OP.003", "DockingPlace" };
            for (int i = 0; i < names.Length; i++)
            {
                Transform t = FindChild(root.transform, names[i]);
                if (t == null)
                {
                    Debug.LogWarning("Crosswim bake missing " + names[i]);
                    continue;
                }
                Vector3 p = t.localPosition;
                Vector3 e = t.localRotation.eulerAngles;
                Debug.Log($"Crosswim rest {names[i]} pos=({p.x:F4},{p.y:F4},{p.z:F4}) euler=({e.x:F1},{e.y:F1},{e.z:F1})");
            }
        }

        private static Transform FindChild(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && string.Equals(all[i].name, name, StringComparison.Ordinal))
                    return all[i];
            }
            return null;
        }

        private static void DisableAnimations(GameObject root)
        {
            Animation[] anims = root.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                Animation a = anims[i];
                if (a == null)
                    continue;
                a.playAutomatically = false;
                a.Stop();
                a.enabled = false;
            }
        }

        [Serializable]
        private class LookRoot { public LookEntry[] mats; }

        [Serializable]
        private class LookEntry
        {
            public string name;
            public float colR, colG, colB, colA = 1f;
            public float metallic;
            public float roughness;
            public string albedo;
        }

        private struct Look
        {
            public Color color;
            public float metallic;
            public float roughness;
            public string albedo;
        }

        private static Dictionary<string, Look> LoadLooks(string assetPath)
        {
            var map = new Dictionary<string, Look>(StringComparer.OrdinalIgnoreCase);
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(abs))
                return map;
            try
            {
                string json = File.ReadAllText(abs);
                if (!json.Contains("\"mats\""))
                    json = "{\"mats\":[]}";
                LookRoot root = JsonUtility.FromJson<LookRoot>(json);
                if (root?.mats == null)
                    return map;
                for (int i = 0; i < root.mats.Length; i++)
                {
                    LookEntry e = root.mats[i];
                    if (e == null || string.IsNullOrEmpty(e.name))
                        continue;
                    map[e.name] = new Look
                    {
                        color = new Color(e.colR, e.colG, e.colB, e.colA > 0f ? e.colA : 1f),
                        metallic = e.metallic,
                        roughness = e.roughness,
                        albedo = e.albedo
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("crosswim look: " + ex.Message);
            }
            return map;
        }

        private static void ApplyLook(Material mat, string blenderName, Dictionary<string, Look> looks, string assetsRoot)
        {
            Look look;
            if (!looks.TryGetValue(blenderName, out look))
            {
                look = new Look { color = Color.white, metallic = 0.6f, roughness = 0.35f };
            }
            string texRoot = $"{assetsRoot}/Textures/Crosswim";
            bool albedoOk = false;
            if (!string.IsNullOrEmpty(look.albedo))
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{texRoot}/{look.albedo}");
                if (tex != null)
                {
                    if (mat.HasProperty("_MainTex"))
                        mat.SetTexture("_MainTex", tex);
                    if (mat.HasProperty("_BaseMap"))
                        mat.SetTexture("_BaseMap", tex);
                    albedoOk = true;
                }
            }

            // Baked UCUPaint albedo already owns paint color — white tint avoids double multiply.
            Color tint = albedoOk ? Color.white : look.color;
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", look.metallic);
            float smooth = Mathf.Clamp01(1f - look.roughness);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", smooth);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smooth);
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "mesh";
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                    chars[i] = '_';
            }
            return new string(chars);
        }

        private static void ConfigureImporter(string fbxPath, ModelImporterAnimationType anim)
        {
            ModelImporter imp = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (imp == null)
            {
                AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);
                return;
            }
            imp.weldVertices = false;
            imp.importNormals = ModelImporterNormals.Import;
            imp.preserveHierarchy = true;
            imp.addCollider = false;
            imp.importLights = false;
            imp.importCameras = false;
            imp.animationType = anim;
            imp.importAnimation = anim != ModelImporterAnimationType.None;
            imp.resampleCurves = false;
            imp.animationCompression = ModelImporterAnimationCompression.Off;
            // Keep Blender File Scale ×100 on transforms — do NOT flatten (scatters empties/meshes).
            imp.useFileScale = true;
            imp.globalScale = 1f;
            imp.SaveAndReimport();
        }
    }
}

public static class BatchBuild
{
    public static void Build()
    {
        Crosswim.UnityBake.NobpBundleBuilder.Build();
    }
}
