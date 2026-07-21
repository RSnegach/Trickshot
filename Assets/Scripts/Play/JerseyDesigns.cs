using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // Which tab a design appears under in the customization screen.
    public enum DesignTab { Nations, ClassicKits, Patterns, Bold }

    // A single predrawn jersey design. Apply paints FRONT+BACK regions of a
    // 256x520 atlas buffer that is already filled with the jersey base color.
    public class Design
    {
        public string Name;
        public DesignTab Tab;
        public Action<Color32[]> Apply;
    }

    // Library of predrawn jersey designs. Pure runtime C#, no editor deps.
    public static class JerseyDesigns
    {
        // Atlas layout. Single source of truth for the paint code.
        public const int W       = 256;   // atlas width (also each region's width)
        public const int RegionH = 256;   // each region is 256 tall
        public const int AtlasH  = 520;   // total atlas height
        public const int BackY0  = 0;     // BACK region occupies rows [0, 256)
        public const int FrontY0 = 256;   // FRONT region occupies rows [256, 512)
        public const int PlainY0 = 512;   // PLAIN band rows [512, 520) - never touched

        static List<Design> _all;
        static readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();

        // ---- public API --------------------------------------------------

        public static IReadOnlyList<Design> All
        {
            get { EnsureBuilt(); return _all; }
        }

        public static IReadOnlyList<Design> InTab(DesignTab tab)
        {
            EnsureBuilt();
            var list = new List<Design>();
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Tab == tab) list.Add(_all[i]);
            return list;
        }

        // Cached ~48x48 thumbnail rendered from the design's FRONT image.
        public static Texture2D Thumb(Design d)
        {
            if (d == null) return null;
            if (_thumbs.TryGetValue(d.Name, out var cached) && cached != null)
                return cached;

            var buf = new Color32[W * AtlasH];
            var grey = new Color32(128, 128, 128, 255);
            for (int i = 0; i < buf.Length; i++) buf[i] = grey;
            if (d.Apply != null) d.Apply(buf);

            const int size = 48;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int ty = 0; ty < size; ty++)
            {
                int sy = ty * RegionH / size;            // 0..255
                for (int tx = 0; tx < size; tx++)
                {
                    int sx = tx * W / size;              // 0..255
                    int src = (FrontY0 + sy) * W + sx;
                    if (src < 0 || src >= buf.Length) continue;
                    px[ty * size + tx] = buf[src];
                }
            }
            tex.SetPixels32(px);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            _thumbs[d.Name] = tex;
            return tex;
        }

        // ---- build / registration ----------------------------------------

        static void EnsureBuilt()
        {
            if (_all != null) return;
            var list = new List<Design>();
            BuildNations(list);
            BuildClassic(list);
            BuildPatterns(list);
            BuildBold(list);
            _all = list;
        }

        static void Add(List<Design> list, string name, DesignTab tab,
                        Action<Action<int, int, Color32>> paint)
        {
            list.Add(new Design { Name = name, Tab = tab, Apply = MakeApply(paint) });
        }

        // Runs the per-image painter once for the BACK region and once for FRONT.
        static Action<Color32[]> MakeApply(Action<Action<int, int, Color32>> paint)
        {
            return buf =>
            {
                paint(RegionSetter(buf, BackY0));
                paint(RegionSetter(buf, FrontY0));
            };
        }

        // A bounds-checked per-pixel setter in LOCAL region coords (0..255, y up).
        static Action<int, int, Color32> RegionSetter(Color32[] buf, int regionY0)
        {
            return (x, y, c) =>
            {
                if (x < 0 || x >= W || y < 0 || y >= RegionH) return;
                int idx = (regionY0 + y) * W + x;
                if (idx < 0 || idx >= buf.Length) return;
                buf[idx] = c;
            };
        }

        // ---- color helpers ------------------------------------------------

        static Color32 C(byte r, byte g, byte b) { return new Color32(r, g, b, 255); }

        static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32(
                (byte)Mathf.RoundToInt(a.r + (b.r - a.r) * t),
                (byte)Mathf.RoundToInt(a.g + (b.g - a.g) * t),
                (byte)Mathf.RoundToInt(a.b + (b.b - a.b) * t),
                255);
        }

        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        // Common flag / kit colors.
        static readonly Color32 White   = C(245, 245, 245);
        static readonly Color32 Black   = C(24, 24, 26);
        static readonly Color32 Red     = C(200, 40, 48);
        static readonly Color32 Blue    = C(0, 85, 164);
        static readonly Color32 Green   = C(0, 140, 69);
        static readonly Color32 Yellow  = C(250, 205, 60);
        static readonly Color32 Gold    = C(255, 206, 0);
        static readonly Color32 Orange  = C(255, 136, 62);
        static readonly Color32 LightBlue = C(117, 170, 219);

        // ---- primitive drawing (local 0..255 region space) ----------------

        static void FillRegion(Action<int, int, Color32> px, Color32 c)
        {
            for (int y = 0; y < RegionH; y++)
                for (int x = 0; x < W; x++)
                    px(x, y, c);
        }

        // Horizontal band, rows y0..y1 inclusive.
        static void HBand(Action<int, int, Color32> px, int y0, int y1, Color32 c)
        {
            if (y0 > y1) { int t = y0; y0 = y1; y1 = t; }
            if (y0 < 0) y0 = 0; if (y1 > RegionH - 1) y1 = RegionH - 1;
            for (int y = y0; y <= y1; y++)
                for (int x = 0; x < W; x++)
                    px(x, y, c);
        }

        // Vertical band, cols x0..x1 inclusive.
        static void VBand(Action<int, int, Color32> px, int x0, int x1, Color32 c)
        {
            if (x0 > x1) { int t = x0; x0 = x1; x1 = t; }
            if (x0 < 0) x0 = 0; if (x1 > W - 1) x1 = W - 1;
            for (int x = x0; x <= x1; x++)
                for (int y = 0; y < RegionH; y++)
                    px(x, y, c);
        }

        static void Rect(Action<int, int, Color32> px, int x0, int y0, int x1, int y1, Color32 c)
        {
            if (x0 > x1) { int t = x0; x0 = x1; x1 = t; }
            if (y0 > y1) { int t = y0; y0 = y1; y1 = t; }
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    px(x, y, c);
        }

        static void Disc(Action<int, int, Color32> px, int cx, int cy, int r, Color32 c)
        {
            if (r < 0) return;
            int r2 = r * r;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= r2) px(x, y, c);
                }
        }

        static void Ring(Action<int, int, Color32> px, int cx, int cy, int rOuter, int rInner, Color32 c)
        {
            int ro2 = rOuter * rOuter, ri2 = rInner * rInner;
            for (int y = cy - rOuter; y <= cy + rOuter; y++)
                for (int x = cx - rOuter; x <= cx + rOuter; x++)
                {
                    int dx = x - cx, dy = y - cy;
                    int d2 = dx * dx + dy * dy;
                    if (d2 <= ro2 && d2 >= ri2) px(x, y, c);
                }
        }

        static void Diamond(Action<int, int, Color32> px, int cx, int cy, int rx, int ry, Color32 c)
        {
            if (rx <= 0 || ry <= 0) return;
            for (int y = cy - ry; y <= cy + ry; y++)
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    float nx = Mathf.Abs(x - cx) / (float)rx;
                    float ny = Mathf.Abs(y - cy) / (float)ry;
                    if (nx + ny <= 1f) px(x, y, c);
                }
        }

        // Diagonal band. rising = bottom-left to top-right.
        static void DiagStripe(Action<int, int, Color32> px, Color32 c, int width, bool rising)
        {
            for (int y = 0; y < RegionH; y++)
                for (int x = 0; x < W; x++)
                {
                    int d = rising ? (x - y) : (x + y - (RegionH - 1));
                    if (Mathf.Abs(d) <= width) px(x, y, c);
                }
        }

        static void Saltire(Action<int, int, Color32> px, Color32 c, int width)
        {
            DiagStripe(px, c, width, true);
            DiagStripe(px, c, width, false);
        }

        // Scandinavian cross. vCenter is x of vertical bar (left-of-center on flags).
        static void NordicCross(Action<int, int, Color32> px, Color32 bg, Color32 cross,
                                int vCenter, int hCenter, int thickness)
        {
            FillRegion(px, bg);
            int h = thickness / 2;
            VBand(px, vCenter - h, vCenter + h, cross);
            HBand(px, hCenter - h, hCenter + h, cross);
        }

        static void VTriband(Action<int, int, Color32> px, Color32 a, Color32 b, Color32 c)
        {
            VBand(px, 0, W / 3 - 1, a);
            VBand(px, W / 3, 2 * W / 3 - 1, b);
            VBand(px, 2 * W / 3, W - 1, c);
        }

        // top maps to the highest rows (visual top).
        static void HTriband(Action<int, int, Color32> px, Color32 top, Color32 mid, Color32 bot)
        {
            HBand(px, 0, RegionH / 3 - 1, bot);
            HBand(px, RegionH / 3, 2 * RegionH / 3 - 1, mid);
            HBand(px, 2 * RegionH / 3, RegionH - 1, top);
        }

        // N vertical stripes alternating a/b.
        static void Stripes(Action<int, int, Color32> px, int n, Color32 a, Color32 b)
        {
            if (n < 1) n = 1;
            for (int x = 0; x < W; x++)
            {
                int band = (x * n) / W;
                Color32 c = (band % 2 == 0) ? a : b;
                for (int y = 0; y < RegionH; y++) px(x, y, c);
            }
        }

        // N horizontal hoops alternating a/b.
        static void Hoops(Action<int, int, Color32> px, int n, Color32 a, Color32 b)
        {
            if (n < 1) n = 1;
            for (int y = 0; y < RegionH; y++)
            {
                int band = (y * n) / RegionH;
                Color32 c = (band % 2 == 0) ? a : b;
                for (int x = 0; x < W; x++) px(x, y, c);
            }
        }

        static void Checker(Action<int, int, Color32> px, int cells, Color32 a, Color32 b)
        {
            if (cells < 1) cells = 1;
            for (int y = 0; y < RegionH; y++)
                for (int x = 0; x < W; x++)
                {
                    int cxi = (x * cells) / W;
                    int cyi = (y * cells) / RegionH;
                    Color32 c = ((cxi + cyi) % 2 == 0) ? a : b;
                    px(x, y, c);
                }
        }

        // Stacked V (chevron) bands pointing downward.
        static void Chevrons(Action<int, int, Color32> px, int thickness, Color32 a, Color32 b)
        {
            if (thickness < 1) thickness = 1;
            int cx = W / 2;
            for (int y = 0; y < RegionH; y++)
                for (int x = 0; x < W; x++)
                {
                    int v = y + Mathf.Abs(x - cx);
                    int band = v / thickness;
                    Color32 c = (band % 2 == 0) ? a : b;
                    px(x, y, c);
                }
        }

        // Vertical two-color lerp. bottom at y=0, top at y=255.
        static void Gradient(Action<int, int, Color32> px, Color32 bottom, Color32 top)
        {
            for (int y = 0; y < RegionH; y++)
            {
                float t = (float)y / (RegionH - 1);
                Color32 c = Lerp(bottom, top, t);
                for (int x = 0; x < W; x++) px(x, y, c);
            }
        }

        static void PolkaDots(Action<int, int, Color32> px, int spacing, int radius, Color32 dot)
        {
            if (spacing < 1) spacing = 1;
            for (int cy = spacing / 2; cy < RegionH; cy += spacing)
                for (int cx = spacing / 2; cx < W; cx += spacing)
                    Disc(px, cx, cy, radius, dot);
        }

        // Diamond lattice (argyle).
        static void Diamonds(Action<int, int, Color32> px, int size, Color32 a, Color32 b)
        {
            if (size < 2) size = 2;
            for (int y = 0; y < RegionH; y++)
                for (int x = 0; x < W; x++)
                {
                    int mx = Mod(x, size) - size / 2;
                    int my = Mod(y, size) - size / 2;
                    bool inside = (Mathf.Abs(mx) + Mathf.Abs(my)) < size / 2;
                    px(x, y, inside ? a : b);
                }
        }

        // Concentric rings alternating a/b.
        static void Rings(Action<int, int, Color32> px, int cx, int cy, int bandW, Color32 a, Color32 b)
        {
            if (bandW < 1) bandW = 1;
            for (int y = 0; y < RegionH; y++)
                for (int x = 0; x < W; x++)
                {
                    int dx = x - cx, dy = y - cy;
                    int d = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy));
                    int band = d / bandW;
                    Color32 c = (band % 2 == 0) ? a : b;
                    px(x, y, c);
                }
        }

        // Filled 5-point star via polygon fill.
        static void DrawStar(Action<int, int, Color32> px, int cx, int cy, int r, Color32 c)
        {
            if (r < 1) return;
            const int n = 10;
            var vx = new float[n];
            var vy = new float[n];
            for (int i = 0; i < n; i++)
            {
                float rad = (i % 2 == 0) ? r : r * 0.42f;
                float ang = Mathf.PI / 2f + i * Mathf.PI / 5f; // start pointing up
                vx[i] = cx + rad * Mathf.Cos(ang);
                vy[i] = cy + rad * Mathf.Sin(ang);
            }
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if (PointInPoly(x + 0.5f, y + 0.5f, vx, vy)) px(x, y, c);
        }

        static readonly int[,] _starField = { { 60, 190 }, { 150, 200 }, { 200, 120 }, { 80, 80 }, { 170, 55 } };
        static void StarScatter(Action<int, int, Color32> px, Color32 c, int r)
        {
            for (int i = 0; i < _starField.GetLength(0); i++)
                DrawStar(px, _starField[i, 0], _starField[i, 1], r, c);
        }

        static bool PointInPoly(float px, float py, float[] vx, float[] vy)
        {
            bool inside = false;
            int n = vx.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((vy[i] > py) != (vy[j] > py)) &&
                    (px < (vx[j] - vx[i]) * (py - vy[i]) / (vy[j] - vy[i]) + vx[i]))
                    inside = !inside;
            }
            return inside;
        }

        // Single upward-tapering flame tongue rising from the bottom.
        static void FlameTongue(Action<int, int, Color32> px, int cx, int height)
        {
            if (height < 1) return;
            for (int y = 0; y < height && y < RegionH; y++)
            {
                float t = (float)y / height;
                int hw = Mathf.RoundToInt(Mathf.Lerp(22, 2, t));
                Color32 col = Lerp(C(255, 120, 0), C(255, 240, 120), t);
                for (int x = cx - hw; x <= cx + hw; x++) px(x, y, col);
            }
        }

        // ---- Nations tab (28) ---------------------------------------------

        static void BuildNations(List<Design> l)
        {
            Add(l, "France", DesignTab.Nations, px => VTriband(px, C(0, 85, 164), White, C(206, 17, 38)));
            Add(l, "Italy", DesignTab.Nations, px => VTriband(px, C(0, 140, 69), White, C(206, 43, 55)));
            Add(l, "Ireland", DesignTab.Nations, px => VTriband(px, C(22, 155, 98), White, C(255, 136, 62)));
            Add(l, "Belgium", DesignTab.Nations, px => VTriband(px, Black, C(253, 218, 36), C(239, 51, 64)));
            Add(l, "Nigeria", DesignTab.Nations, px => VTriband(px, C(0, 135, 81), White, C(0, 135, 81)));

            Add(l, "Germany", DesignTab.Nations, px => HTriband(px, Black, C(221, 0, 0), Gold));
            Add(l, "Netherlands", DesignTab.Nations, px => HTriband(px, C(174, 28, 40), White, C(33, 70, 139)));
            Add(l, "Russia", DesignTab.Nations, px => HTriband(px, White, C(0, 57, 166), C(213, 43, 30)));

            Add(l, "Spain", DesignTab.Nations, px =>
            {
                HBand(px, 0, RegionH / 4 - 1, C(198, 11, 30));
                HBand(px, RegionH / 4, 3 * RegionH / 4 - 1, C(255, 196, 0));
                HBand(px, 3 * RegionH / 4, RegionH - 1, C(198, 11, 30));
            });

            Add(l, "Argentina", DesignTab.Nations, px =>
            {
                HTriband(px, C(117, 170, 219), White, C(117, 170, 219));
                Disc(px, W / 2, RegionH / 2, 26, C(247, 205, 70));
            });

            Add(l, "Colombia", DesignTab.Nations, px =>
            {
                HBand(px, 0, RegionH / 4 - 1, C(206, 17, 38));
                HBand(px, RegionH / 4, RegionH / 2 - 1, C(0, 56, 147));
                HBand(px, RegionH / 2, RegionH - 1, C(252, 209, 22));
            });

            Add(l, "England", DesignTab.Nations, px =>
            {
                FillRegion(px, White);
                VBand(px, W / 2 - 16, W / 2 + 16, C(206, 17, 38));
                HBand(px, RegionH / 2 - 16, RegionH / 2 + 16, C(206, 17, 38));
            });

            Add(l, "Switzerland", DesignTab.Nations, px =>
            {
                FillRegion(px, C(210, 16, 52));
                Rect(px, W / 2 - 18, 78, W / 2 + 18, 178, White);
                Rect(px, 78, RegionH / 2 - 18, 178, RegionH / 2 + 18, White);
            });

            Add(l, "Denmark", DesignTab.Nations, px => NordicCross(px, C(198, 12, 48), White, 100, 128, 34));
            Add(l, "Sweden", DesignTab.Nations, px => NordicCross(px, C(0, 82, 147), C(254, 205, 0), 100, 128, 34));
            Add(l, "Finland", DesignTab.Nations, px => NordicCross(px, White, C(0, 53, 128), 100, 128, 34));

            Add(l, "Japan", DesignTab.Nations, px =>
            {
                FillRegion(px, White);
                Disc(px, W / 2, RegionH / 2, 52, C(188, 0, 45));
            });

            Add(l, "Bangladesh", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 106, 78));
                Disc(px, 112, RegionH / 2, 48, C(244, 42, 65));
            });

            Add(l, "Poland", DesignTab.Nations, px =>
            {
                HBand(px, 0, RegionH / 2 - 1, C(220, 20, 60));
                HBand(px, RegionH / 2, RegionH - 1, White);
            });

            Add(l, "Ukraine", DesignTab.Nations, px =>
            {
                HBand(px, 0, RegionH / 2 - 1, C(255, 213, 0));
                HBand(px, RegionH / 2, RegionH - 1, C(0, 91, 187));
            });

            Add(l, "Austria", DesignTab.Nations, px => HTriband(px, C(237, 41, 57), White, C(237, 41, 57)));
            Add(l, "Peru", DesignTab.Nations, px => VTriband(px, C(217, 16, 35), White, C(217, 16, 35)));

            Add(l, "Jamaica", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 150, 57));
                Saltire(px, C(255, 209, 0), 14);
            });

            Add(l, "Brazil", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 151, 57));
                Diamond(px, W / 2, RegionH / 2, 96, 96, C(255, 223, 0));
                Disc(px, W / 2, RegionH / 2, 40, C(0, 39, 118));
            });

            Add(l, "Portugal", DesignTab.Nations, px =>
            {
                VBand(px, 0, (int)(W * 0.4f) - 1, C(0, 102, 0));
                VBand(px, (int)(W * 0.4f), W - 1, C(200, 20, 40));
            });

            Add(l, "Greece", DesignTab.Nations, px =>
            {
                Hoops(px, 9, C(13, 94, 175), White);
                int cs = (RegionH * 5) / 9;
                Rect(px, 0, RegionH - cs, cs, RegionH - 1, C(13, 94, 175));
                int ccx = cs / 2;
                int ccy = RegionH - cs / 2;
                Rect(px, ccx - 10, RegionH - cs, ccx + 10, RegionH - 1, White);
                Rect(px, 0, ccy - 10, cs, ccy + 10, White);
            });

            Add(l, "USA", DesignTab.Nations, px =>
            {
                Hoops(px, 13, C(178, 34, 52), White);
                int cantonW = W / 2;
                int cantonH = (RegionH * 7) / 13;
                Rect(px, 0, RegionH - cantonH, cantonW, RegionH - 1, C(60, 59, 110));
                for (int gy = 0; gy < 4; gy++)
                    for (int gx = 0; gx < 5; gx++)
                        Disc(px, 15 + gx * 24, RegionH - cantonH + 15 + gy * 24, 4, White);
            });

            Add(l, "Mexico", DesignTab.Nations, px =>
            {
                VTriband(px, C(0, 104, 71), White, C(206, 17, 38));
                Disc(px, W / 2, RegionH / 2, 16, C(120, 80, 40));
            });
        }

        // ---- ClassicKits tab (6) ------------------------------------------

        static void BuildClassic(List<Design> l)
        {
            Add(l, "Vertical Stripes", DesignTab.ClassicKits, px => Stripes(px, 8, C(198, 40, 50), White));
            Add(l, "Horizontal Hoops", DesignTab.ClassicKits, px => Hoops(px, 8, C(0, 130, 70), White));

            Add(l, "Halves", DesignTab.ClassicKits, px =>
            {
                VBand(px, 0, W / 2 - 1, C(120, 20, 40));
                VBand(px, W / 2, W - 1, C(90, 160, 210));
            });

            Add(l, "Quarters", DesignTab.ClassicKits, px =>
            {
                Color32 a = C(110, 20, 50), b = C(120, 180, 220);
                Rect(px, 0, RegionH / 2, W / 2 - 1, RegionH - 1, a);
                Rect(px, W / 2, RegionH / 2, W - 1, RegionH - 1, b);
                Rect(px, 0, 0, W / 2 - 1, RegionH / 2 - 1, b);
                Rect(px, W / 2, 0, W - 1, RegionH / 2 - 1, a);
            });

            Add(l, "Sash", DesignTab.ClassicKits, px =>
            {
                FillRegion(px, White);
                DiagStripe(px, C(200, 30, 50), 22, true);
            });

            Add(l, "Pinstripes", DesignTab.ClassicKits, px =>
            {
                FillRegion(px, C(20, 30, 70));
                for (int x = 8; x < W; x += 20) VBand(px, x, x + 1, White);
            });
        }

        // ---- Patterns tab (6) ---------------------------------------------

        static void BuildPatterns(List<Design> l)
        {
            Add(l, "Checkerboard", DesignTab.Patterns, px => Checker(px, 8, Black, White));

            Add(l, "Camo", DesignTab.Patterns, px =>
            {
                FillRegion(px, C(78, 90, 50));
                int[,] blobs = { { 60, 190, 40 }, { 120, 150, 50 }, { 200, 200, 45 },
                                 { 40, 80, 55 }, { 180, 60, 40 }, { 150, 110, 38 }, { 90, 40, 42 } };
                Color32[] greens = { C(50, 70, 35), C(100, 120, 60), C(35, 50, 25) };
                for (int i = 0; i < blobs.GetLength(0); i++)
                    Disc(px, blobs[i, 0], blobs[i, 1], blobs[i, 2], greens[i % 3]);
            });

            Add(l, "Polka Dots", DesignTab.Patterns, px =>
            {
                FillRegion(px, C(30, 40, 80));
                PolkaDots(px, 40, 10, White);
            });

            Add(l, "Chevrons", DesignTab.Patterns, px => Chevrons(px, 26, C(40, 40, 60), C(230, 200, 60)));
            Add(l, "Vertical Gradient", DesignTab.Patterns, px => Gradient(px, C(20, 120, 200), C(180, 230, 255)));
            Add(l, "Diamonds", DesignTab.Patterns, px => Diamonds(px, 48, C(150, 40, 60), C(230, 210, 120)));
        }

        // ---- Bold tab (6) -------------------------------------------------

        static void BuildBold(List<Design> l)
        {
            Add(l, "Flames", DesignTab.Bold, px =>
            {
                Gradient(px, C(120, 20, 0), C(20, 10, 30));
                int[] fx = { 40, 90, 140, 190, 220, 15 };
                int[] fh = { 150, 190, 130, 170, 110, 120 };
                for (int i = 0; i < fx.Length; i++) FlameTongue(px, fx[i], fh[i]);
            });

            Add(l, "Lightning Bolt", DesignTab.Bold, px =>
            {
                FillRegion(px, C(20, 20, 40));
                float[] vx = { 150, 108, 140, 104, 128, 168 };
                float[] vy = { 236, 138, 138, 22, 128, 128 };
                Color32 bolt = C(255, 224, 60);
                for (int y = 22; y <= 236; y++)
                    for (int x = 104; x <= 168; x++)
                        if (PointInPoly(x + 0.5f, y + 0.5f, vx, vy)) px(x, y, bolt);
            });

            Add(l, "Galaxy", DesignTab.Bold, px =>
            {
                Gradient(px, C(20, 10, 60), C(60, 20, 90));
                for (int i = 0; i < 40; i++)
                {
                    int x = (i * 73 + 19) % W;
                    int y = (i * 149 + 37) % RegionH;
                    int r = ((i * 13) % 3 == 0) ? 2 : 1;
                    Disc(px, x, y, r, White);
                }
            });

            Add(l, "Star Scatter", DesignTab.Bold, px =>
            {
                FillRegion(px, C(30, 30, 60));
                StarScatter(px, C(255, 220, 70), 26);
            });

            Add(l, "Concentric Rings", DesignTab.Bold, px => Rings(px, W / 2, RegionH / 2, 20, C(220, 40, 60), White));
            Add(l, "Fire Gradient", DesignTab.Bold, px => Gradient(px, C(200, 20, 20), C(255, 230, 60)));
        }
    }
}
