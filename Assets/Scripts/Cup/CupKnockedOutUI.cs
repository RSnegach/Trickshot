using System;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The Solo KNOCKED OUT card (design 6.7): a 520x300 red panel with "KNOCKED OUT IN THE
    /// ROUND OF 16", the losing line ("2-3 to BRAZIL"), the career best stage (flagged when this
    /// cup beat it), and three buttons: Simulate to end, New Cup, Main Menu.
    ///
    /// Simulate to end is director.SimulateRest(): one press fills one stage. After the first
    /// press the card gives way to the full bracket with the same three buttons in a bar along the
    /// bottom, so each press is watched landing in the tree; the last press crowns the AI
    /// champion and the button disables. New Cup is PlayAgain() (a new seed, back to CHOOSE YOUR
    /// NATION); Main Menu is QuitToMenu() (Solo: the cup ends, no confirm - it is already over).
    ///
    /// The card and the bracket bar are two different control sets, but the switch happens on a
    /// click (an event pass), never between IMGUI's Layout and Repaint, which keeps ids stable.
    /// </summary>
    public class CupKnockedOutUI : MonoBehaviour
    {
        const float CardW = 520f, CardH = 300f;
        const float ButtonH = 44f;

        public static CupKnockedOutUI Create(Transform root, CupDirector director)
        {
            var go = new GameObject("CupKnockedOutUI");
            if (root != null) go.transform.SetParent(root, false);
            var ui = go.AddComponent<CupKnockedOutUI>();
            ui.Init(director);
            return ui;
        }

        /// <summary>
        /// The player's career best stage BEFORE this cup, if the flow latched it at cup start.
        /// With it the "new career best" line is exact; without it the card falls back to
        /// CupCareer.BeatsBest against the save file, which only reads true if the flow has not
        /// yet recorded this cup's stage.
        /// </summary>
        public static CupStage? BestStageBefore { get; set; }

        /// <summary>The bracket is being shown (after the first Simulate press).</summary>
        public bool BracketShown => _bracketShown;

        CupDirector _director;
        Action _draw;
        bool _hooked, _closed, _wasPaused, _bracketShown;

        // Latched at Create so the card never changes its mind as SimulateRest fills the bracket.
        CupStage _stage;
        string _losingLine, _bestLine;
        bool _newBest;

        static GUIStyle _titleSt, _lineSt, _btnSt, _bestSt;

        void Init(CupDirector director)
        {
            _director = director;
            if (_director != null) CupStatsLedger.Attach(_director);
            Latch();
            GameInput.CaptureCursor(false);
            _draw = Draw;
            if (_director != null) { _director.AddGuiHook(_draw); _hooked = true; }
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            if (_hooked && _director != null) _director.RemoveGuiHook(_draw);
            _hooked = false;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_hooked && _director != null) _director.RemoveGuiHook(_draw);
            _hooked = false;
        }

        void Update()
        {
            bool paused = PauseMenu.Paused;
            if (_wasPaused && !paused) GameInput.CaptureCursor(false);
            _wasPaused = paused;
        }

        /// <summary>Work out the stage, the losing line and the career-best line once.</summary>
        void Latch()
        {
            var d = _director;
            var b = d != null ? d.Bracket : null;
            _stage = d != null ? d.Stage : CupStage.RoundOf32;
            _losingLine = "";
            _newBest = false;
            _bestLine = "";
            if (b != null && d.LocalEntrant >= 0)
            {
                var at = b.EliminatedAt(d.LocalEntrant);
                if (at.HasValue) _stage = at.Value;
                var r = b.RoundOfEntrant(_stage, d.LocalEntrant);
                if (r != null && r.Done)
                {
                    var side = r.SideOf(d.LocalEntrant) ?? CupSide.A;
                    int own = r.ScoreOf(side), theirs = r.ScoreOf(CupSides.Other(side));
                    int opp = r.OpponentOf(d.LocalEntrant);
                    string oppName = b.IsValidEntrant(opp) ? b.Entrants[opp].Name.ToUpperInvariant() : "?";
                    _losingLine = CupText.ScoreLine(own, theirs, r.SuddenDeath) + "  to  " + oppName;
                }
            }

            var data = CareerStats.Data;
            var sp = data != null ? data.SP : null;
            if (BestStageBefore.HasValue) _newBest = (int)_stage > (int)BestStageBefore.Value;
            else _newBest = CupCareer.BeatsBest(sp, _stage);
            string best = CupCareer.BestStageLabel(sp);
            if (_newBest) _bestLine = "NEW CAREER BEST   -   " + CupStages.Name(_stage).ToUpperInvariant();
            else if (best != "-") _bestLine = "Career best:  " + best;
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
            GUI.enabled = !paused;

            var d = _director;
            var b = d != null ? d.Bracket : null;
            bool complete = b != null && b.IsComplete;

            if (!_bracketShown)
            {
                UITheme.Scrim(w, h, 0.5f, CardW + 300f);
                var r = new Rect(w * 0.5f - CardW * 0.5f, h * 0.5f - CardH * 0.5f, CardW, CardH);
                UITheme.Glow(new Rect(r.x - 120f, r.y - 90f, r.width + 240f, r.height + 180f), new Color(0f, 0f, 0f, 0.45f));
                UITheme.Panel(r, UITheme.Red);

                UITheme.Shadowed(new Rect(r.x + 16f, r.y + 26f, r.width - 32f, 44f), CupText.KnockedOutIn(_stage), _titleSt, UITheme.Red, 0.75f, 2.5f);
                if (!string.IsNullOrEmpty(_losingLine))
                    UITheme.Shadowed(new Rect(r.x, r.y + 84f, r.width, 30f), _losingLine, _lineSt, UITheme.Ink, 0.7f, 2f);
                if (!string.IsNullOrEmpty(_bestLine))
                {
                    _bestSt.normal.textColor = _newBest ? UITheme.Gold : UITheme.Dim;
                    UITheme.Label(new Rect(r.x, r.y + 122f, r.width, 24f), _bestLine, _bestSt);
                }
                UITheme.Divider(r.x + 40f, r.y + 160f, r.width - 80f);

                // Three buttons in a row inside the card.
                const float bw = 150f, gap = 12f;
                float bx = r.center.x - (bw * 3f + gap * 2f) * 0.5f;
                float by = r.yMax - ButtonH - 26f;
                DrawButtons(bx, by, bw, gap, d, complete, ref fire);
            }
            else
            {
                UITheme.Scrim(w, h, 0.6f, 1100f);
                string header = complete ? CupText.Champions : CupBracketView.Header(b != null ? b.CurrentStage : _stage, false);
                UITheme.Title(new Rect(0f, 14f, w, 56f), header, 36, complete ? UITheme.Gold : UITheme.Red, showRule: false);
                UITheme.Hint(new Rect(0f, 70f, w, 20f), CupText.KnockedOutIn(_stage) + (string.IsNullOrEmpty(_losingLine) ? "" : "   -   " + _losingLine));
                int mine = d != null ? d.LocalEntrant : -1;
                CupBracketView.DrawFull(new Rect(24f, 96f, w - 48f, h - 190f), b, CupStage.RoundOf32, mine, -1, false, 1f,
                                        d != null ? d.Players : null);

                const float bw = 190f, gap = 16f;
                float bx = w * 0.5f - (bw * 3f + gap * 2f) * 0.5f;
                float by = h - 72f;
                DrawButtons(bx, by, bw, gap, d, complete, ref fire);
            }

            GUI.enabled = true;
            MenuScale.End();
            fire?.Invoke();   // may destroy this object (New Cup / Main Menu); nothing after it touches `this`
        }

        void DrawButtons(float bx, float by, float bw, float gap, CupDirector d, bool complete, ref Action fire)
        {
            bool outerEnabled = GUI.enabled;

            // Simulate to end: one stage per press; disabled once the champion is crowned. The
            // control stays allocated either way (disabled, never skipped).
            GUI.enabled = outerEnabled && d != null && !complete;
            string simLabel = complete ? "Cup complete" : CupText.SimulateToEnd;
            var keep = GUI.backgroundColor;
            GUI.backgroundColor = UITheme.WarnTint;
            if (UITheme.Button(new Rect(bx, by, bw, ButtonH), simLabel, _btnSt))
            {
                fire = () =>
                {
                    d.SimulateRest();
                    _bracketShown = true;   // the tree is the thing to look at from here
                };
            }
            GUI.backgroundColor = keep;
            GUI.enabled = outerEnabled;

            if (UITheme.Button(new Rect(bx + bw + gap, by, bw, ButtonH), CupText.NewCup, _btnSt))
                fire = () => d?.PlayAgain();

            if (UITheme.Button(new Rect(bx + (bw + gap) * 2f, by, bw, ButtonH), CupText.MainMenu, _btnSt, bad: true))
                fire = () => d?.QuitToMenu();
        }

        static void EnsureStyles()
        {
            if (_titleSt != null) return;
            _titleSt = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            UIFont.Heavy(_titleSt);
            _titleSt.fontSize = 30;
            _lineSt = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            UIFont.Heavy(_lineSt);
            _lineSt.fontSize = 22;
            _bestSt = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _btnSt = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
        }
    }
}
