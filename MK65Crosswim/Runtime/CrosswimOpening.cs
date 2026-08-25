using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Hangar: FBX local bind, Legacy Animation off.
    /// Water: rest * dump deltas, not Animation.Play.
    /// </summary>
    internal static class CrosswimOpening
    {
        internal static void PoseClosed(Transform? visual)
        {
            if (visual == null)
                return;
            DisableAnimations(visual);
            CrosswimCubeClosed.Apply(visual);
        }

        internal static void Play(Transform? visual)
        {
            if (visual == null)
                return;
            DisableAnimations(visual);
            CrosswimCubeDriver driver = visual.GetComponent<CrosswimCubeDriver>();
            if (driver == null)
                driver = visual.gameObject.AddComponent<CrosswimCubeDriver>();
            driver.Begin();
        }

        private static void DisableAnimations(Transform visual)
        {
            Animation[] anims = visual.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                Animation a = anims[i];
                if (a == null)
                    continue;
                a.playAutomatically = false;
                a.Stop();
                a.enabled = false;
            }

            Animator[] animators = visual.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator an = animators[i];
                if (an == null)
                    continue;
                an.speed = 0f;
                an.enabled = false;
            }
        }

        internal static bool IsOpeningPart(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.StartsWith("Cube", System.StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.StartsWith("OP", System.StringComparison.OrdinalIgnoreCase))
                return true;
            return name.IndexOf("Opening", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
