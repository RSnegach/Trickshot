using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The handful of drawing helpers every cup screen shares (flag badge, lit band, gold spine,
    /// kick pips, the EaseOutBack pop). They live beside the bracket renderer because that is the
    /// one file every cup screen already depends on; a screen never has to re-spell the lit-band
    /// colour LobbyUI uses or re-derive how a nation is drawn "as a flag" (there are no flag
    /// textures - the jersey thumb IS the badge, design 2.4).
    /// </summary>
    internal static class CupUiKit
    {
        /// <summary>The "your row" band, the same blue LobbyUI lights the local roster row with.</summary>
        public static readonly Color LitBand = new Color(0.14f, 0.28f, 0.48f, 0.55f);
        /// <summary>A faint band for hover / the keyboard cursor.</summary>
        public static readonly Color HoverBand = new Color(1f, 1f, 1f, 0.05f);
        /// <summary>An empty kick pip (design 6.5: empty at 0.14 alpha).</summary>
        public static readonly Color EmptyPip = new Color(1f, 1f, 1f, 0.14f);
        /// <summary>The recessed field behind a text box / strip cell.</summary>
        public static readonly Color Well = new Color(0f, 0f, 0f, 0.38f);

        /// <summary>Back-out overshoot (the same curve Hud.Flash pops with; Hud's copy is private).</summary>
        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.9f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        /// <summary>
        /// A nation's badge: the jersey thumb (48x48, point-filtered) stretched into the rect with
        /// a hairline dark frame so a white-heavy flag (Japan, Finland) still reads as a tile on a
        /// pale plate. An unresolved nation draws an empty tile rather than nothing, so a row never
        /// collapses when a table entry drifts from its design.
        /// </summary>
        public static void Flag(Rect r, int nation, float alpha = 1f)
        {
            var tex = CupNations.IsValid(nation) ? CupNations.Thumb(nation) : null;
            if (tex != null)
            {
                var keep = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
                GUI.color = keep;
            }
            else
            {
                UITheme.Fill(r, new Color(1f, 1f, 1f, 0.08f * alpha));
            }
            var edge = new Color(0f, 0f, 0f, 0.45f * alpha);
            UITheme.Fill(new Rect(r.x, r.y, r.width, 1f), edge);
            UITheme.Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), edge);
            UITheme.Fill(new Rect(r.x, r.y, 1f, r.height), edge);
            UITheme.Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), edge);
        }

        /// <summary>The 3 px accent spine along a row's leading edge (gold = selected / you).</summary>
        public static void Spine(Rect row, Color c) => UITheme.Fill(new Rect(row.x, row.y, 3f, row.height), c);

        /// <summary>Vertical step between wrapped pip rows for pips of diameter d and gap.</summary>
        public static float PipRowStep(float d, float gap) => d + gap + 2f;

        /// <summary>
        /// How many pip rows a round needs on its longer side: one row per KicksEach kicks, so a
        /// sudden-death shootout wraps onto extra rows UNDER the first instead of running into the
        /// neighbouring column. Never less than 1.
        /// </summary>
        public static int PipRows(CupRound round)
        {
            if (round == null) return 1;
            int a = 0, b = 0;
            for (int i = 0; i < round.Kicks.Count; i++) { if (round.Kicks[i].Side == CupSide.A) a++; else b++; }
            int most = Mathf.Max(a, b, CupTuning.KicksEach);
            return Mathf.Max(1, Mathf.CeilToInt(most / (float)CupTuning.KicksEach));
        }

        /// <summary>
        /// One side's kick pips for a round: a flat disc per kick that side took, green for a goal
        /// and red for anything else, then empty pips up to the regulation count so a short line
        /// (an early finish) still shows how many were never needed. Sudden-death kicks append and
        /// WRAP: every KicksEach pips start a new row PipRowStep below (cy is the first row's
        /// centre), so the block never grows wider than a regulation line. Returns the width used
        /// (at most one regulation row).
        /// </summary>
        public static float Pips(float x, float cy, CupRound round, CupSide side, float d, float gap, float alpha)
        {
            int shown = 0;
            float px = x, py = cy;
            float rowW = 0f;
            if (round != null)
            {
                for (int i = 0; i < round.Kicks.Count; i++)
                {
                    var k = round.Kicks[i];
                    if (k.Side != side) continue;
                    if (shown > 0 && shown % CupTuning.KicksEach == 0)
                    {
                        rowW = Mathf.Max(rowW, px - x - gap);
                        px = x;
                        py += PipRowStep(d, gap);
                    }
                    var c = k.Scored ? UITheme.Green : UITheme.Red;
                    c.a = alpha;
                    UITheme.Disc(new Rect(px, py - d * 0.5f, d, d), c);
                    px += d + gap;
                    shown++;
                }
            }
            for (int i = shown; i < CupTuning.KicksEach; i++)
            {
                var c = EmptyPip; c.a *= alpha;
                UITheme.Disc(new Rect(px, py - d * 0.5f, d, d), c);
                px += d + gap;
            }
            return Mathf.Max(rowW, px - x - gap);
        }

        /// <summary>The names of a list of players, "Alice, Bob, Cara".</summary>
        public static string Names(IList<CupPlayer> players)
        {
            if (players == null || players.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < players.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(players[i].DisplayName);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Whether a cup screen currently owns the Escape key. PauseMenu polls Escape through the
    /// Input System in its Update, BEFORE IMGUI sees the KeyDown, so a screen that closes an
    /// overlay or steps Back on Esc has to say so up front or the same press also opens the pause
    /// menu - the exact contract QuickChatFeed.EscapeOwned / GameManager.CrossMapEscapeOwned /
    /// AccuracyGame.MapEscapeOwned already follow. One aggregate so PauseMenu checks one flag.
    /// </summary>
    public static class CupEscape
    {
        public static bool Owned => NationPickerUI.EscapeOwned || CupLobbyUI.EscapeOwned || CupResultsUI.EscapeOwned || CupSpectatorView.EscapeOwned;
    }

    /// <summary>
    /// The one bracket renderer (design 6.2): the 32-16-8-4-2 tree with the Final in the middle,
    /// drawn full-screen by <see cref="CupBracketScreen"/>, as the cup lobby's View Bracket
    /// overlay, behind the KNOCKED OUT card, and small as the in-round Tab peek (DrawMini). One
    /// renderer so the four views can never disagree about which cell is whose.
    ///
    /// Geometry: every cell is (stage, round, side). The FIRST stage shown lays its entrants out
    /// top to bottom in tree order - round i of that stage puts side A on row 2i and side B on
    /// row 2i+1 of its half (left half = the first half of the stage's rounds, right half = the
    /// rest); every later stage's cell sits at the vertical centre of the two feeder cells it is
    /// fed by (round i of stage s+1 is fed by rounds 2i and 2i+1 of stage s, which is exactly how
    /// CupBracket.Advance feeds). The Final's two cells are stacked in the centre column with the
    /// champion's cell above them. Showing a later stage first simply starts the layout there, so
    /// the tree "shrinks" to the nations still in (16 rows per half, then 8, 4, 2, 1).
    ///
    /// Rows are 20 px at the design height; DrawFull scales them up to 24 when the rect allows
    /// and DrawMini down to whatever fits, with the fonts following the row height.
    /// </summary>
    public static class CupBracketView
    {
        /// <summary>Design row height (design 6.2: 16 rows per half at 20 px).</summary>
        public const float RowH = 20f;
        const float ColGap = 16f;            // horizontal room between columns for the connectors
        const float FullRowMax = 24f;        // full-screen rows may grow to this
        const float MiniRowMax = 18f;
        const float FullCellMax = 156f;
        const float MiniCellMax = 104f;

        static GUIStyle _codeSt, _nameSt, _scoreSt, _tagSt, _emptySt, _champSt;

        /// <summary>"THE DRAW" on the first showing, then the stage header ("ROUND OF 16").</summary>
        public static string Header(CupStage stage, bool first) => first ? CupText.TheDraw : CupStages.Header(stage);

        /// <summary>
        /// The full tree from `upTo` to the Final. highlightEntrant gets the gold spine (+ YOU);
        /// in Co-op teamEntrant gets the gold outline and band with every player's name; revealT
        /// (0..1) fades the first stage's cells in tree order (pass 1 for no reveal); players
        /// supplies the names for the Co-op team cell.
        /// </summary>
        public static void DrawFull(Rect r, CupBracket b, CupStage upTo, int highlightEntrant, int teamEntrant,
                                    bool coop, float revealT, IEnumerable<CupPlayer> players)
        {
            Draw(r, b, upTo, highlightEntrant, teamEntrant, coop, revealT, players, null, false);
        }

        /// <summary>The small tree (Tab peek): codes and scores only, the live round pulsing.</summary>
        public static void DrawMini(Rect r, CupBracket b, CupStage stage, int highlightEntrant, CupRound live)
        {
            Draw(r, b, stage, highlightEntrant, -1, false, 1f, null, live, true);
        }

        // ---- the renderer --------------------------------------------------------------------

        struct Layout
        {
            public CupStage from;
            public int columns;
            public float rowH, cellW, cellH, k;   // k = font scale (rowH / RowH)
            public float x0, top;
        }

        static void Draw(Rect r, CupBracket b, CupStage upTo, int highlightEntrant, int teamEntrant, bool coop,
                         float revealT, IEnumerable<CupPlayer> players, CupRound live, bool mini)
        {
            EnsureStyles();
            if (b == null)
            {
                // Nothing drawn yet: say so rather than drawing an empty tree, which reads as a bug.
                UITheme.Hint(r, "No draw yet");
                return;
            }
            if (!CupStages.IsValid(upTo)) upTo = CupStage.RoundOf32;

            var L = new Layout();
            L.from = upTo;
            int stagesShown = CupStages.Count - (int)upTo;             // upTo..Final
            L.columns = 2 * (stagesShown - 1) + 1;                       // both halves + the final column
            int rows = upTo == CupStage.Final ? 2 : CupStages.EntrantsIn(upTo) / 2;
            // The champion cell sits 2.4 rows above the Final, so the layout keeps that much
            // headroom whatever the row count; with 16 rows it is inside the tree anyway.
            float needRows = Mathf.Max(rows, 5f);
            L.rowH = Mathf.Min(mini ? MiniRowMax : FullRowMax, r.height / needRows);
            L.k = L.rowH / RowH;
            L.cellH = L.rowH - Mathf.Max(2f, 3f * L.k);
            L.cellW = Mathf.Min(mini ? MiniCellMax : FullCellMax, (r.width - ColGap * (L.columns - 1)) / L.columns);
            float treeW = L.columns * L.cellW + (L.columns - 1) * ColGap;
            float treeH = rows * L.rowH;
            L.x0 = r.x + (r.width - treeW) * 0.5f;
            L.top = r.y + Mathf.Max((r.height - treeH) * 0.5f, 3f * L.rowH);
            if (L.top + treeH > r.yMax) L.top = Mathf.Max(r.y, r.yMax - treeH);

            revealT = Mathf.Clamp01(revealT);
            int firstCells = CupStages.EntrantsIn(upTo);   // cells of the first stage, for the reveal order
            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f));

            // Who pulses: the live round (mini) or, on the first showing, the highlighted entrant's
            // next opponent ("your first opponent pulsing", design 3.5).
            int pulseEntrant = -1;
            CupRound pulseRound = live;
            if (live == null && b.IsValidEntrant(highlightEntrant))
            {
                var next = b.NextRoundOf(highlightEntrant);
                if (next != null && next.Ready && !next.Done) pulseEntrant = next.OpponentOf(highlightEntrant);
            }

            // ---- connectors first, so cells draw over the line ends ----
            var hair = new Color(1f, 1f, 1f, 0.16f);
            for (int si = (int)upTo; si < (int)CupStage.Final; si++)
            {
                var s = (CupStage)si;
                var rounds = b.RoundsOf(s);
                int n = rounds.Count, half = n / 2;
                for (int i = 0; i < n; i++)
                {
                    bool left = i < half;
                    float cx = ColumnX(L, s, left);
                    float yA = CellY(L, s, i, CupSide.A), yB = CellY(L, s, i, CupSide.B);
                    float xv = left ? cx + L.cellW + ColGap * 0.5f : cx - ColGap * 0.5f;
                    // Where the pair's winner lands: the next stage's cell (the Final's cells are
                    // offset from the tree centre, so the vertical stretches to reach it).
                    var nextStage = CupStages.Next(s);
                    var nextSide = CupBracket.FeedSide(rounds[i]);
                    float yTo = nextStage == CupStage.Final ? FinalY(L, nextSide) : CellY(L, nextStage, i / 2, nextSide);
                    float y0 = Mathf.Min(yA, yTo), y1 = Mathf.Max(yB, yTo);
                    UITheme.Fill(new Rect(xv, y0, 1f, y1 - y0), hair);
                    // stubs from the two cells into the vertical
                    if (left)
                    {
                        UITheme.Fill(new Rect(cx + L.cellW, yA, ColGap * 0.5f, 1f), hair);
                        UITheme.Fill(new Rect(cx + L.cellW, yB, ColGap * 0.5f, 1f), hair);
                        UITheme.Fill(new Rect(xv, yTo, ColGap * 0.5f, 1f), hair);
                    }
                    else
                    {
                        UITheme.Fill(new Rect(cx - ColGap * 0.5f, yA, ColGap * 0.5f, 1f), hair);
                        UITheme.Fill(new Rect(cx - ColGap * 0.5f, yB, ColGap * 0.5f, 1f), hair);
                        UITheme.Fill(new Rect(xv - ColGap * 0.5f, yTo, ColGap * 0.5f, 1f), hair);
                    }
                }
            }

            // ---- cells ----
            int cellIndex = 0;
            for (int si = (int)upTo; si < (int)CupStage.Final; si++)
            {
                var s = (CupStage)si;
                var rounds = b.RoundsOf(s);
                int n = rounds.Count, half = n / 2;
                // Tree order for the reveal: the left half top to bottom, then the right half.
                for (int i = 0; i < n; i++)
                {
                    bool left = i < half;
                    float cx = ColumnX(L, s, left);
                    for (int sd = 0; sd < 2; sd++)
                    {
                        var side = CupSides.At(sd);
                        float alpha = 1f;
                        if (s == upTo)
                        {
                            // Each first-stage cell fades in over the last 15% of the reveal window,
                            // staggered by its tree position - the "names fade in in tree order".
                            float start = firstCells > 1 ? cellIndex / (float)firstCells * 0.85f : 0f;
                            alpha = Mathf.Clamp01((revealT - start) / 0.15f);
                            cellIndex++;
                        }
                        else alpha = revealT;
                        var cell = new Rect(cx, CellY(L, s, i, side) - L.cellH * 0.5f, L.cellW, L.cellH);
                        DrawCell(cell, L, b, rounds[i], side, alpha, mini, highlightEntrant, teamEntrant, coop,
                                 players, pulseRound, pulseEntrant, pulse);
                    }
                }
            }

            // ---- the Final and the champion, centre column ----
            var final = b.Round(CupStage.Final, 0);
            float fx = ColumnX(L, CupStage.Final, true);
            float fa = revealT;
            for (int sd = 0; sd < 2; sd++)
            {
                var side = CupSides.At(sd);
                var cell = new Rect(fx, FinalY(L, side) - L.cellH * 0.5f, L.cellW, L.cellH);
                DrawCell(cell, L, b, final, side, fa, mini, highlightEntrant, teamEntrant, coop, players,
                         pulseRound, pulseEntrant, pulse);
            }
            var champ = new Rect(fx, FinalY(L, CupSide.A) - L.rowH * 2.4f - L.cellH * 0.5f, L.cellW, L.cellH);
            DrawChampion(champ, L, b, fa, mini, highlightEntrant, teamEntrant, coop, players);
            // a short stem from the Final pair up to the champion
            UITheme.Fill(new Rect(fx + L.cellW * 0.5f, champ.yMax, 1f, FinalY(L, CupSide.A) - L.cellH * 0.5f - champ.yMax), hair);
        }

        static float ColumnX(Layout L, CupStage s, bool left)
        {
            float step = L.cellW + ColGap;
            if (s == CupStage.Final) return L.x0 + ((L.columns - 1) / 2) * step;
            int ci = (int)s - (int)L.from;
            return left ? L.x0 + ci * step : L.x0 + (L.columns - 1 - ci) * step;
        }

        /// <summary>Vertical centre of a cell: laid out at the first stage, the feeders' midpoint after.</summary>
        static float CellY(Layout L, CupStage s, int round, CupSide side)
        {
            if (s == L.from)
            {
                int n = CupStages.RoundsIn(s), half = n / 2;
                int local = (n > 1 && round >= half) ? round - half : round;
                int row = local * 2 + CupSides.Index(side);
                return L.top + row * L.rowH + L.rowH * 0.5f;
            }
            var prev = CupStages.Previous(s);
            int feeder = round * 2 + CupSides.Index(side);
            return 0.5f * (CellY(L, prev, feeder, CupSide.A) + CellY(L, prev, feeder, CupSide.B));
        }

        /// <summary>The Final's two cells are stacked about the tree's centre (they would otherwise share one y).</summary>
        static float FinalY(Layout L, CupSide side)
        {
            float mid;
            if (L.from == CupStage.Final) mid = L.top + L.rowH;   // rows 0 and 1
            else mid = 0.5f * (CellY(L, CupStage.Final, 0, CupSide.A) + CellY(L, CupStage.Final, 0, CupSide.B));
            return side == CupSide.A ? mid - L.rowH * 0.65f : mid + L.rowH * 0.65f;
        }

        static void DrawCell(Rect c, Layout L, CupBracket b, CupRound round, CupSide side, float alpha, bool mini,
                             int highlightEntrant, int teamEntrant, bool coop, IEnumerable<CupPlayer> players,
                             CupRound pulseRound, int pulseEntrant, float pulse)
        {
            if (alpha <= 0.01f) return;
            int e = round != null ? round.Entrant(side) : -1;
            bool has = b.IsValidEntrant(e);
            bool done = round != null && round.Done;
            bool winner = done && has && round.WinnerEntrant == e;
            bool loser = done && has && !winner;
            bool isYou = has && e == highlightEntrant;
            bool isTeam = coop && has && e == teamEntrant;
            bool pulsing = has && ((pulseRound != null && ReferenceEquals(pulseRound, round)) || e == pulseEntrant);

            // plate
            UITheme.Fill(c, new Color(1f, 1f, 1f, 0.05f * alpha));
            if (isTeam)
            {
                var band = UITheme.Gold; band.a = 0.18f * alpha;
                UITheme.Fill(c, band);
                var outline = UITheme.Gold; outline.a = alpha;
                UITheme.FrameOutline(new Rect(c.x - 1f, c.y - 1f, c.width + 2f, c.height + 2f), outline);
            }
            else if (isYou)
            {
                var band = CupUiKit.LitBand; band.a *= alpha;
                UITheme.Fill(c, band);
            }
            if (pulsing)
            {
                var g = UITheme.Gold; g.a = 0.22f * pulse * alpha;
                UITheme.Fill(c, g);
            }
            if (isYou || isTeam)
            {
                var sp = UITheme.Gold; sp.a = alpha;
                CupUiKit.Spine(c, sp);
            }

            float k = L.k;
            float flag = Mathf.Round(16f * k);
            float x = c.x + 5f;
            if (!has)
            {
                _emptySt.fontSize = Mathf.RoundToInt(12f * k);
                _emptySt.normal.textColor = new Color(UITheme.Faint.r, UITheme.Faint.g, UITheme.Faint.b, alpha);
                UITheme.Label(new Rect(x, c.y, c.width - 10f, c.height), "-", _emptySt);
                return;
            }

            var entrant = b.Entrants[e];
            CupUiKit.Flag(new Rect(x, c.y + (c.height - flag) * 0.5f, flag, flag), entrant.NationIndex, loser ? 0.55f * alpha : alpha);
            x += flag + 5f;

            var col = loser ? UITheme.Dim : UITheme.Ink;
            if (winner && done) col = UITheme.Ink;
            col.a = alpha;
            _codeSt.fontSize = Mathf.RoundToInt(12f * k);
            _codeSt.normal.textColor = col;
            string code = entrant.Code;
            float codeW = _codeSt.CalcSize(new GUIContent(code)).x + 4f;
            UITheme.Label(new Rect(x, c.y, codeW, c.height), code, _codeSt);
            x += codeW;

            // score on the right (only once played); the winner's number in gold, "SD" tag beside it
            float scoreRight = c.xMax - 5f;
            if (done)
            {
                _scoreSt.fontSize = Mathf.RoundToInt(12f * k);
                var sc = winner ? UITheme.Gold : UITheme.Dim; sc.a = alpha;
                _scoreSt.normal.textColor = sc;
                string score = round.ScoreOf(side).ToString();
                float sw = _scoreSt.CalcSize(new GUIContent(score)).x + 2f;
                if (winner && round.SuddenDeath && !mini && c.width >= 110f)
                {
                    _tagSt.fontSize = Mathf.Max(7, Mathf.RoundToInt(8f * k));
                    var tc = UITheme.Gold; tc.a = 0.8f * alpha;
                    _tagSt.normal.textColor = tc;
                    float tw = _tagSt.CalcSize(new GUIContent(CupText.SuddenDeathTag)).x + 2f;
                    UITheme.Label(new Rect(scoreRight - tw, c.y, tw, c.height), CupText.SuddenDeathTag, _tagSt);
                    scoreRight -= tw + 2f;
                }
                UITheme.Label(new Rect(scoreRight - sw, c.y, sw, c.height), score, _scoreSt);
                scoreRight -= sw + 4f;
            }

            // human name beside the flag (11 pt dim); "YOU" for the highlighted entrant; the Co-op
            // team lists every player's name when there is room, else YOUR TEAM.
            if (!mini && scoreRight - x > 26f)
            {
                string name = null;
                if (isTeam)
                {
                    string all = CupUiKit.Names(ActivePlayers(players));
                    name = string.IsNullOrEmpty(all) ? CupText.YourTeam : all;
                }
                else if (isYou && !coop) name = CupText.You;
                else if (entrant.WasHuman) name = entrant.ReplacedByAi ? CupText.AiName(entrant.HumanName) : entrant.HumanName;
                if (!string.IsNullOrEmpty(name))
                {
                    _nameSt.fontSize = Mathf.RoundToInt(11f * k);
                    var nc = (isYou || isTeam) ? UITheme.Gold : UITheme.Dim; nc.a = alpha;
                    _nameSt.normal.textColor = nc;
                    UITheme.Label(new Rect(x, c.y, scoreRight - x, c.height), name, _nameSt);
                }
            }
        }

        static void DrawChampion(Rect c, Layout L, CupBracket b, float alpha, bool mini, int highlightEntrant,
                                 int teamEntrant, bool coop, IEnumerable<CupPlayer> players)
        {
            if (alpha <= 0.01f) return;
            int e = b.Champion;
            bool has = b.IsValidEntrant(e);
            var gold = UITheme.Gold; gold.a = alpha * (has ? 1f : 0.45f);
            if (has)
            {
                var band = UITheme.Gold; band.a = 0.22f * alpha;
                UITheme.Fill(c, band);
                UITheme.Glow(new Rect(c.x - 12f, c.y - 10f, c.width + 24f, c.height + 20f), new Color(gold.r, gold.g, gold.b, 0.16f * alpha));
            }
            UITheme.FrameOutline(new Rect(c.x - 1f, c.y - 1f, c.width + 2f, c.height + 2f), gold);

            float k = L.k;
            _champSt.fontSize = Mathf.Max(7, Mathf.RoundToInt(9f * k));
            if (!has)
            {
                var tc = UITheme.Faint; tc.a = alpha;
                _champSt.normal.textColor = tc;
                UITheme.Label(c, CupText.Champions, _champSt);
                return;
            }
            var entrant = b.Entrants[e];
            float flag = Mathf.Round(16f * k);
            float x = c.x + 5f;
            CupUiKit.Flag(new Rect(x, c.y + (c.height - flag) * 0.5f, flag, flag), entrant.NationIndex, alpha);
            x += flag + 5f;
            _codeSt.fontSize = Mathf.RoundToInt(12f * k);
            var cc = UITheme.Gold; cc.a = alpha;
            _codeSt.normal.textColor = cc;
            string code = entrant.Code;
            float codeW = _codeSt.CalcSize(new GUIContent(code)).x + 4f;
            UITheme.Label(new Rect(x, c.y, codeW, c.height), code, _codeSt);
            x += codeW;
            if (!mini && c.xMax - 5f - x > 26f)
            {
                string name = coop && e == teamEntrant ? CupText.YourTeam
                            : e == highlightEntrant && !coop ? CupText.You
                            : entrant.WasHuman ? (entrant.ReplacedByAi ? CupText.AiName(entrant.HumanName) : entrant.HumanName)
                            : null;
                if (!string.IsNullOrEmpty(name))
                {
                    _nameSt.fontSize = Mathf.RoundToInt(11f * k);
                    _nameSt.normal.textColor = cc;
                    UITheme.Label(new Rect(x, c.y, c.xMax - 5f - x, c.height), name, _nameSt);
                }
            }
        }

        static readonly List<CupPlayer> _activeScratch = new List<CupPlayer>();
        static IList<CupPlayer> ActivePlayers(IEnumerable<CupPlayer> players)
        {
            _activeScratch.Clear();
            if (players == null) return _activeScratch;
            foreach (var p in players) if (p != null && p.Active) _activeScratch.Add(p);
            return _activeScratch;
        }

        static void EnsureStyles()
        {
            if (_codeSt != null) return;
            _codeSt = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _nameSt = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _scoreSt = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            _tagSt = new GUIStyle(GUI.skin.label) { fontSize = 8, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            _emptySt = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _champSt = new GUIStyle(GUI.skin.label) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }
    }
}
