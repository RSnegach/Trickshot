using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // THE ELEPHANT. Its ears, tusks and tack used to be raw decor primitives: two slabs, capsules
    // half-buried in the skull, a box pushed through the head for a cloth. The decor table still
    // owns every COLLIDER (the ear boxes and the tusk chords are gameplay: the header surface and
    // the reach must not move with a cosmetic pick), but those rows are Hidden now and the visuals
    // are built here on top of them: a hinged fan sheet inside each ear box, a swept tapering ivory
    // tube along the arc the chord colliders approximate, and a cap, buckled bands and a lofted
    // blanket on the real body pieces for tack.
    public static partial class Cosmetics
    {
        // ---- static textures (built once, never per body: the customize preview rebuilds every drag frame)
        static class ElephantTex
        {
            static Texture2D _ear, _tusk, _weave, _damask;
            public static Texture2D Ear    => _ear    != null ? _ear    : _ear    = BuildEar();
            public static Texture2D Tusk   => _tusk   != null ? _tusk   : _tusk   = BuildTusk();
            public static Texture2D Weave  => _weave  != null ? _weave  : _weave  = Tile("ElephantWeave", 64, 64, (u, v) => { float w = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 16f) * Mathf.Sin(v * Mathf.PI * 16f)); return new Color(0.90f + 0.10f * w, 0.90f + 0.10f * w, 0.90f + 0.10f * w, 1f); });
            public static Texture2D Damask => _damask != null ? _damask : _damask = Tile("ElephantDamask", 128, 128, (u, v) =>
            {
                // A diamond lattice with a lozenge in every cell, low contrast so the tint carries it.
                float a = Mathf.Abs(Mathf.Repeat(u + v, 1f) - 0.5f), b = Mathf.Abs(Mathf.Repeat(u - v, 1f) - 0.5f);
                float line = Mathf.Min(a, b) < 0.03f ? 0.86f : 1f;
                float cx = Mathf.Repeat(u + v, 1f) - 0.5f, cy = Mathf.Repeat(u - v, 1f) - 0.5f;
                float loz = Mathf.Abs(cx) + Mathf.Abs(cy) < 0.16f ? 0.90f : 1f;
                float l = 0.96f * line * loz;
                return new Color(l, l, l, 1f);
            });

            static Texture2D Tile(string name, int w, int h, Func<float, float, Color> f)
            {
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
                var t = new Texture2D(w, h, TextureFormat.RGB24, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                t.SetPixels(px); t.Apply(true, true);
                return t;
            }

            static Texture2D BuildEar()
            {
                // 256 x 128: left half the outer face, right half the back of the ear (u = along the
                // margin from the top, v = hinge -> margin). Five vein ridges fan from the hinge.
                const int W = 256, H = 128;
                var px = new Color[W * H];
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool inner = x >= W / 2;
                    float u = ((x % (W / 2)) + 0.5f) / (W / 2f), v = (y + 0.5f) / H;
                    float vein = 0f;
                    for (int k = 0; k < 5; k++)
                    {
                        float uk = 0.12f + k * 0.19f + 0.03f * Mathf.Sin(v * 9f + k * 1.7f);
                        vein = Mathf.Max(vein, 1f - Smooth(0.006f, 0.022f, Mathf.Abs(u - uk)));
                    }
                    vein *= Smooth(0.12f, 0.45f, v);
                    float mottle = (Mathf.PerlinNoise(u * 11f + 3f, v * 7f + 1f) - 0.5f) * 0.10f;
                    float wrinkle = 0.03f * Mathf.Sin(v * 60f + 3f * Mathf.Sin(u * 12f));
                    float lum = 1f - 0.16f * vein + mottle + wrinkle;
                    var c = new Color(lum, lum, lum, 1f);
                    if (inner)
                    {
                        // Thin skin over blood toward the margin: warmer and pinker on the back.
                        float pink = 0.32f * Smooth(0.55f, 0.95f, v);
                        c = Color.Lerp(c, new Color(lum * 1.02f, lum * 0.80f, lum * 0.78f, 1f), pink);
                    }
                    if (y == H - 1) c = new Color(Mathf.Min(1f, c.r * 1.15f), Mathf.Min(1f, c.g * 1.15f), Mathf.Min(1f, c.b * 1.15f), 1f);   // rim band row: scar-pale cut edges
                    px[y * W + x] = c;
                }
                var t = new Texture2D(W, H, TextureFormat.RGB24, true) { name = "ElephantEar", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                t.SetPixels(px); t.Apply(true, true);
                return t;
            }

            static Texture2D BuildTusk()
            {
                // v along the tusk: a warmer, darker root shading to clean ivory at the tip.
                const int W = 4, H = 32;
                var px = new Color[W * H];
                for (int y = 0; y < H; y++)
                {
                    float v = (y + 0.5f) / H;
                    float l = Mathf.Lerp(0.78f, 1f, Smooth(0f, 0.65f, v));
                    var c = new Color(l, l * Mathf.Lerp(0.95f, 1f, v), l * Mathf.Lerp(0.86f, 1f, v), 1f);
                    for (int x = 0; x < W; x++) px[y * W + x] = c;
                }
                var t = new Texture2D(W, H, TextureFormat.RGB24, false) { name = "ElephantTusk", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                t.SetPixels(px); t.Apply(false, true);
                return t;
            }
        }

        // ---- small mesh helpers ------------------------------------------------------------------
        /// <summary>MeshGen.Param wound so its front face points along <paramref name="outwardAt"/>(u, v)
        /// at the patch centre. Saves every caller from reasoning about Cross(du, dv) by hand.</summary>
        static Mesh ParamOut(Func<float, float, Vector3> f, int nu, int nv, Func<float, float, Vector3> outwardAt, bool wrapU = false, float uvU = 1f, float uvV = 1f)
        {
            const float e = 1e-3f;
            Vector3 p = f(0.5f, 0.5f);
            Vector3 n = Vector3.Cross(f(0.5f + e, 0.5f) - p, f(0.5f, 0.5f + e) - p);
            bool flip = Vector3.Dot(n, outwardAt(0.5f, 0.5f)) < 0f;
            return MeshGen.Param(f, nu, nv, wrapU: wrapU, flip: flip, uvRepeatU: uvU, uvRepeatV: uvV);
        }
        static Mesh ParamOut(Func<float, float, Vector3> f, int nu, int nv, Vector3 outward, bool wrapU = false, float uvU = 1f, float uvV = 1f)
            => ParamOut(f, nu, nv, (u, v) => outward, wrapU, uvU, uvV);

        static void RemapUv(Mesh m, float uOff, float uScale, float vFixed = -1f)
        {
            var uv = m.uv;
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(uOff + uv[i].x * uScale, vFixed >= 0f ? vFixed : uv[i].y);
            m.uv = uv;
        }

        static float CatRom1(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
        static Vector2 CatRom2(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
            => new Vector2(CatRom1(p0.x, p1.x, p2.x, p3.x, t), CatRom1(p0.y, p1.y, p2.y, p3.y, t));
        /// <summary>Open Catmull-Rom through every point, u 0..1 over the whole run.</summary>
        static Vector2 Spline2At(Vector2[] pts, float u)
        {
            int n = pts.Length;
            float tt = Mathf.Clamp01(u) * (n - 1);
            int k = Mathf.Min(Mathf.FloorToInt(tt), n - 2);
            return CatRom2(pts[Mathf.Max(k - 1, 0)], pts[k], pts[k + 1], pts[Mathf.Min(k + 2, n - 1)], tt - k);
        }

        /// <summary>A dome (hemisphere) of radius r about local +Y, base on y = 0.</summary>
        static Mesh DomeMesh(float r, int seg = 14)
        {
            var prof = new Vector2[7];
            for (int i = 0; i < 7; i++) { float a = i / 6f * Mathf.PI * 0.5f; prof[i] = new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a)); }
            return MeshGen.Lathe(prof, seg);
        }

        // ---- ears ------------------------------------------------------------------------------
        // The Plain outline, unit HEAD frame (y, z), from the top-front round the margin to the
        // bottom-front, drawn against the Plain box (centre (0.02, -0.075), 0.40 x 0.36). Wide's
        // bigger box scales it through the dims ratio, so the outline never leaves the collider.
        static readonly Vector2[] EarOutline =
        {
            new Vector2(0.19f, 0.07f), new Vector2(0.215f, -0.03f), new Vector2(0.17f, -0.16f), new Vector2(0.10f, -0.245f),
            new Vector2(-0.02f, -0.25f), new Vector2(-0.10f, -0.21f), new Vector2(-0.165f, -0.13f), new Vector2(-0.175f, -0.05f),
            new Vector2(-0.145f, 0.03f),
        };
        static readonly Vector2 EarHingeTop = new Vector2(0.14f, 0.09f), EarHingeBot = new Vector2(-0.12f, 0.06f);
        static readonly Vector2 EarRefCentre = new Vector2(0.02f, -0.075f);

        static float EarUAt(Vector2 target)
        {
            float best = 0f, bd = float.MaxValue;
            for (int i = 0; i <= 200; i++)
            {
                float u = i / 200f;
                float d = (Spline2At(EarOutline, u) - target).sqrMagnitude;
                if (d < bd) { bd = d; best = u; }
            }
            return best;
        }

        /// <summary>
        /// One ear: a fan sheet hinged on a line that is PROJECTED onto the skull (so the root is
        /// buried in the sphere at every build), lying flat against the head at the root and flaring
        /// out to the collider's outer face at the margin. Thick at the hinge, thin at the edge, with
        /// a rolled bead along the top third of the margin. Notched (style 1) and Torn (style 3) are
        /// dips cut into the margin radius; the rim band closes every cut edge.
        /// </summary>
        static void BuildEar(Transform head, ActiveRagdoll rag, int side, Transform box, Vector3 dims, int style, Material mat)
        {
            float g = rag.GirthScale, h = rag.HeightScale, R = rag.HeadVisualRadius, s = side < 0 ? -1f : 1f;
            Vector3 c = box.localPosition;                       // head-local box centre, already scaled
            float ky = dims.y / (0.40f * h), kz = dims.z / (0.36f * g);
            bool wide = kz > 1.2f;
            Vector2 Scaled(Vector2 p)
            {
                Vector2 rel = p - EarRefCentre;
                if (wide && rel.y < -0.05f) rel.y *= 1.06f;      // Wide: the rear lobe hangs further back
                return new Vector2(c.y + rel.x * h * ky, c.z + rel.y * g * kz);
            }
            Vector2 HT = new Vector2(EarHingeTop.x * h, EarHingeTop.y * g), HB = new Vector2(EarHingeBot.x * h, EarHingeBot.y * g);
            // Hinge x on the skull, floored at 0.55R so a hinge point above the silhouette (tall, lean
            // builds) stays finite, then sunk a centimetre.
            float XHinge(Vector2 yz) => s * (Mathf.Sqrt(Mathf.Max(R * R - yz.x * yz.x - yz.y * yz.y, 0.3025f * R * R)) - 0.01f * g);
            float xOut = c.x + s * dims.x * 0.5f;

            float uNotch = EarUAt(new Vector2(0.02f, -0.245f)), uNick = EarUAt(new Vector2(-0.12f, -0.19f));
            float uA = EarUAt(new Vector2(-0.02f, -0.22f)), uB = EarUAt(new Vector2(-0.16f, -0.09f));
            float uTop1 = EarUAt(new Vector2(0.20f, 0.03f)), uTop2 = EarUAt(new Vector2(0.19f, -0.10f)), uLobe = EarUAt(new Vector2(-0.175f, -0.05f));
            var jag = new float[8];
            {
                var rng = new Lcg((uint)(side > 0 ? 7 : 11));
                for (int i = 1; i < 7; i++) jag[i] = ((i & 1) == 0 ? 0.022f : 0.058f) + rng.Range(-0.012f, 0.014f);
            }
            float V(float u, float u0, float hw, float depth) => depth * Mathf.Max(0f, 1f - Mathf.Abs(u - u0) / hw);
            float Dip(float u)
            {
                float d = 0f;
                if (style == 1) d += V(u, uNotch, 0.035f, 0.06f) + V(u, uNick, 0.015f, 0.02f);
                else if (style == 3)
                {
                    if (u > uA && u < uB) { float k = (u - uA) / (uB - uA) * 7f; int i = Mathf.Min((int)k, 6); d += Mathf.Lerp(jag[i], jag[i + 1], k - i); }
                    d += V(u, uTop1, 0.012f, 0.015f) + V(u, uTop2, 0.012f, 0.015f) + V(u, uLobe, 0.05f, 0.05f);
                }
                return d * g;
            }

            Vector2 Hn(float u) => Vector2.Lerp(HT, HB, u);
            Vector2 Mg(float u)
            {
                Vector2 m = Scaled(Spline2At(EarOutline, u)), hn = Hn(u);
                float dist = (m - hn).magnitude, dip = Dip(u);
                return dip > 0f && dist > 1e-4f ? Vector2.Lerp(m, hn, Mathf.Min(dip / dist, 0.6f)) : m;
            }
            float S(float v) => v * v * (3f - 2f * v);
            float Thick(float u, float v) => g * (Mathf.Lerp(0.04f, 0.010f, S(v)) + 0.008f * Smooth(0.88f, 1f, v) * Smooth(0.42f, 0.30f, u));
            // The sheet leaves the skull quickly and is a flat flap at the collider's outer face for
            // most of its area: an ear STANDS OFF the head, it does not wrap round it.
            Vector3 Outer(float u, float v)
            {
                Vector2 hn = Hn(u), yz = Vector2.Lerp(hn, Mg(u), v);
                float xh = XHinge(hn);
                return new Vector3(xh + (xOut - xh) * Smooth(0f, 0.40f, v), yz.x, yz.y);
            }
            Vector3 Inner(float u, float v) { var p = Outer(u, v); p.x -= s * Thick(u, v); return p; }

            var outer = ParamOut(Outer, 64, 10, new Vector3(s, 0f, 0f));
            var inner = ParamOut(Inner, 64, 10, new Vector3(-s, 0f, 0f));
            Vector2 hmid = (HT + HB) * 0.5f;
            var rim = ParamOut((u, w) => Vector3.Lerp(Outer(u, 1f), Inner(u, 1f), w), 64, 1,
                               (u, w) => { var p = Outer(u, 1f); return new Vector3(0f, p.y - hmid.x, p.z - hmid.y); });
            RemapUv(outer, 0f, 0.5f); RemapUv(inner, 0.5f, 0.5f); RemapUv(rim, 0f, 0.5f, 0.995f);
            Piece(head, MeshGen.Combine(outer, inner, rim), mat);
        }

        // ---- tusks -----------------------------------------------------------------------------
        /// <summary>
        /// A tusk: MeshGen.Tube swept along BodyLayout.ElephantTuskArc (the same arc the Hidden chord
        /// colliders were solved from), radius tapering to a point, with a skin collar at the socket
        /// and, for Banded, brass rings riding the tube's own frames.
        /// </summary>
        static void BuildTusk(Transform head, ActiveRagdoll rag, int side, int style, Material ivory, Material skin, Material brass)
        {
            float g = rag.GirthScale;
            BodyLayout.ElephantTuskSpec(style, out _, out _, out float r0);
            const int N = 24;
            var path = new Vector3[N]; var rad = new float[N];
            for (int i = 0; i < N; i++)
            {
                float t = i / (N - 1f);
                BodyLayout.ElephantTuskArc(side, style, t, out var p, out _);
                path[i] = p * g;
                rad[i] = i == N - 1 ? 0.004f * g : r0 * g * Mathf.Pow(1f - 0.92f * t, 0.7f);
            }
            Piece(head, MeshGen.Tube(path, rad, 16, false, true), ivory);

            BodyLayout.ElephantTuskArc(side, style, 0f, out var root, out var d0);
            var lip = MeshGen.Torus(1.15f * r0 * g, 0.012f * g, 20, 8);
            MeshGen.Transform(lip, root * g + d0 * (0.006f * g), Quaternion.FromToRotation(Vector3.up, d0));
            Piece(head, lip, skin);

            if (style == 4)
            {
                var parts = new List<Mesh>();
                foreach (var (t, rr) in new[] { (0.04f, 0.009f), (0.22f, 0.007f), (0.30f, 0.007f) })
                {
                    BodyLayout.ElephantTuskArc(side, style, t, out var p, out var tan);
                    float rt = r0 * g * Mathf.Pow(1f - 0.92f * t, 0.7f);
                    var ring = MeshGen.Torus(rt + 0.003f * g, rr * g, 24, 10);
                    MeshGen.Transform(ring, p * g, Quaternion.FromToRotation(Vector3.up, tan));
                    parts.Add(ring);
                }
                Piece(head, MeshGen.Combine(parts.ToArray()), brass);
            }
        }

        // ---- tack ------------------------------------------------------------------------------
        static Vector3 SphereDir(float az, float ph)   // az 0..1 round from the front (+Z), ph = polar from +Y
        {
            float sa = Mathf.Sin(az * 2f * Mathf.PI), ca = Mathf.Cos(az * 2f * Mathf.PI);
            return new Vector3(Mathf.Sin(ph) * sa, Mathf.Cos(ph), Mathf.Sin(ph) * ca);
        }

        /// <summary>
        /// Head cloth: a cap on the skull whose edge is an azimuth -> polar function (lower over the
        /// nape, higher at the brow, scalloped behind), a frontlet panel hanging down the forehead
        /// from under it, a brass hem on both, three rows of brass studs and a sun medallion on the
        /// poll. No chin strap: the skull is a sphere and the jaw line runs through the trunk root.
        /// </summary>
        static void BuildHeadCloth(Transform head, ActiveRagdoll rag, Material cloth, Material brass)
        {
            float g = rag.GirthScale, R = rag.HeadVisualRadius;
            float[] edgeDeg = { 56f, 59f, 62f, 67f, 72f, 67f, 62f, 59f };
            float Edge(float az)
            {
                float tt = Mathf.Repeat(az, 1f) * 8f; int k = Mathf.FloorToInt(tt); float f = tt - k;
                float e = CatRom1(edgeDeg[(k + 7) % 8], edgeDeg[k % 8], edgeDeg[(k + 1) % 8], edgeDeg[(k + 2) % 8], f);
                float w = Smooth(0.22f, 0.32f, az) * Smooth(0.78f, 0.68f, az);
                e += 3f * (0.5f - 0.5f * Mathf.Cos((az - 0.25f) * 2f * Mathf.PI * 10f)) * w;   // five scallops over the nape half
                return e * Mathf.Deg2Rad;
            }
            const float poleEps = 0.02f;
            Vector3 CapAt(float u, float v, float rad) => SphereDir(u, Mathf.Lerp(poleEps, Edge(u), v)) * rad;
            float rOut = R + 0.008f * g, rIn = R + 0.003f * g;
            var outer = ParamOut((u, v) => CapAt(u, v, rOut), 48, 12, (u, v) => SphereDir(u, Edge(u) * v), wrapU: true, uvU: 8f, uvV: 3f);
            var inner = ParamOut((u, v) => CapAt(u, v, rIn), 48, 12, (u, v) => -SphereDir(u, Edge(u) * v), wrapU: true, uvU: 8f, uvV: 3f);
            var rim = ParamOut((u, w) => Vector3.Lerp(CapAt(u, 1f, rOut), CapAt(u, 1f, rIn), w), 48, 1,
                               (u, w) => SphereDir(u, Edge(u) + 0.02f) - SphereDir(u, Edge(u)), wrapU: true);
            Piece(head, MeshGen.Combine(outer, inner, rim), cloth);
            // Brass hem, 3 degrees wide, riding just proud of the cloth.
            float hemR = R + 0.0105f * g, hemDeg = 3f * Mathf.Deg2Rad;
            Piece(head, ParamOut((u, w) => SphereDir(u, Edge(u) + (w - 0.5f) * hemDeg) * hemR, 64, 1, (u, w) => SphereDir(u, Edge(u)), wrapU: true), brass);

            // Frontlet: under the cap edge, narrowing toward the trunk, scalloped hem, above the trunk root.
            float f0 = 50f * Mathf.Deg2Rad, f1 = 106f * Mathf.Deg2Rad;
            float HalfW(float v) => Mathf.Lerp(0.30f, 0.13f, v);
            float FAz(float u, float v) => (u - 0.5f) * 2f * HalfW(v) / (2f * Mathf.PI);
            float FPh(float u, float v) => Mathf.Lerp(f0, f1 + 3f * Mathf.Deg2Rad * (0.5f - 0.5f * Mathf.Cos(u * 2f * Mathf.PI * 3f)), v);
            float fOut = R + 0.006f * g, fIn = R + 0.002f * g;
            Vector3 FrontAt(float u, float v, float rad) => SphereDir(FAz(u, v), FPh(u, v)) * rad;
            var fo = ParamOut((u, v) => FrontAt(u, v, fOut), 16, 14, (u, v) => SphereDir(FAz(u, v), FPh(u, v)), uvU: 2f, uvV: 3f);
            var fi = ParamOut((u, v) => FrontAt(u, v, fIn), 16, 14, (u, v) => -SphereDir(FAz(u, v), FPh(u, v)), uvU: 2f, uvV: 3f);
            var fr = ParamOut((u, w) => Vector3.Lerp(FrontAt(u, 1f, fOut), FrontAt(u, 1f, fIn), w), 16, 1, (u, w) => SphereDir(FAz(u, 1f), FPh(u, 1f) + 0.02f) - SphereDir(FAz(u, 1f), FPh(u, 1f)));
            var fl = ParamOut((v, w) => Vector3.Lerp(FrontAt(0f, v, fOut), FrontAt(0f, v, fIn), w), 14, 1, (v, w) => SphereDir(FAz(0f, v) - 0.01f, FPh(0f, v)) - SphereDir(FAz(0f, v), FPh(0f, v)));
            var frr = ParamOut((v, w) => Vector3.Lerp(FrontAt(1f, v, fOut), FrontAt(1f, v, fIn), w), 14, 1, (v, w) => SphereDir(FAz(1f, v) + 0.01f, FPh(1f, v)) - SphereDir(FAz(1f, v), FPh(1f, v)));
            Piece(head, MeshGen.Combine(fo, fi, fr, fl, frr), cloth);
            Piece(head, ParamOut((u, w) => SphereDir(FAz(u, 1f), FPh(u, 1f) + (w - 0.5f) * hemDeg) * (R + 0.0085f * g), 24, 1, (u, w) => SphereDir(FAz(u, 1f), FPh(u, 1f))), brass);

            // Studs: three rows round the cap, three down the frontlet; the medallion on the poll.
            var metal = new List<Mesh>();
            float[] rows = { 0.55f, 0.72f, 0.88f };
            for (int r = 0; r < rows.Length; r++)
                for (int k = 0; k < 12; k++)
                {
                    float az = (k + 0.5f * r) / 12f;
                    Vector3 d = SphereDir(az, Edge(az) * rows[r]);
                    var dome = DomeMesh(0.007f * g, 12);
                    MeshGen.Transform(dome, d * (R + 0.007f * g), Quaternion.FromToRotation(Vector3.up, d));
                    metal.Add(dome);
                }
            foreach (float v in new[] { 0.28f, 0.50f, 0.72f })
            {
                Vector3 d = SphereDir(0f, FPh(0.5f, v));
                var dome = DomeMesh(0.007f * g, 12);
                MeshGen.Transform(dome, d * (R + 0.005f * g), Quaternion.FromToRotation(Vector3.up, d));
                metal.Add(dome);
            }
            var ring = MeshGen.Torus(0.045f * g, 0.006f * g, 28, 10);
            MeshGen.Transform(ring, new Vector3(0f, R + 0.009f * g, 0f));
            metal.Add(ring);
            metal.Add(MeshGen.Disc(new Vector3(0f, R + 0.012f * g, 0f), Vector3.up, 0.02f * g, 20));
            Piece(head, MeshGen.Combine(metal.ToArray()), brass);
        }

        /// <summary>A buckled band round a leg capsule (axis local Y): a filleted lathe shell just
        /// proud of the leg, a bevelled buckle plate on the lateral face with a dark tongue, and four
        /// dome studs between.</summary>
        static void BuildAnkleBand(Transform bone, int side, float legR, float y0, float g, float h, Material strap, Material brass, Material dark)
        {
            float s = side < 0 ? -1f : 1f;
            float rIn = legR + 0.002f * g, rOut = legR + 0.014f * g, hh = 0.035f * h, f = 0.005f * g;
            var prof = new[]
            {
                new Vector2(rIn, -hh), new Vector2(rOut - f, -hh), new Vector2(rOut, -hh + f),
                new Vector2(rOut, hh - f), new Vector2(rOut - f, hh), new Vector2(rIn, hh),
            };
            var band = MeshGen.Lathe(prof, 36, smooth: false);
            MeshGen.Transform(band, new Vector3(0f, y0, 0f));
            Piece(bone, band, strap);

            var plate = MeshGen.Extrude(MeshGen.Superellipse(0.0225f * g, 0.0175f * g, 4f, 20), 0.010f * g, 0.002f * g);
            MeshGen.Transform(plate, new Vector3(s * (rOut + 0.002f * g), y0, 0f), Quaternion.Euler(0f, s * 90f, 0f));
            Piece(bone, plate, brass);
            var tongue = MeshGen.Extrude(MeshGen.Superellipse(0.012f * g, 0.008f * g, 4f, 16), 0.004f * g, 0.001f * g);
            MeshGen.Transform(tongue, new Vector3(s * (rOut + 0.008f * g), y0, 0f), Quaternion.Euler(0f, s * 90f, 0f));
            Piece(bone, tongue, dark);

            var studs = new List<Mesh>();
            for (int k = 0; k < 4; k++)
            {
                float ang = k * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                Vector3 nrm = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                var dome = DomeMesh(0.008f * g, 12);
                MeshGen.Transform(dome, nrm * (rOut - 0.002f * g) + new Vector3(0f, y0, 0f), Quaternion.FromToRotation(Vector3.up, nrm));
                studs.Add(dome);
            }
            Piece(bone, MeshGen.Combine(studs.ToArray()), brass);
        }

        /// <summary>
        /// Ceremonial blanket: a U-shaped cloth lofted over the barrel (torso-local Y = length, +Z =
        /// down, flanks at +/-X) with a fold over each top edge, a scalloped flared hem below the
        /// belly line, a girth strap over it, brass hem trim and a medallion per flank, and a row of
        /// tassels. Spans the barrel between the legs so the hem never touches a leg.
        /// </summary>
        static void BuildBlanket(Transform torso, ActiveRagdoll rag, Material damask, Material cloth, Material brass, Material strap)
        {
            float g = rag.GirthScale, h = rag.HeightScale;
            Vector3 bd = new Vector3(0.46f * g, 0.88f * h, 0.52f * g);
            float hx = bd.x * 0.5f, hz = bd.z * 0.5f, len = 0.34f * h, y0 = 0f;
            float drop = bd.z + 0.04f * g, fold = 0.03f * g;
            const float ff = 0.34f;   // fraction of the U spent on each flank
            float Scallop(float v) => 0.014f * g * (0.5f - 0.5f * Mathf.Cos(v * 2f * Mathf.PI * 7f));
            Vector3 Prof(float sU, float v, float t)
            {
                if (sU < ff)
                {
                    float k = sU / ff;
                    float z = Mathf.Lerp(-hz + drop - Scallop(v), -hz + fold, k);
                    float flare = 0.02f * g * Smooth(0.30f, 0f, k);
                    return new Vector3(-hx - t - flare, 0f, z);
                }
                if (sU > 1f - ff)
                {
                    float k = (sU - (1f - ff)) / ff;
                    float z = Mathf.Lerp(-hz + fold, -hz + drop - Scallop(v), k);
                    float flare = 0.02f * g * Smooth(0.70f, 1f, k);
                    return new Vector3(hx + t + flare, 0f, z);
                }
                {
                    float k = (sU - ff) / (1f - 2f * ff);
                    return new Vector3(Mathf.Lerp(-hx, hx, k), 0f, -hz - t);
                }
            }
            Vector3 Sheet(float u, float v, float t)
            {
                Vector3 p = Prof(u, v, t);
                // Round the four corners of the cloth where the hem meets the front and back edges.
                float edge = Mathf.Min(u, 1f - u), endF = Mathf.Min(v, 1f - v);
                float corner = Mathf.Clamp01(1f - Mathf.Max(0f, 0.06f - edge) / 0.06f * Mathf.Max(0f, 0.06f - endF) / 0.06f);
                p.z = Mathf.Lerp(-hz + fold, p.z, corner);
                return p + new Vector3(0f, y0 + Mathf.Lerp(-len * 0.5f, len * 0.5f, v), 0f);
            }
            float tO = 0.010f * g, tI = 0.002f * g;
            Vector3 OutwardAt(float u, float v) { var a = Sheet(u, v, tO); var b = Sheet(u, v, tI); return a - b; }
            var outer = ParamOut((u, v) => Sheet(u, v, tO), 56, 18, OutwardAt, uvU: 4f, uvV: 2f);
            var inner = ParamOut((u, v) => Sheet(u, v, tI), 56, 18, (u, v) => -OutwardAt(u, v), uvU: 4f, uvV: 2f);
            var hemL = ParamOut((v, w) => Vector3.Lerp(Sheet(0f, v, tO), Sheet(0f, v, tI), w), 36, 1, (v, w) => Vector3.forward);
            var hemR = ParamOut((v, w) => Vector3.Lerp(Sheet(1f, v, tO), Sheet(1f, v, tI), w), 36, 1, (v, w) => Vector3.forward);
            var endA = ParamOut((u, w) => Vector3.Lerp(Sheet(u, 0f, tO), Sheet(u, 0f, tI), w), 56, 1, (u, w) => Vector3.down);
            var endB = ParamOut((u, w) => Vector3.Lerp(Sheet(u, 1f, tO), Sheet(u, 1f, tI), w), 56, 1, (u, w) => Vector3.up);
            Piece(torso, MeshGen.Combine(outer, inner, hemL, hemR, endA, endB), damask);

            // Brass trim along both hems, a medallion on each flank.
            float tT = 0.013f * g;
            foreach (float uh in new[] { 0f, 1f })
            {
                float dir = uh < 0.5f ? 1f : -1f;
                Piece(torso, ParamOut((v, w) => Sheet(uh + dir * w * 0.05f, v, tT), 36, 2, (v, w) => new Vector3(-dir, 0f, 0f)), brass);
                var medal = new List<Mesh>();
                float mx = dir < 0f ? hx + 0.015f * g : -hx - 0.015f * g;
                float mz = -hz + drop * 0.42f;
                var ring = MeshGen.Torus(0.06f * g, 0.006f * g, 28, 10);
                MeshGen.Transform(ring, new Vector3(mx, y0, mz), Quaternion.FromToRotation(Vector3.up, new Vector3(-dir, 0f, 0f)));
                medal.Add(ring);
                medal.Add(MeshGen.Disc(new Vector3(mx, y0, mz), new Vector3(-dir, 0f, 0f), 0.03f * g, 20));
                Piece(torso, MeshGen.Combine(medal.ToArray()), brass);
            }
            // Girth strap over the blanket, all round the barrel.
            BoxRing(torso, bd, y0, 0.035f, 0.014f, 0.03f, strap, 8f);
            // Tassels, ten per flank, hanging from the hem.
            var tassels = new List<Mesh>();
            for (int k = 0; k < 10; k++)
            {
                float v = (k + 0.5f) / 10f;
                foreach (float uh in new[] { 0f, 1f })
                {
                    Vector3 p = Sheet(uh, v, tO * 0.5f);
                    var cyl = MeshGen.Cylinder(0.008f * g, 0.003f * g, 0.05f * g, 10, true, false);
                    MeshGen.Transform(cyl, p + new Vector3(0f, 0f, 0.003f * g), Quaternion.FromToRotation(Vector3.up, Vector3.forward));
                    tassels.Add(cyl);
                }
            }
            Piece(torso, MeshGen.Combine(tassels.ToArray()), cloth);
        }

        // ---- the elephant's cosmetic pass --------------------------------------------------------
        static void AttachElephantDecor(ActiveRagdoll rag, PlayerAppearance a)
        {
            var head = rag.Phys(Bone.Head);
            if (head == null) return;
            float g = rag.GirthScale, h = rag.HeightScale;

            // EARS. The hide colour, not the slot tint: an ear that is not the colour of the head it
            // grows from reads as a prop. Whichever box pair the table built for this style is dressed.
            var earMat = Own(Make.MatTexTint(ElephantTex.Ear, a.Skin, 0.12f));
            foreach (var (name, side) in new[] { ("D_EarL", -1), ("D_EarR", 1), ("D_EarWideL", -1), ("D_EarWideR", 1) })
                if (rag.TryGetDecor(name, out var box, out var dims)) BuildEar(head, rag, side, box, dims, a.HairStyle, earMat);

            // TUSKS (StyleB tint = ivory by default).
            if (a.FacialStyle >= 1 && a.FacialStyle <= 4)
            {
                var ivory = Own(Make.MatTexTint(ElephantTex.Tusk, a.FacialColor, 0.45f));
                var skin = Own(Make.Mat(a.Skin, 0.15f));
                var brass = a.FacialStyle == 4 ? Own(Make.Mat(new Color(0.72f, 0.55f, 0.25f), 0.6f, 0.7f)) : null;
                BuildTusk(head, rag, -1, a.FacialStyle, ivory, skin, brass);
                BuildTusk(head, rag, 1, a.FacialStyle, ivory, skin, brass);
            }

            // TACK (StyleC tint).
            if (a.Accessory <= 0) return;
            var gold = Own(Make.Mat(new Color(0.72f, 0.55f, 0.25f), 0.6f, 0.7f));
            var dark = Own(Make.Mat(new Color(0.12f, 0.10f, 0.09f), 0.3f));
            switch (a.Accessory)
            {
                case 1:
                    BuildHeadCloth(head, rag, Own(Make.MatTexTint(ElephantTex.Weave, a.AccessoryColor, 0.12f)), gold);
                    break;
                case 2:
                {
                    var strap = Own(Make.MatTexTint(HorseDecals.Leather, a.AccessoryColor, 0.3f));
                    foreach (var (bone, side, r, y) in new[] { (Bone.ForearmL, -1, 0.135f, -0.045f), (Bone.ForearmR, 1, 0.135f, -0.045f), (Bone.CalfL, -1, 0.125f, -0.095f), (Bone.CalfR, 1, 0.125f, -0.095f) })
                    {
                        var t = rag.Phys(bone);
                        if (t != null) BuildAnkleBand(t, side, r * g, y * h, g, h, strap, gold, dark);
                    }
                    break;
                }
                case 3:
                {
                    var torso = rag.Phys(Bone.Torso);
                    if (torso == null) break;
                    var damask = Own(Make.MatTexTint(ElephantTex.Damask, a.AccessoryColor, 0.10f));
                    var cloth = Own(Make.Mat(a.AccessoryColor, 0.12f));
                    var strap = Own(Make.MatTexTint(HorseDecals.Leather, new Color(0.30f, 0.20f, 0.12f), 0.3f));
                    BuildBlanket(torso, rag, damask, cloth, gold, strap);
                    break;
                }
            }
        }
    }
}
