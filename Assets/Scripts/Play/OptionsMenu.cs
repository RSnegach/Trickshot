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
        enum Tab { Keybindings, Audio, Quickchat }
        Tab _tab = Tab.Keybindings;

        GameInput _input;
        string _listening;   // action currently awaiting a key press (null = none)
        InputActionRebindingExtensions.RebindingOperation _op;
        int _qcPickingSlot;                 // 0 = not picking; 1-6 = choosing a phrase for that key
        Vector2 _qcScroll;                  // scroll pos of the 25-phrase picker

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

            // Tab strip. Switching tabs cancels any in-flight rebind so it isn't orphaned.
            var tab = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            var tabSel = new GUIStyle(tab); tabSel.normal.textColor = new Color(1f, 0.9f, 0.3f);
            if (GUI.Button(new Rect(x + 24f, y + 54f, 140f, 30f), "Keybindings", _tab == Tab.Keybindings ? tabSel : tab) && _tab != Tab.Keybindings)
                _tab = Tab.Keybindings;
            if (GUI.Button(new Rect(x + 24f + 148f, y + 54f, 140f, 30f), "Audio", _tab == Tab.Audio ? tabSel : tab) && _tab != Tab.Audio)
                { CancelListening(); _tab = Tab.Audio; }
            if (GUI.Button(new Rect(x + 24f + 296f, y + 54f, 140f, 30f), "Quickchat", _tab == Tab.Quickchat ? tabSel : tab) && _tab != Tab.Quickchat)
                { CancelListening(); _qcPickingSlot = 0; _tab = Tab.Quickchat; }

            if      (_tab == Tab.Keybindings) DrawKeybindings(x, y + 96f, w, h - 96f);
            else if (_tab == Tab.Audio)       DrawAudio(x, y + 96f, w, h - 96f);
            else                              DrawQuickchat(x, y + 96f, w, h - 96f);

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

        // Per-player volume sliders. Values live on the AudioManager (persisted to PlayerPrefs),
        // so they are local to this player and not networked. Moving a slider updates the running
        // loops immediately. No-op safe if the AudioManager somehow isn't installed.
        void DrawAudio(float x, float y, float w, float h)
        {
            var am = AudioManager.Instance;
            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.86f, 0.9f) } };
            var pct = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.75f, 0.76f, 0.8f) } };

            if (am == null)
            {
                GUI.Label(new Rect(x + 24f, y, w - 48f, 30f), "Audio system not available.", lbl);
                return;
            }

            var rows = new (string label, AudioManager.Channel ch)[]
            {
                ("Master",       AudioManager.Channel.Master),
                ("Music",        AudioManager.Channel.Music),
                ("Crowd",        AudioManager.Channel.Crowd),
                ("Effects",      AudioManager.Channel.Sfx),
            };

            float lx = x + 24f, rowH = 34f, gap = 18f, sliderW = w - 48f - 210f;
            for (int i = 0; i < rows.Length; i++)
            {
                float ry = y + i * (rowH + gap);
                GUI.Label(new Rect(lx, ry, 120f, rowH), rows[i].label, lbl);

                float cur = am.GetVolume(rows[i].ch);
                float next = GUI.HorizontalSlider(new Rect(lx + 130f, ry + rowH * 0.4f, sliderW, rowH), cur, 0f, 1f);
                if (!Mathf.Approximately(next, cur)) am.SetVolume(rows[i].ch, next);

                GUI.Label(new Rect(lx + 130f + sliderW + 12f, ry, 60f, rowH), Mathf.RoundToInt(next * 100f) + "%", pct);
            }

            var note = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.75f, 0.76f, 0.8f) } };
            GUI.Label(new Rect(lx, y + rows.Length * (rowH + gap) + 6f, w - 48f, 40f),
                "Volumes are per player and saved on this machine.", note);
        }

        // Quickchat assignment (multiplayer): six rows, one per number key 1-6, each showing the
        // phrase currently bound to it. Clicking a row opens a scrollable picker of all 25 phrases;
        // choosing one assigns it to that key (saved per player). Assignments are LOCAL.
        void DrawQuickchat(float x, float y, float w, float h)
        {
            var keyLbl  = new GUIStyle(GUI.skin.label)  { fontSize = 14, alignment = TextAnchor.MiddleLeft,  normal = { textColor = new Color(0.85f, 0.86f, 0.9f) } };
            var rowBtn  = new GUIStyle(GUI.skin.button) { fontSize = 13, alignment = TextAnchor.MiddleLeft };

            float lx = x + 24f, rowH = 30f, gap = 6f;
            float labelW = 70f, btnW = w - 48f - labelW - 8f;

            for (int key = 1; key <= 6; key++)
            {
                float ry = y + (key - 1) * (rowH + gap);
                GUI.Label(new Rect(lx, ry, labelW, rowH), "Key " + key, keyLbl);
                string cur = QuickChat.PhraseForKey(key);
                var prevBg = GUI.backgroundColor;
                if (_qcPickingSlot == key) GUI.backgroundColor = new Color(1f, 0.85f, 0.3f);   // gold while picking
                if (GUI.Button(new Rect(lx + labelW + 8f, ry, btnW, rowH), "  " + cur, rowBtn))
                    _qcPickingSlot = (_qcPickingSlot == key) ? 0 : key;
                GUI.backgroundColor = prevBg;
            }

            // Picker: scrollable grid of all 25 phrases, shown when a key row is active.
            if (_qcPickingSlot >= 1 && _qcPickingSlot <= 6)
            {
                float py = y + 6f * (rowH + gap) + 8f;
                float ph = h - (py - y) - 52f;
                var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(1f, 0.9f, 0.4f) } };
                GUI.Label(new Rect(lx, py, w - 48f, 18f), "Pick a phrase for Key " + _qcPickingSlot + ":", hint);
                py += 20f; ph -= 20f;

                var phrases = QuickChat.Phrases;
                float cellW = (w - 48f - 16f) * 0.5f, cellH = 26f, cgap = 4f;
                int cols = 2;
                int rows = Mathf.CeilToInt(phrases.Length / (float)cols);
                var view = new Rect(lx, py, w - 48f, ph);
                var content = new Rect(0, 0, cellW * cols + cgap, rows * (cellH + cgap));
                _qcScroll = GUI.BeginScrollView(view, _qcScroll, content);
                int curIdx = QuickChat.PhraseIndexForKey(_qcPickingSlot);
                var cell = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
                for (int i = 0; i < phrases.Length; i++)
                {
                    int c = i % cols, r = i / cols;
                    var cr = new Rect(c * (cellW + cgap), r * (cellH + cgap), cellW, cellH);
                    var prevBg = GUI.backgroundColor;
                    if (i == curIdx) GUI.backgroundColor = new Color(0.2f, 0.5f, 0.9f);   // highlight current
                    if (GUI.Button(cr, "  " + phrases[i], cell))
                    {
                        QuickChat.SetSlot(_qcPickingSlot, i);
                        _qcPickingSlot = 0;
                        GUI.backgroundColor = prevBg;
                        break;
                    }
                    GUI.backgroundColor = prevBg;
                }
                GUI.EndScrollView();
            }
            else
            {
                var note = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.75f, 0.76f, 0.8f) } };
                GUI.Label(new Rect(lx, y + 6f * (rowH + gap) + 10f, w - 48f, 40f),
                    "In multiplayer, press 1-6 to send the assigned quickchat, or Tab to type a custom one.", note);
            }

            var reset = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            if (GUI.Button(new Rect(lx, y + h - 46f, 170f, 34f), "Reset to Defaults", reset))
            { QuickChat.ResetDefaults(); _qcPickingSlot = 0; }
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
