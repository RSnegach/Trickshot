using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The typeface. Everything the game draws is IMGUI, and IMGUI with no font set falls back to
    /// Unity's built-in Arial - which is the single loudest tell that a build is a hobby project.
    /// Barlow Condensed replaces it: a condensed grotesque, which is what sports broadcast graphics
    /// have used for decades, and narrower than Arial so nothing that fitted before stops fitting.
    ///
    /// Two cuts, not four. Medium carries the interface, Bold carries the display sizes - the clock,
    /// the score, the callouts. Both come off disk once and are shared.
    ///
    /// Bold is a REAL face here, so any style handed it must also drop back to FontStyle.Normal.
    /// FontStyle.Bold on a dynamic font asks Unity to synthesise a bold by smearing the glyph, and
    /// doing that on top of a face that is already bold is how 72 pt callouts turn to mud.
    ///
    /// OFL licensed, notice in Resources/Fonts/OFL.txt.
    /// </summary>
    public static class UIFont
    {
        static Font _body, _display;
        static bool _tried;

        /// <summary>Interface text. Null only if the font failed to load, in which case IMGUI
        /// keeps its built-in default and the game still draws.</summary>
        public static Font Body { get { Load(); return _body; } }

        /// <summary>Heavy cut for large text.</summary>
        public static Font Display { get { Load(); return _display; } }

        static void Load()
        {
            if (_tried) return;
            _tried = true;
            _body    = Resources.Load<Font>("Fonts/BarlowCondensed-Medium");
            _display = Resources.Load<Font>("Fonts/BarlowCondensed-Bold");
            if (_display == null) _display = _body;
        }

        /// <summary>
        /// Put a style on the heavy cut and cancel the synthetic bold that came with it. Silently
        /// leaves the style alone if the font is missing, so a failed load costs weight, not text.
        /// </summary>
        public static void Heavy(GUIStyle st)
        {
            if (st == null) return;
            var f = Display;
            if (f == null) return;
            st.font = f;
            st.fontStyle = FontStyle.Normal;
        }
    }
}
