using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // ACCESSORIES: the shared surface toolkit (patches, tubes and bands that CONFORM to the head
    // sphere), the eyewear builders, the small props and jewellery, and the catalog. Hats live in
    // Cosmetics.Hats.cs and masks in Cosmetics.Masks.cs. Every literal is head-local metres at
    // girth 1, scaled by _cosScale where it becomes geometry; nothing here ever adds a collider.
    public static partial class Cosmetics
    {
        // ---- material ownership -----------------------------------------------------------
        // The ragdoll being dressed, so any material a builder creates can be registered for
        // teardown (the customize preview rebuilds a body on every drag frame).
        static ActiveRagdoll _rag;
        static Material Own(Material m) { _rag?.RegisterCosmeticMaterial(m); return m; }

        // Fixed materials, cached for the process lifetime (never registered, never destroyed).
        static Material _glass, _dark, _light, _ivory, _paper, _paleWood, _tobacco, _ash, _ember, _stemBlack, _gold, _gunmetal, _lensTint;
        static Material Glass()     => _glass     ??= Make.Mat(new Color(0.60f, 0.80f, 0.95f), 0.6f);
        static Material Dark()      => _dark      ??= Make.Mat(new Color(0.06f, 0.06f, 0.07f), 0.15f);
        static Material Light()     => _light     ??= Make.Mat(new Color(0.93f, 0.92f, 0.88f), 0.1f);
        static Material Ivory()     => _ivory     ??= Make.Mat(new Color(0.95f, 0.92f, 0.85f), 0.55f);
        static Material Paper()     => _paper     ??= Make.Mat(Color.white, 0.3f);
        static Material PaleWood()  => _paleWood  ??= Make.Mat(new Color(0.85f, 0.75f, 0.55f), 0.2f);
        static Material Tobacco()   => _tobacco   ??= Make.Mat(new Color(0.40f, 0.26f, 0.16f), 0.35f);
        static Material Ash()       => _ash       ??= Make.Mat(new Color(0.45f, 0.45f, 0.45f), 0.1f);
        static Material Ember()     => _ember     ??= Make.Glow(new Color(1f, 0.45f, 0.1f));
        static Material StemBlack() => _stemBlack ??= Make.Mat(new Color(0.05f, 0.05f, 0.05f), 0.6f);
        static Material Gold()      => _gold      ??= Make.Mat(new Color(0.85f, 0.70f, 0.30f), 0.85f, 0.75f);
        static Material Gunmetal()  => _gunmetal  ??= Make.Mat(new Color(0.30f, 0.30f, 0.32f), 0.5f, 0.6f);
        static Material LensTint()  => _lensTint  ??= Make.Mat(new Color(0.35f, 0.42f, 0.40f), 0.85f);
        /// <summary>A registered transparent lens material.</summary>
        static Material Lens(Color c, float alpha, float smooth, float metallic = 0f) => Own(Make.Transparent(c, alpha, smooth, metallic));

        // ---- surface mapping ----------------------------------------------------------------
        /// <summary>Direction on the sphere at tangent-plane offset (u right, v up) metres from centre.</summary>
        static Vector3 SurfDir(Vector3 centre, Vector2 t)
        {
            centre.Normalize();
            Vector3 side = Vector3.Cross(centre, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(centre, Vector3.forward);
            side.Normalize();
            Vector3 up2 = Vector3.Cross(side, centre).normalized;
            // HeadPatch's mapping: yaw about up2, then pitch about side. Note the yaw sign: +u is
            // toward `side`, which for the face (+Z) is +X, the character's left / viewer's right.
            Vector3 d = Quaternion.AngleAxis(-t.x / HeadR * Mathf.Rad2Deg, up2) * centre;
            Vector3 side2 = Quaternion.AngleAxis(-t.x / HeadR * Mathf.Rad2Deg, up2) * side;
            d = Quaternion.AngleAxis(-t.y / HeadR * Mathf.Rad2Deg, side2) * d;
            return d.normalized;
        }
        static Vector3 SurfPos(Vector3 dir, float standoff) => dir.normalized * (HeadR + standoff) * _cosScale;
        static Vector3 Dir(float x, float y, float z) => new Vector3(x, y, z).normalized;

        /// <summary>Directions along a great-circle path that sags toward the neck by `bow`.</summary>
        static Vector3[] PathDirs(Vector3 from, Vector3 to, float bow, int n)
        {
            from.Normalize(); to.Normalize();
            var d = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector3 p = Vector3.Slerp(from, to, t);
                d[i] = Vector3.Slerp(p, Vector3.down, bow * Mathf.Sin(t * Mathf.PI)).normalized;
            }
            return d;
        }

        // Parametric mesh with normals forced OUTWARD from the head centre (rebuilt flipped when
        // the parameterisation runs the other way), unscaled.
        static Mesh OutwardParam(Func<float, float, Vector3> P, int cols, int rows, bool wrapU, bool wrapV = false)
        {
            var m = MeshGen.Param(P, cols, rows, wrapU: wrapU, wrapV: wrapV, flip: false);
            var v = m.vertices; var n = m.normals;
            int inward = 0;
            for (int i = 0; i < v.Length; i++) if (Vector3.Dot(n[i], v[i]) < 0f) inward++;
            if (inward > v.Length / 2)
            {
                UnityEngine.Object.Destroy(m);
                m = MeshGen.Param(P, cols, rows, wrapU: wrapU, wrapV: wrapV, flip: true);
            }
            return m;
        }
        static void Scale(Mesh m, float k)
        {
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] *= k;
            m.vertices = v; m.RecalculateBounds();
        }

        static int Wrap(int i, int n) => ((i % n) + n) % n;

        // ---- outlines (tangent-plane metres, CCW, star-shaped about the origin) ---------------
        static Vector2[] Circle(float r, int n = 32)
        {
            var p = new Vector2[n];
            for (int i = 0; i < n; i++) { float a = i / (float)n * Mathf.PI * 2f; p[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
            return p;
        }
        static Vector2[] Ellipse(float rx, float ry, int n = 32)
        {
            var p = new Vector2[n];
            for (int i = 0; i < n; i++) { float a = i / (float)n * Mathf.PI * 2f; p[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry); }
            return p;
        }
        /// <summary>A rounded polygon: corners with per-corner radii, `perCorner` samples each.</summary>
        static Vector2[] RoundedPoly(Vector2[] corners, float[] radii, int perCorner = 5)
        {
            var pts = new List<Vector2>();
            int n = corners.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = corners[(i - 1 + n) % n], c = corners[i], q = corners[(i + 1) % n];
                Vector2 a = (p - c).normalized, b = (q - c).normalized;
                float r = radii[i % radii.Length];
                float ang = Mathf.Acos(Mathf.Clamp(Vector2.Dot(a, b), -1f, 1f));
                float tan = r / Mathf.Tan(ang * 0.5f);
                tan = Mathf.Min(tan, Mathf.Min((p - c).magnitude, (q - c).magnitude) * 0.45f);
                r = tan * Mathf.Tan(ang * 0.5f);
                Vector2 pa = c + a * tan, pb = c + b * tan;
                Vector2 bis = (a + b).normalized;
                float dist = r / Mathf.Sin(ang * 0.5f);
                Vector2 centre = c + bis * dist;
                float a0 = Mathf.Atan2(pa.y - centre.y, pa.x - centre.x), a1 = Mathf.Atan2(pb.y - centre.y, pb.x - centre.x);
                float da = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                for (int k = 0; k <= perCorner; k++)
                {
                    float t = k / (float)perCorner;
                    float aa = a0 + da * t;
                    pts.Add(centre + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * r);
                }
            }
            // Ensure CCW.
            float area = 0f;
            for (int i = 0; i < pts.Count; i++) { var s = pts[i]; var e = pts[(i + 1) % pts.Count]; area += s.x * e.y - s.y * e.x; }
            if (area < 0f) pts.Reverse();
            return pts.ToArray();
        }
        /// <summary>Aviator teardrop: widest above the centre, tip aimed down-outward by tiltDeg.</summary>
        static Vector2[] Teardrop(float w, float h, float tiltDeg, int n = 36)
        {
            var p = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float s = Mathf.Sin(a);
                // Superellipse-ish: fuller at the top (s > 0), pinched toward the bottom.
                float rx = w * 0.5f * Mathf.Lerp(0.55f, 1f, (s + 1f) * 0.5f);
                float ry = h * 0.5f;
                Vector2 q = new Vector2(Mathf.Cos(a) * rx, s * ry + h * 0.08f);
                float c = Mathf.Cos(tiltDeg * Mathf.Deg2Rad), sn = Mathf.Sin(tiltDeg * Mathf.Deg2Rad);
                p[i] = new Vector2(q.x * c - q.y * sn, q.x * sn + q.y * c);
            }
            return p;
        }
        static Vector2[] MirrorU(Vector2[] o)
        {
            var r = new Vector2[o.Length];
            for (int i = 0; i < o.Length; i++) r[i] = new Vector2(-o[o.Length - 1 - i].x, o[o.Length - 1 - i].y);   // mirror and keep CCW
            return r;
        }
        static Vector2[] Offset(Vector2[] o, float d)   // radial inset/outset about the centroid (star-shaped outlines)
        {
            var r = new Vector2[o.Length];
            for (int i = 0; i < o.Length; i++) { float m = o[i].magnitude; r[i] = m > 1e-6f ? o[i] * ((m + d) / m) : o[i]; }
            return r;
        }

        // ---- conformal pieces ---------------------------------------------------------------
        /// <summary>A filled patch on the sphere: polar fan from the centre out to an outline.</summary>
        static Mesh SurfacePatchMesh(Vector3 centre, Vector2[] outline, float standoff, int rings = 3)
        {
            int n = outline.Length;
            var m = OutwardParam((u, v) =>
            {
                int i0 = Wrap(Mathf.FloorToInt(u * n), n); int i1 = Wrap(i0 + 1, n);
                float f = u * n - Mathf.Floor(u * n);
                Vector2 o = Vector2.Lerp(outline[i0], outline[i1], f) * v;
                return SurfDir(centre, o) * (HeadR + standoff);
            }, n, rings, wrapU: true);
            // Flush on the sphere: the radial direction IS the normal (and the fan's pole has none).
            { var vv = m.vertices; var nn = new Vector3[vv.Length]; for (int i = 0; i < vv.Length; i++) nn[i] = vv[i].normalized; m.normals = nn; }
            Scale(m, _cosScale);
            return m;
        }
        static GameObject SurfacePatch(Transform head, Material mat, Vector3 centre, Vector2[] outline, float standoff, int rings = 3)
            => Piece(head, SurfacePatchMesh(centre, outline, standoff), mat, castShadows: false);

        /// <summary>Tube along a list of DIRECTIONS at a standoff, optionally closed into a loop.</summary>
        static Mesh SweptTubeMesh(Vector3[] dirs, float standoff, float r, int sides, bool closed, float[] radiusScale = null)
        {
            int n = dirs.Length + (closed ? 2 : 0);
            var pts = new Vector3[n]; var rad = new float[n];
            for (int i = 0; i < n; i++)
            {
                int k = i % dirs.Length;
                pts[i] = SurfPos(dirs[k], standoff);
                rad[i] = r * _cosScale * (radiusScale != null ? radiusScale[k] : 1f);
            }
            return MeshGen.Tube(pts, rad, sides, !closed, !closed);
        }
        static GameObject SweptTube(Transform head, Material mat, Vector3[] dirs, float standoff, float r, int sides = 6, bool closed = false, float[] radiusScale = null)
            => Piece(head, SweptTubeMesh(dirs, standoff, r, sides, closed, radiusScale), mat);

        /// <summary>A small box lying on the surface at dir, its +Z along the surface normal.</summary>
        static GameObject SurfBlk(Transform head, Material mat, Vector3 dir, float standoff, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "cz";
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(head, false);
            go.transform.localPosition = SurfPos(dir, standoff);
            go.transform.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            go.transform.localScale = size * _cosScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>A band around part of the head, lifted off the sphere, with per-column (yTop, yBot).</summary>
        static Mesh HeadBandMesh(float standoff, float thetaFromDeg, float thetaToDeg, Func<float, Vector2> extents, int cols, int rows = 4)
        {
            var m = OutwardParam((u, v) =>
            {
                float th = Mathf.Lerp(thetaFromDeg, thetaToDeg, u) * Mathf.Deg2Rad;
                Vector2 e = extents(Mathf.Lerp(thetaFromDeg, thetaToDeg, u));
                float y = Mathf.Lerp(e.y, e.x, v);                       // bottom -> top
                float R = HeadR + standoff;
                float rr = Mathf.Sqrt(Mathf.Max(R * R - y * y, 1e-6f));
                return new Vector3(rr * Mathf.Sin(th), y, rr * Mathf.Cos(th));
            }, cols, rows, wrapU: Mathf.Abs(thetaToDeg - thetaFromDeg) >= 359.9f);
            Scale(m, _cosScale);
            return m;
        }
        static GameObject HeadBand(Transform head, Material mat, float standoff, float thetaFrom, float thetaTo, Func<float, Vector2> extents, int cols)
            => Piece(head, HeadBandMesh(standoff, thetaFrom, thetaTo, extents, cols), mat, castShadows: false);

        // ---- spectacles ---------------------------------------------------------------------
        /// <summary>
        /// One builder for every pair of glasses: per eye a lens patch, a rim tube on the same
        /// outline, a hinge block at the outermost point, a temple tube back to the ear on a low
        /// standoff and an ear hook down the meridian; bridges between the inner rim points.
        /// </summary>
        static void Spectacles(Transform head, Material frame, Material lens, Vector2[] outlineRight, Vector2 centreXY,
                               float rimR, int rimSides, float lensStandoff, float hingeSize, float templeY1, float hookLen,
                               float[] bridgeYs, float[] browScale = null, Material rimMat = null)
        {
            rimMat ??= frame;
            Vector3 hingeL = Vector3.zero, hingeR = Vector3.zero, innerL = Vector3.zero, innerR = Vector3.zero;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 centre = Dir(side * centreXY.x, centreXY.y, HeadR);
                Vector2[] outline = side > 0 ? outlineRight : MirrorU(outlineRight);
                SurfacePatch(head, lens, centre, outline, lensStandoff);
                var dirs = new Vector3[outline.Length];
                int iOuter = 0, iInner = 0;
                for (int i = 0; i < outline.Length; i++)
                {
                    dirs[i] = SurfDir(centre, outline[i]);
                    if (outline[i].x * side > outline[iOuter].x * side) iOuter = i;
                    if (outline[i].x * side < outline[iInner].x * side) iInner = i;
                }
                SweptTube(head, rimMat, dirs, lensStandoff, rimR, rimSides, closed: true, radiusScale: browScale);
                if (side > 0) { hingeR = dirs[iOuter]; innerR = dirs[iInner]; } else { hingeL = dirs[iOuter]; innerL = dirs[iInner]; }
                if (hingeSize > 0f) SurfBlk(head, frame, dirs[iOuter], lensStandoff - 0.002f, Vector3.one * hingeSize);
                // Temple: from the hinge back to the ear at a low standoff, then a hook down.
                Vector3 hinge = dirs[iOuter];
                Vector3 ear = Dir(side * Mathf.Sin(95f * Mathf.Deg2Rad), 0f, Mathf.Cos(95f * Mathf.Deg2Rad));
                ear = new Vector3(ear.x, templeY1 / HeadR, ear.z).normalized;
                var path = new List<Vector3>(PathDirs(hinge, ear, 0f, 10));
                if (hookLen > 0f)
                {
                    int hookN = 5;
                    for (int k = 1; k <= hookN; k++)
                    {
                        float ang = (hookLen / HeadR) * k / hookN;
                        path.Add((Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.Cross(ear, Vector3.up).normalized * -1f) * ear).normalized);
                    }
                }
                // Standoff ramps from the lens standoff at the hinge down to 6 mm at the temple.
                var pts = new Vector3[path.Count]; var rad = new float[path.Count];
                for (int i = 0; i < pts.Length; i++)
                {
                    float t = Mathf.Clamp01(i / 3f);
                    pts[i] = SurfPos(path[i], Mathf.Lerp(lensStandoff - 0.002f, 0.006f, t));
                    rad[i] = rimR * 0.9f * _cosScale;
                }
                Piece(head, MeshGen.Tube(pts, rad, rimSides, true, true), frame);
            }
            foreach (float by in bridgeYs)
            {
                Vector3 a = new Vector3(innerR.x, 0f, innerR.z).normalized; a = new Vector3(a.x, by / HeadR, a.z).normalized;
                Vector3 b = new Vector3(innerL.x, 0f, innerL.z).normalized; b = new Vector3(b.x, by / HeadR, b.z).normalized;
                SweptTube(head, rimMat, PathDirs(a, b, 0f, 6), lensStandoff + 0.001f, rimR * 0.9f, rimSides);
            }
        }

        // ---- eyewear ------------------------------------------------------------------------
        static void BuildGlasses(Transform h, Material m)
        {
            var frame = Own(Make.Mat(m.color, 0.6f));
            Spectacles(h, frame, Lens(new Color(0.85f, 0.92f, 1f), 0.25f, 0.9f), Circle(0.052f), new Vector2(0.08f, 0.02f),
                       0.003f, 6, 0.011f, 0.008f, 0.03f, 0.03f, new[] { 0.02f });
        }
        static void BuildSquareGlasses(Transform h, Material m)
        {
            var frame = Own(Make.Mat(m.color, 0.55f));
            var outline = RoundedPoly(new[] { new Vector2(-0.05f, -0.036f), new Vector2(0.05f, -0.036f), new Vector2(0.05f, 0.036f), new Vector2(-0.05f, 0.036f) }, new[] { 0.016f }, 5);
            Spectacles(h, frame, Lens(new Color(0.85f, 0.92f, 1f), 0.25f, 0.9f), outline, new Vector2(0.08f, 0.02f),
                       0.0042f, 8, 0.011f, 0.010f, 0.03f, 0.03f, new[] { 0.02f });
        }
        static void BuildSunglasses(Transform h, Material m)
        {
            var frame = Own(Make.Mat(m.color, 0.55f));
            var outline = RoundedPoly(new[] { new Vector2(-0.050f, -0.030f), new Vector2(0.044f, -0.034f), new Vector2(0.060f, 0.046f), new Vector2(-0.052f, 0.038f) },
                                      new[] { 0.014f, 0.028f, 0.010f, 0.010f }, 5);
            var brow = new float[outline.Length];
            for (int i = 0; i < outline.Length; i++) { float a = Mathf.Atan2(outline[i].y, outline[i].x) * Mathf.Rad2Deg; brow[i] = a > 20f && a < 160f ? 1.4f : 1f; }
            Spectacles(h, frame, Lens(new Color(0.03f, 0.03f, 0.04f), 0.85f, 0.95f), outline, new Vector2(0.082f, 0.02f),
                       0.005f, 8, 0.011f, 0.010f, 0.03f, 0.03f, new[] { 0.03f }, brow);
        }
        static void BuildAviators(Transform h, Material m)
        {
            // The accessory colour tints the LENS here; the wire frame is fixed gold.
            var lens = Lens(Color.Lerp(m.color, Color.black, 0.55f), 0.80f, 0.95f, 0.3f);
            Spectacles(h, Gold(), lens, Teardrop(0.098f, 0.088f, -20f), new Vector2(0.082f, 0.015f),
                       0.0025f, 6, 0.011f, 0.006f, 0.03f, 0.035f, new[] { 0.036f, 0.014f });
        }
        static void BuildVisorShades(Transform h, Material m)
        {
            var lens = Lens(Color.Lerp(m.color, Color.black, 0.7f), 0.85f, 0.95f);
            Vector2 Extents(float th)
            {
                float a = Mathf.Abs(th);
                float edge = 1f - 0.45f * Smooth(60f, 75f, a);                 // converge over the outer 15 deg
                float top = 0.052f * edge, bot = -0.012f * edge;
                if (a < 6f) bot += 0.008f * (1f - a / 6f);                     // nose notch
                return new Vector2(top, bot);
            }
            HeadBand(h, lens, 0.010f, -75f, 75f, Extents, 40);
            // Trim along the top edge and temples to the ears.
            var top = new Vector3[21];
            for (int i = 0; i <= 20; i++) { float th = Mathf.Lerp(-75f, 75f, i / 20f); float y = Extents(th).x; float R = HeadR; float rr = Mathf.Sqrt(R * R - y * y); top[i] = new Vector3(rr * Mathf.Sin(th * Mathf.Deg2Rad), y, rr * Mathf.Cos(th * Mathf.Deg2Rad)).normalized; }
            SweptTube(h, m, top, 0.012f, 0.0035f, 6);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 a = Dir(side * Mathf.Sin(75f * Mathf.Deg2Rad), 0.04f / HeadR, Mathf.Cos(75f * Mathf.Deg2Rad));
                Vector3 b = Dir(side * Mathf.Sin(95f * Mathf.Deg2Rad), 0.04f / HeadR, Mathf.Cos(95f * Mathf.Deg2Rad));
                var path = new List<Vector3>(PathDirs(a, b, 0f, 6));
                for (int k = 1; k <= 4; k++) path.Add(Quaternion.AngleAxis((0.025f / HeadR) * k / 4f * Mathf.Rad2Deg, -Vector3.Cross(b, Vector3.up).normalized) * b);
                SweptTube(h, m, path.ToArray(), 0.006f, 0.003f, 6);
            }
        }
        static void BuildMonocle(Transform h, Material m)
        {
            Vector3 centre = Dir(0.08f, 0.02f, HeadR);
            var outline = Circle(0.046f, 32);
            var dirs = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++) dirs[i] = SurfDir(centre, outline[i]);
            SweptTube(h, Gold(), dirs, 0.009f, 0.0035f, 8, closed: true);
            SurfacePatch(h, Lens(new Color(0.85f, 0.92f, 1f), 0.22f, 0.92f), centre, Circle(0.043f, 32), 0.010f);
            // Gallery stud at the bottom-outer point and a silk cord round the ear to the nape.
            Vector3 stud = SurfDir(centre, new Vector2(0.046f * Mathf.Cos(-40f * Mathf.Deg2Rad), 0.046f * Mathf.Sin(-40f * Mathf.Deg2Rad)));
            SurfBlk(h, Gold(), stud, 0.010f, new Vector3(0.006f, 0.006f, 0.008f));
            var cord = PathDirs(stud, Dir(0.02f, -0.08f, -0.17f), 0.06f, 16);
            SweptTube(h, StemBlack(), cord, 0.004f, 0.002f, 6);
        }
        static void BuildEyepatch(Transform h, Material m)
        {
            Vector3 centre = Dir(0.08f, 0.02f, HeadR);
            var outline = Teardrop(0.100f, 0.112f, -30f, 32);
            SurfacePatch(h, Own(Make.Mat(new Color(0.06f, 0.06f, 0.07f), 0.15f)), centre, outline, 0.004f);
            var dirs = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++) dirs[i] = SurfDir(centre, outline[i]);
            SweptTube(h, Own(Make.Mat(new Color(0.12f, 0.12f, 0.13f), 0.2f)), dirs, 0.0055f, 0.0015f, 6, closed: true);
            // Two straps closing a loop at the nape: over the crown and round the temple.
            Vector3 nape = Dir(0f, -0.06f, -0.19f);
            SweptTube(h, m, PathDirs(Dir(0.08f, 0.10f, 0.18f), nape, 0.15f, 20), 0.004f, 0.006f, 6);
            SweptTube(h, m, PathDirs(Dir(0.16f, 0.04f, 0.10f), nape, 0.05f, 20), 0.004f, 0.005f, 6);
        }
        static void BuildSkiGoggles(Transform h, Material m)
        {
            // A rounded-rectangle shell standing off the face, flat-topped by a bulge that rises to
            // a plateau; the lens is an inner plate just inside the front edge; a strap round the back.
            Vector3 centre = HairShape.Dir(EyePhi * Mathf.Deg2Rad, 0f);
            float Outline(float a)
            {
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float rx = 52f, ry = 20f; float p = 3.2f;
                float r = 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(ca) / ry, p) + Mathf.Pow(Mathf.Abs(sa) / rx, p), 1f / p);
                if (ca < 0f && Mathf.Abs(sa) < 0.25f) r -= 3.5f * (1f - Mathf.Abs(sa) / 0.25f);   // nose notch
                return r * Mathf.Deg2Rad;
            }
            float Shell(float a, float t) => 0.016f * Mathf.Clamp01(1f - Mathf.Pow(Mathf.Max(0f, (t - 0.72f) / 0.28f), 1.6f)) - 0.004f;
            var body = FacePlateMesh(centre, Outline, 0.004f, Shell, 56, 8, out var rim);
            Piece(h, body, Own(Make.Mat(m.color, 0.5f)));
            SweptTube(h, Own(Make.Mat(new Color(0.75f, 0.75f, 0.75f), 0.2f)), rim, 0.004f, 0.005f, 8, closed: true);   // foam seal
            var lens = FacePlateMesh(centre, a => Outline(a) * 0.8f, 0.0215f, null, 56, 3, out _);
            Piece(h, lens, Lens(new Color(0.95f, 0.45f, 0.15f), 0.97f, 0.95f, 0.3f), castShadows: false);
            HeadBand(h, m, 0.004f, 52f, 308f, th => new Vector2(0.02f + 0.0175f + 0.006f, 0.02f - 0.0175f + 0.006f), 40);
            SurfBlk(h, Dark(), Dir(Mathf.Sin(250f * Mathf.Deg2Rad), 0.03f / HeadR, Mathf.Cos(250f * Mathf.Deg2Rad)), 0.006f, new Vector3(0.02f, 0.035f, 0.006f));
        }
        static void BuildReadingGlasses(Transform h, Material m)
        {
            var frame = Own(Make.Mat(m.color, 0.6f));
            var outline = RoundedPoly(new[] { new Vector2(-0.044f, -0.023f), new Vector2(0.044f, -0.023f), new Vector2(0.044f, 0.023f), new Vector2(-0.044f, 0.023f) },
                                      new[] { 0.023f, 0.023f, 0.004f, 0.004f }, 6);
            Spectacles(h, frame, Lens(new Color(0.85f, 0.92f, 1f), 0.22f, 0.9f), outline, new Vector2(0.072f, -0.020f),
                       0.0025f, 8, 0.010f, 0.007f, 0.035f, 0.03f, new[] { -0.018f });
        }

        // ---- props and jewellery ------------------------------------------------------------
        // Piece at a head-local pose (girth-1 metres) with the mesh scaled by _cosScale.
        static GameObject PieceAt(Transform head, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, bool shadows = true)
        {
            MeshGen.Transform(mesh, pos * _cosScale, rot, Vector3.one * _cosScale);
            return Piece(head, mesh, mat, shadows);
        }
        static Mesh StudDome() => MeshGen.Lathe(new[] { new Vector2(0.011f, 0f), new Vector2(0.0105f, 0.0025f), new Vector2(0.007f, 0.0045f), new Vector2(0f, 0.0055f) }, 16);
        static readonly Vector3 LobeR = new Vector3(1f, -0.18f, -0.05f).normalized;

        static void BuildPipe(Transform h, Material m)
        {
            // Generated: a hollow lathe bowl and a swept stem (the downloaded candidate sat badly).
            // Local frame: bit at the origin (in the mouth), stem along +Z, bowl standing up at the far end.
            var bowl = MeshGen.Lathe(new[] { new Vector2(0.011f, 0f), new Vector2(0.024f, 0.010f), new Vector2(0.027f, 0.026f), new Vector2(0.024f, 0.042f), new Vector2(0.020f, 0.050f), new Vector2(0.014f, 0.050f), new Vector2(0.014f, 0.032f), new Vector2(0f, 0.032f) }, 20);
            var tobacco = MeshGen.Disc(new Vector3(0f, 0.031f, 0f), Vector3.up, 0.014f, 16);
            var head2 = MeshGen.Combine(bowl, tobacco);
            MeshGen.Transform(head2, new Vector3(0f, -0.012f, 0.135f), Quaternion.identity);
            var path = MeshGen.Spline(new[] { new Vector3(0f, 0f, -0.012f), new Vector3(0f, 0f, 0.05f), new Vector3(0f, -0.004f, 0.10f), new Vector3(0f, 0.008f, 0.132f) }, 6);
            var radii = new float[path.Length]; for (int i = 0; i < radii.Length; i++) radii[i] = Mathf.Lerp(0.0055f, 0.009f, i / (float)(radii.Length - 1));
            var stem = MeshGen.Tube(path, radii, 10, true, false);
            var rot = Quaternion.Euler(22f, -10f, 0f);   // forward, 22 deg down, 10 deg out of the mouth corner
            Vector3 mouth = new Vector3(0.055f, -0.08f, 0.150f);
            PieceAt(h, head2, m, mouth, rot);
            PieceAt(h, stem, StemBlack(), mouth, rot);
        }
        static void BuildStudEarrings(Transform h, Material m)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 n = new Vector3(side * LobeR.x, LobeR.y, LobeR.z);
                PieceAt(h, StudDome(), m, n * (HeadR - 0.003f), Quaternion.FromToRotation(Vector3.up, n));
            }
        }
        static void BuildHoopEarrings(Transform h, Material m)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var ring = MeshGen.Torus(0.030f, 0.0028f, 28, 8);
                var rot = Quaternion.Euler(0f, 0f, side * 84f);   // ring plane vertical, normal along X, tilted 6 deg out
                PieceAt(h, ring, m, new Vector3(side * 0.192f, -0.062f, -0.010f), rot);
            }
        }
        static void BuildDangleEarrings(Transform h, Material m)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 lobe = new Vector3(side * 0.187f, -0.034f, -0.009f);
                Vector3 n = new Vector3(side * LobeR.x, LobeR.y, LobeR.z);
                var dome = StudDome(); MeshGen.Transform(dome, Vector3.zero, Quaternion.FromToRotation(Vector3.up, n));
                var link = MeshGen.Tube(new[] { new Vector3(0f, -0.004f, 0f), new Vector3(0f, -0.020f, 0f), new Vector3(0f, -0.036f, 0f) }, 0.0018f, 6, false, false);
                var ring = MeshGen.Torus(0.005f, 0.0012f, 16, 6); MeshGen.Transform(ring, new Vector3(0f, -0.038f, 0f), Quaternion.Euler(90f, 0f, 0f));
                var drop = MeshGen.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.007f, 0.006f), new Vector2(0.012f, 0.014f), new Vector2(0.010f, 0.024f), new Vector2(0.004f, 0.030f), new Vector2(0f, 0.032f) }, 16);
                MeshGen.Transform(drop, new Vector3(0f, -0.075f, 0f), Quaternion.identity);
                PieceAt(h, MeshGen.Combine(dome, link, ring, drop), m, lobe, Quaternion.identity);
            }
        }
        static void BuildNoseStud(Transform h, Material m)
        {
            Vector3 n = new Vector3(0.13f, -0.24f, 1f).normalized;
            var dome = MeshGen.Lathe(new[] { new Vector2(0.006f, 0f), new Vector2(0.0055f, 0.002f), new Vector2(0.003f, 0.0035f), new Vector2(0f, 0.004f) }, 12);
            PieceAt(h, dome, m, n * (HeadR - 0.002f), Quaternion.FromToRotation(Vector3.up, n));
            var gem = MeshGen.Lathe(new[] { new Vector2(0f, -0.0025f), new Vector2(0.0025f, 0f), new Vector2(0f, 0.0025f) }, 10);
            PieceAt(h, gem, Glass(), n * (HeadR + 0.0025f), Quaternion.FromToRotation(Vector3.up, n));
        }
        static void BuildSeptumRing(Transform h, Material m)
        {
            var ring = MeshGen.Torus(0.013f, 0.0022f, 24, 8);
            PieceAt(h, ring, m, new Vector3(0f, -0.066f, 0.190f), Quaternion.Euler(-60f, 0f, 0f));
        }
        static void BuildEyebrowPiercing(Transform h, Material m)
        {
            Vector3 p1 = new Vector3(0.11f, 0.085f, 0.1295f), p2 = new Vector3(0.11f, 0.055f, 0.1448f);
            var b1 = MeshGen.Lathe(new[] { new Vector2(0f, -0.004f), new Vector2(0.004f, 0f), new Vector2(0f, 0.004f) }, 10); MeshGen.Transform(b1, p1.normalized * 0.193f, Quaternion.identity);
            var b2 = MeshGen.Lathe(new[] { new Vector2(0f, -0.004f), new Vector2(0.004f, 0f), new Vector2(0f, 0.004f) }, 10); MeshGen.Transform(b2, p2.normalized * 0.193f, Quaternion.identity);
            var pts = new Vector3[5]; for (int i = 0; i < 5; i++) pts[i] = Vector3.Slerp(p1.normalized, p2.normalized, i / 4f) * 0.1935f;
            var bar = MeshGen.Tube(pts, 0.0022f, 8, false, false);
            PieceAt(h, MeshGen.Combine(b1, b2, bar), m, Vector3.zero, Quaternion.identity);
        }
        static void BuildCigar(Transform h, Material m)
        {
            var rot = Quaternion.Euler(-12f, -10f, 0f) * Quaternion.Euler(90f, 0f, 0f);   // +Y of the lathe -> forward, 12 deg down, 10 deg out
            Vector3 root = new Vector3(-0.045f, -0.078f, 0.152f);
            PieceAt(h, MeshGen.Cylinder(0.011f, 0.011f, 0.130f, 16, true, false), Tobacco(), root, rot);
            var band = MeshGen.Cylinder(0.0125f, 0.0125f, 0.012f, 16, false, false); MeshGen.Transform(band, new Vector3(0f, 0.045f, 0f));
            PieceAt(h, band, m, root, rot);
            var ash = MeshGen.Cylinder(0.011f, 0.011f, 0.008f, 16, false, true); MeshGen.Transform(ash, new Vector3(0f, 0.130f, 0f));
            PieceAt(h, ash, Ash(), root, rot);
            var ember = MeshGen.Disc(new Vector3(0f, 0.1385f, 0f), Vector3.up, 0.007f, 12);
            PieceAt(h, ember, Ember(), root, rot);
        }
        static void BuildToothpick(Transform h, Material m)
        {
            var pick = MeshGen.Cylinder(0.0022f, 0.0008f, 0.075f, 8, true, true);
            var rot = Quaternion.Euler(-8f, 25f, 0f) * Quaternion.Euler(90f, 0f, 0f);
            PieceAt(h, pick, PaleWood(), new Vector3(0.055f, -0.078f, 0.154f), rot);
        }
        static void BuildLollipop(Transform h, Material m)
        {
            var rot = Quaternion.Euler(-10f, 12f, 0f) * Quaternion.Euler(90f, 0f, 0f);
            Vector3 root = new Vector3(0.02f, -0.08f, 0.156f);
            PieceAt(h, MeshGen.Cylinder(0.003f, 0.003f, 0.10f, 8, false, false), Paper(), root, rot);
            var candy = MeshGen.Lathe(new[] { new Vector2(0f, -0.006f), new Vector2(0.020f, -0.006f), new Vector2(0.024f, -0.003f), new Vector2(0.024f, 0.003f), new Vector2(0.020f, 0.006f), new Vector2(0f, 0.006f) }, 32);
            MeshGen.Transform(candy, new Vector3(0f, 0.115f, 0f), Quaternion.identity);
            // A swirl: stripes in the lathe's (angle, profile) UV, painted into a small texture in the accessory colour.
            var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[128 * 128]; Color32 c = m.color, w = Color.white;
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++) px[y * 128 + x] = Mathf.Repeat(x / 128f * 2f + y / 128f * 5f, 1f) < 0.5f ? c : w;
            tex.SetPixels32(px); tex.Apply(false, true);
            var candyMat = Own(Make.MatTex(tex, 0.9f));
            var go = PieceAt(h, candy, candyMat, root, rot);
            go.GetComponent<GeneratedMeshOwner>().Tex = tex;
        }
        static void BuildBindi(Transform h, Material m)
        {
            SurfacePatch(h, Own(Make.Mat(m.color, 0.6f)), Dir(0f, 0.055f, HeadR), Circle(0.0045f, 16), 0.0035f, 2);
            var gem = MeshGen.Lathe(new[] { new Vector2(0f, -0.002f), new Vector2(0.002f, 0f), new Vector2(0f, 0.002f) }, 10);
            Vector3 n = Dir(0f, 0.055f, HeadR);
            PieceAt(h, gem, Glass(), n * (HeadR + 0.004f), Quaternion.FromToRotation(Vector3.up, n));
        }
        static void BuildVampireFangs(Transform h, Material m)
        {
            var parts = new List<Mesh>();
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 baseP = new Vector3(side * 0.03f, -0.085f, 0.167f).normalized * 0.187f;
                Vector3 down = (Quaternion.Euler(12f, 0f, side * -10f) * Vector3.down).normalized;
                Vector3 fwd = Vector3.forward;
                var path = new[] { baseP, baseP + down * 0.012f + fwd * 0.0015f, baseP + down * 0.024f };
                parts.Add(MeshGen.Tube(path, new[] { 0.0045f, 0.0028f, 0.0008f }, 8, false, true));
            }
            PieceAt(h, MeshGen.Combine(parts.ToArray()), Ivory(), Vector3.zero, Quaternion.identity);
        }
        /// <summary>Chain necklace on the TORSO (a head-parented chest item slides across the chest
        /// on every turn): a beaded tube round the neck opening resting on the chest and back faces.</summary>
        static void BuildChainNecklace(Transform torso, Vector3 dims, Material m)
        {
            float hw = dims.x * 0.5f, hh = dims.y * 0.5f, hd = dims.z * 0.5f;
            var ctrl = new List<Vector3>
            {
                new Vector3(0f, hh - 0.11f, hd + 0.004f),
                new Vector3(0.06f, hh - 0.07f, hd + 0.004f),
                new Vector3(0.135f, hh + 0.003f, hd - 0.02f),
                new Vector3(0.135f, hh + 0.003f, -hd + 0.02f),
                new Vector3(0.13f, hh - 0.03f, -(hd + 0.003f)),
                new Vector3(0f, hh - 0.04f, -(hd + 0.003f)),
                new Vector3(-0.13f, hh - 0.03f, -(hd + 0.003f)),
                new Vector3(-0.135f, hh + 0.003f, -hd + 0.02f),
                new Vector3(-0.135f, hh + 0.003f, hd - 0.02f),
                new Vector3(-0.06f, hh - 0.07f, hd + 0.004f),
                new Vector3(0f, hh - 0.11f, hd + 0.004f),
            };
            var path = MeshGen.Spline(ctrl.ToArray(), 5);
            var radii = new float[path.Length];
            for (int i = 0; i < radii.Length; i++) radii[i] = (i % 2 == 0) ? 0.0035f : 0.0022f;
            var chain = MeshGen.Tube(path, radii, 8, false, false);
            var pendant = MeshGen.Extrude(MeshGen.Superellipse(0.020f, 0.020f, 2f, 24), 0.004f, 0.001f);
            MeshGen.Transform(pendant, new Vector3(0f, hh - 0.135f, hd + 0.006f), Quaternion.identity);
            var bail = MeshGen.Torus(0.004f, 0.001f, 12, 6); MeshGen.Transform(bail, new Vector3(0f, hh - 0.113f, hd + 0.005f), Quaternion.Euler(0f, 0f, 90f));
            var clasp = MeshGen.Torus(0.004f, 0.0012f, 12, 6); MeshGen.Transform(clasp, new Vector3(0f, hh - 0.04f, -(hd + 0.004f)), Quaternion.Euler(90f, 0f, 0f));
            Piece(torso, MeshGen.Combine(chain, pendant, bail, clasp), m);
        }

        // ---- the catalog (index 0 = None; order is wire state) --------------------------------
        // Nipple Piercings was removed (a head-parented chest item slid across the jersey); every
        // index after it shifts by one, the same way commit 5fdec54 handled its removals.
        static readonly List<AccessoryEntry> _accessories = new List<AccessoryEntry>
        {
            new AccessoryEntry { Name = "None", Build = (h, m) => { } },

            // EYEWEAR / MASKS ---------------------------------------------------------------
            new AccessoryEntry { Name = "Glasses",        Build = BuildGlasses },
            new AccessoryEntry { Name = "Square Glasses", Build = BuildSquareGlasses },
            new AccessoryEntry { Name = "Sunglasses",     Build = BuildSunglasses },
            new AccessoryEntry { Name = "Aviators",       Build = BuildAviators },
            new AccessoryEntry { Name = "Visor Shades",   Build = BuildVisorShades },
            new AccessoryEntry { Name = "Monocle",        Build = BuildMonocle },
            new AccessoryEntry { Name = "Eyepatch",       Build = BuildEyepatch },
            new AccessoryEntry { Name = "Ski Goggles",    Build = BuildSkiGoggles, Smoothness = 0.5f },
            new AccessoryEntry { Name = "Reading Glasses", Build = BuildReadingGlasses },
            // "Batman Mask" renamed: a generic cowl with open eye holes and short round ears.
            new AccessoryEntry { Name = "Vigilante Cowl", Headgear = true, Build = BuildCowl, Smoothness = 0.35f },
            new AccessoryEntry { Name = "Hockey Mask",    Build = BuildHockeyMask, Smoothness = 0.55f },
            new AccessoryEntry { Name = "Venetian Mask",  Build = BuildVenetianMask, Smoothness = 0.75f, Metallic = 0.6f },
            new AccessoryEntry { Name = "Gas Mask",       Build = BuildGasMask, Smoothness = 0.25f },
            new AccessoryEntry { Name = "Welding Mask",   Headgear = true, Build = BuildWeldingMask, Smoothness = 0.3f },

            // JEWELLERY / FACE PROPS ---------------------------------------------------------
            new AccessoryEntry { Name = "Pipe",             Build = BuildPipe, Smoothness = 0.45f },
            new AccessoryEntry { Name = "Stud Earrings",    Build = BuildStudEarrings, Smoothness = 0.85f, Metallic = 0.75f },
            new AccessoryEntry { Name = "Hoop Earrings",    Build = BuildHoopEarrings, Smoothness = 0.85f, Metallic = 0.75f },
            new AccessoryEntry { Name = "Dangle Earrings",  Build = BuildDangleEarrings, Smoothness = 0.85f, Metallic = 0.75f },
            new AccessoryEntry { Name = "Nose Stud",        Build = BuildNoseStud, Smoothness = 0.85f, Metallic = 0.75f },
            new AccessoryEntry { Name = "Septum Ring",      Build = BuildSeptumRing, Smoothness = 0.85f, Metallic = 0.75f },
            new AccessoryEntry { Name = "Eyebrow Piercing", Build = BuildEyebrowPiercing, Smoothness = 0.85f, Metallic = 0.75f },
            new AccessoryEntry { Name = "Cigar",            Build = BuildCigar, Smoothness = 0.35f },
            new AccessoryEntry { Name = "Toothpick",        Build = BuildToothpick },
            new AccessoryEntry { Name = "Lollipop",         Build = BuildLollipop, Smoothness = 0.9f },
            new AccessoryEntry { Name = "Bindi",            Build = BuildBindi, Smoothness = 0.6f },
            new AccessoryEntry { Name = "Vampire Fangs",    Build = BuildVampireFangs },
            new AccessoryEntry { Name = "Chain Necklace",   Build = (h, m) => { }, BuildBody = BuildChainNecklace, Smoothness = 0.85f, Metallic = 0.75f },

            // HEADWEAR (only wearable when bald) ---------------------------------------------
            new AccessoryEntry { Name = "Cap",         Headgear = true, Build = BuildCap, Smoothness = 0.05f },
            new AccessoryEntry { Name = "Bucket Hat",  Headgear = true, Build = BuildBucketHat, Smoothness = 0.05f },
            new AccessoryEntry { Name = "Fedora",      Headgear = true, Build = BuildFedora, Smoothness = 0.05f },
            new AccessoryEntry { Name = "Top Hat",     Headgear = true, Build = BuildTopHat, Smoothness = 0.5f },
            new AccessoryEntry { Name = "Cowboy Hat",  Headgear = true, Build = BuildCowboyHat, Smoothness = 0.05f },
            new AccessoryEntry { Name = "Beret",       Headgear = true, Build = BuildBeret, Smoothness = 0.03f },
            new AccessoryEntry { Name = "Peaky Cap",   Headgear = true, Build = BuildPeakyCap, Smoothness = 0.05f },
            new AccessoryEntry { Name = "Headband",    Headgear = true, Build = BuildHeadband, Smoothness = 0.02f },
            new AccessoryEntry { Name = "Trapper Hat", Headgear = true, Build = BuildTrapperHat, Smoothness = 0.05f },
            new AccessoryEntry { Name = "Sombrero",    Headgear = true, Build = BuildSombrero, Smoothness = 0.05f },
            new AccessoryEntry { Name = "Party Hat",   Headgear = true, Build = BuildPartyHat, Smoothness = 0.1f },
            new AccessoryEntry { Name = "Wizard Hat",  Headgear = true, Build = BuildWizardHat, Smoothness = 0.05f },
        };
    }
}
