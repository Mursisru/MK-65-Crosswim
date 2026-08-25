using Crosswim.Blueprinter;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// One-time ApplyFbxLook + VisualFit into a dormant template.
    /// Live stamp = Instantiate only (no material rebuild hitch on drop).
    /// </summary>
    internal static class CrosswimVisualCache
    {
        private static GameObject? _hold;
        private static GameObject? _template;
        private static bool _ready;

        internal static bool Ready => _ready && _template != null;

        internal static void Warm()
        {
            if (_ready)
                return;
            NobpContent.TryLoad();
            GameObject? src = NobpContent.VisualPrefab;
            if (src == null)
                return;

            if (_hold == null)
            {
                _hold = new GameObject("Crosswim_VisualHold");
                Object.DontDestroyOnLoad(_hold);
                _hold.SetActive(false);
            }

            if (_template != null)
                Object.Destroy(_template);

            _template = Object.Instantiate(src, _hold.transform, false);
            _template.name = CrosswimConstants.VisualRootName;
            _template.SetActive(true);
            VisualMaterials.StripSceneJunk(_template);
            VisualMaterials.DestroySpawnedJunk(_template);
            VisualMaterials.ApplyFbxLook(_template);
            VisualFit.Apply(_template.transform);
            CrosswimOpening.PoseClosed(_template.transform);
            _template.SetActive(false);
            _ready = true;
            CrosswimPlugin.ModLog?.LogInfo("Crosswim visual cache warmed (live stamp skips ApplyFbxLook).");
        }

        internal static GameObject? InstantiatePrepared(Transform parent)
        {
            if (!_ready)
                Warm();
            if (_template == null)
                return null;
            GameObject go = Object.Instantiate(_template, parent, false);
            go.name = CrosswimConstants.VisualRootName;
            go.hideFlags = HideFlags.None;
            go.SetActive(true);
            return go;
        }
    }
}
