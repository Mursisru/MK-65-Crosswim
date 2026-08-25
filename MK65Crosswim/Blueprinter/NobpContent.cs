using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Crosswim.Blueprinter
{
    internal static class NobpContent
    {
        private static AssetBundle? _bundle;
        private static GameObject? _visualPrefab;
        private static bool _tried;

        internal static GameObject? VisualPrefab => _visualPrefab;

        internal static void TryLoad()
        {
            if (_tried)
                return;
            _tried = true;
            try
            {
                _bundle = FindLoaded() ?? LoadFromDiskOrEmbedded();
                if (_bundle == null)
                {
                    CrosswimPlugin.ModLog?.LogWarning("MK65Crosswim.nobp missing — visual stamp skipped.");
                    return;
                }

                _visualPrefab = _bundle.LoadAsset<GameObject>(CrosswimConstants.MeshPrefabAsset);
                if (_visualPrefab == null)
                {
                    GameObject[] all = _bundle.LoadAllAssets<GameObject>();
                    if (all != null)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            GameObject go = all[i];
                            if (go == null)
                                continue;
                            if (go.name.IndexOf("Crosswim", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                _visualPrefab = go;
                                break;
                            }
                        }
                    }
                }

                if (_visualPrefab != null)
                    CrosswimPlugin.ModLog?.LogInfo($"Crosswim visual ready: '{_visualPrefab.name}'");
                else
                    CrosswimPlugin.ModLog?.LogWarning("nobp loaded but CrosswimVisual not found.");
            }
            catch (Exception ex)
            {
                CrosswimPlugin.ModLog?.LogError($"NobpContent: {ex}");
            }
        }

        private static AssetBundle? FindLoaded()
        {
            foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (b == null)
                    continue;
                try
                {
                    if (b.Contains(CrosswimConstants.MeshPrefabAsset))
                        return b;
                }
                catch
                {
                    // ignore
                }
            }
            return null;
        }

        private static AssetBundle? LoadFromDiskOrEmbedded()
        {
            string? path = FindNobpPath();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                AssetBundle? fromFile = AssetBundle.LoadFromFile(path);
                if (fromFile != null)
                {
                    CrosswimPlugin.ModLog?.LogInfo($"Loaded .nobp from file: {path}");
                    return fromFile;
                }
            }
            return LoadEmbedded();
        }

        private static string? FindNobpPath()
        {
            string? pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(pluginDir))
                return null;
            string direct = Path.Combine(pluginDir, CrosswimConstants.NobpFileName);
            return File.Exists(direct) ? direct : null;
        }

        private static AssetBundle? LoadEmbedded()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith(".nobp", StringComparison.OrdinalIgnoreCase))
                    continue;
                using Stream? stream = asm.GetManifestResourceStream(name);
                if (stream == null)
                    continue;
                using MemoryStream ms = new MemoryStream();
                stream.CopyTo(ms);
                return AssetBundle.LoadFromMemory(ms.ToArray());
            }
            return null;
        }
    }
}
