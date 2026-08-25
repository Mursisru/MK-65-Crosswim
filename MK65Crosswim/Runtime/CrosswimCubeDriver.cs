using Crosswim;
using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Opening = absolute FBX localRotation from bake. No bind multiply, no Convert.
    /// </summary>
    internal sealed class CrosswimCubeDriver : MonoBehaviour
    {
        private readonly Transform?[] _fins = new Transform?[CrosswimCubeKeys.FinCount];
        private readonly Vector3[] _pos = new Vector3[CrosswimCubeKeys.FinCount];
        private readonly Vector3[] _scale = new Vector3[CrosswimCubeKeys.FinCount];
        private bool _ready;
        private float _elapsed;
        private bool _playing;

        internal void CaptureBindIfNeeded()
        {
            if (_ready)
                return;
            for (int i = 0; i < CrosswimCubeKeys.FinCount; i++)
            {
                Transform? t = CrosswimCubeClosed.FindExact(transform, CrosswimCubeKeys.Names[i]);
                _fins[i] = t;
                if (t == null)
                    continue;
                _pos[i] = t.localPosition;
                _scale[i] = t.localScale;
            }
            _ready = true;
        }

        internal void Begin()
        {
            CaptureBindIfNeeded();
            _elapsed = 0f;
            _playing = true;
            enabled = true;
            ApplyFrame(0f);
        }

        internal void StopClosed()
        {
            _playing = false;
            enabled = false;
            CaptureBindIfNeeded();
            ApplyFrame(0f);
        }

        private void Update()
        {
            if (!_playing)
                return;

            _elapsed += Time.deltaTime;
            float frame = _elapsed * CrosswimCubeKeys.Fps * CrosswimConstants.OpeningPlaybackRate;
            float last = CrosswimCubeKeys.FrameCount - 1;
            if (frame >= last)
            {
                ApplyFrame(last);
                _playing = false;
                enabled = false;
                return;
            }

            ApplyFrame(frame);
        }

        private void ApplyFrame(float frame)
        {
            if (!_ready)
                return;
            for (int i = 0; i < _fins.Length; i++)
            {
                Transform? t = _fins[i];
                if (t == null)
                    continue;
                t.localPosition = _pos[i];
                t.localScale = _scale[i];
                t.localRotation = CrosswimCubeKeys.Sample(i, frame);
            }
        }
    }
}
