using UnityEngine;

namespace Trickshot
{
    // Core roles plus the single-player modes and the full match.
    // SetPieces = networked free-kick shootout (also playable solo via the FreeKick build).
    // NOTE: append new values at the END - MatchConfig sends GameMode as a byte over the wire.
    // Freeplay and TimeTrial were removed outright (not just reordered) - pre-release, no
    // external client needs the old byte values to stay stable, so every value after them
    // shifted down rather than leaving a permanent gap.
    public enum GameMode { Striker, Goalkeeper, Accuracy, FreeKick, Match, SetPieces }

    /// <summary>
    /// IMGUI start menu, two screens: a title SPLASH (the wordmark + "press any key"), then a
    /// FIFA-style HUB of mode cards (Single Player / Multiplayer / Career Stats / Zoo / Options).
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

        // Options overlay (Keybindings + Audio), same panel the pause menu uses. Built lazily from
        // the passed GameInput; null if none was supplied (then no Options card is shown).
        OptionsMenu _options;
        bool _optionsOpen;
        CareerStatsUI _careerStats;

        // Small Friends/Achievements chips, bottom-right of the Hub only (see DrawHub). Mutually
        // exclusive flyouts - opening one closes the other.
        bool _showFriends, _showAchievements;

        public void Init(System.Action<GameMode> onChoose, System.Action onMultiplayer = null,
                         GameInput input = null, bool skipSplash = false)
        {
            _onChoose = onChoose;
            _onMultiplayer = onMultiplayer;
            if (input != null) _options = new OptionsMenu(input);
            GameInput.CaptureCursor(false);
            _splashStartFrame = Time.frameCount;
            if (skipSplash) _phase = Phase.Hub;
        }

        // Legacy Input.anyKeyDown is deliberate here, not GameInput/IStrikerInput: that abstraction
        // is a gameplay action map (Jump, LegL, LegR, ...) with no "any key at all" concept, and is
        // itself optional (null when no Options should show) - the wrong coupling for a one-shot,
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

            // Options overlay takes over the whole menu while open (same panel as the pause menu).
            // Only ever reachable from Phase.Hub (the Options card), same as the old Options button.
            if (_optionsOpen && _options != null)
            {
                _options.Draw(() => _optionsOpen = false);
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
        // Card count adapts if Options has nothing to open into (no GameInput supplied) - same
        // "hasOptions" guard the old single-column button list used, just sizing a row instead of
        // a column. Card size and spacing are fractions of the real canvas (MenuScale.Width/
        // Height), not fixed pixel values - that canvas is NOT a constant 1280x760, it scales with
        // the actual window and the UI Scale setting (measured live elsewhere in this project at
        // 1361.5x649.8 in one ordinary window), so a fixed-size block under-fills a larger canvas
        // and overflows a smaller one.
        void DrawHub()
        {
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.26f, 720f, 0.30f, 0f);

            bool hasOptions = _options != null;

            float marginX = MenuScale.Width * 0.05f;

            var optionsBtn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, stretchWidth = false };
            if (hasOptions && UITheme.Button(new Rect(MenuScale.Width - marginX - 90f, 26f, 90f, 34f), "OPTIONS", optionsBtn))
                _optionsOpen = true;

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

            DrawCornerTabs(marginX);
        }

        // Small Friends/Achievements chips in the bottom-right corner - the only place on this
        // screen they belong, per the request ("the first screen after clicking any button to
        // continue"). Flyouts open UPWARD from the chip so they never run off the bottom edge.
        void DrawCornerTabs(float marginX)
        {
            const float chipW = 150f, chipH = 40f, chipGap = 10f;
            const float panelW = 320f, panelH = 260f;
            float chipY = MenuScale.Height - 24f - chipH;
            float achX = MenuScale.Width - marginX - chipW;
            float frX = achX - chipGap - chipW;

            var chipBtn = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };

            if (UITheme.Toggle(new Rect(frX, chipY, chipW, chipH), "FRIENDS", _showFriends, chipBtn, UITheme.GoodTint))
            {
                _showFriends = !_showFriends;
                if (_showFriends) { _showAchievements = false; FriendsPanelUI.OnOpened(); }
            }
            if (UITheme.Toggle(new Rect(achX, chipY, chipW, chipH), "ACHIEVEMENTS", _showAchievements, chipBtn, UITheme.GoodTint))
            {
                _showAchievements = !_showAchievements;
                if (_showAchievements) _showFriends = false;
            }

            if (_showFriends)
                FriendsPanelUI.Draw(new Rect(frX, chipY - panelH - 10f, panelW, panelH), () => _showFriends = false);
            if (_showAchievements)
                AchievementsPanelUI.Draw(new Rect(achX + chipW - panelW, chipY - panelH - 10f, panelW, panelH), () => _showAchievements = false);
        }

        // Consolidates what used to be two separate things (the top-level Striker/Goalkeeper
        // buttons, and the old "Mode" submenu's 5 entries) into one flat list under the Single
        // Player card. Every row still calls the same Choose(GameMode) unchanged.
        void DrawSinglePlayer()
        {
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.26f, 720f, 0.30f, 0f);

            // Two columns x 3 rows, since Freeplay/TimeTrial's removal dropped this from 8 entries
            // to 6 - kept the same tested two-column shape (a single column of 8 measurably
            // clipped the title against MenuScale.Height's real, UI-Scale-shrunk size) rather than
            // risking an untested single-column-of-6 against the same constraint.
            float w = 320f, h = 52f, gap = 16f, colGap = 40f;
            float totalH = h * 3f + gap * 2f;
            float cy = MenuScale.Height * 0.5f - totalH * 0.5f;
            float cxL = MenuScale.Width * 0.5f - (w * 2f + colGap) * 0.5f;
            float cxR = cxL + w + colGap;
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };

            UITheme.Title(new Rect(0, cy - 78f, MenuScale.Width, 60f), "SINGLE PLAYER", 40, showRule: false);

            if (UITheme.Button(new Rect(cxL, cy, w, h), "Striker", btn)) Choose(GameMode.Striker);
            if (UITheme.Button(new Rect(cxL, cy + (h + gap), w, h), "Goalkeeper", btn)) Choose(GameMode.Goalkeeper);
            if (UITheme.Button(new Rect(cxL, cy + (h + gap) * 2f, w, h), "Match", btn)) Choose(GameMode.Match);

            if (UITheme.Button(new Rect(cxR, cy, w, h), "Accuracy", btn)) Choose(GameMode.Accuracy);
            if (UITheme.Button(new Rect(cxR, cy + (h + gap), w, h), "Free Kick / Penalty", btn)) Choose(GameMode.FreeKick);
            if (UITheme.Button(new Rect(cxR, cy + (h + gap) * 2f, w, h), "Back", btn)) _phase = Phase.Hub;
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
            _onChoose?.Invoke(m);   // may destroy this object; do nothing after
        }

        void OnDestroy()
        {
            _options?.Dispose();   // abort any in-flight rebind so the op isn't orphaned
        }
    }
}
