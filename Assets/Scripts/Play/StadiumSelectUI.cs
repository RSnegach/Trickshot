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
            float panelH = 150f + all.Length * (rowH + gap);
            float x = MenuScale.Width * 0.5f - panelW * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;

            GUI.Box(new Rect(x, y, panelW, panelH), GUIContent.none);

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 34, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(x, y + 14f, panelW, 44f), "SELECT STADIUM", title);

            var name = new GUIStyle(GUI.skin.button)
            { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var blurb = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.LowerLeft, normal = { textColor = new Color(0.85f, 0.85f, 0.88f) }, wordWrap = true };

            float row = y + 70f;
            for (int i = 0; i < all.Length; i++)
            {
                bool selected = i == StadiumStyle.SelectedIndex;
                var r = new Rect(x + 30f, row, panelW - 60f, rowH);
                string tag = (selected ? "▶ " : "   ") + all[i].Name;
                // Row click SELECTS only (highlights it); a dedicated Next button advances,
                // so the flow matches the other screens' back/forward buttons.
                if (GUI.Button(r, tag, name))
                    StadiumStyle.SelectedIndex = i;
                GUI.Label(new Rect(r.x + 14f, row + 34f, r.width - 28f, rowH - 36f), all[i].Blurb, blurb);
                row += rowH + gap;
            }

            // Back/Next anchored to the far left/right screen edges.
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            float bw = 150f, edge = 24f, by = MenuScale.Height - 100f;   // 100px above the bottom, clear of panel content
            if (GUI.Button(new Rect(edge, by, bw, 42f), "Back", btn))
            {
                enabled = false;
                _onBack?.Invoke();
                MenuScale.End();
                return;
            }
            if (GUI.Button(new Rect(MenuScale.Width - edge - bw, by, bw, 42f), "Next", btn))
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
