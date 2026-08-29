using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Stadium picker shown after the mode is chosen and before the pre-match config.
    /// Lists every StadiumStyle with its blurb; selecting one sets
    /// StadiumStyle.SelectedIndex and continues. IMGUI, no Canvas wiring.
    /// </summary>
    public class StadiumSelectUI : MonoBehaviour
    {
        System.Action _onPicked;
        System.Action _onBack;

        public void Init(System.Action onPicked, System.Action onBack)
        {
            _onPicked = onPicked;
            _onBack = onBack;
            GameInput.CaptureCursor(false);
        }

        void OnGUI()
        {
            // Scale the whole menu up on big displays (see MenuScale): all sizes below stay the
            // same numbers, they just cover more of the screen. Use MenuScale.Width/Height instead
            // of Screen.* while scaled.
            MenuScale.Begin();
            var all = StadiumStyle.All;
            float panelW = 560f, rowH = 74f, gap = 14f;
            // Offered venues + the one Coming Soon card, not the whole catalog: the hidden venues
            // still sit in All so their wire index keeps resolving.
            int shown = 0;
            for (int i = 0; i < all.Length; i++) if (all[i].Pickable) shown++;
            float panelH = 150f + (shown + 1) * (rowH + gap);
            float x = MenuScale.Width * 0.5f - panelW * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, panelW + 260f);
            UITheme.Panel(new Rect(x, y, panelW, panelH), UITheme.Gold);

            UITheme.Title(new Rect(x, y + 14f, panelW, 44f), "SELECT STADIUM", 34);

            var name = new GUIStyle(GUI.skin.button)
            { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var blurb = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.LowerLeft, normal = { textColor = UITheme.Dim }, wordWrap = true };

            float row = y + 70f;
            for (int i = 0; i < all.Length; i++)
            {
                // Not offered yet (StadiumStyle.Pickable). Skipped rather than greyed so the list
                // has no holes; the entry stays in All so its wire index still resolves.
                if (!all[i].Pickable) continue;
                bool selected = i == StadiumStyle.SelectedIndex;
                var r = new Rect(x + 30f, row, panelW - 60f, rowH);
                // Row click SELECTS only (highlights it); a dedicated Next button advances,
                // so the flow matches the other screens' back/forward buttons.
                if (UITheme.Toggle(r, "    " + all[i].Name, selected, name))
                    StadiumStyle.SelectedIndex = i;
                // Gold spine on the leading edge marks the pick (replaces the old text arrow).
                if (selected) UITheme.Fill(new Rect(r.x + 6f, r.y + 8f, 3f, r.height - 16f), UITheme.Gold);

                // Palette swatch: turf, seats, accent. Tells the venues apart at a glance.
                var st = all[i];
                UITheme.Chip(new Rect(r.xMax - 74f, r.y + 11f, 20f, 20f), st.Grass);
                UITheme.Chip(new Rect(r.xMax - 48f, r.y + 11f, 20f, 20f), st.Seats);
                UITheme.Chip(new Rect(r.xMax - 22f, r.y + 11f, 20f, 20f), st.Accent);

                GUI.Label(new Rect(r.x + 14f, row + 34f, r.width - 28f, rowH - 36f), all[i].Blurb, blurb);
                row += rowH + gap;
            }

            // Teaser card. Highlights on mouse-over like a real row but is not clickable, so the
            // venues still to come are visible without pretending they are playable. UITheme.Tease
            // builds no control at all; GUI.enabled = false would have taken the highlight with it.
            var soon = new Rect(x + 30f, row, panelW - 60f, rowH);
            bool soonHot = UITheme.Tease(soon, "    Coming Soon...", name);
            GUI.Label(new Rect(soon.x + 14f, row + 34f, soon.width - 28f, rowH - 36f),
                      "More venues on the way.", blurb);
            if (soonHot) UITheme.Fill(new Rect(soon.x + 6f, soon.y + 8f, 3f, soon.height - 16f), UITheme.Dim);

            // Back/Next anchored to the far left/right screen edges.
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            float bw = 150f, edge = 24f, by = MenuScale.Height - 100f;   // 100px above the bottom, clear of panel content
            if (UITheme.Button(new Rect(edge, by, bw, 42f), "Back", btn))
            {
                enabled = false;
                _onBack?.Invoke();
                MenuScale.End();
                return;
            }
            if (UITheme.Button(new Rect(MenuScale.Width - edge - bw, by, bw, 42f), "Next", btn))
            {
                enabled = false;
                _onPicked?.Invoke();
                MenuScale.End();
                return;
            }
            MenuScale.End();
        }
    }
}
