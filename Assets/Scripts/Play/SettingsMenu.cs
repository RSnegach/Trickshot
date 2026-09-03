using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// Settings overlay opened from the pause menu and the main-menu hub. Five tabs:
    ///   KEYBINDINGS - one row per action showing its current bind, click to rebind, duplicates
    ///                 highlighted, plus Reset to Defaults.
    ///   AUDIO       - per-channel volume sliders.
    ///   QUICKCHAT   - assign a phrase to each number key.
    ///   CAMERA      - resolution, window mode, vsync, UI scale, and camera field of view.
    ///   CREDITS     - rolling, looping credits: the creator, then every third-party asset
    ///                 (the text itself lives in CreditsData).
    ///
    /// Drawn by PauseMenu / MenuUI while Settings is open, inside their MenuScale block, so lay
    /// out against MenuScale.Width/Height rather than Screen.*.
    /// </summary>
    public class SettingsMenu
    {
        enum Tab { Keybindings, Audio, Quickchat, Camera, Credits }
        Tab _tab = Tab.Keybindings;

        GameInput _input;
        string _listening;   // action currently awaiting a key press (null = none)
        InputActionRebindingExtensions.RebindingOperation _op;
        int _qcPickingSlot;                 // 0 = not picking; 1-6 = choosing a phrase for that key
        Vector2 _qcScroll;                  // scroll pos of the 25-phrase picker
        Vector2 _resScroll;                 // scroll pos of the resolution list
        bool _gfxOpen;                      // the Graphics tier dropdown is unfolded

        // Credits roll (see DrawCredits). The offset is how far the list has scrolled up, in
        // virtual px, wrapped modulo the list's height so it loops; the hold is the time left
        // before auto-scroll resumes after the player scrubbed it. Entry heights are measured
        // once, because the long asset lines word-wrap.
        float _creditsOffset, _creditsHold, _creditsDragY, _creditsTotal;
        bool _creditsDrag;
        float[] _creditsHeights;
        const float CreditsSpeed = 28f;   // px/s: about a line a second, a film's end crawl
        const float CreditsHold = 2f;     // seconds the roll waits after a wheel or a drag

        public bool IsRebinding => _listening != null;

        public SettingsMenu(GameInput input) { _input = input; }

        // Returns true while open; PauseMenu calls Draw and closes when it returns false.
        // `onClose` fires when the user backs out.
        public void Draw(System.Action onClose)
        {
            float w = 660f, h = 500f;
            float x = MenuScale.Width * 0.5f - w * 0.5f, y = MenuScale.Height * 0.5f - h * 0.5f;

            // Dark rounded card with a gold accent rule and a drop shadow (see UITheme).
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, w + 260f);
            UITheme.Panel(new Rect(x, y, w, h), UITheme.Gold);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Ink } };
            UITheme.Shadowed(new Rect(x + 24f, y + 14f, w - 48f, 34f), "SETTINGS", title, UITheme.Ink, 0.7f, 2f);

            // Tab strip. Switching tabs cancels any in-flight rebind so it isn't orphaned.
            var tab = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            var tabSel = new GUIStyle(tab); tabSel.normal.textColor = UITheme.Gold;
            // Five tabs share the 612 px content width: 116 each on a 122 stride.
            const float tabW = 116f, tabStep = 122f;
            if (TabBtn(new Rect(x + 24f, y + 54f, tabW, 30f), "Keybindings", _tab == Tab.Keybindings, tab, tabSel) && _tab != Tab.Keybindings)
                _tab = Tab.Keybindings;
            if (TabBtn(new Rect(x + 24f + tabStep, y + 54f, tabW, 30f), "Audio", _tab == Tab.Audio, tab, tabSel) && _tab != Tab.Audio)
                { CancelListening(); _tab = Tab.Audio; }
            if (TabBtn(new Rect(x + 24f + tabStep * 2f, y + 54f, tabW, 30f), "Quickchat", _tab == Tab.Quickchat, tab, tabSel) && _tab != Tab.Quickchat)
                { CancelListening(); _qcPickingSlot = 0; _tab = Tab.Quickchat; }
            if (TabBtn(new Rect(x + 24f + tabStep * 3f, y + 54f, tabW, 30f), "Camera", _tab == Tab.Camera, tab, tabSel) && _tab != Tab.Camera)
                { CancelListening(); _qcPickingSlot = 0; _tab = Tab.Camera; }
            if (TabBtn(new Rect(x + 24f + tabStep * 4f, y + 54f, tabW, 30f), "Credits", _tab == Tab.Credits, tab, tabSel) && _tab != Tab.Credits)
                { CancelListening(); _qcPickingSlot = 0; _tab = Tab.Credits; _creditsOffset = 0f; _creditsHold = 0f; _creditsDrag = false; }

            // Hairline under the strip so the tabs read as attached to the content below.
            UITheme.Divider(x + 24f, y + 88f, w - 48f);

            if      (_tab == Tab.Keybindings) DrawKeybindings(x, y + 96f, w, h - 96f);
            else if (_tab == Tab.Audio)       DrawAudio(x, y + 96f, w, h - 96f);
            else if (_tab == Tab.Quickchat)   DrawQuickchat(x, y + 96f, w, h - 96f);
            else if (_tab == Tab.Camera)      DrawCamera(x, y + 96f, w, h - 96f);
            else                              DrawCredits(x, y + 96f, w, h - 96f);

            // Sliders write their value every frame while dragged, so the pref store is only
            // flushed to disk once the drag ends.
            if (Event.current.type == EventType.MouseUp) DisplaySettings.Flush();

            // Back button.
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(x + w - 150f, y + h - 46f, 126f, 34f), "Back", btn))
            {
                CancelListening();
                onClose?.Invoke();
            }
        }

        // Tab button with the active one lit. Tints have to exceed
        // 1.0: GUI.backgroundColor MULTIPLIES the plate, so a saturated colour would only darken it.
        // (No gold underline under the active tab - the lit plate alone marks it.)
        static bool TabBtn(Rect r, string label, bool on, GUIStyle normal, GUIStyle selected)
        {
            var keep = GUI.backgroundColor;
            if (on) GUI.backgroundColor = UITheme.SelTint;
            bool hit = UITheme.Button(r, label, on ? selected : normal);
            GUI.backgroundColor = keep;
            return hit;
        }

        void DrawKeybindings(float x, float y, float w, float h)
        {
            var keyLbl = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            var bindBtn = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };

            // Adult-only binds are listed only while adult mode is on (the bind still exists and
            // still works underneath; it just has nothing to act on without the appendage).
            bool adult = PlayerProfile.Appearance.Adult;
            var actions = System.Array.FindAll(Keybinds.Actions, a => adult || !Keybinds.AdultOnly(a.action));
            float lx = x + 24f, colW = (w - 48f - 16f) * 0.5f;
            float rowH = 30f, gap = 4f;
            // Two columns, split evenly however many bindings exist.
            int perCol = Mathf.CeilToInt(actions.Length / 2f);

            for (int i = 0; i < actions.Length; i++)
            {
                int col = i / perCol, r = i % perCol;
                float cx = lx + col * (colW + 16f);
                float ry = y + r * (rowH + gap);
                var (action, label) = actions[i];

                UITheme.Label(new Rect(cx, ry, colW * 0.55f, rowH), label, keyLbl);

                bool listening = _listening == action;
                bool dup = Keybinds.IsDuplicate(action);
                string caption = listening ? "press a key..." : Keybinds.Display(Keybinds.Path(action));

                var prev = GUI.backgroundColor;
                if (listening) GUI.backgroundColor = UITheme.WarnTint;
                else if (dup)  GUI.backgroundColor = UITheme.BadTint;

                var bRect = new Rect(cx + colW * 0.55f, ry, colW * 0.45f, rowH);
                if (UITheme.Button(bRect, caption, bindBtn) && !IsRebinding)
                    BeginListen(action);
                GUI.backgroundColor = prev;
            }

            var note = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = UITheme.Faint } };
            UITheme.Label(new Rect(x + 24f, y + perCol * (rowH + gap) + 6f, w - 48f, 34f),
                "Click a binding, then press a key. Esc cancels. Red = shared.", note);

            var reset = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            if (UITheme.Button(new Rect(x + 24f, y + h - 46f, 170f, 34f), "Reset to Defaults", reset) && !IsRebinding)
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
            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            var pct = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Gold } };

            if (am == null)
            {
                UITheme.Label(new Rect(x + 24f, y, w - 48f, 30f), "Audio system not available.", lbl);
                return;
            }

            var rows = new (string label, AudioManager.Channel ch)[]
            {
                ("Master",       AudioManager.Channel.Master),
                ("Music",        AudioManager.Channel.Music),
                ("Crowd",        AudioManager.Channel.Crowd),
                ("Effects",      AudioManager.Channel.Sfx),
            };

            UITheme.Section(new Rect(x + 24f, y - 22f, w - 48f, 20f), "VOLUME");

            float lx = x + 24f, rowH = 34f, gap = 18f, sliderW = w - 48f - 210f;
            for (int i = 0; i < rows.Length; i++)
            {
                float ry = y + i * (rowH + gap);
                UITheme.Label(new Rect(lx, ry, 120f, rowH), rows[i].label, lbl);

                float cur = am.GetVolume(rows[i].ch);
                float next = GUI.HorizontalSlider(new Rect(lx + 130f, ry + rowH * 0.4f, sliderW, rowH), cur, 0f, 1f);
                if (!Mathf.Approximately(next, cur)) am.SetVolume(rows[i].ch, next);

                UITheme.Label(new Rect(lx + 130f + sliderW + 12f, ry, 60f, rowH), Mathf.RoundToInt(next * 100f) + "%", pct);
                UITheme.Divider(lx, ry + rowH + 6f, w - 48f);
            }

            var note = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = UITheme.Faint } };
            UITheme.Label(new Rect(lx, y + rows.Length * (rowH + gap) + 6f, w - 48f, 40f),
                "Per player, saved on this machine.", note);
        }

        // Quickchat assignment (multiplayer): six rows, one per number key 1-6, each showing the
        // phrase currently bound to it. Clicking a row opens a scrollable picker of all 25 phrases;
        // choosing one assigns it to that key (saved per player). Assignments are LOCAL.
        void DrawQuickchat(float x, float y, float w, float h)
        {
            var keyLbl  = new GUIStyle(GUI.skin.label)  { fontSize = 14, alignment = TextAnchor.MiddleLeft,  normal = { textColor = UITheme.Dim } };
            var rowBtn  = new GUIStyle(GUI.skin.button) { fontSize = 13, alignment = TextAnchor.MiddleLeft };

            float lx = x + 24f, rowH = 30f, gap = 6f;
            float labelW = 70f, btnW = w - 48f - labelW - 8f;

            for (int key = 1; key <= 6; key++)
            {
                float ry = y + (key - 1) * (rowH + gap);
                UITheme.Label(new Rect(lx, ry, labelW, rowH), "Key " + key, keyLbl);
                string cur = QuickChat.PhraseForKey(key);
                var prevBg = GUI.backgroundColor;
                if (_qcPickingSlot == key) GUI.backgroundColor = UITheme.WarnTint;   // gold while picking
                if (UITheme.Button(new Rect(lx + labelW + 8f, ry, btnW, rowH), "  " + cur, rowBtn))
                    _qcPickingSlot = (_qcPickingSlot == key) ? 0 : key;
                GUI.backgroundColor = prevBg;
            }

            // Picker: scrollable grid of all 25 phrases, shown when a key row is active.
            if (_qcPickingSlot >= 1 && _qcPickingSlot <= 6)
            {
                float py = y + 6f * (rowH + gap) + 8f;
                float ph = h - (py - y) - 52f;
                var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = UITheme.Gold } };
                UITheme.Label(new Rect(lx, py, w - 48f, 18f), "Pick a phrase for Key " + _qcPickingSlot + ":", hint);
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
                    if (i == curIdx) GUI.backgroundColor = UITheme.SelTint;   // highlight current
                    if (UITheme.Button(cr, "  " + phrases[i], cell))
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
                var note = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = UITheme.Faint } };
                UITheme.Label(new Rect(lx, y + 6f * (rowH + gap) + 10f, w - 48f, 40f),
                    "Press 1-6 to quickchat. Tab to type.", note);
            }

            var reset = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            if (UITheme.Button(new Rect(lx, y + h - 46f, 170f, 34f), "Reset to Defaults", reset))
            { QuickChat.ResetDefaults(); _qcPickingSlot = 0; }
        }

        // Display + camera options. This tab is what makes the game fit the player's monitor:
        // resolution and window mode, plus the UI Scale multiplier feeding MenuScale, which decides
        // whether the menus and the bottom control banner fit on-screen. All local, all persisted
        // by DisplaySettings.
        void DrawCamera(float x, float y, float w, float h)
        {
            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            var val = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Gold } };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            var sel = UITheme.SelTint;

            float lx = x + 24f, cw = w - 48f;
            float fx = lx + 130f, fw = cw - 130f;   // value column

            UITheme.Section(new Rect(lx, y, cw, 20f), "DISPLAY");

            // ---- resolution list (deduped by size, current one highlighted) ----
            UITheme.Label(new Rect(lx, y + 24f, 120f, 24f), "Resolution", lbl);
            var list = DisplaySettings.Available;
            float cellW = (fw - 8f) * 0.5f, cellH = 24f, cgap = 4f;
            const int cols = 2;
            int rows = Mathf.CeilToInt(list.Length / (float)cols);
            var view = new Rect(fx, y + 24f, fw, 100f);
            var content = new Rect(0, 0, cellW * cols + cgap, rows * (cellH + cgap));
            var cell = new GUIStyle(GUI.skin.button) { fontSize = 12 };
            int pick = -1;
            _resScroll = GUI.BeginScrollView(view, _resScroll, content);
            for (int i = 0; i < list.Length; i++)
            {
                var cr = new Rect((i % cols) * (cellW + cgap), (i / cols) * (cellH + cgap), cellW, cellH);
                bool cur = list[i].width == Screen.width && list[i].height == Screen.height;
                var prevBg = GUI.backgroundColor;
                if (cur) GUI.backgroundColor = sel;
                if (UITheme.Button(cr, list[i].width + " x " + list[i].height, cell)) pick = i;
                GUI.backgroundColor = prevBg;
            }
            GUI.EndScrollView();
            // Applied AFTER the scroll view closes: the switch resizes the window, and IMGUI does
            // not want the layout changing between BeginScrollView and EndScrollView.
            if (pick >= 0) DisplaySettings.Apply(list[pick].width, list[pick].height, Screen.fullScreenMode);

            // ---- window mode ----
            float y2 = y + 134f;
            UITheme.Label(new Rect(lx, y2, 120f, 28f), "Window", lbl);
            var modes = DisplaySettings.Modes;
            float mw = (fw - 12f) / 3f;
            for (int i = 0; i < modes.Length; i++)
            {
                bool on = Screen.fullScreenMode == modes[i];
                var prevBg = GUI.backgroundColor;
                if (on) GUI.backgroundColor = sel;
                if (UITheme.Button(new Rect(fx + i * (mw + 6f), y2, mw, 28f), DisplaySettings.ModeLabel(modes[i]), btn) && !on)
                    DisplaySettings.ApplyMode(modes[i]);
                GUI.backgroundColor = prevBg;
            }

            // ---- vsync ----
            float y3 = y2 + 34f;
            UITheme.Label(new Rect(lx, y3, 120f, 28f), "VSync", lbl);
            if (UITheme.Button(new Rect(fx, y3, 110f, 28f), DisplaySettings.VSync ? "On" : "Off", btn))
                DisplaySettings.VSync = !DisplaySettings.VSync;

            // ---- graphics tier (dropdown) ----
            float y3b = y3 + 34f;
            UITheme.Label(new Rect(lx, y3b, 120f, 28f), "Graphics", lbl);
            var gfxBtn = new Rect(fx, y3b, 200f, 28f);
            var tiers = DisplaySettings.TierNames;
            if (UITheme.Button(gfxBtn, tiers[(int)DisplaySettings.Graphics] + (_gfxOpen ? "  ▴" : "  ▾"), btn))
                _gfxOpen = !_gfxOpen;
            if (_gfxOpen)
            {
                // The unfolded list is drawn HERE, before the rows it would cover, because IMGUI
                // hands a click to the first control drawn under it - and those rows are simply
                // not drawn while it is open. Pick a tier, or click anywhere else, to fold it.
                const float rowH = 28f;
                var menu = new Rect(gfxBtn.x, gfxBtn.yMax + 2f, gfxBtn.width, rowH * tiers.Length + 8f);
                UITheme.Panel(menu, UITheme.Gold);
                for (int i = 0; i < tiers.Length; i++)
                {
                    bool on = i == (int)DisplaySettings.Graphics;
                    var prevBg = GUI.backgroundColor;
                    if (on) GUI.backgroundColor = sel;
                    if (UITheme.Button(new Rect(menu.x + 4f, menu.y + 4f + i * rowH, menu.width - 8f, rowH - 2f), tiers[i], btn))
                    { DisplaySettings.Graphics = (DisplaySettings.GraphicsTier)i; _gfxOpen = false; }
                    GUI.backgroundColor = prevBg;
                }
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && !menu.Contains(ev.mousePosition) && !gfxBtn.Contains(ev.mousePosition))
                    _gfxOpen = false;
                var tip = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = UITheme.Faint } };
                UITheme.Label(new Rect(lx, menu.yMax + 8f, cw, 40f),
                    "Your own frame rate only. In multiplayer, the host's setting is the one that affects everyone.", tip);
                return;
            }

            // ---- UI scale (multiplies the automatic fit) ----
            float y4 = y3b + 34f;
            UITheme.Label(new Rect(lx, y4, 120f, 28f), "UI Scale", lbl);
            float uiCur = DisplaySettings.UiScale;
            float uiNext = GUI.HorizontalSlider(new Rect(fx, y4 + 11f, fw - 74f, 20f), uiCur,
                                                MenuScale.MinUserScale, MenuScale.MaxUserScale);
            if (!Mathf.Approximately(uiNext, uiCur)) DisplaySettings.UiScale = uiNext;
            UITheme.Label(new Rect(fx + fw - 66f, y4, 66f, 28f), Mathf.RoundToInt(uiNext * 100f) + "%", val);

            // ---- camera ----
            float y5 = y4 + 34f;
            UITheme.Section(new Rect(lx, y5, cw, 20f), "CAMERA");
            float y6 = y5 + 24f;
            UITheme.Label(new Rect(lx, y6, 120f, 28f), "Field of View", lbl);
            float fovCur = DisplaySettings.FovOffset;
            float fovNext = GUI.HorizontalSlider(new Rect(fx, y6 + 11f, fw - 74f, 20f), fovCur,
                                                 DisplaySettings.MinFov, DisplaySettings.MaxFov);
            if (!Mathf.Approximately(fovNext, fovCur)) DisplaySettings.FovOffset = fovNext;
            int fovShown = Mathf.RoundToInt(fovNext);
            UITheme.Label(new Rect(fx + fw - 66f, y6, 66f, 28f),
                      (fovShown > 0 ? "+" : "") + fovShown, val);

            // ---- gameplay: post-goal replays (drill + set-piece modes; a match never rolls one) ----
            float y7 = y6 + 34f;
            UITheme.Section(new Rect(lx, y7, cw, 20f), "GAMEPLAY");
            float y8 = y7 + 24f;
            UITheme.Label(new Rect(lx, y8, 120f, 28f), "Replays", lbl);
            if (UITheme.Button(new Rect(fx, y8, 110f, 28f), GameplaySettings.Replays ? "On" : "Off", btn))
                GameplaySettings.Replays = !GameplaySettings.Replays;

            var note = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = UITheme.Faint } };
            UITheme.Label(new Rect(lx, y8 + 32f, cw, 18f),
                "Saved on this machine. Replays are the post-goal slow-mo outside matches; online, the host's setting decides.", note);
        }

        // Rolling credits. CreditsData's list scrolls up on its own like a film's end crawl and
        // wraps seamlessly: copies are stacked one under the other, so the head of the list
        // follows its own tail with no blank stretch. The wheel or a drag scrubs it, and either
        // holds the roll for a couple of seconds so the player can read what they stopped on.
        // unscaledDeltaTime, because the pause menu freezes timeScale and this must still roll.
        //
        // No GUI.BeginGroup: UITheme's crisp-text pass drops GUI.matrix to identity for every
        // label it draws, and how that composes with a clip group pushed under the scaled matrix
        // is not something to lean on blind. Lines are culled to the window instead, and faded
        // over a short band at either edge, so nothing ever draws past the window and the ends
        // read as a soft vignette rather than a hard cut.
        void DrawCredits(float x, float y, float w, float h)
        {
            var win = new Rect(x + 24f, y + 2f, w - 48f, h - 58f);   // stops short of the Back row
            var entries = CreditsData.Entries;

            var heading = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = UITheme.Ink } };
            var sub     = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = UITheme.Gold } };
            var strong  = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = UITheme.Ink } };
            var line    = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = UITheme.Dim } };

            // Heights are measured once. The wrap is measured a little narrower than the draw, so
            // the crisp pass (which re-rasterizes at a rounded native font size, and can wrap a
            // hair differently) only ever has more room than the measure assumed, never less.
            if (_creditsHeights == null || _creditsHeights.Length != entries.Length)
            {
                _creditsHeights = new float[entries.Length];
                _creditsTotal = 0f;
                for (int i = 0; i < entries.Length; i++)
                {
                    float eh = EntryHeight(entries[i], win.width - 16f, line);
                    _creditsHeights[i] = eh;
                    _creditsTotal += eh;
                }
            }
            if (_creditsTotal <= 0f) return;

            // Scrubbing. The drag tracks absolute mouse Y rather than Event.delta: mousePosition
            // is already in virtual coordinates under MenuScale, delta is not promised to be.
            var e = Event.current;
            bool over = win.Contains(e.mousePosition);
            if (e.type == EventType.ScrollWheel && over)
            {
                _creditsOffset += e.delta.y * 10f;   // a notch is ~3 units: ~30 px, about a line
                _creditsHold = CreditsHold;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && over)
            {
                _creditsDrag = true;
                _creditsDragY = e.mousePosition.y;
                _creditsHold = CreditsHold;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _creditsDrag)
            {
                _creditsOffset -= e.mousePosition.y - _creditsDragY;   // drag down = pull the list back down
                _creditsDragY = e.mousePosition.y;
                _creditsHold = CreditsHold;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _creditsDrag)
            {
                _creditsDrag = false;   // not Used: Draw's MouseUp check still flushes DisplaySettings
            }

            // Advance on Repaint only - OnGUI runs once per event, and every pass calls Draw.
            if (e.type == EventType.Repaint)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);   // a hitch must not skip a screenful
                if (_creditsHold > 0f) _creditsHold -= dt;
                else if (!_creditsDrag) _creditsOffset += CreditsSpeed * dt;
            }
            _creditsOffset = Mathf.Repeat(_creditsOffset, _creditsTotal);

            // Enough copies to cover the window whatever the list's length; two, in practice.
            const float fade = 36f;
            int copies = Mathf.CeilToInt(win.height / _creditsTotal) + 1;
            var keepCol = GUI.color;
            for (int c = 0; c < copies; c++)
            {
                float ly = win.y - _creditsOffset + c * _creditsTotal;
                for (int i = 0; i < entries.Length; i++)
                {
                    float top = ly, lh = _creditsHeights[i];
                    ly += lh;
                    var en = entries[i];
                    if (en.Kind == CreditsData.Kind.Gap) continue;
                    if (top < win.y || top + lh > win.yMax) continue;   // culled: never past the window
                    float a = Mathf.Clamp01(Mathf.Min((top - win.y) / fade, (win.yMax - top - lh) / fade));
                    if (a <= 0.01f) continue;
                    GUI.color = new Color(1f, 1f, 1f, a);   // multiplies the text (and the shadow)

                    var r = new Rect(win.x, top, win.width, lh);
                    switch (en.Kind)
                    {
                        case CreditsData.Kind.Heading: UITheme.Shadowed(r, en.Text, heading, UITheme.Ink, 0.7f, 2f); break;
                        case CreditsData.Kind.Sub:     UITheme.Label(r, en.Text, sub); break;
                        case CreditsData.Kind.Strong:  UITheme.Label(r, en.Text, strong); break;
                        default:                       UITheme.Label(r, en.Text, line); break;
                    }
                }
            }
            GUI.color = keepCol;

            UITheme.Hint(new Rect(x + 24f, y + h - 46f, w - 48f - 150f, 34f), "Scroll or drag to browse", TextAnchor.MiddleLeft);
        }

        // Height of one credits entry in virtual px. Only Line entries wrap; the rest are one line
        // of a known size, and Gap is whatever the data says.
        static float EntryHeight(CreditsData.Entry en, float wrapW, GUIStyle line)
        {
            switch (en.Kind)
            {
                case CreditsData.Kind.Heading: return 46f;
                case CreditsData.Kind.Sub:     return 26f;
                case CreditsData.Kind.Strong:  return 30f;
                case CreditsData.Kind.Gap:     return en.Size;
                default:                       return line.CalcHeight(new GUIContent(en.Text), wrapW) + 4f;
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
