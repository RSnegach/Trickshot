using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The title screen's custom "K": a stick-figure silhouette mid-bicycle-kick, built the same
    /// way SkillIcons.cs builds its procedural icons (coverage-antialiased strokes/fills, max-blended
    /// into a white RGBA buffer, no image files), but in its OWN dedicated buffer rather than
    /// SkillIcons' shared 64x64 cache - that cache is sized for 46px skill-tree badges and would blur
    /// badly stretched to hero wordmark scale. 256px here comfortably covers even the most extreme
    /// MenuScale.MaxFactor(2.1) x title-fontSize(132) combination with room to spare.
    ///
    /// THE DESIGN: a literal K, not a torso standing in for one. A capital K is a vertical stem
    /// plus two diagonals meeting at one vertex - here the STEM is the figure's ARMS (one stroke
    /// straight up from the vertex, one straight down - together they trace the same line a plain
    /// K's stem would), and the two DIAGONALS are his LEGS, branching up-and-out / down-and-out from
    /// that same vertex, same as a real K's branches. A small head perches just past the raised
    /// arm's own tip (the same "sits just past the stroke's end" offset trick as everything else
    /// here), and a small ring-drawn ball arcs off the top foot - the kicking read the original
    /// design carried, kept even though the pose is more literally the letter now.
    /// </summary>
    public static class TitleGlyph
    {
        const int S = 256;

        static Texture2D _k;
        static Color32[] _buf;

        /// <summary>The cached K glyph texture. Built once, lazily, on first access.</summary>
        public static Texture2D K => _k != null ? _k : (_k = Build());

        static Texture2D Build()
        {
            _buf = new Color32[S * S];   // all zero => fully transparent
            Draw();
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "title_kick_k"
            };
            tex.SetPixels32(_buf);
            tex.Apply();
            _buf = null;
            return tex;
        }

        // ----------------------------------------------------------- pixel primitives
        // Identical to SkillIcons.cs's own (Plot/Dot/Stroke/FillPoly), just re-declared here rather
        // than exposed from that file - SkillIcons hardcodes its buffer size/scale as private
        // constants that ~40 working skill icons depend on, so this stays a self-contained sibling.
        static void Plot(int x, int y, float a)
        {
            if (x < 0 || y < 0 || x >= S || y >= S) return;
            int i = y * S + x;
            byte na = (byte)(Mathf.Clamp01(a) * 255f);
            if (na > _buf[i].a) _buf[i] = new Color32(255, 255, 255, na);   // max-blend (union)
        }

        static void Dot(float cx, float cy, float r)
        {
            int x0 = Mathf.FloorToInt(cx - r - 1f), x1 = Mathf.CeilToInt(cx + r + 1f);
            int y0 = Mathf.FloorToInt(cy - r - 1f), y1 = Mathf.CeilToInt(cy + r + 1f);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float cov = Mathf.Clamp01(r + 0.5f - d);
                    if (cov > 0f) Plot(x, y, cov);
                }
        }

        static void Stroke(float x0, float y0, float x1, float y1, float w)
        {
            float r = w * 0.5f;
            float dx = x1 - x0, dy = y1 - y0;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(dx * dx + dy * dy)));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Dot(x0 + dx * t, y0 + dy * t, r);
            }
        }

        // Outline circle (a chain of short Strokes around the circumference), for the ball - a
        // SOLID Dot would just be a blob; the seam-like cross drawn over a ring reads as a ball
        // via the same negative-space idea SkillIcons.Ball()/MenuIcons.Ball() already use.
        static void Ring(float cx, float cy, float rad, float w)
        {
            int steps = Mathf.CeilToInt(rad * 6.5f) + 8;
            float px = cx + rad, py = cy;
            for (int i = 1; i <= steps; i++)
            {
                float a = (i / (float)steps) * Mathf.PI * 2f;
                float x = cx + Mathf.Cos(a) * rad, y = cy + Mathf.Sin(a) * rad;
                Stroke(px, py, x, y, w); px = x; py = y;
            }
        }

        static void FillPoly(params float[] p)
        {
            int n = p.Length / 2;
            if (n < 3) return;
            float miny = float.MaxValue, maxy = float.MinValue;
            for (int i = 0; i < n; i++) { miny = Mathf.Min(miny, p[i * 2 + 1]); maxy = Mathf.Max(maxy, p[i * 2 + 1]); }
            int y0 = Mathf.FloorToInt(miny), y1 = Mathf.CeilToInt(maxy);
            var xs = new List<float>(8);
            for (int y = y0; y <= y1; y++)
            {
                xs.Clear();
                float yc = y + 0.5f;
                for (int i = 0; i < n; i++)
                {
                    float ax = p[i * 2], ay = p[i * 2 + 1];
                    int j = (i + 1) % n;
                    float bx = p[j * 2], by = p[j * 2 + 1];
                    if ((ay <= yc && by > yc) || (by <= yc && ay > yc))
                    {
                        float t = (yc - ay) / (by - ay);
                        xs.Add(ax + t * (bx - ax));
                    }
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int sx = Mathf.RoundToInt(xs[k]), ex = Mathf.RoundToInt(xs[k + 1]);
                    for (int x = sx; x <= ex; x++) Plot(x, y, 1f);
                }
            }
        }

        // ----------------------------------------------------------- the pose
        // y increases UPWARD (matches SkillIcons.Runner()'s own convention: its head sits at a
        // higher y than its leg strokes). All coordinates below are in that 256x256 buffer space.
        static void Draw()
        {
            // The vertex: the K's own branch point, where the stem meets both diagonals. Arms and
            // legs both radiate from here, same as a real K's stem meets its two diagonals at one
            // spot rather than three separate joints.
            const float vx = 100f, vy = 128f;

            // Arms ARE the K's vertical stem - one straight up from the vertex, one straight down,
            // rather than one continuous stroke, per the brief. Together they trace the same line a
            // plain K's stem would.
            Stroke(vx, vy, vx, 205f, 24f);          // arm up
            Stroke(vx, vy, vx, 38f, 22f);           // arm down

            // Head: a small solid disc perched just past the raised arm's own tip - the same
            // "sits just past the stroke's end" offset the original design used for its torso.
            Dot(vx - 3f, 222f, 17f);

            // Kicking leg: the K's UPPER diagonal, up and out from the vertex - the top foot the
            // ball arcs off of.
            Stroke(vx, vy, 152f, 168f, 25f);        // thigh
            Stroke(152f, 168f, 196f, 192f, 20f);    // shin
            FillPoly(196f, 192f, 210f, 197f, 205f, 207f, 189f, 201f);   // boot

            // Trailing leg: the K's LOWER diagonal, down and out from the vertex - a real K's other
            // branch.
            Stroke(vx, vy, 155f, 90f, 25f);         // thigh
            Stroke(155f, 90f, 178f, 78f, 18f);      // toe flick

            // A small stylized ball arcing off the top foot: an outline ring, not a solid disc, so
            // it reads as a ball (not a blob) even shrunk down to the hub's small wordmark size.
            // Two shrinking trail dots between the boot and the ball read as the arc's motion.
            const float bx = 222f, by = 212f, br = 12f;
            Dot(205f, 205f, 5f);
            Dot(213f, 209f, 7f);
            Ring(bx, by, br, 4f);
            Stroke(bx - br * 0.4f, by - br * 0.2f, bx + br * 0.4f, by + br * 0.3f, 2.6f);
            Stroke(bx - br * 0.1f, by - br * 0.5f, bx - br * 0.1f, by + br * 0.5f, 2.6f);
        }
    }
}
