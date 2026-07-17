using UnityEngine;

namespace Trickshot
{
    public enum GameMode { Striker, Goalkeeper }

    /// <summary>
    /// Simple IMGUI start menu with two buttons: Striker and Goalkeeper. Invokes a
    /// callback with the chosen mode, then disables itself. Kept as IMGUI so it needs
    /// no Canvas/EventSystem wiring (consistent with the rest of the runtime build).
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        System.Action<GameMode> _onChoose;
        bool _chosen;

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

            float w = 320f, h = 70f, gap = 24f;
            float cx = Screen.width * 0.5f - w * 0.5f;
            float cy = Screen.height * 0.5f - h - gap * 0.5f;

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 54, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(0, cy - 120f, Screen.width, 80f), "TRICKSHOT", title);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = FontStyle.Bold };

            if (GUI.Button(new Rect(cx, cy, w, h), "Striker", btn)) Choose(GameMode.Striker);
            if (GUI.Button(new Rect(cx, cy + h + gap, w, h), "Goalkeeper", btn)) Choose(GameMode.Goalkeeper);
        }

        void Choose(GameMode m)
        {
            _chosen = true;
            _onChoose?.Invoke(m);
            enabled = false;
        }
    }
}
