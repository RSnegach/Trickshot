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
        float _savedTimeScale = 1f;

        public void Init(System.Action onMainMenu, System.Action onMatchSetup = null)
        {
            _onMainMenu = onMainMenu;
            _onMatchSetup = onMatchSetup;
            Paused = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (Paused) Resume(); else Pause();
            }
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

            float w = 300f, h = 64f, gap = 20f;
            // Center the stack of buttons (3 rows when Match Setup is available, else 2).
            bool hasSetup = _onMatchSetup != null;
            int rows = hasSetup ? 3 : 2;
            float cx = Screen.width * 0.5f - w * 0.5f;
            float cy = Screen.height * 0.5f - (rows * h + (rows - 1) * gap) * 0.5f;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0, cy - 90f, Screen.width, 70f), "PAUSED", title);

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

            if (GUI.Button(new Rect(cx, ry, w, h), "Main Menu", btn))
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
        }
    }
}
