using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The cup lobby (design 6.3): Head to Head between stages, and Solo's stage-complete screen
    /// with one row. A 640-wide gold panel titled with the stage, one 46 px row per entrant human
    /// (flag, name, status, Spectate while they play), the local row lit with its slot colour,
    /// then the stage's simulated results revealing row by row under "Simulating the rest of the
    /// stage", a gate line, and the screen-pinned footer: Quit to Menu / View Bracket / Customize
    /// / Ready (Continue in Solo).
    ///
    /// Everything shown comes from the director's read model (Players, Bracket, Stage), so the
    /// host and every client draw the same lobby; the buttons call intents only: SetReady,
    /// Spectate / StopSpectating, QuitToMenu. Customize has no director intent (the skeleton has
    /// none), so the flow that owns the customize path sets <see cref="OnCustomizeRequested"/>;
    /// the button is disabled until it does.
    ///
    /// Modal states (the View Bracket overlay and the Quit confirm) never remove the panel's
    /// controls - they disable them with GUI.enabled and draw on top - so the control list is the
    /// same on every IMGUI pass. Every callback fires after MenuScale.End().
    /// </summary>
    public class CupLobbyUI : MonoBehaviour
    {
        const float PanelW = 640f;
        const float HeadH = 76f;
        const float RowH = 46f;            // design 6.3: rows of 46 px
        const float SimRowH = 24f;
        const float SimHeadH = 34f;
        static readonly Color BracketBackdrop = new Color(0.05f, 0.06f, 0.09f, 1f);   // opaque plate behind the View Bracket overlay
        const float FooterH = 72f;         // DrawNav's band: buttons at Height - 72

        public static CupLobbyUI Create(Transform root, CupDirector director)
        {
            var go = new GameObject("CupLobbyUI");
            if (root != null) go.transform.SetParent(root, false);
            var ui = go.AddComponent<CupLobbyUI>();
            ui.Init(director);
            return ui;
        }

        /// <summary>Customize (appearance only). Null = the button is disabled. Fired after the GUI pass.</summary>
        public Action OnCustomizeRequested { get; set; }
        /// <summary>The View Bracket overlay is up.</summary>
        public bool BracketOpen => _bracketOpen;
        /// <summary>A Quit confirm card is up.</summary>
        public bool ConfirmOpen => _confirmAct != null;

        /// <summary>
        /// A lobby overlay (View Bracket / the Quit confirm) is up and closes on Esc, so the pause
        /// menu must not also open on that press (PauseMenu checks CupEscape.Owned). Held one frame
        /// past the close for the raw key read that can land a frame after the IMGUI event.
        /// </summary>
        public static bool EscapeOwned => s_modalOpen > 0 || Time.frameCount <= s_escGraceFrame;
        static int s_modalOpen;
        static int s_escGraceFrame = -1;
        bool _ownsEsc;

        void SyncEscOwnership()
        {
            bool want = !_closed && (_bracketOpen || _confirmAct != null);
            if (want == _ownsEsc) return;
            _ownsEsc = want;
            if (want) s_modalOpen++;
            else
            {
                if (s_modalOpen > 0) s_modalOpen--;
                s_escGraceFrame = Time.frameCount + 1;
            }
        }

        CupDirector _director;
        Action _draw;
        bool _hooked, _closed, _wasPaused;
        bool _bracketOpen;

        // Confirm card, the PauseMenu.DrawConfirm shape (Cancel lit by default, Confirm red).
        Action _confirmAct;
        string _confirmTitle, _confirmBody;
        bool _confirmYes;

        readonly List<CupPlayer> _rows = new List<CupPlayer>();
        readonly List<CupRound> _simRounds = new List<CupRound>();

        static GUIStyle _titleSt, _nameSt, _statusSt, _btnSt, _rowBtnSt, _simCodeSt, _simScoreSt, _gateSt, _confirmTitleSt, _confirmBtnSt;

        void Init(CupDirector director)
        {
            _director = director;
            if (_director != null) CupStatsLedger.Attach(_director);   // idempotent; belt and braces
            GameInput.CaptureCursor(false);
            _draw = Draw;
            if (_director != null) { _director.AddGuiHook(_draw); _hooked = true; }
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            SyncEscOwnership();
            if (_hooked && _director != null) _director.RemoveGuiHook(_draw);
            _hooked = false;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            _closed = true;
            SyncEscOwnership();
            if (_hooked && _director != null) _director.RemoveGuiHook(_draw);
            _hooked = false;
        }

        void Update()
        {
            bool paused = PauseMenu.Paused;
            if (_wasPaused && !paused) GameInput.CaptureCursor(false);
            _wasPaused = paused;
            SyncEscOwnership();   // before PauseMenu's poll can matter for a fresh press

            // The row lists are rebuilt once per frame here, never inside a GUI pass, so the
            // number of controls cannot change between IMGUI's Layout and event passes.
            _rows.Clear();
            _simRounds.Clear();
            if (_director == null) return;
            var players = _director.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.Left && !p.ReplacedByAi) continue;   // gone without a nation to hand over: nothing to show
                _rows.Add(p);
            }
            var b = _director.Bracket;
            if (b != null && _director.Style != CupStyle.Coop)
            {
                var ai = b.AiRounds(_director.Stage);
                for (int i = 0; i < ai.Count; i++) if (ai[i].Done) _simRounds.Add(ai[i]);
            }
        }

        void OnGUI()
        {
            if (!_hooked) Draw();
        }

        void Draw()
        {
            if (_closed) return;
            EnsureStyles();
            MenuScale.Begin();
            Action fire = null;
            float w = MenuScale.Width, h = MenuScale.Height;
            bool paused = PauseMenu.Paused;
            var e = Event.current;

            UITheme.Scrim(w, h, 0.42f, PanelW + 260f);

            // Esc: closes the overlay / cancels the confirm. Handled before any control; latched.
            bool esc = false;
            if (!paused && e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && (_bracketOpen || _confirmAct != null))
            {
                esc = true;
                e.Use();
            }

            bool modal = _bracketOpen || _confirmAct != null;
            GUI.enabled = !paused && !modal;

            var d = _director;
            var b = d != null ? d.Bracket : null;
            var stage = d != null ? d.Stage : CupStage.RoundOf32;
            var me = d != null ? d.LocalPlayer : null;
            bool solo = d == null || d.Style == CupStyle.Solo;

            // ---- panel geometry ----
            // The finished AI rounds of the stage sit under the player rows in one or two columns.
            // Row heights vary: a sudden-death shootout wraps its pips onto extra rows underneath
            // (CupUiKit.PipRows), so each column is laid out cumulatively and the block is as tall
            // as its taller column.
            int simCols = _simRounds.Count > 8 ? 2 : 1;
            int simRowsN = simCols == 0 ? 0 : Mathf.CeilToInt(_simRounds.Count / (float)simCols);
            float simBodyH = 0f;
            for (int c = 0; c < simCols; c++)
            {
                float colH = 0f;
                for (int i = c * simRowsN; i < Mathf.Min(_simRounds.Count, (c + 1) * simRowsN); i++) colH += SimRowHFor(_simRounds[i]);
                simBodyH = Mathf.Max(simBodyH, colH);
            }
            float simH = _simRounds.Count > 0 ? SimHeadH + simBodyH + 6f : 0f;
            float rowsH = Mathf.Max(1, _rows.Count) * RowH;
            float panelH = HeadH + rowsH + 10f + simH + 18f;
            float footerY = h - FooterH;
            float gateY = footerY - 34f;
            float px = w * 0.5f - PanelW * 0.5f;
            float py = Mathf.Max(22f, (gateY - 10f - panelH) * 0.5f);

            UITheme.Panel(new Rect(px, py, PanelW, panelH), UITheme.Gold);
            UITheme.Shadowed(new Rect(px + 24f, py + 14f, PanelW - 48f, 36f), CupText.StageTitle(stage), _titleSt, UITheme.Ink, 0.7f, 2f);

            // ---- rows ----
            float ry = py + HeadH;
            for (int i = 0; i < _rows.Count; i++)
            {
                DrawRow(_rows[i], px, ry, e, ref fire);
                ry += RowH;
            }
            if (_rows.Count == 0) UITheme.Hint(new Rect(px, ry, PanelW, RowH), "No players");
            ry += 10f;

            // ---- the stage's other results (the AI rounds), plain and immediate ----
            if (_simRounds.Count > 0)
            {
                UITheme.Section(new Rect(px + 24f, ry + 8f, PanelW - 48f, 18f), "RESULTS");
                ry += SimHeadH;
                float colW = (PanelW - 48f) / simCols;
                for (int c = 0; c < simCols; c++)
                {
                    float y = ry;
                    for (int i = c * simRowsN; i < Mathf.Min(_simRounds.Count, (c + 1) * simRowsN); i++)
                    {
                        float rh = SimRowHFor(_simRounds[i]);
                        DrawSimRow(_simRounds[i], b, px + 24f + c * colW, y, colW, rh, 1f);
                        y += rh;
                    }
                }
            }

            // ---- gate line ----
            string gate = GateLine(d, b, stage, me, solo);
            if (!string.IsNullOrEmpty(gate)) UITheme.Label(new Rect(0f, gateY, w, 24f), gate, _gateSt);

            // ---- footer, screen-pinned like PrematchUI.DrawNav ----
            DrawFooter(w, footerY, d, me, solo, ref fire);

            GUI.enabled = true;

            // ---- modals, drawn last so they sit over the panel ----
            if (_bracketOpen)
            {
                bool close = DrawBracketOverlay(w, h, d, b, stage) || esc;
                if (close) fire = () => _bracketOpen = false;
                // (a click on the overlay is its own control; the panel below is disabled)
            }
            else if (_confirmAct != null)
            {
                DrawConfirm(w, h, esc, ref fire);
            }

            MenuScale.End();
            fire?.Invoke();   // may destroy this object (Quit); nothing after it touches `this`
            if (!_closed) SyncEscOwnership();   // an overlay opened/closed by this pass owns Esc from now
        }

        void DrawRow(CupPlayer p, float px, float ry, Event e, ref Action fire)
        {
            bool outerEnabled = GUI.enabled;
            var d = _director;
            bool isMe = d != null && p.Slot == d.LocalSlot;
            var row = new Rect(px + 20f, ry, PanelW - 40f, RowH);

            if (isMe)
            {
                // The local row: lit band + the slot-colour spine, findable at a glance in eight rows.
                UITheme.Fill(new Rect(row.x - 6f, row.y, row.width + 12f, RowH - 2f), CupUiKit.LitBand);
                CupUiKit.Spine(new Rect(row.x - 6f, row.y, 3f, RowH - 2f), Hud.SlotColor(p.Slot));
            }
            else UITheme.Divider(row.x, row.yMax - 2f, row.width);

            CupUiKit.Flag(new Rect(row.x + 6f, row.y + 3f, 40f, 40f), p.Nation, p.ReplacedByAi ? 0.5f : 1f);
            _nameSt.normal.textColor = p.ReplacedByAi ? UITheme.Dim : UITheme.Ink;
            UITheme.Label(new Rect(row.x + 56f, row.y, 176f, RowH), p.DisplayName + (isMe ? "  (you)" : ""), _nameSt);

            Color statusCol;
            string status = StatusOf(p, out statusCol);
            _statusSt.normal.textColor = statusCol;
            UITheme.Label(new Rect(row.x + 236f, row.y, row.width - 236f - 112f, RowH), status, _statusSt);

            // The row button: Spectate while that player plays (Head to Head), Stop while we
            // watch them. Allocated on every row, parked off-screen and disabled when it does not
            // apply - a control that appears between passes breaks every click on the screen.
            bool h2h = d != null && d.Style == CupStyle.HeadToHead;
            bool watching = d != null && d.LocalPlayer != null && d.LocalPlayer.SpectatingSlot == p.Slot;
            bool canSpectate = h2h && !isMe && p.Active && p.Playing;
            bool show = canSpectate || (h2h && watching);
            var br = show ? new Rect(row.xMax - 104f, row.y + 8f, 96f, RowH - 16f) : new Rect(-1000f, -1000f, 10f, 10f);
            GUI.enabled = outerEnabled && show;
            if (UITheme.Button(br, watching ? "Stop" : CupText.Spectate, _rowBtnSt))
            {
                int slot = p.Slot;
                if (watching) fire = () => d.StopSpectating();
                else fire = () => d.Spectate(slot);
            }
            GUI.enabled = outerEnabled;
        }

        /// <summary>The status cell text and colour for a row (design 6.3's status values).</summary>
        string StatusOf(CupPlayer p, out Color col)
        {
            var d = _director;
            var b = d != null ? d.Bracket : null;
            col = UITheme.Dim;
            if (p.ReplacedByAi) return CupText.AiTag;
            if (p.Playing)
            {
                col = UITheme.Gold;
                string opp = CupNations.IsValid(p.LiveOpponentNation) ? CupNations.Code(p.LiveOpponentNation) : "?";
                return CupText.StatusPlaying(opp, p.LiveScoreFor, p.LiveScoreAgainst, Mathf.Max(1, p.LiveKick));
            }
            if (p.IsSpectating)
            {
                var t = d.PlayerAt(p.SpectatingSlot);
                return CupText.StatusSpectating(t != null ? t.DisplayName : "?");
            }
            if (b != null && p.Entrant >= 0)
            {
                // Out: the round that knocked them out (any earlier stage). In: this stage's round.
                CupStage st = d.Stage;
                if (p.Out)
                {
                    var at = b.EliminatedAt(p.Entrant);
                    if (at.HasValue) st = at.Value;
                }
                var r = b.RoundOfEntrant(st, p.Entrant);
                if (r != null && r.Done)
                {
                    var side = r.SideOf(p.Entrant) ?? CupSide.A;
                    int own = r.ScoreOf(side), theirs = r.ScoreOf(CupSides.Other(side));
                    if (r.WinnerEntrant == p.Entrant)
                    {
                        col = UITheme.Green;
                        return CupText.StatusWon(own, theirs, r.SuddenDeath);
                    }
                    col = UITheme.Red;
                    int opp = r.OpponentOf(p.Entrant);
                    return CupText.StatusOut(own, theirs, b.IsValidEntrant(opp) ? b.Entrants[opp].Code : "?");
                }
                if (r != null && r.Ready && !r.Done)
                {
                    // Drawn but not played yet (a head-to-head round waiting its turn).
                    int opp = r.OpponentOf(p.Entrant);
                    return "Up next vs " + (b.IsValidEntrant(opp) ? b.Entrants[opp].Code : "?");
                }
            }
            if (p.Ready) { col = UITheme.Green; return CupText.Ready; }
            return "";
        }

        const float SimPip = 6f, SimPipGap = 2f;

        /// <summary>
        /// The height of one result row: the base row plus one pip row step for every extra row of
        /// pips a long shootout wraps onto (a regulation or early-finish round is exactly SimRowH).
        /// </summary>
        static float SimRowHFor(CupRound r)
            => SimRowH + (CupUiKit.PipRows(r) - 1) * CupUiKit.PipRowStep(SimPip, SimPipGap);

        /// <summary>
        /// One simulated AI round: flag + code, its pips, the score, the other side's pips, code + flag.
        /// Flags, codes and the score centre on the full row height; the pips start on the first
        /// pip row and wrap downward, which is what makes a sudden-death line taller, never wider.
        /// </summary>
        void DrawSimRow(CupRound r, CupBracket b, float x, float y, float w, float rowH, float alpha)
        {
            if (alpha <= 0.01f || b == null) return;
            float cy = y + rowH * 0.5f;                 // flags / codes / score
            float pipCy = y + SimRowH * 0.5f;           // first pip row
            const float flag = 18f, pip = SimPip, pipGap = SimPipGap;
            float pipsW = CupTuning.KicksEach * (pip + pipGap) - pipGap;
            float scoreW = 64f;
            // measure the two codes so the middle stays centred whatever the letters
            _simCodeSt.normal.textColor = UITheme.Ink;
            float codeW = 34f;
            float total = flag + 4f + codeW + 6f + pipsW + 6f + scoreW + 6f + pipsW + 6f + codeW + 4f + flag;
            float sx = x + (w - Mathf.Min(w, total)) * 0.5f;

            int ea = r.EntrantA, eb = r.EntrantB;
            bool hasA = b.IsValidEntrant(ea), hasB = b.IsValidEntrant(eb);
            bool aWon = r.WinnerEntrant == ea;
            var winCol = UITheme.Ink; winCol.a = alpha;
            var loseCol = UITheme.Dim; loseCol.a = alpha;

            CupUiKit.Flag(new Rect(sx, cy - flag * 0.5f, flag, flag), hasA ? b.Entrants[ea].NationIndex : -1, alpha);
            sx += flag + 4f;
            _simCodeSt.alignment = TextAnchor.MiddleLeft;
            _simCodeSt.normal.textColor = aWon ? winCol : loseCol;
            UITheme.Label(new Rect(sx, y, codeW, rowH), hasA ? b.Entrants[ea].Code : "-", _simCodeSt);
            sx += codeW + 6f;
            CupUiKit.Pips(sx, pipCy, r, CupSide.A, pip, pipGap, alpha);
            sx += pipsW + 6f;
            var sc = UITheme.Gold; sc.a = alpha;
            _simScoreSt.normal.textColor = sc;
            UITheme.Label(new Rect(sx, y, scoreW, rowH), r.ScoreLine, _simScoreSt);
            sx += scoreW + 6f;
            CupUiKit.Pips(sx, pipCy, r, CupSide.B, pip, pipGap, alpha);
            sx += pipsW + 6f;
            _simCodeSt.alignment = TextAnchor.MiddleRight;
            _simCodeSt.normal.textColor = aWon ? loseCol : winCol;
            UITheme.Label(new Rect(sx, y, codeW, rowH), hasB ? b.Entrants[eb].Code : "-", _simCodeSt);
            sx += codeW + 4f;
            CupUiKit.Flag(new Rect(sx, cy - flag * 0.5f, flag, flag), hasB ? b.Entrants[eb].NationIndex : -1, alpha);
        }

        /// <summary>"Waiting for 2 rounds to finish" / "Head to head next: Alice vs Bob" / "Waiting for Bob, Cara".</summary>
        string GateLine(CupDirector d, CupBracket b, CupStage stage, CupPlayer me, bool solo)
        {
            _gateSt.normal.textColor = UITheme.Faint;
            if (d == null) return "";
            if (solo)
            {
                if (me != null && me.Out) return "";
                if (b != null && b.IsChampion(d.LocalEntrant)) return "";
                return CupStages.IsLast(stage) ? "" : "Continue to the " + CupStages.Name(CupStages.Next(stage));
            }
            int playing = 0;
            var waiting = new List<CupPlayer>();
            var players = d.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!p.Active) continue;
                if (p.Playing) playing++;
                else if (!p.Ready && !p.Out) waiting.Add(p);
            }
            if (playing > 0) return CupText.WaitingForRounds(playing);
            if (b != null)
            {
                var pending = b.PendingRounds(stage);
                for (int i = 0; i < pending.Count; i++)
                {
                    var r = pending[i];
                    var ea = b.Entrants[r.EntrantA];
                    var eb = b.Entrants[r.EntrantB];
                    if (ea.IsHuman && eb.IsHuman)
                    {
                        _gateSt.normal.textColor = UITheme.Gold;
                        return CupText.HeadToHeadNext(ea.HumanName, eb.HumanName);
                    }
                }
            }
            if (waiting.Count > 0) return CupText.WaitingForPlayers(CupUiKit.Names(waiting));
            return "Everyone is ready";
        }

        void DrawFooter(float w, float by, CupDirector d, CupPlayer me, bool solo, ref Action fire)
        {
            bool outerEnabled = GUI.enabled;
            const float bw = 170f, bh = 48f, edge = 24f;

            // Quit to Menu (bad): a confirm card with the style's own wording. Opened through
            // `fire` so the card's controls first exist on a fresh pass, never mid-pass.
            if (UITheme.Button(new Rect(edge, by, bw, bh), CupText.QuitToMenu, _btnSt, bad: true))
            {
                string title = CupText.ConfirmQuitTitle, body = CupText.ConfirmQuitSoloBody;
                if (d != null && d.IsNetworked)
                {
                    if (Trickshot.Net.Multiplayer.IsHost) { title = CupText.ConfirmEndMatchTitle; body = CupText.ConfirmEndMatchBody; }
                    else body = d.Style == CupStyle.Coop ? CupText.ConfirmQuitCoopBody : CupText.ConfirmQuitHeadToHeadBody;
                }
                fire = () => OpenConfirm(title.ToUpperInvariant(), body, () => d?.QuitToMenu());
            }

            // View Bracket: the overlay (Esc / click closes).
            if (UITheme.Button(new Rect(w * 0.5f - bw - 8f, by, bw, bh), CupText.ViewBracket, _btnSt))
                fire = () => _bracketOpen = true;

            // Customize: appearance only, through whoever owns the customize path. Disabled with
            // no handler, and while the local round is on (the body is in use).
            bool localPlaying = me != null && me.Playing;
            GUI.enabled = outerEnabled && OnCustomizeRequested != null && !localPlaying;
            if (UITheme.Button(new Rect(w * 0.5f + 8f, by, bw, bh), CupText.Customize, _btnSt))
            {
                var cb = OnCustomizeRequested;
                fire = () => cb?.Invoke();
            }
            GUI.enabled = outerEnabled;

            // Ready (Solo: Continue). Disabled while the local round is on ("your round is still
            // on") and once eliminated (the director keeps the eliminated ready).
            bool ready = me != null && me.Ready;
            bool eliminated = me != null && me.Out;
            bool canToggle = d != null && me != null && !localPlaying && !eliminated;
            var rr = new Rect(w - edge - bw, by, bw, bh);
            GUI.enabled = outerEnabled && canToggle;
            string label = solo ? CupText.Continue : (ready ? "READY" : CupText.Ready);
            bool hit;
            if (ready && !solo) hit = UITheme.Toggle(rr, label, true, _btnSt, UITheme.GoodTint);
            else
            {
                var keep = GUI.backgroundColor;
                GUI.backgroundColor = UITheme.GoodTint;
                hit = UITheme.Button(rr, label, _btnSt);
                GUI.backgroundColor = keep;
            }
            if (hit)
            {
                bool next = solo || !ready;
                fire = () => d.SetReady(next);
            }
            GUI.enabled = outerEnabled;
            if (localPlaying) UITheme.Hint(new Rect(rr.x - 60f, rr.y - 22f, rr.width + 60f, 18f), CupText.YourRoundStillOn, TextAnchor.MiddleRight);
            else if (eliminated && !solo) UITheme.Hint(new Rect(rr.x - 60f, rr.y - 22f, rr.width + 60f, 18f), "auto-ready (out)", TextAnchor.MiddleRight);
        }

        /// <summary>The View Bracket overlay. Returns true when clicked (anywhere) to close.</summary>
        bool DrawBracketOverlay(float w, float h, CupDirector d, CupBracket b, CupStage stage)
        {
            bool prev = GUI.enabled;
            GUI.enabled = !PauseMenu.Paused;
            // A fully SOLID backdrop: the tree over a half-seen pitch and lobby read badly, so the
            // overlay owns the whole screen while it is up (the same dark plate the goal editor's
            // picture box uses).
            UITheme.Fill(new Rect(0f, 0f, w, h), BracketBackdrop);
            UITheme.Title(new Rect(0f, 14f, w, 56f), CupBracketView.Header(stage, false), 36, showRule: false);
            int mine = d != null ? d.LocalEntrant : -1;
            bool coop = d != null && d.Style == CupStyle.Coop;
            CupBracketView.DrawFull(new Rect(24f, 88f, w - 48f, h - 150f), b, CupStage.RoundOf32, mine, coop ? mine : -1, coop, 1f,
                                    d != null ? d.Players : null);
            UITheme.Hint(new Rect(0f, h - 40f, w, 24f), "Esc or click to close");
            // One full-screen invisible control: it is the click-to-close AND the blocker for
            // anything drawn later. The panel below was drawn disabled, so nothing under it reacts.
            bool hit = GUI.Button(new Rect(0f, 0f, w, h), GUIContent.none, GUIStyle.none);
            GUI.enabled = prev;
            return hit;
        }

        void DrawConfirm(float w, float h, bool esc, ref Action fire)
        {
            bool prev = GUI.enabled;
            GUI.enabled = !PauseMenu.Paused;
            const float cw = 440f, ch = 200f;
            var r = new Rect(w * 0.5f - cw * 0.5f, h * 0.5f - ch * 0.5f, cw, ch);
            UITheme.Glow(new Rect(r.x - 120f, r.y - 90f, r.width + 240f, r.height + 180f), new Color(0f, 0f, 0f, 0.5f));
            UITheme.Panel(r, UITheme.Red);
            UITheme.Shadowed(new Rect(r.x, r.y + 22f, r.width, 38f), _confirmTitle, _confirmTitleSt, UITheme.Ink, 0.7f, 2f);
            UITheme.Hint(new Rect(r.x + 20f, r.y + 66f, r.width - 40f, 40f), _confirmBody);

            const float bw = 176f, bh = 48f;
            float by = r.yMax - bh - 24f;
            var cancel = new Rect(r.center.x - bw - 8f, by, bw, bh);
            var ok = new Rect(r.center.x + 8f, by, bw, bh);
            var e = Event.current;
            if (e != null)
            {
                if (cancel.Contains(e.mousePosition)) _confirmYes = false;
                if (ok.Contains(e.mousePosition)) _confirmYes = true;
            }
            var keep = GUI.backgroundColor;
            if (!_confirmYes) GUI.backgroundColor = UITheme.SelTint;
            bool no = UITheme.Button(cancel, "Cancel", _confirmBtnSt);
            GUI.backgroundColor = _confirmYes ? UITheme.BadTint : keep;
            bool yes = UITheme.Button(ok, "Confirm", _confirmBtnSt);
            GUI.backgroundColor = keep;
            GUI.enabled = prev;

            if (no || esc) fire = ClearConfirm;
            else if (yes)
            {
                var act = _confirmAct;
                fire = () => { ClearConfirm(); act?.Invoke(); };
            }
        }

        void OpenConfirm(string title, string body, Action act)
        {
            _confirmTitle = title;
            _confirmBody = body;
            _confirmYes = false;
            _confirmAct = act;
        }

        void ClearConfirm()
        {
            _confirmAct = null;
            _confirmTitle = null;
            _confirmBody = null;
            _confirmYes = false;
        }

        static void EnsureStyles()
        {
            if (_titleSt != null) return;
            _titleSt = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            UIFont.Heavy(_titleSt);
            _titleSt.fontSize = 28;
            _nameSt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _statusSt = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _btnSt = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            _rowBtnSt = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            _simCodeSt = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _simScoreSt = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _gateSt = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _confirmTitleSt = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _confirmBtnSt = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
        }
    }
}
