using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Uniform scaling for the pre-match / setup menus (prematch setup, customize, stadium select,
    /// host setup, lobby).
    ///
    /// Every one of those screens is laid out in FIXED PIXELS (panel widths, row heights, font
    /// sizes), which was tuned on a small window. On a 1080p laptop the whole thing ends up as a
    /// small island in the middle of the screen with cramped headers, because nothing grows with
    /// the resolution.
    ///
    /// Rather than re-tune hundreds of hand-placed Rects (and risk breaking the relative spacing
    /// that already works), we scale the WHOLE IMGUI coordinate space with GUI.matrix: every rect,
    /// font and gap grows by the same factor, so the layout is identical - just bigger and using
    /// more of the screen. Call Begin() at the top of OnGUI and End() at the bottom.
    ///
    /// IMPORTANT for callers: inside a Begin/End block, GUI coordinates are VIRTUAL. Use
    /// Width/Height (not Screen.width/height) to place things relative to the screen edges, and
    /// convert to real device pixels with ToScreen() for anything outside IMGUI - a camera
    /// viewport rect (Camera.pixelRect) or a Texture2D.ReadPixels call.
    /// </summary>
    public static class MenuScale
    {
        // The layout was authored against roughly this window height. Anything taller scales up.
        const float DesignHeight = 760f;
        // Don't shrink below 1 (small windows keep the original, already-compact layout), and cap
        // the growth so a 4K display doesn't end up with absurdly huge controls.
        const float MinFactor = 1f, MaxFactor = 2.1f;

        static float _factor = 1f;
        static Matrix4x4 _saved;

        /// <summary>The scale currently applied (1 = unscaled).</summary>
        public static float Factor => _factor;

        /// <summary>Virtual screen size to lay out against while scaled (use instead of Screen.*).</summary>
        public static float Width  => Screen.width  / _factor;
        public static float Height => Screen.height / _factor;

        /// <summary>Convert a scaled-GUI point/rect to real device pixels (cameras, ReadPixels).</summary>
        public static Vector2 ToScreen(Vector2 guiPoint) => guiPoint * _factor;
        public static Rect ToScreen(Rect guiRect) => new Rect(guiRect.x * _factor, guiRect.y * _factor,
                                                              guiRect.width * _factor, guiRect.height * _factor);

        /// <summary>Scale the GUI coordinate space up. Always pair with End().</summary>
        public static void Begin()
        {
            _factor = Mathf.Clamp(Screen.height / DesignHeight, MinFactor, MaxFactor);
            _saved = GUI.matrix;
            if (_factor != 1f)
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                           new Vector3(_factor, _factor, 1f));
        }

        /// <summary>Restore the unscaled GUI matrix.</summary>
        public static void End() => GUI.matrix = _saved;
    }
}
