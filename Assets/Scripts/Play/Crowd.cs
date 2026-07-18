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
    ///   var crowd = Crowd.Create();              // or Crowd.Create(stadiumRoot); build once
    ///   crowd.Celebrate();                        // harmless no-op (fans are static)
    ///
    /// Build() places one fan per PitchLayout seat (see PitchLayout.Seats), capped by the
    /// selected venue's MaxFans via an even skip stride, and colored from that venue's
    /// jersey palette. Fans are STATIC - built once in a fixed raised-arm cheer, with no
    /// per-frame update, no colliders, and no per-fan scripts.
    ///
    /// Assumes the crowd root is unrotated (translation-only parent is fine).
    /// </summary>
    public sealed class Crowd : MonoBehaviour
    {
        /// <summary>How many fans were actually spawned (after the MaxFans stride cap).</summary>
        public int FanCount => _count;

        // ---- Tuning constants ----
        // Fans are STATIC (no per-frame animation) for a clean look + zero runtime cost.
        // They are posed once at build with arms raised in a fixed cheer.
        const float ArmRaiseDeg = 120f;  // fixed arm-raise pose (deg about local X)

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

        // ---- Palette (from the selected venue; falls back to a default bright kit) ----
        static readonly Color[] DefaultJerseys =
        {
            new Color(0.75f, 0.15f, 0.15f), // 0 red
            new Color(0.15f, 0.30f, 0.75f), // 1 blue
            new Color(0.90f, 0.90f, 0.92f), // 2 white
            new Color(0.85f, 0.75f, 0.20f), // 3 yellow
            new Color(0.20f, 0.55f, 0.25f), // 4 green
            new Color(0.85f, 0.45f, 0.15f), // 5 orange
        };
        static readonly int[] DefaultSideHome = { 0, 1, 3, 4 };
        static readonly Color SkinColor = new Color(0.85f, 0.68f, 0.55f);
        const float HomeBias = 0.6f; // chance a fan on a side wears that side's colour

        Color[] _jerseyColors;
        int[]   _sideHome;
        Material[] _jerseyMats;
        Material _skinMat;
        int _count;
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

            var style = StadiumStyle.Active;
            _jerseyColors = (style.Jerseys != null && style.Jerseys.Length > 0) ? style.Jerseys : DefaultJerseys;
            _sideHome = (style.SideHomeJersey != null && style.SideHomeJersey.Length >= 4) ? style.SideHomeJersey : DefaultSideHome;
            int maxFans = Mathf.Max(1, style.MaxFans);
            EnsureMaterials();

            // Pass 1: count seats so we can pick an even skip stride for the fan cap.
            int total = 0;
            foreach (var side in PitchLayout.AllSides)
                foreach (var _ in PitchLayout.Seats(side))
                    total++;

            int stride = total > maxFans ? Mathf.CeilToInt((float)total / maxFans) : 1;
            if (stride < 1) stride = 1;

            // Pass 2: build the kept fans (every stride-th seat, evenly across the bowl).
            // Fans are static so there is nothing to cache - build and forget.
            int gi = 0, w = 0;
            foreach (var side in PitchLayout.AllSides)
            {
                int homeIdx = _sideHome[(int)side];
                foreach (var seat in PitchLayout.Seats(side))
                {
                    if (gi % stride == 0) { BuildFan(seat, PickJersey(homeIdx)); w++; }
                    gi++;
                }
            }

            _count = w;
            _built = true;
        }

        /// <summary>No-op: fans are static. Kept so goal callouts can call it harmlessly.</summary>
        public void Celebrate() { }

        void EnsureMaterials()
        {
            _jerseyMats = new Material[_jerseyColors.Length];
            for (int i = 0; i < _jerseyColors.Length; i++)
            {
                var m = Make.Mat(_jerseyColors[i], 0.15f);
                m.enableInstancing = true; // thousands of same-mesh renderers batch well
                _jerseyMats[i] = m;
            }
            _skinMat = Make.Mat(SkinColor, 0.1f);
            _skinMat.enableInstancing = true;
        }

        Material PickJersey(int homeIdx)
        {
            if (Random.value < HomeBias) return _jerseyMats[homeIdx % _jerseyMats.Length];
            return _jerseyMats[Random.Range(0, _jerseyMats.Length)];
        }

        void BuildFan(in PitchLayout.Seat seat, Material jersey)
        {
            Transform fanRoot = Make.Empty("Fan", seat.pos, transform).transform;
            fanRoot.rotation = seat.facing; // set before building parts so TransformPoint is correct

            // Torso: jersey colour, no collider.
            Make.Box("Torso", TorsoSize, fanRoot.TransformPoint(TorsoLocal), jersey, fanRoot, false);

            // Head: skin sphere; strip the primitive collider (fans are purely visual).
            var head = Make.Sphere("Head", HeadDia, fanRoot.TransformPoint(HeadLocal), _skinMat, fanRoot);
            var hc = head.GetComponent<Collider>();
            if (hc != null) Destroy(hc);

            // Arms: skin boxes on shoulder pivots, posed once in a fixed raised cheer.
            BuildArm(fanRoot, ShoulderLocalL);
            BuildArm(fanRoot, ShoulderLocalR);
        }

        void BuildArm(Transform fanRoot, Vector3 shoulderLocal)
        {
            Transform pivot = Make.Empty("ArmPivot", fanRoot.TransformPoint(shoulderLocal), fanRoot).transform;
            pivot.localRotation = Quaternion.identity;
            Make.Box("Arm", ArmSize, pivot.TransformPoint(ArmLocal), _skinMat, pivot, false);
            // Fixed raised-arm cheer pose (no animation).
            pivot.localRotation = Quaternion.Euler(ArmRaiseDeg, 0f, 0f);
        }
    }
}
