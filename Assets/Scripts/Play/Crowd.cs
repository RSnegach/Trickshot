using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A whole animated stadium crowd driven by ONE MonoBehaviour. Every fan is a few
    /// shared-material primitives with NO collider, NO rigidbody, NO per-fan script.
    /// This single driver owns all fans and animates them from flat arrays each frame,
    /// so it stays cheap with thousands of fans.
    ///
    /// HOW THE CALLER DRIVES IT:
    ///   // build once (e.g. from the stadium/setup code):
    ///   var crowd = Crowd.Create();              // or Crowd.Create(stadiumRoot)
    ///   ...
    ///   // on a goal, make the whole crowd cheer harder for a moment:
    ///   crowd.Celebrate();
    ///
    ///   // optionally set a steady mood at any time (0 calm .. 1 wild):
    ///   crowd.Excitement = 0.5f;
    ///
    /// Build() places one fan per PitchLayout seat (see PitchLayout.Seats). It caches
    /// each fan's root Transform, its two arm-pivot Transforms, a base local position
    /// and a random-ish phase. Update() then loops those arrays with a single Mathf.Sin
    /// per fan - no allocation, no LINQ, no GetComponent.
    ///
    /// Assumes the crowd root is unrotated (translation-only parent is fine): fans hop
    /// along world up and their base positions live in local space under this transform.
    /// </summary>
    public sealed class Crowd : MonoBehaviour
    {
        // ---- Public knobs the caller can drive ----

        /// <summary>Steady crowd mood, 0 (calm) .. 1 (wild). Scales hop height and arm
        /// raise. A brief Celebrate() spike is added on top of this.</summary>
        [Range(0f, 1f)] public float Excitement = 0.35f;

        /// <summary>How many fans were actually spawned (after the MaxFans stride cap).</summary>
        public int FanCount => _count;

        // ---- Tuning constants ----

        // Hard cap on spawned fans. If PitchLayout yields more seats than this we skip
        // seats evenly (every Nth) so the crowd still fills the bowl but stays cheap.
        const int MaxFans = 4000;

        const float JumpSpeed  = 4.5f;   // radians/sec base cheer cadence
        const float JumpHeight = 0.35f;  // metres of hop at full excitement
        const float MaxArmDeg  = 150f;   // arm raise at full excitement (deg about local X)

        // Celebrate() spikes an extra excitement term to 1 that decays at this rate/sec.
        const float CelebrateDecayRate = 0.5f;

        // Phase spread: neighbours share a similar phase (a rolling wave) plus jitter so
        // the stands look lively rather than perfectly synced.
        const float WaveSpatial = 0.15f; // phase per metre along the stands (the wave)
        const float WaveJitter  = 1.6f;  // extra random phase per fan

        // ---- Fan proportions (metres, local to a fan whose feet sit at the seat pos) ----
        static readonly Vector3 TorsoSize  = new Vector3(0.45f, 1.05f, 0.28f);
        static readonly Vector3 TorsoLocal = new Vector3(0f, 0.70f, 0f);
        const float HeadDia = 0.32f;
        static readonly Vector3 HeadLocal  = new Vector3(0f, 1.35f, 0f);
        static readonly Vector3 ArmSize    = new Vector3(0.10f, 0.70f, 0.12f);
        const float ShoulderX = 0.285f;   // half torso width + a little
        const float ShoulderY = 1.05f;    // near the top of the torso
        static readonly Vector3 ShoulderLocalL = new Vector3(-ShoulderX, ShoulderY, 0f);
        static readonly Vector3 ShoulderLocalR = new Vector3( ShoulderX, ShoulderY, 0f);
        // Arm box hangs below its pivot so rotating the pivot swings the arm at the shoulder.
        static readonly Vector3 ArmLocal = new Vector3(0f, -ArmSize.y * 0.5f, 0f);

        // ---- Shared materials (6 jerseys + 1 skin = 7 total, reused across every fan) ----
        static readonly Color[] JerseyColors =
        {
            new Color(0.75f, 0.15f, 0.15f), // 0 red
            new Color(0.15f, 0.30f, 0.75f), // 1 blue
            new Color(0.90f, 0.90f, 0.92f), // 2 white
            new Color(0.85f, 0.75f, 0.20f), // 3 yellow
            new Color(0.20f, 0.55f, 0.25f), // 4 green
            new Color(0.85f, 0.45f, 0.15f), // 5 orange
        };
        static readonly Color SkinColor = new Color(0.85f, 0.68f, 0.55f);

        // Which jersey each stand side leans toward, so the stands read as partisan.
        // Indexed by (int)PitchLayout.Side: PlusX, MinusX, AttackEnd, FarEnd.
        static readonly int[] SideHomeJersey = { 0, 1, 3, 4 };
        const float HomeBias = 0.6f; // chance a fan on a side wears that side's colour

        Material[] _jerseyMats;
        Material _skinMat;

        // ---- Per-fan caches (parallel arrays, sized to the spawned fan count) ----
        Transform[] _roots;   // hop this on local Y
        Transform[] _armL;    // shoulder pivots, rotated to raise arms
        Transform[] _armR;
        Vector3[]   _basePos; // each fan's resting local position
        float[]     _phase;   // per-fan cheer phase
        int _count;

        float _celebrate; // decaying extra excitement from Celebrate()
        bool _built;

        /// <summary>Create a "Crowd" GameObject, add this driver, build every fan, and
        /// return the driver. Pass a parent to nest the crowd under (translation only).</summary>
        public static Crowd Create(Transform parent = null)
        {
            var go = new GameObject("Crowd");
            var crowd = go.AddComponent<Crowd>();
            crowd.Build(parent);
            return crowd;
        }

        /// <summary>Build all fans as children of this transform, optionally nesting this
        /// transform under <paramref name="root"/>. Safe to call once. Later calls no-op.</summary>
        public void Build(Transform root)
        {
            if (_built) return;
            if (root != null) transform.SetParent(root, false);
            // Keep the crowd root unrotated at whatever position the parent gives it.

            EnsureMaterials();

            // Pass 1: count seats so we can size arrays and pick an even skip stride.
            int total = 0;
            foreach (var side in PitchLayout.AllSides)
                foreach (var _ in PitchLayout.Seats(side))
                    total++;

            int stride = total > MaxFans ? Mathf.CeilToInt((float)total / MaxFans) : 1;
            if (stride < 1) stride = 1;
            int kept = (total + stride - 1) / stride;

            _roots   = new Transform[kept];
            _armL    = new Transform[kept];
            _armR    = new Transform[kept];
            _basePos = new Vector3[kept];
            _phase   = new float[kept];

            // Pass 2: build the kept fans (every stride-th seat, evenly across the bowl).
            int gi = 0;   // global seat index across all sides
            int w  = 0;   // write index into the caches
            foreach (var side in PitchLayout.AllSides)
            {
                int homeIdx = SideHomeJersey[(int)side];
                foreach (var seat in PitchLayout.Seats(side))
                {
                    if (gi % stride == 0 && w < kept)
                    {
                        Material jersey = PickJersey(homeIdx);
                        BuildFan(seat, jersey, w);
                        w++;
                    }
                    gi++;
                }
            }

            _count = w;
            _built = true;
        }

        /// <summary>Briefly spike the whole crowd toward wild cheering, then let it decay
        /// back to the steady Excitement. Call on goals / big moments.</summary>
        public void Celebrate()
        {
            _celebrate = 1f;
        }

        void EnsureMaterials()
        {
            if (_skinMat != null) return;
            _jerseyMats = new Material[JerseyColors.Length];
            for (int i = 0; i < JerseyColors.Length; i++)
            {
                var m = Make.Mat(JerseyColors[i], 0.15f);
                m.enableInstancing = true; // thousands of same-mesh renderers batch well
                _jerseyMats[i] = m;
            }
            _skinMat = Make.Mat(SkinColor, 0.1f);
            _skinMat.enableInstancing = true;
        }

        Material PickJersey(int homeIdx)
        {
            if (Random.value < HomeBias) return _jerseyMats[homeIdx];
            return _jerseyMats[Random.Range(0, _jerseyMats.Length)];
        }

        void BuildFan(in PitchLayout.Seat seat, Material jersey, int w)
        {
            Transform fanRoot = Make.Empty("Fan", seat.pos, transform).transform;
            fanRoot.rotation = seat.facing; // set before building parts so TransformPoint is correct

            // Torso: random jersey colour, no collider.
            Make.Box("Torso", TorsoSize, fanRoot.TransformPoint(TorsoLocal), jersey, fanRoot, false);

            // Head: skin sphere. Make.Sphere leaves a SphereCollider on the primitive, so
            // strip it - fans are purely visual.
            var head = Make.Sphere("Head", HeadDia, fanRoot.TransformPoint(HeadLocal), _skinMat, fanRoot);
            var hc = head.GetComponent<Collider>();
            if (hc != null) Destroy(hc);

            // Arms: skin boxes hanging from shoulder pivots we can rotate.
            Transform pivL = BuildArm("ArmL", fanRoot, ShoulderLocalL);
            Transform pivR = BuildArm("ArmR", fanRoot, ShoulderLocalR);

            _roots[w]   = fanRoot;
            _armL[w]    = pivL;
            _armR[w]    = pivR;
            _basePos[w] = fanRoot.localPosition;
            _phase[w]   = (seat.pos.x + seat.pos.z) * WaveSpatial + Random.value * WaveJitter;
        }

        Transform BuildArm(string name, Transform fanRoot, Vector3 shoulderLocal)
        {
            // Pivot sits at the shoulder, inheriting the fan's facing (local rotation identity).
            Transform pivot = Make.Empty(name + "Pivot", fanRoot.TransformPoint(shoulderLocal), fanRoot).transform;
            // Arm box hangs below the pivot; rotating the pivot swings it at the shoulder.
            Make.Box(name, ArmSize, pivot.TransformPoint(ArmLocal), _skinMat, pivot, false);
            return pivot;
        }

        void Update()
        {
            if (!_built || _count == 0) return;

            // Decay the celebrate spike, then fold it into the steady mood (once per frame).
            _celebrate = Mathf.MoveTowards(_celebrate, 0f, Time.deltaTime * CelebrateDecayRate);
            float exc = Mathf.Clamp01(Excitement + _celebrate);
            float time = Time.time;

            float hopScale = JumpHeight * exc;
            float armScale = MaxArmDeg * exc;

            for (int i = 0; i < _count; i++)
            {
                float s = Mathf.Sin(time * JumpSpeed + _phase[i]);
                float up = s > 0f ? s : 0f; // rectified: pop up and settle back to the tread

                Vector3 p = _basePos[i];
                p.y += up * hopScale;
                _roots[i].localPosition = p;

                Quaternion aq = Quaternion.Euler(up * armScale, 0f, 0f);
                _armL[i].localRotation = aq;
                _armR[i].localRotation = aq;
            }
        }
    }
}
