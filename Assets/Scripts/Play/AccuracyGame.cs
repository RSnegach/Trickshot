using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// ACCURACY challenge mode, in two shapes chosen on the AccuracyModeUI fork
    /// (SimConfig.AccuracyPractice):
    ///
    /// CHALLENGE - a solo THREE-STRIKES run, one shot per round. Each round places the shooter and
    /// a dead ball at a random spot in the shot band - the edge of the 18-yard box out to a few
    /// metres short of 25 yards (SimConfig.AccuracySpotNear/Far) - and a SINGLE target patrols the
    /// goal face DVD-logo style.
    /// The shot has to do two things - go in the goal AND pass through the target - and anything
    /// less is a strike: saved, wide, over, or a goal that missed the target. Three strikes ends the
    /// run. Difficulty is a pure function of the round number and nothing the player configures (see
    /// SimConfig.AccuracyTier): rounds 1-10 give a big slow target and a keeper set to 1%, and every
    /// ten rounds up to 100 the target shrinks 10%, speeds up 10%, and the keeper climbs toward 70%.
    /// Score is the number of rounds cleared, and the high score is the career best for THIS run's
    /// board - CareerStats keeps a separate one with a keeper and without, since beating a keeper is
    /// a different game - so it survives a relaunch.
    ///
    /// PRACTICE - the same shot with none of the game around it. The ball sits on the spot placed
    /// on the pre-match map (and M reopens that map mid-session to move it), the keeper is whatever
    /// the pre-match picker said, and the target's size and pace are the two practice sliders. No
    /// strikes, no score, no end screen: a resolved shot just re-arms.
    ///
    /// The shot itself is the dead-ball free kick the mode has always used (SetPieceTaker: HOLD
    /// Space for the power meter, WASD spin, mouse aim).
    /// </summary>
    public class AccuracyGame : MonoBehaviour
    {
        GameInput _input;
        BallController _ball;
        Striker _striker;
        ActiveRagdoll _strikerRagdoll;
        Goalkeeper _keeper;
        ActiveRagdoll _keeperRagdoll;
        readonly SaveWatch _save = new SaveWatch();   // shared SAVE / EPIC SAVE / MISS verdict
        GameCamera _cam;

        // The set-piece taker: AI aesthetic runup + swing; the player controls the power meter
        // (Space) + WASD spin + mouse aim. It launches the ball by code.
        readonly SetPieceTaker _taker = new SetPieceTaker();
        readonly AccuracyBoard _board = new AccuracyBoard();

        /// <summary>Practice, not the scored challenge. Latched at Configure so a mid-session change
        /// to the static cannot switch the rules under a running game.</summary>
        bool _practice;

        /// <summary>Open goal. Latched with _practice, and for the same reason: it picks WHICH career
        /// best this run can beat, so it must not change under a run in progress.</summary>
        bool _noKeeper;

        enum Phase { Armed, Live, Cooldown }
        Phase _phase;

        float _liveTime, _restTimer, _cooldown;
        bool _hitThisKick;      // did this shot pass through the target?
        bool _goalThisKick;     // ...and did it also go in?

        int _score;             // rounds CLEARED (goal + target). The round number is _score + _strikes + 1.
        int _strikes;
        bool _finished;

        /// <summary>Career best for THIS run's board as it stood before the run, latched at
        /// Configure. EndRun banks the score first, so this is the only way to know a run beat it.</summary>
        int _bestBefore;

        // End-card navigation, injected by GameBootstrap (the same closures the pause menu gets).
        // Replay is in-process (BeginRun), so it needs no callback.
        System.Action _onMatchSetup, _onMainMenu;

        /// <summary>True once the end card has freed the mouse, so the re-capture that Resume does
        /// on unpause can be undone. The card is clickable, so it owns the cursor while it is up.</summary>
        bool _cursorFreed;

        string _flash = "";
        float _flashTime;

        Vector3 _ballSpot;      // dead-ball spot for the current round
        Vector3 _strikerBase;   // striker feet position behind the ball (run-up start)

        // ---- practice: the free-kick map, reopenable on M ----
        bool _mapOpen;
        Vector3 _mapBall, _mapWall;
        int _mapEdit;
        bool _mapRandom;
        static int _mapClosedFrame = -10;
        /// <summary>True while the in-mode map owns Escape (and for the frame after it closes, to
        /// swallow the raw key read that lands after the IMGUI close). PauseMenu checks this so a
        /// press that closes the map does not ALSO open the pause menu - the same contract
        /// GameManager.CrossMapEscapeOwned has for the striker cross map.</summary>
        public static bool MapEscapeOwned { get; private set; }

        const float RunUp       = 3f;     // striker starts this far behind the ball
        const float KickSpeed   = 2.5f;   // ball speed that marks the kick as taken
        const float RestSpeed   = 0.7f;   // ball considered stopped below this
        const float RestHold    = 0.5f;   // seconds at rest before resolving
        const float MaxLiveTime = 5f;     // safety cap so an attempt always resolves
        const float ResetDelay  = 0.9f;   // beat between a resolved shot and the next round

        /// <summary>1-based round currently being played (or just played, once finished).</summary>
        int Round => _score + _strikes + 1;

        public void Configure(GameInput input, BallController ball, Striker striker, ActiveRagdoll strikerRagdoll,
                              Goalkeeper keeper, ActiveRagdoll keeperRagdoll, GameCamera cam,
                              System.Action onMatchSetup = null, System.Action onMainMenu = null)
        {
            _input = input;
            _ball = ball;
            _striker = striker;
            _strikerRagdoll = strikerRagdoll;
            _keeper = keeper;
            _keeperRagdoll = keeperRagdoll;
            _cam = cam;
            _onMatchSetup = onMatchSetup;
            _onMainMenu = onMainMenu;
            _practice = SimConfig.AccuracyPractice;
            _noKeeper = SimConfig.AccuracyNoKeeper;

            // The CHALLENGE is scored, so every run is the SAME SHOT for everybody: maxed shooting
            // and control, and every body-derived baseline evaluated at the default height and
            // weight. It measures aim, not how much skill tree the player bought or how their body
            // sliders happen to sit. PRACTICE is not scored and keeps their own build and body,
            // which is the point of practising with it.
            //
            // Both override a computed RESULT, never the saved profile - the player's tree, size and
            // appearance are untouched. Cleared in OnDestroy.
            SkillTree.MaxShootingOverride = !_practice;
            PlayerProfile.UniformBodyOverride = !_practice;

            // The career best BEFORE this run, so the end card can honestly say a run beat it.
            // RecordAccuracyRoundEnd is void and banks the score before anything reads it back, so
            // after EndRun the stored best has already absorbed _score and the two can never differ.
            _bestBefore = CareerStats.AccuracyBest(_noKeeper);

            // Set pieces get the arcadey loft + curl and stat-scaled assist.
            _ball.SetPieceShot = true;

            // Camera + striker turn axis: same wiring as striker/free-kick mode.
            _cam.SetFollow(_strikerRagdoll.Pelvis.transform, () => _input.Look);
            _striker.SetCameraYaw(() => _cam.Yaw, () => _cam.Pitch);
            _cam.SetMode(GameCamera.Mode.Follow);

            // ONE target: this mode is "hit the moving target", not a gallery of them.
            _board.Scored += OnTargetScored;
            _board.Build(transform, 1, (uint)System.Environment.TickCount | 1u);

            // Practice starts from the spot placed on the pre-match map, and keeps editing it there.
            if (_practice)
            {
                if (SimConfig.SetPiecePlaced)
                {
                    _mapBall = new Vector3(SimConfig.SetPieceBallSpot.x, SimConfig.BallRadius, SimConfig.SetPieceBallSpot.z);
                    _mapWall = SimConfig.SetPieceWallCenter;
                }
                else SetPieceMap.DefaultPlacement(out _mapBall, out _mapWall);
                _mapRandom = SimConfig.SetPieceRandomSpots;
            }

            BeginRun();
        }

        void BeginRun()
        {
            _score = 0;
            _strikes = 0;
            _finished = false;
            _flash = "";
            _flashTime = 0f;
            // Replaying from the end card: the card had the mouse free, and play wants it back.
            if (_cursorFreed) SetCursorFreed(false);
            BeginRound();
        }

        // Set the round up. CHALLENGE derives everything from the round number; PRACTICE takes the
        // spot from the map and the target from the two sliders, and never touches KeeperAbility
        // (the pre-match picker owns it, so a keeper of None stays None).
        void BeginRound()
        {
            if (_practice)
            {
                _ballSpot = _mapRandom
                          ? WithBallRadius(SetPieceMap.RandomSpot(_rng))
                          : WithBallRadius(_mapBall);
                _board.SpawnPatrol(SimConfig.AccuracyPracticeRadius(), SimConfig.AccuracyPracticeSpeed());
            }
            else
            {
                int round = Round;
                SimConfig.KeeperAbility = SimConfig.AccuracyKeeperAbility(round);
                _ballSpot = RandomSpot();
                _board.SpawnPatrol(SimConfig.AccuracyTargetRadius(round), SimConfig.AccuracyTargetSpeed(round));
            }
            RecomputeStrikerBase();
            Arm();
        }

        readonly System.Random _rng = new System.Random();

        static Vector3 WithBallRadius(Vector3 p) => new Vector3(p.x, SimConfig.BallRadius, p.z);

        // Challenge: a spot inside the D - between the front edge of the 18 and the far edge of the
        // arc. The far edge DEPENDS ON X (it is an arc, not a line), so x is picked first and the
        // depth is drawn against that column's own reach; see SimConfig.AccuracySpotFarAt.
        // Measured back from the goal LINE along -Z, the direction the shooter attacks from.
        Vector3 RandomSpot()
        {
            float x = Random.Range(-SimConfig.AccuracySpotHalfW, SimConfig.AccuracySpotHalfW);
            float dist = Random.Range(SimConfig.AccuracySpotNear, SimConfig.AccuracySpotFarAt(x));
            return new Vector3(x, SimConfig.BallRadius, SimConfig.GoalCenter.z - dist);
        }

        void Update()
        {
            if (_input == null) return;

            // The map owns Escape for the frame it closes on, whether or not it is still open.
            MapEscapeOwned = _mapOpen || (Time.frameCount - _mapClosedFrame) <= 1;

            if (PauseMenu.Paused) return;

            // Unpausing over the end card re-captures the mouse: PauseMenu.Resume does that
            // unconditionally, with no notion of a mode that wanted it free. Re-assert here, on the
            // first unpaused frame, or the card's buttons become unclickable after one pause.
            if (_cursorFreed && GameInput.CursorCaptured) GameInput.CaptureCursor(false);

            // PRACTICE only: M opens the placement map mid-session, Escape closes it. The challenge
            // deliberately has no map - its spot is the game.
            if (_practice)
            {
                if (_input.CrossMapPressed) SetMapOpen(!_mapOpen);
                else if (_mapOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    SetMapOpen(false);
                if (_mapOpen) return;   // the map owns the screen: no play behind it
            }

            if (_input.ResetPressed) { BeginRun(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();

            if (_finished)
            {
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            // The taker owns the striker body during a set piece, so the player's Striker
            // locomotion is NOT ticked here - only the taker drives the ragdoll.
            _taker.Tick();
            if (_keeper != null) _keeper.Tick();

            switch (_phase)
            {
                case Phase.Armed:    TickArmed();    break;
                case Phase.Live:     TickLive();     break;
                case Phase.Cooldown: TickCooldown(); break;
            }

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // Opening the map frees the cursor and holds the view still; closing it re-arms the shot on
        // whatever spot was placed, so a move takes effect immediately rather than next round.
        void SetMapOpen(bool open)
        {
            if (_mapOpen == open) return;
            _mapOpen = open;
            GameInput.CaptureCursor(!open);
            if (_cam != null) _cam.FreezeLook = open;
            if (!open)
            {
                _mapClosedFrame = Time.frameCount;
                // Publish it, so leaving the mode and coming back keeps the spot the player chose.
                SimConfig.SetPiecePlaced      = true;
                SimConfig.SetPieceBallSpot    = _mapBall;
                SimConfig.SetPieceWallCenter  = _mapWall;
                SimConfig.SetPieceRandomSpots = _mapRandom;
                BeginRound();
            }
        }

        // Dead ball waiting to be struck: the kick is detected by the ball picking up pace.
        void TickArmed()
        {
            if (_ball.Speed > KickSpeed)
            {
                _phase = Phase.Live;
                CareerStats.RecordAccuracyKick();
                _liveTime = 0f;
                _restTimer = 0f;
                _hitThisKick = false;
                _goalThisKick = false;
                _save.Arm();
            }
        }

        // Watch the struck ball until it goes in, stops, or leaves play, then resolve the round.
        void TickLive()
        {
            _liveTime += Time.deltaTime;
            Vector3 c = _ball.transform.position;

            _save.Poll(_ball, _keeperRagdoll, _keeper != null && _keeper.WasDivingSave);
            if (_ball.Speed < RestSpeed) _restTimer += Time.deltaTime; else _restTimer = 0f;

            // A goal ends the attempt the moment it crosses, so a ball that goes in and rebounds
            // out of the net still counts. The target latch is read at the same moment.
            if (!_goalThisKick && BallInGoal(c)) { _goalThisKick = true; Resolve(); return; }

            bool outOfPlay = c.y < -3f
                             || Mathf.Abs(c.x) > SimConfig.FieldWidth
                             || Mathf.Abs(c.z) > SimConfig.FieldLength;
            bool dead = _restTimer > RestHold || _liveTime > MaxLiveTime;

            if (outOfPlay || dead) Resolve();
        }

        bool BallInGoal(Vector3 c)
        {
            float r = SimConfig.BallRadius, halfW = SimConfig.GoalWidth * 0.5f;
            return c.z - r >= SimConfig.GoalCenter.z && c.z <= SimConfig.GoalCenter.z + SimConfig.GoalDepth
                   && Mathf.Abs(c.x) <= halfW - r && c.y >= r && c.y <= SimConfig.GoalHeight - r;
        }

        // The round's verdict. Clearing it needs BOTH halves - in the goal and through the target -
        // and every other outcome is a strike, including a scored goal that missed the target.
        // PRACTICE keeps the same verdict but spends no strike and banks no score.
        //
        // The callout is deliberately TERSE - "GOAL" or "STRIKE n" and nothing else. It used to
        // spell out the round number and the reason for the miss ("ROUND 4 CLEARED",
        // "STRIKE 2 - MISSED THE TARGET"), which is a sentence to read on a pill that is up for
        // 1.6 seconds while the next round is already arming. Everything it said is on screen
        // anyway and stays there: the round number and the strike pips are both rows of the HUD
        // panel, so the callout only has to name the OUTCOME.
        void Resolve()
        {
            _phase = Phase.Cooldown;
            _cooldown = ResetDelay;
            _board.HideAll();

            bool cleared = _goalThisKick && _hitThisKick;
            if (cleared)
            {
                if (!_practice) _score++;
                AudioManager.Instance?.OnSetPieceGoal(0);
                CrowdCheer.Celebrate();
                Flash("GOAL");
                return;
            }

            AudioManager.Instance?.OnSetPieceMiss(0);
            // PRACTICE cannot say "STRIKE n": it spends no strike, so there is no number to show.
            // "MISS" is the same one-word verdict at the same tier (Hud.KindOf reads both as a
            // failure, so both come out red).
            if (_practice) { Flash("MISS"); return; }

            _strikes++;
            Flash("STRIKE " + _strikes);
        }

        void TickCooldown()
        {
            _cooldown -= Time.deltaTime;
            if (_cooldown > 0f) return;
            if (!_practice && _strikes >= SimConfig.AccuracyStrikes) EndRun();
            else BeginRound();
        }

        // Re-arm: dead ball on this round's spot, striker behind it, keeper home, and the taker
        // armed to read the power meter + WASD spin + mouse aim for this attempt.
        void Arm()
        {
            _ball.ResetTo(_ballSpot);
            _striker.ForceRecover();
            _strikerRagdoll.ResetTo(_strikerBase, Quaternion.identity);   // identity faces +Z (goal)
            if (_keeper != null && _keeperRagdoll != null) _keeper.ResetTo(SimConfig.KeeperStart);
            _taker.Begin(_input, _strikerRagdoll, _ball, _ballSpot, SimConfig.AttackGoalCenter,
                false, -1f,
                () => SetPieceTaker.LookAimPoint(_ballSpot, _cam.Yaw, _cam.Pitch, SimConfig.AttackGoalCenter.z));
            _phase = Phase.Armed;
            AudioManager.Instance?.PlayWhistle();   // shooter set behind the ball
        }

        void RecomputeStrikerBase()
        {
            Vector3 toGoal = SimConfig.GoalCenter - _ballSpot; toGoal.y = 0f;
            Vector3 dir = toGoal.sqrMagnitude > 1e-4f ? toGoal.normalized : Vector3.forward;
            _strikerBase = new Vector3(_ballSpot.x, 0f, _ballSpot.z) - dir * RunUp;
        }

        // The target was struck. This only LATCHES - the round is graded in Resolve, because a
        // target hit on its own does not clear anything without the goal to go with it.
        void OnTargetScored(int points, int index)
        {
            if (_finished || _hitThisKick) return;
            _hitThisKick = true;
            CareerStats.RecordAccuracyTargetHit();
        }

        void EndRun()
        {
            _finished = true;
            CareerStats.RecordAccuracyRoundEnd(_score, _noKeeper);   // beats only its own board
            _board.HideAll();
            _taker.Reset();
            // The end card is CLICKABLE, so the run has to hand the mouse back - the same trade the
            // placement map makes in SetMapOpen. Play is over, so nothing wants a locked cursor.
            SetCursorFreed(true);
        }

        // Free or re-capture the mouse for the end card, and remember that we did. Kept in one
        // place because the pause menu re-captures unconditionally on Resume with no notion of what
        // the mode wanted, so the card has to re-assert (see Update).
        void SetCursorFreed(bool freed)
        {
            _cursorFreed = freed;
            GameInput.CaptureCursor(!freed);
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        void OnDestroy()
        {
            // Never leave the mode holding Escape or a frozen camera for the next screen.
            MapEscapeOwned = false;
            // GLOBAL statics this mode borrowed: hand them back, or every later mode is shot with
            // maxed stats on a default body.
            SkillTree.MaxShootingOverride = false;
            PlayerProfile.UniformBodyOverride = false;
        }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;

            // Practice map: it owns the screen while open, exactly as the striker cross map does.
            if (_practice && _mapOpen && !PauseMenu.Paused) { DrawMap(); return; }

            Hud.Begin();

            if (_practice)
            {
                // No score, no strikes, no high score - none of it means anything here. Just what
                // the target is set to, so a session's difficulty is readable at a glance.
                var pp = Hud.PanelStart("ACCURACY - PRACTICE", 2);
                Hud.Stat(ref pp, "Target size", Mathf.RoundToInt(SimConfig.AccuracyPracticeSize01 * 100f).ToString());
                Hud.Stat(ref pp, "Target speed", Mathf.RoundToInt(SimConfig.AccuracyPracticeSpeed01 * 100f).ToString());
                Hud.Legend("HOLD Space power   Mouse aim   WASD spin   M move the ball   V ball cam   R restart");
                Hud.Flash(_flash, _flashTime / 1.6f);
                DrawPowerMeter();
                Hud.End();
                return;
            }

            // Score, and the high score under it. Nothing else: this mode has no clock, no kick
            // count and no target tally to report.
            // The high score is the one for THIS run's board - with a keeper, or open goal - since
            // beating a keeper is a different game and a shared number would only ever show the
            // easier record.
            int best = CareerStats.AccuracyBest(_noKeeper);
            var p = Hud.PanelStart("ACCURACY", _finished ? 2 : 4);
            Hud.Stat(ref p, "Score", _score.ToString());
            Hud.Stat(ref p, _noKeeper ? "High (open goal)" : "High (keeper)",
                     Mathf.Max(_score, best).ToString());

            if (_finished)
            {
                DrawEndCard();
                Hud.End();
                return;
            }

            Hud.Stat(ref p, "Round", Round.ToString());
            DrawStrikesRow(ref p);
            Hud.Legend("HOLD Space power   Mouse aim   WASD spin   V ball cam   R restart");
            Hud.Flash(_flash, _flashTime / 1.6f);
            DrawPowerMeter();
            Hud.End();
        }

        // ------------------------------------------------------------ end card (CHALLENGE only)
        // The scored run's result, and the three ways out of it. This replaces a Hud.Banner that
        // said "Press R to play again" and offered nothing else - a dead end on a mode whose whole
        // point is the next attempt.
        //
        // Practice never reaches here: EndRun is gated on !_practice in TickCooldown, and the
        // practice branch of OnGUI returns above this. A practice session has no score to card.
        //
        // The buttons are the SAME closures the pause menu is given (GameBootstrap wires both), so
        // this is a shortcut to paths that already work rather than a second implementation of
        // them. Replay is in-process: BeginRun re-runs the mode without a rebuild, which is what
        // R has always done.
        static GUIStyle _endBtn, _endBig, _endKey, _endVal, _endTag;
        void DrawEndCard()
        {
            // The pause menu draws its own full-screen scrim and buttons, and IMGUI gives an
            // overlapping click to whichever control was drawn first. Yield the screen entirely
            // rather than stack two sets of buttons - the pause menu offers these same three.
            if (PauseMenu.Paused) return;

            if (_endBtn == null)
            {
                _endBtn = new GUIStyle(GUI.skin.button) { fontSize = 19, fontStyle = FontStyle.Bold };
                _endKey = new GUIStyle { fontSize = 14, alignment = TextAnchor.MiddleCenter,
                                         normal = { textColor = UITheme.Dim } };
                _endTag = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold,
                                         alignment = TextAnchor.MiddleCenter,
                                         normal = { textColor = UITheme.Gold } };
                // The big figures go on the real bold cut, as every other large HUD number does -
                // synthetic bold is a smear at this size. UIFont.Heavy also clears fontStyle.
                _endBig = new GUIStyle { fontSize = 54, alignment = TextAnchor.MiddleCenter,
                                         normal = { textColor = UITheme.Ink } };
                _endVal = new GUIStyle { fontSize = 38, alignment = TextAnchor.MiddleCenter,
                                         normal = { textColor = UITheme.Ink } };
                UIFont.Heavy(_endBig);
                UIFont.Heavy(_endVal);
            }

            bool newBest = _score > _bestBefore;
            int best = Mathf.Max(_score, _bestBefore);

            const float w = 520f, h = 340f;
            float x = Hud.W * 0.5f - w * 0.5f, y = Hud.H * 0.5f - h * 0.5f;

            UITheme.Scrim(Hud.W, Hud.H, 0.55f, w + 200f);
            UITheme.Panel(new Rect(x, y, w, h), newBest ? UITheme.Gold : UITheme.Blue);

            UITheme.Shadowed(new Rect(x, y + 22f, w, 56f), "RUN OVER", _endBig,
                             newBest ? UITheme.Gold : UITheme.Ink, 0.75f, 2.5f);

            // A record run says so where the eye already is, instead of making the player compare
            // two numbers below it.
            if (newBest)
                UITheme.Label(new Rect(x, y + 78f, w, 20f), "NEW PERSONAL BEST", _endTag);

            UITheme.Divider(x + 40f, y + 104f, w - 80f);

            // The three numbers that describe the run: what it scored, the board it was scored on,
            // and the record it was measured against. Strikes are always AccuracyStrikes here (the
            // run ended because they ran out), so the count is the board, not a variable.
            float colW = (w - 80f) / 3f, cx = x + 40f, ny = y + 118f;
            Cell(cx,              ny, colW, "ROUNDS CLEARED", _score.ToString());
            Cell(cx + colW,       ny, colW, "STRIKES",        _strikes + " / " + SimConfig.AccuracyStrikes);
            Cell(cx + colW * 2f,  ny, colW, _noKeeper ? "BEST (OPEN GOAL)" : "BEST (KEEPER)", best.ToString());

            // Buttons. Replay first - it is what a player who just lost a run wants, and the run is
            // short. Main Menu last and tinted bad, matching the pause menu's own ordering.
            const float bw = 148f, bh = 46f, gap = 12f;
            float total = bw * 3f + gap * 2f;
            float bx = x + w * 0.5f - total * 0.5f, by = y + h - bh - 30f;

            if (UITheme.Button(new Rect(bx, by, bw, bh), "Replay", _endBtn))
                BeginRun();
            if (UITheme.Button(new Rect(bx + bw + gap, by, bw, bh), "Match Setup", _endBtn))
                _onMatchSetup?.Invoke();
            if (UITheme.Button(new Rect(bx + (bw + gap) * 2f, by, bw, bh), "Main Menu", _endBtn, bad: true))
                _onMainMenu?.Invoke();

            UITheme.Hint(new Rect(x + 20f, by + bh + 2f, w - 40f, 22f), "R replays   Esc pause menu");
        }

        // One labelled figure on the end card.
        void Cell(float x, float y, float w, string key, string val)
        {
            UITheme.Label(new Rect(x, y, w, 18f), key, _endKey);
            UITheme.Shadowed(new Rect(x, y + 20f, w, 44f), val, _endVal, UITheme.Ink, 0.6f, 2f);
        }

        // The same placement panel the pre-match screen shows, centred over the paused-looking mode.
        // Closing it (M or Escape) re-arms on the new spot - see SetMapOpen.
        void DrawMap()
        {
            MenuScale.Begin();
            float w = MenuScale.Width, h = MenuScale.Height;
            UITheme.Scrim(w, h, 0.55f, 700f);

            const float panelW = 340f, mapH = 300f;
            float px = w * 0.5f - panelW * 0.5f, py = h * 0.5f - (mapH + 108f) * 0.5f - 20f;
            SetPieceMap.DrawSetupPanel(px, py, panelW, mapH,
                                       ref _mapBall, ref _mapWall, ref _mapEdit, ref _mapRandom,
                                       "Random spot each attempt.", showWall: false);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(px + panelW * 0.5f - 90f, py + mapH + 118f, 180f, 40f), "Done", btn))
                SetMapOpen(false);

            MenuScale.End();
        }

        // Strikes as filled/empty pips, drawn as a PANEL ROW so it sits on the panel's dark plate.
        // A baseball count reads faster than a number, and this is the only thing standing between
        // the player and the end screen - but it used to be drawn BELOW the panel, as bare text over
        // whatever the camera was showing, and over the crowd it was unreadable. Mirrors Hud.Stat's
        // own geometry (13px pad, 21px row, 23px stride) so it lines up with the stats above it.
        void DrawStrikesRow(ref Hud.P p)
        {
            const float pad = 13f, rowH = 21f, stride = 23f, dotR = 5.5f, gap = 8f;
            var r = new Rect(p.x + pad, p.row, p.w - pad * 2f, rowH);

            var key = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft,
                                                     normal = { textColor = UITheme.Dim } };
            UITheme.Label(r, "Strikes", key);

            // Pips right-aligned, where a stat's value would sit. Disc, not Dot or Chip: Dot is a
            // square Fill wrapped in a wide glow that bleeds into its neighbours, and Chip is a
            // rounded SQUARE. These are circles with no glow.
            int n = SimConfig.AccuracyStrikes;
            float d = dotR * 2f;
            float total = n * d + (n - 1) * gap;
            float px = r.xMax - total;
            for (int i = 0; i < n; i++)
                UITheme.Disc(new Rect(px + i * (d + gap), r.center.y - dotR, d, d),
                             i < _strikes ? UITheme.Red : new Color(1f, 1f, 1f, 0.22f));

            p.row += stride;
        }

        // Power meter while charging, mirroring the free-kick HUD.
        void DrawPowerMeter()
        {
            if (!_taker.IsCharging) return;
            Hud.Meter(_taker.Meter, "POWER  (release to shoot)");
        }
    }
}
