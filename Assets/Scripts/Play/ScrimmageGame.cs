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

        // Emote wheel state (toggle open/closed with B).
        bool _wheelOpen;
        int _wheelPage;   // which of Celebration.Pages is showing (arrows cycle it)
        KeeperController _humanKeeper;    // keeper role
        ActiveRagdoll _humanKeeperRagdoll;

        // Which team last touched the ball (for AI support/defend logic).
        public int PossessionTeam { get; private set; } = 0;

        // ---- Networked host mode ----
        // When set, this ScrimmageGame is the HOST sim behind a NetScrimmageMatch driver: it still
        // runs the ball/possession/AI/goals/clock/kickoff, but it does NOT own local human control,
        // the camera, or the HUD (the net driver does), and it leaves every body in
        // _netControlled to be driven by networked input (their Striker/KeeperController is fed by
        // the driver) instead of AI. Set via ConfigureNetHost before Configure.
        bool _netHost;
        readonly HashSet<Footballer> _netControlled = new HashSet<Footballer>();
        public void MarkNetControlled(Footballer f) { if (f != null) _netControlled.Add(f); }
        // A networked human left: hand their body back to AI (the AI loop resumes driving it).
        public void UnmarkNetControlled(Footballer f) { if (f != null) _netControlled.Remove(f); }
        public void ConfigureNetHost() => _netHost = true;
        public int HomeScore => _homeScore;
        public int AwayScore => _awayScore;
        public float ClockRemaining => _clock;

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
            // Skipped in net-host mode: the NetScrimmageMatch driver owns human control per slot.
            if (!_netHost && _role == SimConfig.ScrimRole.Outfield && _home.Count > 0)
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
            // Whistle at match start AND every post-goal kickoff. Local for SP + host; the host also
            // broadcasts so clients (who don't run this sim) hear it too.
            AudioManager.Instance?.PlayWhistle();
            if (Trickshot.Net.Multiplayer.IsHost) Trickshot.Net.Multiplayer.Session.BroadcastEvent("WHISTLE");

            // Cancel any celebration + knockdown still active (nobody starts on the ground).
            if (_controlledCeleb != null) _controlledCeleb.Cancel();
            foreach (var f in _all) if (f != null && f.Knock != null) f.Knock.Cancel();
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

            // Net host: the driver owns reset/ball-cam/local input; only run the sim below.
            if (!_netHost)
            {
                if (_input.ResetPressed) { Kickoff(); return; }
                if (_input.BallCamPressed) _cam.ToggleBallCam();
            }
            if (_kickoffTimer > 0f) _kickoffTimer -= Time.deltaTime;

            // Match clock counts DOWN once the kickoff freeze clears; full time at zero.
            if (_kickoffTimer <= 0f)
            {
                _clock -= Time.deltaTime;
                if (_clock <= 0f) { _clock = 0f; EndMatch(); return; }
            }

            StuckBallWatchdog();
            UpdatePossession();

            // --- Human control --- (skipped in net-host mode: the driver ticks every human slot's
            // Striker/KeeperController from networked input, not this single-human path.)
            if (_netHost)
            {
                // no local human control here
            }
            else if (_role == SimConfig.ScrimRole.Keeper)
            {
                if (_humanKeeper != null) _humanKeeper.Tick();
            }
            else
            {
                // Emote wheel: B TOGGLES it open/closed. While open the real mouse cursor is
                // freed so you can click an emote directly (the buttons are drawn + handled
                // in OnGUI). Normal control is suspended so the two don't fight.
                if (_input.EmotePressed) SetWheelOpen(!_wheelOpen);
                bool emoting = _wheelOpen || (_controlledCeleb != null && _controlledCeleb.Playing);
                if (_wheelOpen && _controlled != null && _controlled.Ragdoll != null)
                    _controlled.Ragdoll.MoveInput = Vector3.zero;   // stand still while choosing

                bool down = _controlled != null && _controlled.IsDown;
                if (!emoting && !down)
                {
                    // No switching: the human controls ONE fixed player the whole match;
                    // every other outfielder is AI.
                    if (_humanStriker != null) _humanStriker.Tick();

                    // Passing: hold Q/E to charge (tap = soft, hold = hard), release to
                    // pass, WHEN you have the ball. Pressing without the ball is an instant
                    // call for a pass from an AI teammate (no charge).
                    HandlePassInput();

                    // Tackle (C): lunge forward to win the ball off an opponent.
                    if (_input.TacklePressed) TryHumanTackle();

                    // Slide tackle: hold BOTH legs (LMB+RMB) and run into an opponent to
                    // fell them (and yourself), like a sliding challenge.
                    if (_input.LeftLegHeld && _input.RightLegHeld) TrySlideTackle();
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
                if (_netHost && _netControlled.Contains(f)) continue;   // networked human drives this body
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
        float _slideCooldown;

        // Slide tackle: moving fast into an opponent with both legs held fells them (and
        // yourself). Wins the ball if that opponent had it. On a cooldown.
        void TrySlideTackle()
        {
            if (_slideCooldown > 0f || _controlled == null || _controlled.Ragdoll == null) return;
            Vector3 vel = _controlled.Ragdoll.Pelvis.linearVelocity; vel.y = 0f;
            if (vel.magnitude < SimConfig.SlideTackleMinSpeed) return;

            Vector3 me = _controlled.Pos; me.y = 0f;
            Footballer victim = null; float best = SimConfig.SlideTackleRange;
            foreach (var f in _away)   // Home human -> opponents are Away
            {
                if (f == null) continue;
                Vector3 fp = f.Pos; fp.y = 0f;
                float dd = Vector3.Distance(me, fp);
                if (dd < best) { best = dd; victim = f; }
            }
            if (victim == null) return;

            _slideCooldown = SimConfig.SlideTackleCooldown;
            Vector3 dir = (victim.Pos - _controlled.Pos); dir.y = 0f;
            if (victim.Knock != null) victim.Knock.Fell(dir);          // they go down
            if (_controlled.Knock != null) _controlled.Knock.Fell(dir); // and so do you (a slide)

            // If the felled opponent had the ball, knock it loose toward our attack.
            Vector3 b = _ball.transform.position; b.y = 0f;
            if (Vector3.Distance(victim.Pos, b) <= SimConfig.BallRadius + 1.3f)
            {
                _ball.DribbleHold = false;
                Vector3 fwd = new Vector3(0f, 0f, _controlled.AttackZ);
                _ball.KickTo(fwd * SimConfig.TackleKnock + Vector3.up * 0.4f);
            }
            Flash("SLIDE TACKLE!");
        }

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
            if (_slideCooldown > 0f) _slideCooldown -= Time.deltaTime;
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
        // tackler's forward, and fell the player who was on the ball. Cancels dribble hold.
        void WinBall(Footballer tackler)
        {
            var d = tackler != null ? tackler.GetComponent<Dribble>() : null;
            if (d != null) d.ForceRelease();
            _ball.DribbleHold = false;

            Vector3 dir = tackler != null ? (tackler.Ragdoll.FacingRotation * Vector3.forward) : Vector3.forward;
            dir.y = 0f;
            _ball.KickTo(dir.normalized * SimConfig.TackleKnock + Vector3.up * 0.5f);

            // Fell the opponent who was on the ball (nearest opponent of the tackler's team).
            var victim = NearestOpponentToBall(tackler != null ? tackler.Team : 0);
            if (victim != null && victim.Knock != null)
                victim.Knock.Fell(victim.Pos - (tackler != null ? tackler.Pos : victim.Pos));
            Flash("TACKLE!");
        }

        // Nearest player of the team OPPOSITE `team` to the ball (the likely carrier).
        Footballer NearestOpponentToBall(int team)
        {
            var opp = team == 0 ? _away : _home;
            Footballer best = null; float bestD = float.MaxValue;
            Vector3 b = _ball.transform.position;
            foreach (var f in opp)
            {
                if (f == null || f.IsKeeper) continue;
                float dd = Vector3.Distance(f.Pos, b);
                if (dd < bestD) { bestD = dd; best = f; }
            }
            return best;
        }

        // Teammates of a given team (for AI spacing). Read-only view.
        public List<Footballer> TeamList(int team) => team == 0 ? _home : _away;

        // ------------------------------------------------------------- emote wheel
        // Open/close the emote wheel. While open the real cursor is freed + shown so you can
        // click an emote directly; closing re-locks it for gameplay.
        void SetWheelOpen(bool open)
        {
            _wheelOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        // A real, clickable radial menu. Each emote is a button laid out around a ring;
        // clicking one plays it and closes the wheel. Uses the actual OS cursor (freed in
        // SetWheelOpen), so there's a pointer to click with.
        void DrawEmoteWheel()
        {
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float rad = 210f;   // wide enough that labels don't overlap

            // Dim backdrop (also swallows stray clicks outside the buttons).
            var prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            int pages = Celebration.Pages.Length;
            _wheelPage = ((_wheelPage % pages) + pages) % pages;
            var page = Celebration.Pages[_wheelPage];
            int n = page.Length;
            var lbl = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
            for (int i = 0; i < n; i++)
            {
                float ang = (360f / n * i) * Mathf.Deg2Rad;   // 0 = up, clockwise
                float sx = cx + Mathf.Sin(ang) * rad;
                float sy = cy - Mathf.Cos(ang) * rad;
                float bw = 132f, bh = 42f;
                var r = new Rect(sx - bw * 0.5f, sy - bh * 0.5f, bw, bh);
                if (GUI.Button(r, page[i].name, lbl))
                {
                    if (_controlledCeleb != null) _controlledCeleb.Play(page[i].e);
                    SetWheelOpen(false);
                    return;   // wheel closed; stop drawing this frame
                }
            }

            // Left/right arrows flanking the ring cycle the pages.
            var arrow = new GUIStyle(GUI.skin.button) { fontSize = 30, fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(cx - rad - 96f, cy - 26f, 52f, 52f), "‹", arrow)) _wheelPage--;
            if (GUI.Button(new Rect(cx + rad + 44f, cy - 26f, 52f, 52f), "›", arrow)) _wheelPage++;

            var hint = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(cx - 160f, cy - 20f, 320f, 22f), "Click an emote  ·  B to close", hint);
            float dotW = 16f, gap = 8f, totalW = pages * dotW + (pages - 1) * gap;
            for (int d = 0; d < pages; d++)
            {
                var dr = new Rect(cx - totalW * 0.5f + d * (dotW + gap), cy + 8f, dotW, dotW);
                var pc = GUI.color; GUI.color = d == _wheelPage ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(dr, Texture2D.whiteTexture); GUI.color = pc;
            }
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

        // Does the controlled player actually have the ball right now? (Dribbling it, or it
        // is right at their feet.) Only then does Q/E play a pass.
        bool ControlledHasBall()
        {
            if (_controlled == null) return false;
            if (_humanDribble != null && _humanDribble.Carrying) return true;
            Vector3 me = _controlled.Pos; me.y = 0f;
            Vector3 b = _ball.transform.position; b.y = 0f;
            return Vector3.Distance(me, b) <= SimConfig.BallRadius + 1.1f && _ball.Speed < 8f;
        }

        // ------------------------------------------------------------- passing
        // Charge state: how long each pass key has been held (only meaningful with the ball).
        float _groundCharge, _loftedCharge;

        void HandlePassInput()
        {
            if (_controlled == null || _humanStriker == null) return;
            bool haveBall = ControlledHasBall();

            // Without the ball, a PRESS is an instant call for a pass (no charge).
            if (!haveBall)
            {
                _groundCharge = _loftedCharge = 0f;
                if (_input.PassGroundPressed) CallForPass(lofted: false);
                else if (_input.PassLoftedPressed) CallForPass(lofted: true);
                return;
            }

            // With the ball: charge while held, fire on release, power scaled by hold time.
            if (_input.PassGroundHeld) _groundCharge += Time.deltaTime;
            if (_input.PassGroundReleased) { PlayPass(false, ChargeMul(_groundCharge), _humanDribble); _groundCharge = 0f; }

            if (_input.PassLoftedHeld) _loftedCharge += Time.deltaTime;
            if (_input.PassLoftedReleased) { PlayPass(true, ChargeMul(_loftedCharge), _humanDribble); _loftedCharge = 0f; }
        }

        // 0..PassMaxCharge seconds held -> a speed factor between min (tap) and max (hold).
        float ChargeMul(float held)
            => Mathf.Lerp(SimConfig.PassChargeMinMul, SimConfig.PassChargeMaxMul,
                          Mathf.Clamp01(held / SimConfig.PassMaxCharge));

        // Call for a pass: the AI teammate on the ball passes it to the controlled player.
        void CallForPass(bool lofted)
        {
            var carrier = TeammateOnBall();
            if (carrier == null || carrier == _controlled) { Flash("CALLING"); return; }
            Vector3 lead = _controlled.Ragdoll.Pelvis.linearVelocity * SimConfig.PassLeadFrac;
            Vector3 dir = (_controlled.Pos + lead) - _ball.transform.position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = carrier.Ragdoll.FacingRotation * Vector3.forward;
            // The AI carrier plays it; use its own passing accuracy for the scatter.
            LaunchPass(_ball.transform.position, dir.normalized, lofted, 1f,
                       carrier.GetComponent<Dribble>(), PlayerProfile.PassAccuracyMul);
            Flash(lofted ? "CALL: LOFTED" : "CALL: PASS");
        }

        // The controlled player plays a pass along the aim (auto-aimed to the best teammate
        // in that cone), charged by `chargeMul`, scattered by the player's passing accuracy.
        void PlayPass(bool lofted, float chargeMul, Dribble carry)
        {
            Footballer target = BestPassTarget(_ball.transform.position, _humanStriker.FacingForward);
            Vector3 dir = _humanStriker.FacingForward;
            if (target != null)
            {
                Vector3 lead = target.Ragdoll.Pelvis.linearVelocity * SimConfig.PassLeadFrac;
                Vector3 to = (target.Pos + lead) - _ball.transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) dir = to.normalized;
            }
            LaunchPass(_ball.transform.position, dir, lofted, chargeMul, carry, PlayerProfile.PassAccuracyMul);
            Flash(lofted ? "LOFTED PASS" : "PASS");
        }

        // Common launch: applies pass power (trait + charge), then a random angle + power
        // SCATTER inversely scaled by passing accuracy so low-passing players misplace it.
        void LaunchPass(Vector3 from, Vector3 dir, bool lofted, float chargeMul, Dribble carry, float accMul)
        {
            // Accuracy 0..1: PassAccuracyMul is 1.0 with no nodes, up to ~1.85 fully invested;
            // Maestro = pinpoint. Higher accuracy -> less scatter.
            float acc = Mathf.Clamp01((accMul - 1f) / 0.85f);
            if (PlayerProfile.PerkMaestro) acc = 1f;
            float scatterDeg = SimConfig.PassScatterMaxDeg * (1f - acc);
            float wobble = SimConfig.PassPowerWobble * (1f - acc);

            // Random yaw error about up, plus a small power wobble. Harder charge scatters
            // a touch more (a firm pass is harder to place).
            float ang = Random.Range(-scatterDeg, scatterDeg) * (0.7f + 0.3f * chargeMul);
            dir = Quaternion.AngleAxis(ang, Vector3.up) * dir;

            float power = (lofted ? SimConfig.PassLoftedSpeed : SimConfig.PassGroundSpeed)
                          * PlayerProfile.PassPowerMul * chargeMul
                          * (1f + Random.Range(-wobble, wobble));
            Vector3 v = dir * power;
            if (lofted) v += Vector3.up * (power * SimConfig.PassLoftedArc);
            if (carry != null) carry.ForceRelease();

            // Nudge the ball clear of the PASSER before launching. Without this a lofted pass
            // rises straight into the passer's own torso/head (the ball sits at their feet) and
            // gets batted back down - so an "aerial" pass came out along the ground. Move it a
            // little forward along the pass dir, and for a loft lift it above the body too.
            Vector3 spawn = from + dir * SimConfig.PassSpawnForward;
            if (lofted) spawn += Vector3.up * SimConfig.PassSpawnLift;
            _ball.ResetTo(spawn);
            _ball.KickTo(v);
        }

        // The Home teammate (not the controlled player) currently on the ball, if any.
        Footballer TeammateOnBall()
        {
            if (PossessionTeam != 0) return null;   // Home must have possession
            Vector3 b = _ball.transform.position;
            Footballer best = null; float bestD = SimConfig.BallRadius + 1.3f;
            foreach (var f in _home)
            {
                if (f == null || f == _controlled || f.IsKeeper) continue;
                float dd = Vector3.Distance(f.Pos, b);
                if (dd < bestD) { bestD = dd; best = f; }
            }
            return best;
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
            // Crowd audio: cheer + applause on every goal; boos if a team is now 2+ down. Host + SP
            // run here; net clients fire the same off replicated score deltas (NetScrimmageMatch).
            AudioManager.Instance?.OnScrimmageGoal(_homeScore, _awayScore);
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
            // Three whistles = full time. Local for SP + host; host broadcasts to clients.
            AudioManager.Instance?.PlayWhistleTriple();
            if (Trickshot.Net.Multiplayer.IsHost) Trickshot.Net.Multiplayer.Session.BroadcastEvent("WHISTLE3");
            AudioManager.Instance?.PlayGoalCelebration();   // full-time cheer + applause over the ambient bed
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
            if (_netHost) return;   // the NetScrimmageMatch driver draws the networked HUD
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
                // Shared styled callout (big, colour-coded, shadowed) - same look as every other mode.
                Hud.Begin();
                Hud.Flash(_flash, _flashTime / 1.6f);
            }

            if (_wheelOpen) DrawEmoteWheel();
        }

        static GUIStyle st2Centered() => new GUIStyle(GUI.skin.label)
        { fontSize = 15, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
    }
}
