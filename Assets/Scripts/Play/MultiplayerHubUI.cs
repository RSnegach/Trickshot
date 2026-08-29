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
            GameInput.CaptureCursor(false);
        }

        void OnGUI()
        {
            MenuScale.Begin();   // fit to the window; virtual coordinates from here on

            float w = 340f, h = 66f, gap = 20f;
            float cx = MenuScale.Width * 0.5f - w * 0.5f;
            float cy = MenuScale.Height * 0.5f - (h * 1.5f + gap);

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.30f, w + 380f);
            UITheme.Title(new Rect(0, cy - 110f, MenuScale.Width, 80f), "MULTIPLAYER", 48);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(cx, cy, w, h), "Host a Session", btn)) { enabled = false; _onHost?.Invoke(); }
            if (UITheme.Button(new Rect(cx, cy + (h + gap), w, h), "Find a Session", btn)) { enabled = false; _onJoin?.Invoke(); }
            if (UITheme.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Back", btn)) { enabled = false; _onBack?.Invoke(); }

            // Transport status, with a lit dot so the state reads before the words do.
            bool steam = Multiplayer.SteamLinked;
            string status = steam ? "Steam connected" : "Direct connect. Host shares their IP.";
            var note = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = steam ? UITheme.Green : UITheme.Dim } };
            var nr = new Rect(0, cy + (h + gap) * 3f + 6f, MenuScale.Width, 22f);
            float tw = note.CalcSize(new GUIContent(status)).x;
            Color dot = steam ? UITheme.Green : UITheme.Gold;
            UITheme.Glow(new Rect(nr.center.x - tw * 0.5f - 24f, nr.y + 1f, 20f, 20f), new Color(dot.r, dot.g, dot.b, 0.7f));
            UITheme.Fill(new Rect(nr.center.x - tw * 0.5f - 17f, nr.y + 8f, 6f, 6f), dot);
            GUI.Label(nr, status, note);

            MenuScale.End();
        }
    }
}
