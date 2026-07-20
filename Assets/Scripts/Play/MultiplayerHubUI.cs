using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Multiplayer hub: the first screen after the main-menu Multiplayer button. Choose to
    /// HOST a session (goes to host setup) or JOIN one (goes to the session browser). Shows
    /// whether Steam is linked; when it isn't, the flow still works over the in-process
    /// loopback transport (useful for testing), noted on-screen.
    /// </summary>
    public class MultiplayerHubUI : MonoBehaviour
    {
        System.Action _onHost, _onJoin, _onBack;

        public void Init(System.Action onHost, System.Action onJoin, System.Action onBack)
        {
            _onHost = onHost; _onJoin = onJoin; _onBack = onBack;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        void OnGUI()
        {
            float w = 340f, h = 66f, gap = 20f;
            float cx = Screen.width * 0.5f - w * 0.5f;
            float cy = Screen.height * 0.5f - (h * 1.5f + gap);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0, cy - 110f, Screen.width, 80f), "MULTIPLAYER", title);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(cx, cy, w, h), "Host a Session", btn)) { enabled = false; _onHost?.Invoke(); }
            if (GUI.Button(new Rect(cx, cy + (h + gap), w, h), "Find a Session", btn)) { enabled = false; _onJoin?.Invoke(); }
            if (GUI.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Back", btn)) { enabled = false; _onBack?.Invoke(); }

            var note = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.8f, 0.85f) } };
            string status = Multiplayer.SteamLinked
                ? "Steam connected"
                : "Direct connect (LAN / Tailscale) — host shares their IP, friends join by IP. See MULTIPLAYER.md.";
            GUI.Label(new Rect(0, cy + (h + gap) * 3f + 6f, Screen.width, 22f), status, note);
        }
    }
}
