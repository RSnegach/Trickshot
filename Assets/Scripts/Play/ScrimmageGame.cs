using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Full scrimmage match driver: two teams of outfielders + a keeper each, on a
    /// two-goal pitch walled all round. The human controls one player; the rest are AI.
    ///
    ///  - Outfield role: the human controls the Home teammate NEAREST the ball (FIFA-style
    ///    auto-switch, plus F to switch manually). The controlled player uses the normal
    ///    Striker control scheme (WASD + mouse + dribble + shoot); Q ground-passes and E
    ///    lofts a pass to the teammate nearest the aim.
    ///  - Keeper role: the human controls the Home keeper (KeeperController) the whole
    ///    match; every outfielder is AI.
    ///
    /// Scoring: geometric per-frame test at each goal (like FreeplayGame). Ball into the
    /// +Z goal = Away scores; into the -Z goal = Home scores. Reset to kickoff after a goal.
    /// </summary>
    public class ScrimmageGame : MonoBehaviour
    {
        GameInput _input;
        BallController _ball;
        GameCamera _cam;

        // Arena.
        public float HalfLength, HalfWidth;
        public Vector3 HomeGoal, AwayGoal;   // +Z and -Z goal centres

        // Roster.
        readonly List<Footballer> _all = new List<Footballer>();
        readonly List<Footballer> _home = new List<Footballer>();
        readonly List<Footballer> _away = new List<Footballer>();
        Footballer _homeKeeper, _awayKeeper;

        // Human control.
        SimConfig.ScrimRole _role;
        Striker _humanStriker;            // outfield role: the striker component on the controlled body
        Dribble _humanDribble;
        Footballer _controlled;           // which footballer the human drives (outfield role)
        KeeperController _humanKeeper;    // keeper role
        ActiveRagdoll _humanKeeperRagdoll;
        float _switchLock;

        // Which team last touched the ball (for AI support/defend logic).
        public int PossessionTeam { get; private set; } = 0;

        int _homeScore, _awayScore;
        string _flash = "";
        float _flashTime;
        bool _resolved;                   // goal handled this dead-ball
        float _kickoffTimer;

        public void Configure(GameInput input, BallController ball, GameCamera cam,
                              ScrimmageArena.Refs arena, SimConfig.ScrimRole role,
                              List<Footballer> home, List<Footballer> away,
                              Footballer homeKeeper, Footballer awayKeeper,
                              Striker humanStriker, Dribble humanDribble,
                              KeeperController humanKeeper, ActiveRagdoll humanKeeperRagdoll)
        {
            _input = input; _ball = ball; _cam = cam;
            HalfLength = arena.halfLength; HalfWidth = arena.halfWidth;
            HomeGoal = arena.homeGoalCenter; AwayGoal = arena.awayGoalCenter;
            _role = role;
            _home.AddRange(home); _away.AddRange(away);
            _homeKeeper = homeKeeper; _awayKeeper = awayKeeper;
            _humanStriker = humanStriker; _humanDribble = humanDribble;
            _humanKeeper = humanKeeper; _humanKeeperRagdoll = humanKeeperRagdoll;

            _all.AddRange(home); _all.AddRange(away);
            if (homeKeeper != null) _all.Add(homeKeeper);
            if (awayKeeper != null) _all.Add(awayKeeper);

            Kickoff();
        }

        // ------------------------------------------------------------- lifecycle
        void Kickoff()
        {
            foreach (var f in _all) if (f != null) f.ResetTo(f == _homeKeeper || f == _awayKeeper ? KeeperSpot(f) : SpawnSpot(f));
            if (_humanKeeper != null && _humanKeeperRagdoll != null)
            {
                _humanKeeper.ForceRecover();
                _humanKeeperRagdoll.ResetTo(new Vector3(0f, 0f, HomeGoal.z - 1.0f),
                                            Quaternion.LookRotation(new Vector3(0f, 0f, -1f), Vector3.up));
            }
            _ball.ResetTo(new Vector3(0f, SimConfig.ScrimKickoffBallHeight, 0f));
            _resolved = false;
            _kickoffTimer = 0.5f;

            // In outfield role, hand control to the Home player nearest the ball.
            if (_role == SimConfig.ScrimRole.Outfield) SwitchTo(NearestHomeOutfielderToBall());
        }

        Vector3 SpawnSpot(Footballer f)
        {
            // Simple formation: spread across the team's own half, a little back from centre.
            var list = f.Team == 0 ? _home : _away;
            int idx = list.IndexOf(f);
            int n = Mathf.Max(1, list.Count);
            float x = Mathf.Lerp(-HalfWidth * 0.6f, HalfWidth * 0.6f, n == 1 ? 0.5f : idx / (float)(n - 1));
            float z = f.Team == 0 ? -HalfLength * 0.35f : HalfLength * 0.35f;   // own half
            return new Vector3(x, 0f, z);
        }

        Vector3 KeeperSpot(Footballer f)
        {
            // Home attacks +Z (HomeGoal) so DEFENDS the -Z goal (AwayGoal); Away is the
            // mirror. Keeper stands 1m in front of its own line, toward the pitch.
            float z = f.Team == 0 ? AwayGoal.z + 1.0f : HomeGoal.z - 1.0f;
            return new Vector3(0f, 0f, z);
        }

        // ------------------------------------------------------------- update
        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;
            if (_input.ResetPressed) { Kickoff(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();
            if (_kickoffTimer > 0f) _kickoffTimer -= Time.deltaTime;

            UpdatePossession();

            // --- Human control ---
            if (_role == SimConfig.ScrimRole.Keeper)
            {
                if (_humanKeeper != null) _humanKeeper.Tick();
            }
            else
            {
                if (_input.SwitchPressed && _switchLock <= 0f)
                    SwitchTo(NearestHomeOutfielderToBall(exclude: _controlled));
                if (_switchLock > 0f) _switchLock -= Time.deltaTime;

                if (_humanStriker != null) _humanStriker.Tick();

                // Passing (only meaningful when controlling an outfielder).
                if (_input.PassGroundPressed) TryPass(lofted: false);
                else if (_input.PassLoftedPressed) TryPass(lofted: true);

                // Auto-switch to whoever's now nearest the ball (defense especially), unless
                // the human's current player is carrying / very close to the ball.
                MaybeAutoSwitch();
            }

            // --- AI: every footballer that isn't the human-controlled one ---
            // In keeper role the human drives a separate KeeperController ragdoll (not a
            // Footballer), so the Home keeper Footballer is suppressed to avoid two keepers.
            Footballer humanBody = _role == SimConfig.ScrimRole.Outfield ? _controlled : null;
            var homeClosest = ClosestToBall(_home);
            var awayClosest = ClosestToBall(_away);
            foreach (var f in _all)
            {
                if (f == null || f == humanBody) continue;
                if (_role == SimConfig.ScrimRole.Keeper && f == _homeKeeper) continue;
                if (f.IsKeeper) { f.AiKeeperTick(); continue; }
                bool isClosest = f == (f.Team == 0 ? homeClosest : awayClosest);
                f.AiTick(isClosest);
            }

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;

            TrackGoals();
        }

        // Teammates of a given team (for AI spacing). Read-only view.
        public List<Footballer> TeamList(int team) => team == 0 ? _home : _away;

        void UpdatePossession()
        {
            // Nearest outfielder of either team to the ball claims possession for AI logic.
            Footballer h = ClosestToBall(_home), a = ClosestToBall(_away);
            if (h == null && a == null) return;
            Vector3 b = _ball.transform.position;
            float dh = h != null ? Vector3.Distance(h.Pos, b) : 999f;
            float da = a != null ? Vector3.Distance(a.Pos, b) : 999f;
            PossessionTeam = dh <= da ? 0 : 1;
        }

        // ------------------------------------------------------------- switching
        void MaybeAutoSwitch()
        {
            if (_switchLock > 0f) return;
            var nearest = NearestHomeOutfielderToBall();
            if (nearest == null || nearest == _controlled) return;
            // Only auto-switch when the human's player is clearly not involved (ball far),
            // so it doesn't yank control mid-dribble.
            float ctrlDist = _controlled != null ? Vector3.Distance(_controlled.Pos, _ball.transform.position) : 999f;
            if (ctrlDist > 6f) SwitchTo(nearest);
        }

        void SwitchTo(Footballer f)
        {
            if (f == null || f == _controlled) return;

            // Detach the old body: its Striker/Dribble go dormant (AI takes it over).
            if (_controlled != null)
            {
                var s = _controlled.GetComponent<Striker>();
                if (s != null) s.ControlEnabled = false;
                var d = _controlled.GetComponent<Dribble>();
                if (d != null) d.Enabled = false;
            }

            _controlled = f;
            _switchLock = SimConfig.SwitchLockout;

            _humanStriker = f.GetComponent<Striker>();
            _humanDribble = f.GetComponent<Dribble>();
            if (_humanStriker != null) _humanStriker.ControlEnabled = true;
            if (_humanDribble != null) _humanDribble.Enabled = true;

            // Camera + look follow the new body.
            _cam.SetFollow(f.Ragdoll.Pelvis.transform, () => _input.Look);
            if (_humanStriker != null) _humanStriker.SetCameraYaw(() => _cam.Yaw);
        }

        Footballer NearestHomeOutfielderToBall(Footballer exclude = null)
        {
            Footballer best = null; float bestD = float.MaxValue;
            Vector3 b = _ball.transform.position;
            foreach (var f in _home)
            {
                if (f == null || f == exclude || f.IsKeeper) continue;
                float d = Vector3.Distance(f.Pos, b);
                if (d < bestD) { bestD = d; best = f; }
            }
            return best;
        }

        Footballer ClosestToBall(List<Footballer> team)
        {
            Footballer best = null; float bestD = float.MaxValue;
            Vector3 b = _ball.transform.position;
            foreach (var f in team)
            {
                if (f == null || f.IsKeeper) continue;
                float d = Vector3.Distance(f.Pos, b);
                if (d < bestD) { bestD = d; best = f; }
            }
            return best;
        }

        // ------------------------------------------------------------- passing
        // Pass to the Home teammate nearest the controlled player's aim direction. Ground =
        // rolled, lofted = chipped. Power scales with the controlled player's shot power.
        void TryPass(bool lofted)
        {
            if (_controlled == null || _humanStriker == null) return;
            Vector3 from = _ball.transform.position;
            Vector3 aim = _humanStriker.FacingForward;

            Footballer target = BestPassTarget(from, aim);
            Vector3 dir;
            if (target != null)
            {
                Vector3 lead = target.Ragdoll.Pelvis.linearVelocity * SimConfig.PassLeadFrac;
                Vector3 to = (target.Pos + lead) - from; to.y = 0f;
                dir = to.sqrMagnitude > 0.01f ? to.normalized : aim;
            }
            else dir = aim;   // no teammate in the cone: just knock it where you aim

            float power = (lofted ? SimConfig.PassLoftedSpeed : SimConfig.PassGroundSpeed) * PlayerProfile.ShotPowerMul;
            Vector3 v = dir * power;
            if (lofted) v += Vector3.up * (power * SimConfig.PassLoftedArc);
            _ball.KickTo(v);
            if (_humanDribble != null) _humanDribble.ForceRelease();
            Flash(lofted ? "LOFTED PASS" : "PASS");
        }

        Footballer BestPassTarget(Vector3 from, Vector3 aim)
        {
            Footballer best = null; float bestScore = -1f;
            foreach (var f in _home)
            {
                if (f == null || f == _controlled) continue;
                Vector3 to = f.Pos - from; to.y = 0f;
                float dist = to.magnitude;
                if (dist < 1.5f || dist > SimConfig.PassMaxRange) continue;
                float dot = Vector3.Dot(aim, to.normalized);
                if (dot < SimConfig.PassAimConeDot) continue;
                // Prefer teammates most in-line with the aim, then closer.
                float score = dot - dist * 0.01f;
                if (score > bestScore) { bestScore = score; best = f; }
            }
            return best;
        }

        // ------------------------------------------------------------- scoring
        void TrackGoals()
        {
            if (_resolved || _kickoffTimer > 0f) return;
            Vector3 c = _ball.transform.position;
            // HomeGoal (+Z) is the goal Home ATTACKS, so a ball into it = HOME scores.
            // AwayGoal (-Z) is the goal Away attacks -> AWAY scores.
            if (BallInGoal(c, HomeGoal, +1f)) { OnGoal(awayScored: false); return; }
            if (BallInGoal(c, AwayGoal, -1f)) { OnGoal(awayScored: true); return; }
        }

        // Whole ball past the line, within posts/bar, inside the goal depth. dirSign = the
        // world-Z direction INTO the goal from the pitch (+1 for the +Z goal).
        bool BallInGoal(Vector3 c, Vector3 goal, float dirSign)
        {
            float r = SimConfig.BallRadius;
            float halfW = SimConfig.GoalWidth * 0.5f;
            float rel = (c.z - goal.z) * dirSign;               // + = past the line into the goal
            return rel - r >= 0f
                   && rel <= SimConfig.GoalDepth
                   && Mathf.Abs(c.x) <= halfW - r
                   && c.y >= r
                   && c.y <= SimConfig.GoalHeight - r;
        }

        void OnGoal(bool awayScored)
        {
            _resolved = true;
            if (awayScored) { _awayScore++; Flash("AWAY SCORES!"); }
            else { _homeScore++; Flash("GOAL!  HOME SCORES!"); CrowdCheer.Celebrate(); }
            _kickoffTimer = 1.5f;
            Invoke(nameof(Kickoff), 1.6f);
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // ------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            var score = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            var big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };

            GUI.Label(new Rect(0, 10, Screen.width, 34), $"HOME  {_homeScore} - {_awayScore}  AWAY", score);

            string help = _role == SimConfig.ScrimRole.Keeper
                ? "Keeper:  A/D move   Space/LMB/RMB dive   Reset: R"
                : "Move WASD   Camera Mouse   Shoot LMB/RMB   Q pass   E lofted pass   F switch   V ball cam   R reset";
            GUI.Label(new Rect(8, Screen.height - 26, Screen.width - 16, 22), help, st);

            if (_flashTime > 0f)
            {
                var c = big.normal.textColor; c.a = Mathf.Clamp01(_flashTime / 1.6f); big.normal.textColor = c;
                GUI.Label(new Rect(0, 60, Screen.width, 44), _flash, big);
            }
        }
    }
}
