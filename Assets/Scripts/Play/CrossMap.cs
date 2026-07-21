using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Reusable top-down penalty-box map for choosing where a cross lands. Draws a striped
    /// pitch with the goal mouth along the top edge, the six-yard box, the penalty spot + arc,
    /// and a marker at the chosen target. While the cursor is over the map a live, animated
    /// reticle follows the mouse (a pulsing ring + crosshair); a click drops the target there
    /// and the placed marker gets its own animated pulse. Shared by the in-match overlay.
    /// </summary>
    public static class CrossMap
    {
        // Pitch band shown on the map: full goal width + margin across, this deep out.
        const float DepthShown = 18f;
        static float HalfShown => SimConfig.GoalWidth * 0.5f + 3f;

        static readonly Color Grass     = new Color(0.17f, 0.44f, 0.20f, 0.98f);
        static readonly Color GrassAlt  = new Color(0.15f, 0.40f, 0.18f, 0.98f);
        static readonly Color Line      = new Color(0.95f, 0.97f, 0.95f, 0.9f);
        static readonly Color Gold      = new Color(1f, 0.85f, 0.25f);
        static readonly Color HoverCol  = new Color(0.55f, 0.9f, 1f);

        // Draw the map filling `rect`. Reads/writes `target` (world). Returns true if the
        // marker was moved this frame. `interactive` gates click handling + the hover reticle.
        public static bool Draw(Rect rect, ref Vector3 target, bool interactive)
        {
            var prev = GUI.color;
            float t = Time.unscaledTime;

            // --- Pitch: mowed stripes (alternating horizontal bands) ---
            const int stripes = 7;
            float bandH = rect.height / stripes;
            for (int i = 0; i < stripes; i++)
            {
                GUI.color = (i & 1) == 0 ? Grass : GrassAlt;
                GUI.DrawTexture(new Rect(rect.x, rect.y + i * bandH, rect.width, bandH + 1f), Texture2D.whiteTexture);
            }

            // --- Outer frame (double line: dark drop then bright edge) ---
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            DrawRectOutline(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), 3f);
            GUI.color = Line;
            DrawRectOutline(rect, 2f);

            // --- Goal mouth: posts + bar along the top edge ---
            float goalPxHalf = (SimConfig.GoalWidth * 0.5f / HalfShown) * (rect.width * 0.5f);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(rect.center.x - goalPxHalf, rect.y - 3f, goalPxHalf * 2f, 6f), Texture2D.whiteTexture);
            // little posts hanging down
            GUI.DrawTexture(new Rect(rect.center.x - goalPxHalf, rect.y, 3f, 9f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.center.x + goalPxHalf - 3f, rect.y, 3f, 9f), Texture2D.whiteTexture);

            // --- Six-yard box + penalty box guides ---
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            float sixHalf = goalPxHalf * 1.5f, sixDepth = rect.height * 0.20f;
            DrawRectOutline(new Rect(rect.center.x - sixHalf, rect.y, sixHalf * 2f, sixDepth), 1.5f);
            float boxHalf = goalPxHalf * 2.6f, boxDepth = rect.height * 0.5f;
            DrawRectOutline(new Rect(rect.center.x - boxHalf, rect.y, boxHalf * 2f, boxDepth), 1.5f);

            // Penalty spot + D-arc.
            float spotY = rect.y + rect.height * 0.34f;
            GUI.color = Line;
            GUI.DrawTexture(new Rect(rect.center.x - 2f, spotY - 2f, 4f, 4f), Texture2D.whiteTexture);
            DrawArc(new Vector2(rect.center.x, spotY), rect.width * 0.14f, 200f, 340f, 1.5f, Line);
            GUI.color = prev;

            // --- Click to place the target ---
            bool moved = false;
            Event e = Event.current;
            bool hovering = interactive && rect.Contains(e.mousePosition);
            if (hovering && e.type == EventType.MouseDown && e.button == 0)
            {
                float fx = Mathf.Clamp01((e.mousePosition.x - rect.x) / rect.width);
                float fy = Mathf.Clamp01((e.mousePosition.y - rect.y) / rect.height);
                target = new Vector3(Mathf.Lerp(-HalfShown, HalfShown, fx), 0.25f,
                                     SimConfig.GoalCenter.z - Mathf.Lerp(0f, DepthShown, fy));
                moved = true;
                e.Use();
            }

            // --- Placed target marker: gold pulsing ring + crosshair + soft shadow ---
            float mfx = Mathf.InverseLerp(-HalfShown, HalfShown, target.x);
            float mfy = Mathf.InverseLerp(0f, DepthShown, SimConfig.GoalCenter.z - target.z);
            var mc = new Vector2(rect.x + mfx * rect.width, rect.y + mfy * rect.height);
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4f);
            DrawReticle(mc, 16f + pulse * 6f, Gold, ringAlpha: 0.5f + 0.5f * pulse, filled: true);

            // --- Live hover reticle following the mouse (until you click to place) ---
            if (hovering && e.type == EventType.Repaint)
            {
                float hp = 0.5f + 0.5f * Mathf.Sin(t * 7f);
                DrawReticle(e.mousePosition, 13f + hp * 4f, HoverCol, ringAlpha: 0.35f + 0.4f * hp, filled: false);
            }

            GUI.color = prev;
            return moved;
        }

        // A crosshair reticle: an outer ring (drawn as 4 arcs), a center dot, and cross ticks.
        static void DrawReticle(Vector2 c, float r, Color col, float ringAlpha, bool filled)
        {
            var prev = GUI.color;
            // Ring (segmented circle).
            GUI.color = new Color(col.r, col.g, col.b, ringAlpha);
            DrawCircle(c, r, 2f, 28);
            // Crosshair ticks (leave a gap at the centre).
            float gap = r * 0.45f, len = r * 0.55f;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - 1f, c.y - r - 3f, 2f, len), Texture2D.whiteTexture);          // top
            GUI.DrawTexture(new Rect(c.x - 1f, c.y + gap, 2f, len), Texture2D.whiteTexture);              // bottom
            GUI.DrawTexture(new Rect(c.x - r - 3f, c.y - 1f, len, 2f), Texture2D.whiteTexture);           // left
            GUI.DrawTexture(new Rect(c.x + gap, c.y - 1f, len, 2f), Texture2D.whiteTexture);              // right
            // Centre dot.
            if (filled) { GUI.color = col; GUI.DrawTexture(new Rect(c.x - 3f, c.y - 3f, 6f, 6f), Texture2D.whiteTexture); }
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

        // A partial arc (degrees), for the penalty D.
        static void DrawArc(Vector2 c, float r, float fromDeg, float toDeg, float thick, Color col)
        {
            var prev = GUI.color; GUI.color = col;
            int segs = 20;
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)segs);
                var p = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
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
