using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// Esc pause menu with Resume / Match Setup / Main Menu. Freezes the game
    /// (Time.timeScale = 0) and frees the cursor while paused; exposes a global Paused
    /// flag the controllers check so no input is processed behind the menu. Match Setup
    /// tears the match down and reopens the pre-match slider config for this mode; Main
    /// Menu tears down and reshows the start menu. Both are teardown callbacks.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public static bool Paused { get; private set; }

        System.Action _onMainMenu;
        System.Action _onMatchSetup;
        System.Action _onLeave;    // client-only: leave a net match without ending it for others
        GameInput _input;
        OptionsMenu _options;
        bool _optionsOpen;
        float _savedTimeScale = 1f;

        public void Init(System.Action onMainMenu, System.Action onMatchSetup = null, GameInput input = null,
                         System.Action onLeave = null)
        {
            _onMainMenu = onMainMenu;
            _onMatchSetup = onMatchSetup;
            _onLeave = onLeave;
            _input = input;
            if (input != null) _options = new OptionsMenu(input);
            Paused = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

            // While options is open, Esc backs out to the pause buttons instead of
            // unpausing. If a rebind is listening, the rebind op consumes Esc itself, so
            // ignore it here (don't close options mid-listen).
            if (_optionsOpen)
            {
                if (_options != null && _options.IsRebinding) return;
                _optionsOpen = false;
                return;
            }

            if (Paused) Resume(); else Pause();
        }

        void Pause()
        {
            Paused = true;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Resume()
        {
            Paused = false;
            Time.timeScale = _savedTimeScale <= 0f ? 1f : _savedTimeScale;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnGUI()
        {
            if (!Paused) return;

            // Options overlay takes over the pause screen while open.
            if (_optionsOpen && _options != null)
            {
                _options.Draw(() => _optionsOpen = false);
                return;
            }

            float w = 300f, h = 60f, gap = 16f;
            // Rows: Resume, [Match Setup], [Options], [Leave Match], Main Menu.
            bool hasSetup = _onMatchSetup != null;
            bool hasOptions = _options != null;
            bool hasLeave = _onLeave != null;   // client in a networked match
            int rows = 2 + (hasSetup ? 1 : 0) + (hasOptions ? 1 : 0) + (hasLeave ? 1 : 0);
            float cx = Screen.width * 0.5f - w * 0.5f;
            float cy = Screen.height * 0.5f - (rows * h + (rows - 1) * gap) * 0.5f;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0, cy - 86f, Screen.width, 70f), "PAUSED", title);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
            float ry = cy;
            if (GUI.Button(new Rect(cx, ry, w, h), "Resume", btn)) Resume();
            ry += h + gap;

            if (hasSetup)
            {
                if (GUI.Button(new Rect(cx, ry, w, h), "Match Setup", btn))
                {
                    Time.timeScale = 1f;
                    Paused = false;
                    _onMatchSetup?.Invoke();
                }
                ry += h + gap;
            }

            if (hasOptions)
            {
                if (GUI.Button(new Rect(cx, ry, w, h), "Options", btn)) _optionsOpen = true;
                ry += h + gap;
            }

            // Client in a networked match: leave without ending it for everyone else. The host
            // keeps running the sim and this player's slot reverts to AI.
            if (hasLeave)
            {
                if (GUI.Button(new Rect(cx, ry, w, h), "Leave Match", btn))
                {
                    Time.timeScale = 1f;
                    Paused = false;
                    _onLeave?.Invoke();
                }
                ry += h + gap;
            }

            // For a networked HOST this ends the match for everyone (no host migration); in
            // single-player it's just quit-to-menu. Label reflects that.
            string quitLabel = Trickshot.Net.Multiplayer.IsHost ? "End Match" : "Main Menu";
            if (GUI.Button(new Rect(cx, ry, w, h), quitLabel, btn))
            {
                // Restore time/cursor before tearing down.
                Time.timeScale = 1f;
                Paused = false;
                _onMainMenu?.Invoke();
            }
        }

        void OnDestroy()
        {
            // Never leave the game frozen if this object is destroyed while paused.
            if (Paused) { Time.timeScale = 1f; Paused = false; }
            _options?.Dispose();   // abort any in-flight rebind operation
        }
    }
}
