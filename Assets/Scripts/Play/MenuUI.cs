using UnityEngine;

namespace Trickshot
{
    // Core roles plus the single-player modes and the full match.
    // SetPieces = networked free-kick shootout (also playable solo via the FreeKick build).
    // NOTE: append new values at the END - MatchConfig sends GameMode as a byte over the wire.
    // Freeplay and TimeTrial were removed outright (not just reordered) - pre-release, no
    // external client needs the old byte values to stay stable, so every value after them
    // shifted down rather than leaving a permanent gap.
    // TrickshotCup = the knockout cup (Solo / Head to Head / Co-op are a STYLE inside it, carried
    // in MatchConfig.cupStyle; Penalties / Free Kicks a FORMAT in cupFormat - neither is a mode).
    public enum GameMode { Striker, Goalkeeper, Accuracy, FreeKick, Match, SetPieces, TrickshotCup }

    /// <summary>
    /// IMGUI start menu, two screens: a title SPLASH (the wordmark + "press any key"), then a
    /// FIFA-style HUB of mode cards (Single Player / Multiplayer / Career Stats / Zoo / Settings).
    /// Single Player opens a consolidated flat list of every solo mode (Striker/Goalkeeper plus
    /// what used to be the separate "Mode" submenu). Kept as IMGUI so it needs no Canvas/
    /// EventSystem wiring (consistent with the rest of the runtime build).
    ///
    /// Career Stats is now real (see CareerStats.cs, this project's first save file) and opens
    /// CareerStatsUI. Zoo is still a "Coming Soon" placeholder - there is no character-creation
    /// system to back it yet, and an honest placeholder beats fabricating one.
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        System.Action<GameMode> _onChoose;
        System.Action _onMultiplayer;
        bool _chosen;

        enum Phase { Splash, Hub, SinglePlayer, CareerStats, Zoo }
        Phase _phase = Phase.Splash;
        int _splashStartFrame;   // guards Input.anyKeyDown against a false-positive on frame one

        // Settings overlay (Keybindings + Audio), same panel the pause menu uses. Built lazily from
        // the passed GameInput; null if none was supplied (then no Settings button is shown).
        SettingsMenu _settings;
        bool _settingsOpen;
        CareerStatsUI _careerStats;

        // Small Friends/Achievements chips, bottom-right of the Hub only (see DrawHub). Mutually
        // exclusive flyouts - opening one closes the other.
        bool _showFriends, _showAchievements;

        public void Init(System.Action<GameMode> onChoose, System.Action onMultiplayer = null,
                         GameInput input = null, bool skipSplash = false)
        {
            _onChoose = onChoose;
            _onMultiplayer = onMultiplayer;
            if (input != null) _settings = new SettingsMenu(input);
            GameInput.CaptureCursor(false);
            _splashStartFrame = Time.frameCount;
            if (skipSplash) _phase = Phase.Hub;
        }

        // Legacy Input.anyKeyDown is deliberate here, not GameInput/IStrikerInput: that abstraction
        // is a gameplay action map (Jump, LegL, LegR, ...) with no "any key at all" concept, and is
        // itself optional (null when no Settings should show) - the wrong coupling for a one-shot,
        // non-gameplay splash dismiss. activeInputHandler is "Both" in this project (confirmed in
        // ProjectSettings.asset), so legacy Input isn't disabled, just unused by gameplay code.
        void Update()
        {
            if (_phase == Phase.Splash && Time.frameCount > _splashStartFrame && Input.anyKeyDown)
                _phase = Phase.Hub;
        }

        void OnGUI()
        {
            if (_chosen) return;

            // Fit to the window (see MenuScale); virtual coordinates from here on.
            MenuScale.Begin();

            // Settings overlay takes over the whole menu while open (same panel as the pause menu).
            // Only ever reachable from Phase.Hub (the Settings button in the top-right corner).
            if (_settingsOpen && _settings != null)
            {
                // The overlay draws INSTEAD of the phase below, so a Single Player grid would keep
                // a scene live (and the menu's slow-mo applied) behind a panel nobody can see. It
                // is rebuilt when the overlay closes.
                CloseSoloGrid();
                _settings.Draw(() => _settingsOpen = false);
                MenuScale.End();
                return;
            }

            switch (_phase)
            {
                case Phase.Splash: DrawSplash(); break;
                case Phase.Hub: DrawHub(); break;
                case Phase.SinglePlayer: DrawSinglePlayer(); break;
                case Phase.CareerStats:
                    _careerStats ??= new CareerStatsUI();
                    _careerStats.Draw(() => _phase = Phase.Hub);
                    break;
                case Phase.Zoo: DrawComingSoon("ZOO"); break;
            }

            MenuScale.End();
        }

