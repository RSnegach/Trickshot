using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Career Stats screen, opened from the main menu hub. Same shape as OptionsMenu/CustomizeUI -
    /// a plain class MenuUI holds and calls Draw(...) on, not a MonoBehaviour.
    ///
    /// Categories cycle with a compact ‹ › arrow (CustomizeUI's pattern) rather than a row of tab
    /// buttons (OptionsMenu's pattern) - nine categories as button chips would be cramped. Every
    /// number comes straight from CareerStats.Data; there is no formatting beyond plain label/value
    /// rows, per instruction. Friends is an honest placeholder - there is no player-account/friend
    /// system yet, so it names that plainly rather than showing fabricated names or numbers.
    /// </summary>
    public class CareerStatsUI
    {
        enum Cat { Overall, Striker, Goalkeeper, Accuracy, TimeTrial, FreeKick, Freeplay, Scrimmage, Friends }
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
                case Cat.TimeTrial:  return "TIME TRIAL";
                case Cat.FreeKick:   return "FREE KICK";
                case Cat.Freeplay:   return "FREEPLAY";
                case Cat.Scrimmage:  return "SCRIMMAGE";
                default:             return "FRIENDS";
            }
        }

        public void Draw(System.Action onBack)
        {
            float w = 700f, h = 560f;
            float x = MenuScale.Width * 0.5f - w * 0.5f, y = MenuScale.Height * 0.5f - h * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, w + 260f);
            UITheme.Panel(new Rect(x, y, w, h), UITheme.Gold);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Ink } };
            UITheme.Shadowed(new Rect(x + 24f, y + 14f, w - 48f, 34f), "CAREER STATS", title, UITheme.Ink, 0.7f, 2f);

            int count = System.Enum.GetValues(typeof(Cat)).Length;
            var arrow = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(x + 24f, y + 58f, 36f, 30f), "‹", arrow)) _cat = (_cat - 1 + count) % count;
            var catStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            UITheme.Shadowed(new Rect(x + 60f, y + 58f, w - 48f - 72f, 30f), CatName((Cat)_cat), catStyle, UITheme.Gold, 0.75f, 2f);
            if (GUI.Button(new Rect(x + w - 24f - 36f, y + 58f, 36f, 30f), "›", arrow)) _cat = (_cat + 1) % count;

            UITheme.Divider(x + 24f, y + 96f, w - 48f);

            float cy = y + 104f, cw = w;
            switch ((Cat)_cat)
            {
                case Cat.Overall:    DrawRows(x, cy, cw, OverallRows()); break;
                case Cat.Striker:    DrawRows(x, cy, cw, StrikerRows()); break;
                case Cat.Goalkeeper: DrawRows(x, cy, cw, GoalkeeperRows()); break;
                case Cat.Accuracy:   DrawRows(x, cy, cw, AccuracyRows()); break;
                case Cat.TimeTrial:  DrawRows(x, cy, cw, TimeTrialRows()); break;
                case Cat.FreeKick:   DrawRows(x, cy, cw, FreeKickRows()); break;
                case Cat.Freeplay:   DrawRows(x, cy, cw, FreeplayRows()); break;
                case Cat.Scrimmage:  DrawRows(x, cy, cw, ScrimmageRows()); break;
                case Cat.Friends:    DrawFriends(x, cy, cw); break;
            }

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

            if (_confirmAct != null) DrawConfirm();
        }

        // ---- per-category rows ----

        static (string, string)[] OverallRows()
        {
            var d = CareerStats.Data;
            int goals = d.StrikerGoals + d.TimeTrialGoals + d.FreeKickGoals + d.FreeplayGoals + d.ScrimmageGoals;
            int saves = d.KeeperSaves + d.ScrimmageSaves;
            int crosses = d.StrikerCrosses + d.TimeTrialCrosses + d.FreeplayCrosses;
            return new[]
            {
                ("Matches played", d.ScrimmageMatchesPlayed.ToString()),
                ("Goals scored (all modes)", goals.ToString()),
                ("Saves (all modes)", saves.ToString()),
                ("Crosses (all modes)", crosses.ToString()),
            };
        }

        static (string, string)[] StrikerRows()
        {
            var d = CareerStats.Data;
            return new[]
            {
                ("Goals scored", d.StrikerGoals.ToString()),
                ("Trick goals", d.StrikerTrickGoals.ToString()),
                ("Crosses", d.StrikerCrosses.ToString()),
                ("Shots denied", d.StrikerShotsDenied.ToString()),
            };
        }

        static (string, string)[] GoalkeeperRows()
        {
            var d = CareerStats.Data;
            return new[]
            {
                ("Saves", d.KeeperSaves.ToString()),
                ("Shots faced", d.KeeperShotsFaced.ToString()),
                ("Goals conceded", d.KeeperGoalsConceded.ToString()),
                ("Save percentage", Pct(d.KeeperSaves, d.KeeperShotsFaced)),
            };
        }

        static (string, string)[] AccuracyRows()
        {
            var d = CareerStats.Data;
            return new[]
            {
                ("Rounds played", d.AccuracyRoundsPlayed.ToString()),
                ("Kicks taken", d.AccuracyKicks.ToString()),
                ("Targets hit", d.AccuracyTargetsHit.ToString()),
                ("Best score", d.AccuracyBestScore.ToString()),
                ("Average score", Avg(d.AccuracyTotalScore, d.AccuracyRoundsPlayed)),
            };
        }

        static (string, string)[] TimeTrialRows()
        {
            var d = CareerStats.Data;
            return new[]
            {
                ("Runs played", d.TimeTrialRunsPlayed.ToString()),
                ("Crosses", d.TimeTrialCrosses.ToString()),
                ("Goals scored", d.TimeTrialGoals.ToString()),
                ("Best run (goals)", d.TimeTrialBestRunGoals.ToString()),
            };
        }

        static (string, string)[] FreeKickRows()
        {
            var d = CareerStats.Data;
            return new[]
            {
                ("Attempts", d.FreeKickAttempts.ToString()),
                ("Goals scored", d.FreeKickGoals.ToString()),
                ("Conversion", Pct(d.FreeKickGoals, d.FreeKickAttempts)),
            };
        }

        static (string, string)[] FreeplayRows()
        {
            var d = CareerStats.Data;
            return new[]
            {
                ("Crosses", d.FreeplayCrosses.ToString()),
                ("Goals scored", d.FreeplayGoals.ToString()),
            };
        }

        static (string, string)[] ScrimmageRows()
        {
            var d = CareerStats.Data;
            return new[]
            {
                ("Matches played", d.ScrimmageMatchesPlayed.ToString()),
                ("Wins", d.ScrimmageWins.ToString()),
                ("Losses", d.ScrimmageLosses.ToString()),
                ("Draws", d.ScrimmageDraws.ToString()),
                ("Goals", d.ScrimmageGoals.ToString()),
                ("Assists", d.ScrimmageAssists.ToString()),
                ("Shots", d.ScrimmageShots.ToString()),
                ("Tackles", d.ScrimmageTackles.ToString()),
                ("Saves", d.ScrimmageSaves.ToString()),
                ("Conceded", d.ScrimmageConceded.ToString()),
                ("Passes", d.ScrimmagePasses.ToString()),
                ("Passes completed", d.ScrimmagePassesCompleted.ToString()),
            };
        }

        static string Pct(int made, int total) => total <= 0 ? "-" : Mathf.RoundToInt(100f * made / total) + "%";
        static string Avg(long total, int count) => count <= 0 ? "-" : (total / (float)count).ToString("0.0");

        void DrawRows(float x, float y, float w, (string label, string value)[] rows)
        {
            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            var val = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Gold } };
            float lx = x + 24f, rowH = 28f, gap = 6f, rw = w - 48f;
            for (int i = 0; i < rows.Length; i++)
            {
                float ry = y + i * (rowH + gap);
                GUI.Label(new Rect(lx, ry, rw * 0.6f, rowH), rows[i].label, lbl);
                GUI.Label(new Rect(lx + rw * 0.6f, ry, rw * 0.4f, rowH), rows[i].value, val);
                UITheme.Divider(lx, ry + rowH + gap * 0.5f, rw);
            }
        }

        void DrawFriends(float x, float y, float w)
        {
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, alignment = TextAnchor.UpperLeft, normal = { textColor = UITheme.Faint } };
            GUI.Label(new Rect(x + 24f, y + 8f, w - 48f, 80f),
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
            bool no = GUI.Button(cancel, "Cancel", btn);
            GUI.backgroundColor = _confirmYes ? UITheme.BadTint : keep;
            bool yes = GUI.Button(ok, "Confirm", btn);
            GUI.backgroundColor = keep;

            if (no) ClearConfirm();
            else if (yes) { var act = _confirmAct; ClearConfirm(); act?.Invoke(); }
        }

        void ClearConfirm() { _confirmAct = null; _confirmTitle = null; _confirmBody = null; _confirmYes = false; }
    }
}
