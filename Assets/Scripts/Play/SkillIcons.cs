using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Procedurally generated, transparent-background, white line-art icons for each
    /// skill-tree node, built once (lazily) into cached Texture2Ds and drawn on the node
    /// badges. No image files - fits the runtime-only, no-assets architecture.
    ///
    /// Everything is painted white with alpha = coverage into a 64x64 RGBA buffer using a
    /// handful of primitives (round-brush strokes, rings, arcs, filled polygons). Strokes
    /// are max-blended so overlapping shapes union cleanly instead of darkening.
    /// </summary>
    public static class SkillIcons
    {
        const int S = 64;                    // texture resolution
        const float C = 32f;                 // centre
        static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        static Color32[] _buf;               // working buffer during a build

        public static Texture2D Get(string id)
        {
            if (_cache.TryGetValue(id, out var t) && t != null) return t;
            t = Build(id);
            _cache[id] = t;
            return t;
        }

        static Texture2D Build(string id)
        {
            _buf = new Color32[S * S];       // all zero => fully transparent
            Draw(id);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "skillicon_" + id
            };
            tex.SetPixels32(_buf);
            tex.Apply();
            _buf = null;
            return tex;
        }

        // ----------------------------------------------------------- pixel primitives
        static void Plot(int x, int y, float a)
        {
            if (x < 0 || y < 0 || x >= S || y >= S) return;
            int i = y * S + x;
            byte na = (byte)(Mathf.Clamp01(a) * 255f);
            if (na > _buf[i].a) _buf[i] = new Color32(255, 255, 255, na);   // max-blend (union)
        }

        // Round brush stamp (filled disc with a 1px soft edge).
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

        // Open (or closed) polyline through x,y pairs.
        static void PolyLine(float w, bool close, params float[] p)
        {
            for (int i = 0; i + 3 < p.Length; i += 2)
                Stroke(p[i], p[i + 1], p[i + 2], p[i + 3], w);
            if (close && p.Length >= 4)
                Stroke(p[p.Length - 2], p[p.Length - 1], p[0], p[1], w);
        }

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

        static void Arc(float cx, float cy, float rad, float a0, float a1, float w)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 6f));
            float px = cx + Mathf.Cos(a0 * Mathf.Deg2Rad) * rad;
            float py = cy + Mathf.Sin(a0 * Mathf.Deg2Rad) * rad;
            for (int i = 1; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
                float x = cx + Mathf.Cos(a) * rad, y = cy + Mathf.Sin(a) * rad;
                Stroke(px, py, x, y, w); px = x; py = y;
            }
        }

        // Filled simple polygon (even-odd scanline).
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

        // ----------------------------------------------------------- motifs
        // Arrow segment start->end with a V head.
        static void ArrowSeg(float x0, float y0, float x1, float y1, float w, float head)
        {
            Stroke(x0, y0, x1, y1, w);
            float ang = Mathf.Atan2(y1 - y0, x1 - x0);
            float a1 = ang + Mathf.PI * 0.80f, a2 = ang - Mathf.PI * 0.80f;
            Stroke(x1, y1, x1 + Mathf.Cos(a1) * head, y1 + Mathf.Sin(a1) * head, w);
            Stroke(x1, y1, x1 + Mathf.Cos(a2) * head, y1 + Mathf.Sin(a2) * head, w);
        }

        // Chevron ">" pointing right, apex at (x,y).
        static void ChevronR(float x, float y, float size, float w)
        {
            Stroke(x - size, y + size, x, y, w);
            Stroke(x, y, x - size, y - size, w);
        }

        static void Ball(float cx, float cy, float r)
        {
            Ring(cx, cy, r, 2.2f);
            float pr = r * 0.36f;
            float[] pent = new float[10];
            for (int i = 0; i < 5; i++)
            {
                float a = (-90f + i * 72f) * Mathf.Deg2Rad;
                pent[i * 2] = cx + Mathf.Cos(a) * pr;
                pent[i * 2 + 1] = cy + Mathf.Sin(a) * pr;
            }
            PolyLine(1.7f, true, pent);
            for (int i = 0; i < 5; i++)
            {
                float a = (-90f + i * 72f) * Mathf.Deg2Rad;
                Stroke(pent[i * 2], pent[i * 2 + 1], cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r, 1.4f);
            }
        }

        // Side-view boot silhouette. dir +1 toe-right, -1 toe-left.
        static void Boot(float cx, float cy, float s, int dir)
        {
            float[] pts = {
                -5f, 14f,   -5f, 0f,   -4f, -4f,   12f, -4f,
                15f, 0f,     4f, 2f,    2f, 14f
            };
            for (int i = 0; i < pts.Length; i += 2)
            {
                pts[i] = cx + pts[i] * dir * s;
                pts[i + 1] = cy + pts[i + 1] * s;
            }
            FillPoly(pts);
        }

        static void Head(float cx, float cy, float r)
        {
            Ring(cx, cy, r, 2.4f);
        }

        // 4-point sparkle.
        static void Spark(float cx, float cy, float r)
        {
            Stroke(cx, cy - r, cx, cy + r, 1.6f);
            Stroke(cx - r, cy, cx + r, cy, 1.6f);
            Dot(cx, cy, 1.4f);
        }

        static void Burst(float cx, float cy, float r0, float r1)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Stroke(cx + Mathf.Cos(a) * r0, cy + Mathf.Sin(a) * r0,
                       cx + Mathf.Cos(a) * r1, cy + Mathf.Sin(a) * r1, 1.8f);
            }
        }

        // Little goal frame (posts + bar).
        static void Goal(float cx, float cy, float w, float h)
        {
            Stroke(cx - w, cy - h, cx - w, cy + h, 2f);
            Stroke(cx + w, cy - h, cx + w, cy + h, 2f);
            Stroke(cx - w, cy + h, cx + w, cy + h, 2f);
        }

        static void GroundLine()
        {
            Stroke(12f, 12f, 52f, 12f, 2.4f);
        }

        // ----------------------------------------------------------- per-node art
        static void Draw(string id)
        {
            switch (id)
            {
                // ---------------- PACE ----------------
                case "p0": // Quick Feet - a boot
                    Boot(30f, 30f, 1.35f, 1); break;
                case "p1a": // Acceleration - chevrons
                    ChevronR(24f, C, 9f, 3f); ChevronR(33f, C, 9f, 3f); ChevronR(42f, C, 9f, 3f); break;
                case "p1b": // Long Strides - running figure
                    Runner(); break;
                case "p2a": // Sharp Turns - hooked arrow
                    Arc(30f, 30f, 12f, 200f, 20f, 2.6f);
                    ArrowSeg(42f, 30f, 46f, 40f, 2.6f, 6f); break;
                case "p2b": // Flat Out - speed lines
                    Ball(44f, C, 8f);
                    Stroke(10f, 40f, 30f, 40f, 2.4f);
                    Stroke(8f, C, 30f, C, 2.4f);
                    Stroke(12f, 24f, 30f, 24f, 2.4f); break;
                case "pcap": // Afterburners - flame
                    Flame(); break;

                // ---------------- SHOOTING ----------------
                case "s0": // Clean Strike - ball + short arrow
                    Ball(26f, 28f, 10f);
                    ArrowSeg(36f, 34f, 50f, 44f, 2.4f, 6f); break;
                case "s1a": // Power - ball + burst
                    Burst(32f, 32f, 12f, 20f); Ball(32f, 32f, 10f); break;
                case "s1b": // Placement - target
                    Target(32f, 32f); break;
                case "s2a": // Rising Shot - ball + rising arrow
                    Ball(22f, 22f, 8f);
                    Arc(30f, 20f, 18f, 210f, 300f, 2.4f);
                    ArrowSeg(44f, 34f, 48f, 46f, 2.4f, 6f); break;
                case "s2b": // Finesse - arrow into bullseye
                    Target(34f, 34f);
                    ArrowSeg(14f, 14f, 32f, 32f, 2.2f, 6f); break;
                case "scap": // Cannon - barrel + ball
                    Cannon(); break;

                // ---------------- HEADING ----------------
                case "h0": // Timing - head + clock
                    Head(24f, 30f, 12f);
                    Ring(44f, 40f, 9f, 2f);
                    Stroke(44f, 40f, 44f, 46f, 1.8f);
                    Stroke(44f, 40f, 48f, 40f, 1.8f); break;
                case "h1a": // Power Header - head + ball + impact
                    Head(28f, 28f, 12f);
                    Ball(46f, 44f, 7f);
                    Burst(40f, 38f, 3f, 7f); break;
                case "h1b": // Leap - up arrow + ground
                    GroundLine(); ArrowSeg(C, 16f, C, 48f, 3f, 8f); break;
                case "h2a": // Bullet Head - head + forward arrow
                    Head(24f, 32f, 12f);
                    ArrowSeg(34f, 32f, 52f, 32f, 2.6f, 7f); break;
                case "h2b": // Hang Time - figure + up chevrons
                    Head(C, 24f, 7f);
                    Stroke(C, 17f, C, 6f, 2.2f);
                    Stroke(C, 12f, 25f, 8f, 2.2f); Stroke(C, 12f, 39f, 8f, 2.2f);
                    ChevronUp(C, 40f, 8f, 2.4f); ChevronUp(C, 48f, 8f, 2.4f); break;
                case "hcap": // Aerial Threat - head + arrow to goal
                    Head(20f, 34f, 11f);
                    Goal(48f, 30f, 7f, 9f);
                    ArrowSeg(28f, 32f, 40f, 30f, 2.4f, 6f); break;

                // ---------------- STRENGTH ----------------
                case "st0": // Core - torso shield
                    Shield(32f, 34f, 16f, 20f); Stroke(C, 44f, C, 24f, 1.8f); break;
                case "st1a": // Frame - broad shoulders + head
                    Head(C, 46f, 6f);
                    FillPoly(16f, 34f, 22f, 40f, 42f, 40f, 48f, 34f, 44f, 16f, 20f, 16f); break;
                case "st1b": // Balance - scales
                    Scales(); break;
                case "st2a": // Powerhouse - flexed arm
                    FlexArm(); break;
                case "st2b": // Anchor - anchor
                    Anchor(); break;
                case "stcap": // Immovable - brick wall
                    Wall(); break;

                // ---------------- CONTROL ----------------
                case "c0": // First Touch - boot + small ball
                    Boot(28f, 24f, 1.1f, 1); Ball(40f, 44f, 7f); break;
                case "c1a": // Cushion - ball + cushion arcs
                    Ball(C, 38f, 9f);
                    Arc(C, 30f, 14f, 200f, 340f, 2.2f);
                    Arc(C, 24f, 18f, 205f, 335f, 2.2f); break;
                case "c1b": // Weak Foot - single (left) boot
                    Boot(34f, 30f, 1.35f, -1); break;
                case "c2a": // Composure - calm concentric rings
                    Ring(32f, 32f, 18f, 2.2f); Ring(32f, 32f, 10f, 2.2f); Dot(32f, 32f, 2.4f); break;
                case "c2b": // Two-Footed - two boots
                    Boot(22f, 30f, 1.0f, -1); Boot(42f, 30f, 1.0f, 1); break;
                case "ccap": // Silky - two boots + sparkle
                    Boot(24f, 26f, 0.95f, -1); Boot(44f, 26f, 0.95f, 1); Spark(44f, 48f, 6f); break;

                // ---------------- AGILITY ----------------
                case "a0": // Spring - coil
                    Coil(); break;
                case "a1a": // Nimble - swirl
                    Arc(32f, 32f, 16f, 90f, 380f, 2.6f);
                    Arc(32f, 32f, 9f, 380f, 620f, 2.4f); break;
                case "a1b": // Bounce - ball + bounce arcs
                    Ball(44f, 40f, 7f);
                    Arc(20f, 12f, 8f, 0f, 180f, 2f);
                    Arc(36f, 12f, 10f, 0f, 180f, 2f); break;
                case "a2a": // Twist - rotation arrows
                    Arc(32f, 32f, 15f, 40f, 300f, 2.6f);
                    ArrowSeg(43f, 20f, 47f, 30f, 2.4f, 6f);
                    ArrowSeg(21f, 44f, 17f, 34f, 2.4f, 6f); break;
                case "a2b": // Elevation - up arrow + ground
                    GroundLine(); ArrowSeg(C, 14f, C, 50f, 3f, 9f); break;
                case "acap": // Acrobat - flip loop + figure
                    Ring(32f, 30f, 15f, 2.6f);
                    ArrowSeg(45f, 24f, 48f, 34f, 2.4f, 6f);
                    Dot(32f, 30f, 3f); break;

                default:
                    Ring(32f, 32f, 14f, 2.4f); break;
            }
        }

        // ---- composite motifs used by name above ----
        static void Runner()
        {
            Head(38f, 48f, 6f);
            Stroke(36f, 42f, 28f, 30f, 2.4f);   // torso
            Stroke(28f, 30f, 20f, 22f, 2.4f);   // back leg
            Stroke(28f, 30f, 40f, 20f, 2.4f);   // front thigh
            Stroke(40f, 20f, 36f, 12f, 2.4f);   // front shin
            Stroke(34f, 40f, 46f, 44f, 2.2f);   // front arm
            Stroke(34f, 40f, 24f, 44f, 2.2f);   // back arm
        }

        static void Flame()
        {
            FillPoly(32f, 52f, 40f, 36f, 38f, 26f, 34f, 30f, 35f, 18f,
                     28f, 26f, 30f, 32f, 25f, 30f, 24f, 40f);
        }

        static void Target(float cx, float cy)
        {
            Ring(cx, cy, 15f, 2.2f);
            Ring(cx, cy, 8f, 2.2f);
            Dot(cx, cy, 2.2f);
            Stroke(cx - 20f, cy, cx - 12f, cy, 1.8f);
            Stroke(cx + 12f, cy, cx + 20f, cy, 1.8f);
            Stroke(cx, cy - 20f, cx, cy - 12f, 1.8f);
            Stroke(cx, cy + 12f, cx, cy + 20f, 1.8f);
        }

        static void Cannon()
        {
            // Angled barrel.
            FillPoly(16f, 20f, 22f, 14f, 46f, 34f, 42f, 40f);
            Ring(20f, 20f, 7f, 2.2f);       // wheel
            Ball(50f, 42f, 6f);             // ball leaving
            Burst(45f, 38f, 2f, 6f);
        }

        static void ChevronUp(float x, float y, float size, float w)
        {
            Stroke(x - size, y - size, x, y, w);
            Stroke(x, y, x + size, y - size, w);
        }

        static void Shield(float cx, float cy, float hw, float hh)
        {
            PolyLine(2.2f, true,
                cx - hw, cy + hh, cx + hw, cy + hh,
                cx + hw, cy - hh * 0.2f, cx, cy - hh, cx - hw, cy - hh * 0.2f);
        }

        static void Scales()
        {
            Stroke(32f, 14f, 32f, 46f, 2.2f);       // post
            Stroke(16f, 44f, 48f, 44f, 2.4f);       // beam
            Arc(22f, 44f, 10f, 200f, 340f, 2f);     // left pan
            Arc(42f, 44f, 10f, 200f, 340f, 2f);     // right pan
            Stroke(28f, 14f, 36f, 14f, 2.2f);       // top
        }

        static void FlexArm()
        {
            FillPoly(16f, 20f, 16f, 30f, 26f, 34f, 30f, 44f, 40f, 44f, 40f, 34f,
                     30f, 30f, 30f, 20f);
            Ring(35f, 38f, 4f, 2f);                 // fist knuckle hint
        }

        static void Anchor()
        {
            Ring(32f, 48f, 5f, 2.2f);               // ring
            Stroke(32f, 43f, 32f, 18f, 2.6f);       // shank
            Stroke(24f, 38f, 40f, 38f, 2.4f);       // stock
            Arc(32f, 20f, 14f, 200f, 340f, 2.6f);   // arms/flukes
        }

        static void Wall()
        {
            // brick grid
            for (int r = 0; r < 3; r++)
            {
                float yy = 20f + r * 9f;
                Stroke(14f, yy, 50f, yy, 2f);
            }
            Stroke(14f, 20f, 14f, 47f, 2f);
            Stroke(50f, 20f, 50f, 47f, 2f);
            // staggered verticals
            Stroke(26f, 20f, 26f, 29f, 1.8f);
            Stroke(38f, 20f, 38f, 29f, 1.8f);
            Stroke(32f, 29f, 32f, 38f, 1.8f);
            Stroke(20f, 29f, 20f, 38f, 1.8f);
            Stroke(44f, 29f, 44f, 38f, 1.8f);
            Stroke(26f, 38f, 26f, 47f, 1.8f);
            Stroke(38f, 38f, 38f, 47f, 1.8f);
        }

        static void Coil()
        {
            float x = 24f;
            PolyLine(2.4f, false,
                x, 12f, x + 16f, 18f, x, 24f, x + 16f, 30f,
                x, 36f, x + 16f, 42f, x, 48f);
            Stroke(20f, 12f, 44f, 12f, 2.2f);
            Stroke(20f, 48f, 44f, 48f, 2.2f);
        }
    }
}
