using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The radial emote wheel for the cup: the scored window, the win beat and the podium. A copy
    /// of NetStrikerMatch.DrawEmoteWheel with the body and the pages passed in, so the podium can
    /// hand it a curated page (standing emotes that leave the trophy arm alone) and the round HUD
    /// the full Celebration.Pages.
    ///
    /// Cursor contract: <see cref="SetOpen"/> is the ONLY way the wheel opens or closes, and it
    /// captures / frees the cursor through GameInput.CaptureCursor - the single cursor owner - so
    /// the wheel can never leave a free cursor behind on a closed wheel. A caller closing the wheel
    /// because the PAUSE menu came up must NOT re-capture (the menu owns the cursor and Resume
    /// re-captures); use <see cref="ForceClosed"/> for that.
    ///
    /// A pick does two things: GameInput.SetEmotePick puts the id on the next InputFrame (the host
    /// starts the emote on the authoritative body and every peer's puppet follows), and the local
    /// Celebration plays at once for instant feedback - the same pair NetStrikerMatch does.
    ///
    /// Draw inside a Hud.Begin/End block. IMGUI rule: the pick returns early only AFTER every
    /// control on the wheel has been allocated for this pass, and the wheel is closed, so the next
    /// pass draws nothing - control ids never shift under a click.
    /// </summary>
    public static class CupEmoteWheel
    {
        const float Radius = 210f;
        const float ButtonW = 132f, ButtonH = 42f;

        static int _page;
        static GUIStyle _label, _arrow, _hint;

        /// <summary>The page showing (persists across opens, like the match wheels).</summary>
        public static int Page
        {
            get => _page;
            set => _page = value;
        }

        /// <summary>Open or close the wheel; frees the cursor while open and captures it on close.</summary>
        public static void SetOpen(ref bool open, bool value)
        {
            open = value;
            GameInput.CaptureCursor(!value);
        }

        /// <summary>Close without touching the cursor (the pause menu took it).</summary>
        public static void ForceClosed(ref bool open) => open = false;

        /// <summary>
        /// Draw the wheel while <paramref name="open"/>; returns the emote picked this pass (or null).
        /// <paramref name="celeb"/> may be null (the pick still goes on the wire).
        /// </summary>
        public static Celebration.Emote? Draw(Celebration celeb, GameInput input, (Celebration.Emote e, string name)[][] pages, ref bool open)
        {
            if (!open || pages == null || pages.Length == 0) return null;
            Styles();

            float cx = Hud.W * 0.5f, cy = Hud.H * 0.5f;
            Hud.Scrim(0.55f);

            int pageCount = pages.Length;
            _page = ((_page % pageCount) + pageCount) % pageCount;
            var page = pages[_page];
            int n = page != null ? page.Length : 0;

            Celebration.Emote? picked = null;
            for (int i = 0; i < n; i++)
            {
                float ang = (360f / n * i) * Mathf.Deg2Rad;
                float sx = cx + Mathf.Sin(ang) * Radius;
                float sy = cy - Mathf.Cos(ang) * Radius;
                var r = new Rect(sx - ButtonW * 0.5f, sy - ButtonH * 0.5f, ButtonW, ButtonH);
                if (UITheme.Button(r, page[i].name, _label) && picked == null) picked = page[i].e;
            }

            // Left/right arrows flanking the ring cycle the pages.
            int pageDelta = 0;
            if (UITheme.Button(new Rect(cx - Radius - 96f, cy - 26f, 52f, 52f), "‹", _arrow)) pageDelta--;
            if (UITheme.Button(new Rect(cx + Radius + 44f, cy - 26f, 52f, 52f), "›", _arrow)) pageDelta++;

            UITheme.Label(new Rect(cx - 160f, cy - 20f, 320f, 22f), "Click an emote  ·  B to close", _hint);
            Hud.PageDots(cx, cy + 16f, pageCount, _page);

            if (pageDelta != 0) _page += pageDelta;

            if (picked.HasValue)
            {
                Play(celeb, input, picked.Value);
                SetOpen(ref open, false);
            }
            return picked;
        }

        /// <summary>Start an emote on a body the way the wheel does: on the wire AND locally at once.</summary>
        public static void Play(Celebration celeb, GameInput input, Celebration.Emote e)
        {
            if (input != null) input.SetEmotePick((int)e);   // sync to the host -> everyone
            if (celeb != null)
            {
                // Play snapshots the body's control flags; a second Play mid-emote would snapshot
                // the emote's own flags and restore those instead of the body's.
                if (celeb.Playing) celeb.Cancel();
                celeb.Play(e);   // instant local feedback
            }
        }

        static void Styles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
            _arrow = new GUIStyle(GUI.skin.button) { fontSize = 30, fontStyle = FontStyle.Bold };
            _hint = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Hud.Dim } };
        }
    }
}
