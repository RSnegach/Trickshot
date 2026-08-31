using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Match's own multiplayer split, reached from the Multiplayer hub's "Match" button:
    ///   - Friendlies: host or join a lobby with friends, exactly like Match multiplayer worked
    ///     before this screen existed (see OtherModesUI, reused here with the mode pre-locked).
    ///   - Online: ranked drop-in - pick a playlist, get auto-matched with strangers, go. See
    ///     OnlineQueueUI.
    /// </summary>
    public class MatchModeUI : MonoBehaviour
    {
        System.Action _onFriendlies, _onOnline, _onBack;

        public void Init(System.Action onFriendlies, System.Action onOnline, System.Action onBack)
        {
            _onFriendlies = onFriendlies; _onOnline = onOnline; _onBack = onBack;
            GameInput.CaptureCursor(false);
        }

        void OnGUI()
        {
            MenuScale.Begin();

            float w = 340f, h = 66f, gap = 20f;
            float cx = MenuScale.Width * 0.5f - w * 0.5f;
            float cy = MenuScale.Height * 0.5f - (h * 1.5f + gap);

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.30f, w + 380f);
            UITheme.Title(new Rect(0, cy - 110f, MenuScale.Width, 80f), "MATCH", 48);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(cx, cy, w, h), "Friendlies", btn)) { enabled = false; _onFriendlies?.Invoke(); }
            if (UITheme.Button(new Rect(cx, cy + (h + gap), w, h), "Online", btn)) { enabled = false; _onOnline?.Invoke(); }
            if (UITheme.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Back", btn)) { enabled = false; _onBack?.Invoke(); }

            var sub = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Faint } };
            GUI.Label(new Rect(cx, cy + h - 22f, w, 18f), "Play with friends", sub);
            GUI.Label(new Rect(cx, cy + (h + gap) + h - 22f, w, 18f), "Ranked drop-in", sub);

            MenuScale.End();
        }
    }
}
