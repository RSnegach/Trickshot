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
    /// THE DESIGN: a capital K is already a vertical stem plus two diagonals meeting at one vertex.
    /// A bicycle kick is a torso plus two legs meeting at one hip. Those are the SAME three-stroke
    /// skeleton, just relabeled - which is what lets this read as "K" at a glance (the eye keys off
    /// the classic stem+diagonals wedge first) and as a kicker up close, rather than two unrelated
    /// shapes forced together. The torso is deliberately more upright than a real bicycle kick would
    /// be, to keep the K legible - a legibility trade-off, not an oversight.
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
            // Torso (the K's stem): a slight backward lean, hip vertex at the bottom.
            Stroke(107f, 171f, 119f, 69f, 30f);

            // Head: a solid disc, not a ring - "silhouette" reads as a solid pictogram shape, and a
            // thin outline (right for a 46px skill badge) is the wrong visual weight at hero scale.
            Dot(105f, 187f, 19f);

            // Hip vertex (119,69): both legs branch from here - the same point a real K's two
            // diagonals would meet its stem.

            // Kicking leg: the K's UPPER diagonal, swept up and over the head - the one unambiguous
            // "bicycle kick" tell. Deliberately overshoots above the head's own top.
            Stroke(119f, 69f, 159f, 145f, 26f);    // thigh
            Stroke(159f, 145f, 149f, 209f, 22f);   // shin
            FillPoly(149f, 209f, 163f, 215f, 155f, 221f, 139f, 217f);   // boot

            // Trailing leg: the K's LOWER diagonal, foot landing on the baseline so the glyph stands
            // on the same line as the surrounding lettering.
            Stroke(119f, 69f, 163f, 35f, 26f);     // thigh
            Stroke(163f, 35f, 175f, 29f, 18f);     // toe flick

            // Arms, for balance - kept short so they stay inside the silhouette's own bounding box
            // rather than widening the glyph past what the TRI/K/SHOT kerning expects.
            Stroke(111f, 159f, 81f, 145f, 18f);    // back arm
            Stroke(113f, 153f, 137f, 177f, 16f);   // front arm
        }
    }
}
