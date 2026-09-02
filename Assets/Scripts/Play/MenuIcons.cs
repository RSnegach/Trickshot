using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Procedural icons for the FIFA-style main menu's mode cards. Same architecture as
    /// SkillIcons.cs (coverage-antialiased strokes/fills, max-blended into a white RGBA buffer, no
    /// image files) but its own 96x96 buffer - these render at ~150px on a card, noticeably bigger
    /// than a 46px skill badge, so SkillIcons' shared 64px cache would blur too much if reused here.
    /// </summary>
    public static class MenuIcons
    {
        const int S = 96;
        const float C = 48f;
        static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        static Color32[] _buf;

        // Assets/Resources/MenuIcons/<id>.png overrides the procedural drawing, same convention
        // SkillIcons uses - an artist can drop in real art later without touching code.
        const string ResourceDir = "MenuIcons/";

        public static Texture2D Get(string id)
        {
            if (_cache.TryGetValue(id, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>(ResourceDir + id) ?? Build(id);
            _cache[id] = t;
            return t;
        }

        static Texture2D Build(string id)
        {
            _buf = new Color32[S * S];
            Draw(id);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "menuicon_" + id
            };
            tex.SetPixels32(_buf);
            tex.Apply();
            _buf = null;
            return tex;
        }

        // ----------------------------------------------------------- pixel primitives (same as
        // SkillIcons.cs's own, re-declared at this file's scale - see TitleGlyph.cs for why these
        // stay separate per-file rather than shared).
        static void Plot(int x, int y, float a)
        {
            if (x < 0 || y < 0 || x >= S || y >= S) return;
            int i = y * S + x;
            byte na = (byte)(Mathf.Clamp01(a) * 255f);
            if (na > _buf[i].a) _buf[i] = new Color32(255, 255, 255, na);
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

        // ----------------------------------------------------------- per-card art
        static void Draw(string id)
        {
            switch (id)
            {
                case "single": Ball(C, C, 34f); break;
                case "multiplayer": TwoHeads(); break;
                case "career": BarChart(); break;
                case "zoo": Quadruped(); break;
                case "settings": Gear(); break;
            }
        }

        // A football: ring + inscribed pentagon + spokes (same shape SkillIcons.Ball draws,
        // recomposed here at this file's own scale rather than shared - see file doc comment).
        static void Ball(float cx, float cy, float r)
        {
            Ring(cx, cy, r, 4f);
            float pr = r * 0.36f;
            float[] pent = new float[10];
            for (int i = 0; i < 5; i++)
            {
                float a = (-90f + i * 72f) * Mathf.Deg2Rad;
                pent[i * 2] = cx + Mathf.Cos(a) * pr;
                pent[i * 2 + 1] = cy + Mathf.Sin(a) * pr;
            }
            PolyLine(2.6f, true, pent);
            for (int i = 0; i < 5; i++)
            {
                float a = (-90f + i * 72f) * Mathf.Deg2Rad;
                Stroke(pent[i * 2], pent[i * 2 + 1], cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r, 2.2f);
            }
        }

        // Two overlapping bust silhouettes - head + widening shoulder trapezoid each - for "with
        // other people". Foreground figure lower-left and slightly bigger; background figure
        // upper-right and slightly smaller, reading as "behind" it.
        static void TwoHeads()
        {
            // Background figure first, so the foreground one draws (max-blends) over it.
            Dot(66f, 68f, 11f);
            FillPoly(58f, 54f, 68f, 54f, 80f, 30f, 46f, 30f);

            Dot(32f, 62f, 13f);
            FillPoly(26f, 46f, 38f, 46f, 54f, 16f, 10f, 16f);
        }

        // A simple ascending 3-bar chart on a baseline.
        static void BarChart()
        {
            Stroke(20f, 24f, 76f, 24f, 3f);     // baseline
            Stroke(30f, 24f, 30f, 40f, 8f);
            Stroke(48f, 24f, 48f, 50f, 8f);
            Stroke(66f, 24f, 66f, 64f, 8f);
        }

        // A generic quadruped: a rounded body blob, four short legs, a head, two ear ticks.
        // Flagged in the design plan as the loosest spec of the five card icons - "generic animal"
        // has no one obviously-correct silhouette, so expect this to need a second pass.
        static void Quadruped()
        {
            FillPoly(22f, 46f, 30f, 54f, 66f, 54f, 74f, 46f, 74f, 34f, 66f, 26f, 30f, 26f, 22f, 34f);
            Stroke(30f, 26f, 28f, 12f, 6f);
            Stroke(44f, 26f, 42f, 12f, 6f);
            Stroke(58f, 26f, 56f, 12f, 6f);
            Stroke(70f, 26f, 68f, 12f, 6f);
            Dot(80f, 46f, 10f);
            Stroke(75f, 54f, 73f, 62f, 3f);
            Stroke(85f, 54f, 87f, 62f, 3f);
        }

        // A gear: ring body, 8 blocky radial teeth, inner hub hole.
        static void Gear()
        {
            Ring(C, C, 26f, 5f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Stroke(C + Mathf.Cos(a) * 26f, C + Mathf.Sin(a) * 26f,
                       C + Mathf.Cos(a) * 35f, C + Mathf.Sin(a) * 35f, 7f);
            }
            Ring(C, C, 11f, 4f);
        }
    }
}
