using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// Esc pause menu with Resume / Main Menu. Freezes the game (Time.timeScale = 0)
    /// and frees the cursor while paused; exposes a global Paused flag the controllers
    /// check so no input is processed behind the menu. Main Menu invokes a teardown
    /// callback that destroys the match and reshows the start menu.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public static bool Paused { get; private set; }

        System.Action _onMainMenu;
        float _savedTimeScale = 1f;

        public void Init(System.Action onMainMenu)
        {
            _onMainMenu = onMainMenu;
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
            float cx = Screen.width * 0.5f - w * 0.5f;
            float cy = Screen.height * 0.5f - h - gap * 0.5f;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0, cy - 110f, Screen.width, 70f), "PAUSED", title);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(cx, cy, w, h), "Resume", btn)) Resume();
            if (GUI.Button(new Rect(cx, cy + h + gap, w, h), "Main Menu", btn))
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
