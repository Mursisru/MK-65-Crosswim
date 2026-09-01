using System.Collections;
using Blueprinter;
using HarmonyLib;
using UnityEngine;

namespace Crosswim.Patches
{
    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new System.Type[] { })]
    internal static class EncyclopediaAfterLoadPatch
    {
        private static void Postfix(Encyclopedia __instance)
        {
            if (__instance == null || CrosswimPlugin.Instance == null)
                return;
            CrosswimPlugin.Instance.StartBootstrap(__instance);
        }
    }

    [HarmonyPatch(typeof(PatchRunner), nameof(PatchRunner.ApplyAllOps))]
    internal static class BlueprinterOpsAppliedPatch
    {
        private static void Postfix() => BlueprinterGate.MarkOpsApplied();
    }

    internal static class BlueprinterGate
    {
        private static bool _opsApplied;

        internal static void MarkOpsApplied() => _opsApplied = true;

        internal static IEnumerator WaitUntilReady()
        {
            float timeout = 120f;
            float t = 0f;
            while (t < timeout)
            {
                if (_opsApplied)
                    yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            CrosswimPlugin.ModLog?.LogWarning("Blueprinter ApplyAllOps timeout — continuing bootstrap.");
        }
    }
}
