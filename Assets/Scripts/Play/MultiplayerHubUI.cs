using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Multiplayer hub: the first screen after the main-menu Multiplayer button. One large panel
    /// per networkable mode (NetModes), each showing a live scene from that mode, and picking one
    /// IS the mode choice - every screen after it (Host/Find, Host Setup, the session browser) is
    /// locked to it, so none of them asks again. There is no "Other Modes" catch-all any more.
    ///
    /// Layout, paging and the scenes all live in ModeGrid, shared with the Single Player list. The
    /// page survives the instance: every Back rebuilds this hub, and a mode picked off page two
    /// should come back to page two.
    /// </summary>
    public class MultiplayerHubUI : MonoBehaviour
    {
        /// <summary>Every mode with a networked driver, in hub order. Host Setup and the browser
        /// are locked to one of these; a mode not listed here cannot be hosted or found.</summary>
        public static readonly GameMode[] NetModes = { GameMode.Match, GameMode.Striker, GameMode.SetPieces, GameMode.Accuracy, GameMode.TrickshotCup };

        static int _page;

        System.Action<GameMode> _onMode;
        System.Action _onBack;
        ModeGrid _grid;

        public void Init(System.Action<GameMode> onMode, System.Action onBack)
        {
            _onMode = onMode; _onBack = onBack;
            GameInput.CaptureCursor(false);
            _grid = new ModeGrid("MULTIPLAYER", NetModes, transform, _page);
        }

        void OnGUI()
        {
            MenuScale.Begin();   // fit to the window; virtual coordinates from here on
            // Callbacks fire AFTER MenuScale.End(): they destroy this object, and the GUI matrix
            // must be popped either way.
            var picked = _grid.Draw(out bool back);
            _page = _grid.Page;
            MenuScale.End();

            if (picked.HasValue) { enabled = false; _grid.Teardown(); _onMode?.Invoke(picked.Value); }
            else if (back) { enabled = false; _grid.Teardown(); _onBack?.Invoke(); }
        }

        void OnDestroy() => _grid?.Teardown();
    }
}
