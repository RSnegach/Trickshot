using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Small flyout for the Hub's "Achievements" chip (MenuUI.cs). Lists Achievements.All with a
    /// progress bar per StatThreshold entry - fully local, no Steam needed to show progress (only
    /// the "also tell Steam" side effect on unlock needs it, and that's invisible here either
    /// way). A LeaderboardTop entry (none defined yet - see Achievements.cs) would show as
    /// "not available yet" rather than a progress bar, since nothing can evaluate it.
    /// </summary>
    public static class AchievementsPanelUI
    {
        public static void Draw(Rect r, System.Action onClose)
        {
            UITheme.Panel(r, UITheme.Blue);
            UITheme.Section(new Rect(r.x + 16f, r.y + 10f, r.width - 32f, 18f), "ACHIEVEMENTS");

            var closeBtn = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            if (UITheme.Button(new Rect(r.x + r.width - 30f, r.y + 6f, 22f, 22f), "x", closeBtn)) onClose?.Invoke();

            var title = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var desc = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            var data = CareerStats.Data;

            float y = r.y + 34f, lx = r.x + 16f, lw = r.width - 32f;
            const float rowH = 54f;
            float maxRows = Mathf.Floor((r.height - 44f) / rowH);
            for (int i = 0; i < Achievements.All.Length && i < maxRows; i++)
            {
                var a = Achievements.All[i];
                bool unlocked = Achievements.IsUnlocked(a.Id);
                title.normal.textColor = unlocked ? UITheme.Gold : UITheme.Ink;
                UITheme.Label(new Rect(lx, y, lw, 18f), (unlocked ? "★ " : "") + a.Title, title);
                UITheme.Label(new Rect(lx, y + 17f, lw, 16f), a.Description, desc);

                if (a.Kind == AchievementKind.StatThreshold)
                {
                    int cur = Mathf.Min(a.CurrentValue(data), a.Target);
                    var barBack = new Rect(lx, y + 36f, lw, 8f);
                    UITheme.Fill(barBack, new Color(1f, 1f, 1f, 0.08f));
                    float frac = a.Target > 0 ? (float)cur / a.Target : 0f;
                    if (frac > 0f) UITheme.Fill(new Rect(barBack.x, barBack.y, barBack.width * frac, barBack.height),
                                                unlocked ? UITheme.Gold : UITheme.Green);
                    var progSt = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Faint } };
                    UITheme.Label(new Rect(lx, y + 36f, lw, 8f), cur + " / " + a.Target, progSt);
                }
                else
                {
                    UITheme.Label(new Rect(lx, y + 36f, lw, 14f), "Not available yet.",
                              new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = UITheme.Faint } });
                }

                y += rowH;
            }
        }
    }
}
