using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// The goal, as a picture you resize: drag its sides or corners like an image, the bottom edge
    /// (the goal line) never moves, width and height read out in metres beside the left post, and
    /// a to-scale keeper stands beside the right post so the size means something. Under it, the
    /// goalkeeper difficulty ladder. One widget, drawn beside the host's stadium pick before a
    /// networked striker match and as the whole of the in-match Match Setup.
    ///
    /// Draw-only: it edits the three values it is handed and says whether they changed. Applying
    /// them to a live match (statics, the rebuilt goal, the wire) is GoalSetup.Apply.
    /// </summary>
    public class GoalEditor
    {
        public const float PanelW = 440f, PanelH = 318f;
        /// <summary>Height of the goal + keeper row alone (unframed: embedded in another panel).</summary>
        public const float ContentH = 280f;

        // The same range the pre-match sliders allow, so the picture can never make a goal the
        // rest of the game does not expect.
        public const float MinMul = 0.6f, MaxMul = 1.5f;
        public static float MinW => SimConfig.GoalWidthBase * MinMul;
        public static float MaxW => SimConfig.GoalWidthBase * MaxMul;
        public static float MinH => SimConfig.GoalHeightBase * MinMul;
        public static float MaxH => SimConfig.GoalHeightBase * MaxMul;

        // Which handle is being dragged: 0 none, 1 left post, 2 right post, 3 crossbar,
        // 4 top-left corner, 5 top-right corner.
        int _drag;

        /// <summary>Draw the window at `p`. `width`/`height` are metres; `keeperLevel` indexes
        /// SimConfig.AiLevelNames. `framed` draws it as its own window (panel + title); false embeds
        /// just the goal + keeper row (ContentH tall) inside somebody else's panel, the single-player
        /// pre-match screen. Returns true if anything changed this pass.</summary>
        // Cached styles: this panel is drawn inside the pause menu over a networked match that
        // keeps running, and a fresh GUIStyle per label per OnGUI pass is garbage at frame rate.
        static GUIStyle _titleSt, _lblSt, _smallBtnSt, _readSt, _readLSt, _klblSt, _kBtnSt, _kBtnSelSt;

        public bool Draw(Rect p, ref float width, ref float height, ref int keeperLevel, bool framed = true)
        {
            float w0 = width, h0 = height; int k0 = keeperLevel;
            width  = Mathf.Clamp(width,  MinW, MaxW);
            height = Mathf.Clamp(height, MinH, MaxH);

            float inset = framed ? 24f : 0f;
            if (framed)
            {
                UITheme.Panel(p, UITheme.Gold);
                var title = _titleSt ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Ink } };
                UITheme.Shadowed(new Rect(p.x + 24f, p.y + 12f, 200f, 30f), "GOAL", title, UITheme.Ink, 0.7f, 2f);
                UITheme.Fill(new Rect(p.x + 24f, p.y + 42f, 40f, 2.5f), UITheme.Gold);
            }
            else
            {
                var lbl = _lblSt ??= new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = UITheme.Ink } };
                UITheme.Label(new Rect(p.x, p.y + 2f, 200f, 24f), "Goal:", lbl);
            }

            // Default: the regulation goal. The keeper is deliberately not reset by it - "default
            // goal dimensions" is what it says.
            var smallBtn = _smallBtnSt ??= new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(p.xMax - inset - 96f, p.y + (framed ? 12f : 0f), 96f, 28f), "Default", smallBtn))
            { width = SimConfig.GoalWidthBase; height = SimConfig.GoalHeightBase; }

            // ---- the picture ----
            var box = framed ? new Rect(p.x + 20f, p.y + 54f, p.width - 40f, 172f)
                             : new Rect(p.x, p.y + 36f, p.width, 172f);
            UITheme.Chip(box, new Color(0.05f, 0.06f, 0.09f, 0.82f), UITheme.Gold);

            // One scale for everything in the box, sized so the BIGGEST goal plus the keeper beside
            // it still fit with room for the height label on the left. Metres to pixels.
            const float labelRoom = 62f, keeperRoom = 48f, pad = 10f;
            float usable = box.width - labelRoom - keeperRoom - pad * 2f;
            float scale = usable / MaxW;
            float groundY = box.yMax - 26f;                       // the goal line: never moves
            float cx = box.x + labelRoom + pad + usable * 0.5f;    // the goal stays centred on its line

            HandleDrag(cx, groundY, scale, ref width, ref height);

            float halfPx = width * 0.5f * scale, hPx = height * scale;
            float lx = cx - halfPx, rx = cx + halfPx, topY = groundY - hPx;

            var pc = GUI.color;

            // Net: a faint mesh behind the frame, cells about half a metre.
            GUI.color = new Color(1f, 1f, 1f, 0.16f);
            float cell = 0.5f * scale;
            for (float gx = lx + cell; gx < rx - 1f; gx += cell)
                GUI.DrawTexture(new Rect(gx, topY, 1f, hPx), Texture2D.whiteTexture);
            for (float gy = groundY - cell; gy > topY + 1f; gy -= cell)
                GUI.DrawTexture(new Rect(lx, gy, rx - lx, 1f), Texture2D.whiteTexture);

            // Goal line, full width of the box.
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            GUI.DrawTexture(new Rect(box.x + 6f, groundY, box.width - 12f, 2f), Texture2D.whiteTexture);

            // Frame: two posts and the bar, white.
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(lx - 2f, topY, 4f, hPx), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rx - 2f, topY, 4f, hPx), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(lx - 2f, topY - 2f, rx - lx + 4f, 4f), Texture2D.whiteTexture);

            // Handles: corners and the three movable edges (the goal line is not one).
            GUI.color = UITheme.Gold;
            Handle(lx, topY); Handle(rx, topY);
            Handle(lx, (topY + groundY) * 0.5f); Handle(rx, (topY + groundY) * 0.5f); Handle(cx, topY);
            GUI.color = pc;

            // Readouts: height beside the crossbar on the left, width under the left post.
            var read = _readSt ??= new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Ink } };
            UITheme.Label(new Rect(lx - labelRoom - 2f, topY - 10f, labelRoom - 6f, 20f), height.ToString("0.00") + " m", read);
            var readL = _readLSt ??= new GUIStyle(read) { alignment = TextAnchor.MiddleLeft };
            UITheme.Label(new Rect(lx - 2f, groundY + 4f, 90f, 20f), width.ToString("0.00") + " m", readL);

            // The keeper: 1.8 m tall at this scale, feet on the goal line, standing just outside the
            // right post. He moves ONLY sideways with the post - never up, never resized.
            DrawKeeper(rx + 0.35f * scale, groundY, scale);

            // ---- goalkeeper difficulty, under the goal ----
            float ky = box.yMax + (framed ? 10f : 14f);
            var klbl = _klblSt ??= new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = UITheme.Ink } };
            UITheme.Label(new Rect(p.x + inset, ky, 120f, 22f), "Goalkeeper:", klbl);
            var names = SimConfig.AiLevelNames;
            float bx = p.x + inset, bw = p.width - inset * 2f, each = (bw - 6f * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                bool sel = i == keeperLevel;
                var st = sel
                    ? (_kBtnSelSt ??= new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Gold } })
                    : (_kBtnSt ??= new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Normal });
                if (UITheme.Toggle(new Rect(bx + i * (each + 6f), ky + 24f, each, 28f), names[i], sel, st))
                    keeperLevel = i;
            }

            return !Mathf.Approximately(width, w0) || !Mathf.Approximately(height, h0) || keeperLevel != k0;
        }

        static void Handle(float x, float y)
            => GUI.DrawTexture(new Rect(x - 4f, y - 4f, 8f, 8f), Texture2D.whiteTexture);

        // Grab a side or a corner and drag. The dragged edge lands under the mouse: a post drag
        // sets the width so that post sits at the cursor (the other mirrors, the goal stays
        // centred), the crossbar sets the height from the fixed goal line, a corner does both.
        void HandleDrag(float cx, float groundY, float scale, ref float width, ref float height)
        {
            Event e = Event.current;
            float halfPx = width * 0.5f * scale, hPx = height * scale;
            float lx = cx - halfPx, rx = cx + halfPx, topY = groundY - hPx;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _drag = Hit(e.mousePosition, lx, rx, topY, groundY);
                if (_drag != 0) e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _drag != 0)
            {
                Vector2 m = e.mousePosition;
                if (_drag == 1 || _drag == 4) width = (cx - m.x) * 2f / scale;
                if (_drag == 2 || _drag == 5) width = (m.x - cx) * 2f / scale;
                if (_drag >= 3)               height = (groundY - m.y) / scale;
                width  = Mathf.Clamp(width,  MinW, MaxW);
                height = Mathf.Clamp(height, MinH, MaxH);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0) _drag = 0;
        }

        static int Hit(Vector2 m, float lx, float rx, float topY, float groundY)
        {
            const float grab = 11f;
            if (Mathf.Abs(m.x - lx) < grab && Mathf.Abs(m.y - topY) < grab) return 4;
            if (Mathf.Abs(m.x - rx) < grab && Mathf.Abs(m.y - topY) < grab) return 5;
            bool inX = m.x > lx - grab && m.x < rx + grab, inY = m.y > topY - grab && m.y < groundY;
            if (Mathf.Abs(m.y - topY) < grab && inX) return 3;
            if (Mathf.Abs(m.x - lx) < grab && inY) return 1;
            if (Mathf.Abs(m.x - rx) < grab && inY) return 2;
            return 0;
        }

        // A blocky keeper in his ready stance, to scale. `x` is his left edge.
        static void DrawKeeper(float x, float groundY, float m)
        {
            var pc = GUI.color;
            Color kit = new Color(0.95f, 0.85f, 0.25f, 0.95f);   // the AI keeper's yellow
            Color skin = new Color(0.85f, 0.68f, 0.55f, 0.95f);
            Color shorts = new Color(0.25f, 0.25f, 0.3f, 0.95f);
            float ck = x + 0.32f * m;   // centre line
            // Head, torso, arms out to the sides at shoulder height, legs slightly apart.
            GUI.color = skin;
            GUI.DrawTexture(new Rect(ck - 0.12f * m, groundY - 1.80f * m, 0.24f * m, 0.24f * m), Texture2D.whiteTexture);
            GUI.color = kit;
            GUI.DrawTexture(new Rect(ck - 0.21f * m, groundY - 1.54f * m, 0.42f * m, 0.60f * m), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(ck - 0.62f * m, groundY - 1.50f * m, 0.41f * m, 0.13f * m), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(ck + 0.21f * m, groundY - 1.50f * m, 0.41f * m, 0.13f * m), Texture2D.whiteTexture);
            GUI.color = shorts;
            GUI.DrawTexture(new Rect(ck - 0.21f * m, groundY - 0.96f * m, 0.42f * m, 0.24f * m), Texture2D.whiteTexture);
            GUI.color = skin;
            GUI.DrawTexture(new Rect(ck - 0.20f * m, groundY - 0.74f * m, 0.16f * m, 0.74f * m), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(ck + 0.04f * m, groundY - 0.74f * m, 0.16f * m, 0.74f * m), Texture2D.whiteTexture);
            GUI.color = pc;
        }
    }

    /// <summary>
    /// Push a goal size + keeper difficulty at a LIVE match. Everything that reads them does so
    /// every frame off the statics (goal detection, the AI keeper), except the goal's own frame,
    /// net and backstops, which Arena rebuilds in place. Hosting: the values ride the match
    /// config to every client (RosterSync), and NetStrikerMatch rebuilds their goal on arrival.
    /// </summary>
    public static class GoalSetup
    {
        public static int KeeperLevel => SimConfig.NearestAiLevel(SimConfig.KeeperAbility);

        public static void Apply(float width, float height, int keeperLevel)
        {
            keeperLevel = Mathf.Clamp(keeperLevel, 0, SimConfig.AiLevelAbility.Length - 1);
            bool resized = !Mathf.Approximately(width, SimConfig.GoalWidth) || !Mathf.Approximately(height, SimConfig.GoalHeight);
            SimConfig.GoalWidth  = Mathf.Clamp(width,  GoalEditor.MinW, GoalEditor.MaxW);
            SimConfig.GoalHeight = Mathf.Clamp(height, GoalEditor.MinH, GoalEditor.MaxH);
            SimConfig.KeeperAbility = SimConfig.AiLevelAbility[keeperLevel];
            if (resized) Arena.RebuildGoal();

            var s = Multiplayer.Session;
            if (s != null && s.IsHost)
            {
                var cfg = s.Config;
                cfg.goalScale     = SimConfig.GoalWidth  / SimConfig.GoalWidthBase;
                cfg.goalScaleH    = SimConfig.GoalHeight / SimConfig.GoalHeightBase;
                cfg.keeperAbility = SimConfig.KeeperAbility;
                s.SetConfig(cfg);
            }
        }
    }
}
