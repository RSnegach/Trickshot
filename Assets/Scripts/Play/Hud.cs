using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Shared IMGUI HUD toolkit for every mode, so the scoreboard, clock, callouts, and control
    /// legend look the same everywhere and only have to be made good once.
    ///
    /// Draw order per mode's OnGUI:
    ///   Hud.Begin();                                   // styles + UI scale (pair with Hud.End)
    ///   var p = Hud.PanelStart(title, rows);           // top-left titled panel
    ///     Hud.Stat(ref p, "Goals", goals.ToString());  // one stat row per line
    ///   Hud.Clock(seconds);                            // big top-centre clock pill
    ///   Hud.Scoreboard(...);                           // or the full broadcast bug for team modes
    ///   Hud.Flash(text, alpha);                        // centre callout (animated, fades)
    ///   Hud.Banner("TIME!", "Goals: 7", "Press R");    // centre end-of-round card
    ///   Hud.Legend(line);                              // bottom control band
    ///   Hud.End();                                     // on EVERY exit path, early returns too
    ///
    /// Begin() enters a MenuScale block, so the HUD fits the player's window instead of being
    /// fixed pixels: use Hud.W / Hud.H (not Screen.width/height) inside it.
    ///
    /// Visual language lives in UITheme: rounded plates, hairline borders, soft drop shadows,
    /// gold accents. Purely presentational - the modes own all scoring and logic.
    /// </summary>
    public static class Hud
    {
        public static readonly Color Ink    = UITheme.Ink;
        public static readonly Color Dim    = UITheme.Dim;
        public static readonly Color Gold   = UITheme.Gold;
        public static readonly Color Panel  = new Color(0.07f, 0.08f, 0.11f, 0.82f);
        public static readonly Color Accent = UITheme.Blue;

        static GUIStyle _title, _statKey, _statVal, _clock, _flash, _flashSub, _bannerBig, _bannerSub,
                        _legend, _legendKey, _score, _teamName, _tag,
                        _meterLbl, _overlayHdr, _overlayTip, _rowName, _rowVal, _scoreDash, _clockSmall;
        static bool _ready;

        /// <summary>Virtual screen size inside a Begin/End block (use instead of Screen.*).</summary>
        public static float W => MenuScale.Width;
        public static float H => MenuScale.Height;

        public static void Begin()
        {
            InitStyles();
            MenuScale.Begin();   // must run EVERY frame, unlike the one-time style setup
        }

        /// <summary>Leave the HUD scale block. Required on every exit path of a mode's OnGUI.</summary>
        public static void End() => MenuScale.End();

        static void InitStyles()
        {
            if (_ready) return;
            _ready = true;

            _title     = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Gold } };
            _statKey   = new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = Dim } };
            _statVal   = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Ink } };
            _clock     = new GUIStyle { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
            _flash     = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
            _flashSub  = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Dim } };
            _bannerBig = new GUIStyle { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
            _bannerSub = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } };
            _legend    = new GUIStyle { fontSize = LegendFont, alignment = TextAnchor.MiddleLeft, wordWrap = false, normal = { textColor = Dim } };
            _legendKey = new GUIStyle { fontSize = LegendFont, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, normal = { textColor = Gold } };
            _score     = new GUIStyle { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
            _teamName  = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
            _tag       = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Dim } };
            _meterLbl  = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } };
            _overlayHdr= new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter, normal = { textColor = Ink } };
            _overlayTip= new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = Dim } };
            _rowName   = new GUIStyle { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = Ink } };
            _rowVal    = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Ink } };
            _scoreDash = new GUIStyle(_score) { normal = { textColor = new Color(1f, 1f, 1f, 0.28f) } };
            _clockSmall= new GUIStyle(_clock) { fontSize = 20 };

            // The big ones go on the real bold cut. Synthetic bold is a smear of the glyph against
            // itself, which is tolerable at 12 pt and mud at 72, and these are the numbers the eye
            // goes to first. UIFont.Heavy also clears fontStyle, or Unity would bold a bold.
            UIFont.Heavy(_clock);
            UIFont.Heavy(_flash);
            UIFont.Heavy(_flashSub);
            UIFont.Heavy(_bannerBig);
            UIFont.Heavy(_bannerSub);
            UIFont.Heavy(_score);
            UIFont.Heavy(_teamName);
        }

        static void Fill(Rect r, Color c) => UITheme.Fill(r, c);

        // ================================================================ top-left stat panel
        public struct P { public float x, y, w, row; }

        public static P PanelStart(string title, int stats)
        {
            const float w = 232f, pad = 13f, head = 32f, rowH = 23f;
            float h = head + stats * rowH + pad * 0.6f;
            var p = new P { x = 14f, y = 14f, w = w, row = 0f };

            UITheme.Panel(new Rect(p.x, p.y, w, h), Accent);
            UITheme.Shadowed(new Rect(p.x + pad, p.y + 7f, w - pad * 2f, 20f), title.ToUpperInvariant(), _title, Gold, 0.6f, 1f);
            UITheme.Divider(p.x + pad, p.y + head - 5f, w - pad * 2f);

            p.row = p.y + head;
            return p;
        }

        public static void Stat(ref P p, string key, string val)
        {
            const float pad = 13f;
            var r = new Rect(p.x + pad, p.row, p.w - pad * 2f, 21f);
            UITheme.Label(r, key, _statKey);
            UITheme.Shadowed(r, val, _statVal, Ink, 0.55f, 1f);
            p.row += 23f;
        }

        // ================================================================ clock
        /// <summary>Big centred clock in a rounded pill. Goes red and gains a pulse under 15 s.</summary>
        public static void Clock(float seconds, bool urgent = false)
        {
            int t = Mathf.Max(0, Mathf.CeilToInt(seconds));
            bool hot = urgent || (t <= 15 && t > 0);

            const float w = 152f, h = 54f;
            var r = new Rect(W * 0.5f - w * 0.5f, 12f, w, h);
            UITheme.Panel(r, hot ? UITheme.Red : Accent);

            if (hot)
            {
                // Unscaled time so the pulse keeps beating if the mode slows the clock down.
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
                UITheme.Glow(new Rect(r.x - 14f, r.y - 10f, r.width + 28f, r.height + 20f),
                             new Color(1f, 0.28f, 0.24f, 0.10f + 0.16f * pulse));
            }

            UITheme.Shadowed(new Rect(r.x, r.y + 3f, r.width, r.height - 6f),
                             $"{t / 60:0}:{t % 60:00}", _clock, hot ? UITheme.Red : Ink, 0.7f, 2f);
        }

        // ================================================================ broadcast scoreboard
        /// <summary>
        /// Broadcast-style score bug for the team modes: coloured team blocks either side of a
        /// central score, with the clock hanging underneath. Replaces the plain centred text the
        /// match drivers used to draw themselves.
        ///
        /// seconds &lt; 0 hides the clock. sub is an optional caption chip under the bug
        /// (round number, "SHOOTOUT", etc.).
        /// </summary>
        public static void Scoreboard(string homeName, Color homeCol, int homeScore,
                                      int awayScore, string awayName, Color awayCol,
                                      float seconds = -1f, bool urgent = false, string sub = null)
        {
            const float w = 400f, h = 52f, teamW = 132f;
            float x = W * 0.5f - w * 0.5f, y = 12f;
            var r = new Rect(x, y, w, h);

            UITheme.Panel(r, Gold);

            // Team blocks: a saturated colour bar against the panel, name centred on it.
            var lBar = new Rect(x + 5f, y + 6f, teamW, h - 12f);
            var rBar = new Rect(r.xMax - teamW - 5f, y + 6f, teamW, h - 12f);
            Fill(lBar, new Color(homeCol.r, homeCol.g, homeCol.b, 0.30f));
            Fill(rBar, new Color(awayCol.r, awayCol.g, awayCol.b, 0.30f));
            Fill(new Rect(lBar.x, lBar.y, 3.5f, lBar.height), homeCol);
            Fill(new Rect(rBar.xMax - 3.5f, rBar.y, 3.5f, rBar.height), awayCol);
            UITheme.Shadowed(lBar, homeName, _teamName, Ink, 0.75f, 1.5f);
            UITheme.Shadowed(rBar, awayName, _teamName, Ink, 0.75f, 1.5f);

            // Central score. The dash is dimmed so the digits carry the eye.
            var mid = new Rect(lBar.xMax, y + 4f, rBar.x - lBar.xMax, h - 8f);
            float half = mid.width * 0.5f;
            UITheme.Shadowed(new Rect(mid.x, mid.y, half - 5f, mid.height), homeScore.ToString(), _score, Ink, 0.7f, 2f);
            UITheme.Shadowed(new Rect(mid.x + half + 5f, mid.y, half - 5f, mid.height), awayScore.ToString(), _score, Ink, 0.7f, 2f);
            UITheme.Label(new Rect(mid.x, mid.y, mid.width, mid.height), "-", _scoreDash);

            float below = r.yMax + 4f;

            // Clock strip under the bug.
            if (seconds >= 0f)
            {
                int t = Mathf.Max(0, Mathf.CeilToInt(seconds));
                bool hot = urgent || (t <= 15 && t > 0);
                const float cw = 108f, ch = 26f;
                var cr = new Rect(W * 0.5f - cw * 0.5f, below, cw, ch);
                UITheme.Chip(cr, new Color(0.03f, 0.04f, 0.06f, 0.92f));
                if (hot)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
                    UITheme.Glow(new Rect(cr.x - 10f, cr.y - 8f, cr.width + 20f, cr.height + 16f),
                                 new Color(1f, 0.28f, 0.24f, 0.10f + 0.14f * pulse));
                }
                var cs = _clockSmall;
                UITheme.Shadowed(cr, $"{t / 60:0}:{t % 60:00}", cs, hot ? UITheme.Red : Ink, 0.7f, 1.5f);
                below = cr.yMax + 4f;
            }

            if (!string.IsNullOrEmpty(sub))
            {
                var content = new GUIContent(sub.ToUpperInvariant());
                float tw = _tag.CalcSize(content).x + 22f;
                var sr = new Rect(W * 0.5f - tw * 0.5f, below, tw, 20f);
                UITheme.Chip(sr, new Color(0.03f, 0.04f, 0.06f, 0.82f));
                UITheme.Label(sr, content, _tag);
            }
        }

        // ================================================================ top callout
        // What KIND of thing a callout is, keyed off its text so the modes only have to agree on
        // the word. Three outcomes and a neutral:
        //   Good    - a goal or an ordinary save. Green.
        //   Epic    - an epic save. Yellow, with a star either side.
        //   Bad     - everything else that is an OUTCOME: misses, strikes, wide, over, post.
        //   Neutral - the callouts that are not outcomes at all (the picked cross delivery, the
        //             replay prompt). Red would tell the player they had failed at something.
        enum FlashKind { Neutral, Good, Epic, Bad }

        static readonly Color FlashGood    = new Color(0.29f, 0.82f, 0.48f);   // green
        static readonly Color FlashEpic    = new Color(1.00f, 0.84f, 0.28f);   // yellow
        static readonly Color FlashBad     = new Color(1.00f, 0.36f, 0.30f);   // red
        static readonly Color FlashNeutral = new Color(0.78f, 0.82f, 0.90f);   // plain light grey

        // ORDER MATTERS HERE, and every rule below earns its place by a real callout that the
        // previous ordering got wrong. The tests run most-specific first:
        //   1. A FAILURE that happens to name a good thing. "STRIKE 2 - EPIC SAVE!" is a lost
        //      round, not a triumph, and must not come out gold with stars; "NO GOAL" and
        //      "MISSED THE TARGET" both contain a positive word too. The cup's "KNOCKED OUT 2-3"
        //      banner lives here too: it is the end of a player's cup, red, before anything else
        //      in the line (a score, a stage name) can be read as informational.
        //   2. Informational callouts, before any keyword can claim them. The cup's coin toss
        //      result ("HEADS" / "TAILS", design 6.11) is a fact about a coin, not a verdict on the
        //      player - neutral grey - so it sits above the good/bad keyword tests.
        //   3. The epic save, before the plain-save rule swallows it.
        //   4. Plain good, then plain bad. " WIN" (with its leading space) is the cup's round-end
        //      "BRAZIL WIN 4-2"; the space keeps a bare "WIN" from claiming words that merely
        //      contain it, and it is tested AFTER rule 1 so "KNOCKED OUT" can never turn green
        //      because some future line puts both words together.
        static FlashKind KindOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return FlashKind.Neutral;
            string t = text.ToUpperInvariant();

            // 1. Failures first. A strike is a lost round whatever else the line says about it;
            //    KNOCKED OUT is the cup's elimination line.
            if (t.StartsWith("STRIKE") || t.Contains("NO GOAL") || t.Contains("MISSED")
                || t.Contains("ALL OUT") || t.Contains(" IS OUT") || t.Contains("KNOCKED OUT"))
                return FlashKind.Bad;

            // 2. Not verdicts at all: the picked cross delivery, the replay prompt, the neutral
            //    end-of-round lines that report a result rather than judging the player, and the
            //    coin toss result.
            if (t.StartsWith("CROSS:") || t.Contains("REPLAY") || t.StartsWith("TIE")
                || t.StartsWith("GAME OVER") || t.StartsWith("+")
                || t.StartsWith("HEADS") || t.StartsWith("TAILS"))
                return FlashKind.Neutral;

            // 3. Epic, before the plain SAVE rule below can take it.
            if (t.Contains("EPIC")) return FlashKind.Epic;

            // 4. Good, then bad. "OVER"/"WIDE"/"POST" are ball-missed-the-goal words; GAME OVER is
            //    already handled above, which is why "OVER" is safe to test for here. " WIN" is
            //    the cup's "<NATION> WIN a-b" banner.
            if (t.Contains("GOAL") || t.Contains("SAVE") || t.Contains("SCORE")
                || t.Contains("CLEARED") || t.Contains("ON TARGET") || t.Contains("BLOCK")
                || t.Contains("WINS") || t.Contains(" WIN") || t.Contains("SURVIVES"))
                return FlashKind.Good;
            if (t.Contains("MISS") || t.Contains("WIDE") || t.Contains("OVER")
                || t.Contains("POST") || t.Contains("OUT"))
                return FlashKind.Bad;

            return FlashKind.Neutral;
        }

        static Color FlashTint(string text)
        {
            switch (KindOf(text))
            {
                case FlashKind.Good: return FlashGood;
                case FlashKind.Epic: return FlashEpic;
                case FlashKind.Bad:  return FlashBad;
                default:             return FlashNeutral;
            }
        }

        // Back-out overshoot: shoots past 1 then settles. The snap is what makes a callout feel
        // like an impact instead of a fade-in.
        static float EaseOutBack(float t)
        {
            const float c1 = 1.9f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
        static float EaseOutCubic(float t) { float u = 1f - t; return 1f - u * u * u; }

        /// <summary>
        /// The one callout renderer for every mode's GOAL / SAVE / BLOCKED / MISS.
        ///
        /// alpha runs 1 -&gt; 0 across the callout's life, which is all the animation state needed:
        /// punch-in with overshoot, a burst of radial streaks and a colour bloom on the hit frame,
        /// a light sweep across the band, then a slight push-out as it fades. The whole group is
        /// drawn through a scaled GUI.matrix so the text stays crisp at any size.
        /// </summary>
        /// <param name="top">Where the pill's top sits (virtual px). The default hugs the top of the
        /// screen; a mode with its own scoreboard up there (the cup) passes a lower value so the
        /// callout never overlaps it.</param>
        public static void Flash(string text, float alpha, string sub = null, float top = 16f)
        {
            if (alpha <= 0f || string.IsNullOrEmpty(text)) return;
            alpha = Mathf.Clamp01(alpha);

            var kind = KindOf(text);
            Color tint = FlashTint(text);
            bool epic = kind == FlashKind.Epic;

            float life = 1f - alpha;                                   // 0 at spawn -> 1 at death
            float inT  = Mathf.Clamp01(life / 0.18f);                  // punch-in window
            float outT = Mathf.Clamp01((life - 0.84f) / 0.16f);        // fade-out window
            // A small banner cannot punch as hard as the old full-width one without looking like a
            // glitch, so the overshoot is gentler and the drift is a short slide DOWN into place.
            float scale = Mathf.Lerp(0.86f, 1f, EaseOutBack(inT));
            float rise  = Mathf.Lerp(-14f, 0f, EaseOutCubic(inT)) + outT * -8f;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(alpha / 0.30f));

            string label = text.ToUpperInvariant();
            var content = new GUIContent(label);
            Vector2 ts = _flash.CalcSize(content);

            // The pill hugs its text, so a one-word callout stays genuinely small. Stars only take
            // room on an epic save.
            const float padX = 22f, barH = 34f, starW = 20f;
            float w = ts.x + padX * 2f + (epic ? starW * 2f + 12f : 0f);
            w = Mathf.Min(w, W * 0.7f);
            float x = W * 0.5f - w * 0.5f, y = top + rise;
            var band = new Rect(x, y, w, barH);

            var keep = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(band.center.x, band.y));

            // Soft bloom in the callout's colour, tight to the pill.
            var bloom = tint; bloom.a = 0.22f * a;
            UITheme.Glow(new Rect(band.x - 34f, band.y - 22f, band.width + 68f, band.height + 44f), bloom);

            // Plate: near-black pill, a coloured edge, and a colour wash so the state reads from
            // the bar itself rather than only from the word.
            UITheme.Chip(band, new Color(0.02f, 0.03f, 0.05f, 0.88f * a));
            var wash = tint; wash.a = 0.16f * a;
            UITheme.Fill(new Rect(band.x + 1f, band.y + 1f, band.width - 2f, band.height - 2f), wash);
            var rule = tint; rule.a = 0.95f * a;
            UITheme.Fill(new Rect(band.x, band.y, band.width, 2f), rule);
            UITheme.Fill(new Rect(band.x, band.yMax - 2f, band.width, 2f), rule);

            // Stars flanking an epic save, drawn in the band colour.
            if (epic)
            {
                var sc = tint; sc.a = a;
                Star(band.x + padX * 0.5f + starW * 0.5f, band.center.y, 8.5f, sc);
                Star(band.xMax - padX * 0.5f - starW * 0.5f, band.center.y, 8.5f, sc);
            }

            // The word: a dark outline, then the coloured face over it.
            float tx = epic ? band.x + starW + 6f : band.x;
            float tw = epic ? band.width - (starW + 6f) * 2f : band.width;
            var textRect = new Rect(tx, band.y, tw, barH);
            _flash.normal.textColor = new Color(0f, 0f, 0f, 0.8f * a);
            for (int dx = -1; dx <= 1; dx += 2)
                for (int dy = -1; dy <= 1; dy += 2)
                    UITheme.Label(new Rect(textRect.x + dx * 1.5f, textRect.y + dy * 1.5f, textRect.width, textRect.height), label, _flash);
            var face = tint; face.a = a;
            _flash.normal.textColor = face;
            UITheme.Label(textRect, label, _flash);
            _flash.normal.textColor = Ink;

            // Optional second line, in its own smaller pill under the first.
            if (!string.IsNullOrEmpty(sub))
            {
                var subC = new GUIContent(sub.ToUpperInvariant());
                float sw = _flashSub.CalcSize(subC).x + 20f;
                var sr = new Rect(W * 0.5f - sw * 0.5f, band.yMax + 4f, sw, 20f);
                UITheme.Chip(sr, new Color(0.02f, 0.03f, 0.05f, 0.80f * a));
                var scol = Dim; scol.a = a * 0.95f;
                _flashSub.normal.textColor = scol;
                UITheme.Label(sr, subC, _flashSub);
                _flashSub.normal.textColor = Dim;
            }

            GUI.matrix = keep;
        }

        // A five-pointed star, drawn as a fan of triangles from its centre. Used to flank an EPIC
        // callout. Built from Fill spans rather than a texture so it scales with the banner and
        // needs no asset.
        static void Star(float cx, float cy, float r, Color col)
        {
            // Scanline fill of the star polygon: for each row, find where the outline crosses it
            // and fill between the outermost pair. Ten vertices, alternating outer and inner radius.
            const int pts = 10;
            var v = new Vector2[pts];
            for (int i = 0; i < pts; i++)
            {
                float ang = (-90f + i * 36f) * Mathf.Deg2Rad;
                float rad = (i % 2 == 0) ? r : r * 0.42f;
                v[i] = new Vector2(cx + Mathf.Cos(ang) * rad, cy + Mathf.Sin(ang) * rad);
            }

            int rows = Mathf.CeilToInt(r * 2f);
            for (int row = 0; row < rows; row++)
            {
                float yy = cy - r + row;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < pts; i++)
                {
                    Vector2 p1 = v[i], p2 = v[(i + 1) % pts];
                    if ((yy < p1.y && yy < p2.y) || (yy >= p1.y && yy >= p2.y)) continue;
                    float t = (yy - p1.y) / (p2.y - p1.y);
                    float xx = Mathf.Lerp(p1.x, p2.x, t);
                    if (xx < lo) lo = xx;
                    if (xx > hi) hi = xx;
                }
                if (hi > lo) Fill(new Rect(lo, yy, hi - lo, 1f), col);
            }
        }

        // ================================================================ player indicator
        // Per-slot marker colours: eight fully distinct hues keyed to the NET SLOT, so two humans can
        // never draw the same chevron. Nothing a player picks is usable for this - skin comes off one
        // shared swatch palette (SpeciesCosmetics.SkinSwatches), hair/facial/accessory colours are a
        // free-for-all, and the jersey base (PlayerProfile.JerseyBase) is local-only and never on the
        // wire. Slots 0-3 are Home and 4-7 Away (NetMatch.TeamOfSlot), so the first four lean
        // warm and the last four cool: team still reads off the kit, the chevron only says WHO.
        static readonly Color[] _slotCols =
        {
            new Color(1.00f, 0.82f, 0.29f),   // 0 gold
            new Color(1.00f, 0.47f, 0.18f),   // 1 orange
            new Color(1.00f, 0.36f, 0.58f),   // 2 pink
            new Color(0.76f, 0.42f, 1.00f),   // 3 violet
            new Color(0.24f, 0.55f, 1.00f),   // 4 blue
            new Color(0.18f, 0.90f, 0.94f),   // 5 cyan
            new Color(0.29f, 0.82f, 0.48f),   // 6 green
            new Color(0.95f, 0.96f, 0.98f),   // 7 white
        };

        /// <summary>Indicator colour for a slot. Wraps, so any index is safe.</summary>
        public static Color SlotColor(int slot)
            => _slotCols[((slot % _slotCols.Length) + _slotCols.Length) % _slotCols.Length];

        static Camera _worldCam;
        const float MarkerLift = 0.55f;   // metres above the head bone the chevron floats

        /// <summary>
        /// World point to VIRTUAL GUI point, valid inside a Hud.Begin block. Camera.WorldToScreenPoint
        /// gives real device pixels with y = 0 at the BOTTOM, while GUI y = 0 is the TOP and the whole
        /// GUI space is scaled by MenuScale - so both axes divide by the factor and y flips. Returns
        /// false behind the camera (z &lt;= 0), where the projection mirrors and the marker would land on
        /// the opposite side of the screen. Camera.main is the bootstrap camera (GameBootstrap tags it
        /// MainCamera), the same lookup FlexNet uses.
        /// </summary>
        public static bool WorldToGui(Vector3 world, out Vector2 gui)
        {
            gui = default;
            if (_worldCam == null) _worldCam = Camera.main;
            if (_worldCam == null) return false;
            Vector3 sp = _worldCam.WorldToScreenPoint(world);
            if (sp.z <= 0.01f) return false;
            float f = MenuScale.Factor;
            gui = new Vector2(sp.x / f, (Screen.height - sp.y) / f);
            return true;
        }

        /// <summary>
        /// FIFA-style player indicator: a small downward chevron floating over a body's head in that
        /// player's slot colour. IMGUI only - no world geometry, no material, nothing to clean up.
        /// Anchors on the Head bone when the layout has one (a quadruped's head sits lower and further
        /// forward than a biped's) and falls back to the pelvis. Fixed VIRTUAL size, so it stays the
        /// same readable size at any distance and any window scale.
        /// </summary>
        public static void PlayerMarker(ActiveRagdoll body, Color col)
        {
            if (body == null) return;
            Transform head = body.Phys(Bone.Head);
            Vector3 anchor;
            if (head != null) anchor = head.position;
            else if (body.Pelvis != null) anchor = body.Pelvis.position + Vector3.up * 1.35f;
            else return;
            if (!WorldToGui(anchor + Vector3.up * MarkerLift, out var p)) return;
            // Off-screen: drop it rather than pin it to an edge, where it would sit on top of the HUD.
            if (p.x < -40f || p.x > W + 40f || p.y < -40f || p.y > H + 40f) return;

            const int rows = 11;
            const float halfW = 9f, hgt = 12f;

            // Bloom first, in the same hue, so the chevron reads against a bright pitch.
            var bleed = col; bleed.a = 0.30f;
            UITheme.Glow(new Rect(p.x - 17f, p.y - 9f, 34f, 36f), bleed);

            // Solid downward triangle from stacked rows. No rotation, so it needs none of the pivot
            // correction a rotated matrix does under MenuScale (see StatRadar.Line).
            for (int pass = 0; pass < 2; pass++)
            {
                float off = pass == 0 ? 1.5f : 0f;
                Color c = pass == 0 ? new Color(0f, 0f, 0f, 0.45f) : col;
                for (int i = 0; i < rows; i++)
                {
                    float t = i / (float)rows;
                    float w = halfW * 2f * (1f - t);
                    Fill(new Rect(p.x - w * 0.5f + off, p.y + hgt * t + off, w, hgt / rows + 0.6f), c);
                }
            }
        }

        // ================================================================ overlays and widgets
        /// <summary>Full-screen dim behind an in-match overlay (emote wheel, placement map).</summary>
        public static void Scrim(float alpha = 0.5f)
            => Fill(new Rect(0f, 0f, W, H), new Color(0.02f, 0.03f, 0.05f, alpha));

        /// <summary>Row styles for a score table, so every board reads the same.</summary>
        public static GUIStyle RowName => _rowName;
        public static GUIStyle RowValue => _rowVal;

        /// <summary>
        /// Charge meter for a set piece: green -> amber -> red with quarter ticks, a bright leading
        /// edge, and a bloom once the bar is into the red. One widget for every mode that charges a
        /// shot, so the free kick, accuracy and networked set-piece HUDs cannot drift apart.
        /// </summary>
        public static void Meter(float t01, string label = null)
        {
            const float w = 320f, h = 22f;
            Meter(new Rect((W - w) * 0.5f, H - 92f, w, h), t01, label);
        }

        public static void Meter(Rect r, float t01, string label = null)
        {
            float f = Mathf.Clamp01(t01);
            Color fill = f < 0.5f ? Color.Lerp(new Color(0.22f, 0.85f, 0.32f), new Color(0.95f, 0.85f, 0.22f), f * 2f)
                                  : Color.Lerp(new Color(0.95f, 0.85f, 0.22f), new Color(0.92f, 0.22f, 0.17f), (f - 0.5f) * 2f);

            // Red-zone bloom, so a maxed meter is felt rather than read off the bar.
            if (f > 0.85f)
                UITheme.Glow(new Rect(r.x - 16f, r.y - 14f, r.width + 32f, r.height + 28f),
                             new Color(1f, 0.30f, 0.22f, 0.05f + 0.20f * (f - 0.85f) / 0.15f));

            UITheme.Chip(r, new Color(0.05f, 0.06f, 0.09f, 0.92f));
            var inner = new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f);
            Fill(inner, new Color(0.13f, 0.14f, 0.18f, 0.95f));
            Fill(new Rect(inner.x, inner.y, inner.width * f, inner.height), fill);
            // Bright head on the bar: an edge tracks far better than a colour ramp alone.
            if (f > 0.01f)
                Fill(new Rect(inner.x + inner.width * f - 2f, inner.y, 2f, inner.height), Color.white);
            // Quarter ticks, for judging where to release.
            for (int i = 1; i < 4; i++)
                Fill(new Rect(inner.x + inner.width * (i * 0.25f), inner.y, 1f, inner.height),
                     new Color(0f, 0f, 0f, 0.35f));

            if (!string.IsNullOrEmpty(label))
                UITheme.Shadowed(new Rect(r.x, r.y - 20f, r.width, 18f), label, _meterLbl, Gold, 0.6f, 1f);
        }

        // ================================================================ shot charge bar
        // BAND WORDS. charge01 is the entire shot - BallController.LaunchChargedShot reads it and
        // nothing else for elevation and pace - so the band a player sees IS the shot they get, not
        // decoration. Boundaries are simple thirds; the elevation curve itself is a continuous lerp
        // with a kink at 0.5 (LaunchChargedShot: light->mid over the first half, mid->full over the
        // second), so any reasonable split works, and thirds is the easiest one to read at a glance.
        public enum ShotBand { Chip, Placed, Drive }
        const float ShotBandChipMax = 0.34f, ShotBandPlacedMax = 0.70f;

        public static ShotBand BandOf(float t01)
            => t01 < ShotBandChipMax ? ShotBand.Chip
             : t01 < ShotBandPlacedMax ? ShotBand.Placed : ShotBand.Drive;

        static string BandName(ShotBand b) => b == ShotBand.Chip ? "CHIP" : b == ShotBand.Placed ? "PLACED" : "DRIVE";

        /// <summary>
        /// The human shot charge bar. Sits ABOVE the pass bar at the same x and width - LMB/RMB and
        /// Q/E can be held in the same frame, so the two bars stack rather than fight for one slot.
        ///
        /// Three things this says that the pass bar cannot, none of which a player could previously
        /// see at all:
        ///   - the BAND word, because charge01 IS the shot (see BandOf)
        ///   - a FULL FIRES hint, because max charge auto-releases - the pass bar is cap-and-wait and
        ///     this one is not, and nothing on screen used to admit the difference
        ///   - a NO BALL state when held out of range, because Striker.ShotCharge01 reads 0 in both
        ///     "not holding" and "holding, out of range" - a bar driven off charge alone could not
        ///     tell them apart, which is exactly how "the mechanic ran, drew nothing" looked from
        ///     outside. `inRange` is Striker.ShotInRange, sampled independently of the charge value.
        /// </summary>
        public static void ShotBar(float t01, bool holding, bool inRange)
        {
            if (!holding) return;   // not held at all: nothing to show, same rule PowerBar follows
            const float w = 250f, h = 20f, pad = 22f;
            float y = H - 108f - h - 26f;   // stacked above the pass bar with room for its own label

            if (!inRange)
            {
                UITheme.Shadowed(new Rect(pad, y - 20f, w, 18f), "NO BALL", _meterLbl, Dim, 0.6f, 1f);
                Meter(new Rect(pad, y, w, h), 0f);
                return;
            }

            var band = BandOf(t01);
            string top = BandName(band) + (t01 >= 0.999f ? "   FULL - FIRES!" : "");
            UITheme.Shadowed(new Rect(pad, y - 20f, w, 18f), top, _meterLbl, Gold, 0.6f, 1f);
            Meter(new Rect(pad, y, w, h), t01);
        }

        /// <summary>
        /// Bottom-left pass power bar: the player's name and the pass type over a filling meter.
        ///
        /// Deliberately reuses Meter for the fill, so the colour ramp (green -> amber -> red, with the
        /// amber waypoint that stops the middle reading as dirty khaki), the red-zone bloom, the bright
        /// tracking head and the quarter ticks are identical to every other meter in the game. The ticks
        /// matter MORE here than on a set-piece meter rather than less: where you release picks the pass
        /// DISTANCE, so they are the only reference the player has for how far the ball is going.
        ///
        /// Anchored above the legend band rather than at the screen edge, and on the left so it never
        /// meets the centred set-piece meter or the right-hand scoreboard.
        /// </summary>
        public static void PowerBar(string name, float t01, string kind)
        {
            const float w = 250f, h = 20f, pad = 22f;
            float y = H - 108f;
            string top = string.IsNullOrEmpty(kind) ? name : name + "   " + kind;
            if (!string.IsNullOrEmpty(top))
                UITheme.Shadowed(new Rect(pad, y - 20f, w, 18f), top, _meterLbl, Gold, 0.6f, 1f);
            Meter(new Rect(pad, y, w, h), t01);
        }

        /// <summary>Header above and hint below a map overlay, so every map reads the same.</summary>
        public static void OverlayLabel(Rect map, string header, string tip, float headerUp = 34f)
        {
            if (!string.IsNullOrEmpty(header))
                UITheme.Shadowed(new Rect(map.x, map.y - headerUp, map.width, 28f), header, _overlayHdr, Ink, 0.75f, 2f);
            if (!string.IsNullOrEmpty(tip))
                UITheme.Label(new Rect(map.x, map.yMax + 6f, map.width, 22f), tip, _overlayTip);
        }

        /// <summary>Segmented toggle for the map overlays (Ball/Wall, Target/Crosser).</summary>
        public static bool Seg(Rect r, string label, bool on)
        {
            var st = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            st.normal.textColor = on ? Gold : Dim;
            return UITheme.Toggle(r, label, on, st);
        }

        /// <summary>Rounded card with a gold section header: the frame for any score table.</summary>
        public static void Card(Rect r, string header, Color? accent = null)
        {
            UITheme.Panel(r, accent ?? Gold);
            if (string.IsNullOrEmpty(header)) return;
            UITheme.Shadowed(new Rect(r.x + 14f, r.y + 6f, r.width - 28f, 22f), header.ToUpperInvariant(), _title, Gold, 0.6f, 1f);
            UITheme.Divider(r.x + 12f, r.y + 30f, r.width - 24f);
        }

        /// <summary>Page dots, for the emote wheel's pages.</summary>
        public static void PageDots(float cx, float cy, int pages, int current)
        {
            const float gap = 14f;
            float total = (pages - 1) * gap;
            for (int d = 0; d < pages; d++)
                UITheme.Dot(cx - total * 0.5f + d * gap, cy,
                            d == current ? Gold : new Color(1f, 1f, 1f, 0.30f),
                            d == current ? 5f : 3.5f);
        }

        // ================================================================ end-of-round card
        /// <summary>
        /// The END-OF-ROUND card: still centred and still a full panel, because it ENDS play rather
        /// than interrupting it - the in-play callouts are the small top banners (see Flash). It
        /// takes its accent from the same classifier they do, so a win and a knock-out are coloured
        /// consistently with the callouts that led to them.
        /// </summary>
        public static void Banner(string big, string sub, string hint)
        {
            const float w = 520f, h = 200f;
            float x = W * 0.5f - w * 0.5f, y = H * 0.5f - h * 0.5f;
            var r = new Rect(x, y, w, h);

            var kind = KindOf(big);
            Color accent = FlashTint(big);
            bool epic = kind == FlashKind.Epic;

            // Extra darkening behind the card so it reads as the end of play.
            UITheme.Glow(new Rect(r.x - 140f, r.y - 110f, r.width + 280f, r.height + 220f),
                         new Color(0f, 0f, 0f, 0.55f));
            UITheme.Panel(r, accent);

            UITheme.Shadowed(new Rect(x, y + 30f, w, 54f), big, _bannerBig, accent, 0.75f, 2.5f);
            if (epic)
            {
                // Same flanking stars the epic callout gets, sized to this card's heading.
                float half = _bannerBig.CalcSize(new GUIContent(big)).x * 0.5f;
                Star(W * 0.5f - half - 26f, y + 57f, 13f, accent);
                Star(W * 0.5f + half + 26f, y + 57f, 13f, accent);
            }
            if (!string.IsNullOrEmpty(sub))
                UITheme.Shadowed(new Rect(x, y + 94f, w, 30f), sub, _bannerSub, Ink, 0.7f, 2f);

            UITheme.Divider(x + 40f, y + 138f, w - 80f);
            if (!string.IsNullOrEmpty(hint))
                UITheme.Hint(new Rect(x + 20f, y + 150f, w - 40f, 26f), hint);
        }

        // ================================================================ bottom control band
        // The control strings are long and the window can be narrow, so the band FITS the text
        // instead of trusting it: step the font down, then wrap and grow the band. Combined with
        // the MenuScale block this keeps the banner fully on-screen at any resolution.
        //
        // When it fits on one line the band is drawn token by token, keys in gold and actions in
        // grey with dividers between groups, which is a lot easier to scan mid-match than one
        // long grey sentence. Groups are the runs separated by 2+ spaces in the source string.
        const int LegendFont = 13, LegendFontMin = 9;
        const float ChunkGap = 20f;

        static readonly List<string> _chunks = new List<string>();

        public static void Legend(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            float w = W, pad = 14f;
            float inner = Mathf.Max(80f, w - pad * 2f);

            SplitChunks(line);
            int font = FitFont(inner);

            if (font > 0) { DrawRichLegend(inner, pad, font); return; }

            // Too much text for one line even at the floor font: fall back to the plain wrapped
            // label and let the band grow, rather than clipping anything.
            var content = new GUIContent(line);
            _legend.wordWrap = true;
            _legend.fontSize = LegendFontMin;
            float textH = _legend.CalcHeight(content, inner);
            float h = Mathf.Max(28f, textH + 10f);
            BandPlate(w, h);
            UITheme.Label(new Rect(pad, H - h + 5f, inner, h - 10f), line, _legend);
            _legend.wordWrap = false;
            _legend.fontSize = LegendFont;
        }

        static void SplitChunks(string line)
        {
            _chunks.Clear();
            int i = 0, n = line.Length;
            while (i < n)
            {
                while (i < n && line[i] == ' ') i++;
                if (i >= n) break;
                int start = i;
                // A run ends at two or more consecutive spaces (the modes' group separator).
                while (i < n && !(line[i] == ' ' && i + 1 < n && line[i + 1] == ' ')) i++;
                _chunks.Add(line.Substring(start, i - start).Trim());
            }
        }

        /// <summary>Largest font at which the token layout fits one line, or 0 if none does.</summary>
        static int FitFont(float inner)
        {
            for (int f = LegendFont; f >= LegendFontMin; f--)
            {
                if (MeasureChunks(f) <= inner) return f;
            }
            return 0;
        }

        static float MeasureChunks(int font)
        {
            _legend.wordWrap = false; _legend.fontSize = font;
            _legendKey.wordWrap = false; _legendKey.fontSize = font;
            float total = 0f;
            for (int i = 0; i < _chunks.Count; i++)
            {
                SplitKey(_chunks[i], out string key, out string act);
                if (!string.IsNullOrEmpty(key)) total += _legendKey.CalcSize(new GUIContent(key)).x;
                if (!string.IsNullOrEmpty(act)) total += _legend.CalcSize(new GUIContent(" " + act)).x;
                if (i < _chunks.Count - 1) total += ChunkGap;
            }
            return total;
        }

        /// <summary>Split "LMB/RMB legs" into the bind ("LMB/RMB") and what it does ("legs"): the
        /// bind is the leading run of tokens before the first one that starts lowercase.</summary>
        static void SplitKey(string chunk, out string key, out string act)
        {
            key = chunk; act = null;
            if (string.IsNullOrEmpty(chunk)) return;
            int cut = -1, i = 0;
            while (i < chunk.Length)
            {
                int sp = chunk.IndexOf(' ', i);
                int end = sp < 0 ? chunk.Length : sp;
                if (end > i && char.IsLower(chunk[i])) { cut = i; break; }
                if (sp < 0) break;
                i = sp + 1;
            }
            if (cut <= 0) return;                       // all-caps group: it's all bind
            key = chunk.Substring(0, cut).TrimEnd();
            act = chunk.Substring(cut);
        }

        static void BandPlate(float w, float h)
        {
            float top = H - h;
            // Gradient up out of the bottom edge, so the band grounds itself instead of looking
            // like a floating grey bar.
            Fill(new Rect(0f, top, w, h), new Color(0.02f, 0.03f, 0.05f, 0.62f));
            Fill(new Rect(0f, top, w, h * 0.45f), new Color(0.02f, 0.03f, 0.05f, 0.16f));
            Fill(new Rect(0f, top, w, 1f), new Color(1f, 1f, 1f, 0.13f));
            Fill(new Rect(0f, top + 1f, w, 1f), new Color(0f, 0f, 0f, 0.30f));
        }

        static void DrawRichLegend(float inner, float pad, int font)
        {
            float h = Mathf.Max(28f, font + 15f);
            float top = H - h;
            BandPlate(W, h);

            _legend.fontSize = font; _legend.wordWrap = false;
            _legendKey.fontSize = font; _legendKey.wordWrap = false;

            // Centre the row so a short legend doesn't hug the left edge on a wide monitor.
            float total = MeasureChunks(font);
            float x = total < inner - 40f ? (W - total) * 0.5f : pad;
            float rowY = top + 6f, rowH = h - 12f;

            for (int i = 0; i < _chunks.Count; i++)
            {
                SplitKey(_chunks[i], out string key, out string act);
                if (!string.IsNullOrEmpty(key))
                {
                    float kw = _legendKey.CalcSize(new GUIContent(key)).x;
                    UITheme.Label(new Rect(x, rowY, kw + 2f, rowH), key, _legendKey);
                    x += kw;
                }
                if (!string.IsNullOrEmpty(act))
                {
                    string s = " " + act;
                    float aw = _legend.CalcSize(new GUIContent(s)).x;
                    UITheme.Label(new Rect(x, rowY, aw + 2f, rowH), s, _legend);
                    x += aw;
                }
                if (i < _chunks.Count - 1)
                {
                    Fill(new Rect(x + ChunkGap * 0.5f - 0.5f, rowY + 3f, 1f, rowH - 6f), new Color(1f, 1f, 1f, 0.13f));
                    x += ChunkGap;
                }
            }

            _legend.fontSize = LegendFont;
            _legendKey.fontSize = LegendFont;
        }
    }
}
