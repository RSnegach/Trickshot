using UnityEngine;

namespace Trickshot
{
    // Core roles plus the single-player challenge modes.
    public enum GameMode { Striker, Goalkeeper, Freeplay, TimeTrial, Accuracy, FreeKick }

    /// <summary>
    /// IMGUI start menu. Top level: Striker, Goalkeeper, Challenges. "Challenges" opens
    /// a submenu of the four extra single-player modes (Freeplay, Time Trial, Accuracy,
    /// Free Kick). Invokes a callback with the chosen mode. Kept as IMGUI so it needs no
    /// Canvas/EventSystem wiring (consistent with the rest of the runtime build).
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        System.Action<GameMode> _onChoose;
        bool _chosen;
        bool _inChallenges;

        public void Init(System.Action<GameMode> onChoose)
        {
            _onChoose = onChoose;
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
                float cy = Screen.height * 0.5f - (h * 1.5f + gap);
                GUI.Label(new Rect(0, cy - 120f, Screen.width, 80f), "TRICKSHOT", title);
                if (GUI.Button(new Rect(cx, cy, w, h), "Striker", btn)) Choose(GameMode.Striker);
                if (GUI.Button(new Rect(cx, cy + (h + gap), w, h), "Goalkeeper", btn)) Choose(GameMode.Goalkeeper);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Challenges", btn)) _inChallenges = true;
            }
            else
            {
                float cy = Screen.height * 0.5f - (h * 2.5f + gap * 2f);
                GUI.Label(new Rect(0, cy - 110f, Screen.width, 80f), "CHALLENGES", title);
                if (GUI.Button(new Rect(cx, cy, w, h), "Freeplay", btn)) Choose(GameMode.Freeplay);
                if (GUI.Button(new Rect(cx, cy + (h + gap), w, h), "Time Trial", btn)) Choose(GameMode.TimeTrial);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Accuracy", btn)) Choose(GameMode.Accuracy);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 3f, w, h), "Free Kick / Penalty", btn)) Choose(GameMode.FreeKick);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 4f, w, h), "Back", btn)) _inChallenges = false;
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
