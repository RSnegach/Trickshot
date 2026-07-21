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
    // partial: the full world-flag set is split across JerseyDesigns.NationsN.cs files that
    // each add one BuildNationsBatchN(list) method, so they can be generated independently.
    public static partial class JerseyDesigns
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
            BuildNations(list);       // the original core set
            BuildWorldFlags(list);    // the full world set (partial-file batches)
            BuildClassic(list);
            BuildPatterns(list);
            BuildBold(list);

            // Dedupe by name (keep the first definition of any name).
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var deduped = new List<Design>();
            foreach (var d in list)
                if (d != null && d.Name != null && seen.Add(d.Name)) deduped.Add(d);

            // Rebuild tab-by-tab in a fixed tab order. The Nations tab is sorted A-Z; the
            // other tabs keep their authored order. This avoids relying on an unstable Sort
            // across tabs (which could scramble tab groupings).
            var result = new List<Design>(deduped.Count);
            foreach (DesignTab tab in (DesignTab[])System.Enum.GetValues(typeof(DesignTab)))
            {
                var inTab = new List<Design>();
                foreach (var d in deduped) if (d.Tab == tab) inTab.Add(d);
                if (tab == DesignTab.Nations)
                    inTab.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
                result.AddRange(inTab);
            }
            _all = result;
        }

        // World-flag batches live in their own partial files (JerseyDesigns.NationsN.cs) as
        // static methods named BuildNationsBatch1, BuildNationsBatch2, ... each taking a
        // List<Design>. They are discovered by reflection and invoked in name order, so
        // adding a new batch file needs NO edit to this aggregation point.
        static void BuildWorldFlags(List<Design> l)
        {
            var methods = new List<System.Reflection.MethodInfo>();
            foreach (var m in typeof(JerseyDesigns).GetMethods(
                         System.Reflection.BindingFlags.Static |
                         System.Reflection.BindingFlags.NonPublic |
                         System.Reflection.BindingFlags.Public))
            {
                if (!m.Name.StartsWith("BuildNationsBatch")) continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(List<Design>))
                    methods.Add(m);
            }
            // Deterministic order: batch number ascending (BuildNationsBatch2 before ...10).
            methods.Sort((a, b) => BatchIndex(a.Name).CompareTo(BatchIndex(b.Name)));
            var args = new object[] { l };
            foreach (var m in methods) m.Invoke(null, args);
        }

        static int BatchIndex(string name)
        {
            int i = "BuildNationsBatch".Length;
            return (i < name.Length && int.TryParse(name.Substring(i), out int n)) ? n : 0;
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

        // ---- extended primitives for world flags (local 0..255 region space) ----

        // Filled triangle through 3 points (bbox-limited, half-open even-odd fill).
        static void Tri(Action<int, int, Color32> px, int ax, int ay, int bx, int by, int cx, int cy, Color32 col)
        {
            var vx = new float[] { ax, bx, cx };
            var vy = new float[] { ay, by, cy };
            int minX = Mathf.Max(0, Mathf.Min(ax, Mathf.Min(bx, cx)));
            int maxX = Mathf.Min(W - 1, Mathf.Max(ax, Mathf.Max(bx, cx)));
            int minY = Mathf.Max(0, Mathf.Min(ay, Mathf.Min(by, cy)));
            int maxY = Mathf.Min(RegionH - 1, Mathf.Max(ay, Mathf.Max(by, cy)));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (PointInPoly(x + 0.5f, y + 0.5f, vx, vy)) px(x, y, col);
        }

        // N equal VERTICAL bands, left -> right = cols[0] -> cols[last].
        static void VBands(Action<int, int, Color32> px, params Color32[] cols)
        {
            if (cols == null || cols.Length == 0) return;
            int n = cols.Length;
            for (int x = 0; x < W; x++)
            {
                int band = Mathf.Min(n - 1, x * n / W);
                for (int y = 0; y < RegionH; y++) px(x, y, cols[band]);
            }
        }

        // N equal HORIZONTAL bands, cols[0] = TOP (highest rows) -> cols[last] = bottom.
        static void HBands(Action<int, int, Color32> px, params Color32[] cols)
        {
            if (cols == null || cols.Length == 0) return;
            int n = cols.Length;
            for (int y = 0; y < RegionH; y++)
            {
                // y is up, so the TOP of the flag is the HIGH rows -> invert the index.
                int fromTop = Mathf.Min(n - 1, (RegionH - 1 - y) * n / RegionH);
                for (int x = 0; x < W; x++) px(x, y, cols[fromTop]);
            }
        }

        // Upright plus-cross (full span) centred at (cx,cy) with the given arm half-thickness.
        static void PlusCross(Action<int, int, Color32> px, int cx, int cy, int halfThick, Color32 col)
        {
            VBand(px, cx - halfThick, cx + halfThick, col);
            HBand(px, cy - halfThick, cy + halfThick, col);
        }

        // Crescent: a disc of `col`, then an offset disc of `bg` carved out of it. dxOffset>0
        // opens the crescent toward +x (right). Draw AFTER the field is filled with bg.
        static void Crescent(Action<int, int, Color32> px, int cx, int cy, int rOuter, int dxOffset, Color32 bg, Color32 col)
        {
            Disc(px, cx, cy, rOuter, col);
            Disc(px, cx + dxOffset, cy, rOuter - 2, bg);
        }

        // Generalized star: `points`-pointed, rotated `rotDeg` (0 = one point straight up).
        static void StarN(Action<int, int, Color32> px, int cx, int cy, int r, int points, float rotDeg, Color32 col)
        {
            if (r < 1 || points < 2) return;
            int n = points * 2;
            var vx = new float[n];
            var vy = new float[n];
            float inner = r * (points >= 6 ? 0.55f : 0.40f);
            float rot = rotDeg * Mathf.Deg2Rad;
            for (int i = 0; i < n; i++)
            {
                float rad = (i % 2 == 0) ? r : inner;
                float ang = Mathf.PI / 2f + rot + i * Mathf.PI / points;
                vx[i] = cx + rad * Mathf.Cos(ang);
                vy[i] = cy + rad * Mathf.Sin(ang);
            }
            int minX = Mathf.Max(0, cx - r), maxX = Mathf.Min(W - 1, cx + r);
            int minY = Mathf.Max(0, cy - r), maxY = Mathf.Min(RegionH - 1, cy + r);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (PointInPoly(x + 0.5f, y + 0.5f, vx, vy)) px(x, y, col);
        }

        // A ring of `count` stars evenly spaced on a circle of radius `ringR`.
        static void StarRing(Action<int, int, Color32> px, int cx, int cy, int ringR, int starR, int count, Color32 col)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i * (Mathf.PI * 2f / count) - Mathf.PI / 2f;
                int sx = cx + Mathf.RoundToInt(ringR * Mathf.Cos(a));
                int sy = cy + Mathf.RoundToInt(ringR * Mathf.Sin(a));
                StarN(px, sx, sy, starR, 5, 0f, col);
            }
        }

        // Sun: a core disc plus `rays` triangular rays. rotDeg rotates the ray pattern.
        static void Sun(Action<int, int, Color32> px, int cx, int cy, int coreR, int rayLen, int rays, float rotDeg, Color32 col)
        {
            float rot = rotDeg * Mathf.Deg2Rad;
            float half = (Mathf.PI / rays) * 0.35f;   // ray base half-angle
            for (int k = 0; k < rays; k++)
            {
                float a = rot + k * (Mathf.PI * 2f / rays);
                int tipX = cx + Mathf.RoundToInt((coreR + rayLen) * Mathf.Cos(a));
                int tipY = cy + Mathf.RoundToInt((coreR + rayLen) * Mathf.Sin(a));
                int b1x = cx + Mathf.RoundToInt(coreR * Mathf.Cos(a - half));
                int b1y = cy + Mathf.RoundToInt(coreR * Mathf.Sin(a - half));
                int b2x = cx + Mathf.RoundToInt(coreR * Mathf.Cos(a + half));
                int b2y = cy + Mathf.RoundToInt(coreR * Mathf.Sin(a + half));
                Tri(px, tipX, tipY, b1x, b1y, b2x, b2y, col);
            }
            Disc(px, cx, cy, coreR, col);
        }

        // Diagonal split of the whole region into two colours. rising=true -> the line runs
        // bottom-left to top-right; colLow fills below/right of it, colHigh above/left.
        static void DiagHalf(Action<int, int, Color32> px, Color32 colLow, Color32 colHigh, bool rising)
        {
            for (int y = 0; y < RegionH; y++)
                for (int x = 0; x < W; x++)
                {
                    bool high = rising ? (y * (long)W > x * (long)RegionH)
                                       : (y * (long)W > (W - 1 - x) * (long)RegionH);
                    px(x, y, high ? colHigh : colLow);
                }
        }

        // A Union Jack drawn into the sub-rect [x0,x1] x [y0,y1] (inclusive). Best-effort at
        // small sizes: blue field, white then red saltire, white then red upright cross.
        static void UnionJack(Action<int, int, Color32> px, int x0, int y0, int x1, int y1)
        {
            Color32 ujBlue = C(1, 33, 105), ujRed = C(200, 16, 46), ujWhite = C(245, 245, 245);
            if (x1 < x0) { int t = x0; x0 = x1; x1 = t; }
            if (y1 < y0) { int t = y0; y0 = y1; y1 = t; }
            float w = Mathf.Max(1, x1 - x0), h = Mathf.Max(1, y1 - y0);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float fx = (x - x0) / w, fy = (y - y0) / h;
                    Color32 c = ujBlue;
                    // white saltire (both diagonals), then thinner red saltire on top
                    float dSalt = Mathf.Min(Mathf.Abs(fx - fy), Mathf.Abs(fx - (1f - fy)));
                    if (dSalt < 0.16f) c = ujWhite;
                    if (dSalt < 0.07f) c = ujRed;
                    // white upright cross, then thinner red cross on top (drawn last = on top)
                    if (Mathf.Abs(fx - 0.5f) < 0.15f || Mathf.Abs(fy - 0.5f) < 0.15f) c = ujWhite;
                    if (Mathf.Abs(fx - 0.5f) < 0.075f || Mathf.Abs(fy - 0.5f) < 0.075f) c = ujRed;
                    px(x, y, c);
                }
        }

        // ---- optional image-asset overlays (Resources) ----------------------
        // Some emblems (Soviet hammer-and-sickle, Kyrgyz sun) can be supplied as real PNGs in
        // Assets/Resources/<name>.png (Read/Write Enabled in the import settings). If the asset
        // is present it is blitted with alpha into the region; if not, callers fall back to a
        // hand-drawn emblem. Loaded textures are cached (including a null entry, so a missing
        // asset is not re-queried on every bake). Tries a "flags/" subfolder first, then the
        // Resources root, so it works whether the PNG is filed under Resources/flags/ or
        // directly under Resources/.
        static readonly Dictionary<string, Texture2D> _overlayCache = new Dictionary<string, Texture2D>();

        static Texture2D LoadOverlay(string name)
        {
            if (_overlayCache.TryGetValue(name, out var t)) return t;
            Texture2D tex = null;
            try { tex = Resources.Load<Texture2D>("flags/" + name) ?? Resources.Load<Texture2D>(name); }
            catch { tex = null; }
            _overlayCache[name] = tex;   // may be null; cached to avoid repeat lookups
            return tex;
        }

        // Blit a Resources PNG centred at (cx,cy) scaled to fit within (boxW x boxH), alpha
        // composited over whatever is already in the region. Returns false if the asset is
        // absent (or not readable) so the caller can draw its fallback instead.
        static bool OverlayImage(Action<int, int, Color32> px, string name, int cx, int cy, int boxW, int boxH)
        {
            var tex = LoadOverlay(name);
            if (tex == null) return false;
            Color32[] src;
            try { src = tex.GetPixels32(); }   // requires Read/Write Enabled
            catch { return false; }
            int tw = tex.width, thh = tex.height;
            if (tw <= 0 || thh <= 0) return false;
            // Scale to fit the box while preserving aspect. Note: GetPixels32 is row-major with
            // row 0 at the BOTTOM (y-up), matching our px() convention, so no vertical flip.
            float scale = Mathf.Min(boxW / (float)tw, boxH / (float)thh);
            int dw = Mathf.Max(1, Mathf.RoundToInt(tw * scale));
            int dh = Mathf.Max(1, Mathf.RoundToInt(thh * scale));
            int x0 = cx - dw / 2, y0 = cy - dh / 2;
            for (int dy = 0; dy < dh; dy++)
                for (int dx = 0; dx < dw; dx++)
                {
                    int sxp = Mathf.Clamp(dx * tw / dw, 0, tw - 1);
                    int syp = Mathf.Clamp(dy * thh / dh, 0, thh - 1);
                    Color32 c = src[syp * tw + sxp];
                    if (c.a < 8) continue;   // treat near-transparent as clear
                    px(x0 + dx, y0 + dy, c);
                }
            return true;
        }

        // ---- Nations tab -----------------------------------------------------

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
                // Coat of arms toward the hoist, on the yellow band. Best-effort: the Pillars
                // of Hercules flanking a crowned quartered shield (Castile/Leon/Aragon/Navarre
                // + Granada pomegranate at the base).
                int cx = 88, cy = RegionH / 2;
                Color32 castR = C(170, 20, 30), castY = C(240, 200, 30), whiteF = White,
                        crimson = C(170, 20, 30), crown = C(214, 178, 70), pillar = C(210, 60, 40);
                // Pillars of Hercules (red columns with caps) either side of the shield.
                Rect(px, cx - 40, cy - 30, cx - 32, cy + 34, pillar);
                Rect(px, cx - 44, cy + 34, cx - 28, cy + 40, pillar);
                Rect(px, cx + 32, cy - 30, cx + 40, cy + 34, pillar);
                Rect(px, cx + 28, cy + 34, cx + 44, cy + 40, pillar);
                // Shield: rounded rectangle, quartered.
                Rect(px, cx - 24, cy - 30, cx + 24, cy + 30, whiteF);       // shield ground
                Rect(px, cx - 22, cy - 28, cx - 1, cy + 2, crimson);        // Q1 Castile (red)
                Rect(px, cx + 1, cy - 28, cx + 22, cy + 2, whiteF);         // Q2 Leon (white)
                Rect(px, cx - 22, cy + 4, cx - 1, cy + 28, castY);          // Q3 Aragon (gold)
                Rect(px, cx + 1, cy + 4, cx + 22, cy + 28, crimson);        // Q4 Navarre (red)
                Disc(px, cx, cy - 13, 4, castY);                           // Castile castle hint
                Disc(px, cx + 11, cy - 13, 4, castR);                      // Leon lion hint
                for (int i = 0; i < 4; i++) Rect(px, cx - 20 + i * 6, cy + 6, cx - 17 + i * 6, cy + 26, castR); // Aragon pales
                Disc(px, cx, cy - 34, 5, crimson);                         // Granada pomegranate at base
                // Royal crown above the shield.
                Rect(px, cx - 16, cy + 30, cx + 16, cy + 36, crown);
                Tri(px, cx - 16, cy + 36, cx - 8, cy + 46, cx, cy + 36, crown);
                Tri(px, cx - 4, cy + 36, cx, cy + 48, cx + 4, cy + 36, crown);
                Tri(px, cx, cy + 36, cx + 8, cy + 46, cx + 16, cy + 36, crown);
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
                // Hinomaru: red disc diameter = 3/5 of the flag height -> radius ~= 0.3*RegionH.
                FillRegion(px, White);
                Disc(px, W / 2, RegionH / 2, 77, C(188, 0, 45));
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
                // Gold saltire splits the flag into 4 triangles: green top+bottom,
                // black hoist+fly (left+right). The wedge boundaries are the same two
                // diagonals the Saltire helper draws (y=x and x+y=RegionH-1).
                Color32 green = C(0, 155, 58), gold = C(255, 209, 0);
                int n = RegionH - 1;
                for (int y = 0; y < RegionH; y++)
                    for (int x = 0; x < W; x++)
                    {
                        bool aboveRising = y > x;          // vs the y=x diagonal
                        bool aboveFalling = (x + y) > n;   // vs the y=n-x diagonal
                        bool leftWedge = aboveRising && !aboveFalling;   // hoist triangle
                        bool rightWedge = !aboveRising && aboveFalling;  // fly triangle
                        px(x, y, (leftWedge || rightWedge) ? Black : green);
                    }
                Saltire(px, gold, 14);
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
                for (int gy = 0; gy < 6; gy++)
                    for (int gx = 0; gx < 5; gx++)
                        Disc(px, 15 + gx * 24, RegionH - cantonH + 15 + gy * 24, 4, White);
            });

            Add(l, "Mexico", DesignTab.Nations, px =>
            {
                VTriband(px, C(0, 104, 71), White, C(206, 17, 38));
                // Best-effort coat of arms: an eagle on a nopal over water. Approximate with a
                // brown eagle body + spread wings, green cactus pad below, and a small blue arc.
                int cx = W / 2, cy = RegionH / 2;
                Color32 brown = C(105, 70, 38), cactus = C(40, 120, 55), water = C(70, 120, 190);
                Disc(px, cx, cy - 4, 8, brown);                       // eagle body
                Tri(px, cx, cy + 2, cx - 22, cy + 20, cx - 6, cy + 6, brown);   // left wing
                Tri(px, cx, cy + 2, cx + 22, cy + 20, cx + 6, cy + 6, brown);   // right wing
                Tri(px, cx + 6, cy - 4, cx + 16, cy - 2, cx + 6, cy + 2, brown); // beak/head
                Disc(px, cx, cy - 16, 6, cactus);                     // nopal pad
                Disc(px, cx - 9, cy - 14, 4, cactus);
                Disc(px, cx + 9, cy - 14, 4, cactus);
                Ring(px, cx, cy - 22, 16, 13, water);                 // water/laurel arc hint
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
                // Stormy sky gradient (dark indigo at the bottom to near-black at the top).
                Gradient(px, C(30, 32, 62), C(8, 8, 20));
                // The bolt is a zig-zag centreline stamped with thick discs, so it is always a
                // clean continuous shape. Three passes: a wide soft blue glow, a mid amber
                // body, and a thin bright-white core, for a lit, glowing look. Top = high y.
                var pts = new (int x, int y)[]
                {
                    (150, 244), (108, 156), (140, 150), (96, 60)
                };
                void Stroke(int rad, Color32 col)
                {
                    for (int s = 0; s < pts.Length - 1; s++)
                    {
                        var a = pts[s]; var b = pts[s + 1];
                        int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
                        for (int t = 0; t <= steps; t++)
                        {
                            int x = a.x + (b.x - a.x) * t / steps;
                            int y = a.y + (b.y - a.y) * t / steps;
                            Disc(px, x, y, rad, col);
                        }
                    }
                }
                Stroke(22, C(70, 90, 200));    // outer glow (blue-violet)
                Stroke(13, C(120, 150, 255));  // inner glow
                Stroke(11, C(255, 196, 40));   // amber body
                Stroke(4, C(255, 248, 170));   // hot white-yellow core
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
