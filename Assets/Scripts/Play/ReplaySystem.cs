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
    ///
    /// Local SCALE is recorded alongside position and rotation. Bones never scale, but the
    /// adult-mode appendage's pieces are re-scaled every tick by AnatomySim (the shaft's length is
    /// its scale), and they are tracked too - see <see cref="TrackBody"/>.
    /// </summary>
    public class ReplaySystem : MonoBehaviour
    {
        struct Frame { public Vector3[] pos; public Quaternion[] rot; public Vector3[] scl; }

        Transform[] _tracked;
        Rigidbody[] _bodies;      // to freeze/thaw during playback
        bool[] _wasKinematic;     // original kinematic state, restored after playback
        MonoBehaviour[] _drivers; // driving scripts to pause during playback
        // A preallocated RING of frames, reused for the whole match. This used to be a List that
        // got two new arrays per physics step and a RemoveAt(0) shift of a 200-entry list once
        // full - steady garbage at 50 Hz in every mode with a replay.
        Frame[] _ring;
        int _head, _count;        // oldest frame index, frames held
        int _capacity;
        Frame FrameAt(int i) => _ring[(_head + i) % _capacity];   // i = 0 is the oldest
        bool _recording;
        bool _playing;
        float _playCursor;        // fractional frame index during playback
        float _playRate;          // frames advanced per unscaled second * dt

        public bool IsPlaying => _playing;

        /// <summary>
        /// Add everything on a body a replay has to carry: its bones, plus the adult-mode
        /// appendage's posed pieces, with the AnatomySim that poses them added as a DRIVER so it is
        /// paused for the playback and the recording wins. Without that the piece re-simulated a
        /// fresh hang off the replayed pelvis, so a goal scored with it standing to attention
        /// replayed with it hanging. `drivers` may be null when the caller has no list to keep.
        /// </summary>
        public static void TrackBody(List<Transform> tracked, List<MonoBehaviour> drivers, ActiveRagdoll rag)
        {
            if (rag == null) return;
            tracked.AddRange(rag.BoneTransforms);
            var anatomy = rag.Anatomy;
            if (anatomy == null) return;
            tracked.AddRange(anatomy.ReplayTransforms);
            drivers?.Add(anatomy);
        }

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
            _capacity = Mathf.Max(2, Mathf.CeilToInt(windowSeconds / 0.02f)); // fixed step is 0.02s
            _ring = new Frame[_capacity];
            for (int i = 0; i < _capacity; i++)
                _ring[i] = new Frame { pos = new Vector3[_tracked.Length], rot = new Quaternion[_tracked.Length],
                                       scl = new Vector3[_tracked.Length] };
            _head = 0; _count = 0;
            _recording = true;
        }

        void FixedUpdate()
        {
            if (!_recording || _playing || _tracked == null || _ring == null) return;
            // Write into the slot after the newest; once full, that slot IS the oldest, which the
            // head then steps past - no allocation, no shift.
            int slot = (_head + _count) % _capacity;
            if (_count == _capacity) _head = (_head + 1) % _capacity;
            else _count++;
            var f = _ring[slot];
            for (int i = 0; i < _tracked.Length; i++)
            {
                if (_tracked[i] == null) continue;
                f.pos[i] = _tracked[i].position;
                f.rot[i] = _tracked[i].rotation;
                f.scl[i] = _tracked[i].localScale;
            }
        }

        /// <summary>Freeze physics and play the buffered window back at slowMul speed.</summary>
        public void Play(float slowMul)
        {
            if (_count < 2) return;
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
            if (i >= _count - 1) { Stop(); return; }

            float frac = _playCursor - i;
            var a = FrameAt(i);
            var b = FrameAt(i + 1);
            for (int k = 0; k < _tracked.Length; k++)
            {
                if (_tracked[k] == null) continue;
                _tracked[k].position = Vector3.Lerp(a.pos[k], b.pos[k], frac);
                _tracked[k].rotation = Quaternion.Slerp(a.rot[k], b.rot[k], frac);
                _tracked[k].localScale = Vector3.Lerp(a.scl[k], b.scl[k], frac);
            }
        }

        public void Stop()
        {
            if (!_playing) return;
            _playing = false;
            SetKinematic(false);
            SetDrivers(true);
            _head = 0; _count = 0;
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
