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
            if (!File.Exists(produced) && File.Exists(alt))
                File.Copy(alt, produced, true);

            string pluginRes = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "MK65Crosswim", "Resources"));
            Directory.CreateDirectory(pluginRes);
            if (File.Exists(produced))
            {
                File.Copy(produced, Path.Combine(pluginRes, OutputName), true);
                string binRel = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "MK65Crosswim", "bin", "Release"));
                Directory.CreateDirectory(binRel);
                File.Copy(produced, Path.Combine(binRel, OutputName), true);
            }

            string deploy = @"C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\BepInEx\plugins\MK-65-Crosswim";
            Directory.CreateDirectory(deploy);
            if (File.Exists(produced))
            {
                File.Copy(produced, Path.Combine(deploy, OutputName), true);
                File.Copy(produced, Path.Combine(deploy, OutputName.ToLowerInvariant()), true);
            }

            Debug.Log($"Crosswim: built {produced}");
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

            ConfigureImporter(fbxPath);
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

            PoseClosed(root);
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
            Animation[] anims = root.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                Animation a = anims[i];
                if (a == null)
                    continue;
                a.playAutomatically = false;
                a.enabled = true;
                a.Stop();
                foreach (AnimationState st in a)
                {
                    if (st == null)
                        continue;
                    st.enabled = true;
                    st.weight = 1f;
                    st.time = 0f;
                    st.speed = 0f;
                }
                a.Sample();
                a.enabled = false;
            }

            ApplyDumpClosedPose(root);
        }

        // Blender dump frame 0 → Unity FBX (pos -X,Z,Y; rot -90X * euler(x,-y,-z); scale 100).
        private static void ApplyDumpClosedPose(GameObject root)
        {
            ApplyRest(root, "Cube", new Vector3(-1.69196f, 0.00028f, 0.68979f), new Vector3(0f, -1.57036f, 1.5708f));
            ApplyRest(root, "Cube.001", new Vector3(-1.69196f, -0.68979f, 0.00028f), new Vector3(3.14159f, -0.00044f, -1.5708f));
            ApplyRest(root, "Cube.002", new Vector3(-1.69196f, 0.00028f, -0.68979f), new Vector3(0f, 1.46512f, 1.5708f));
            ApplyRest(root, "Cube.003", new Vector3(-1.69196f, 0.68979f, 0.00028f), new Vector3(0f, -0.10567f, 1.5708f));
            ApplyRest(root, "OP", new Vector3(5.10305f, 0f, 0f), new Vector3(0f, -0.05262f, 0f));
            ApplyRest(root, "OP.001", new Vector3(5.10305f, 0f, 0f), new Vector3(1.5708f, 0f, -0.05262f));
            ApplyRest(root, "OP.002", new Vector3(5.10305f, 0f, 0f), new Vector3(0f, -0.05262f, 0f));
            ApplyRest(root, "OP.003", new Vector3(5.10305f, 0f, 0f), new Vector3(1.5708f, 0f, -0.05262f));
        }

        private static void ApplyRest(GameObject root, string name, Vector3 blenderLoc, Vector3 blenderEulerRad)
        {
            Transform t = FindExact(root.transform, name);
            if (t == null)
                return;
            t.localPosition = new Vector3(-blenderLoc.x, blenderLoc.z, blenderLoc.y);
            Quaternion b = Quaternion.Euler(
                blenderEulerRad.x * Mathf.Rad2Deg,
                blenderEulerRad.y * Mathf.Rad2Deg,
                blenderEulerRad.z * Mathf.Rad2Deg);
            Quaternion mapped = new Quaternion(-b.x, -b.z, -b.y, b.w);
            t.localRotation = Quaternion.Euler(-90f, 0f, 0f) * mapped;
            t.localScale = Vector3.one * 100f;
        }

        private static Transform FindExact(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                    return all[i];
            }
            return null;
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

        private static void ConfigureImporter(string fbxPath)
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
            imp.animationType = ModelImporterAnimationType.Legacy;
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
