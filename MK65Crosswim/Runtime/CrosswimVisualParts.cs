using System;
using UnityEngine;

namespace Crosswim.Runtime
{
    internal static class CrosswimVisualParts
    {
        // Exact VLSB mesh only — not VLSBEngineEffectsSpawn*.
        internal static void ApplyCarrier(Transform vis, bool ship, bool encyclopedia)
        {
            if (vis == null)
                return;

            bool showVlsb = ship && !encyclopedia;
            bool showDock = !ship && !encyclopedia;

            SetVlsbTree(vis, showVlsb);
            SetNamedActive(vis, CrosswimConstants.DockAliases, showDock);
        }

        private static void SetVlsbTree(Transform vis, bool active)
        {
            Transform[] all = vis.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == vis)
                    continue;
                string n = t.name;
                if (string.Equals(n, "VLSB", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("VLSB.", StringComparison.OrdinalIgnoreCase) ||
                    n.StartsWith("VLSBEngine", StringComparison.OrdinalIgnoreCase))
                    t.gameObject.SetActive(active);
            }
        }

        internal static void SetNamedActive(Transform vis, string[] aliases, bool active)
        {
            Transform[] all = vis.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == vis)
                    continue;
                if (!NameMatches(t.name, aliases))
                    continue;
                t.gameObject.SetActive(active);
            }
        }

        internal static Transform? FindExact(Transform root, string exactName)
        {
            if (root == null || string.IsNullOrEmpty(exactName))
                return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t != null && string.Equals(t.name, exactName, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        /// <summary>Stop booster exhaust left on the body after VLSB mesh is shed.</summary>
        internal static void KillVlsbFx(Transform vis)
        {
            if (vis == null)
                return;
            Transform[] all = vis.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == vis)
                    continue;
                string n = t.name;
                if (!n.StartsWith("VLSBEngine", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(n, "VLSB", StringComparison.OrdinalIgnoreCase) &&
                    !n.StartsWith("VLSB.", StringComparison.OrdinalIgnoreCase))
                    continue;
                KillVlsbFxSubtree(t);
                t.gameObject.SetActive(false);
            }
        }

        internal static void KillVlsbFxSubtree(Transform root)
        {
            if (root == null)
                return;
            ParticleSystem[] ps = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int p = 0; p < ps.Length; p++)
            {
                if (ps[p] == null)
                    continue;
                ps[p].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps[p].gameObject.SetActive(false);
            }
        }

        internal static Transform? FindByAliases(Transform root, string[] aliases)
        {
            if (root == null || aliases == null)
                return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int a = 0; a < aliases.Length; a++)
            {
                string alias = aliases[a];
                if (string.IsNullOrEmpty(alias))
                    continue;
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t != null && string.Equals(t.name, alias, System.StringComparison.OrdinalIgnoreCase))
                        return t;
                }
            }
            // Fuzzy match — skip VLSBEngine* when looking for VLSB mesh.
            for (int a = 0; a < aliases.Length; a++)
            {
                string alias = aliases[a];
                if (string.IsNullOrEmpty(alias))
                    continue;
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t == null)
                        continue;
                    if (t.name.StartsWith("VLSBEngine", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (t.name.IndexOf(alias, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return t;
                }
            }
            return null;
        }

        internal static bool NameMatches(string name, string[] aliases)
        {
            if (string.IsNullOrEmpty(name) || aliases == null)
                return false;
            for (int i = 0; i < aliases.Length; i++)
            {
                string a = aliases[i];
                if (string.IsNullOrEmpty(a))
                    continue;
                if (string.Equals(name, a, System.StringComparison.OrdinalIgnoreCase))
                    return true;
                if (name.IndexOf(a, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
