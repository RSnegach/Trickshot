using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Multiplayer hub: the first screen after the main-menu Multiplayer button. Match and every
    /// other networkable mode (Striker, Set Pieces, Accuracy) share the plain Host/Find flow this
    /// screen used to show directly - see OtherModesUI, which now carries exactly what this file
    /// used to. Match reaches it pre-locked to the Match mode, titled "PLAY A MATCH".
    /// </summary>
    public class MultiplayerHubUI : MonoBehaviour
    {
        System.Action _onMatch, _onOtherModes, _onBack;

        public void Init(System.Action onMatch, System.Action onOtherModes, System.Action onBack)
        {
            _onMatch = onMatch; _onOtherModes = onOtherModes; _onBack = onBack;
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
            if (UITheme.Button(new Rect(cx, cy, w, h), "Match", btn)) { enabled = false; _onMatch?.Invoke(); }
            if (UITheme.Button(new Rect(cx, cy + (h + gap), w, h), "Other Modes", btn)) { enabled = false; _onOtherModes?.Invoke(); }
            if (UITheme.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Back", btn)) { enabled = false; _onBack?.Invoke(); }

            MenuScale.End();
        }
    }
}
