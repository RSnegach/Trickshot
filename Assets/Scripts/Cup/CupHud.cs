using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// The in-round HUD of the cup (design 6.5): the broadcast scoreboard with nation codes and kit
    /// colours, the pips, the role panel, the kick-clock ring around the power meter, callouts
    /// (Hud.Flash), the round-end banner (Hud.Banner), the yellow skip texts, the Tab bracket peek,
    /// the per-role legend, the emote wheel while the local body is freed, and the quick-chat feed
    /// in multiplayer.
    ///
    /// It READS the round through <see cref="CupRoundDriver"/> and the cup through
    /// <see cref="CupDirector"/>, and changes game state in exactly three places: a click to skip
    /// an open window (driver.SkipCelebration, or the director's request on a client), a click to
    /// vote a replay skip (driver.VoteSkipReplay), and an emote pick (the local body's Celebration
    /// plus GameInput.SetEmotePick for the wire). Everything else is presentation.
    ///
    /// Pause contract: nothing draws while the game is FROZEN (a Solo pause); under the MP overlay
    /// pause the HUD keeps drawing beneath the menu, but every local input here (skip clicks, the
    /// B wheel) is cut on <see cref="PauseMenu.Paused"/>.
    ///
    /// Members of the driver that the round agent (C1) adds beyond the skeleton - the Callout
    /// event, ReplayPlaying / ReplaySkipVotes / ReplaySkipNeeded / VoteSkipReplay, LocalBody.Celeb
    /// and the SetPieceTaker - are reached through <see cref="DriverBridge"/>, a late-bound seam so
    /// this file compiles against the skeleton alone and picks the members up the moment they exist.
    /// The same seam finds CupBracketView.DrawMini for the Tab peek, with a built-in fallback card.
    /// </summary>
    public sealed class CupHud : MonoBehaviour
    {
        // ---- tunables (layout lives beside the drawing, per CupTuning's rule) --------------------
        /// <summary>Callout life (the 1.6 s idiom every mode uses).</summary>
        public const float FlashLife = 1.6f;
        /// <summary>The round-end banner shows this long after the decision, then gets out of the way of the win beat / dejection; it returns for the Over phase. (tune)</summary>
        public const float BannerHold = 3f;
        /// <summary>IMGUI depth: behind the director's screens (0) and the loading / intro cards.</summary>
        public const int GuiDepth = 5;
        const float PipD = 11f, PipGap = 5f, PipSdGap = 8f;
        const float MeterW = 320f, MeterH = 22f;

        CupDirector _director;
        GameInput _input;
        CupRoundDriver _driver;
        readonly DriverBridge _bridge = new DriverBridge();
        QuickChatFeed _chat;

        string _flashText, _flashSub;
        float _flashTime;
        string _bannerBig, _bannerSub;
        bool _bannerOn;
        float _bannerAge;
        bool _wheelOpen;
        bool _votedThisReplay, _replayWas;

        static GUIStyle _skip, _ringNum, _peekName, _peekScore, _peekLine, _peekTag;

        /// <summary>The round being drawn (null between rounds).</summary>
        public CupRoundDriver Driver => _driver;
        /// <summary>The emote wheel is open (the cursor is free).</summary>
        public bool WheelOpen => _wheelOpen;

        /// <summary>Create the HUD under <paramref name="root"/>; Bind a driver to draw a round.</summary>
        public static CupHud Create(Transform root, CupDirector director, GameInput input)
        {
            var go = new GameObject("CupHud");
            if (root != null) go.transform.SetParent(root, false);
            var hud = go.AddComponent<CupHud>();
            hud._director = director;
            hud._input = input;
            hud.AttachChat();
            return hud;
        }

        // ================================================================ binding
        /// <summary>Draw this round: subscribes to its events (and its Callout once C1's driver carries one).</summary>
        public void Bind(CupRoundDriver driver)
        {
            Unbind();
            _driver = driver;
            if (driver == null) return;
            driver.PhaseChanged += OnPhase;
            driver.KickResolved += OnKick;
            driver.RoundDecided += OnDecided;
            _bridge.Attach(driver, Flash);
            _flashTime = 0f;
            _bannerOn = false;
            _votedThisReplay = _replayWas = false;
            if (_wheelOpen) SetWheel(false, false);
        }

        public void Unbind()
        {
            if (_wheelOpen) SetWheel(false, true);
            if (_driver != null)
            {
                _driver.PhaseChanged -= OnPhase;
                _driver.KickResolved -= OnKick;
                _driver.RoundDecided -= OnDecided;
            }
            _bridge.Detach();
            _driver = null;
        }

        /// <summary>
        /// Show a callout in the top pill. A second line may ride along after a newline or a '|'
        /// ("HEADS\nGHANA KICK FIRST"): the first part is the flash, the rest its sub-line.
        /// </summary>
        public void Flash(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int cut = text.IndexOfAny(new[] { '\n', '|' });
            if (cut >= 0)
            {
                _flashText = text.Substring(0, cut).Trim();
                _flashSub = text.Substring(cut + 1).Trim();
            }
            else
            {
                _flashText = text;
                _flashSub = null;
            }
            _flashTime = FlashLife;
        }

        public void Flash(string text, string sub)
        {
            _flashText = text;
            _flashSub = sub;
            _flashTime = string.IsNullOrEmpty(text) ? 0f : FlashLife;
        }

        void OnPhase(RoundPhase phase)
        {
            // A new kick arms: the previous decision banner (if any) is stale. The banner is raised
            // by RoundDecided, never by the phase, so a client mirroring a host state agrees.
            if (phase == RoundPhase.Placing || phase == RoundPhase.Armed) _bannerOn = false;
            if ((phase == RoundPhase.Over || phase == RoundPhase.Idle) && _wheelOpen) SetWheel(false, true);
        }

        void OnKick(KickOutcome outcome, CupSide side, int scorerSlot)
        {
            // The driver's Callout carries the verdict wording once C1 lands; until then the
            // skeleton's KickResolved is the only source, so flash the verdict from here.
            if (!_bridge.HasCallout) Flash(CupText.Verdict(outcome));
        }

        void OnDecided(CupSide winner)
        {
            if (_driver == null) return;
            CupSide view;
            bool hasSide = TryViewSide(out view);
            int wa = ScoreOf(winner), wb = ScoreOf(CupSides.Other(winner));
            if (!hasSide || winner == view)
            {
                _bannerBig = CupText.WinLine(NameOf(winner), wa, wb);
            }
            else
            {
                _bannerBig = CupText.KnockedOutLine(ScoreOf(view), ScoreOf(CupSides.Other(view)));
            }
            _bannerSub = _driver.SuddenDeath ? CupText.SuddenDeath : CupStages.Name(StageOf());
            _bannerOn = true;
            _bannerAge = 0f;
        }

        // ================================================================ per frame (input)
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_flashTime > 0f) _flashTime -= dt;
            if (_bannerOn) _bannerAge += dt;

            if (_driver == null || !_driver.Configured)
            {
                if (_wheelOpen) SetWheel(false, false);
                return;
            }

            bool replay = _bridge.ReplayPlaying;
            if (replay && !_replayWas) _votedThisReplay = false;
            _replayWas = replay;

            // The pause menu owns the cursor and the local input while it is up (overlay or not).
            if (PauseMenu.Paused)
            {
                if (_wheelOpen) SetWheel(false, false);
                return;
            }

            bool freed = LocalFreed;
            if (_wheelOpen && !freed) SetWheel(false, true);
            if (freed && _input != null && _input.EmotePressed) SetWheel(!_wheelOpen, true);
            if (_wheelOpen) return;   // the wheel owns the mouse

            if (_input != null && _input.LeftClickPressed && !QuickChatFeed.AnyOpen)
            {
                if (replay)
                {
                    if (CanVoteReplay && !_votedThisReplay)
                    {
                        _votedThisReplay = true;
                        _bridge.VoteSkipReplay();
                    }
                }
                else if (_driver.CanLocalSkip)
                {
                    // The driver skips under Local / Host authority; a client's skip is a request
                    // the director sends to the host (the host's state then closes the window).
                    if (_driver.Authority == RoundAuthority.Client) _director?.SkipCelebration();
                    else _driver.SkipCelebration();
                }
            }
        }

        // ================================================================ drawing
        void OnGUI()
        {
            if (PauseMenu.Frozen) return;
            if (_driver == null || !_driver.Configured) return;
            GUI.depth = GuiDepth;
            Hud.Begin();
            try { DrawAll(); }
            finally { Hud.End(); }
        }

        void DrawAll()
        {
            Styles();
            CupSide view;
            bool hasSide = TryViewSide(out view);
            CupSide left = hasSide ? view : CupSide.A;
            CupSide right = CupSides.Other(left);

            DrawScoreboard(left, right);
            DrawRolePanel(hasSide, view);
            if (_chat != null) _chat.Draw();
            DrawMeterAndClock();
            DrawSkipTexts();
            // Below the scoreboard and its pip rows (12 + 52 + the sub chip + pips), never over them.
            const float FlashTop = 130f;
            Hud.Flash(_flashText, _flashTime / FlashLife, _flashSub, FlashTop);
            if (_bannerOn && (_bannerAge < BannerHold || _driver.Phase == RoundPhase.Over))
                Hud.Banner(_bannerBig, _bannerSub, null);
            Hud.Legend(LegendFor());
            if (TabHeld) DrawBracketPeek(hasSide, view);
            if (_wheelOpen && !PauseMenu.Paused)
            {
                bool open = _wheelOpen;
                CupEmoteWheel.Draw(_bridge.LocalCelebration, _input, Celebration.Pages, ref open);
                if (!open) SetWheel(false, false);   // a pick closed it (the wheel re-captured the cursor itself)
            }
        }

        /// <summary>
        /// The one place the wheel opens or closes: the cursor follows through CupEmoteWheel when
        /// <paramref name="touchCursor"/> (never when the pause menu took it, or when the wheel
        /// already re-captured on a pick), and the driver is told (EmoteWheelOpen) so the local
        /// body's input is suspended while the mouse is on the wheel.
        /// </summary>
        void SetWheel(bool open, bool touchCursor)
        {
            if (touchCursor) CupEmoteWheel.SetOpen(ref _wheelOpen, open);
            else _wheelOpen = open;
            _bridge.SetEmoteWheelOpen(_wheelOpen);
        }

        void DrawScoreboard(CupSide left, CupSide right)
        {
            int nl = NationOf(left), nr = NationOf(right);
            Hud.Scoreboard(CodeOf(nl), ColourOf(nl), ScoreOf(left), ScoreOf(right), CodeOf(nr), ColourOf(nr), -1f, false, _driver.KickLabel);

            // Pips under each team block: 5 regulation slots, sudden-death pips appended after a
            // gap. The left row grows right from its block, the right row grows left, so both
            // stay anchored to their team as sudden death stretches them toward the middle.
            const float boardW = 400f, teamW = 132f;
            float bx = Hud.W * 0.5f - boardW * 0.5f;
            float py = 12f + 52f + 4f + 20f + 6f;   // scoreboard, gap, sub chip, gap
            DrawPips(left, bx + 5f, py, false);
            DrawPips(right, bx + boardW - 5f, py, true);
        }

        static readonly List<KickOutcome> _pipBuf = new List<KickOutcome>(16);

        void DrawPips(CupSide side, float anchorX, float y, bool rightAligned)
        {
            var line = _driver.Line;
            if (line == null) return;
            _pipBuf.Clear();
            for (int i = 0; i < line.Kicks.Count; i++) if (line.Kicks[i].Side == side) _pipBuf.Add(line.Kicks[i].Outcome);
            int total = Mathf.Max(CupTuning.KicksEach, _pipBuf.Count);
            // Sudden death: show the pair in progress too, so the empty slot a side is kicking into
            // exists before its kick lands. Level (both have taken the same number): the next pair
            // opens for both. Behind by one: this side's slot is the one the other side just filled.
            if (_driver.SuddenDeath && !_driver.IsDecided)
            {
                int mine = _pipBuf.Count;
                int other = line.Taken(CupSides.Other(side));
                int want = mine < other ? other : (mine == other ? mine + 1 : mine);
                total = Mathf.Max(total, want);
            }

            float width = total * PipD + (total - 1) * PipGap + (total > CupTuning.KicksEach ? PipSdGap : 0f);
            float x0 = rightAligned ? anchorX - width : anchorX;
            bool live = !_driver.IsDecided && _driver.Kicker == side && _driver.Phase != RoundPhase.Idle;
            int next = _pipBuf.Count;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);

            float x = x0;
            for (int i = 0; i < total; i++)
            {
                if (i == CupTuning.KicksEach) x += PipSdGap;
                var r = new Rect(x, y, PipD, PipD);
                if (i < _pipBuf.Count)
                {
                    UITheme.Disc(r, _pipBuf[i] == KickOutcome.Goal ? UITheme.Green : UITheme.Red);
                }
                else
                {
                    if (live && i == next)
                        UITheme.Disc(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.18f + 0.30f * pulse));
                    UITheme.Disc(r, new Color(1f, 1f, 1f, 0.14f));
                }
                x += PipD + PipGap;
            }
        }

        void DrawRolePanel(bool hasSide, CupSide view)
        {
            var p = Hud.PanelStart(CupText.Title, 3);
            Hud.Stat(ref p, "Stage", CupStages.Name(StageOf()));
            Hud.Stat(ref p, "You", RoleText());
            int n = hasSide ? NationOf(view) : -1;
            string nation = n >= 0 ? CupNations.Name(n) : "-";
            if (nation.Length > 16) nation = CodeOf(n);   // "Bosnia and Herzegovina" does not fit the value column
            Hud.Stat(ref p, "Nation", nation);
        }

        void DrawMeterAndClock()
        {
            if (!_driver.LocalIsTaker) return;
            float meter;
            bool charging;
            bool haveMeter = _bridge.TryGetMeter(out meter, out charging);
            bool ring = _driver.Phase == RoundPhase.Armed && _driver.KickClockRemaining > 0f
                        && _driver.KickClockRemaining <= CupTuning.KickClockRing;
            if (!ring && !(haveMeter && charging)) return;

            var mr = new Rect((Hud.W - MeterW) * 0.5f, Hud.H - 92f, MeterW, MeterH);
            float remaining = _driver.KickClockRemaining;
            float frac = Mathf.Clamp01(remaining / CupTuning.KickClockRing);
            Color ringCol = Color.Lerp(UITheme.Red, UITheme.Gold, frac);

            if (ring)
            {
                // The frame around the meter depletes with the clock and reddens; the dial to its
                // left counts the seconds. Both in the same colour so they read as one clock.
                var frame = new Rect(mr.x - 5f, mr.y - 5f, mr.width + 10f, mr.height + 10f);
                UITheme.Glow(new Rect(frame.x - 18f, frame.y - 14f, frame.width + 36f, frame.height + 28f),
                             new Color(ringCol.r, ringCol.g, ringCol.b, 0.10f + 0.12f * (1f - frac)));
                var faint = new Color(1f, 1f, 1f, 0.10f);
                UITheme.Fill(new Rect(frame.x, frame.y, frame.width, 2f), faint);
                UITheme.Fill(new Rect(frame.x, frame.yMax - 2f, frame.width, 2f), faint);
                UITheme.Fill(new Rect(frame.x, frame.y, 2f, frame.height), faint);
                UITheme.Fill(new Rect(frame.xMax - 2f, frame.y, 2f, frame.height), faint);
                float lit = frame.width * frac;
                UITheme.Fill(new Rect(frame.x, frame.y, lit, 2f), ringCol);
                UITheme.Fill(new Rect(frame.xMax - lit, frame.yMax - 2f, lit, 2f), ringCol);
            }

            Hud.Meter(mr, haveMeter && charging ? meter : 0f, haveMeter && charging ? "POWER  (release to shoot)" : null);

            if (ring) DrawClockDial(mr.x - 32f, mr.center.y, remaining, frac, ringCol);
        }

        static void DrawClockDial(float cx, float cy, float remaining, float frac, Color col)
        {
            const float r = 16f;
            const int segs = 30;
            UITheme.Disc(new Rect(cx - r - 7f, cy - r - 7f, (r + 7f) * 2f, (r + 7f) * 2f), new Color(0.02f, 0.03f, 0.05f, 0.88f));
            int lit = Mathf.CeilToInt(frac * segs);
            for (int i = 0; i < segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;   // from the top, clockwise
                float px = cx + Mathf.Sin(a) * r, py = cy - Mathf.Cos(a) * r;
                bool on = i < lit;
                UITheme.Disc(new Rect(px - 2f, py - 2f, 4f, 4f), on ? col : new Color(1f, 1f, 1f, 0.12f));
            }
            UITheme.Shadowed(new Rect(cx - 20f, cy - 11f, 40f, 22f), Mathf.CeilToInt(remaining).ToString(), _ringNum, col, 0.7f, 1.5f);
        }

        void DrawSkipTexts()
        {
            string text = null;
            if (_bridge.ReplayPlaying)
            {
                if (CanVoteReplay) text = CupText.ClickToSkipVotes(_bridge.ReplaySkipVotes, Mathf.Max(_bridge.ReplaySkipNeeded, 1));
            }
            else if (_driver.CanLocalSkip && (_driver.ScoredWindowOpen || _driver.WinBeatOpen))
            {
                text = CupText.ClickToSkip;
            }
            if (text == null) return;
            float pulse = 0.72f + 0.28f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f));
            var col = UITheme.Gold; col.a = pulse;
            UITheme.Shadowed(new Rect(0f, Hud.H - 66f, Hud.W, 24f), text, _skip, col, 0.7f, 1.5f);
        }

        string LegendFor()
        {
            bool penalties = _driver.Setup != null && _driver.Setup.Format == CupFormat.Penalties;
            if (_driver.LocalIsTaker)
                return penalties ? "HOLD Space power   Mouse aim   WASD spin   Tab bracket"
                                 : "HOLD Space power   Mouse aim   WASD spin   V ball cam   Tab bracket";
            if (_driver.LocalIsKeeper)
                return "WASD move   Mouse aim   LMB/RMB dive/save   Space jump   E/Q throw   Tab bracket";
            if (LocalFreed)
                return "WASD move   B emotes   Mouse look around   Tab bracket";
            if (_driver.LocalInLineup || (_driver.Setup != null && _driver.Setup.LocalHasBody))
                return "Mouse look around   Tab bracket";
            // A bodiless watcher of a HOST-simulated round (a Client driver, no body): Esc only
            // frees the mirrored camera (CupSpectatorView) - the lobby comes back when the host
            // moves on. A lobby spectator of a locally simulated round has no driver and no HUD.
            if (_driver.Authority == RoundAuthority.Client)
                return "Esc  free camera   Mouse orbit   Tab bracket";
            return "Esc  back to lobby   Tab bracket";
        }

        void DrawBracketPeek(bool hasSide, CupSide view)
        {
            var bracket = _director != null ? _director.Bracket : null;
            var data = _driver.Data;
            const float w = 640f, h = 400f;
            var r = new Rect(Hud.W * 0.5f - w * 0.5f, Hud.H * 0.5f - h * 0.5f, w, h);
            UITheme.Glow(new Rect(r.x - 80f, r.y - 60f, r.width + 160f, r.height + 120f), new Color(0f, 0f, 0f, 0.45f));
            Hud.Card(r, CupText.StageTitle(StageOf()));
            var inner = new Rect(r.x + 14f, r.y + 40f, r.width - 28f, r.height - 54f);
            int highlight = _director != null ? _director.LocalEntrant : -1;
            if (bracket != null && DriverBridge.DrawMini(inner, bracket, StageOf(), highlight, data)) return;
            DrawPeekFallback(inner);
        }

        // Until CupBracketView lands: the live round as a card - both nations, the score, the line.
        void DrawPeekFallback(Rect r)
        {
            int na = NationOf(CupSide.A), nb = NationOf(CupSide.B);
            float cy = r.y + 40f;
            var ta = CupNations.Thumb(na);
            var tb = CupNations.Thumb(nb);
            if (ta != null) GUI.DrawTexture(new Rect(r.x + 40f, cy - 24f, 48f, 48f), ta);
            if (tb != null) GUI.DrawTexture(new Rect(r.xMax - 88f, cy - 24f, 48f, 48f), tb);
            UITheme.Shadowed(new Rect(r.x + 96f, cy - 14f, 180f, 28f), NameOf(CupSide.A), _peekName, Hud.Ink, 0.6f, 1f);
            UITheme.Shadowed(new Rect(r.xMax - 276f, cy - 14f, 180f, 28f), NameOf(CupSide.B), _peekName, Hud.Ink, 0.6f, 1f);
            UITheme.Shadowed(new Rect(r.x, cy - 22f, r.width, 44f), _driver.ScoreA + "  -  " + _driver.ScoreB, _peekScore, Hud.Ink, 0.7f, 2f);
            UITheme.Divider(r.x + 20f, cy + 34f, r.width - 40f);

            var line = _driver.Line;
            string kicks = line != null ? CupRoundRules.Describe(line) : "";
            UITheme.Label(new Rect(r.x + 20f, cy + 46f, r.width - 40f, 60f), kicks, _peekLine);
            string tag = _driver.SuddenDeath ? CupText.SuddenDeath : (_driver.IsDecided ? "DECIDED" : _driver.KickLabel);
            UITheme.Label(new Rect(r.x, r.yMax - 30f, r.width, 22f), tag, _peekTag);
        }

        // ================================================================ read helpers
        bool TabHeld => !PauseMenu.Paused && !QuickChatFeed.AnyOpen && Keyboard.current != null && Keyboard.current.tabKey.isPressed;

        /// <summary>The side the local player sees the round from: their own, or the spectated player's. False for a neutral view.</summary>
        bool TryViewSide(out CupSide side)
        {
            side = CupSide.A;
            var s = _driver != null ? _driver.Setup : null;
            if (s == null) return false;
            if (s.LocalHasBody) { side = s.LocalSide; return true; }
            if (_director != null && _driver.Data != null)
            {
                var me = _director.LocalPlayer;
                if (me != null && me.SpectatingSlot >= 0)
                {
                    var p = _director.PlayerAt(me.SpectatingSlot);
                    if (p != null && p.Entrant >= 0)
                    {
                        var ss = _driver.Data.SideOf(p.Entrant);
                        if (ss.HasValue) { side = ss.Value; return true; }
                    }
                }
            }
            return false;
        }

        bool LocalFreed
        {
            get
            {
                if (_driver == null || _driver.Setup == null || !_driver.Setup.LocalHasBody) return false;
                if (_driver.LocalIsFreed) return true;   // the driver's own truth (a freed free-kick teammate too)
                int me = _driver.Setup.LocalSlot;
                if (_driver.ScoredWindowOpen && _driver.LastScorerSlot >= 0 && _driver.LastScorerSlot == me) return true;
                if (_driver.WinBeatOpen)
                {
                    CupSide view;
                    var w = _driver.Winner;
                    if (w.HasValue && TryViewSide(out view) && view == w.Value) return true;
                }
                return false;
            }
        }

        bool CanVoteReplay => _driver != null && _driver.Setup != null && _driver.Setup.LocalHasBody;

        CupStage StageOf() => _driver != null && _driver.Data != null ? _driver.Data.Stage : (_director != null ? _director.Stage : CupStage.RoundOf32);

        int ScoreOf(CupSide side) => side == CupSide.A ? _driver.ScoreA : _driver.ScoreB;

        int NationOf(CupSide side)
        {
            var d = _driver != null ? _driver.Data : null;
            var b = _director != null ? _director.Bracket : null;
            if (d == null || b == null) return -1;
            int e = d.Entrant(side);
            return b.IsValidEntrant(e) ? b.Entrants[e].NationIndex : -1;
        }

        string NameOf(CupSide side)
        {
            int n = NationOf(side);
            return n >= 0 && CupNations.IsValid(n) ? CupNations.Name(n) : "?";
        }

        static string CodeOf(int nation) => nation >= 0 && CupNations.IsValid(nation) ? CupNations.Code(nation) : "---";
        static Color ColourOf(int nation) => nation >= 0 && CupNations.IsValid(nation) ? CupNations.PrimaryColor(nation) : Hud.Dim;

        string RoleText()
        {
            if (_driver.LocalIsTaker) return CupText.Taking;
            if (_driver.LocalIsKeeper) return CupText.Keeping;
            if (_driver.LocalInLineup) return CupText.InTheLineup;
            if (_director != null)
            {
                var me = _director.LocalPlayer;
                if (me != null && me.SpectatingSlot >= 0)
                {
                    var p = _director.PlayerAt(me.SpectatingSlot);
                    return CupText.Watching(p != null ? p.DisplayName : "...");
                }
            }
            if (_driver.Setup != null && _driver.Setup.LocalHasBody) return CupText.InTheLineup;
            return "Watching";
        }

        void AttachChat()
        {
            if (!Multiplayer.IsActive || Multiplayer.Session == null) return;
            _chat = gameObject.AddComponent<QuickChatFeed>();
            _chat.Bind(Multiplayer.Session);
        }

        static void Styles()
        {
            if (_skip != null) return;
            _skip = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            _ringNum = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Hud.Ink } };
            _peekName = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Hud.Ink } };
            _peekScore = new GUIStyle { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Hud.Ink } };
            _peekLine = new GUIStyle { fontSize = 13, alignment = TextAnchor.UpperLeft, wordWrap = true, normal = { textColor = Hud.Dim } };
            _peekTag = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            UIFont.Heavy(_peekScore);
        }

        void OnDestroy()
        {
            Unbind();
        }

        // ================================================================ the late-bound seam
        /// <summary>
        /// Reaches the round-agent's driver members (contract item 3) and CupBracketView.DrawMini
        /// (item 6) by name, so the HUD compiles with the skeleton alone and lights those features
        /// up as soon as the members exist. Every lookup is cached once per type; a missing member
        /// reads as "off" (no replay, no meter, no local celebration) rather than throwing.
        /// Replace with direct calls once every piece is in the tree - each accessor below maps to
        /// exactly one member of the contract.
        /// </summary>
        sealed class DriverBridge
        {
            const BindingFlags Pub = BindingFlags.Public | BindingFlags.Instance;
            static bool _probed;
            static EventInfo _evCallout;
            static MemberInfo _mReplayPlaying, _mReplayVotes, _mReplayNeeded, _mLocalBody, _mBodyCeleb, _mTaker, _mWheelOpen;
            static MethodInfo _voteSkip;
            static bool _miniProbed;
            static MethodInfo _drawMini;

            CupRoundDriver _drv;
            Action<string> _calloutHandler;

            public bool HasCallout { get { Probe(); return _evCallout != null; } }

            public void Attach(CupRoundDriver drv, Action<string> onCallout)
            {
                Probe();
                _drv = drv;
                if (_drv == null || _evCallout == null || onCallout == null) return;
                _calloutHandler = onCallout;
                try { _evCallout.AddEventHandler(_drv, _calloutHandler); }
                catch (Exception e) { CupLog.Warn("CupHud: could not subscribe to Callout: " + e.Message); _calloutHandler = null; }
            }

            public void Detach()
            {
                if (_drv != null && _evCallout != null && _calloutHandler != null)
                {
                    try { _evCallout.RemoveEventHandler(_drv, _calloutHandler); }
                    catch (Exception) { }
                }
                _calloutHandler = null;
                _drv = null;
            }

            public bool ReplayPlaying => _drv != null && Read(_mReplayPlaying, _drv) is bool b && b;
            public int ReplaySkipVotes => _drv != null && Read(_mReplayVotes, _drv) is int v ? v : 0;
            public int ReplaySkipNeeded => _drv != null && Read(_mReplayNeeded, _drv) is int n ? n : 0;

            public void VoteSkipReplay()
            {
                if (_drv == null || _voteSkip == null) return;
                try { _voteSkip.Invoke(_drv, null); }
                catch (Exception e) { CupLog.Warn("CupHud: VoteSkipReplay failed: " + e.Message); }
            }

            /// <summary>Tell the driver the wheel is open (EmoteWheelOpen), so it suspends the local body's input while the cursor is on the wheel.</summary>
            public void SetEmoteWheelOpen(bool open)
            {
                if (_drv == null || _mWheelOpen == null) return;
                try
                {
                    if (_mWheelOpen is PropertyInfo p) p.SetValue(_drv, open);
                    else ((FieldInfo)_mWheelOpen).SetValue(_drv, open);
                }
                catch (Exception e) { CupLog.Warn("CupHud: EmoteWheelOpen write failed: " + e.Message); }
            }

            /// <summary>
            /// The local body's Celebration (LocalBody.Celeb), or null. Null on a CLIENT driver on
            /// purpose: its local body is a kinematic puppet posed from the host's snapshots, and a
            /// Celebration.Play on it would write velocities and control flags to kinematic bones
            /// for nothing - the wheel's pick still rides the input frame (SetEmotePick) to the
            /// host, whose body plays it, and the snapshot poses the puppet with it a round trip later.
            /// </summary>
            public Celebration LocalCelebration
            {
                get
                {
                    if (_drv == null || _mLocalBody == null || _mBodyCeleb == null) return null;
                    if (_drv.Authority == RoundAuthority.Client) return null;
                    object body = Read(_mLocalBody, _drv);
                    return body != null ? Read(_mBodyCeleb, body) as Celebration : null;
                }
            }

            /// <summary>The driver's SetPieceTaker (any public member of that type): its meter and charging state.</summary>
            public bool TryGetMeter(out float meter, out bool charging)
            {
                meter = 0f;
                charging = false;
                if (_drv == null || _mTaker == null) return false;
                var taker = Read(_mTaker, _drv) as SetPieceTaker;
                if (taker == null) return false;
                meter = taker.Meter;
                charging = taker.IsCharging;
                return true;
            }

            /// <summary>CupBracketView.DrawMini(rect, bracket, stage, highlightEntrant, live) when it exists.</summary>
            public static bool DrawMini(Rect r, CupBracket bracket, CupStage stage, int highlightEntrant, CupRound live)
            {
                if (!_miniProbed)
                {
                    _miniProbed = true;
                    var t = Type.GetType("Trickshot.CupBracketView");
                    if (t != null)
                        _drawMini = t.GetMethod("DrawMini", BindingFlags.Public | BindingFlags.Static, null,
                                                new[] { typeof(Rect), typeof(CupBracket), typeof(CupStage), typeof(int), typeof(CupRound) }, null);
                }
                if (_drawMini == null) return false;
                try { _drawMini.Invoke(null, new object[] { r, bracket, stage, highlightEntrant, live }); }
                catch (Exception e) { CupLog.Warn("CupHud: CupBracketView.DrawMini threw: " + e.Message); _drawMini = null; return false; }
                return true;
            }

            static void Probe()
            {
                if (_probed) return;
                _probed = true;
                var t = typeof(CupRoundDriver);
                _evCallout = t.GetEvent("Callout", Pub);
                if (_evCallout != null && _evCallout.EventHandlerType != typeof(Action<string>)) _evCallout = null;
                _mReplayPlaying = Member(t, "ReplayPlaying", typeof(bool));
                _mReplayVotes = Member(t, "ReplaySkipVotes", typeof(int));
                _mReplayNeeded = Member(t, "ReplaySkipNeeded", typeof(int));
                _voteSkip = t.GetMethod("VoteSkipReplay", Pub, null, Type.EmptyTypes, null);
                _mLocalBody = Member(t, "LocalBody", null);
                if (_mLocalBody != null) _mBodyCeleb = Member(TypeOf(_mLocalBody), "Celeb", typeof(Celebration));
                _mTaker = FirstOfType(t, typeof(SetPieceTaker));
                _mWheelOpen = Member(t, "EmoteWheelOpen", typeof(bool));
                if (_mWheelOpen is PropertyInfo wp && !wp.CanWrite) _mWheelOpen = null;
            }

            static MemberInfo Member(Type t, string name, Type want)
            {
                var p = t.GetProperty(name, Pub);
                if (p != null && p.CanRead && (want == null || want.IsAssignableFrom(p.PropertyType))) return p;
                var f = t.GetField(name, Pub);
                if (f != null && (want == null || want.IsAssignableFrom(f.FieldType))) return f;
                return null;
            }

            static MemberInfo FirstOfType(Type t, Type want)
            {
                foreach (var p in t.GetProperties(Pub)) if (p.CanRead && p.GetIndexParameters().Length == 0 && want.IsAssignableFrom(p.PropertyType)) return p;
                foreach (var f in t.GetFields(Pub)) if (want.IsAssignableFrom(f.FieldType)) return f;
                return null;
            }

            static Type TypeOf(MemberInfo m) => m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType;

            static object Read(MemberInfo m, object target)
            {
                if (m == null || target == null) return null;
                try { return m is PropertyInfo p ? p.GetValue(target) : ((FieldInfo)m).GetValue(target); }
                catch (Exception) { return null; }
            }
        }
    }
}
