using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Career Stats screen, opened from the main menu hub. Same shape as SettingsMenu/CustomizeUI -
    /// a plain class MenuUI holds and calls Draw(...) on, not a MonoBehaviour.
    ///
    /// Categories cycle with a compact ‹ › arrow (CustomizeUI's pattern) rather than a row of tab
    /// buttons (SettingsMenu's pattern) - nine categories as button chips would be cramped. Every
    /// category is a 3-column row list (label / SP / MP) rather than a stacked SP block then an
    /// MP block - Match's tab is already the tallest at 13 rows, and doubling every tab's row
    /// count would blow the panel's height budget. Numbers come straight from CareerStats.Data;
    /// there is no formatting beyond plain label/value rows, per instruction. An MP column reads
    /// all-zero for every mode except Match until a future pass wires MP recording into the
    /// others - expected, not a bug. Friends is an honest placeholder - there is no player-account/
    /// friend system yet, so it names that plainly rather than showing fabricated names or numbers.
    /// </summary>
    public class CareerStatsUI
    {
        enum Cat { Overall, Striker, Goalkeeper, Accuracy, FreeKick, Match, Friends }
        int _cat;

        // Reset-all confirm. Same shape as PauseMenu's _confirmAct/_confirmTitle/_confirmBody/
        // _confirmYes - the only confirm-dialog precedent in the codebase, copied rather than
        // inventing a second one.
        System.Action _confirmAct;
        string _confirmTitle, _confirmBody;
        bool _confirmYes;

        static string CatName(Cat c)
        {
            switch (c)
            {
                case Cat.Overall:    return "OVERALL";
                case Cat.Striker:    return "STRIKER";
                case Cat.Goalkeeper: return "GOALKEEPER";
                case Cat.Accuracy:   return "ACCURACY";
                case Cat.FreeKick:   return "FREE KICK";
                case Cat.Match:      return "MATCH";
                default:             return "FRIENDS";
            }
        }

        public void Draw(System.Action onBack)
        {
            // 650 tall: Match's 13 rows (28px + 6px gap each), plus the SP/MP column header above
            // them, need the extra room over the other categories' handful, or the last row and
            // its divider bleed under the buttons.
            float w = 700f, h = 650f;
            float x = MenuScale.Width * 0.5f - w * 0.5f, y = MenuScale.Height * 0.5f - h * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, w + 260f);
            UITheme.Panel(new Rect(x, y, w, h), UITheme.Gold);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Ink } };
            UITheme.Shadowed(new Rect(x + 24f, y + 14f, w - 48f, 34f), "CAREER STATS", title, UITheme.Ink, 0.7f, 2f);

            // Modal, same as PauseMenu.DrawConfirm's own guard: while a reset confirm is pending,
            // it fully replaces the switcher/rows/buttons rather than sitting over still-clickable
            // ones - otherwise Back stays live under it, and leaving that way carries the pending
            // confirm into the next visit (this page's CareerStatsUI instance is never recreated).
            if (_confirmAct != null) { DrawConfirm(); return; }

            int count = System.Enum.GetValues(typeof(Cat)).Length;
            var arrow = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(x + 24f, y + 58f, 36f, 30f), "‹", arrow)) _cat = (_cat - 1 + count) % count;
            var catStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            UITheme.Shadowed(new Rect(x + 60f, y + 58f, w - 48f - 72f, 30f), CatName((Cat)_cat), catStyle, UITheme.Gold, 0.75f, 2f);
            if (UITheme.Button(new Rect(x + w - 24f - 36f, y + 58f, 36f, 30f), "›", arrow)) _cat = (_cat + 1) % count;

            UITheme.Divider(x + 24f, y + 96f, w - 48f);

            float cy = y + 128f, cw = w;   // 104 + 24 to leave room for DrawRows' own SP/MP header
            if ((Cat)_cat == Cat.Friends) DrawFriends(x, cy, cw);
            else DrawRows(x, cy, cw, RowsFor((Cat)_cat));

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(x + 24f, y + h - 58f, 140f, 42f), "Back", btn))
                onBack?.Invoke();
            if (UITheme.Button(new Rect(x + w - 24f - 220f, y + h - 58f, 220f, 42f), "Reset All Stats", btn, bad: true))
            {
                _confirmAct = CareerStats.ResetAll;
                _confirmTitle = "RESET ALL STATS?";
                _confirmBody = "Erases every lifetime stat on this machine. This cannot be undone.";
                _confirmYes = false;
            }
        }

        // ---- per-category rows: (label, SP value, MP value) ----

        static (string label, string sp, string mp)[] RowsFor(Cat cat)
        {
            switch (cat)
            {
                case Cat.Overall:    return OverallRows();
                case Cat.Striker:    return StrikerRows();
                case Cat.Goalkeeper: return GoalkeeperRows();
                case Cat.Accuracy:   return AccuracyRows();
                case Cat.FreeKick:   return FreeKickRows();
                default:             return MatchRows();
            }
        }

        static (string, string, string)[] OverallRows()
        {
            var sp = CareerStats.Data.SP; var mp = CareerStats.Data.MP;
            return new[]
            {
                ("Matches played", sp.MatchesPlayed.ToString(), mp.MatchesPlayed.ToString()),
                ("Goals scored (all modes)", OverallGoals(sp).ToString(), OverallGoals(mp).ToString()),
                ("Saves (all modes)", OverallSaves(sp).ToString(), OverallSaves(mp).ToString()),
                ("Crosses (all modes)", OverallCrosses(sp).ToString(), OverallCrosses(mp).ToString()),
            };
        }
        static int OverallGoals(ModeStats d) => d.StrikerGoals + d.FreeKickGoals + d.MatchGoals;
        static int OverallSaves(ModeStats d) => d.KeeperSaves + d.MatchSaves;
        static int OverallCrosses(ModeStats d) => d.StrikerCrosses;

        static (string, string, string)[] StrikerRows()
        {
            var sp = CareerStats.Data.SP; var mp = CareerStats.Data.MP;
            return new[]
            {
                ("Goals scored", sp.StrikerGoals.ToString(), mp.StrikerGoals.ToString()),
                ("Trick goals", sp.StrikerTrickGoals.ToString(), mp.StrikerTrickGoals.ToString()),
                ("Crosses", sp.StrikerCrosses.ToString(), mp.StrikerCrosses.ToString()),
                ("Shots denied", sp.StrikerShotsDenied.ToString(), mp.StrikerShotsDenied.ToString()),
            };
        }

        static (string, string, string)[] GoalkeeperRows()
        {
            var sp = CareerStats.Data.SP; var mp = CareerStats.Data.MP;
            return new[]
            {
                ("Saves", sp.KeeperSaves.ToString(), mp.KeeperSaves.ToString()),
                ("Shots faced", sp.KeeperShotsFaced.ToString(), mp.KeeperShotsFaced.ToString()),
                ("Goals conceded", sp.KeeperGoalsConceded.ToString(), mp.KeeperGoalsConceded.ToString()),
                ("Save percentage", Pct(sp.KeeperSaves, sp.KeeperShotsFaced), Pct(mp.KeeperSaves, mp.KeeperShotsFaced)),
            };
        }

        static (string, string, string)[] AccuracyRows()
        {
            var sp = CareerStats.Data.SP; var mp = CareerStats.Data.MP;
            return new[]
            {
                ("Rounds played", sp.AccuracyRoundsPlayed.ToString(), mp.AccuracyRoundsPlayed.ToString()),
                ("Kicks taken", sp.AccuracyKicks.ToString(), mp.AccuracyKicks.ToString()),
                ("Targets hit", sp.AccuracyTargetsHit.ToString(), mp.AccuracyTargetsHit.ToString()),
                ("Best score (keeper)", sp.AccuracyBestScore.ToString(), mp.AccuracyBestScore.ToString()),
                ("Best score (open goal)", sp.AccuracyBestScoreNoKeeper.ToString(), mp.AccuracyBestScoreNoKeeper.ToString()),
                ("Average score", Avg(sp.AccuracyTotalScore, sp.AccuracyRoundsPlayed), Avg(mp.AccuracyTotalScore, mp.AccuracyRoundsPlayed)),
            };
        }

        static (string, string, string)[] FreeKickRows()
        {
            var sp = CareerStats.Data.SP; var mp = CareerStats.Data.MP;
            return new[]
            {
                ("Attempts", sp.FreeKickAttempts.ToString(), mp.FreeKickAttempts.ToString()),
                ("Goals scored", sp.FreeKickGoals.ToString(), mp.FreeKickGoals.ToString()),
                ("Conversion", Pct(sp.FreeKickGoals, sp.FreeKickAttempts), Pct(mp.FreeKickGoals, mp.FreeKickAttempts)),
            };
        }

        static (string, string, string)[] MatchRows()
        {
            var sp = CareerStats.Data.SP; var mp = CareerStats.Data.MP;
            return new[]
            {
                ("Matches played", sp.MatchesPlayed.ToString(), mp.MatchesPlayed.ToString()),
                ("Wins", sp.MatchWins.ToString(), mp.MatchWins.ToString()),
                ("Losses", sp.MatchLosses.ToString(), mp.MatchLosses.ToString()),
                ("Draws", sp.MatchDraws.ToString(), mp.MatchDraws.ToString()),
                ("Goals", sp.MatchGoals.ToString(), mp.MatchGoals.ToString()),
                ("Assists", sp.MatchAssists.ToString(), mp.MatchAssists.ToString()),
                ("Shots", sp.MatchShots.ToString(), mp.MatchShots.ToString()),
                ("Tackles", sp.MatchTackles.ToString(), mp.MatchTackles.ToString()),
                ("Saves", sp.MatchSaves.ToString(), mp.MatchSaves.ToString()),
                ("Conceded", sp.MatchConceded.ToString(), mp.MatchConceded.ToString()),
                ("Passes", sp.MatchPasses.ToString(), mp.MatchPasses.ToString()),
                ("Passes completed", sp.MatchPassesCompleted.ToString(), mp.MatchPassesCompleted.ToString()),
                ("Man of the Match", sp.MatchMOTM.ToString(), mp.MatchMOTM.ToString()),
            };
        }

        static string Pct(int made, int total) => total <= 0 ? "-" : Mathf.RoundToInt(100f * made / total) + "%";
        static string Avg(long total, int count) => count <= 0 ? "-" : (total / (float)count).ToString("0.0");

        void DrawRows(float x, float y, float w, (string label, string sp, string mp)[] rows)
        {
            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            var val = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Gold } };
            var head = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Faint } };

            float lx = x + 24f, rowH = 28f, gap = 6f, rw = w - 48f;
            float colLabel = rw * 0.5f, colVal = rw * 0.25f;
            float xSp = lx + colLabel, xMp = xSp + colVal;

            // One SP/MP mini-header above the list, not per row.
            UITheme.Label(new Rect(xSp, y - 24f, colVal, 16f), "SP", head);
            UITheme.Label(new Rect(xMp, y - 24f, colVal, 16f), "MP", head);

            for (int i = 0; i < rows.Length; i++)
            {
                float ry = y + i * (rowH + gap);
                UITheme.Label(new Rect(lx, ry, colLabel, rowH), rows[i].label, lbl);
                UITheme.Label(new Rect(xSp, ry, colVal, rowH), rows[i].sp, val);
                UITheme.Label(new Rect(xMp, ry, colVal, rowH), rows[i].mp, val);
                UITheme.Divider(lx, ry + rowH + gap * 0.5f, rw);
            }
        }

        void DrawFriends(float x, float y, float w)
        {
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = UITheme.Faint } };
            UITheme.Label(new Rect(x + 24f, y + 8f, w - 48f, 80f),
                "Needs player accounts - coming later. Once accounts exist, a friend's own stats will show here.",
                hint);
        }

        // ---- reset-all confirm, copied from PauseMenu.DrawConfirm ----
        void DrawConfirm()
        {
            float sw = MenuScale.Width, sh = MenuScale.Height;
            const float w = 440f, h = 200f;
            var r = new Rect(sw * 0.5f - w * 0.5f, sh * 0.5f - h * 0.5f, w, h);
            UITheme.Panel(r, UITheme.Red);

            var t = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            UITheme.Shadowed(new Rect(r.x, r.y + 22f, r.width, 38f), _confirmTitle, t, UITheme.Ink, 0.7f, 2f);
            UITheme.Hint(new Rect(r.x + 20f, r.y + 66f, r.width - 40f, 40f), _confirmBody);

            const float bw = 176f, bh = 48f;
            float by = r.yMax - bh - 24f;
            var cancel = new Rect(r.center.x - bw - 8f, by, bw, bh);
            var ok = new Rect(r.center.x + 8f, by, bw, bh);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };

            var e = Event.current;
            if (cancel.Contains(e.mousePosition)) _confirmYes = false;
            if (ok.Contains(e.mousePosition)) _confirmYes = true;

            var keep = GUI.backgroundColor;
            if (!_confirmYes) GUI.backgroundColor = UITheme.SelTint;
            bool no = UITheme.Button(cancel, "Cancel", btn);
            GUI.backgroundColor = _confirmYes ? UITheme.BadTint : keep;
            bool yes = UITheme.Button(ok, "Confirm", btn);
            GUI.backgroundColor = keep;

            if (no) ClearConfirm();
            else if (yes) { var act = _confirmAct; ClearConfirm(); act?.Invoke(); }
        }

        void ClearConfirm() { _confirmAct = null; _confirmTitle = null; _confirmBody = null; _confirmYes = false; }
    }
}
