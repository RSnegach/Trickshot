using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Top-down placement map for the host to set a free kick: drops TWO markers on the near
    /// half of the pitch - the BALL SPOT (gold) where the kick is taken, and the WALL (red)
    /// centre. Same striped-pitch aesthetic as CrossMap, but a deeper band (a free kick can be
    /// ~20 m out). A click moves whichever marker `editing` selects (0 = ball, 1 = wall).
    /// World<->map mapping matches CrossMap's convention: x across the width, z running from the
    /// goal line back into the pitch.
    /// </summary>
    public static class SetPieceMap
    {
        const float DepthShown = 26f;                       // metres of pitch shown, goal line -> out
        static float HalfShown => SimConfig.GoalWidth * 0.5f + 6f;

        static readonly Color Grass    = new Color(0.17f, 0.44f, 0.20f, 0.98f);
        static readonly Color GrassAlt = new Color(0.15f, 0.40f, 0.18f, 0.98f);
        static readonly Color Line     = new Color(0.95f, 0.97f, 0.95f, 0.9f);
        static readonly Color Gold     = new Color(1f, 0.85f, 0.25f);
        static readonly Color WallCol  = new Color(0.95f, 0.35f, 0.30f);

        // Draw the map into `rect`. ballSpot/wallPos are world points (y ignored); a click moves
        // the one `editing` selects. Returns true if a marker moved this frame.
        public static bool Draw(Rect rect, ref Vector3 ballSpot, ref Vector3 wallPos, int editing)
        {
            var prev = GUI.color;
            float t = Time.unscaledTime;

            // Striped pitch.
            const int stripes = 8;
            float bandH = rect.height / stripes;
            for (int i = 0; i < stripes; i++)
            {
                GUI.color = (i & 1) == 0 ? Grass : GrassAlt;
                GUI.DrawTexture(new Rect(rect.x, rect.y + i * bandH, rect.width, bandH + 1f), Texture2D.whiteTexture);
            }

            // Frame.
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            DrawRectOutline(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), 3f);
            GUI.color = Line; DrawRectOutline(rect, 2f);

            // Goal mouth + posts along the top.
            float goalPxHalf = (SimConfig.GoalWidth * 0.5f / HalfShown) * (rect.width * 0.5f);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(rect.center.x - goalPxHalf, rect.y - 3f, goalPxHalf * 2f, 6f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.center.x - goalPxHalf, rect.y, 3f, 9f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.center.x + goalPxHalf - 3f, rect.y, 3f, 9f), Texture2D.whiteTexture);

            // Penalty box guide. Its FRONT edge is the free-kick boundary: the ball must be
            // placed OUTSIDE (below) it. Shade the box faintly red as a no-go for the ball.
            float boxFrontZ = SimConfig.GoalCenter.z - SimConfig.PenaltyBoxDepth;
            float boxBottomY = WorldToMap(rect, new Vector3(0f, 0f, boxFrontZ)).y;
            float boxHalf = goalPxHalf * 2.6f;
            GUI.color = new Color(0.9f, 0.3f, 0.3f, 0.14f);
            GUI.DrawTexture(new Rect(rect.center.x - boxHalf, rect.y, boxHalf * 2f, boxBottomY - rect.y), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            DrawRectOutline(new Rect(rect.center.x - boxHalf, rect.y, boxHalf * 2f, boxBottomY - rect.y), 1.5f);
            GUI.color = prev;

            // Click to place the selected marker.
            bool moved = false;
            Event e = Event.current;
            bool hovering = rect.Contains(e.mousePosition);
            if (hovering && e.type == EventType.MouseDown && e.button == 0)
            {
                Vector3 p = MapToWorld(rect, e.mousePosition);
                if (editing == 1) wallPos = p;
                else ballSpot = ClampOutsideBox(p);   // a free kick must be taken OUTSIDE the box
                moved = true;
                e.Use();
            }

            // Draw both markers; the one being edited pulses brighter.
            DrawMarker(rect, ballSpot, Gold, editing == 0, t, "BALL");
            DrawMarker(rect, wallPos, WallCol, editing == 1, t, "WALL");

            // A faint line from ball to wall so the host reads the wall's offset at a glance.
            GUI.color = new Color(1f, 1f, 1f, 0.3f);
            var a = WorldToMap(rect, ballSpot); var b = WorldToMap(rect, wallPos);
            DrawDottedLine(a, b, 4f);
            GUI.color = prev;

            // Live hover reticle following the mouse.
            if (hovering && e.type == EventType.Repaint)
            {
                float hp = 0.5f + 0.5f * Mathf.Sin(t * 7f);
                Color hc = editing == 1 ? WallCol : Gold;
                DrawReticle(e.mousePosition, 12f + hp * 4f, new Color(hc.r, hc.g, hc.b, 0.5f), false);
            }

            GUI.color = prev;
            return moved;
        }

        // A free kick can't be taken from inside the penalty box: push the z out to the box
        // front edge if the host clicks inside it. (x is unconstrained.)
        public static Vector3 ClampOutsideBox(Vector3 p)
        {
            float boxFrontZ = SimConfig.GoalCenter.z - SimConfig.PenaltyBoxDepth;
            if (p.z > boxFrontZ) p.z = boxFrontZ;   // higher z = nearer goal = inside the box
            return p;
        }

        static Vector3 MapToWorld(Rect rect, Vector2 m)
        {
            float fx = Mathf.Clamp01((m.x - rect.x) / rect.width);
            float fy = Mathf.Clamp01((m.y - rect.y) / rect.height);
            return new Vector3(Mathf.Lerp(-HalfShown, HalfShown, fx), 0f,
                               SimConfig.GoalCenter.z - Mathf.Lerp(0f, DepthShown, fy));
        }

        static Vector2 WorldToMap(Rect rect, Vector3 w)
        {
            float fx = Mathf.InverseLerp(-HalfShown, HalfShown, w.x);
            float fy = Mathf.InverseLerp(0f, DepthShown, SimConfig.GoalCenter.z - w.z);
            return new Vector2(rect.x + fx * rect.width, rect.y + fy * rect.height);
        }

        static void DrawMarker(Rect rect, Vector3 world, Color col, bool active, float t, string label)
        {
            var c = WorldToMap(rect, world);
            float pulse = active ? 0.5f + 0.5f * Mathf.Sin(t * 5f) : 0.25f;
            DrawReticle(c, 14f + pulse * 6f, new Color(col.r, col.g, col.b, 0.55f + 0.45f * pulse), true);
            var lab = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = col } };
            var pc = GUI.color; GUI.color = Color.white;
            GUI.Label(new Rect(c.x - 24f, c.y + 10f, 48f, 16f), label, lab);
            GUI.color = pc;
        }

        static void DrawReticle(Vector2 c, float r, Color col, bool filled)
        {
            var prev = GUI.color;
            GUI.color = col;
            DrawCircle(c, r, 2f, 26);
            float gap = r * 0.45f, len = r * 0.55f;
            GUI.DrawTexture(new Rect(c.x - 1f, c.y - r - 3f, 2f, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(c.x - 1f, c.y + gap, 2f, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(c.x - r - 3f, c.y - 1f, len, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(c.x + gap, c.y - 1f, len, 2f), Texture2D.whiteTexture);
            if (filled) GUI.DrawTexture(new Rect(c.x - 3f, c.y - 3f, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        static void DrawCircle(Vector2 c, float r, float thick, int segs)
        {
            for (int i = 0; i < segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                var p = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                GUI.DrawTexture(new Rect(p.x - thick * 0.5f, p.y - thick * 0.5f, thick, thick), Texture2D.whiteTexture);
            }
        }

        static void DrawDottedLine(Vector2 a, Vector2 b, float step)
        {
            float d = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(d / step));
            for (int i = 0; i <= n; i += 2)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                GUI.DrawTexture(new Rect(p.x - 1f, p.y - 1f, 2f, 2f), Texture2D.whiteTexture);
            }
        }

        static void DrawRectOutline(Rect r, float th)
        {
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, th), tex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - th, r.width, th), tex);
            GUI.DrawTexture(new Rect(r.x, r.y, th, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - th, r.y, th, r.height), tex);
        }
    }
}
