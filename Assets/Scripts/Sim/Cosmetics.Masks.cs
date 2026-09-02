using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // MASKS. Shells that conform to the head - a polar FacePlate or a lat/long HeadCap - with
    // eye holes and detail painted into a cutout texture, a rim tube along the real mesh edge,
    // and straps drawn on the head. The gas mask mounts a downloaded model with a generated
    // fallback; the welding hood is generated.
    public static partial class Cosmetics
    {
        // ---- painted mask textures (cached for the process lifetime) --------------------------
        static class MaskArt
        {
            static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
            /// <summary>Paint a texture from a per-texel function of (u, v) in 0..1.</summary>
            public static Texture2D Get(string key, int w, int h, Func<float, float, Color32> f)
            {
                if (_cache.TryGetValue(key, out var t) && t != null) return t;
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
                t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "MaskArt_" + key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                t.SetPixels32(px); t.Apply(true, true);
                _cache[key] = t;
                return t;
            }
            public static Color32 Lum(float l, byte a = 255) { byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(l) * 255f); return new Color32(b, b, b, a); }
        }

        /// <summary>
        /// A polar shell on the sphere about centreDir: spoke angle a (0 = up, CCW seen from outside),
        /// ring t in 0..1 reaching outlineRad(a) radians from the centre; proud + bulge(a, t) in
        /// metres along the normal. UV = (a/2pi, t). Returns the outer ring as directions.
        /// </summary>
        static Mesh FacePlateMesh(Vector3 centre, Func<float, float> outlineRad, float proud, Func<float, float, float> bulge, int spokes, int rings, out Vector3[] outer)
        {
            centre.Normalize();
            Vector3 side = Vector3.Cross(centre, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(centre, Vector3.forward);
            side.Normalize();
            Vector3 up2 = Vector3.Cross(side, centre).normalized;
            Vector3 DirAt(float a, float t)
            {
                float rad = outlineRad(a) * t;
                Vector3 tangent = up2 * Mathf.Cos(a) - side * Mathf.Sin(a);       // a = 0 up, increasing toward -side (CCW from outside)
                return (centre * Mathf.Cos(rad) + tangent * Mathf.Sin(rad)).normalized;
            }
            var m = OutwardParam((u, v) =>
            {
                float a = u * Mathf.PI * 2f;
                Vector3 d = DirAt(a, v);
                float hh = proud + (bulge != null ? bulge(a, v) : 0f);
                return d * (HeadR + hh);
            }, spokes, rings, wrapU: true);
            // A flat plate (no bulge) is a sphere patch: radial normals, which also cures the pole.
            if (bulge == null) { var vv = m.vertices; var nn = new Vector3[vv.Length]; for (int i = 0; i < vv.Length; i++) nn[i] = vv[i].normalized; m.normals = nn; }
            Scale(m, _cosScale);
            outer = new Vector3[spokes];
            for (int i = 0; i < spokes; i++) outer[i] = DirAt(i / (float)spokes * Mathf.PI * 2f, 1f);
            return m;
        }

        /// <summary>A lat/long cap from the crown down to phiBot(theta), UV = (theta/2pi, phi/pi).</summary>
        static Mesh HeadCapMesh(Func<float, float> phiBotDeg, float proud, Func<float, float, float> bulge, int cols, int rows, out Vector3[] lip)
        {
            var m = OutwardParam((u, v) =>
            {
                float th = -Mathf.PI + u * Mathf.PI * 2f;
                float phi = v * phiBotDeg(th * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                Vector3 d = HairShape.Dir(phi, th);
                float hh = proud + (bulge != null ? bulge(th * Mathf.Rad2Deg, phi * Mathf.Rad2Deg) : 0f);
                return d * (HeadR + hh);
            }, cols, rows, wrapU: true);
            // UV in (theta, phi) so holes can be painted in lat/long.
            var v = m.vertices; var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                HairShape.Polar(v[i].normalized, out float phi, out float th);
                uv[i] = new Vector2((th + Mathf.PI) / (Mathf.PI * 2f), phi / Mathf.PI);
            }
            m.uv = uv;
            Scale(m, _cosScale);
            lip = new Vector3[cols];
            for (int i = 0; i < cols; i++) { float th = -Mathf.PI + i / (float)cols * Mathf.PI * 2f; lip[i] = HairShape.Dir(phiBotDeg(th * Mathf.Rad2Deg) * Mathf.Deg2Rad, th); }
            return m;
        }

        static float Gauss(float x, float sigma) => Mathf.Exp(-0.5f * x * x / (sigma * sigma));
        // Eye centres shared by every mask: theta +/-22 deg, phi 84 deg (the eyepatch's eye).
        const float EyeTheta = 22f, EyePhi = 84f;

        static void BuildCowl(Transform h, Material m)
        {
            float PhiBot(float thDeg) => Mathf.Lerp(100f, 115f, Smooth(20f, 60f, Mathf.Abs(thDeg)));
            float Bulge(float thDeg, float phiDeg)
            {
                float brow = 0.004f * Gauss(phiDeg - 76f, 4f) * Mathf.Clamp01(1f - Mathf.Abs(thDeg) / 40f);
                float nose = 0.012f * Gauss(thDeg, 6f) * Gauss(phiDeg - 96f, 6f);
                return brow + nose;
            }
            var tex = MaskArt.Get("cowl", 512, 256, (u, v) =>
            {
                float th = (u - 0.5f) * 360f, phi = v * 180f;
                float d = Mathf.Min(EyeDist(th, phi, EyeTheta), EyeDist(th, phi, -EyeTheta));
                if (d < 1f) return MaskArt.Lum(0f, 0);                 // open hole
                if (d < 1.25f) return MaskArt.Lum(0.55f);              // cut edge
                return MaskArt.Lum(1f);
            });
            var capMat = Own(Make.MatCutout(tex, m.color, 0.35f, 0f));
            var cap = HeadCapMesh(PhiBot, 0.008f, Bulge, 72, 36, out var lip);
            Piece(h, cap, capMat);
            SweptTube(h, Own(Make.Mat(m.color, 0.35f)), lip, 0.008f, 0.005f, 8, closed: true);
            // Short rounded ears.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 baseDir = HairShape.Dir(22f * Mathf.Deg2Rad, side * 28f * Mathf.Deg2Rad);
                var fin = MeshGen.Lathe(new[] { new Vector2(0.022f, 0f), new Vector2(0.020f, 0.03f), new Vector2(0.013f, 0.06f), new Vector2(0.005f, 0.078f), new Vector2(0f, 0.08f) }, 12);
                var axis = Tilt(baseDir, new Vector3(side, 0f, 0f), 8f);
                MeshGen.Transform(fin, baseDir * (HeadR - 0.002f) * _cosScale, Quaternion.FromToRotation(Vector3.up, axis), new Vector3(0.7f, 1f, 1f) * _cosScale);
                Piece(h, fin, Own(Make.Mat(m.color, 0.35f)));
            }
        }
        // Normalised angular distance from an almond eye hole (21 x 10.5 deg) at (thetaC, EyePhi).
        static float EyeDist(float th, float phi, float thetaC)
        {
            float dx = (th - thetaC) / 10.5f, dy = (phi - EyePhi) / 5.25f;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static void BuildHockeyMask(Transform h, Material m)
        {
            Vector3 centre = HairShape.Dir(88f * Mathf.Deg2Rad, 0f);
            float Outline(float a)
            {
                // Superellipse in angular space: 66 deg sideways, 56 deg vertical, narrower top.
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float rx = 66f, ry = ca > 0f ? 50f : 56f;
                float p = 2.6f;
                float r = 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(ca) / ry, p) + Mathf.Pow(Mathf.Abs(sa) / rx, p), 1f / p);
                return r * Mathf.Deg2Rad;
            }
            float Bulge(float a, float t)
            {
                // Muzzle dome low in the plate, faded to the edge.
                float y = -Mathf.Cos(a) * t, x = Mathf.Sin(a) * t;   // rough plate-space coordinates
                return 0.02f * Gauss(x, 0.35f) * Gauss(y - 0.35f, 0.3f) * (1f - Smooth(0.8f, 1f, t));
            }
            var tex = MaskArt.Get("hockey", 512, 512, (u, v) =>
            {
                // Plate UV: u = spoke angle (0 up), v = ring. Paint holes by plate-space position.
                float a = u * Mathf.PI * 2f;
                float x = Mathf.Sin(a) * v * 66f, y = -Mathf.Cos(a) * v * 56f;   // approx degrees from the centre
                float eye = Mathf.Min(Mathf.Sqrt(Mathf.Pow((x - 20f) / 8f, 2f) + Mathf.Pow((y + 6f) / 5.5f, 2f)), Mathf.Sqrt(Mathf.Pow((x + 20f) / 8f, 2f) + Mathf.Pow((y + 6f) / 5.5f, 2f)));
                if (eye < 1f) return MaskArt.Lum(0f, 0);
                if (eye < 1.2f) return MaskArt.Lum(0.6f);
                // Vent grid over the muzzle.
                if (y > 6f && y < 40f && Mathf.Abs(x) > 6f && Mathf.Abs(x) < 34f)
                {
                    float gx = Mathf.Repeat(Mathf.Abs(x) - 6f, 7f) - 3.5f, gy = Mathf.Repeat(y - 6f, 8.5f) - 4.25f;
                    float dv = Mathf.Sqrt(gx * gx + gy * gy);
                    if (dv < 2.2f) return MaskArt.Lum(0f, 0);
                    if (dv < 2.8f) return MaskArt.Lum(0.6f);
                }
                // Three forehead vents.
                for (int k = -1; k <= 1; k++)
                {
                    float dv = Mathf.Sqrt(Mathf.Pow(x - k * 12f, 2f) + Mathf.Pow(y + 36f, 2f));
                    if (dv < 2f) return MaskArt.Lum(0f, 0);
                    if (dv < 2.6f) return MaskArt.Lum(0.6f);
                }
                return MaskArt.Lum(1f);
            });
            var plateMat = Own(Make.MatCutout(tex, m.color, 0.55f, 0f));
            var plate = FacePlateMesh(centre, Outline, 0.008f, Bulge, 48, 16, out var outer);
            Piece(h, plate, plateMat);
            SweptTube(h, Own(Make.Mat(m.color, 0.55f)), outer, 0.008f, 0.005f, 8, closed: true);
            // Straps round the back to a buckle.
            Vector3 nape = HairShape.Dir(95f * Mathf.Deg2Rad, Mathf.PI);
            for (int side = -1; side <= 1; side += 2)
            {
                SweptTube(h, Dark(), PathDirs(HairShape.Dir(75f * Mathf.Deg2Rad, side * 68f * Mathf.Deg2Rad), nape, 0.04f, 16), 0.004f, 0.006f, 6);
                SweptTube(h, Dark(), PathDirs(HairShape.Dir(110f * Mathf.Deg2Rad, side * 68f * Mathf.Deg2Rad), nape, 0.04f, 16), 0.004f, 0.006f, 6);
            }
            SurfacePatch(h, Dark(), nape, Circle(0.02f, 16), 0.005f, 2);
        }

        static void BuildVenetianMask(Transform h, Material m)
        {
            Vector3 centre = HairShape.Dir(78f * Mathf.Deg2Rad, 0f);
            float Outline(float a)
            {
                // a = 0 up. Base 30 deg, wider than tall, a central upward peak and two temple sweeps.
                float deg = 30f + 6f * Mathf.Cos(2f * a);
                deg += 10f * Gauss(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 0f), 12f);                          // peak (up)
                deg += 9f * Gauss(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 65f), 12f) + 9f * Gauss(Mathf.DeltaAngle(a * Mathf.Rad2Deg, -65f), 12f);
                deg -= 6f * Gauss(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 150f), 14f) + 6f * Gauss(Mathf.DeltaAngle(a * Mathf.Rad2Deg, -150f), 14f);
                return deg * Mathf.Deg2Rad;
            }
            float Bulge(float a, float t)
            {
                float x = Mathf.Sin(a) * t * 36f, y = -Mathf.Cos(a) * t * 30f;
                return 0.012f * Gauss(x, 5f) * Gauss(y - 12f, 6f);
            }
            var tex = MaskArt.Get("venetian", 1024, 512, (u, v) =>
            {
                float a = u * Mathf.PI * 2f;
                float x = Mathf.Sin(a) * v * 36f, y = -Mathf.Cos(a) * v * 30f;
                float eye = Mathf.Min(Mathf.Sqrt(Mathf.Pow((x - 22f) / 9f, 2f) + Mathf.Pow((y - 4f) / 5.5f, 2f)), Mathf.Sqrt(Mathf.Pow((x + 22f) / 9f, 2f) + Mathf.Pow((y - 4f) / 5.5f, 2f)));
                if (eye < 1f) return MaskArt.Lum(0f, 0);
                if (eye < 1.15f) return MaskArt.Lum(0.5f);
                if (v > 0.93f) return MaskArt.Lum(0.55f);                                   // border band
                // Filigree: mirrored curls as luminance, from a few sine ridges.
                float curl = Mathf.Sin(Mathf.Abs(x) * 0.7f + Mathf.Sin(y * 0.5f) * 2f) * Mathf.Sin(y * 0.6f + Mathf.Abs(x) * 0.2f);
                float l = 1f - 0.45f * Mathf.Clamp01(Mathf.Abs(curl) > 0.85f ? 1f : 0f);
                if (Mathf.Abs(curl) > 0.97f) l = 1.0f;
                return MaskArt.Lum(l);
            });
            var goldMat = Own(Make.MatCutout(tex, m.color, 0.75f, 0.6f));
            var plate = FacePlateMesh(centre, Outline, 0.006f, Bulge, 64, 16, out var outer);
            Piece(h, plate, goldMat);
            SweptTube(h, Own(Make.Mat(m.color, 0.75f, 0.6f)), outer, 0.006f, 0.004f, 8, closed: true);
            // Ribbons to a bow at the back.
            Vector3 knot = HairShape.Dir(92f * Mathf.Deg2Rad, Mathf.PI);
            for (int side = -1; side <= 1; side += 2)
                SweptTube(h, Dark(), PathDirs(HairShape.Dir(80f * Mathf.Deg2Rad, side * 66f * Mathf.Deg2Rad), knot, 0.05f, 16), 0.004f, 0.005f, 6);
            var bowMat = Own(Make.Mat(m.color, 0.5f));
            var kn = MeshGen.Lathe(new[] { new Vector2(0f, -0.012f), new Vector2(0.012f, 0f), new Vector2(0f, 0.012f) }, 12);
            PieceAt(h, kn, bowMat, knot * (HeadR + 0.006f), Quaternion.identity);
            for (int side = -1; side <= 1; side += 2)
            {
                var loop = MeshGen.Torus(0.016f, 0.005f, 16, 8);
                PieceAt(h, loop, bowMat, knot * (HeadR + 0.006f) + new Vector3(side * 0.02f, 0f, 0f), Quaternion.Euler(0f, 0f, side * 35f) * Quaternion.Euler(90f, 0f, 0f));
            }
            // Feather: a tapered card with a quill, at the right temple.
            Vector3 root = HairShape.Dir(50f * Mathf.Deg2Rad, 55f * Mathf.Deg2Rad);
            Vector3 fdir = Tilt(Tilt(root, Vector3.back, 35f), Vector3.right, 20f);
            var path = new Vector3[7];
            for (int i = 0; i < 7; i++) { float t = i / 6f; path[i] = root * (HeadR + 0.004f) + fdir * (0.16f * t) + Vector3.up * (0.02f * Mathf.Sin(t * Mathf.PI)); }
            var plumeTex = MaskArt.Get("plume", 64, 256, (u, v) =>
            {
                float edge = Mathf.Abs(u - 0.5f) * 2f;
                float barb = Mathf.Abs(Mathf.Sin(v * 60f + u * 3f));
                bool solid = edge < 0.06f || (barb > 0.35f && edge < 1f - 0.35f * Mathf.Pow(v, 0.5f));
                return solid ? MaskArt.Lum(edge < 0.06f ? 0.35f : 1f) : MaskArt.Lum(0f, 0);
            });
            var plumeMat = Own(Make.MatCutout(plumeTex, new Color(0.45f, 0.08f, 0.15f), 0.3f, 0f));
            var card = MeshGen.Ribbon(path, 0.045f, Vector3.zero, new Vector2(0f, 1f), 0f, 1f, -0.7f);
            MeshGen.Transform(card, Vector3.zero, Quaternion.identity, Vector3.one * _cosScale);
            { var cv = card.vertices; var cn = new Vector3[cv.Length]; for (int i = 0; i < cv.Length; i++) cn[i] = Vector3.Cross(fdir, Vector3.Cross(cv[i].normalized, fdir)).normalized; card.normals = cn; }
            Piece(h, card, plumeMat, castShadows: false);
            var quill = MeshGen.Tube(path, new[] { 0.003f, 0.0028f, 0.0025f, 0.002f, 0.0015f, 0.001f, 0.0005f }, 6, false, true);
            PieceAt(h, quill, Ivory(), Vector3.zero, Quaternion.identity);
        }

        static void BuildGasMask(Transform h, Material m)
        {
            // Generated: facepiece with a muzzle dome, eyepieces, a canister and straps (the
            // downloaded candidate was a lump with no separate lens slot).
            Vector3 centre = HairShape.Dir(92f * Mathf.Deg2Rad, 0f);
            float Outline(float a)
            {
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float rx = 70f, ry = ca > 0f ? 37f : 48f; float p = 2.2f;
                return 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(ca) / ry, p) + Mathf.Pow(Mathf.Abs(sa) / rx, p), 1f / p) * Mathf.Deg2Rad;
            }
            float Bulge(float a, float t)
            {
                float x = Mathf.Sin(a) * t * 70f, y = -Mathf.Cos(a) * t * 45f;
                return 0.03f * Gauss(x / 14f, 1f) * Gauss((y - 13f) / 12f, 1f) * (1f - Smooth(0.8f, 1f, t));
            }
            var rubber = Own(Make.Mat(m.color, 0.25f));
            var plate = FacePlateMesh(centre, Outline, 0.010f, Bulge, 64, 20, out var outer);
            Piece(h, plate, rubber);
            SweptTube(h, rubber, outer, 0.010f, 0.005f, 8, closed: true);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 eye = HairShape.Dir(80f * Mathf.Deg2Rad, side * 23f * Mathf.Deg2Rad);
                var rim = MeshGen.Torus(0.042f, 0.007f, 24, 8);
                PieceAt(h, rim, Gunmetal(), eye * (HeadR + 0.014f), Quaternion.FromToRotation(Vector3.up, eye));
                var lens = MeshGen.Disc(Vector3.zero, Vector3.up, 0.036f, 24);
                PieceAt(h, lens, LensTint(), eye * (HeadR + 0.011f), Quaternion.FromToRotation(Vector3.up, eye));
            }
            Vector3 can = HairShape.Dir(108f * Mathf.Deg2Rad, 12f * Mathf.Deg2Rad);
            var canister = MeshGen.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.040f, 0f), new Vector2(0.048f, 0.008f), new Vector2(0.048f, 0.060f), new Vector2(0.052f, 0.064f), new Vector2(0.048f, 0.068f), new Vector2(0.030f, 0.075f), new Vector2(0f, 0.075f) }, 24);
            PieceAt(h, canister, Own(Make.Mat(new Color(0.35f, 0.36f, 0.25f), 0.3f, 0.2f)), can * (HeadR + 0.034f), Quaternion.FromToRotation(Vector3.up, Tilt(can, Vector3.down, 20f)));
            Vector3 nape = HairShape.Dir(100f * Mathf.Deg2Rad, Mathf.PI);
            for (int side = -1; side <= 1; side += 2)
            {
                SweptTube(h, Dark(), PathDirs(HairShape.Dir(80f * Mathf.Deg2Rad, side * 70f * Mathf.Deg2Rad), nape, 0.03f, 16), 0.004f, 0.006f, 6);
                SweptTube(h, Dark(), PathDirs(HairShape.Dir(125f * Mathf.Deg2Rad, side * 55f * Mathf.Deg2Rad), nape, 0.03f, 16), 0.004f, 0.006f, 6);
            }
            SurfacePatch(h, Dark(), nape, Circle(0.022f, 16), 0.005f, 2);
        }

        static void BuildWeldingMask(Transform h, Material m)
        {
            // A rigid hood standing off the head: front panel, cheek panels, crown plate, lens window, headband, pivots.
            var shell = Own(Make.Mat(m.color, 0.3f));
            Vector3 centre = HairShape.Dir(88f * Mathf.Deg2Rad, 0f);
            float Outline(float a)
            {
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float rx = 48f, ry = ca > 0f ? 52f : 50f; float p = 5f;
                return 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(ca) / ry, p) + Mathf.Pow(Mathf.Abs(sa) / rx, p), 1f / p) * Mathf.Deg2Rad;
            }
            var plate = FacePlateMesh(centre, Outline, 0.035f, (a, t) => 0f, 48, 10, out var outer);
            Piece(h, plate, shell);
            SweptTube(h, shell, outer, 0.035f, 0.006f, 8, closed: true);
            // Cheek panels back to the temples, a crown plate over the top.
            for (int side = -1; side <= 1; side += 2)
            {
                int sd = side;
                var cheek = MeshGen.Param((u, v) =>
                {
                    float th = (sd * 48f + sd * u * 45f) * Mathf.Deg2Rad;
                    float phi = Mathf.Lerp(45f, 135f, v) * Mathf.Deg2Rad;
                    float proud = Mathf.Lerp(0.035f, 0.015f, u);
                    return HairShape.Dir(phi, th) * (HeadR + proud);
                }, 8, 8);
                Scale(cheek, _cosScale);
                Piece(h, cheek, shell);
            }
            var crown = MeshGen.Param((u, v) =>
            {
                float th = Mathf.Lerp(-95f, 95f, u) * Mathf.Deg2Rad;
                float phi = Mathf.Lerp(12f, 45f, v) * Mathf.Deg2Rad;
                return HairShape.Dir(phi, th) * (HeadR + Mathf.Lerp(0.022f, 0.035f, v));
            }, 24, 5);
            Scale(crown, _cosScale);
            Piece(h, crown, shell);
            // Lens window with a raised frame.
            Vector3 win = HairShape.Dir(84f * Mathf.Deg2Rad, 0f);
            SurfacePatch(h, Own(Make.Mat(new Color(0.05f, 0.20f, 0.10f), 0.85f)), win, RoundedPoly(new[] { new Vector2(-0.055f, -0.018f), new Vector2(0.055f, -0.018f), new Vector2(0.055f, 0.018f), new Vector2(-0.055f, 0.018f) }, new[] { 0.006f }, 4), 0.036f, 2);
            var frameO = RoundedPoly(new[] { new Vector2(-0.061f, -0.024f), new Vector2(0.061f, -0.024f), new Vector2(0.061f, 0.024f), new Vector2(-0.061f, 0.024f) }, new[] { 0.008f }, 4);
            var fd = new Vector3[frameO.Length]; for (int i = 0; i < fd.Length; i++) fd[i] = SurfDir(win, frameO[i]);
            SweptTube(h, Dark(), fd, 0.038f, 0.004f, 6, closed: true);
            HeadRing(h, Dark(), 0.85f, 0.03f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 piv = HairShape.Dir(80f * Mathf.Deg2Rad, side * 100f * Mathf.Deg2Rad);
                var knob = MeshGen.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.02f, 0f), new Vector2(0.02f, 0.012f), new Vector2(0.012f, 0.018f), new Vector2(0f, 0.02f) }, 16);
                PieceAt(h, knob, Dark(), piv * (HeadR + 0.02f), Quaternion.FromToRotation(Vector3.up, piv));
            }
        }
    }
}
