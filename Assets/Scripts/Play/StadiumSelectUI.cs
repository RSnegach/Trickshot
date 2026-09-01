using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Stadium picker shown after the mode is chosen and before the pre-match config.
    /// Lists every pickable StadiumStyle by name; selecting one sets
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
            { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            float row = y + 70f;
            for (int i = 0; i < all.Length; i++)
            {
                // Not offered yet (StadiumStyle.Pickable). Skipped rather than greyed so the list
                // has no holes; the entry stays in All so its wire index still resolves.
                if (!all[i].Pickable) continue;
                bool selected = i == StadiumStyle.SelectedIndex;
                var r = new Rect(x + 30f, row, panelW - 60f, rowH);
                // A row press picks the stadium AND advances immediately - there is no Next
                // button. The lit plate still marks the current pick in the frame before the
                // click lands. The name IS the row: big and centred, no blurb underneath, no
                // palette swatches, no accent spine.
                if (UITheme.Toggle(r, all[i].Name, selected, name))
                {
                    StadiumStyle.SelectedIndex = i;
                    enabled = false;
                    _onPicked?.Invoke();
                    MenuScale.End();
                    return;
                }
                row += rowH + gap;
            }

            // Teaser card. Highlights on mouse-over like a real row but is not clickable, so the
            // venues still to come are visible without pretending they are playable. UITheme.Tease
            // builds no control at all; GUI.enabled = false would have taken the highlight with it.
            var soon = new Rect(x + 30f, row, panelW - 60f, rowH);
            UITheme.Tease(soon, "Coming Soon...", name);

            // Back anchored to the far left screen edge.
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            float bw = 150f, edge = 24f, by = MenuScale.Height - 100f;   // 100px above the bottom, clear of panel content
            if (UITheme.Button(new Rect(edge, by, bw, 42f), "Back", btn))
            {
                enabled = false;
                _onBack?.Invoke();
                MenuScale.End();
                return;
            }
            MenuScale.End();
        }
    }
}
