using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Opening fins: Cube / OP* with Blender actions 0–120.
    /// Hangar must stay at frame 0 (closed). Play only on water / motor arm.
    /// </summary>
    internal static class CrosswimOpening
    {
        internal static void PoseClosed(Transform? visual)
        {
            if (visual == null)
                return;

            Animation[] anims = visual.GetComponentsInChildren<Animation>(true);
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

            // Bind pose is mid-open; force Blender dump frame 0 (also baked into fresh nobp).
            CrosswimRestPose.ApplyClosed(visual);

            Animator[] animators = visual.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator an = animators[i];
                if (an == null)
                    continue;
                an.enabled = true;
                an.speed = 0f;
                an.Play(0, 0, 0f);
                an.Update(0f);
                an.enabled = false;
            }
        }

        internal static void Play(Transform? visual)
        {
            if (visual == null)
                return;

            float dur = CrosswimConstants.OpeningFrames / CrosswimConstants.OpeningFpsFallback;
            var animated = new System.Collections.Generic.HashSet<Transform>();

            Animation[] anims = visual.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                Animation a = anims[i];
                if (a == null)
                    continue;
                a.playAutomatically = false;
                a.enabled = true;
                bool played = false;
                foreach (AnimationState st in a)
                {
                    if (st == null)
                        continue;
                    st.speed = 1f;
                    st.time = 0f;
                    st.weight = 1f;
                    a.Play(st.name);
                    played = true;
                }
                if (played && a.transform != null)
                    animated.Add(a.transform);
            }

            Animator[] animators = visual.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                Animator an = animators[i];
                if (an == null || an.runtimeAnimatorController == null)
                    continue;
                an.enabled = true;
                an.speed = 1f;
                an.Play(0, 0, 0f);
                animated.Add(an.transform);
            }

            // OP* often have no FBX clip — unfold those that stayed silent.
            Transform[] all = visual.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == visual || !IsOpeningPart(t.name) || animated.Contains(t))
                    continue;
                CrosswimUnfold u = t.gameObject.GetComponent<CrosswimUnfold>() ??
                                  t.gameObject.AddComponent<CrosswimUnfold>();
                u.Begin(dur);
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

    internal sealed class CrosswimUnfold : MonoBehaviour
    {
        private Quaternion _from;
        private Quaternion _to;
        private float _dur = 1f;
        private float _t;

        internal void Begin(float duration)
        {
            _dur = Mathf.Max(0.05f, duration);
            _to = transform.localRotation;
            _from = _to * Quaternion.Euler(70f, 0f, 0f);
            transform.localRotation = _from;
            _t = 0f;
            enabled = true;
        }

        private void LateUpdate()
        {
            _t += Time.deltaTime;
            float u = Mathf.Clamp01(_t / _dur);
            transform.localRotation = Quaternion.Slerp(_from, _to, u);
            if (u >= 1f)
                enabled = false;
        }
    }
}
