using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The pop-up TARGET BOARD hung across the goal mouth for accuracy modes: a fixed pool of
    /// AccuracyTargets that spawn at random non-overlapping spots, score their points when the
    /// ball passes through, then re-pop a fresh one a beat later - so there are always
    /// `count` targets up.
    ///
    /// Extracted from AccuracyGame so the single-player driver (AccuracyGame) and the networked
    /// driver (NetAccuracyMatch) share identical target behaviour. Plain class, not a
    /// MonoBehaviour: the owner builds it under a parent transform and pumps Tick() each frame.
    ///
    /// Placement is driven by a private LCG seeded by the owner, so a networked host can seed
    /// from the match config and reproduce the same layout sequence.
    /// </summary>
    public class AccuracyBoard
    {
        // Raised when a target is struck: (points, targetIndex). The owner adds the points to
        // whichever score it keeps (a session score, or a per-slot shootout tally).
        public event System.Action<int, int> Scored;

        Transform _container;
        AccuracyTarget[] _targets;
        float[] _respawn;        // per-slot countdown after a hit before a fresh pop
        int _count;
        uint _seed;

        const float RespawnDelay = 0.6f;
        const float MinSeparation = 0.35f;   // extra gap between target rims

        public int Count => _count;
        public AccuracyTarget At(int i) => (_targets != null && i >= 0 && i < _count) ? _targets[i] : null;

        /// <summary>Create the pool under `parent`. `seed` drives the placement LCG (0 -> clock).</summary>
        public void Build(Transform parent, int count, uint seed)
        {
            _count = Mathf.Max(1, count);
            _seed = seed | 1u;   // LCG needs a non-zero odd-ish seed
            _container = Make.Empty("AccuracyTargets", Vector3.zero, parent).transform;
            _targets = new AccuracyTarget[_count];
            _respawn = new float[_count];
            for (int i = 0; i < _count; i++)
            {
                var go = new GameObject("Target" + i);
                go.transform.SetParent(_container, false);
                var t = go.AddComponent<AccuracyTarget>();
                t.OnHit += HandleHit;
                _targets[i] = t;
            }
        }

        /// <summary>
        /// Single PATROLLING target for a strikes-mode round: slot 0 only, at `radius`, launched
        /// from a random spot on the goal face at `speed` on a random heading. Any other slots are
        /// hidden, so the same pool serves both this and the multi-target gallery.
        ///
        /// The heading is kept away from the horizontal and the vertical (a target sliding exactly
        /// along one axis never uses the other, which makes a round far easier than its tier says),
        /// and the start spot is inset by the radius so the disc opens fully inside the frame.
        /// </summary>
        public void SpawnPatrol(float radius, float speed)
        {
            if (_targets == null || _count < 1) return;
            for (int i = 1; i < _count; i++) { _targets[i].Hide(); _respawn[i] = 0f; }

            var b = AccuracyTarget.PatrolBounds(radius);
            var pos = new Vector3(Mathf.Lerp(b.xMin, b.xMax, Rand()),
                                  Mathf.Lerp(b.yMin, b.yMax, Rand()),
                                  SimConfig.GoalCenter.z);

            _respawn[0] = 0f;
            _targets[0].Spawn(pos, radius, PatrolColor, 1);
            // One of the four diagonal quadrants, jittered: never within 20 degrees of an axis.
            float dir = 90f * Mathf.Floor(Rand() * 4f) + Mathf.Lerp(20f, 70f, Rand());
            _targets[0].SetDrift(speed, dir);
        }

        /// <summary>The strikes-mode target's colour. One target, one value, so it does not need
        /// the gallery's white/yellow/red value tiers - it reads as "the" target.</summary>
        static readonly Color PatrolColor = new Color(1f, 0.85f, 0.1f);

        /// <summary>Pop every slot fresh (round start).</summary>
        public void SpawnAll()
        {
            if (_targets == null) return;
            for (int i = 0; i < _count; i++) { _respawn[i] = 0f; SpawnAt(i); }
        }

        /// <summary>Hide every target's DISC but keep its trigger - see AccuracyTarget
        /// .SetVisualHidden. Sticky across respawns, so it is set once when the board is built.</summary>
        public void SetVisualHidden(bool hidden)
        {
            if (_targets == null) return;
            for (int i = 0; i < _count; i++) _targets[i].SetVisualHidden(hidden);
        }

        /// <summary>Hide every target (round over / between turns).</summary>
        public void HideAll()
        {
            if (_targets == null) return;
            for (int i = 0; i < _count; i++) _targets[i].Hide();
        }

        /// <summary>Re-pop hit targets once their delay elapses. Call every frame while live.</summary>
        public void Tick(float dt)
        {
            if (_targets == null) return;
            for (int i = 0; i < _count; i++)
            {
                if (!_targets[i].Hit) continue;
                _respawn[i] -= dt;
                if (_respawn[i] <= 0f) SpawnAt(i);
            }
        }

        /// <summary>Targets currently up and unhit (HUD readout).</summary>
        public int ActiveCount()
        {
            if (_targets == null) return 0;
            int n = 0;
            for (int i = 0; i < _count; i++)
                if (_targets[i].Shown && !_targets[i].Hit) n++;
            return n;
        }

        public void Teardown()
        {
            if (_container != null) Object.Destroy(_container.gameObject);
            _container = null; _targets = null; _respawn = null; _count = 0;
        }

        // The target notifies us through its OnHit action: schedule its re-pop and tell the owner
        // how many points it was worth.
        void HandleHit(AccuracyTarget t)
        {
            int i = System.Array.IndexOf(_targets, t);
            if (i >= 0) _respawn[i] = RespawnDelay;
            Scored?.Invoke(t.Points, i);
        }

        // ------------------------------------------------------------- spawning
        // Pick a value tier, then a spot in the goal mouth. Higher tiers are smaller, worth more,
        // and pushed toward the corners. Rejection-sample so targets currently up don't overlap.
        void SpawnAt(int index)
        {
            float roll = Rand();
            float radius, edgeBias;
            int points;
            Color color;
            if (roll < 0.5f)       { radius = 0.55f; points = 1; edgeBias = 0.15f; color = new Color(1f, 1f, 1f); }
            else if (roll < 0.83f) { radius = 0.42f; points = 2; edgeBias = 0.5f;  color = new Color(1f, 0.85f, 0.1f); }
            else                   { radius = 0.3f;  points = 3; edgeBias = 0.85f; color = new Color(1f, 0.24f, 0.16f); }

            // Shrink targets when the goal is smaller than default so they still fit and don't
            // overlap (min goal size collapsed the placement band otherwise).
            float goalScale = Mathf.Min(SimConfig.GoalWidth / 7.32f, SimConfig.GoalHeight / 2.44f);
            radius *= Mathf.Clamp(goalScale, 0.55f, 1f);

            Vector3 pos = Vector3.zero;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                pos = RandomSpot(radius, edgeBias);
                if (!OverlapsOther(index, pos, radius)) break;
            }

            _respawn[index] = 0f;
            _targets[index].Spawn(pos, radius, color, points);
        }

        Vector3 RandomSpot(float radius, float edgeBias)
        {
            float halfW = SimConfig.GoalWidth * 0.5f;
            float xMax = Mathf.Max(0.1f, halfW - radius - 0.15f);
            float yMin = radius + 0.2f;
            float yMax = Mathf.Max(yMin + 0.1f, SimConfig.GoalHeight - radius - 0.15f);

            // -1..1 across, biased toward a post for high tiers.
            float ux = Rand() * 2f - 1f;
            ux = Mathf.Sign(ux == 0f ? 1f : ux) * Mathf.Lerp(Mathf.Abs(ux), 1f, edgeBias);

            // 0..1 up, biased toward the bar or the ground (a corner) for high tiers.
            float uy = Rand();
            float toward = Rand() < 0.5f ? 0f : 1f;
            uy = Mathf.Lerp(uy, toward, edgeBias);

            float x = ux * xMax;
            float y = Mathf.Lerp(yMin, yMax, uy);
            return new Vector3(x, y, SimConfig.GoalCenter.z);
        }

        bool OverlapsOther(int index, Vector3 pos, float radius)
        {
            for (int i = 0; i < _count; i++)
            {
                if (i == index) continue;
                var o = _targets[i];
                if (!o.Shown || o.Hit) continue;   // hidden / waiting-to-respawn don't block
                float minDist = radius + o.Radius + MinSeparation;
                Vector3 d = o.Center - pos; d.z = 0f;
                if (d.sqrMagnitude < minDist * minDist) return true;
            }
            return false;
        }

        // Small LCG (same family as ShotServer) so layout doesn't lean on UnityEngine.Random's
        // global state - and so a seeded host reproduces the same sequence.
        float Rand()
        {
            _seed = _seed * 1664525u + 1013904223u;
            return (_seed >> 8) / 16777216f;   // top 24 bits -> [0,1)
        }
    }
}
