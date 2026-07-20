using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// ACCURACY challenge mode. The crosser self-loops serving balls to the player, who
    /// heads / kicks them at coloured targets popped up across the goal mouth. Hitting a
    /// target scores its points, hides it, and pops a fresh one at a new random spot after
    /// a short delay, so there are always AccuracyTargetCount targets up.
    ///
    /// The round is timed (SimConfig.AccuracySeconds). On time-out the round freezes and a
    /// FINISHED banner shows the final score; R restarts. Pause (Esc) is respected.
    ///
    /// Only the striker is player-controlled, exactly as in striker mode: the camera
    /// follows the pelvis on the mouse, and the striker turns to the camera yaw.
    /// </summary>
    public class AccuracyGame : MonoBehaviour
    {
        GameInput _input;
        Crosser _crosser;
        AimReticle _reticle;
        BallController _ball;
        Striker _striker;
        ActiveRagdoll _strikerRagdoll;
        GameCamera _cam;
        Transform _launchPoint;

        Transform _container;
        AccuracyTarget[] _targets;
        float[] _respawn;       // per-slot countdown after a hit before a fresh pop
        int _count;

        int _score;
        float _timeLeft;
        bool _finished;

        string _flash = "";
        float _flashTime;

        uint _seed;
        const float RespawnDelay = 0.6f;
        const float MinSeparation = 0.35f;   // extra gap between target rims

        public void Configure(GameInput input, Crosser crosser, AimReticle reticle, BallController ball,
                              Striker striker, ActiveRagdoll strikerRagdoll, GameCamera cam, Transform launchPoint)
        {
            _input = input;
            _crosser = crosser;
            _reticle = reticle;
            _ball = ball;
            _striker = striker;
            _strikerRagdoll = strikerRagdoll;
            _cam = cam;
            _launchPoint = launchPoint;

            _seed = (uint)System.Environment.TickCount | 1u;

            // Camera follows the pelvis on the mouse; the striker turns to the camera yaw
            // (Minecraft third person), matching striker mode.
            _cam.SetFollow(_strikerRagdoll.Pelvis.transform, () => _input.Look);
            _striker.SetCameraYaw(() => _cam.Yaw);
            _cam.SetMode(GameCamera.Mode.Follow);

            _count = Mathf.Max(1, SimConfig.AccuracyTargetCount);
            BuildTargets();
            BeginRound();
        }

        void BuildTargets()
        {
            _container = Make.Empty("AccuracyTargets", Vector3.zero, transform).transform;
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

        void BeginRound()
        {
            _score = 0;
            _timeLeft = SimConfig.AccuracySeconds;
            _finished = false;
            _flash = "";
            _flashTime = 0f;

            _crosser.Arm(SimConfig.ServeFirstDelay);
            for (int i = 0; i < _count; i++)
            {
                _respawn[i] = 0f;
                SpawnAt(i);
            }
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;

            if (_input.ResetPressed) { BeginRound(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();

            if (_finished)
            {
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            // Player + auto-server both keep running.
            _striker.Tick();
            _crosser.Tick();

            // Re-pop hit targets a beat after they were struck.
            for (int i = 0; i < _count; i++)
            {
                if (_targets[i].Hit)
                {
                    _respawn[i] -= Time.deltaTime;
                    if (_respawn[i] <= 0f) SpawnAt(i);
                }
            }

            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f) EndRound();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        void HandleHit(AccuracyTarget t)
        {
            if (_finished) return;
            _score += t.Points;
            int i = System.Array.IndexOf(_targets, t);
            if (i >= 0) _respawn[i] = RespawnDelay;
            Flash("+" + t.Points);
        }

        void EndRound()
        {
            _timeLeft = 0f;
            _finished = true;
            for (int i = 0; i < _count; i++) _targets[i].Hide();
        }

        // ------------------------------------------------------------- spawning
        // Pick a value tier, then a spot in the goal mouth. Higher tiers are smaller,
        // worth more, and pushed toward the corners. Rejection-sample so targets that are
        // currently up don't overlap.
        void SpawnAt(int index)
        {
            float roll = Rand();
            float radius, edgeBias;
            int points;
            Color color;
            if (roll < 0.5f)      { radius = 0.55f; points = 1; edgeBias = 0.15f; color = new Color(1f, 1f, 1f); }
            else if (roll < 0.83f) { radius = 0.42f; points = 2; edgeBias = 0.5f;  color = new Color(1f, 0.85f, 0.1f); }
            else                   { radius = 0.3f;  points = 3; edgeBias = 0.85f; color = new Color(1f, 0.24f, 0.16f); }

            // Shrink targets when the goal is smaller than default so they still fit and
            // don't overlap (min goal size collapsed the placement band otherwise).
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

        int ActiveTargets()
        {
            int n = 0;
            for (int i = 0; i < _count; i++)
                if (_targets[i].Shown && !_targets[i].Hit) n++;
            return n;
        }

        // Small LCG (same family as ShotServer) so we don't lean on UnityEngine.Random's
        // global state for layout.
        float Rand()
        {
            _seed = _seed * 1664525u + 1013904223u;
            return (_seed >> 8) / 16777216f;   // top 24 bits -> [0,1)
        }

        void Flash(string s) { _flash = s; _flashTime = 1.2f; }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            Hud.Begin();

            var p = Hud.PanelStart("ACCURACY", 2);
            Hud.Stat(ref p, "Score", _score.ToString());
            Hud.Stat(ref p, "Targets up", ActiveTargets().ToString());

            Hud.Clock(_timeLeft, urgent: !_finished && _timeLeft <= 10f);
            Hud.Legend("WASD move   Mouse aim   LMB/RMB legs   Space jump   Wheel air-pitch   V ball cam   R reset");

            if (_finished)
            {
                Hud.Banner("FINISHED!", "Score: " + _score, "Press R to play again");
                return;
            }
            if (!_finished) Hud.Flash(_flash, _flashTime / 1.2f);
        }
    }
}
