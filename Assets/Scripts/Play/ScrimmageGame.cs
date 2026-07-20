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
        Celebration _controlledCeleb;     // emote component on the controlled body
        Footballer _controlled;           // which footballer the human drives (outfield role)

        // Emote wheel state.
        bool _wheelOpen, _wheelWasOpen;
        int _wheelSel = -1;               // hovered slice index, -1 = none
        KeeperController _humanKeeper;    // keeper role
        ActiveRagdoll _humanKeeperRagdoll;

        // Which team last touched the ball (for AI support/defend logic).
        public int PossessionTeam { get; private set; } = 0;

        int _homeScore, _awayScore;
        string _flash = "";
        float _flashTime;
        bool _resolved;                   // goal handled this dead-ball
        float _kickoffTimer;              // brief freeze after kickoff/goal before play + scoring
        float _clock;                     // counts DOWN, seconds remaining
        bool _fullTime;                   // match over: play frozen, banner shown

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

            _clock = SimConfig.ScrimmageMatchSeconds;

            // Outfield role: the human controls ONE fixed Home player for the whole match
            // (no switching). Pick the first Home outfielder and give it control once.
            if (_role == SimConfig.ScrimRole.Outfield && _home.Count > 0)
                AssignControl(_home[0]);

            Kickoff();
        }

        // ------------------------------------------------------------- lifecycle
        void Kickoff()
        {
            foreach (var f in _all) if (f != null) f.ResetTo(f == _homeKeeper || f == _awayKeeper ? KeeperSpot(f) : SpawnSpot(f));
            if (_humanKeeper != null && _humanKeeperRagdoll != null)
            {
                // Human keeper defends the -Z (Away) goal - 1m in front of that line.
                _humanKeeper.ForceRecover();
                _humanKeeperRagdoll.ResetTo(new Vector3(0f, 0f, AwayGoal.z + 1.0f),
                                            Quaternion.LookRotation(new Vector3(0f, 0f, -1f), Vector3.up));
            }
            _ball.ResetTo(new Vector3(0f, SimConfig.ScrimKickoffBallHeight, 0f));
            _resolved = false;
            _kickoffTimer = SimConfig.ScrimKickoffFreeze;   // brief set-and-ready freeze
            Flash("KICK OFF");

            // Cancel any celebration still running on the controlled player.
            if (_controlledCeleb != null) _controlledCeleb.Cancel();
        }

        Vector3 SpawnSpot(Footballer f)
        {
            // Formation with DEPTH: alternate players into a back line (deeper, in own half)
            // and a forward line (nearer halfway), and spread each line across the width so
            // they don't start in a flat clump. Player index 0 sits closest to centre for
            // kickoff. Own half is -Z for Home (+Z attack), +Z for Away.
            var list = f.Team == 0 ? _home : _away;
            int idx = list.IndexOf(f);
            int n = Mathf.Max(1, list.Count);
            float ownSign = f.Team == 0 ? -1f : 1f;

            if (idx == 0) return new Vector3(0f, 0f, ownSign * HalfLength * 0.12f);   // central, near ball

            // Remaining players split into two lines by parity.
            bool backLine = (idx % 2) == 0;
            int laneIdx = (idx - 1) / 2;
            int lanes = Mathf.Max(1, (n - 1 + 1) / 2);
            float x = lanes <= 1 ? 0f : Mathf.Lerp(-HalfWidth * 0.65f, HalfWidth * 0.65f, laneIdx / (float)(lanes - 1));
            float z = ownSign * (backLine ? HalfLength * 0.55f : HalfLength * 0.25f);
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

            // Full time: play is frozen. R starts a fresh match (rematch).
            if (_fullTime)
            {
                if (_input.ResetPressed) Rematch();
                return;
            }

            if (_input.ResetPressed) { Kickoff(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();
            if (_kickoffTimer > 0f) _kickoffTimer -= Time.deltaTime;

            // Match clock counts DOWN once the kickoff freeze clears; full time at zero.
            if (_kickoffTimer <= 0f)
            {
                _clock -= Time.deltaTime;
                if (_clock <= 0f) { _clock = 0f; EndMatch(); return; }
            }

            StuckBallWatchdog();
            UpdatePossession();

            // --- Human control ---
            if (_role == SimConfig.ScrimRole.Keeper)
            {
                if (_humanKeeper != null) _humanKeeper.Tick();
            }
            else
            {
                // Emote wheel: held open with B. While open (or an emote is playing) the
                // player's normal control is suspended so the two don't fight.
                _wheelOpen = _input.EmoteHeld;
                bool emoting = _wheelOpen || (_controlledCeleb != null && _controlledCeleb.Playing);
                if (_wheelOpen)
                {
                    UpdateWheelSelection();
                    // Stand still while browsing the wheel (Striker.Tick is suspended, so it
                    // won't refresh MoveInput; without this the last velocity keeps gliding).
                    if (_controlled != null && _controlled.Ragdoll != null)
                        _controlled.Ragdoll.MoveInput = Vector3.zero;
                }
                // Releasing B with a slice selected performs that emote.
                if (!_input.EmoteHeld && _wheelWasOpen && _wheelSel >= 0 && _controlledCeleb != null)
                    _controlledCeleb.Play(Celebration.Menu[_wheelSel].e);
                _wheelWasOpen = _wheelOpen;

                if (!emoting)
                {
                    // No switching: the human controls ONE fixed player the whole match;
                    // every other outfielder is AI.
                    if (_humanStriker != null) _humanStriker.Tick();

                    // Passing to a teammate.
                    if (_input.PassGroundPressed) TryPass(lofted: false);
                    else if (_input.PassLoftedPressed) TryPass(lofted: true);

                    // Tackle (C): lunge forward to win the ball off an opponent.
                    if (_input.TacklePressed) TryHumanTackle();
                }
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

            ResolveTackleWindow();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;

            TrackGoals();
        }

        // --- Tackling ---
        float _tackleWindow;   // seconds left in the human tackle attempt
        float _tackleCooldown;

        void TryHumanTackle()
        {
            if (_tackleCooldown > 0f || _controlled == null || _controlled.Ragdoll == null) return;
            // Lunge forward along facing.
            Vector3 fwd = _controlled.Ragdoll.FacingRotation * Vector3.forward; fwd.y = 0f;
            _controlled.Ragdoll.AddVelocityToAll(fwd.normalized * SimConfig.TackleLunge);
            _tackleWindow = 0.4f;
            _tackleCooldown = SimConfig.TackleCooldown;
        }

        void ResolveTackleWindow()
        {
            if (_tackleCooldown > 0f) _tackleCooldown -= Time.deltaTime;
            if (_tackleWindow <= 0f) return;
            _tackleWindow -= Time.deltaTime;
            if (_controlled == null) { _tackleWindow = 0f; return; }

            // Only wins the ball if the OTHER team currently has it (no tackling your own).
            if (PossessionTeam == 0) return;
            Vector3 me = _controlled.Pos; me.y = 0f;
            Vector3 b = _ball.transform.position; b.y = 0f;
            if (Vector3.Distance(me, b) <= SimConfig.TackleReach)
            {
                WinBall(_controlled);
                _tackleWindow = 0f;
            }
        }

        // AI tackle entry point (Footballer calls this when it lunges in on an opponent).
        public void WinBallForAi(Footballer tackler) => WinBall(tackler);

        // Knock the ball loose from whoever's carrying it, away from the ball toward the
        // tackler's forward, and flag possession flipping. Cancels any dribble hold.
        void WinBall(Footballer tackler)
        {
            var d = tackler != null ? tackler.GetComponent<Dribble>() : null;
            if (d != null) d.ForceRelease();
            _ball.DribbleHold = false;
            Vector3 dir = tackler != null ? (tackler.Ragdoll.FacingRotation * Vector3.forward) : Vector3.forward;
            dir.y = 0f;
            _ball.KickTo(dir.normalized * SimConfig.TackleKnock + Vector3.up * 0.5f);
            Flash("TACKLE!");
        }

        // Teammates of a given team (for AI spacing). Read-only view.
        public List<Footballer> TeamList(int team) => team == 0 ? _home : _away;

        // ------------------------------------------------------------- emote wheel
        // Pick the hovered slice from the mouse position relative to screen centre. Mouse
        // is a locked FPS pointer, so we accumulate its delta into a virtual cursor while
        // the wheel is open; a big enough push toward a slice selects it.
        Vector2 _wheelCursor;
        void UpdateWheelSelection()
        {
            if (!_wheelWasOpen) _wheelCursor = Vector2.zero;   // recentre on open
            _wheelCursor += _input.Look * 0.15f;
            _wheelCursor = Vector2.ClampMagnitude(_wheelCursor, 120f);

            if (_wheelCursor.magnitude < 30f) { _wheelSel = -1; return; }   // dead zone at centre
            // Angle: 0 = up, clockwise. Slice 0 at the top.
            float ang = Mathf.Atan2(_wheelCursor.x, _wheelCursor.y) * Mathf.Rad2Deg;
            if (ang < 0f) ang += 360f;
            int n = Celebration.Menu.Length;
            _wheelSel = Mathf.FloorToInt((ang + 360f / n * 0.5f) % 360f / (360f / n));
            if (_wheelSel >= n) _wheelSel = 0;
        }

        void DrawEmoteWheel()
        {
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            int n = Celebration.Menu.Length;
            float rad = 210f;   // wide enough that 9 labels don't overlap

            // Dim backdrop.
            var prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            for (int i = 0; i < n; i++)
            {
                float ang = (360f / n * i) * Mathf.Deg2Rad;   // 0 = up
                float sx = cx + Mathf.Sin(ang) * rad;
                float sy = cy - Mathf.Cos(ang) * rad;
                bool sel = i == _wheelSel;
                float bw = 128f, bh = 40f;
                var r = new Rect(sx - bw * 0.5f, sy - bh * 0.5f, bw, bh);
                GUI.color = sel ? new Color(0.22f, 0.6f, 0.32f) : new Color(0.12f, 0.13f, 0.16f, 0.95f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                if (sel) { GUI.color = new Color(1f, 0.85f, 0.3f); DrawOutline(r, 2f); }
                GUI.color = Color.white;
                lbl.normal.textColor = sel ? Color.white : new Color(0.85f, 0.85f, 0.88f);
                GUI.Label(r, Celebration.Menu[i].name, lbl);
            }
            GUI.color = prev;

            var hint = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(cx - 150f, cy - 10f, 300f, 20f), "Aim + release B", hint);
        }

        static void DrawOutline(Rect r, float t)
        {
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), tex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), tex);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), tex);
        }

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

        // ------------------------------------------------------------- control
        // One-time: give the human control of this fixed player for the whole match and
        // point the camera at it. Called once from Configure (no switching thereafter).
        void AssignControl(Footballer f)
        {
            if (f == null) return;
            _controlled = f;
            _humanStriker = f.GetComponent<Striker>();
            _humanDribble = f.GetComponent<Dribble>();
            _controlledCeleb = f.GetComponent<Celebration>();
            if (_humanStriker != null) _humanStriker.ControlEnabled = true;
            if (_humanDribble != null) _humanDribble.Enabled = true;

            // Camera follows the controlled body; the striker turns to the camera yaw.
            _cam.SetFollow(f.Ragdoll.Pelvis.transform, () => _input.Look);
            if (_humanStriker != null) _humanStriker.SetCameraYaw(() => _cam.Yaw);
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

        // If the ball goes nearly still for a while (jammed against a wall / corner) with no
        // goal, nudge it back to centre so play resumes. Belt-and-braces; the full walls
        // already keep it in.
        float _stuckTimer;
        void StuckBallWatchdog()
        {
            if (_kickoffTimer > 0f) { _stuckTimer = 0f; return; }
            Vector3 c = _ball.transform.position;
            bool nearWall = Mathf.Abs(c.x) > HalfWidth - 1.2f || Mathf.Abs(c.z) > HalfLength - 1.2f;
            bool slow = _ball.Speed < SimConfig.ScrimStuckSpeed;
            if (nearWall && slow) _stuckTimer += Time.deltaTime; else _stuckTimer = 0f;
            if (_stuckTimer > SimConfig.ScrimStuckTime)
            {
                _stuckTimer = 0f;
                // Drop it in from the nearest touchline point, a little toward centre.
                Vector3 spot = new Vector3(Mathf.Clamp(c.x, -HalfWidth + 3f, HalfWidth - 3f), SimConfig.ScrimKickoffBallHeight,
                                           Mathf.Clamp(c.z, -HalfLength + 3f, HalfLength - 3f));
                _ball.ResetTo(spot);
                Flash("BALL IN");
            }
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

        int _celebRr;   // rotates the auto-celebration so it isn't always the same emote
        void OnGoal(bool awayScored)
        {
            _resolved = true;
            if (awayScored) { _awayScore++; Flash("AWAY SCORES!"); }
            else
            {
                _homeScore++; Flash("GOAL!  HOME SCORES!");
                // Auto-celebrate on the controlled scorer (outfield role): cycle a fun emote.
                if (_controlledCeleb != null)
                {
                    var pool = new[] { Celebration.Emote.FistPump, Celebration.Emote.KneeSlide,
                                       Celebration.Emote.Griddy, Celebration.Emote.Backflip, Celebration.Emote.Robot };
                    _controlledCeleb.Play(pool[_celebRr % pool.Length]);
                    _celebRr++;
                }
            }
            CrowdCheer.Celebrate();
            // Freeze scoring, celebrate, then re-kickoff.
            _kickoffTimer = 3f;
            CancelInvoke(nameof(Kickoff));
            Invoke(nameof(Kickoff), 3f);
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // Full time: stop the ball + all controllers, cancel any pending kickoff, freeze.
        void EndMatch()
        {
            _fullTime = true;
            CancelInvoke(nameof(Kickoff));
            _ball.Rb.linearVelocity = Vector3.zero;
            _ball.Rb.angularVelocity = Vector3.zero;
            if (_controlledCeleb != null) _controlledCeleb.Cancel();
        }

        // R at full time: reset scores + clock and kick off again.
        void Rematch()
        {
            _fullTime = false;
            _homeScore = 0; _awayScore = 0;
            _clock = SimConfig.ScrimmageMatchSeconds;
            Kickoff();
        }

        // ------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            var score = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            var big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };

            GUI.Label(new Rect(0, 10, Screen.width, 34), $"HOME  {_homeScore} - {_awayScore}  AWAY", score);
            int mm = Mathf.FloorToInt(_clock / 60f), ss = Mathf.FloorToInt(_clock % 60f);
            var clock = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.85f, 0.85f, 0.9f) } };
            GUI.Label(new Rect(0, 40, Screen.width, 22), $"{mm}:{ss:00}", clock);

            string help = _role == SimConfig.ScrimRole.Keeper
                ? "Keeper:  A/D move   Space/LMB/RMB dive   Reset: R"
                : "Move WASD   Shoot LMB/RMB   Q pass   E lofted   C tackle   B emote   V ball cam   R reset";
            GUI.Label(new Rect(8, Screen.height - 26, Screen.width - 16, 22), help, st);

            // Full-time banner + rematch prompt.
            if (_fullTime)
            {
                var prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(0, Screen.height * 0.5f - 90f, Screen.width, 180f), Texture2D.whiteTexture);
                GUI.color = prev;
                string winner = _homeScore > _awayScore ? "HOME WINS" : _awayScore > _homeScore ? "AWAY WINS" : "DRAW";
                GUI.Label(new Rect(0, Screen.height * 0.5f - 70f, Screen.width, 44f), "FULL TIME", big);
                GUI.Label(new Rect(0, Screen.height * 0.5f - 20f, Screen.width, 40f), $"{winner}   {_homeScore} - {_awayScore}", score);
                GUI.Label(new Rect(0, Screen.height * 0.5f + 30f, Screen.width, 30f), "Press R for a rematch   |   Esc for the menu", st2Centered());
                return;
            }

            if (_flashTime > 0f)
            {
                var c = big.normal.textColor; c.a = Mathf.Clamp01(_flashTime / 1.6f); big.normal.textColor = c;
                GUI.Label(new Rect(0, 60, Screen.width, 44), _flash, big);
            }

            if (_wheelOpen) DrawEmoteWheel();
        }

        static GUIStyle st2Centered() => new GUIStyle(GUI.skin.label)
        { fontSize = 15, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
    }
}
