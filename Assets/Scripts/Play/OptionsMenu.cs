using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// Options overlay opened from the pause menu. Tabbed; the first (and currently only)
    /// tab is KEYBINDINGS: a standard rebinding list - one row per action showing its
    /// current key/mouse bind, click to rebind (then press any key/mouse button), with
    /// duplicate binds highlighted and a Reset to Defaults button.
    ///
    /// Drawn by PauseMenu when Options is open. IMGUI, consistent with the rest of the UI.
    /// </summary>
    public class OptionsMenu
    {
        enum Tab { Keybindings }
        Tab _tab = Tab.Keybindings;

        GameInput _input;
        string _listening;   // action currently awaiting a key press (null = none)
        InputActionRebindingExtensions.RebindingOperation _op;

        public bool IsRebinding => _listening != null;

        public OptionsMenu(GameInput input) { _input = input; }

        // Returns true while open; PauseMenu calls Draw and closes when it returns false.
        // `onClose` fires when the user backs out.
        public void Draw(System.Action onClose)
        {
            float w = 620f, h = 470f;
            float x = Screen.width * 0.5f - w * 0.5f, y = Screen.height * 0.5f - h * 0.5f;

            var prev = GUI.color; GUI.color = new Color(0.06f, 0.07f, 0.10f, 0.96f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.86f, 0.32f); GUI.DrawTexture(new Rect(x, y, w, 4f), Texture2D.whiteTexture);
            GUI.color = prev;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x + 24f, y + 14f, w - 48f, 34f), "OPTIONS", title);

            // Tab strip (room for more tabs later).
            var tab = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            var tabSel = new GUIStyle(tab); tabSel.normal.textColor = new Color(1f, 0.9f, 0.3f);
            GUI.Button(new Rect(x + 24f, y + 54f, 140f, 30f), "Keybindings", _tab == Tab.Keybindings ? tabSel : tab);

            DrawKeybindings(x, y + 96f, w, h - 96f);

            // Back button.
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(x + w - 150f, y + h - 46f, 126f, 34f), "Back", btn))
            {
                CancelListening();
                onClose?.Invoke();
            }
        }

        void DrawKeybindings(float x, float y, float w, float h)
        {
            var keyLbl = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.86f, 0.9f) } };
            var bindBtn = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };

            var actions = Keybinds.Actions;
            float lx = x + 24f, colW = (w - 48f - 16f) * 0.5f;
            float rowH = 30f, gap = 4f;
            // Two columns so all 15 rows fit.
            int perCol = Mathf.CeilToInt(actions.Length / 2f);

            for (int i = 0; i < actions.Length; i++)
            {
                int col = i / perCol, r = i % perCol;
                float cx = lx + col * (colW + 16f);
                float ry = y + r * (rowH + gap);
                var (action, label) = actions[i];

                GUI.Label(new Rect(cx, ry, colW * 0.55f, rowH), label, keyLbl);

                bool listening = _listening == action;
                bool dup = Keybinds.IsDuplicate(action);
                string caption = listening ? "press a key..." : Keybinds.Display(Keybinds.Path(action));

                var prev = GUI.backgroundColor;
                if (listening) GUI.backgroundColor = new Color(1f, 0.85f, 0.3f);
                else if (dup)  GUI.backgroundColor = new Color(0.9f, 0.4f, 0.35f);

                var bRect = new Rect(cx + colW * 0.55f, ry, colW * 0.45f, rowH);
                if (GUI.Button(bRect, caption, bindBtn) && !IsRebinding)
                    BeginListen(action);
                GUI.backgroundColor = prev;
            }

            var note = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.75f, 0.76f, 0.8f) } };
            GUI.Label(new Rect(x + 24f, y + perCol * (rowH + gap) + 6f, w - 48f, 34f),
                "Click a binding, then press any key or mouse button. Esc cancels. Red = shared with another action.", note);

            var reset = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            if (GUI.Button(new Rect(x + 24f, y + h - 46f, 170f, 34f), "Reset to Defaults", reset) && !IsRebinding)
            {
                Keybinds.ResetDefaults();
                // Re-apply every default onto the live actions.
                foreach (var (action, _) in Keybinds.Actions)
                    _input.ApplyBinding(action, Keybinds.Path(action));
            }
        }

        void BeginListen(string action)
        {
            _listening = action;
            _op = _input.StartRebind(action, path =>
            {
                _listening = null;
                _op = null;
                // Keybinds already saved by StartRebind on success; nothing else to do -
                // the live action was overridden in place.
            });
        }

        void CancelListening()
        {
            if (_op != null) { _op.Cancel(); _op = null; }
            _listening = null;
        }

        // Abort any in-flight rebind (called on teardown so the op is never orphaned).
        public void Dispose() => CancelListening();
    }
}
