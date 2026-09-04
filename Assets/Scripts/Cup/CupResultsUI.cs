using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>Which results screen (design 6.6) to draw.</summary>
    public enum CupResultsMode : byte
    {
        /// <summary>Co-op, lost: GAME OVER (red) with the stage tabs and the per-player table.</summary>
        GameOver = 0,
        /// <summary>Co-op, won the Final: CHAMPIONS (gold), the same tabs and table.</summary>
        Champions = 1,
        /// <summary>Solo / Head to Head after the podium: CUP SUMMARY, one row per human.</summary>
        Summary = 2,
    }

    /// <summary>
    /// Per-player, per-stage kick bookkeeping for the results table. The bracket only records a
    /// kick's SIDE and outcome, which is enough for Solo and Head to Head (one human per side takes
    /// every kick) but not for Co-op, where shooters cycle through the order and the keeper keeps.
    /// So this ledger listens to the live round driver: at every whistle it latches who is about to
    /// take and who keeps (TakerSlotForNextKick / KeeperSlotForNextKick), and on the verdict banks
    /// one row for the taker and one for the keeper. On a client the same events fire from
    /// ApplyState, so every peer's table agrees with the host's kicks.
    ///
    /// Attach() is idempotent and every cup screen calls it (the nation picker is the first screen
    /// of every cup, Play Again included), so a flow does not have to remember to. A missing stage
    /// (a late joiner) falls back to reconstructing from the bracket with the current order.
    /// </summary>
    public static class CupStatsLedger
    {
        /// <summary>One player's numbers for one stage.</summary>
        public sealed class Row
        {
            public int Slot;
            public CupStage Stage;
            public int Kicks, Goals, Missed, SavedAgainst, GkSaves, GkConceded;

            public void Add(Row o)
            {
                if (o == null) return;
                Kicks += o.Kicks; Goals += o.Goals; Missed += o.Missed; SavedAgainst += o.SavedAgainst;
                GkSaves += o.GkSaves; GkConceded += o.GkConceded;
            }

            public bool AnyTaking => Kicks > 0;
            public bool AnyKeeping => GkSaves + GkConceded > 0 || Kept > 0;
            /// <summary>Kicks kept against, counting the shooter's misses too.</summary>
            public int Kept;
        }

        static CupDirector _director;
        static CupRoundDriver _driver;
        static readonly List<Row> _rows = new List<Row>();
        static int _armedTaker = -1, _armedKeeper = -1, _armedKick = -1;
        static int _seenKicks;

        public static bool Attached => _director != null;

        /// <summary>Start listening to a director (idempotent; re-attaching to another director resets).</summary>
        public static void Attach(CupDirector d)
        {
            if (d == null || ReferenceEquals(d, _director)) return;
            Detach();
            _director = d;
            _director.StateChanged += OnState;
            _director.PhaseChanged += OnPhase;
            Bind(_director.Driver);
        }

        public static void Detach()
        {
            if (_director != null)
            {
                _director.StateChanged -= OnState;
                _director.PhaseChanged -= OnPhase;
            }
            Bind(null);
            _director = null;
        }

        /// <summary>Forget every row (Play Again).</summary>
        public static void Reset()
        {
            _rows.Clear();
            _seenKicks = 0;
            _armedTaker = _armedKeeper = _armedKick = -1;
        }

        /// <summary>The row for (slot, stage), created on demand when `create`.</summary>
        public static Row Get(int slot, CupStage stage, bool create = false)
        {
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].Slot == slot && _rows[i].Stage == stage) return _rows[i];
            if (!create) return null;
            var r = new Row { Slot = slot, Stage = stage };
            _rows.Add(r);
            return r;
        }

        /// <summary>Any kick recorded for a stage at all.</summary>
        public static bool HasStage(CupStage stage)
        {
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].Stage == stage && (_rows[i].Kicks > 0 || _rows[i].Kept > 0)) return true;
            return false;
        }

        /// <summary>The sum of a slot's rows over every stage.</summary>
        public static Row Total(int slot)
        {
            var t = new Row { Slot = slot, Stage = CupStage.Final };
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].Slot == slot) { t.Add(_rows[i]); t.Kept += _rows[i].Kept; }
            return t;
        }

        /// <summary>Bank one kick: the taker's line and the keeper's line (either may be -1 = an AI).</summary>
        public static void Record(int takerSlot, int keeperSlot, CupStage stage, KickOutcome outcome)
        {
            if (takerSlot >= 0)
            {
                var t = Get(takerSlot, stage, true);
                t.Kicks++;
                if (outcome == KickOutcome.Goal) t.Goals++;
                else if (outcome == KickOutcome.Saved) t.SavedAgainst++;
                else t.Missed++;
            }
            if (keeperSlot >= 0)
            {
                var k = Get(keeperSlot, stage, true);
                k.Kept++;
                if (outcome == KickOutcome.Goal) k.GkConceded++;
                else if (outcome == KickOutcome.Saved) k.GkSaves++;
            }
        }

        /// <summary>
        /// A stage with no live rows (a late joiner, or a flow that never showed a screen before
        /// the round): attribute the bracket round's kicks by the given Co-op order, the way the
        /// driver cycles them. An approximation only when the order has changed since.
        /// </summary>
        public static void Reconstruct(CupBracket b, CupRound round, CupSide teamSide, int[] order, CupStage stage)
        {
            if (b == null || round == null || order == null || order.Length == 0 || HasStage(stage)) return;
            int shooters = order.Length - 1;
            int keeper = order[0];
            int takenTeam = 0;
            for (int i = 0; i < round.Kicks.Count; i++)
            {
                var k = round.Kicks[i];
                if (k.Side == teamSide)
                {
                    int taker = shooters <= 0 ? keeper : order[1 + CupRoundRules.CoopShooterFor(takenTeam, shooters)];
                    takenTeam++;
                    Record(taker, -1, stage, k.Outcome);
                }
                else Record(-1, keeper, stage, k.Outcome);
            }
        }

        // ---- listening ----

        static void OnState()
        {
            if (_director == null) return;
            if (!ReferenceEquals(_director.Driver, _driver)) Bind(_director.Driver);
        }

        static void OnPhase(CupPhase phase)
        {
            // Play Again lands in NationPick with an empty bracket: a fresh ledger for a fresh cup.
            if (phase == CupPhase.NationPick && _director != null && _director.Bracket == null) Reset();
        }

        static void Bind(CupRoundDriver drv)
        {
            if (ReferenceEquals(drv, _driver)) return;
            if (_driver != null)
            {
                _driver.PhaseChanged -= OnDriverPhase;
                _driver.KickResolved -= OnKick;
            }
            _driver = drv;
            _seenKicks = _driver != null && _driver.Line != null ? _driver.Line.Count : 0;
            _armedTaker = _armedKeeper = _armedKick = -1;
            if (_driver != null)
            {
                _driver.PhaseChanged += OnDriverPhase;
                _driver.KickResolved += OnKick;
            }
        }

        static void OnDriverPhase(RoundPhase phase)
        {
            if (_driver == null || phase != RoundPhase.Armed) return;
            // The whistle: who is on the ball and who keeps, before the verdict moves the line on.
            _armedTaker = _driver.TakerSlotForNextKick;
            _armedKeeper = _driver.KeeperSlotForNextKick;
            _armedKick = _driver.KickIndex;
        }

        static void OnKick(KickOutcome outcome, CupSide side, int scorerSlot)
        {
            if (_driver == null || _driver.Line == null) return;
            var setup = _driver.Setup;
            var stage = _driver.Data != null ? _driver.Data.Stage : (setup != null ? setup.Stage : CupStage.RoundOf32);
            int index = _seenKicks;   // ResolveKick fires one at a time; ApplyState fires a burst in order
            _seenKicks++;

            int taker, keeper;
            if (_armedKick == index && (_armedTaker >= 0 || _armedKeeper >= 0))
            {
                taker = _armedTaker;
                keeper = _armedKeeper;
            }
            else
            {
                // No whistle latch for this kick (a burst from ApplyState): derive from the setup
                // and the side's kick count up to this one, which is how the driver cycles.
                int sideIndex = 0;
                var kicks = _driver.Line.Kicks;
                for (int i = 0; i < index && i < kicks.Count; i++) if (kicks[i].Side == side) sideIndex++;
                taker = TakerFor(setup, side, sideIndex);
                keeper = KeeperFor(setup, side);
            }
            if (outcome == KickOutcome.Goal && scorerSlot >= 0) taker = scorerSlot;   // the driver knows best
            _armedKick = -1;
            Record(taker, keeper, stage, outcome);
        }

        static int TakerFor(CupRoundSetup s, CupSide side, int sideKickIndex)
        {
            if (s == null) return -1;
            if (s.Style == CupStyle.Coop)
            {
                if (side != s.TeamSide) return -1;
                int shooters = s.CoopOrderSlots.Length - 1;
                if (shooters <= 0) return s.CoopOrderSlots.Length == 1 ? s.CoopOrderSlots[0] : -1;
                return s.CoopOrderSlots[1 + CupRoundRules.CoopShooterFor(sideKickIndex, shooters)];
            }
            return s.HumanSlotOf(side);
        }

        static int KeeperFor(CupRoundSetup s, CupSide kicking)
        {
            if (s == null) return -1;
            var keeping = CupSides.Other(kicking);
            if (s.Style == CupStyle.Coop) return keeping == s.TeamSide ? s.CoopKeeperSlot : -1;
            return s.HumanSlotOf(keeping);
        }
    }

    /// <summary>
    /// Results / GAME OVER / CHAMPIONS / CUP SUMMARY (design 6.6).
    ///
    ///   GameOver, Champions (Co-op)  title 54 pt (red / gold), tabs across the top - one per stage
    ///                                the team played plus TOTAL - and a table with one row per
    ///                                player: Kicks, Goals, Missed, Saved-against for the shooters,
    ///                                GK Saves, GK Conceded for the keeper; TOTAL sums every stage.
    ///   Summary (Solo / Head to Head)  one row per human: stage reached, rounds won, goals, saves,
    ///                                coin calls right; Solo adds the career best stage.
    ///
    /// Buttons through director intents only. Multiplayer: End Match (bad; host dissolves, client
    /// leaves) and Play Again for the host / Captain, "waiting for the captain / host" for a
    /// client. Solo: Main Menu / New Cup. The host's End Match confirms first (it ends the cup for
    /// everyone); a client's does not (it is the natural exit).
    /// </summary>
    public class CupResultsUI : MonoBehaviour
    {
        const float PanelW = 820f;
        const float TabW = 118f, TabH = 32f, TabGap = 8f;
        const float RowH = 28f, RowGap = 6f;   // CareerStatsUI.DrawRows metrics
        const float ButtonH = 48f, ButtonW = 190f;

        public static CupResultsUI Create(Transform root, CupDirector director, CupResultsMode mode)
        {
            var go = new GameObject("CupResultsUI");
            if (root != null) go.transform.SetParent(root, false);
            var ui = go.AddComponent<CupResultsUI>();
            ui.Init(director, mode);
            return ui;
        }

        public CupResultsMode Mode { get; private set; }
        /// <summary>The selected tab: a stage, or null for TOTAL (Co-op modes only).</summary>
        public CupStage? Tab => _tab < _tabs.Count ? _tabs[_tab] : (CupStage?)null;

        /// <summary>
        /// The End Match confirm is up and closes on Esc, so the pause menu must not also open on
        /// that press (PauseMenu checks CupEscape.Owned). Held one frame past the close.
        /// </summary>
        public static bool EscapeOwned => s_modalOpen > 0 || Time.frameCount <= s_escGraceFrame;
        static int s_modalOpen;
        static int s_escGraceFrame = -1;
        bool _ownsEsc;

        void SyncEscOwnership()
        {
            bool want = !_closed && _confirmAct != null;
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
        int _tab;
        readonly List<CupStage> _tabs = new List<CupStage>();   // stages played, in order; the last index (== Count) is TOTAL
        readonly List<CupPlayer> _players = new List<CupPlayer>();

        Action _confirmAct;
        string _confirmTitle, _confirmBody;
        bool _confirmYes;

        static GUIStyle _headSt, _lblSt, _valSt, _dimValSt, _btnSt, _stripSt, _confirmTitleSt, _confirmBtnSt;

        void Init(CupDirector director, CupResultsMode mode)
        {
            _director = director;
            Mode = mode;
            if (_director != null) CupStatsLedger.Attach(_director);
            RebuildModel();
            _tab = _tabs.Count;   // open on TOTAL, the whole story at a glance
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
            SyncEscOwnership();
            RebuildModel();   // once per frame, never in a GUI pass (the tab and row counts are controls)
        }

        void RebuildModel()
        {
            _tabs.Clear();
            _players.Clear();
            if (_director == null) return;
            var b = _director.Bracket;
            bool coop = _director.Style == CupStyle.Coop;
            var players = _director.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.Left && !p.ReplacedByAi && !coop) continue;
                if (p.Left && coop && CupStatsLedger.Total(p.Slot).Kicks + CupStatsLedger.Total(p.Slot).Kept == 0) continue;
                _players.Add(p);
            }
            if (coop && b != null && _director.LocalEntrant >= 0)
            {
                for (int s = 0; s < CupStages.Count; s++)
                {
                    var stage = (CupStage)s;
                    var r = b.RoundOfEntrant(stage, _director.LocalEntrant);
                    if (r == null || !r.Done || r.Simulated) continue;
                    _tabs.Add(stage);
                    // A stage the ledger never saw live (a late joiner): rebuild it from the
                    // bracket's kicks and the current order. Done here, per frame, not in a GUI pass.
                    if (!CupStatsLedger.HasStage(stage))
                        CupStatsLedger.Reconstruct(b, r, r.SideOf(_director.LocalEntrant) ?? CupSide.A, _director.CoopOrder, stage);
                }
            }
            if (_tab > _tabs.Count) _tab = _tabs.Count;

            if (Mode == CupResultsMode.Summary)
            {
                // Champion first, then the furthest stage, then rounds won, then goals.
                _players.Sort((x, y) =>
                {
                    int cx = b != null && b.IsChampion(x.Entrant) ? 1 : 0, cy = b != null && b.IsChampion(y.Entrant) ? 1 : 0;
                    if (cx != cy) return cy.CompareTo(cx);
                    var sx = Summarise(x); var sy = Summarise(y);
                    if (sx.stage != sy.stage) return sy.stage.CompareTo(sx.stage);
                    if (sx.won != sy.won) return sy.won.CompareTo(sx.won);
                    if (sx.goals != sy.goals) return sy.goals.CompareTo(sx.goals);
                    return x.Slot.CompareTo(y.Slot);
                });
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

            bool esc = false;
            if (!paused && _confirmAct != null && e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                esc = true;
                e.Use();
            }

            UITheme.Scrim(w, h, 0.5f, PanelW + 300f);
            GUI.enabled = !paused && _confirmAct == null;

            var d = _director;
            var b = d != null ? d.Bracket : null;
            bool coopMode = Mode != CupResultsMode.Summary;
            Color accent = Mode == CupResultsMode.GameOver ? UITheme.Red : UITheme.Gold;
            string title = Mode == CupResultsMode.GameOver ? CupText.GameOver
                         : Mode == CupResultsMode.Champions ? CupText.Champions : CupText.CupSummary;

            UITheme.Title(new Rect(0f, 14f, w, 72f), title, 54, accent);
            UITheme.Label(new Rect(0f, 84f, w, 36f), StripLine(d, b), _stripSt);

            // ---- panel ----
            float px = w * 0.5f - PanelW * 0.5f, py = 122f;
            float ph = h - py - 22f;
            UITheme.Panel(new Rect(px, py, PanelW, ph), accent);

            float y = py + 18f;
            if (coopMode)
            {
                // Stage tabs + TOTAL (Hud.Seg). The tab set is rebuilt in Update, so it is stable
                // within a frame; every tab is a real control.
                int count = _tabs.Count + 1;
                float total = count * TabW + (count - 1) * TabGap;
                float tx = w * 0.5f - total * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    string label = i < _tabs.Count ? CupStages.Short(_tabs[i]) : CupText.Total;
                    if (Hud.Seg(new Rect(tx + i * (TabW + TabGap), y, TabW, TabH), label, i == _tab)) _tab = i;
                }
                y += TabH + 14f;
                UITheme.Divider(px + 24f, y, PanelW - 48f);
                y += 12f;
                DrawCoopTable(px, y, PanelW, d, b);
            }
            else
            {
                UITheme.Divider(px + 24f, y + 4f, PanelW - 48f);
                y += 16f;
                DrawSummaryTable(px, y, PanelW, d, b);
            }

            // ---- buttons ----
            DrawButtons(px, py, PanelW, ph, d, ref fire);

            GUI.enabled = true;
            if (_confirmAct != null) DrawConfirm(w, h, esc, ref fire);

            MenuScale.End();
            fire?.Invoke();   // may destroy this object; nothing after it touches `this`
            if (!_closed) SyncEscOwnership();   // a confirm opened/closed by this pass owns Esc from now
        }

        /// <summary>The line under the title: the nation and the people ("BRAZIL - Alice, Bob"), or the champion.</summary>
        string StripLine(CupDirector d, CupBracket b)
        {
            _stripSt.normal.textColor = UITheme.Gold;
            if (d == null) return "";
            if (Mode == CupResultsMode.GameOver)
            {
                _stripSt.normal.textColor = UITheme.Dim;
                var me = d.LocalPlayer;
                var stage = b != null && me != null && me.Entrant >= 0 ? (b.EliminatedAt(me.Entrant) ?? d.Stage) : d.Stage;
                string nation = CupNations.IsValid(d.TeamNation) ? CupNations.Name(d.TeamNation).ToUpperInvariant() + "   -   " : "";
                return nation + CupText.KnockedOutIn(stage);
            }
            if (Mode == CupResultsMode.Champions)
            {
                string nation = CupNations.IsValid(d.TeamNation) ? CupNations.Name(d.TeamNation).ToUpperInvariant() : CupText.YourTeam;
                var team = new List<CupPlayer>();
                for (int i = 0; i < d.Players.Count; i++) if (d.Players[i].Active) team.Add(d.Players[i]);
                return nation + "   -   " + CupUiKit.Names(team);
            }
            // Summary: who won the cup.
            if (b != null && b.IsComplete)
            {
                var champ = b.Entrants[b.Champion];
                string who = champ.WasHuman ? (champ.ReplacedByAi ? CupText.AiName(champ.HumanName) : champ.HumanName) : null;
                return CupText.ChampionsStrip(champ.Name, who);
            }
            _stripSt.normal.textColor = UITheme.Dim;
            return "";   // no cup tag line anywhere (owner's call): the screen title says it all
        }

        // ---- Co-op table ----

        static readonly string[] CoopCols = { "Kicks", "Goals", "Missed", "Saved", "GK Saves", "GK Conceded" };

        void DrawCoopTable(float px, float y, float pw, CupDirector d, CupBracket b)
        {
            float lx = px + 24f, rw = pw - 48f;
            float nameW = 190f;
            float colW = (rw - nameW) / CoopCols.Length;
            UITheme.Label(new Rect(lx, y, nameW, 16f), "PLAYER", _headSt);
            for (int c = 0; c < CoopCols.Length; c++)
            {
                _headSt.alignment = TextAnchor.MiddleRight;
                UITheme.Label(new Rect(lx + nameW + c * colW, y, colW, 16f), CoopCols[c].ToUpperInvariant(), _headSt);
            }
            _headSt.alignment = TextAnchor.MiddleLeft;
            y += 24f;

            bool total = _tab >= _tabs.Count;
            CupStage stage = total ? CupStage.Final : _tabs[_tab];

            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                var row = total ? CupStatsLedger.Total(p.Slot) : CupStatsLedger.Get(p.Slot, stage);
                float ry = y + i * (RowH + RowGap);
                bool isMe = d != null && p.Slot == d.LocalSlot;
                if (isMe) UITheme.Fill(new Rect(lx - 6f, ry, rw + 12f, RowH), CupUiKit.LitBand);
                _lblSt.normal.textColor = p.Active ? UITheme.Ink : UITheme.Dim;
                UITheme.Label(new Rect(lx, ry, nameW, RowH), p.DisplayName + (isMe ? "  (you)" : ""), _lblSt);

                bool taking = row != null && row.AnyTaking;
                bool keeping = row != null && row.AnyKeeping;
                string[] vals =
                {
                    taking ? row.Kicks.ToString() : "-",
                    taking ? row.Goals.ToString() : "-",
                    taking ? row.Missed.ToString() : "-",
                    taking ? row.SavedAgainst.ToString() : "-",
                    keeping ? row.GkSaves.ToString() : "-",
                    keeping ? row.GkConceded.ToString() : "-",
                };
                for (int c = 0; c < vals.Length; c++)
                {
                    var st = vals[c] == "-" ? _dimValSt : _valSt;
                    UITheme.Label(new Rect(lx + nameW + c * colW, ry, colW, RowH), vals[c], st);
                }
                UITheme.Divider(lx, ry + RowH + RowGap * 0.5f, rw);
            }
            if (_players.Count == 0) UITheme.Hint(new Rect(lx, y, rw, RowH), "No players");
        }

        // ---- Summary table ----

        struct Summary { public CupStage stage; public int won, goals, saves, kicks, conceded; public bool champion; }

        Summary Summarise(CupPlayer p)
        {
            var s = new Summary();
            var b = _director != null ? _director.Bracket : null;
            if (b == null || p == null || p.Entrant < 0) return s;
            s.stage = b.StageReached(p.Entrant);
            s.champion = b.IsChampion(p.Entrant);
            for (int st = 0; st < CupStages.Count; st++)
            {
                var r = b.RoundOfEntrant((CupStage)st, p.Entrant);
                if (r == null || !r.Done || r.Simulated) continue;   // only rounds a human played
                var side = r.SideOf(p.Entrant) ?? CupSide.A;
                var other = CupSides.Other(side);
                s.goals += r.GoalsOf(side);
                s.kicks += r.KicksTaken(side);
                for (int i = 0; i < r.Kicks.Count; i++)
                {
                    var k = r.Kicks[i];
                    if (k.Side != other) continue;
                    if (k.Outcome == KickOutcome.Saved) s.saves++;
                    else if (k.Outcome == KickOutcome.Goal) s.conceded++;
                }
                if (r.WinnerEntrant == p.Entrant) s.won++;
            }
            return s;
        }

        static readonly string[] SummaryCols = { "Stage", "Rounds won", "Goals", "Saves", "Coin calls" };
        static readonly float[] SummaryColW = { 150f, 120f, 90f, 90f, 120f };

        void DrawSummaryTable(float px, float y, float pw, CupDirector d, CupBracket b)
        {
            float lx = px + 24f, rw = pw - 48f;
            float colsW = 0f;
            for (int c = 0; c < SummaryColW.Length; c++) colsW += SummaryColW[c];
            float nameW = rw - colsW;
            UITheme.Label(new Rect(lx, y, nameW, 16f), "PLAYER", _headSt);
            float cx = lx + nameW;
            _headSt.alignment = TextAnchor.MiddleRight;
            for (int c = 0; c < SummaryCols.Length; c++)
            {
                UITheme.Label(new Rect(cx, y, SummaryColW[c], 16f), SummaryCols[c].ToUpperInvariant(), _headSt);
                cx += SummaryColW[c];
            }
            _headSt.alignment = TextAnchor.MiddleLeft;
            y += 24f;

            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                var s = Summarise(p);
                float ry = y + i * (RowH + RowGap);
                bool isMe = d != null && p.Slot == d.LocalSlot;
                if (isMe) UITheme.Fill(new Rect(lx - 6f, ry, rw + 12f, RowH), CupUiKit.LitBand);
                if (s.champion) CupUiKit.Spine(new Rect(lx - 6f, ry, 3f, RowH), UITheme.Gold);

                CupUiKit.Flag(new Rect(lx + 2f, ry + 3f, 22f, 22f), p.Nation, p.Active ? 1f : 0.5f);
                _lblSt.normal.textColor = p.Active ? UITheme.Ink : UITheme.Dim;
                UITheme.Label(new Rect(lx + 32f, ry, nameW - 32f, RowH), p.DisplayName + (isMe ? "  (you)" : ""), _lblSt);

                string stageText = p.Entrant < 0 ? "-" : s.champion ? CupText.AchChampion : CupStages.Name(s.stage);
                string[] vals =
                {
                    stageText,
                    s.won.ToString(),
                    s.goals + "/" + s.kicks,
                    s.saves.ToString(),
                    p.CoinCallsRight + "/" + p.CoinCallsMade,
                };
                cx = lx + nameW;
                for (int c = 0; c < vals.Length; c++)
                {
                    var st = c == 0 && s.champion ? _valSt : (c == 0 ? _dimValSt : _valSt);
                    UITheme.Label(new Rect(cx, ry, SummaryColW[c], RowH), vals[c], st);
                    cx += SummaryColW[c];
                }
                UITheme.Divider(lx, ry + RowH + RowGap * 0.5f, rw);
            }
            if (_players.Count == 0) UITheme.Hint(new Rect(lx, y, rw, RowH), "No players");

            // Solo: the career best stage, from the SP bag (the cup's own style decides the bag).
            if (d != null && d.Style == CupStyle.Solo)
            {
                float by = y + _players.Count * (RowH + RowGap) + 12f;
                var data = CareerStats.Data;
                string best = data != null ? CupCareer.BestStageLabel(data.SP) : "-";
                UITheme.Hint(new Rect(lx, by, rw, 22f), "Career best stage:  " + best, TextAnchor.MiddleLeft);
            }
        }

        // ---- buttons ----

        void DrawButtons(float px, float py, float pw, float ph, CupDirector d, ref Action fire)
        {
            bool outerEnabled = GUI.enabled;
            float by = py + ph - ButtonH - 22f;
            float left = px + 24f, right = px + pw - 24f - ButtonW;
            bool net = d != null && d.IsNetworked;
            bool authority = d == null || d.IsAuthority;

            if (!net)
            {
                if (UITheme.Button(new Rect(left, by, ButtonW, ButtonH), CupText.MainMenu, _btnSt, bad: true))
                    fire = () => d?.QuitToMenu();
                var keep = GUI.backgroundColor;
                GUI.backgroundColor = UITheme.GoodTint;
                if (UITheme.Button(new Rect(right, by, ButtonW, ButtonH), CupText.NewCup, _btnSt))
                    fire = () => d?.PlayAgain();
                GUI.backgroundColor = keep;
                return;
            }

            // Multiplayer. End Match: the host confirms (it ends the cup for everyone), a client
            // just leaves. Play Again is the host's / Captain's; a client waits.
            if (UITheme.Button(new Rect(left, by, ButtonW, ButtonH), CupText.EndMatch, _btnSt, bad: true))
            {
                // The host's confirm opens through `fire`, so the card's controls first exist on a
                // fresh pass rather than appearing mid-pass.
                if (authority) fire = () => OpenConfirm(CupText.ConfirmEndMatchTitle.ToUpperInvariant(), CupText.ConfirmEndMatchBody, () => d.EndMatch());
                else fire = () => d.EndMatch();
            }
            var pr = new Rect(right, by, ButtonW, ButtonH);
            GUI.enabled = outerEnabled && authority;
            var keep2 = GUI.backgroundColor;
            GUI.backgroundColor = UITheme.GoodTint;
            if (UITheme.Button(pr, CupText.PlayAgain, _btnSt))
                fire = () => d.PlayAgain();
            GUI.backgroundColor = keep2;
            GUI.enabled = outerEnabled;
            if (!authority)
            {
                string wait = d.Style == CupStyle.Coop ? CupText.WaitingForCaptain : CupText.WaitingForHost;
                UITheme.Hint(new Rect(pr.x - 80f, pr.y - 22f, pr.width + 80f, 18f), wait, TextAnchor.MiddleRight);
            }
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
            if (_headSt != null) return;
            _headSt = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _headSt.normal.textColor = UITheme.Faint;
            _lblSt = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _lblSt.normal.textColor = UITheme.Ink;
            _valSt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            _valSt.normal.textColor = UITheme.Gold;
            _dimValSt = new GUIStyle(_valSt);
            _dimValSt.normal.textColor = UITheme.Dim;
            _btnSt = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            _stripSt = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            UIFont.Heavy(_stripSt);   // the "you won" line reads as a headline, not a caption (owner's call)
            _confirmTitleSt = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _confirmBtnSt = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
        }
    }
}
