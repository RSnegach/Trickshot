using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Top-down placement map for a free kick: drops TWO markers on the attacking third of the pitch
    /// - the BALL SPOT (gold) where the kick is taken (and where the shooter stands), and the WALL
    /// (red) centre. Shows the ENTIRE attacking third: from the goal line (map top) back one third of
    /// the pitch, full touchline-to-touchline width, with real line markings.
    ///
    /// Like CrossMap, every marking is drawn at its REAL world coordinate through the SAME
    /// world<->map transform the click handler uses, so a click lands EXACTLY where it looks: the
    /// placed marker and the in-world ball/wall end up on the same spot you clicked. A click moves
    /// whichever marker `editing` selects (0 = ball, 1 = wall). The ball is clamped OUTSIDE the
    /// penalty box (a free kick is taken from outside it); the wall is unconstrained within the third.
    /// </summary>
    public static class SetPieceMap
    {
        // Attacking-third extent (world metres), read live so a mutable goal size stays honest.
        // Third depth = one third of the regulation pitch length.
        static float TopZ    => SimConfig.GoalCenter.z;                 // map top = goal line
        static float ThirdDepth => PitchLayout.PitchLength / 3f;        // ~35m
        static float BottomZ => SimConfig.GoalCenter.z - ThirdDepth;    // map bottom = third line
        static float HalfW   => PitchLayout.HalfWidth;                  // touchline half-width

        static readonly Color Grass    = new Color(0.17f, 0.44f, 0.20f, 0.98f);
        static readonly Color GrassAlt = new Color(0.15f, 0.40f, 0.18f, 0.98f);
        static readonly Color Line     = new Color(0.95f, 0.97f, 0.95f, 0.9f);
        static readonly Color LineSoft = new Color(1f, 1f, 1f, 0.5f);
        static readonly Color Gold     = new Color(1f, 0.85f, 0.25f);
        static readonly Color WallCol  = new Color(0.95f, 0.35f, 0.30f);

        // Regulation attacking-half markings (metres), matching PitchBuilder's painted pitch.
        const float PenaltyBoxHalfW = 20.15f, PenaltyBoxDepth = 16.5f;
        const float SixYardHalfW    = 9.15f,  SixYardDepth    = 5.5f;
        const float PenaltySpotDist = 11f;
        const float PenaltyArcRadius = 9.15f;

        // Draw the map into `rect`. ballSpot/wallPos are world points (y ignored); a click moves
        // the one `editing` selects. Returns true if a marker moved this frame.
        public static bool Draw(Rect rect, ref Vector3 ballSpot, ref Vector3 wallPos, int editing)
        {
            var prev = GUI.color;
            float t = Time.unscaledTime;

            // Striped pitch (more bands for the deeper third).
            const int stripes = 10;
            float bandH = rect.height / stripes;
            for (int i = 0; i < stripes; i++)
            {
                GUI.color = (i & 1) == 0 ? Grass : GrassAlt;
                GUI.DrawTexture(new Rect(rect.x, rect.y + i * bandH, rect.width, bandH + 1f), Texture2D.whiteTexture);
            }

            // Frame = touchlines (sides) + goal line (top) + third line (bottom).
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            DrawRectOutline(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), 3f);
            GUI.color = Line; DrawRectOutline(rect, 2f);

            // Boxes drawn at real world coords through WorldToMap.
            GUI.color = LineSoft;
            WBoxOutline(rect, -PenaltyBoxHalfW, PenaltyBoxHalfW, TopZ - PenaltyBoxDepth, TopZ, 1.5f);
            WBoxOutline(rect, -SixYardHalfW, SixYardHalfW, TopZ - SixYardDepth, TopZ, 1.5f);

            // Penalty spot + arc (bulges into the field = down the map).
            float spotZ = TopZ - PenaltySpotDist;
            var spotMap = WorldToMap(rect, new Vector3(0f, 0f, spotZ));
            GUI.color = Line;
            GUI.DrawTexture(new Rect(spotMap.x - 2f, spotMap.y - 2f, 4f, 4f), Texture2D.whiteTexture);
            float half = Mathf.Acos(Mathf.Clamp((PenaltyBoxDepth - PenaltySpotDist) / PenaltyArcRadius, -1f, 1f)) * Mathf.Rad2Deg;
            WorldArc(rect, 0f, spotZ, PenaltyArcRadius, 270f - half, 270f + half, 20, Line);

            // Penalty box no-go shade: the ball must be placed OUTSIDE (below the box front line).
            float boxFrontY = WorldToMap(rect, new Vector3(0f, 0f, TopZ - PenaltyBoxDepth)).y;
            var boxL = WorldToMap(rect, new Vector3(-PenaltyBoxHalfW, 0f, TopZ)).x;
            var boxR = WorldToMap(rect, new Vector3( PenaltyBoxHalfW, 0f, TopZ)).x;
            GUI.color = new Color(0.9f, 0.3f, 0.3f, 0.13f);
            GUI.DrawTexture(new Rect(boxL, rect.y, boxR - boxL, boxFrontY - rect.y), Texture2D.whiteTexture);
            GUI.color = prev;

            // Goal mouth + posts along the top, from live goal width.
            var goalL = WorldToMap(rect, new Vector3(-SimConfig.GoalWidth * 0.5f, 0f, TopZ));
            var goalR = WorldToMap(rect, new Vector3( SimConfig.GoalWidth * 0.5f, 0f, TopZ));
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(goalL.x, rect.y - 3f, goalR.x - goalL.x, 5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(goalL.x, rect.y, 3f, 9f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(goalR.x - 3f, rect.y, 3f, 9f), Texture2D.whiteTexture);
            GUI.color = prev;

            // Click to place the selected marker (ball clamped outside the box).
            bool moved = false;
            Event e = Event.current;
            bool hovering = rect.Contains(e.mousePosition);
            if (hovering && e.type == EventType.MouseDown && e.button == 0)
            {
                Vector3 p = MapToWorld(rect, e.mousePosition);
                if (editing == 1) wallPos = p;
                else ballSpot = ClampOutsideBox(p);
                moved = true;
                e.Use();
            }

            // Draw both markers; the one being edited pulses brighter.
            DrawMarker(rect, ballSpot, Gold, editing == 0, t, "BALL");
            DrawMarker(rect, wallPos, WallCol, editing == 1, t, "WALL");

            // Faint dotted line from ball to wall.
            GUI.color = new Color(1f, 1f, 1f, 0.3f);
            var a = WorldToMap(rect, ballSpot); var b = WorldToMap(rect, wallPos);
            DrawDottedLine(a, b, 4f);
            GUI.color = prev;

            // Live hover reticle following the mouse.
            if (hovering && e.type == EventType.Repaint)
            {
                float hp = 0.5f + 0.5f * Mathf.Sin(t * 7f);
                Color hc = editing == 1 ? WallCol : Gold;
                DrawReticle(e.mousePosition, 9f + hp * 3f, new Color(hc.r, hc.g, hc.b, 0.5f), false);
            }

            GUI.color = prev;
            return moved;
        }

        // A free kick can't be taken from inside the penalty box: push the z out to the box front
        // edge if the click is inside it. (x is unconstrained.)
        public static Vector3 ClampOutsideBox(Vector3 p)
        {
            float boxFrontZ = SimConfig.GoalCenter.z - SimConfig.PenaltyBoxDepth;
            if (p.z > boxFrontZ) p.z = boxFrontZ;   // higher z = nearer goal = inside the box
            return p;
        }

        // world <-> map: x across the width (touchline to touchline), z from the goal line (map top)
        // back to the third line (map bottom). SINGLE source of truth for clicks + markings.
        static Vector3 MapToWorld(Rect rect, Vector2 m)
        {
            float fx = Mathf.Clamp01((m.x - rect.x) / rect.width);
            float fy = Mathf.Clamp01((m.y - rect.y) / rect.height);
            return new Vector3(Mathf.Lerp(-HalfW, HalfW, fx), 0f, Mathf.Lerp(TopZ, BottomZ, fy));
        }

        static Vector2 WorldToMap(Rect rect, Vector3 w)
        {
            float fx = Mathf.InverseLerp(-HalfW, HalfW, w.x);
            float fy = Mathf.InverseLerp(TopZ, BottomZ, w.z);
            return new Vector2(rect.x + fx * rect.width, rect.y + fy * rect.height);
        }

        // A world-axis-aligned box outline mapped to the (anisotropic) screen rect.
        static void WBoxOutline(Rect rect, float minX, float maxX, float minZ, float maxZ, float th)
        {
            var a = WorldToMap(rect, new Vector3(minX, 0f, maxZ));
            var b = WorldToMap(rect, new Vector3(maxX, 0f, minZ));
            var r = new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                             Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            DrawRectOutline(r, th);
        }

        static void DrawMarker(Rect rect, Vector3 world, Color col, bool active, float t, string label)
        {
            var c = WorldToMap(rect, world);
            float pulse = active ? 0.5f + 0.5f * Mathf.Sin(t * 5f) : 0.25f;
            DrawReticle(c, 10f + pulse * 4f, new Color(col.r, col.g, col.b, 0.55f + 0.45f * pulse), true);
            var lab = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = col } };
            var pc = GUI.color; GUI.color = Color.white;
            GUI.Label(new Rect(c.x - 24f, c.y + 8f, 48f, 16f), label, lab);
            GUI.color = pc;
        }

        static void DrawReticle(Vector2 c, float r, Color col, bool filled)
        {
            var prev = GUI.color;
            GUI.color = col;
            DrawCircle(c, r, 2f, 24);
            float gap = r * 0.45f, len = r * 0.55f;
            GUI.DrawTexture(new Rect(c.x - 1f, c.y - r - 3f, 2f, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(c.x - 1f, c.y + gap, 2f, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(c.x - r - 3f, c.y - 1f, len, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(c.x + gap, c.y - 1f, len, 2f), Texture2D.whiteTexture);
            if (filled) GUI.DrawTexture(new Rect(c.x - 2f, c.y - 2f, 4f, 4f), Texture2D.whiteTexture);
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

        // Arc sampled in WORLD space and mapped point-by-point (renders with the map's true x/z
        // scale), clipped to the third. Angle: 0 = +X, 90 = +Z (toward goal), 270 = -Z (down the map).
        static void WorldArc(Rect rect, float cx, float cz, float radiusM, float fromDeg, float toDeg,
                             int segs, Color col)
        {
            var prev = GUI.color; GUI.color = col;
            const float thick = 1.5f;
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)segs);
                float wx = cx + Mathf.Cos(a) * radiusM;
                float wz = cz + Mathf.Sin(a) * radiusM;
                if (wz < BottomZ || wz > TopZ || wx < -HalfW || wx > HalfW) continue;
                var p = WorldToMap(rect, new Vector3(wx, 0f, wz));
                GUI.DrawTexture(new Rect(p.x - thick * 0.5f, p.y - thick * 0.5f, thick, thick), Texture2D.whiteTexture);
            }
            GUI.color = prev;
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