        // Just the wordmark + prompt, over the orbiting background - no buttons, nothing else
        // competing for attention. A lighter scrim than the Hub's: there's far less text to read
        // over the backdrop here, and the point is to show the backdrop off.
        void DrawSplash()
        {
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.18f, 900f, 0.20f, 0f);
            UITheme.TitleWithKickK(new Rect(0, 130f, MenuScale.Width, 200f), 132);
            UITheme.PulseHint(new Rect(0, 446f, MenuScale.Width, 40f), "PRESS ANY KEY TO CONTINUE");
        }

        // The FIFA-style hub: straight to a row of mode cards, no wordmark - the splash already
        // showed the logo once, so repeating it here just ate space the cards can use instead.
        // Card count adapts if Settings has nothing to open into (no GameInput supplied) - same
        // "hasSettings" guard the old single-column button list used, just sizing a row instead of
        // a column. Card size and spacing are fractions of the real canvas (MenuScale.Width/
        // Height), not fixed pixel values - that canvas is NOT a constant 1280x760, it scales with
        // the actual window and the UI Scale setting (measured live elsewhere in this project at
        // 1361.5x649.8 in one ordinary window), so a fixed-size block under-fills a larger canvas
        // and overflows a smaller one.
        void DrawHub()
        {
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.26f, 720f, 0.30f, 0f);

            bool hasSettings = _settings != null;

            float marginX = MenuScale.Width * 0.05f;

            // A Friends/Achievements flyout is MODAL over this screen. Its rect is worked out
            // before anything else is drawn, because the cards below have to be disabled for the
            // pass in which it is open: IMGUI hands a click to the first control that claims it,
            // and the flyout is drawn LAST, so an enabled card under it would take the press first
            // - which is what let an "x" in a panel corner also trigger the card behind it.
            Rect flyout = FlyoutRect(marginX, out bool flyoutOpen);
            bool wasEnabled = GUI.enabled;
            if (flyoutOpen) GUI.enabled = false;

            // 110 wide: "SETTINGS" clips at the old 90 in 12 pt bold, and the origin moves by the
            // same 20 so the button stays flush to the right margin.
            var settingsBtn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, stretchWidth = false };
            if (hasSettings && UITheme.Button(new Rect(MenuScale.Width - marginX - 110f, 26f, 110f, 34f), "SETTINGS", settingsBtn))
                _settingsOpen = true;

            int cardCount = 3;   // Single Player, Multiplayer, Career Stats
            float cardGap = MenuScale.Width * 0.028f;
            float cardWidth = (MenuScale.Width - marginX * 2f - cardGap * (cardCount - 1)) / cardCount;
            float cardHeight = MenuScale.Height * 0.58f;
            float cardY = MenuScale.Height * 0.5f - cardHeight * 0.5f;
            float cardX = marginX;
            int ci = 0;

            if (UITheme.ModeCard(new Rect(cardX + (cardWidth + cardGap) * ci++, cardY, cardWidth, cardHeight), MenuIcons.Get("single"),
                "Single Player", "Striker, goalkeeper, and every challenge mode"))
                _phase = Phase.SinglePlayer;

            if (UITheme.ModeCard(new Rect(cardX + (cardWidth + cardGap) * ci++, cardY, cardWidth, cardHeight), MenuIcons.Get("multiplayer"),
                "Multiplayer", "Play online with friends"))
            {
                _chosen = true; enabled = false; _onMultiplayer?.Invoke();
            }

            if (UITheme.ModeCard(new Rect(cardX + (cardWidth + cardGap) * ci++, cardY, cardWidth, cardHeight), MenuIcons.Get("career"),
                "Career Stats", "Track your progress over time"))
                _phase = Phase.CareerStats;

            GUI.enabled = wasEnabled;

            DrawCornerTabs(marginX, flyout);
        }

        // Small Friends/Achievements chips in the bottom-right corner - the only place on this
        // screen they belong, per the request ("the first screen after clicking any button to
        // continue"). Flyouts open UPWARD from the chip so they never run off the bottom edge.
        const float ChipW = 150f, ChipH = 40f, ChipGap = 10f;
        const float PanelW = 320f, PanelH = 260f;

        /// <summary>
        /// Where the open flyout sits, and whether one is open at all. Split out of DrawCornerTabs
        /// because DrawHub needs the rect BEFORE it draws the cards the flyout covers (see there).
        /// Returns an empty rect when nothing is open - which is also the right "hole" to hand
        /// UITheme.ClickBlocker, since it is then never called.
        /// </summary>
        Rect FlyoutRect(float marginX, out bool open)
        {
            open = _showFriends || _showAchievements;
            if (!open) return new Rect();

            float chipY = MenuScale.Height - 24f - ChipH;
            float achX = MenuScale.Width - marginX - ChipW;
            float frX = achX - ChipGap - ChipW;
            float x = _showFriends ? frX : achX + ChipW - PanelW;
            return new Rect(x, chipY - PanelH - 10f, PanelW, PanelH);
        }

        void DrawCornerTabs(float marginX, Rect flyout)
        {
            float chipY = MenuScale.Height - 24f - ChipH;
            float achX = MenuScale.Width - marginX - ChipW;
            float frX = achX - ChipGap - ChipW;

            var chipBtn = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };

            // Eat every click that misses the open panel, so nothing behind it responds and a
            // click-away closes it. The two live regions are the panel itself and the chip strip:
            // both are drawn AFTER this, and the chips have to stay clickable because a second
            // press on the lit chip is how the panel closes.
            //
            // Called UNCONDITIONALLY, with a full-screen hole when nothing is open: a control that
            // comes and goes between the layout and repaint passes shifts every control id after
            // it, and the chips and the panel are both after it.
            var chipStrip = new Rect(frX, chipY, ChipW * 2f + ChipGap, ChipH);
            var whole = new Rect(0f, 0f, MenuScale.Width, MenuScale.Height);
            bool clickedAway = UITheme.ClickBlocker(MenuScale.Width, MenuScale.Height,
                                                    flyout.width > 0f ? flyout : whole, chipStrip);

            if (UITheme.Toggle(new Rect(frX, chipY, ChipW, ChipH), "FRIENDS", _showFriends, chipBtn, UITheme.GoodTint))
            {
                _showFriends = !_showFriends;
                if (_showFriends) { _showAchievements = false; FriendsPanelUI.OnOpened(); }
            }
            if (UITheme.Toggle(new Rect(achX, chipY, ChipW, ChipH), "ACHIEVEMENTS", _showAchievements, chipBtn, UITheme.GoodTint))
            {
                _showAchievements = !_showAchievements;
                if (_showAchievements) _showFriends = false;
            }

            if (_showFriends)
                FriendsPanelUI.Draw(flyout, () => _showFriends = false);
            if (_showAchievements)
                AchievementsPanelUI.Draw(flyout, () => _showAchievements = false);

            if (clickedAway) { _showFriends = false; _showAchievements = false; }
        }

        // Every solo mode as a large panel with a live scene of that mode playing inside it. The
        // grid, the paging and the scenes are shared with the Multiplayer hub (see ModeGrid); this
        // screen only owns which modes are listed and what a pick does.
        static readonly GameMode[] SoloModes =
        {
            GameMode.Striker, GameMode.Goalkeeper, GameMode.Match,
            GameMode.Accuracy, GameMode.FreeKick, GameMode.TrickshotCup,
        };
        ModeGrid _soloGrid;
        static int _soloPage;

        void DrawSinglePlayer()
        {
            _soloGrid ??= new ModeGrid("SINGLE PLAYER", SoloModes, transform, _soloPage);
            var picked = _soloGrid.Draw(out bool back);
            _soloPage = _soloGrid.Page;
            if (picked.HasValue) { CloseSoloGrid(); Choose(picked.Value); }
            else if (back) { CloseSoloGrid(); _phase = Phase.Hub; }
        }

        // Stop simulating the moment the screen is left: Destroy is deferred to end of frame, so a
        // grid left alive would keep a scene running behind the next screen.
        void CloseSoloGrid()
        {
            _soloGrid?.Teardown();
            _soloGrid = null;
        }

        // Career Stats / Zoo: an honest placeholder, not fabricated data - see the class doc.
        void DrawComingSoon(string label)
        {
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.26f, 720f, 0.30f, 0f);
            float cy = MenuScale.Height * 0.5f;
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };

            UITheme.Title(new Rect(0, cy - 140f, MenuScale.Width, 80f), label, 44, showRule: false);
            UITheme.Hint(new Rect(MenuScale.Width * 0.5f - 260f, cy - 30f, 520f, 60f),
                "Coming soon - check back in a future update.");
            if (UITheme.Button(new Rect(MenuScale.Width * 0.5f - 160f, cy + 60f, 320f, 66f), "Back", btn))
                _phase = Phase.Hub;
        }

        void Choose(GameMode m)
        {
            _chosen = true;
            enabled = false;
            CloseSoloGrid();        // synchronous: the callback below builds the next screen NOW

            _onChoose?.Invoke(m);   // may destroy this object; do nothing after
        }

        void OnDestroy()
        {
            _settings?.Dispose();   // abort any in-flight rebind so the op isn't orphaned
            CloseSoloGrid();        // and never leave a menu scene simulating behind the next screen
        }
    }
}
