using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Uniform scaling for every IMGUI surface in the game: the pre-match / setup menus, the
    /// pause menu and options, and the in-match HUD (see Hud.Begin).
    ///
    /// All of it is laid out in FIXED PIXELS (panel widths, row heights, font sizes) against a
    /// single design canvas. Left alone, that means the layout is a small unreadable island on a
    /// 4K display and hangs off the edge of a short or narrow one - which is exactly how the
    /// bottom control banner ended up off-screen on some monitors.
    ///
    /// Rather than re-tune hundreds of hand-placed Rects (and risk breaking relative spacing that
    /// already works), we scale the WHOLE IMGUI coordinate space with GUI.matrix: every rect, font
    /// and gap moves by the same factor, so the layout is identical - just fitted to the window.
    /// Call Begin() at the top of OnGUI and End() on EVERY exit path.
    ///
    /// The factor fits BOTH axes (Mathf.Min of the two ratios) and is allowed to go below 1, so a
    /// 1280x720 window or a 4:3 monitor shrinks the canvas to fit instead of clipping it. It is
    /// then multiplied by the player's UI Scale option.
    ///
    /// IMPORTANT for callers: inside a Begin/End block, GUI coordinates are VIRTUAL. Use
    /// Width/Height (not Screen.width/height) to place things relative to the screen edges, and
    /// convert to real device pixels with ToScreen() for anything outside IMGUI - a camera
    /// viewport rect (Camera.pixelRect) or a Texture2D.ReadPixels call. Event.current.mousePosition
    /// is already in virtual coordinates, so hit tests need no conversion.
    /// </summary>
    public static class MenuScale
    {
        // The design canvas every fixed-pixel layout was authored against. The widest menu
        // (prematch setup) needs about this much room; the HUD legend needs the width most.
        const float DesignWidth = 1280f, DesignHeight = 760f;

        // Floor: below this the text stops being legible, so a tiny window clips rather than
        // becoming a blur. Ceiling: keeps a 4K display from having absurdly huge controls.
        const float MinFactor = 0.62f, MaxFactor = 2.1f;

        /// <summary>Bounds of the player-facing UI Scale option.</summary>
        public const float MinUserScale = 0.70f, MaxUserScale = 1.40f;

        static float _factor = 1f;
        static float _user = 1f;

        // Begin/End nest (a mode's OnGUI is scaled, and the pause menu drawn from inside it also
        // calls Begin). A single saved matrix would be clobbered by the inner Begin and leave the
        // GUI scaled after the outer End, so keep a small stack.
        static readonly Matrix4x4[] _stack = new Matrix4x4[8];
        static int _depth;
        static int _frame = -1;

        /// <summary>Player UI Scale multiplier on top of the automatic fit.</summary>
        public static float UserScale
        {
            get => _user;
            set => _user = Mathf.Clamp(value, MinUserScale, MaxUserScale);
        }

        /// <summary>The fit that Begin() would apply right now.</summary>
        public static float Fit()
        {
            float f = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
            return Mathf.Clamp(f * _user, MinFactor, MaxFactor);
        }

        /// <summary>The scale currently applied (1 = unscaled). Valid outside a block too.</summary>
        public static float Factor => _depth > 0 ? _factor : Fit();

        /// <summary>True while a Begin/End block is active (i.e. GUI.matrix is scaled by
        /// Factor right now). UITheme's crisp-text pass only applies inside one.</summary>
        public static bool Active => _depth > 0;

        /// <summary>Virtual screen size to lay out against while scaled (use instead of Screen.*).</summary>
        public static float Width  => Screen.width  / Factor;
        public static float Height => Screen.height / Factor;

        /// <summary>Convert a scaled-GUI point/rect to real device pixels (cameras, ReadPixels).</summary>
        public static Vector2 ToScreen(Vector2 guiPoint) => guiPoint * Factor;
        public static Rect ToScreen(Rect guiRect)
        {
            float f = Factor;
            return new Rect(guiRect.x * f, guiRect.y * f, guiRect.width * f, guiRect.height * f);
        }

        /// <summary>Scale the GUI coordinate space to fit the window. Always pair with End().</summary>
        public static void Begin()
        {
            // One shared look for every screen. Installing here means menus that never heard of
            // UITheme still get the new plates, because they all derive their styles from GUI.skin.
            UITheme.Install();

            // Self-heal an unbalanced Begin. Unity resets GUI.matrix to identity at the start of
            // every OnGUI pass, so a depth leaked by a previous frame means nothing and must not
            // accumulate into a permanently wrong scale.
            if (_frame != Time.frameCount) { _frame = Time.frameCount; _depth = 0; }

            // Lock the factor for the whole outermost block: recomputing it per nested Begin
            // would let a resize mid-frame place the pause menu against a different canvas.
            if (_depth == 0) _factor = Fit();
            if (_depth < _stack.Length) _stack[_depth] = GUI.matrix;
            _depth++;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(_factor, _factor, 1f));
        }

        /// <summary>Restore the previous GUI matrix.</summary>
        public static void End()
        {
            if (_depth == 0) return;   // unbalanced End (early return without Begin); ignore
            _depth--;
            if (_depth < _stack.Length) GUI.matrix = _stack[_depth];
        }
    }
}
