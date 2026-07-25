using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Top-down map for choosing where a cross lands (and, in SP, where the AI crosser stands).
    /// Shows the ENTIRE attacking half of the pitch: from the goal line (map top) back to the
    /// halfway line (map bottom), full touchline-to-touchline width. Every marking - goal mouth,
    /// six-yard + penalty boxes, penalty spot + arc, halfway line, centre circle, touchlines - is
    /// drawn at its REAL world coordinate, mapped through the SAME world<->map transform the click
    /// handler uses. That is what makes a click land exactly where it looks: the pixel you click and
    /// the marking you clicked on both resolve through one transform, so placing on the drawn penalty
    /// spot puts the target on the real penalty spot.
    ///
    /// The pitch is regulation size (105x68), so the attacking half is 52.5m deep x 68m wide. The x
    /// and z axes therefore map at different pixel scales; circles/arcs are sampled in WORLD space
    /// and mapped point-by-point so they render as the correct (slightly elliptical) shape rather
    /// than a naive screen circle. A live hover reticle follows the mouse; a click drops the marker.
    /// </summary>
    public static class CrossMap
    {
        // --- Attacking-half extent (world metres), read live so mutable goal size stays honest ---
        static float TopZ    => SimConfig.GoalCenter.z;      // map top edge = attacking goal line
        static float BottomZ => PitchLayout.PitchCenterZ;    // map bottom edge = halfway line
        static float HalfW   => PitchLayout.HalfWidth;       // touchline half-width (x = +/-HalfW)
        const float TargetY  = 0.25f;                        // world y stored for a placed target

        static readonly Color Grass     = new Color(0.17f, 0.44f, 0.20f, 0.98f);
        static readonly Color GrassAlt  = new Color(0.15f, 0.40f, 0.18f, 0.98f);
        static readonly Color Line      = new Color(0.95f, 0.97f, 0.95f, 0.9f);
        static readonly Color LineSoft   = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Gold      = new Color(1f, 0.85f, 0.25f);
        static readonly Color HoverCol  = new Color(0.55f, 0.9f, 1f);
        static readonly Color CrosserCol = new Color(0.4f, 0.7f, 1f);   // crosser placement icon

        // Regulation attacking-half markings (metres), matching PitchBuilder's painted pitch.
        const float PenaltyBoxHalfW = 20.15f, PenaltyBoxDepth = 16.5f;
        const float SixYardHalfW    = 9.15f,  SixYardDepth    = 5.5f;
        const float PenaltySpotDist = 11f;    // out from the goal line
        const float PenaltyArcRadius = 9.15f;
        const float CentreCircleRadius = 9.15f;

        // Draw the map filling `rect`. Reads/writes `target` (world). Returns true if the
        // marker was moved this frame. `interactive` gates click handling + the hover reticle.
        // Target-only overload (callers that don't place a crosser).
        public static bool Draw(Rect rect, ref Vector3 target, bool interactive)
        {
            Vector3 dummy = Vector3.zero;
            return Draw(rect, ref target, ref dummy, interactive, editing: 0, showCrosser: false);
        }

        // Full overload: place the cross TARGET (editing 0) and/or the CROSSER spot (editing 1).
        // showCrosser draws + enables the crosser marker (skip it for a human crosser).
        public static bool Draw(Rect rect, ref Vector3 target, ref Vector3 crosserSpot,
                                bool interactive, int editing, bool showCrosser = true)
        {
            var prev = GUI.color;
            float t = Time.unscaledTime;

            // --- Pitch: mowed stripes (alternating horizontal bands). More bands for the deeper map. ---
            const int stripes = 11;
            float bandH = rect.height / stripes;
            for (int i = 0; i < stripes; i++)
            {
                GUI.color = (i & 1) == 0 ? Grass : GrassAlt;
                GUI.DrawTexture(new Rect(rect.x, rect.y + i * bandH, rect.width, bandH + 1f), Texture2D.whiteTexture);
            }

            // --- Outer frame = touchlines (sides) + goal line (top) + halfway line (bottom) ---
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            DrawRectOutline(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), 3f);
            GUI.color = Line;
            DrawRectOutline(rect, 2f);

            // --- Boxes (drawn at real world coords through WorldToMap) ---
            GUI.color = LineSoft;
            WBoxOutline(rect, -PenaltyBoxHalfW, PenaltyBoxHalfW, TopZ - PenaltyBoxDepth, TopZ, 1.5f);
            WBoxOutline(rect, -SixYardHalfW, SixYardHalfW, TopZ - SixYardDepth, TopZ, 1.5f);

            // --- Penalty spot + arc (arc bulges INTO the field = toward -Z = down the map) ---
            float spotZ = TopZ - PenaltySpotDist;
            var spotMap = WorldToMap(rect, new Vector3(0f, 0f, spotZ));
            GUI.color = Line;
            GUI.DrawTexture(new Rect(spotMap.x - 2f, spotMap.y - 2f, 4f, 4f), Texture2D.whiteTexture);
            // The arc is the part of the 9.15m circle around the spot beyond the box front line.
            float boxFrontZ = TopZ - PenaltyBoxDepth;
            float half = Mathf.Acos(Mathf.Clamp((PenaltyBoxDepth - PenaltySpotDist) / PenaltyArcRadius, -1f, 1f)) * Mathf.Rad2Deg;
            // World angle convention here: 0deg = +X, 90 = +Z (toward goal), 270 = -Z (into field).
            WorldArc(rect, 0f, spotZ, PenaltyArcRadius, 270f - half, 270f + half, 20, Line);

            // --- Centre circle at the halfway line (only the half inside the map is visible) ---
            WorldArc(rect, 0f, BottomZ, CentreCircleRadius, 0f, 180f, 28, LineSoft);
            var midMap = WorldToMap(rect, new Vector3(0f, 0f, BottomZ));
            GUI.color = Line;
            GUI.DrawTexture(new Rect(midMap.x - 2f, midMap.y - 2f, 4f, 4f), Texture2D.whiteTexture);

            // --- Goal mouth: bright bar + posts along the top edge, from live goal width ---
            var goalL = WorldToMap(rect, new Vector3(-SimConfig.GoalWidth * 0.5f, 0f, TopZ));
            var goalR = WorldToMap(rect, new Vector3( SimConfig.GoalWidth * 0.5f, 0f, TopZ));
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(goalL.x, rect.y - 3f, goalR.x - goalL.x, 5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(goalL.x, rect.y, 3f, 8f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(goalR.x - 3f, rect.y, 3f, 8f), Texture2D.whiteTexture);
            GUI.color = prev;

            // --- Click to place whichever marker `editing` selects (0 = target, 1 = crosser) ---
            bool moved = false;
            Event e = Event.current;
            bool hovering = interactive && rect.Contains(e.mousePosition);
            if (hovering && e.type == EventType.MouseDown && e.button == 0)
            {
                Vector3 p = MapToWorld(rect, e.mousePosition);
                if (showCrosser && editing == 1) crosserSpot = p; else target = p;
                moved = true;
                e.Use();
            }

            // --- Placed target marker: gold pulsing reticle (smaller now) ---
            var mc = WorldToMap(rect, target);
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4f);
            bool targetActive = !showCrosser || editing == 0;
            DrawReticle(mc, 9f + (targetActive ? pulse * 3f : 0f), Gold,
                        ringAlpha: targetActive ? 0.5f + 0.5f * pulse : 0.4f, filled: true);

            // --- Crosser marker: a small player icon (distinct blue), pulses when being edited ---
            if (showCrosser)
            {
                var cc = WorldToMap(rect, crosserSpot);
                bool crosserActive = editing == 1;
                DrawPlayerIcon(cc, CrosserCol, crosserActive ? 0.55f + 0.45f * pulse : 0.5f);
            }

            // --- Live hover reticle following the mouse (colour of the marker being placed) ---
            if (hovering && e.type == EventType.Repaint)
            {
                float hp = 0.5f + 0.5f * Mathf.Sin(t * 7f);
                Color hc = (showCrosser && editing == 1) ? CrosserCol : HoverCol;
                DrawReticle(e.mousePosition, 8f + hp * 3f, hc, ringAlpha: 0.35f + 0.4f * hp, filled: false);
            }

            GUI.color = prev;
            return moved;
        }

        // world <-> map helpers. x across the width (touchline to touchline), z from the goal line
        // (map top) back to the halfway line (map bottom). SINGLE source of truth for clicks + markings.
        static Vector3 MapToWorld(Rect rect, Vector2 m)
        {
            float fx = Mathf.Clamp01((m.x - rect.x) / rect.width);
            float fy = Mathf.Clamp01((m.y - rect.y) / rect.height);
            return new Vector3(Mathf.Lerp(-HalfW, HalfW, fx), TargetY,
                               Mathf.Lerp(TopZ, BottomZ, fy));
        }
        static Vector2 WorldToMap(Rect rect, Vector3 w)
        {
            float fx = Mathf.InverseLerp(-HalfW, HalfW, w.x);
            float fy = Mathf.InverseLerp(TopZ, BottomZ, w.z);
            return new Vector2(rect.x + fx * rect.width, rect.y + fy * rect.height);
        }

        // A world-axis-aligned box outline, mapped to the (anisotropic) screen rect.
        static void WBoxOutline(Rect rect, float minX, float maxX, float minZ, float maxZ, float th)
        {
            var a = WorldToMap(rect, new Vector3(minX, 0f, maxZ));   // near-left (toward goal = top)
            var b = WorldToMap(rect, new Vector3(maxX, 0f, minZ));   // far-right (toward halfway = bottom)
            var r = new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                             Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            DrawRectOutline(r, th);
        }

        // A tiny stylised player: head dot + body, in the crosser colour.
        static void DrawPlayerIcon(Vector2 c, Color col, float alpha)
        {
            var prev = GUI.color;
            GUI.color = new Color(col.r, col.g, col.b, alpha);
            GUI.DrawTexture(new Rect(c.x - 2f, c.y - 6f, 4f, 4f), Texture2D.whiteTexture);   // head
            GUI.DrawTexture(new Rect(c.x - 3f, c.y - 1f, 6f, 7f), Texture2D.whiteTexture);   // body
            GUI.color = prev;
        }

        // A crosshair reticle: an outer ring (drawn as short segments), a center dot, and cross ticks.
        static void DrawReticle(Vector2 c, float r, Color col, float ringAlpha, bool filled)
        {
            var prev = GUI.color;
            // Ring (segmented circle).
            GUI.color = new Color(col.r, col.g, col.b, ringAlpha);
            DrawCircle(c, r, 2f, 24);
            // Crosshair ticks (leave a gap at the centre).
            float gap = r * 0.45f, len = r * 0.55f;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - 1f, c.y - r - 3f, 2f, len), Texture2D.whiteTexture);          // top
            GUI.DrawTexture(new Rect(c.x - 1f, c.y + gap, 2f, len), Texture2D.whiteTexture);              // bottom
            GUI.DrawTexture(new Rect(c.x - r - 3f, c.y - 1f, len, 2f), Texture2D.whiteTexture);           // left
            GUI.DrawTexture(new Rect(c.x + gap, c.y - 1f, len, 2f), Texture2D.whiteTexture);              // right
            // Centre dot.
            if (filled) { GUI.color = col; GUI.DrawTexture(new Rect(c.x - 2f, c.y - 2f, 4f, 4f), Texture2D.whiteTexture); }
            GUI.color = prev;
        }

        // Approximate a circle outline with short segment quads.
        static void DrawCircle(Vector2 c, float r, float thick, int segs)
        {
            for (int i = 0; i < segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                var p = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                GUI.DrawTexture(new Rect(p.x - thick * 0.5f, p.y - thick * 0.5f, thick, thick), Texture2D.whiteTexture);
            }
        }

        // A partial arc sampled in WORLD space (metres) and mapped point-by-point, so it renders with
        // the map's true x/z scale (an ellipse), and only the part inside the map is drawn. Angle: 0
        // = +X, 90 = +Z (toward goal), 270 = -Z (into the field, down the map).
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
                if (wz < BottomZ || wz > TopZ || wx < -HalfW || wx > HalfW) continue;   // clip to the half
                var p = WorldToMap(rect, new Vector3(wx, 0f, wz));
                GUI.DrawTexture(new Rect(p.x - thick * 0.5f, p.y - thick * 0.5f, thick, thick), Texture2D.whiteTexture);
            }
            GUI.color = prev;
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
