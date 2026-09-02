using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Full match driver: two teams of outfielders + a keeper each, on a
    /// two-goal pitch walled all round. The human controls one player; the rest are AI.
    ///
    ///  - Outfield role: the human controls the Home teammate NEAREST the ball (FIFA-style
    ///    auto-switch, plus F to switch manually). The controlled player uses the normal
    ///    Striker control scheme (WASD + mouse + dribble + shoot); E ground-passes and Q
    ///    chips one, weighted for the range and led onto the receiver's run (see Passing).
    ///  - Keeper role: the human controls the Home keeper (KeeperController) the whole
    ///    match; every outfielder is AI.
    ///
    /// Scoring: geometric per-frame test at each goal. Ball into the
    /// +Z goal = Away scores; into the -Z goal = Home scores. Reset to kickoff after a goal.
    /// </summary>
    public class MatchGame : MonoBehaviour
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
        SimConfig.MatchRole _role;
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
        // When set, this MatchGame is the HOST sim behind a NetMatch driver: it still
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
        readonly MatchStatsUI _statsUI = new MatchStatsUI();

        public void Configure(GameInput input, BallController ball, GameCamera cam,
                              MatchArena.Refs arena, SimConfig.MatchRole role,
                              List<Footballer> home, List<Footballer> away,
                              Footballer homeKeeper, Footballer awayKeeper,
                              Striker humanStriker, Dribble humanDribble,
                              KeeperController humanKeeper, ActiveRagdoll humanKeeperRagdoll)
        {
            _input = input; _ball = ball; _cam = cam;
            // Match: deliberate LMB/RMB shots (human dribble release + AI shots) fly airborne
            // like a set piece, no controllable spin. Loose-ball trapping stays grounded.
            if (_ball != null) _ball.MatchLoftKicks = true;
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

            BuildLandingReticle();
            BuildStatRows();
            // A human's shots come in by event (see Dribble.ShotFired). Static, so it MUST come back off
            // in OnDestroy or a finished match keeps counting into a dead table.
            Dribble.ShotFired += OnDribbleShot;

            _clock = SimConfig.MatchSeconds;

            // Outfield role: the human controls ONE fixed Home player for the whole match
            // (no switching). Pick the first Home outfielder and give it control once.
            // Skipped in net-host mode: the NetMatch driver owns human control per slot.
            if (!_netHost && _role == SimConfig.MatchRole.Outfield && _home.Count > 0)
                AssignControl(_home[0]);

            Kickoff();
        }

        // ------------------------------------------------------------- lifecycle
        void Kickoff()
        {
            DropTouchState();
            foreach (var f in _all) if (f != null) f.ResetTo(f == _homeKeeper || f == _awayKeeper ? KeeperSpot(f) : SpawnSpot(f));
            if (_humanKeeper != null && _humanKeeperRagdoll != null)
            {
                // Human keeper defends the -Z (Away) goal - 1m in front of that line, looking OUT
                // up +Z. The +Z matters: it has to agree with the facing GameBootstrap builds him
                // with, the _faceDir his controller resolves, and the follow camera's base rotation,
                // or every kickoff snaps his bones to face his own netting and he spins on the spot.
                _humanKeeper.ForceRecover();
                _humanKeeperRagdoll.ResetTo(new Vector3(0f, 0f, AwayGoal.z + 1.0f),
                                            Quaternion.LookRotation(new Vector3(0f, 0f, 1f), Vector3.up));
            }
            _ball.ResetTo(new Vector3(0f, SimConfig.ScrimKickoffBallHeight, 0f));
            _resolved = false;
            _kickoffTimer = SimConfig.ScrimKickoffFreeze;   // brief set-and-ready freeze
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

            // Landing telegraph, BEFORE every early return below. It has to be: this method returns at
            // full time, on a reset and on the frame the clock expires, and the disc would have been
            // left frozen on the turf after the whistle with nothing left to hide it. It is about the
            // BALL, not about whether play is live, and DriveLandingReticle does its own full-time test.
            // Rb.position, not transform.position - the ball interpolates, so the transform lags by up
            // to a render frame and the disc would visibly jitter against the ball it is predicting.
            if (_ball != null)
                DriveLandingReticle(_ball.Rb.position, _ball.Rb.linearVelocity, _ball.Guided);

            // Full time: play is frozen. R starts a fresh match (rematch) - single-player only,
            // same as the net-host guard right below: an unguarded Rematch() here used to restart
            // the sim out from under NetMatch (no coordinated MP rematch exists), re-arming
            // _fullTime's falling/rising edge and firing the MP CareerStats hook a second time
            // with PlayerStat rows that were never reset (BuildStatRows only runs once, from
            // Configure) - a silent double-record, not just a missing feature.
            if (_fullTime)
            {
                _statsUI.TickInput();
                if (!_netHost && _input.ResetPressed) Rematch();
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
            TrackTouches();

            // --- Human control --- (skipped in net-host mode: the driver ticks every human slot's
            // Striker/KeeperController from networked input, not this single-human path.)
            if (_netHost)
            {
                // no local human control here
            }
            else if (_role == SimConfig.MatchRole.Keeper)
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

                // OUTSIDE the gate on purpose. The bar has to be stepped every frame even while he is
                // down or mid-emote, because that is the only place the charge is DISARMED: leaving it
                // inside meant a knockdown froze a half-full bar and its fired latch, and the release
                // that would have cleared them was swallowed. Blocked frames disarm instead of charge.
                HandlePassInput(blocked: emoting || down);

                if (!emoting && !down)
                {
                    // No switching: the human controls ONE fixed player the whole match;
                    // every other outfielder is AI.
                    if (_humanStriker != null) _humanStriker.Tick();

                    // Tackle (C): lunge forward to win the ball off an opponent.
                    if (_input.TacklePressed) TryHumanTackle();

                    // Slide tackle contact. WHETHER he is sliding is the Striker's call now
                    // (LMB+RMB pushed forward, see Striker.UpdateSit); this only resolves who he
                    // takes down. Reading the state instead of re-testing the buttons and a speed
                    // threshold means the tackle and the animation can no longer disagree - the old
                    // pair of gates could both miss, which is how you got a slide pose that felled
                    // nobody, or a felling with no slide.
                    if (_humanStriker != null && _humanStriker.IsSliding) TrySlideTackle();
                }
            }

            // --- AI: every footballer that isn't the human-controlled one ---
            // In keeper role the human drives a separate KeeperController ragdoll (not a
            // Footballer), so the Home keeper Footballer is suppressed to avoid two keepers.
            Footballer humanBody = _role == SimConfig.MatchRole.Outfield ? _controlled : null;
            var homeClosest = ClosestToBall(_home);
            var awayClosest = ClosestToBall(_away);
            foreach (var f in _all)
            {
                if (f == null || f == humanBody) continue;
                if (_role == SimConfig.MatchRole.Keeper && f == _homeKeeper) continue;
                if (_netHost && _netControlled.Contains(f)) continue;   // networked human drives this body
                if (f.IsKeeper) { f.AiKeeperTick(); continue; }
                bool isClosest = f == (f.Team == 0 ? homeClosest : awayClosest);
                f.AiTick(isClosest);
            }

            ResolveTackleWindow();
            ResolveDiveHits();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;

            TrackGoals();

            // Dev measurement harness (F9). LAST on purpose: the AI brains above have already run, so a
            // shot struck this frame still carries its launch velocity and the on-target verdict is read
            // off the real strike. One bool test when off; compiles to nothing in a shipped player.
            MatchProbe.Tick(this, _ball, _all);
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
            // Counted here, past the cooldown gate, because THIS is the frame the slide commits. Counting
            // it at the call site would have counted every frame of one slide.
            MatchProbe.TackleAttempt(ProbeTackle.Slide);
            // No speed test any more. Striker.IsSliding already decided he is committed to a slide,
            // and SlideFriction bleeds his velocity off as he skids - so re-checking speed here would
            // have made the back half of every slide unable to fell anybody.

            Vector3 me = _controlled.Pos; me.y = 0f;
            Footballer victim = null; float best = SimConfig.SlideTackleRange;
            // Opponents OF THE SLIDING PLAYER, not a hardcoded _away. The old scan assumed the human is
            // always Home, so an Away human slide-tackled his own team - fine in single player where he
            // always is Home, wrong the moment a net client is seated on the other side.
            foreach (var f in TeamList(_controlled.Team == 0 ? 1 : 0))
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

            // CONTEST THE STEAL. This was `if (Distance(victim, ball) <= 1.52f)` and nothing else: a
            // slide that reached anybody near the ball took it, every time, with no reference to the
            // carrier. committed:true buys a real slide a better shot at it and makes a MISS cost
            // double (Knockdown.BeatenSlideTime), which is what a mistimed slide should cost.
            //
            // The mutual fell above stays unconditional and happens either way - a slide connects
            // whether or not it wins the ball. Knockdown's re-entry guard makes the Foul path's second
            // Fell refresh the timer instead of restacking the impulse, and Stumble self-cancels on a
            // body whose Striker IsBusy, so the slider is not limped on top of his own slide.
            var carrier = _ball != null ? _ball.DribbleCarrier : null;
            var res = Dribble.ContestTackle(_controlled.Ragdoll, carrier,
                                            _ball != null ? _ball.transform.position : Vector3.zero,
                                            true, out _);
            if (res == Dribble.TackleResult.Won)
            {
                // Credit it properly. This path never called WinBall, so NoteTackle never saw a slide
                // and the post-match TKL column was silently missing every slide steal.
                NoteTackle(_controlled.Ragdoll, carrier);
                _ball.SetDribbleCarrier(null);
                Vector3 fwd = new Vector3(0f, 0f, _controlled.AttackZ);
                _ball.KickTo(fwd * SimConfig.TackleKnock + Vector3.up * 0.4f, _controlled.Ragdoll);
                MatchProbe.SlideWin();
            }
        }

        // Diving header contact: a body in MID-FLIGHT of a diving header that reaches an opponent
        // fells them, the same knockdown a slide tackle applies (Knockdown.Fell owns the limp,
        // the tumble and the automatic get-up, so there is nothing to time here).
        //
        // Deliberately driven off _all rather than off the local human, so ONE code path covers
        // the single-player human, the host's own slot and every remote human slot - the host
        // ticks all of their Strikers, so IsDiving is live for all of them on the host. AI bodies
        // never dive (their Striker is not ticked) so they drop out on the IsDiving test. And
        // because MatchGame only exists on the host in net play, this is host-authoritative
        // without a wire message: the victim's down state already streams via BodyState.down.
        void ResolveDiveHits()
        {
            foreach (var d in _all)
            {
                if (d == null || d.Ragdoll == null) continue;
                var st = d.Strk;
                if (st == null || !st.IsDiving || !st.DiveHitPending) continue;
                if (d.Ragdoll.IsGrounded) continue;   // the flight phase connects, not the prone slide after it

                // Opponents only. TeamList returns outfielders, so a keeper cannot be flattened -
                // the same exemption the slide tackle gets from scanning _away.
                Vector3 me = d.Pos; me.y = 0f;
                Footballer victim = null; float best = SimConfig.DiveHeaderKnockRange;
                foreach (var f in TeamList(d.Team == 0 ? 1 : 0))
                {
                    if (f == null || f.IsDown) continue;   // already down; re-felling would restart the timer
                    Vector3 fp = f.Pos; fp.y = 0f;
                    float dd = Vector3.Distance(me, fp);
                    if (dd < best) { best = dd; victim = f; }
                }
                if (victim == null || victim.Knock == null) continue;

                st.DiveHitPending = false;
                // If the victim was mid-dive itself (two divers meeting), tear that state down
                // first: its DiveYawLock would otherwise survive and fight the knockdown recovery,
                // and its prone timer stops counting while the controller is suspended.
                if (victim.Strk != null && victim.Strk.IsDiving) victim.Strk.ForceRecover();
                Vector3 dir = victim.Pos - d.Pos; dir.y = 0f;
                victim.Knock.Fell(dir);
            }
        }

        void TryHumanTackle()
        {
            if (_tackleCooldown > 0f || _controlled == null || _controlled.Ragdoll == null) return;
            // Lunge forward along facing.
            Vector3 fwd = _controlled.Ragdoll.FacingRotation * Vector3.forward; fwd.y = 0f;
            _controlled.Ragdoll.AddVelocityToAll(fwd.normalized * SimConfig.TackleLunge);
            _tackleWindow = 0.4f;
            _tackleCooldown = SimConfig.TackleCooldown;
            MatchProbe.TackleAttempt(ProbeTackle.Human);   // the WIN is counted off the stat delta
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
                // END THE WINDOW WHETHER OR NOT HE WINS. This is the whole reason the window exists as
                // a window: it gives the lunge time to ARRIVE in reach, and the instant it arrives the
                // challenge happens and is over. Leaving it armed on a loss re-contested every frame -
                // measured at ~24 rolls across a 0.4 s window, which converts a 34% chance into a
                // certainty and would have reproduced "tackles always steal the ball" through the new
                // contest instead of through the old step function.
                _tackleWindow = 0f;
                WinBall(_controlled);
            }
        }

        // AI tackle entry point (Footballer calls this when it lunges in on an opponent).
        public void WinBallForAi(Footballer tackler) => WinBall(tackler);

        // Knock the ball loose from whoever's carrying it, away from the ball toward the
        // tackler's forward, and fell the player who was on the ball. Cancels dribble hold.
        void WinBall(Footballer tackler)
        {
            // Read the carrier FIRST. A won contest releases the hold, and Dribble.StopCarry clears
            // BallController.DribbleCarrier on its way out, so reading it afterwards would credit the
            // tackle against nobody. Both a human carry (Dribble) and an AI carry (Footballer) set
            // this same field, so one read covers both.
            var carrier = _ball != null ? _ball.DribbleCarrier : null;

            // CONTEST IT. Until this line the steal was `if (distance <= 1.6f) win`, with no reference
            // to the carrier at all - a 1-rated defender took the ball off a 99-rated dribbler 100% of
            // the time, from any angle, flat-footed, and the human path re-tested it every frame for
            // 0.4 s so one press got ~24 free attempts. That is the whole of "tackles and slide
            // tackles always steal the ball". Dribble.ContestTackle owns the odds (0.34 for a square
            // challenge) and the cost of missing, so spamming no longer converts a roll into a
            // certainty. It also rejects a LOOSE ball, which this path used to treat as a tackle.
            var res = Dribble.ContestTackle(tackler != null ? tackler.Ragdoll : null, carrier,
                                            _ball != null ? _ball.transform.position : Vector3.zero,
                                            false, out _);
            if (res != Dribble.TackleResult.Won) return;

            NoteTackle(tackler != null ? tackler.Ragdoll : null, carrier);
            // ContestTackle already released the human hold on its way to Won. This still has to run
            // for an AI carrier, which claims the ball directly instead of through a Dribble.
            _ball.SetDribbleCarrier(null);

            Vector3 dir = tackler != null ? (tackler.Ragdoll.FacingRotation * Vector3.forward) : Vector3.forward;
            dir.y = 0f;
            // Scoped to the tackler: the felled carrier and everyone else can strike the loose ball.
            _ball.KickTo(dir.normalized * SimConfig.TackleKnock + Vector3.up * 0.5f,
                         tackler != null ? tackler.Ragdoll : null);

            // Fell the man who was ACTUALLY carrying. This used to fell NearestOpponentToBall, which
            // is a guess, and on a crowded ball it fells a bystander while the carrier stays upright -
            // one of the ways a lost ball read as broken rather than as a tackle. The nearest-opponent
            // path stays as the fallback for the case where the carrier body has no Footballer.
            Footballer victim = null;
            if (carrier != null)
                foreach (var f in _all) { if (f != null && f.Ragdoll == carrier) { victim = f; break; } }
            if (victim == null) victim = NearestOpponentToBall(tackler != null ? tackler.Team : 0);
            if (victim != null && victim.Knock != null)
                victim.Knock.Fell(victim.Pos - (tackler != null ? tackler.Pos : victim.Pos));
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
            GameInput.CaptureCursor(!open);
        }

        // A real, clickable radial menu. Each emote is a button laid out around a ring;
        // clicking one plays it and closes the wheel. Uses the actual OS cursor (freed in
        // SetWheelOpen), so there's a pointer to click with.
        void DrawEmoteWheel()
        {
            float cx = Hud.W * 0.5f, cy = Hud.H * 0.5f;
            float rad = 210f;   // wide enough that labels don't overlap

            // Dim backdrop (also swallows stray clicks outside the buttons).
            Hud.Scrim(0.55f);

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
                if (UITheme.Button(r, page[i].name, lbl))
                {
                    if (_controlledCeleb != null) _controlledCeleb.Play(page[i].e);
                    SetWheelOpen(false);
                    return;   // wheel closed; stop drawing this frame
                }
            }

            // Left/right arrows flanking the ring cycle the pages.
            var arrow = new GUIStyle(GUI.skin.button) { fontSize = 30, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(cx - rad - 96f, cy - 26f, 52f, 52f), "‹", arrow)) _wheelPage--;
            if (UITheme.Button(new Rect(cx + rad + 44f, cy - 26f, 52f, 52f), "›", arrow)) _wheelPage++;

            var hint = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Hud.Dim } };
            UITheme.Label(new Rect(cx - 160f, cy - 20f, 320f, 22f), "Click an emote  ·  B to close", hint);
            Hud.PageDots(cx, cy + 16f, pages, _wheelPage);
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
            if (_humanStriker != null) _humanStriker.SetCameraYaw(() => _cam.Yaw, () => _cam.Pitch);
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

        // Can the controlled player play a pass right now, and would it be a FIRST-TIME one?
        //
        // The old gate demanded a nearly-settled ball (Speed < 8), which made a one-two
        // impossible: the return ball had to be trapped before it could be played on. Now a
        // loose or arriving ball at the feet is playable, at an accuracy cost.
        bool ControlledCanPass(out bool firstTime)
        {
            firstTime = false;
            if (_controlled == null) return false;
            return Passing.CanPlay(_ball, _controlled.Pos,
                                   _humanDribble != null && _humanDribble.Carrying, false, out firstTime);
        }

        // ============================================================= match stats
        /// <summary>
        /// One player's match record. Keyed by ActiveRagdoll rather than Footballer, because the human
        /// keeper in single player is a KeeperController body with no Footballer at all and still needs
        /// a row.
        ///
        /// The display NAME is captured when the row is built, never resolved at draw time: a networked
        /// player who leaves mid-match can no longer be looked up in the roster, and their row would
        /// otherwise decay to "Player 5" on the post-match board.
        /// </summary>
        public class PlayerStat
        {
            public string name;
            public int team;                 // 0 = Home, 1 = Away
            public int slot = 255;           // networked player slot; 255 = AI, no roster entry
            public int shirt;                // 0 = keeper, 1.. = outfield
            public bool keeper;
            public bool netControlled;       // a networked human slot - see the TKL note below
            public int goals, assists, shots, passes, passesDone, tackles, saves, conceded;
            public float rating;             // 6.0..10.0, one decimal; set by FinalizeRatings
            public float rawRating;          // pre-clamp, pre-round: what MOTM is actually chosen on
            public bool motm;
            public bool frozen;              // a departed human's row stops accruing
        }

        readonly List<PlayerStat> _stats = new List<PlayerStat>();
        readonly Dictionary<ActiveRagdoll, PlayerStat> _statOf = new Dictionary<ActiveRagdoll, PlayerStat>();

        /// <summary>Every player's match record, Home first then Away, keepers first within a team.</summary>
        public List<PlayerStat> Stats => _stats;
        public bool FullTime => _fullTime;

        // Touch attribution. A tiny amount of state rather than a ball-contact ledger: with goals,
        // assists, pass completion and the save gate as the only consumers, "who touched it last and
        // who before that" plus one pending pass and one armed shot covers all of them.
        // RECENT TOUCH HISTORY, newest last. Two entries (last + previous) was not enough and it showed:
        // a scrappy goal off a rebound in the box has the defending KEEPER as the newest toucher and
        // another defender before him, so a two-deep look found nobody on the scoring team and the goal
        // went uncredited - the G column then disagreed with the scoreboard on screen. A short window
        // fixes it, and it is also what makes the assist the previous SCORING-SIDE touch rather than
        // whoever happened to be second-newest.
        struct TouchRec { public PlayerStat who; public bool intent; public float at; }
        const int TouchHistory = 10;
        readonly List<TouchRec> _touches = new List<TouchRec>(TouchHistory);
        PlayerStat _lastTouch, _prevTouch;
        bool _lastWasIntent;              // the last touch was a pass/shot, not an incidental contact
        PlayerStat _passBy; float _passAt;          // a pass awaiting a receiver
        PlayerStat _shotBy; float _shotAt; bool _shotSpent;   // a shot a keeper could still save

        // Carry is INTENT: claiming the ball is how a pass is received. Leaving it out made pass
        // completion read zero across the board, because a team-mate who takes a pass and simply runs
        // with it never passes, shoots or tackles, so nothing ever resolved the pass.
        enum Touch { Contact, Carry, Pass, Shot, Tackle, Keeper }
        ActiveRagdoll _prevCarrier;

        void BuildStatRows()
        {
            _stats.Clear(); _statOf.Clear();
            for (int t = 0; t < 2; t++)
            {
                var keeper = t == 0 ? _homeKeeper : _awayKeeper;
                if (keeper != null) AddRow(keeper.Ragdoll, (t == 0 ? "H" : "A") + " GK", t, true, false, 0);
                // The SP human keeper has no Footballer, so his row is added against his own ragdoll.
                if (t == 0 && _role == SimConfig.MatchRole.Keeper && _humanKeeperRagdoll != null)
                    AddRow(_humanKeeperRagdoll, PlayerProfile.PlayerName ?? "YOU", 0, true, false, 0);
                var list = t == 0 ? _home : _away;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null) AddRow(list[i].Ragdoll, StatName(list[i], t, i + 1, false), t, false, false, i + 1);
            }
        }

        void AddRow(ActiveRagdoll rag, string name, int team, bool keeper, bool net, int shirt)
        {
            if (rag == null || _statOf.ContainsKey(rag)) return;
            var s = new PlayerStat { name = name, team = team, keeper = keeper, netControlled = net, shirt = shirt };
            _stats.Add(s); _statOf[rag] = s;
        }

        /// <summary>Attach a networked slot to a row, so a client can resolve its roster name.</summary>
        public void MarkStatSlot(ActiveRagdoll rag, int slot, string name)
        {
            if (rag == null || !_statOf.TryGetValue(rag, out var s)) return;
            s.slot = slot;
            s.netControlled = true;
            if (!string.IsNullOrEmpty(name)) s.name = name;
        }

        /// <summary>The final table, packed for the wire. Host only; call after full time.</summary>
        public Trickshot.Net.StatRow[] WireStats()
        {
            var rows = new Trickshot.Net.StatRow[_stats.Count];
            for (int i = 0; i < _stats.Count; i++)
            {
                var s = _stats[i];
                byte flags = 0;
                if (s.keeper) flags |= 1;
                if (s.netControlled) flags |= 2;
                if (s.motm) flags |= 4;
                rows[i] = new Trickshot.Net.StatRow
                {
                    slot = (byte)Mathf.Clamp(s.slot, 0, 255),
                    team = (byte)Mathf.Clamp(s.team, 0, 1),
                    shirt = (byte)Mathf.Clamp(s.shirt, 0, 255),
                    flags = flags,
                    goals = (byte)s.goals, assists = (byte)s.assists, shots = (byte)s.shots,
                    passes = (byte)s.passes, passesDone = (byte)s.passesDone,
                    tackles = (byte)s.tackles, saves = (byte)s.saves,
                    rat10 = (byte)Mathf.Clamp(Mathf.RoundToInt(s.rating * 10f), 0, 255),
                };
            }
            return rows;
        }

        /// <summary>Unpack a received table into rows the shared board renderer can draw.</summary>
        public static List<PlayerStat> FromWire(Trickshot.Net.StatRow[] rows, System.Func<int, string> nameOfSlot)
        {
            var list = new List<PlayerStat>();
            if (rows == null) return list;
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                bool keeper = (r.flags & 1) != 0;
                string nm = r.slot != 255 && nameOfSlot != null ? nameOfSlot(r.slot) : null;
                if (string.IsNullOrEmpty(nm))
                    nm = (r.team == 0 ? "H" : "A") + (keeper ? " GK" : r.shirt.ToString());
                list.Add(new PlayerStat
                {
                    name = nm, team = r.team, slot = r.slot, shirt = r.shirt,
                    keeper = keeper,
                    netControlled = (r.flags & 2) != 0,
                    motm = (r.flags & 4) != 0,
                    goals = r.goals, assists = r.assists, shots = r.shots,
                    passes = r.passes, passesDone = r.passesDone,
                    tackles = r.tackles, saves = r.saves,
                    rating = r.rat10 / 10f,
                });
            }
            return list;
        }

        void OnDestroy() { Dribble.ShotFired -= OnDribbleShot; _statsUI.Teardown(); }
        void OnDribbleShot(ActiveRagdoll rag) => NoteShot(rag);

        // Terse: the board is a table of numbers, so a name is a shirt, not a sentence. The controlled
        // outfielder takes the player's own name; everyone else is their kit letter and number.
        string StatName(Footballer f, int team, int shirt, bool keeper)
        {
            if (f == _controlled && _role == SimConfig.MatchRole.Outfield)
                return PlayerProfile.PlayerName ?? "YOU";
            return (team == 0 ? "H" : "A") + shirt + (keeper ? " GK" : "");
        }

        /// <summary>An AI body played a pass (Footballer's own pass path, not the human solver).</summary>
        public void NoteAiPass(ActiveRagdoll rag) => NotePass(rag);

        /// <summary>A shot was struck: the AI's Shoot(), or a human's dribble release.</summary>
        public void NoteShotBy(ActiveRagdoll rag) => NoteShot(rag);

        /// <summary>Mark a row as driven by a networked human (its TKL is unreachable - see NoteTackle).</summary>
        public void MarkStatNetControlled(ActiveRagdoll rag)
        { if (rag != null && _statOf.TryGetValue(rag, out var s)) s.netControlled = true; }

        /// <summary>Stop a row accruing: its human left and an AI took the body over mid-match.</summary>
        public void FreezeStatRow(ActiveRagdoll rag)
        { if (rag != null && _statOf.TryGetValue(rag, out var s)) s.frozen = true; }

        PlayerStat Row(ActiveRagdoll rag)
        {
            if (rag == null) return null;
            if (!_statOf.TryGetValue(rag, out var s)) return null;
            return s.frozen ? null : s;
        }

        /// <summary>The local human's own stat row in single-player - null in net-host mode (see
        /// EndMatch's own comment on why _controlled/_humanKeeperRagdoll are never assigned there).
        /// Public so the post-match stats window can find "me" without duplicating this resolution.</summary>
        public PlayerStat MyRow()
        {
            var meRag = _role == SimConfig.MatchRole.Keeper
                        ? _humanKeeperRagdoll
                        : (_controlled != null ? _controlled.Ragdoll : null);
            return Row(meRag);
        }

        // Dead-ball frames must not feed the stats. The post-goal freeze is 3 s and the kickoff freeze
        // 1.2 s, and everything except TrackGoals and the clock keeps running underneath them.
        bool StatsFrozen => _fullTime || _resolved || _kickoffTimer > 0f;

        /// <summary>
        /// Note that a body touched the ball. Resolves a pending pass, advances the last/previous
        /// toucher pair, arms a shot, and banks a keeper's save.
        /// </summary>
        void NoteTouch(ActiveRagdoll rag, Touch kind)
        {
            if (StatsFrozen) return;
            var s = Row(rag);
            if (s == null) return;
            float now = Time.time;

            // Resolve a pending pass. Only an INTENT-bearing touch may resolve one: the pass spawn
            // teleports the ball 0.85 m off the passer and can land inside a pressing defender, so an
            // incidental Contact would score that teleport as a completed pass to the wrong team.
            if (_passBy != null && s != _passBy && now - _passAt >= SimConfig.StatPassSpawnIgnore
                && now - _passAt <= SimConfig.StatPassResolveWindow)
            {
                if (kind != Touch.Contact)
                {
                    if (s.team == _passBy.team) _passBy.passesDone++;
                    _passBy = null;              // an opponent taking it is simply not completed
                }
                else if (s.team != _passBy.team) _passBy = null;   // dead: intercepted by a deflection
            }

            if (s != _lastTouch) { _prevTouch = _lastTouch; _lastTouch = s; _lastWasIntent = kind != Touch.Contact; }
            else if (kind != Touch.Contact) _lastWasIntent = true;

            // Only record a NEW toucher, or a repeat that carries intent: otherwise the proximity pass
            // fills the whole window with one player standing near the ball and pushes out the history
            // that a goal needs.
            if (_touches.Count == 0 || _touches[_touches.Count - 1].who != s || kind != Touch.Contact)
            {
                if (_touches.Count >= TouchHistory) _touches.RemoveAt(0);
                _touches.Add(new TouchRec { who = s, intent = kind != Touch.Contact, at = now });
            }

            if (kind == Touch.Shot) { _shotBy = s; _shotAt = now; _shotSpent = false; }

            // A keeper touch is a SAVE only against a live opponent shot, and it CONSUMES that shot.
            // Without consuming it, a ball pinballing off the keeper banks one save per parry cooldown.
            if (kind == Touch.Keeper && s.keeper && _shotBy != null && !_shotSpent
                && _shotBy.team != s.team && now - _shotAt <= SimConfig.StatSaveShotWindow
                && _ball != null && _ball.Speed >= SimConfig.StatSaveMinBallSpeed)
            { s.saves++; _shotSpent = true; }
        }

        /// <summary>A pass was struck: count it and arm it for a completion.</summary>
        void NotePass(ActiveRagdoll rag)
        {
            if (StatsFrozen) return;
            var s = Row(rag);
            if (s == null) return;
            s.passes++;
            _passBy = s; _passAt = Time.time;
            NoteTouch(rag, Touch.Pass);
        }

        /// <summary>A shot was struck.</summary>
        void NoteShot(ActiveRagdoll rag)
        {
            if (StatsFrozen) return;
            var s = Row(rag);
            if (s == null) return;
            s.shots++;
            NoteTouch(rag, Touch.Shot);
        }

        /// <summary>
        /// A tackle WON the ball. Gated on there having been a carrier to win it from: WinBall fires on
        /// proximity plus a cooldown and PossessionTeam is a bare nearest-player test with no
        /// hysteresis, so without this gate the column was farmable by standing near a loose ball and
        /// mashing tackle. BallController.DribbleCarrier is the authoritative answer and was never
        /// consulted.
        /// </summary>
        void NoteTackle(ActiveRagdoll tackler, ActiveRagdoll carrier)
        {
            if (StatsFrozen || carrier == null) return;
            var t = Row(tackler); var c = Row(carrier);
            if (t == null || c == null || t.team == c.team) return;
            t.tackles++;
            NoteTouch(tackler, Touch.Tackle);
        }

        /// <summary>
        /// Credit a goal, an assist, and the conceding keeper. Prefers the most recent INTENT touch on
        /// the scoring team over whatever touched the ball last: a deflection off a defender or keeper is
        /// the newest contact before most goals, and crediting that would leave the scorer uncredited and
        /// make the G column disagree with the scoreboard.
        /// </summary>
        void AttributeGoal(bool awayScored)
        {
            int scoring = awayScored ? 1 : 0;
            float now = Time.time;
            int scorerIdx = -1;

            // Newest INTENT touch by the scoring side inside the window. Intent first, because the
            // newest touch before most goals is a deflection off a defender or a keeper's fingertips,
            // and crediting that leaves the player who actually struck it with nothing.
            for (int i = _touches.Count - 1; i >= 0; i--)
            {
                var t = _touches[i];
                if (now - t.at > SimConfig.StatGoalCreditWindow) break;
                if (t.who != null && !t.who.frozen && t.who.team == scoring && t.intent) { scorerIdx = i; break; }
            }
            // Nothing deliberate on the scoring side: fall back to any touch of theirs, so a goal that
            // went in off a shin is still somebody's.
            if (scorerIdx < 0)
                for (int i = _touches.Count - 1; i >= 0; i--)
                {
                    var t = _touches[i];
                    if (now - t.at > SimConfig.StatGoalCreditWindow) break;
                    if (t.who != null && !t.who.frozen && t.who.team == scoring) { scorerIdx = i; break; }
                }
            PlayerStat scorer = scorerIdx >= 0 ? _touches[scorerIdx].who : null;

            if (scorer != null)
            {
                scorer.goals++;
                // A headed, volleyed or bicycled goal never went through a shot hook, so G could exceed
                // SHT and the table would contradict itself. A goal IS a shot.
                if (scorer.goals > scorer.shots) scorer.shots = scorer.goals;

                // Assist: the scoring side's previous DIFFERENT toucher, searched back from the scorer
                // rather than from the end of the history, and abandoned if an opponent touched the ball
                // in between (that is a turnover, not a pass).
                for (int i = scorerIdx - 1; i >= 0; i--)
                {
                    var t = _touches[i];
                    if (now - t.at > SimConfig.StatGoalCreditWindow) break;
                    if (t.who == null || t.who == scorer) continue;
                    if (t.who.team != scoring) break;
                    t.who.assists++;
                    break;
                }
            }

            // The CONCEDING keeper is the one defending the goal that was hit. Home attacks +Z in every
            // role (see Footballer.AttackZ), so an Away goal is conceded by Home's keeper.
            int conceding = awayScored ? 0 : 1;
            for (int i = 0; i < _stats.Count; i++)
                if (_stats[i].keeper && _stats[i].team == conceding) _stats[i].conceded++;

            DropTouchState();
        }

        // Forget the ball's history. Called wherever the ball is TELEPORTED rather than played: a
        // kickoff, a goal, the stuck-ball watchdog and the out-of-play reset all move it, and a pending
        // pass or a last-toucher that survives one of those resolves against a completely different
        // phase of play.
        void DropTouchState()
        {
            _touches.Clear();
            _lastTouch = _prevTouch = null;
            _lastWasIntent = false;
            _prevCarrier = null;
            _passBy = null;
            _shotBy = null; _shotSpent = true;
        }

        // Proximity fallback, once a frame. The intent sites note themselves, so this only has to catch
        // what nothing else reports: deflections, headers, a ball rebounding off a body.
        void TrackTouches()
        {
            if (StatsFrozen || _ball == null) return;
            Vector3 b = _ball.transform.position; b.y = 0f;
            float best = SimConfig.StatTouchRadius; ActiveRagdoll near = null;
            for (int i = 0; i < _all.Count; i++)
            {
                var f = _all[i];
                if (f == null || f.Ragdoll == null) continue;
                Vector3 p = f.Pos; p.y = 0f;
                float d = Vector3.Distance(p, b);
                if (d < best) { best = d; near = f.Ragdoll; }
            }
            if (_humanKeeperRagdoll != null && _humanKeeperRagdoll.Pelvis != null)
            {
                Vector3 p = _humanKeeperRagdoll.Pelvis.position; p.y = 0f;
                float d = Vector3.Distance(p, b);
                if (d < best) { best = d; near = _humanKeeperRagdoll; }
            }
            // A CARRIER CHANGE is a reception. Polled off BallController.DribbleCarrier, which is the
            // authoritative answer for both the human's Dribble and an AI's direct claim, so one site
            // covers every way a player can take the ball.
            var carrier = _ball.DribbleCarrier;
            if (carrier != _prevCarrier)
            {
                _prevCarrier = carrier;
                if (carrier != null) NoteTouch(carrier, Touch.Carry);
            }

            if (near == null) return;
            var s = Row(near);
            // A keeper reaching the ball is a keeper touch, which is what the save gate reads.
            NoteTouch(near, s != null && s.keeper ? Touch.Keeper : Touch.Contact);
        }

        /// <summary>
        /// Turn the counters into a 6.0-10.0 rating and pick the man of the match. Called once, at full
        /// time.
        ///
        /// VOLUME TERMS ARE NORMALISED by match length. Length is a pre-match option spanning 2 to 10
        /// minutes, so an unnormalised sum rated a long match far above a short one for identical play.
        /// Goals, assists, goals conceded and the clean sheet are match EVENTS and stay raw.
        /// </summary>
        void FinalizeRatings()
        {
            float lenMul = SimConfig.RatingRefSeconds
                         / Mathf.Max(30f, SimConfig.MatchSeconds);

            for (int i = 0; i < _stats.Count; i++)
            {
                var s = _stats[i];
                // Clamp to the WIRE range first, then rate off the clamped values. Rating from a raw
                // count and shipping a byte would have the client draw a wrapped number beside a rating
                // that was computed from a different one.
                s.goals = Mathf.Clamp(s.goals, 0, 255);
                s.assists = Mathf.Clamp(s.assists, 0, 255);
                s.shots = Mathf.Clamp(s.shots, 0, 255);
                s.passes = Mathf.Clamp(s.passes, 0, 255);
                s.passesDone = Mathf.Clamp(s.passesDone, 0, 255);
                s.tackles = Mathf.Clamp(s.tackles, 0, 255);
                s.saves = Mathf.Clamp(s.saves, 0, 255);

                float raw = SimConfig.RatingBase;
                int lost = Mathf.Max(0, s.passes - s.passesDone);

                if (s.keeper)
                {
                    // A keeper is rated on a DIFFERENT term set, not a modified one: saves and goals
                    // conceded replace goals/shots/tackles outright. Conceding is keeper-only on
                    // purpose - charging outfielders for it would rate a whole losing side down for
                    // team failure.
                    raw += SimConfig.RatingSave * s.saves * lenMul;
                    raw -= SimConfig.RatingConcede * s.conceded;
                    if (s.conceded == 0 && s.saves >= SimConfig.RatingCleanSheetMinSaves)
                        raw += SimConfig.RatingCleanSheet;
                }
                else
                {
                    raw += SimConfig.RatingGoal * s.goals;
                    raw += SimConfig.RatingAssist * s.assists;
                    raw += SimConfig.RatingShot * s.shots * lenMul;
                    // TKL is unreachable for a networked human (net-host skips the local tackle block
                    // entirely), so rating one on it would make the man of the match structurally an AI.
                    if (!s.netControlled) raw += SimConfig.RatingTackle * s.tackles * lenMul;
                }
                raw += SimConfig.RatingPassDone * s.passesDone * lenMul;
                raw -= SimConfig.RatingPassLost * lost * lenMul;

                // Floor(x*10 + 0.5), NOT Mathf.Round: Round is half-to-EVEN, so a raw of 7.25 - which
                // is a common exact value here, not a corner case - displayed as 7.2 and the sequence
                // was visibly non-monotone.
                s.rating = Mathf.Clamp(Mathf.Floor(raw * 10f + 0.5f) / 10f,
                                       SimConfig.RatingMin, SimConfig.RatingMax);
                s.motm = false;
                s.rawRating = raw;
            }

            // MOTM on the UNROUNDED value. The display band holds only 41 steps, so on the rounded one
            // a goalless match resolves by build order rather than by merit. Nobody is named if nobody
            // beat a neutral performance - and then the line simply is not drawn.
            PlayerStat best = null;
            for (int i = 0; i < _stats.Count; i++)
            {
                var s = _stats[i];
                if (s.frozen || s.rawRating <= SimConfig.RatingBase) continue;
                if (best == null || s.rawRating > best.rawRating) best = s;
            }
            if (best != null) best.motm = true;

            // INVARIANT: each team's goals must sum to its score. If they do not, a goal went
            // uncredited and the board contradicts its own header - which is exactly the bug a two-deep
            // touch history used to produce. Loud, because it is silent otherwise.
            int hg = 0, ag = 0;
            for (int i = 0; i < _stats.Count; i++)
                if (_stats[i].team == 0) hg += _stats[i].goals; else ag += _stats[i].goals;
            if (hg != _homeScore || ag != _awayScore)
                Debug.LogWarning($"[scrim stats] goals do not match the score: credited {hg}-{ag}, "
                               + $"scoreboard {_homeScore}-{_awayScore}. A goal was not attributed.");
        }

        // Post-match rendering lives in MatchStatsUI now (see _statsUI above) - WireStats/FromWire
        // just below stay here, since they're the wire serialization, not the renderer.

        // ------------------------------------------------------------- passing
        // The pass power bar: one charge per pass button. Replaces the old pair of bare float timers,
        // which had no notion of a hold being ARMED and so could not tell a charge from a hold carried
        // in off a call-for-pass (see Passing.Bar).
        readonly Passing.Bar _bar = new Passing.Bar();

        /// <summary>The local player's pass bar, for the HUD to draw.</summary>
        public Passing.Bar PassBar => _bar;

        // Match-only landing telegraph: a disc on the turf under where an airborne ball will come
        // down. Built here rather than passed in, so the networked host gets one too without a second
        // wiring site that could be forgotten.
        AimReticle _landing;

        /// <summary>Point the landing disc at the live ball, or hide it. Safe to call every frame.</summary>
        public void DriveLandingReticle(Vector3 ballPos, Vector3 ballVel, bool guided)
        {
            if (_landing == null) return;
            if (_fullTime || guided
                || ballPos.y - SimConfig.BallRadius < SimConfig.ScrimReticleMinHeight
                || !BallController.PredictLanding(ballPos, ballVel, out Vector3 land, out float t)
                || t < SimConfig.ScrimReticleMinTime || t > SimConfig.ScrimReticleMaxTime)
            {
                _landing.Hide();
                return;
            }
            // Clamp onto the playing surface. The box is SEALED, so a ball heading for a wall does not
            // land where the parabola says, and a disc off the turf has no ground under it. A deliberate
            // inaccuracy that is more accurate than the alternative.
            land.x = Mathf.Clamp(land.x, -HalfWidth, HalfWidth);
            land.z = Mathf.Clamp(land.z, -HalfLength, HalfLength);
            _landing.Show(land);
        }

        void BuildLandingReticle()
        {
            var go = new GameObject("LandingReticle");
            go.transform.SetParent(transform, false);
            _landing = go.AddComponent<AimReticle>();
            _landing.Init(Make.Unlit(SimConfig.ScrimReticleTint));
            _landing.Hide();
        }

        void HandlePassInput(bool blocked)
        {
            if (_controlled == null || _humanStriker == null) return;

            bool firstTime = false;
            bool haveBall = !blocked && ControlledCanPass(out firstTime);

            // Step all three buttons whatever the state. With haveBall false this DISARMS and zeroes
            // every charge, which is the whole reason it runs unconditionally.
            bool fired = Passing.StepAll(_bar, _input, haveBall, true, out Passing.PassKind kind, out float c01);

            // Aiming holds the RUN, not the camera: the mouse points the pass while he keeps running
            // where he was going (see Striker.LockRun). Released the moment nothing is charging, so the
            // heading eases back on its own.
            if (_humanStriker != null)
            {
                if (_bar.AnyArmed) _humanStriker.LockRun(_cam != null ? _cam.Yaw : 0f);
                else _humanStriker.ReleaseRun();
            }

            if (haveBall)
            {
                if (fired) PlayPass(kind, c01, firstTime);
                return;
            }

            // No ball: a PRESS is an instant call for a pass instead. The call and the charge share a
            // button deliberately, and Passing.Bar arming only on a press taken WITH the ball is what
            // keeps the pair safe - otherwise holding the call button through the ball's arrival would
            // slide straight into a charge and, at full, fire a maximum-range pass never asked for.
            if (blocked) return;
            if (_input.PassGroundPressed) CallForPass(lofted: false);
            else if (_input.PassLoftedPressed) CallForPass(lofted: true);
        }

        // Terse, because it sits on the HUD next to the player's name.
        public static string PassKindName(Passing.PassKind k)
            => k == Passing.PassKind.Chip ? "CHIP" : k == Passing.PassKind.Air ? "LOFT" : "PASS";

        // Keep an aim point on the playing surface. The match box is SEALED (MatchArena builds
        // walls, lintels and a lid), so an aim past a wall is a pass into a wall: the charge bands are
        // already sized to the smallest pitch, and this catches the rest.
        Vector3 ClampAim(Vector3 aim)
        {
            aim.x = Mathf.Clamp(aim.x, -HalfWidth + 0.5f, HalfWidth - 0.5f);
            aim.z = Mathf.Clamp(aim.z, -HalfLength + 0.5f, HalfLength - 0.5f);
            return aim;
        }

        // Call for a pass: the AI teammate on the ball plays it to the controlled player, led
        // onto his run so it arrives in front of him rather than behind.
        void CallForPass(bool lofted)
        {
            // Rolling 3-per-3s gate, shared with every other call producer. Overflow is dropped,
            // not queued: a call released later would be aimed at a run that has already ended.
            if (!CallLimiter.Allow()) return;
            var carrier = TeammateOnBall();
            if (carrier == null || carrier == _controlled) return;
            Vector3 from = _ball.transform.position;
            Vector3 aim = Passing.Lead(from, _controlled.Pos, _controlled.Vel, lofted, 0.6f, 1f, 1f);
            LaunchPass(aim, lofted ? Passing.PassKind.Air : Passing.PassKind.Ground, 0.6f,
                       carrier.GetComponent<Dribble>(), carrier.Ragdoll,
                       PlayerProfile.PassAccuracyMul, false);
        }

        // The controlled player plays a pass STRAIGHT DOWN THE LOOK RAY, at the range the bar charged
        // to. This replaces Passing.BestTarget, which picked the best TEAMMATE inside an aim cone: the
        // ball goes where the camera points and it is on the player to point it. BestTarget is not
        // deleted - the AI and call-for-pass still use it, because neither has a camera to read.
        //
        // Removing the target snap also removed the safety net that made a scattered pass often still
        // find a body, which is why PassScatterMaxDeg had to come down with it (see SimConfig).
        void PlayPass(Passing.PassKind kind, float charge01, bool firstTime)
        {
            // A first-time ball had no window to charge in - it arrived and was struck - so credit it a
            // floor instead of the near-zero the bar actually holds. See PassFirstTimeChargeFloor.
            if (firstTime) charge01 = Mathf.Max(charge01, SimConfig.PassFirstTimeChargeFloor);
            Vector3 from = _ball.transform.position;
            float yaw = _cam != null ? _cam.Yaw : _controlled.Ragdoll.FacingRotation.eulerAngles.y;
            Vector3 aim = ClampAim(Passing.LookAim(from, yaw, kind, charge01, PlayerProfile.PassPowerMul));
            LaunchPass(aim, kind, charge01, _humanDribble, _controlled.Ragdoll,
                       PlayerProfile.PassAccuracyMul, firstTime);
        }

        // Common launch. Everything about the weight, arc, lead and error lives in Passing, so a
        // human pass, an AI pass and a keeper's throw all behave the same way.
        void LaunchPass(Vector3 aim, Passing.PassKind kind, float charge01, Dribble carry, ActiveRagdoll passer,
                        float accMul, bool firstTime)
        {
            if (carry != null) carry.ForceRelease();
            NotePass(passer);
            Vector3 from = _ball.transform.position;
            float acc = Passing.Accuracy01(accMul, PlayerProfile.PerkMaestro);
            float dist = Vector3.Distance(from, aim);
            float press = Passing.Pressure01(from, TeamList(_controlled != null && _controlled.Team == 0 ? 1 : 0));
            Passing.Launch(_ball, aim, kind, charge01, PlayerProfile.PassPowerMul, passer,
                           Passing.ScatterDeg(acc, dist, press, charge01, firstTime),
                           Passing.Wobble(acc, firstTime));
        }

        /// <summary>
        /// Networked pass entry point: run one body's pass input on the HOST. Same solver as the
        /// local path, driven off that slot's net input instead of the local device.
        /// </summary>
        public void TickNetPass(Footballer f, Striker striker, Dribble carry, IStrikerInput input,
                                Passing.Bar bar, float lookYaw, bool blocked,
                                float powerMul, float accMul, bool maestro)
        {
            if (f == null || striker == null || input == null || _ball == null || bar == null) return;

            bool canPlay = Passing.CanPlay(_ball, f.Pos, carry != null && carry.Carrying, blocked,
                                           out bool firstTime);
            // No early return on a ball-less frame. The bar must still be STEPPED, because that is the
            // only place it disarms; returning here left a charge and its fired latch frozen until the
            // player next had the ball - and with fire-at-full a frozen near-full bar is a misfire.

            // FRESH is what makes fire-at-full safe over the wire. The host feeds this slot's newest
            // frame every tick whether or not one arrived, so a client that goes quiet - paused, typing
            // in quickchat, or dropping packets - leaves its held bits pinned true forever. Charging on
            // a repeat would fill the bar and play a pass the player never asked for.
            bool fresh = input.Fresh;

            bool fired = Passing.StepAll(bar, input, canPlay, fresh, out Passing.PassKind kind, out float c01);

            // Hold this body's run while its owner is aiming, exactly as the local path does - the
            // remote player is looking around to point their pass and their run should not follow it.
            if (bar.AnyArmed) striker.LockRun(lookYaw);
            else striker.ReleaseRun();

            if (canPlay && fired)
                NetPass(f, striker, carry, kind, c01, firstTime, lookYaw, powerMul, accMul, maestro);
        }

        // The passer's OWN stats, supplied per caller exactly as lookYaw is: the host-local slot passes
        // its live PlayerProfile, a remote slot passes what NetSession derived from that slot's node
        // mask. Threading it rather than looking it up keeps this class free of any notion of slots,
        // which is what lets the sim run with no local player at all.
        void NetPass(Footballer f, Striker striker, Dribble carry, Passing.PassKind kind, float charge01,
                     bool firstTime, float lookYaw, float powerMul, float accMul, bool maestro)
        {
            // Same first-time floor as the local path (see PlayPass), so a networked player is not the
            // only one whose first-time balls all arrive as dinks.
            if (firstTime) charge01 = Mathf.Max(charge01, SimConfig.PassFirstTimeChargeFloor);
            Vector3 from = _ball.transform.position;
            var opps  = TeamList(f.Team == 0 ? 1 : 0);
            // Same look-ray aim as the local path, off the yaw the client already sends every frame
            // (InputFrame.lookYaw), so a remote pass goes where that player was actually looking.
            Vector3 aim = ClampAim(Passing.LookAim(from, lookYaw, kind, charge01, powerMul));
            if (carry != null) carry.ForceRelease();
            NotePass(f.Ragdoll);
            // Maestro is carried separately and not folded into accMul, because it is not a bigger
            // number - Accuracy01 SHORT-CIRCUITS to exactly 1 on the perk, while a maxed 1.86 build only
            // reaches 1 through Clamp01. Deriving it from the multiplier would have left a 7 SP capstone
            // silently inert on every networked pass.
            float acc = Passing.Accuracy01(accMul, maestro);
            float dist = Vector3.Distance(from, aim);
            float press = Passing.Pressure01(from, opps);
            Passing.Launch(_ball, aim, kind, charge01, powerMul, f.Ragdoll,
                           Passing.ScatterDeg(acc, dist, press, charge01, firstTime),
                           Passing.Wobble(acc, firstTime));
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

        // Last line of defence. The arena is sealed, but a physics tunnelling event, a bad
        // teleport, or a ball welded to a body that got destroyed could still put it outside the
        // box - and once out, nothing brings it back and the match is over as a contest.
        bool OutOfPlay()
        {
            Vector3 c = _ball.transform.position;
            float zLimit = HalfLength + SimConfig.GoalDepth + 2.5f;
            if (Mathf.Abs(c.x) <= HalfWidth + 2.5f && Mathf.Abs(c.z) <= zLimit
                && c.y > -2f && c.y < 60f) return false;

            _stuckTimer = 0f;
            Vector3 spot = new Vector3(Mathf.Clamp(c.x, -HalfWidth + 3f, HalfWidth - 3f),
                                       SimConfig.ScrimKickoffBallHeight,
                                       Mathf.Clamp(c.z, -HalfLength + 3f, HalfLength - 3f));
            _ball.ResetTo(spot);
            return true;
        }

        // If the ball goes nearly still for a while (jammed against a wall / corner) with no
        // goal, nudge it back to centre so play resumes. The sealed box (walls + over-bar
        // lintels + lid) is what actually keeps it in; this only unjams it.
        float _stuckTimer;
        void StuckBallWatchdog()
        {
            if (_kickoffTimer > 0f) { _stuckTimer = 0f; return; }
            if (OutOfPlay()) return;
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
            // Credit it BEFORE _resolved goes up: _resolved is part of StatsFrozen, so attributing
            // after it would find the tracker already refusing to answer.
            AttributeGoal(awayScored);
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
            // run here; net clients fire the same off replicated score deltas (NetMatch).
            AudioManager.Instance?.OnMatchGoal(_homeScore, _awayScore);
            // Freeze scoring, celebrate, then re-kickoff.
            _kickoffTimer = 3f;
            CancelInvoke(nameof(Kickoff));
            Invoke(nameof(Kickoff), 3f);
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // Full time: stop the ball + all controllers, cancel any pending kickoff, freeze.
        void EndMatch()
        {
            // Rate BEFORE _fullTime goes up, for the same reason OnGoal attributes early: _fullTime is
            // part of StatsFrozen. Update also returns on the clock check well above TrackTouches, so
            // this frame never got a proximity pass - one frame of touches is not worth a special case.
            FinalizeRatings();

            // Single-player only here: a networked HOST also drives this class (see
            // ConfigureNetHost), but in that mode neither _controlled nor _humanKeeperRagdoll is
            // ever assigned (AssignControl is skipped below), so this exact resolution can't find
            // "me" for a net-host - NetMatch records its own MP hook separately, from _game.Stats
            // at its own full-time edge, where _localSlot resolves the right row instead. Same
            // human-body resolution the HUD already uses for the player marker (Hud.PlayerMarker
            // call below).
            if (!_netHost)
            {
                var meRag = _role == SimConfig.MatchRole.Keeper
                            ? _humanKeeperRagdoll
                            : (_controlled != null ? _controlled.Ragdoll : null);
                var my = Row(meRag);
                if (my != null)
                {
                    int myScore = my.team == 0 ? _homeScore : _awayScore;
                    int theirScore = my.team == 0 ? _awayScore : _homeScore;
                    int result = myScore > theirScore ? 1 : (myScore < theirScore ? -1 : 0);
                    CareerStats.RecordMatchEnd(false, result, my.goals, my.assists, my.shots,
                        my.tackles, my.saves, my.conceded, my.passes, my.passesDone, my.motm);
                }
            }

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
            _clock = SimConfig.MatchSeconds;
            Kickoff();
        }

        // ------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            if (_netHost) return;   // the NetMatch driver draws the networked HUD

            // Scale + fit to the window (see MenuScale). Virtual coordinates from here on: use
            // Hud.W / Hud.H, and pair EVERY exit path with Hud.End().
            Hud.Begin();

            // FULL TIME: the board owns the screen. The live furniture is suppressed rather than drawn
            // under it - a scoreboard and a control legend behind a stats table is just clutter, and the
            // board already carries both scores in its card titles.
            if (_fullTime)
            {
                _statsUI.Draw(_stats, _homeScore, _awayScore, MyRow());
                Hud.End();
                return;
            }

            // Broadcast score bug: team blocks either side of the score, clock underneath.
            Hud.Scoreboard("HOME", UITheme.Blue, _homeScore, _awayScore, "AWAY", UITheme.Red, _clock);

            string help = _role == SimConfig.MatchRole.Keeper
                ? "Keeper:  A/D move   Space/LMB/RMB dive   E/Q throw   Reset: R"
                : "WASD move   LMB/RMB shoot   E pass   Q loft   X chip   C tackle   B emote   V ball cam   R reset"
                  + Keybinds.ThirdLegHint(PlayerProfile.Appearance.Adult);
            // Shared banner renderer: it shrinks the font and wraps if the line would run off
            // the right edge, so the controls stay readable and fully on-screen at any resolution.
            Hud.Legend(help);

            // Pass power bar, bottom left. Only while a charge is actually armed, so it is not a
            // permanent fixture: an empty bar on screen at all times reads as a broken one.
            if (_role != SimConfig.MatchRole.Keeper && _bar.Showing(out Passing.PassKind bk, out float bt))
                Hud.PowerBar(PlayerProfile.PlayerName, bt, PassKindName(bk));

            // Shot charge bar, stacked above the pass bar. LMB/RMB (WantsChargedShot) can be held in
            // the same frame as Q/E, so it needs its own slot rather than sharing the pass bar's.
            if (_role != SimConfig.MatchRole.Keeper && _humanStriker != null && _humanStriker.WantsChargedShot)
                Hud.ShotBar(_humanStriker.ShotCharge01, true, _humanStriker.ShotInRange);

            // Shared styled callout (big, colour-coded, shadowed) - same look as every other mode.
            if (_flashTime > 0f) Hud.Flash(_flash, _flashTime / 1.6f);

            // Player indicator: one coloured chevron over the human's head. Single player, so there is
            // exactly one human and it takes slot 0's colour. In Keeper role the human is a bare
            // ActiveRagdoll, not a Footballer (see the AI loop above), so pick the body per role.
            var meRag = _role == SimConfig.MatchRole.Keeper
                        ? _humanKeeperRagdoll
                        : (_controlled != null ? _controlled.Ragdoll : null);
            Hud.PlayerMarker(meRag, Hud.SlotColor(0));

            // Gated on Paused as well as _wheelOpen: only Update is pause-gated, so an already-open
            // wheel kept drawing REAL buttons under the pause menu and they stayed clickable through
            // it (IMGUI has no occlusion; the pause scrim is a plain DrawTexture and eats no events).
            if (_wheelOpen && !PauseMenu.Paused) DrawEmoteWheel();
            Hud.End();
        }
    }
}
