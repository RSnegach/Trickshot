using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The loading card before a round (design 6.4): a solid scrim so the round build (bodies,
    /// referee, wall, ball under the RoundRoot) is never seen popping in, and a centred plate with
    /// the stage, "BRA vs GHA", the two flags and a spinner.
    ///
    /// Timing is the caller's, within one rule the card enforces itself: it never hides before
    /// <c>minSeconds</c> have passed (design: at least 1.5 s), so a Hide() that arrives early is
    /// honoured as soon as the minimum is up. The MP "everyone loaded" barrier is the director's
    /// job (it calls Hide when the barrier opens or times out). Unscaled time, so a slow-mo or a
    /// frozen sim never stretches the cover.
    ///
    /// Draws in its own OnGUI at a depth ABOVE the director's screens and the HUD, because it is a
    /// cover: whatever is being built or torn down beneath it stays out of sight, including the
    /// previous round's last frame. It fades out over <see cref="FadeSeconds"/> once released.
    ///
    /// The one thing it must NOT cover is the pause menu, which draws at IMGUI's default depth 0 and
    /// so would sit behind this card's opaque scrim - a menu the player opened and cannot see, with
    /// invisible live buttons. While <see cref="PauseMenu.Paused"/> the card therefore drops BEHIND
    /// the menu (depth 1) rather than hiding: an early return would lift the cover off a half-built
    /// round, which is the one thing this card exists to prevent. The condition only ever changes in
    /// PauseMenu.Update, never between an OnGUI Layout and Repaint pass, and the card allocates no
    /// controls, so nothing can shift under it.
    /// </summary>
    public sealed class CupLoadingUI : MonoBehaviour
    {
        public const float FadeSeconds = 0.25f;
        /// <summary>IMGUI depth: in front of the director's screens (0) and the HUD.</summary>
        public const int GuiDepth = -1;
        /// <summary>IMGUI depth while the pause menu is up: behind it (the menu draws at the default 0).</summary>
        public const int PausedGuiDepth = 1;
        const float PlateW = 420f, PlateH = 180f, Flag = 48f;

        CupDirector _director;
        CupStage _stage;
        int _nationA = -1, _nationB = -1;
        float _shownAt, _minSeconds, _hideRequestedAt = -1f, _fadeStart = -1f;
        bool _visible;
        static GUIStyle _stageStyle, _vsStyle, _codeStyle, _nameStyle;

        /// <summary>The card is covering the screen (fully or fading).</summary>
        public bool Visible => _visible;
        /// <summary>Fully opaque right now: safe to build or destroy scene objects underneath.</summary>
        public bool Covering => _visible && _fadeStart < 0f;
        /// <summary>The minimum display time has passed (Hide will take effect at once).</summary>
        public bool MinElapsed => _visible && Time.unscaledTime - _shownAt >= _minSeconds;
        /// <summary>Hide has been requested (it may still be waiting on the minimum or fading).</summary>
        public bool HideRequested => _hideRequestedAt >= 0f;

        public static CupLoadingUI Create(Transform root, CupDirector director)
        {
            var go = new GameObject("CupLoadingUI");
            if (root != null) go.transform.SetParent(root, false);
            var ui = go.AddComponent<CupLoadingUI>();
            ui._director = director;
            return ui;
        }

        /// <summary>Show the card for <paramref name="stage"/>, nations by table index (a -1 nation draws as "---").</summary>
        public void Show(CupStage stage, int nationA, int nationB, float minSeconds)
        {
            _stage = stage;
            _nationA = nationA;
            _nationB = nationB;
            _minSeconds = Mathf.Max(0f, minSeconds);
            _shownAt = Time.unscaledTime;
            _hideRequestedAt = -1f;
            _fadeStart = -1f;
            _visible = true;
        }

        /// <summary>Release the cover: fades out once the minimum has elapsed.</summary>
        public void Hide()
        {
            if (!_visible || _hideRequestedAt >= 0f) return;
            _hideRequestedAt = Time.unscaledTime;
        }

        /// <summary>Drop the card at once (a torn-down round, End Match).</summary>
        public void HideImmediate()
        {
            _visible = false;
            _hideRequestedAt = -1f;
            _fadeStart = -1f;
        }

        void Update()
        {
            if (!_visible) return;
            float now = Time.unscaledTime;
            if (_hideRequestedAt >= 0f && _fadeStart < 0f && now - _shownAt >= _minSeconds) _fadeStart = now;
            if (_fadeStart >= 0f && now - _fadeStart >= FadeSeconds) HideImmediate();
        }

        void OnGUI()
        {
            if (!_visible) return;
            GUI.depth = PauseMenu.Paused ? PausedGuiDepth : GuiDepth;
            MenuScale.Begin();
            try { Draw(); }
            finally { MenuScale.End(); }
        }

        void Draw()
        {
            Styles();
            float w = MenuScale.Width, h = MenuScale.Height;
            // The plates (Fill / Panel / Chip) set GUI.color themselves, so the fade is threaded
            // through every colour explicitly rather than through a global GUI.color tint.
            float alpha = _fadeStart < 0f ? 1f : 1f - Mathf.Clamp01((Time.unscaledTime - _fadeStart) / FadeSeconds);

            // Solid cover (0.9 per the design) - the world under it is mid-build.
            UITheme.Scrim(w, h, 0.9f * alpha, 900f, 0.35f * alpha);

            var r = new Rect(w * 0.5f - PlateW * 0.5f, h * 0.5f - PlateH * 0.5f, PlateW, PlateH);
            UITheme.Panel(r, UITheme.Gold, true, alpha);
            UITheme.Shadowed(new Rect(r.x, r.y + 14f, r.width, 26f), CupStages.Header(_stage), _stageStyle, WithAlpha(UITheme.Gold, alpha), 0.6f, 1f);
            UITheme.Fill(new Rect(r.x + 30f, r.y + 46f, r.width - 60f, 1f), new Color(1f, 1f, 1f, 0.09f * alpha));

            // Flags either side of the "A vs B" line, codes under the flags, the names beneath.
            float cy = r.y + 86f;
            DrawFlag(_nationA, r.x + 54f, cy, alpha);
            DrawFlag(_nationB, r.xMax - 54f - Flag, cy, alpha);
            string codeA = Code(_nationA), codeB = Code(_nationB);
            UITheme.Shadowed(new Rect(r.x + 110f, cy - 4f, r.width - 220f, 34f), CupText.Versus(codeA, codeB), _vsStyle, WithAlpha(UITheme.Ink, alpha), 0.7f, 2f);
            UITheme.Shadowed(new Rect(r.x + 110f, cy + 30f, r.width - 220f, 20f), Name(_nationA) + "  ·  " + Name(_nationB), _nameStyle, WithAlpha(UITheme.Dim, alpha), 0.4f, 1f);

            UITheme.Spinner(new Rect(r.x + r.width * 0.5f - 14f, r.yMax - 40f, 28f, 28f), WithAlpha(UITheme.Gold, alpha));
        }

        static void DrawFlag(int nation, float x, float cy, float alpha)
        {
            var tex = nation >= 0 && CupNations.IsValid(nation) ? CupNations.Thumb(nation) : null;
            var r = new Rect(x, cy - Flag * 0.5f, Flag, Flag);
            UITheme.Chip(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(0.03f, 0.04f, 0.06f, 0.9f * alpha));
            if (tex != null)
            {
                var keep = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(r, tex);
                GUI.color = keep;
            }
            else UITheme.Fill(r, new Color(1f, 1f, 1f, 0.08f * alpha));
            UITheme.Shadowed(new Rect(r.x - 20f, r.yMax + 6f, r.width + 40f, 18f), Code(nation), _codeStyle, WithAlpha(UITheme.Dim, alpha), 0.4f, 1f);
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        static string Code(int nation) => nation >= 0 && CupNations.IsValid(nation) ? CupNations.Code(nation) : "---";
        static string Name(int nation) => nation >= 0 && CupNations.IsValid(nation) ? CupNations.Name(nation) : "-";

        static void Styles()
        {
            if (_stageStyle != null) return;
            _stageStyle = new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            _vsStyle = new GUIStyle { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Ink } };
            _codeStyle = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
            _nameStyle = new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
            UIFont.Heavy(_stageStyle);
            UIFont.Heavy(_vsStyle);
        }
    }
}
