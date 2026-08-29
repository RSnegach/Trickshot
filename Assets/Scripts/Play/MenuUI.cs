using UnityEngine;

namespace Trickshot
{
    // Core roles plus the single-player modes and the full scrimmage match.
    // SetPieces = networked free-kick shootout (also playable solo via the FreeKick build).
    // NOTE: append new values at the END - MatchConfig sends GameMode as a byte over the wire.
    public enum GameMode { Striker, Goalkeeper, Freeplay, TimeTrial, Accuracy, FreeKick, Scrimmage, SetPieces }

    /// <summary>
    /// IMGUI start menu. Top level: Striker, Goalkeeper, Mode. "Mode" opens a submenu of
    /// the extra modes (Freeplay, Time Trial, Accuracy, Free Kick, Scrimmage). Invokes a
    /// callback with the chosen mode. Kept as IMGUI so it needs no Canvas/EventSystem
    /// wiring (consistent with the rest of the runtime build).
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        System.Action<GameMode> _onChoose;
        System.Action _onMultiplayer;
        bool _chosen;
        bool _inChallenges;

        // Options overlay (Keybindings + Audio), same panel the pause menu uses. Built lazily from
        // the passed GameInput; null if none was supplied (then no Options button is shown).
        OptionsMenu _options;
        bool _optionsOpen;

        public void Init(System.Action<GameMode> onChoose, System.Action onMultiplayer = null,
                         GameInput input = null)
        {
            _onChoose = onChoose;
            _onMultiplayer = onMultiplayer;
            if (input != null) _options = new OptionsMenu(input);
            // Menu needs a visible, free cursor.
            GameInput.CaptureCursor(false);
        }

        void OnGUI()
        {
            if (_chosen) return;

            // Fit to the window (see MenuScale); virtual coordinates from here on.
            MenuScale.Begin();

            float w = 320f, h = 66f, gap = 20f;
            float cx = MenuScale.Width * 0.5f - w * 0.5f;

            // Darken behind the menu so the white title and buttons read over the moving scene.
            // Ramped from nothing at the top, because the top of the frame is sky and this used to
            // be a flat 0.30 over it: measured, the backdrop was reaching the screen at 0.70 of its
            // authored brightness at the edges and 0.35 down the middle. Ramped it comes to 0.95 at
            // the top corners and 0.64 at the column, and the title's own bloom plus the button
            // plates carry the local contrast that the flat dim was being asked for.
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.26f, 720f, 0.30f, 0f);

            // Options overlay takes over the whole menu while open (same panel as the pause menu).
            if (_optionsOpen && _options != null)
            {
                _options.Draw(() => _optionsOpen = false);
                MenuScale.End();
                return;
            }

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };

            if (!_inChallenges)
            {
                // Row count grows by one when Options is available, so keep the column centered.
                bool hasOptions = _options != null;
                float rows = hasOptions ? 5f : 4f;
                float cy = MenuScale.Height * 0.5f - (h * rows + gap * (rows - 1f)) * 0.5f;
                UITheme.Title(new Rect(0, cy - 110f, MenuScale.Width, 80f), "TRICKSHOT");
                if (UITheme.Button(new Rect(cx, cy, w, h), "Striker", btn)) Choose(GameMode.Striker);
                if (UITheme.Button(new Rect(cx, cy + (h + gap), w, h), "Goalkeeper", btn)) Choose(GameMode.Goalkeeper);
                if (UITheme.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Mode", btn)) _inChallenges = true;
                if (UITheme.Button(new Rect(cx, cy + (h + gap) * 3f, w, h), "Multiplayer", btn))
                {
                    _chosen = true; enabled = false; _onMultiplayer?.Invoke();
                }
                if (hasOptions && UITheme.Button(new Rect(cx, cy + (h + gap) * 4f, w, h), "Options", btn))
                    _optionsOpen = true;
            }
            else
            {
                float cy = MenuScale.Height * 0.5f - (h * 3f + gap * 2.5f);
                UITheme.Title(new Rect(0, cy - 110f, MenuScale.Width, 80f), "MODE");
                if (UITheme.Button(new Rect(cx, cy, w, h), "Scrimmage", btn)) Choose(GameMode.Scrimmage);
                if (UITheme.Button(new Rect(cx, cy + (h + gap), w, h), "Freeplay", btn)) Choose(GameMode.Freeplay);
                if (UITheme.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Time Trial", btn)) Choose(GameMode.TimeTrial);
                if (UITheme.Button(new Rect(cx, cy + (h + gap) * 3f, w, h), "Accuracy", btn)) Choose(GameMode.Accuracy);
                if (UITheme.Button(new Rect(cx, cy + (h + gap) * 4f, w, h), "Free Kick / Penalty", btn)) Choose(GameMode.FreeKick);
                if (UITheme.Button(new Rect(cx, cy + (h + gap) * 5f, w, h), "Back", btn)) _inChallenges = false;
            }

            MenuScale.End();
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
