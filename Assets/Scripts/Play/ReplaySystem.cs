using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Records a rolling window of every tracked transform (ball, striker bones,
    /// keeper) and plays it back sports-broadcast style after a goal: physics is
    /// frozen, the last few seconds are replayed in slow motion, then the scene is
    /// restored and normal play resumes.
    ///
    /// Recording is sampled each FixedUpdate; playback advances a cursor at a slowed
    /// rate and writes the sampled poses straight onto the transforms.
    /// </summary>
    public class ReplaySystem : MonoBehaviour
    {
        struct Frame { public Vector3[] pos; public Quaternion[] rot; }

        Transform[] _tracked;
        Rigidbody[] _bodies;      // to freeze/thaw during playback
        bool[] _wasKinematic;     // original kinematic state, restored after playback
        MonoBehaviour[] _drivers; // driving scripts to pause during playback
        readonly List<Frame> _buffer = new List<Frame>();
        int _capacity;
        bool _recording;
        bool _playing;
        float _playCursor;        // fractional frame index during playback
        float _playRate;          // frames advanced per unscaled second * dt

        public bool IsPlaying => _playing;

        public void Setup(List<Transform> tracked, List<MonoBehaviour> drivers, float windowSeconds)
        {
            _tracked = tracked.ToArray();
            _drivers = drivers != null ? drivers.ToArray() : new MonoBehaviour[0];
            var bodies = new List<Rigidbody>();
            foreach (var t in _tracked)
            {
                if (t == null) continue;
                var rb = t.GetComponent<Rigidbody>();
                if (rb != null) bodies.Add(rb);
            }
            _bodies = bodies.ToArray();
            _wasKinematic = new bool[_bodies.Length];
            _capacity = Mathf.CeilToInt(windowSeconds / 0.02f); // fixed step is 0.02s
            _recording = true;
        }

        void FixedUpdate()
        {
            if (!_recording || _playing || _tracked == null) return;
            var f = new Frame
            {
                pos = new Vector3[_tracked.Length],
                rot = new Quaternion[_tracked.Length]
            };
            for (int i = 0; i < _tracked.Length; i++)
            {
                if (_tracked[i] == null) continue;
                f.pos[i] = _tracked[i].position;
                f.rot[i] = _tracked[i].rotation;
            }
            _buffer.Add(f);
            if (_buffer.Count > _capacity) _buffer.RemoveAt(0);
        }

        /// <summary>Freeze physics and play the buffered window back at slowMul speed.</summary>
        public void Play(float slowMul)
        {
            if (_buffer.Count < 2) return;
            _playing = true;
            _recording = false;
            _playCursor = 0f;
            _playRate = (1f / 0.02f) * Mathf.Clamp01(slowMul); // buffer is 50 fps
            SetDrivers(false);
            SetKinematic(true);
        }

        void Update()
        {
            if (!_playing) return;
            _playCursor += _playRate * Time.unscaledDeltaTime;
            int i = Mathf.FloorToInt(_playCursor);
            if (i >= _buffer.Count - 1) { Stop(); return; }

            float frac = _playCursor - i;
            var a = _buffer[i];
            var b = _buffer[i + 1];
            for (int k = 0; k < _tracked.Length; k++)
            {
                if (_tracked[k] == null) continue;
                _tracked[k].position = Vector3.Lerp(a.pos[k], b.pos[k], frac);
                _tracked[k].rotation = Quaternion.Slerp(a.rot[k], b.rot[k], frac);
            }
        }

        public void Stop()
        {
            if (!_playing) return;
            _playing = false;
            SetKinematic(false);
            SetDrivers(true);
            _buffer.Clear();
            _recording = true;
        }

        void SetKinematic(bool freeze)
        {
            for (int i = 0; i < _bodies.Length; i++)
            {
                var rb = _bodies[i];
                if (rb == null) continue;
                if (freeze)
                {
                    _wasKinematic[i] = rb.isKinematic;
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    rb.isKinematic = true;
                }
                else
                {
                    rb.isKinematic = _wasKinematic[i]; // restore (keeper stays kinematic)
                }
            }
        }

        void SetDrivers(bool enabled)
        {
            foreach (var d in _drivers)
                if (d != null) d.enabled = enabled;
        }
    }
}
