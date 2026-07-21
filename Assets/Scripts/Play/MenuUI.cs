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

        public void Init(System.Action<GameMode> onChoose, System.Action onMultiplayer = null)
        {
            _onChoose = onChoose;
            _onMultiplayer = onMultiplayer;
            // Menu needs a visible, free cursor.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnGUI()
        {
            if (_chosen) return;

            float w = 320f, h = 66f, gap = 20f;
            float cx = Screen.width * 0.5f - w * 0.5f;

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 54, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };

            if (!_inChallenges)
            {
                float cy = Screen.height * 0.5f - (h * 2f + gap * 1.5f);
                GUI.Label(new Rect(0, cy - 110f, Screen.width, 80f), "TRICKSHOT", title);
                if (GUI.Button(new Rect(cx, cy, w, h), "Striker", btn)) Choose(GameMode.Striker);
                if (GUI.Button(new Rect(cx, cy + (h + gap), w, h), "Goalkeeper", btn)) Choose(GameMode.Goalkeeper);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Mode", btn)) _inChallenges = true;
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 3f, w, h), "Multiplayer", btn))
                {
                    _chosen = true; enabled = false; _onMultiplayer?.Invoke();
                }
            }
            else
            {
                float cy = Screen.height * 0.5f - (h * 3f + gap * 2.5f);
                GUI.Label(new Rect(0, cy - 110f, Screen.width, 80f), "MODE", title);
                if (GUI.Button(new Rect(cx, cy, w, h), "Scrimmage", btn)) Choose(GameMode.Scrimmage);
                if (GUI.Button(new Rect(cx, cy + (h + gap), w, h), "Freeplay", btn)) Choose(GameMode.Freeplay);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Time Trial", btn)) Choose(GameMode.TimeTrial);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 3f, w, h), "Accuracy", btn)) Choose(GameMode.Accuracy);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 4f, w, h), "Free Kick / Penalty", btn)) Choose(GameMode.FreeKick);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 5f, w, h), "Back", btn)) _inChallenges = false;
            }
        }

        void Choose(GameMode m)
        {
            _chosen = true;
            enabled = false;
            _onChoose?.Invoke(m);   // may destroy this object; do nothing after
        }
    }
}
