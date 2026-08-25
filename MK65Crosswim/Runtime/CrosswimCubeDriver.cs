using UnityEngine;

namespace Crosswim.Runtime
{
    /// <summary>
    /// Opening = FBX rest * Blender delta. Children stay in model local space.
    /// </summary>
    internal sealed class CrosswimCubeDriver : MonoBehaviour
    {
        private struct Bind
        {
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 scale;
        }

        private readonly Transform?[] _fins = new Transform?[CrosswimCubeKeys.FinCount];
        private readonly Bind[] _bind = new Bind[CrosswimCubeKeys.FinCount];
        private bool _captured;
        private float _elapsed;
        private bool _playing;

        internal void CaptureBindIfNeeded()
        {
            if (_captured)
                return;
            CacheFins();
            for (int i = 0; i < _fins.Length; i++)
            {
                Transform? t = _fins[i];
                if (t == null)
                    continue;
                _bind[i].pos = t.localPosition;
                _bind[i].rot = t.localRotation;
                _bind[i].scale = t.localScale;
            }
            _captured = true;
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
            float frame = _elapsed * CrosswimCubeKeys.Fps;
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

        private void CacheFins()
        {
            for (int i = 0; i < CrosswimCubeKeys.FinCount; i++)
                _fins[i] = CrosswimCubeClosed.FindExact(transform, CrosswimCubeKeys.Names[i]);
        }

        private void ApplyFrame(float frame)
        {
            if (!_captured)
                return;
            for (int i = 0; i < _fins.Length; i++)
            {
                Transform? t = _fins[i];
                if (t == null)
                    continue;
                Bind b = _bind[i];
                t.localPosition = b.pos;
                t.localRotation = b.rot * CrosswimCubeKeys.Delta(i, frame);
                t.localScale = b.scale;
            }
        }
    }
}
