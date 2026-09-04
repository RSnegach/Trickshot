using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The round intro card (design 2.7 / 7.2): three seconds of stage, the two nations with their
    /// flags, and "GHANA KICK FIRST" - shown as the driver enters its Intro phase, over the placed
    /// bodies. Self-hides after <see cref="CupTuning.IntroSeconds"/>.
    ///
    /// Timed on Time.deltaTime, like the driver's Intro phase it accompanies: a Solo pause freezes
    /// both together and they leave together; under the MP overlay pause both keep running.
    /// Nothing here is clickable, so the pause menu drawing over it costs nothing.
    /// </summary>
    public sealed class CupIntroCard : MonoBehaviour
    {
        public const float Seconds = CupTuning.IntroSeconds;
        public const float InSeconds = 0.3f, OutSeconds = 0.35f;
        /// <summary>IMGUI depth: in front of the HUD and the director's screens.</summary>
        public const int GuiDepth = -1;
        const float PlateW = 620f, PlateH = 230f, Flag = 64f;

        CupStage _stage;
        int _nationA = -1, _nationB = -1;
        CupSide _first = CupSide.A;
        float _age;
        bool _visible;
        static GUIStyle _stageStyle, _nameStyle, _codeStyle, _vsStyle, _firstStyle;

        public bool Visible => _visible;

        public static CupIntroCard Create(Transform root)
        {
            var go = new GameObject("CupIntroCard");
            if (root != null) go.transform.SetParent(root, false);
            return go.AddComponent<CupIntroCard>();
        }

        /// <summary>Show for Seconds: stage, nations by table index, and which side kicks first.</summary>
        public void Show(CupStage stage, int nationA, int nationB, CupSide firstKicker)
        {
            _stage = stage;
            _nationA = nationA;
            _nationB = nationB;
            _first = firstKicker;
            _age = 0f;
            _visible = true;
        }

        public void Hide() => _visible = false;

        void Update()
        {
            if (!_visible) return;
            _age += Time.deltaTime;
            if (_age >= Seconds) _visible = false;
        }

        void OnGUI()
        {
            if (!_visible || PauseMenu.Frozen) return;
            GUI.depth = GuiDepth;
            MenuScale.Begin();
            try { Draw(); }
            finally { MenuScale.End(); }
        }

        void Draw()
        {
            Styles();
            float w = MenuScale.Width, h = MenuScale.Height;
            float inT = Mathf.Clamp01(_age / InSeconds);
            float outT = Mathf.Clamp01((_age - (Seconds - OutSeconds)) / OutSeconds);
            float alpha = Mathf.Min(inT, 1f - outT);
            float scale = Mathf.Lerp(0.92f, 1f, EaseOutBack(inT));

            // The plates set GUI.color themselves, so alpha is threaded through every colour
            // explicitly; only the scale rides GUI.matrix.
            var keepMat = GUI.matrix;
            var r = new Rect(w * 0.5f - PlateW * 0.5f, h * 0.5f - PlateH * 0.5f - 30f, PlateW, PlateH);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), r.center);

            UITheme.Glow(new Rect(r.x - 120f, r.y - 90f, r.width + 240f, r.height + 180f), new Color(0f, 0f, 0f, 0.45f * alpha));
            UITheme.Panel(r, UITheme.Gold, true, alpha);
            UITheme.Shadowed(new Rect(r.x, r.y + 14f, r.width, 30f), CupStages.Header(_stage), _stageStyle, WithAlpha(UITheme.Gold, alpha), 0.6f, 1f);
            Hairline(r.x + 40f, r.y + 52f, r.width - 80f, alpha);

            // A on the left, B on the right, "vs" between; the first kicker's flag gets a gold edge.
            float cy = r.y + 112f;
            DrawSide(_nationA, r.x + 40f, cy, false, _first == CupSide.A, alpha);
            DrawSide(_nationB, r.xMax - 40f, cy, true, _first == CupSide.B, alpha);
            UITheme.Shadowed(new Rect(r.x + r.width * 0.5f - 40f, cy - 18f, 80f, 36f), "vs", _vsStyle, WithAlpha(UITheme.Dim, alpha), 0.6f, 1f);

            Hairline(r.x + 40f, r.yMax - 58f, r.width - 80f, alpha);
            int firstNation = _first == CupSide.A ? _nationA : _nationB;
            UITheme.Shadowed(new Rect(r.x, r.yMax - 48f, r.width, 30f), CupText.KickFirst(Name(firstNation)), _firstStyle, WithAlpha(UITheme.Gold, alpha), 0.6f, 1.5f);

            GUI.matrix = keepMat;
        }

        static void DrawSide(int nation, float edgeX, float cy, bool right, bool first, float alpha)
        {
            float fx = right ? edgeX - Flag : edgeX;
            var fr = new Rect(fx, cy - Flag * 0.5f, Flag, Flag);
            var frame = new Rect(fr.x - 4f, fr.y - 4f, fr.width + 8f, fr.height + 8f);
            UITheme.Chip(frame, new Color(0.03f, 0.04f, 0.06f, 0.9f * alpha));
            if (first) UITheme.FrameOutline(frame, WithAlpha(UITheme.Gold, alpha));
            var tex = nation >= 0 && CupNations.IsValid(nation) ? CupNations.Thumb(nation) : null;
            if (tex != null)
            {
                var keep = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(fr, tex);
                GUI.color = keep;
            }
            else UITheme.Fill(fr, new Color(1f, 1f, 1f, 0.08f * alpha));

            const float textW = 180f;
            float tx = right ? fx - 14f - textW : fr.xMax + 14f;
            _nameStyle.alignment = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            _codeStyle.alignment = _nameStyle.alignment;
            UITheme.Shadowed(new Rect(tx, cy - 22f, textW, 28f), Name(nation), _nameStyle, WithAlpha(UITheme.Ink, alpha), 0.6f, 1f);
            UITheme.Shadowed(new Rect(tx, cy + 6f, textW, 20f), Code(nation), _codeStyle, WithAlpha(UITheme.Dim, alpha), 0.4f, 1f);
        }

        static void Hairline(float x, float y, float w, float alpha) => UITheme.Fill(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.09f * alpha));
        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        static string Code(int nation) => nation >= 0 && CupNations.IsValid(nation) ? CupNations.Code(nation) : "---";
        static string Name(int nation) => nation >= 0 && CupNations.IsValid(nation) ? CupNations.Name(nation) : "-";

        static float EaseOutBack(float t)
        {
            const float c1 = 1.7f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        static void Styles()
        {
            if (_stageStyle != null) return;
            _stageStyle = new GUIStyle { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            _nameStyle = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink } };
            _codeStyle = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            _vsStyle = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
            _firstStyle = new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            UIFont.Heavy(_stageStyle);
            UIFont.Heavy(_nameStyle);
            UIFont.Heavy(_firstStyle);
        }
    }
}
