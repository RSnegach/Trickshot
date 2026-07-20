using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Draws the player's attribute card: a FIFA-style radar (spider) chart of the 0-100
    /// stats with concentric guide rings + axis labels, and an optional numeric list below.
    /// Pure IMGUI, reads PlayerProfile.StatCard. Used on the prematch screen and live on the
    /// customize skill tab (so the shape updates as nodes are bought).
    /// </summary>
    public static class StatRadar
    {
        // Draw the radar centred in `area`. Reads PlayerProfile.StatCard.
        public static void Draw(Rect area)
        {
            var stats = PlayerProfile.StatCard;
            int n = stats.Length;
            Vector2 c = area.center;
            float radius = Mathf.Min(area.width, area.height) * 0.5f - 26f;

            var prev = GUI.color;

            // Concentric guide rings (25/50/75/100).
            GUI.color = new Color(1f, 1f, 1f, 0.14f);
            for (int r = 1; r <= 4; r++) Polygon(c, radius * r / 4f, n, 1.5f);
            // Spokes.
            for (int i = 0; i < n; i++)
            {
                Vector2 p = Axis(c, radius, i, n);
                Line(c, p, 1.5f);
            }

            // Stat polygon (filled + outline). Blue like the screenshot.
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float t = Mathf.Clamp01(stats[i].value / 100f);
                pts[i] = Axis(c, radius * t, i, n);
            }
            GUI.color = new Color(0.30f, 0.62f, 0.95f, 0.32f);
            FillFan(c, pts);
            GUI.color = new Color(0.45f, 0.75f, 1f, 0.95f);
            for (int i = 0; i < n; i++) Line(pts[i], pts[(i + 1) % n], 2.4f);
            // Vertex dots.
            for (int i = 0; i < n; i++) Dot(pts[i], 3f);

            GUI.color = prev;

            // Axis labels just outside each spoke.
            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            for (int i = 0; i < n; i++)
            {
                Vector2 p = Axis(c, radius + 16f, i, n);
                GUI.Label(new Rect(p.x - 22f, p.y - 9f, 44f, 18f), stats[i].label, lbl);
            }
        }

        // Numeric stat list (label + coloured value), stacked from `top`. Returns bottom y.
        public static float DrawList(float x, float top, float w)
        {
            var stats = PlayerProfile.StatCard;
            var key = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.86f, 0.9f) } };
            var val = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            string[] names = { "Pace", "Shooting", "Passing", "Physical", "Dribbling", "Agility", "Heading" };
            float y = top, rowH = 22f;
            for (int i = 0; i < stats.Length; i++)
            {
                GUI.Label(new Rect(x, y, w, 20f), i < names.Length ? names[i] : stats[i].label, key);
                val.normal.textColor = RateColor(stats[i].value);
                GUI.Label(new Rect(x, y, w, 20f), stats[i].value.ToString(), val);
                y += rowH;
            }
            return y;
        }

        static Color RateColor(int v)
            => v >= 80 ? new Color(0.3f, 0.85f, 0.4f)
             : v >= 60 ? new Color(0.7f, 0.9f, 0.4f)
             : v >= 40 ? new Color(0.95f, 0.8f, 0.35f)
             : new Color(0.9f, 0.5f, 0.35f);

        // ---- primitives (rotated-texture lines, like the skill-tree/icon drawers) ----
        static Vector2 Axis(Vector2 c, float r, int i, int n)
        {
            float ang = (-90f + 360f / n * i) * Mathf.Deg2Rad;   // start at top, clockwise
            return new Vector2(c.x + Mathf.Cos(ang) * r, c.y + Mathf.Sin(ang) * r);
        }

        static void Polygon(Vector2 c, float r, int n, float w)
        {
            Vector2 prev = Axis(c, r, 0, n);
            for (int i = 1; i <= n; i++) { Vector2 p = Axis(c, r, i % n, n); Line(prev, p, w); prev = p; }
        }

        static void Line(Vector2 a, Vector2 b, float w)
        {
            Vector2 d = b - a; float len = d.magnitude;
            if (len < 0.01f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - w * 0.5f, len, w), Texture2D.whiteTexture);
            GUI.matrix = m;
        }

        static void Dot(Vector2 p, float r)
            => GUI.DrawTexture(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), Texture2D.whiteTexture);

        // Filled polygon as a triangle fan from the centre (approximate; the stat polygon is
        // star-convex about the centre, so a fan fills it correctly).
        static void FillFan(Vector2 c, Vector2[] pts)
        {
            int n = pts.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % n];
                // Draw the triangle (c, a, b) as a stack of thin lines from c toward the a-b edge.
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b));
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 edge = Vector2.Lerp(a, b, s / (float)Mathf.Max(1, steps));
                    Line(c, edge, 1.6f);
                }
            }
        }
    }
}
