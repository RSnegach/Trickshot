using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Runtime mesh generators for cosmetics: parametric surfaces, lathes, swept tubes, tori,
    /// extruded outlines and discs, plus combine/transform/flat-shade utilities. Every builder
    /// returns a fresh Mesh with positions, normals, UVs and triangles; the caller owns it (hand it
    /// to <see cref="GeneratedMeshOwner"/> so a body teardown frees it).
    ///
    /// WINDING. Unity treats a triangle (a, b, c) as front-facing when Cross(b - a, c - a) points at
    /// the viewer, so for a parametric surface f(u, v) the front side is Cross(df/du, df/dv). Every
    /// builder below orders its parameters so that side is OUTWARD (verified against the lathe,
    /// tube and torus derivations in the comments), and <see cref="Param"/> takes a flip for the
    /// cases where a caller's own f(u, v) runs the other way. Nothing here emits double-sided
    /// geometry; if a piece shows its back face, fix the parameterisation rather than doubling it.
    /// </summary>
    public static class MeshGen
    {
        // ------------------------------------------------------------------ parametric surface
        /// <summary>
        /// Sample f over u, v in [0, 1] on an (nu x nv) quad grid. Normals are analytic finite
        /// differences of f, smooth across the grid. wrapU/wrapV close the surface around that axis
        /// (the last column/row is welded to the first). flip reverses winding and normals.
        /// </summary>
        public static Mesh Param(Func<float, float, Vector3> f, int nu, int nv,
                                 bool wrapU = false, bool wrapV = false, bool flip = false,
                                 float uvRepeatU = 1f, float uvRepeatV = 1f)
        {
            nu = Mathf.Max(1, nu); nv = Mathf.Max(1, nv);
            int cols = nu + 1, rows = nv + 1;
            var verts = new Vector3[cols * rows];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            const float h = 1e-3f;
            for (int j = 0; j < rows; j++)
            {
                float v = j / (float)nv;
                for (int i = 0; i < cols; i++)
                {
                    float u = i / (float)nu;
                    int idx = j * cols + i;
                    Vector3 p = f(u, v);
                    // Central differences, clamped at the open edges so the edge normal still
                    // reads the surface rather than a zero step.
                    float u0 = wrapU ? u - h : Mathf.Max(0f, u - h), u1 = wrapU ? u + h : Mathf.Min(1f, u + h);
                    float v0 = wrapV ? v - h : Mathf.Max(0f, v - h), v1 = wrapV ? v + h : Mathf.Min(1f, v + h);
                    // Divide by the parameter step: the raw differences are ~1e-4 m, whose cross
                    // product is below Vector3.Normalize's 1e-5 epsilon and comes back as ZERO,
                    // which the Standard shader renders as black. Derivatives are object-sized.
                    Vector3 du = (f(u1, v) - f(u0, v)) / Mathf.Max(u1 - u0, 1e-6f);
                    Vector3 dv = (f(u, v1) - f(u, v0)) / Mathf.Max(v1 - v0, 1e-6f);
                    Vector3 n = Vector3.Cross(du, dv);
                    if (n.sqrMagnitude < 1e-14f)
                    {
                        // Degenerate (a pole): borrow a neighbour a little inward.
                        Vector3 du2 = f(Mathf.Clamp01(u + 0.02f), Mathf.Clamp01(v + 0.02f)) - p;
                        Vector3 dv2 = f(Mathf.Clamp01(u - 0.02f), Mathf.Clamp01(v + 0.02f)) - p;
                        n = Vector3.Cross(du2, dv2);
                        if (n.sqrMagnitude < 1e-14f) n = Vector3.up;
                    }
                    n.Normalize();
                    verts[idx] = p;
                    norms[idx] = flip ? -n : n;
                    uvs[idx] = new Vector2(u * uvRepeatU, v * uvRepeatV);
                }
            }
            var tris = new List<int>(nu * nv * 6);
            for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int i1 = (i + 1 == cols) ? 0 : i + 1;
                int j1 = (j + 1 == rows) ? 0 : j + 1;
                if (wrapU && i + 1 == nu) i1 = 0;
                if (wrapV && j + 1 == nv) j1 = 0;
                int a = j * cols + i, b = j * cols + i1, c = j1 * cols + i, d = j1 * cols + i1;
                // (a, b, c): b is +u, c is +v -> Cross(du, dv) = front, per the class note.
                if (!flip) { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c); }
                else       { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
            }
            return Finish(verts, norms, uvs, tris.ToArray());
        }

        // ------------------------------------------------------------------ lathe
        /// <summary>
        /// Revolve a 2D profile (x = radius, y = height) around local +Y. Profile runs bottom to
        /// top; a point with radius 0 makes a pole. Outward normal is Cross(d/dphi, d/dprofile),
        /// which is the (dy, -dr) profile normal - outward for a profile traversed upward with the
        /// solid on its left, i.e. the ordinary way to write a hat crown or a cone.
        /// </summary>
        public static Mesh Lathe(Vector2[] profile, int seg = 32, bool smooth = true, float startDeg = 0f, float sweepDeg = 360f)
        {
            if (profile == null || profile.Length < 2) throw new ArgumentException("lathe profile needs 2+ points");
            var pts = profile;
            bool full = Mathf.Abs(sweepDeg - 360f) < 1e-3f;
            Vector3 F(float u, float v)
            {
                float phi = (startDeg + u * sweepDeg) * Mathf.Deg2Rad;
                // Piecewise-linear profile sampled at v.
                float t = v * (pts.Length - 1);
                int k = Mathf.Clamp(Mathf.FloorToInt(t), 0, pts.Length - 2);
                Vector2 q = Vector2.Lerp(pts[k], pts[k + 1], t - k);
                return new Vector3(q.x * Mathf.Sin(phi), q.y, q.x * Mathf.Cos(phi));
            }
            var m = Param(F, seg, (pts.Length - 1) * (smooth ? 1 : 1), wrapU: full, wrapV: false);
            if (!smooth) m = Flat(m);
            return m;
        }

        /// <summary>Cone / frustum / cylinder about +Y, base at y = 0. r1 at the bottom, r2 at the top.</summary>
        public static Mesh Cylinder(float r1, float r2, float height, int seg = 24, bool capBottom = true, bool capTop = true)
        {
            var side = Lathe(new[] { new Vector2(r1, 0f), new Vector2(r2, height) }, seg);
            var parts = new List<Mesh> { side };
            if (capBottom && r1 > 0f) parts.Add(Disc(Vector3.zero, Vector3.down, r1, seg));
            if (capTop && r2 > 0f) parts.Add(Disc(new Vector3(0f, height, 0f), Vector3.up, r2, seg));
            return Combine(parts.ToArray());
        }

        // ------------------------------------------------------------------ torus
        /// <summary>
        /// Ring of major radius R and tube radius r about local +Y (lying flat). arcDeg &lt; 360 gives an
        /// open arc (a half-hoop, a horseshoe). Outward normal is Cross(d/dphi, d/dtheta).
        /// </summary>
        public static Mesh Torus(float R, float r, int segRing = 32, int segTube = 12, float arcDeg = 360f, bool capEnds = true)
        {
            bool full = Mathf.Abs(arcDeg - 360f) < 1e-3f;
            Vector3 F(float u, float v)
            {
                float phi = u * arcDeg * Mathf.Deg2Rad;
                float th = v * Mathf.PI * 2f;
                Vector3 ring = new Vector3(Mathf.Sin(phi), 0f, Mathf.Cos(phi));
                return ring * (R + r * Mathf.Cos(th)) + new Vector3(0f, r * Mathf.Sin(th), 0f);
            }
            var m = Param(F, segRing, segTube, wrapU: full, wrapV: true);
            if (full || !capEnds) return m;
            // Cap the open ends with discs facing away from the arc.
            Vector3 c0 = new Vector3(0f, 0f, R), n0 = Vector3.left;                       // phi = 0 end, tangent is +X
            float a = arcDeg * Mathf.Deg2Rad;
            Vector3 c1 = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * R;
            Vector3 n1 = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));                    // tangent at the far end
            return Combine(m, Disc(c0, n0, r, segTube), Disc(c1, n1, r, segTube));
        }

        // ------------------------------------------------------------------ swept tube
        /// <summary>
        /// A tube along a polyline with a per-node radius (taper), using parallel-transport frames so
        /// the section never twists. Nodes are the tube's centreline; 3+ nodes. Outward normal is
        /// Cross(d/dtheta, T). Cap the ends with discs when the tube is not buried in something.
        /// </summary>
        public static Mesh Tube(Vector3[] path, float[] radius, int seg = 12, bool capStart = false, bool capEnd = true)
        {
            if (path == null || path.Length < 2) throw new ArgumentException("tube needs 2+ nodes");
            int n = path.Length;
            var T = new Vector3[n]; var N = new Vector3[n]; var B = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 t = i == 0 ? path[1] - path[0] : i == n - 1 ? path[n - 1] - path[n - 2] : path[i + 1] - path[i - 1];
                T[i] = t.sqrMagnitude > 1e-12f ? t.normalized : (i > 0 ? T[i - 1] : Vector3.up);
            }
            // Initial normal: anything perpendicular to T0, preferring "up" so a horizontal tube's
            // seam sits underneath.
            Vector3 n0 = Vector3.Cross(T[0], Vector3.up);
            if (n0.sqrMagnitude < 1e-6f) n0 = Vector3.Cross(T[0], Vector3.forward);
            N[0] = Vector3.Cross(n0.normalized, T[0]).normalized;
            B[0] = Vector3.Cross(T[0], N[0]);
            for (int i = 1; i < n; i++)
            {
                // Parallel transport: rotate the previous frame by the rotation that takes T[i-1] to T[i].
                var q = Quaternion.FromToRotation(T[i - 1], T[i]);
                N[i] = (q * N[i - 1]).normalized;
                B[i] = Vector3.Cross(T[i], N[i]);
            }
            Vector3 F(float u, float v)
            {
                float th = u * Mathf.PI * 2f;
                float t = v * (n - 1);
                int k = Mathf.Clamp(Mathf.FloorToInt(t), 0, n - 2);
                float s = t - k;
                Vector3 c = Vector3.Lerp(path[k], path[k + 1], s);
                Vector3 nn = Vector3.Slerp(N[k], N[k + 1], s);
                Vector3 bb = Vector3.Slerp(B[k], B[k + 1], s);
                float r = Mathf.Lerp(radius[Mathf.Min(k, radius.Length - 1)], radius[Mathf.Min(k + 1, radius.Length - 1)], s);
                return c + (nn * Mathf.Cos(th) + bb * Mathf.Sin(th)) * r;
            }
            var m = Param(F, seg, n - 1, wrapU: true, wrapV: false);
            var parts = new List<Mesh> { m };
            if (capStart) parts.Add(Disc(path[0], -T[0], radius[0], seg));
            if (capEnd) parts.Add(Disc(path[n - 1], T[n - 1], radius[Mathf.Min(n - 1, radius.Length - 1)], seg));
            return parts.Count == 1 ? m : Combine(parts.ToArray());
        }

        /// <summary>Tube helper with a uniform radius.</summary>
        public static Mesh Tube(Vector3[] path, float radius, int seg = 12, bool capStart = false, bool capEnd = true)
        {
            var r = new float[path.Length];
            for (int i = 0; i < r.Length; i++) r[i] = radius;
            return Tube(path, r, seg, capStart, capEnd);
        }

        /// <summary>Sample a Catmull-Rom spline through the control points (n per segment).</summary>
        public static Vector3[] Spline(Vector3[] ctrl, int perSegment = 6)
        {
            if (ctrl.Length < 2) return ctrl;
            var pts = new List<Vector3>();
            for (int i = 0; i < ctrl.Length - 1; i++)
            {
                Vector3 p0 = ctrl[Mathf.Max(i - 1, 0)], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[Mathf.Min(i + 2, ctrl.Length - 1)];
                for (int k = 0; k < perSegment; k++)
                {
                    float t = k / (float)perSegment;
                    pts.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
                }
            }
            pts.Add(ctrl[ctrl.Length - 1]);
            return pts.ToArray();
        }

        // ------------------------------------------------------------------ hair ribbons
        /// <summary>
        /// A flat textured RIBBON along a path - exactly the card a HairSim strand renders, but
        /// static: 2 verts per node, width direction = Cross(tangent, outward-from-centre) so the
        /// flat faces away from the head, mesh.normals = the strand tangent (Kajiya shading in the
        /// HairCard shader), uv = one atlas strip root->tip, uv2.x = root(0)->tip(1). For curls,
        /// static tufts, a bun's coil, a beard's fringe.
        /// </summary>
        public static Mesh Ribbon(Vector3[] path, float width, Vector3 centre, Vector2 stripU, float vRoot, float vTip,
                                  float tipTaper = 0f)
        {
            int n = path.Length;
            var verts = new Vector3[n * 2];
            var norms = new Vector3[n * 2];
            var uv = new Vector2[n * 2];
            var uv2 = new Vector2[n * 2];
            var tris = new int[(n - 1) * 6];
            for (int k = 0; k < n; k++)
            {
                Vector3 pos = path[k];
                Vector3 axis = k < n - 1 ? path[k + 1] - pos : pos - path[k - 1];
                if (axis.sqrMagnitude < 1e-10f) axis = Vector3.up;
                axis.Normalize();
                Vector3 outward = pos - centre;
                if (outward.sqrMagnitude < 1e-8f) outward = Vector3.forward;
                outward.Normalize();
                Vector3 wdir = Vector3.Cross(axis, outward);
                if (wdir.sqrMagnitude < 1e-8f) wdir = Vector3.Cross(axis, Vector3.up);
                wdir.Normalize();
                float u = n > 1 ? k / (float)(n - 1) : 0f;
                float hw = width * 0.5f * Mathf.Lerp(1f, 1f - tipTaper, u);
                verts[k * 2] = pos - wdir * hw;
                verts[k * 2 + 1] = pos + wdir * hw;
                norms[k * 2] = axis; norms[k * 2 + 1] = axis;
                float vv = Mathf.Lerp(vRoot, vTip, u);
                uv[k * 2] = new Vector2(stripU.x, vv); uv[k * 2 + 1] = new Vector2(stripU.y, vv);
                uv2[k * 2] = new Vector2(u, 0f); uv2[k * 2 + 1] = new Vector2(u, 0f);
                if (k < n - 1)
                {
                    int vL = k * 2, vR = vL + 1, nL = vL + 2, nR = vL + 3;
                    int t = k * 6;
                    tris[t] = vL; tris[t + 1] = nL; tris[t + 2] = vR;
                    tris[t + 3] = vR; tris[t + 4] = nL; tris[t + 5] = nR;
                }
            }
            var m = new Mesh { vertices = verts, normals = norms, uv = uv, uv2 = uv2, triangles = tris };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Points along a helix: `turns` revolutions of `radius` about `axis` from
        /// `centre`, advancing `pitch` metres per turn. The first point sits on the ring, not on
        /// the axis, so root the helix by placing `centre` one radius inside the surface.</summary>
        public static Vector3[] Helix(Vector3 centre, Vector3 axis, float radius, float pitch, float turns, int nodes, float phase = 0f)
        {
            axis.Normalize();
            Basis(axis, out Vector3 X, out Vector3 Y);
            var pts = new Vector3[nodes];
            for (int i = 0; i < nodes; i++)
            {
                float t = nodes > 1 ? i / (float)(nodes - 1) : 0f;
                float a = phase + t * turns * Mathf.PI * 2f;
                pts[i] = centre + (X * Mathf.Cos(a) + Y * Mathf.Sin(a)) * radius + axis * (t * turns * pitch);
            }
            return pts;
        }

        // ------------------------------------------------------------------ discs and outlines
        /// <summary>A filled disc at centre, facing along normal.</summary>
        public static Mesh Disc(Vector3 centre, Vector3 normal, float radius, int seg = 24)
        {
            normal.Normalize();
            Basis(normal, out Vector3 X, out Vector3 Y);
            var verts = new Vector3[seg + 1];
            var norms = new Vector3[seg + 1];
            var uvs = new Vector2[seg + 1];
            verts[0] = centre; norms[0] = normal; uvs[0] = new Vector2(0.5f, 0.5f);
            for (int k = 0; k < seg; k++)
            {
                float a = k / (float)seg * Mathf.PI * 2f;
                verts[k + 1] = centre + (X * Mathf.Cos(a) + Y * Mathf.Sin(a)) * radius;
                norms[k + 1] = normal;
                uvs[k + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a));
            }
            var tris = new int[seg * 3];
            for (int k = 0; k < seg; k++)
            {
                tris[k * 3] = 0; tris[k * 3 + 1] = k + 1; tris[k * 3 + 2] = (k + 1) % seg + 1;
            }
            return Finish(verts, norms, uvs, tris);
        }

        /// <summary>
        /// Extrude a closed 2D outline (in the XY plane, counter-clockwise when viewed from +Z... any
        /// order is accepted, it is normalised) to a slab of the given thickness centred on z = 0,
        /// with an optional rounded edge: bevel &gt; 0 insets the faces by that much and adds a 45-degree
        /// chamfer band so the slab reads as a solid object rather than a paper cutout. Concave
        /// outlines are fine (ear clipping). Front face normal is +Z... callers rotate as needed.
        /// </summary>
        public static Mesh Extrude(Vector2[] outline, float thickness, float bevel = 0f, Func<Vector2, float> bulge = null)
        {
            outline = Normalise(outline);
            int n = outline.Length;
            float hz = thickness * 0.5f;
            var parts = new List<Mesh>();
            Vector2[] inner = bevel > 0f ? Inset(outline, bevel) : outline;

            // Front/back faces: use the (possibly inset) outline. bulge lets an ear cup a little.
            parts.Add(Cap(inner, hz, +1f, bulge));
            parts.Add(Cap(inner, -hz, -1f, bulge));
            if (bevel > 0f)
            {
                parts.Add(Band(inner, hz, outline, hz - bevel));           // front chamfer
                parts.Add(Band(outline, hz - bevel, outline, -(hz - bevel))); // side wall
                parts.Add(Band(outline, -(hz - bevel), inner, -hz));        // back chamfer
            }
            else parts.Add(Band(outline, hz, outline, -hz));
            return Combine(parts.ToArray());
        }

        static Mesh Cap(Vector2[] poly, float z, float facing, Func<Vector2, float> bulge)
        {
            int n = poly.Length;
            var verts = new Vector3[n];
            var norms = new Vector3[n];
            var uvs = new Vector2[n];
            Bounds2(poly, out Vector2 lo, out Vector2 hi);
            for (int i = 0; i < n; i++)
            {
                float b = bulge != null ? bulge(poly[i]) : 0f;
                verts[i] = new Vector3(poly[i].x, poly[i].y, z + facing * b);
                norms[i] = new Vector3(0f, 0f, facing);
                uvs[i] = new Vector2(Mathf.InverseLerp(lo.x, hi.x, poly[i].x), Mathf.InverseLerp(lo.y, hi.y, poly[i].y));
            }
            var tris = Triangulate(poly);
            if (facing < 0f) for (int t = 0; t < tris.Length; t += 3) { int tmp = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = tmp; }
            var m = Finish(verts, norms, uvs, tris);
            if (bulge != null) m.RecalculateNormals();
            return m;
        }

        // A quad strip between outline A at height za and outline B at zb (same vertex count).
        static Mesh Band(Vector2[] A, float za, Vector2[] B, float zb)
        {
            int n = A.Length;
            var verts = new Vector3[n * 2 + 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[n * 6];
            for (int i = 0; i <= n; i++)
            {
                int k = i % n;
                verts[i * 2] = new Vector3(A[k].x, A[k].y, za);
                verts[i * 2 + 1] = new Vector3(B[k].x, B[k].y, zb);
                uvs[i * 2] = new Vector2(i / (float)n, 1f);
                uvs[i * 2 + 1] = new Vector2(i / (float)n, 0f);
            }
            for (int i = 0; i < n; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                // Outline is CCW viewed from +Z and A sits at the higher z. For the edge running +Y
                // at x = 1 (outside = +X): Cross(b - a, c - a) = Cross((0,0,zb-za), (0,1,0)) = +X, so
                // (a, b, c) then (b, d, c) face outward.
                tris[i * 6] = a; tris[i * 6 + 1] = b; tris[i * 6 + 2] = c;
                tris[i * 6 + 3] = b; tris[i * 6 + 4] = d; tris[i * 6 + 5] = c;
            }
            var m = new Mesh { vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Ear-clipping triangulation of a simple polygon (CCW). Returns triangle indices.</summary>
        public static int[] Triangulate(Vector2[] poly)
        {
            var idx = new List<int>();
            for (int i = 0; i < poly.Length; i++) idx.Add(i);
            var tris = new List<int>();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                    if (Cross2(b - a, c - a) <= 1e-9f) continue;          // reflex or degenerate
                    bool inside = false;
                    for (int j = 0; j < idx.Count; j++)
                    {
                        int k = idx[j];
                        if (k == i0 || k == i1 || k == i2) continue;
                        if (PointInTri(poly[k], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // malformed polygon; emit what we have
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            // Emitted CCW (a, b, c) viewed from +Z: Cross(b-a, c-a) = +Z, which is front-facing toward +Z.
            return tris.ToArray();
        }

        /// <summary>Points on a rounded-rectangle/ellipse outline: a superellipse with exponent p (2 = ellipse, 4+ = boxy).</summary>
        public static Vector2[] Superellipse(float rx, float ry, float p = 2f, int n = 32, float rot = 0f)
        {
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                float x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / p) * rx;
                float y = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / p) * ry;
                float cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
                pts[i] = new Vector2(x * cr - y * sr, x * sr + y * cr);
            }
            return pts;
        }

        /// <summary>Smoothly resample a coarse outline with Catmull-Rom (closed), n points per edge.</summary>
        public static Vector2[] SmoothOutline(Vector2[] ctrl, int perEdge = 4)
        {
            int n = ctrl.Length;
            var pts = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = ctrl[(i - 1 + n) % n], p1 = ctrl[i], p2 = ctrl[(i + 1) % n], p3 = ctrl[(i + 2) % n];
                for (int k = 0; k < perEdge; k++)
                {
                    float t = k / (float)perEdge;
                    pts.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
                }
            }
            return pts.ToArray();
        }

        // ------------------------------------------------------------------ utilities
        /// <summary>Merge meshes into one (single submesh). Inputs are destroyed.</summary>
        public static Mesh Combine(params Mesh[] parts)
        {
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var tans = new List<Vector4>(); var uv2s = new List<Vector2>(); var cols = new List<Color32>();
            bool anyTan = false, anyUv2 = false, anyCol = false;
            foreach (var m in parts)
            {
                if (m == null) continue;
                int b = verts.Count, vc = m.vertexCount;
                verts.AddRange(m.vertices);
                var n = m.normals; if (n == null || n.Length != vc) { m.RecalculateNormals(); n = m.normals; }
                norms.AddRange(n);
                var uv = m.uv; if (uv == null || uv.Length != vc) uv = new Vector2[vc];
                uvs.AddRange(uv);
                // Optional channels ride along when ANY part carries them (the hair shader reads
                // tangents as the geometric normal, uv2.x as root->tip, colour alpha as fade).
                var ta = m.tangents; if (ta != null && ta.Length == vc) { anyTan = true; tans.AddRange(ta); } else for (int i = 0; i < vc; i++) tans.Add(new Vector4(0f, 0f, 1f, 1f));
                var u2 = m.uv2; if (u2 != null && u2.Length == vc) { anyUv2 = true; uv2s.AddRange(u2); } else for (int i = 0; i < vc; i++) uv2s.Add(Vector2.zero);
                var c = m.colors32; if (c != null && c.Length == vc) { anyCol = true; cols.AddRange(c); } else for (int i = 0; i < vc; i++) cols.Add(new Color32(255, 255, 255, 255));
                var t = m.triangles;
                for (int i = 0; i < t.Length; i++) tris.Add(t[i] + b);
                UnityEngine.Object.Destroy(m);
            }
            var outM = Finish(verts.ToArray(), norms.ToArray(), uvs.ToArray(), tris.ToArray());
            if (anyTan) outM.tangents = tans.ToArray();
            if (anyUv2) outM.uv2 = uv2s.ToArray();
            if (anyCol) outM.colors32 = cols.ToArray();
            return outM;
        }

        /// <summary>Apply a transform to every vertex/normal in place. Returns the same mesh.</summary>
        public static Mesh Transform(Mesh m, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var mat = Matrix4x4.TRS(pos, rot, scale);
            var v = m.vertices; var n = m.normals;
            for (int i = 0; i < v.Length; i++) v[i] = mat.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < n.Length; i++) n[i] = mat.MultiplyVector(n[i]).normalized;
            m.vertices = v; m.normals = n;
            var ta = m.tangents;
            if (ta != null && ta.Length == v.Length)
            {
                for (int i = 0; i < ta.Length; i++) { Vector3 d = mat.MultiplyVector(new Vector3(ta[i].x, ta[i].y, ta[i].z)).normalized; ta[i] = new Vector4(d.x, d.y, d.z, ta[i].w); }
                m.tangents = ta;
            }
            if (scale.x * scale.y * scale.z < 0f) { var t = m.triangles; for (int i = 0; i < t.Length; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; } m.triangles = t; }
            m.RecalculateBounds();
            return m;
        }
        public static Mesh Transform(Mesh m, Vector3 pos, Quaternion rot) => Transform(m, pos, rot, Vector3.one);
        public static Mesh Transform(Mesh m, Vector3 pos) => Transform(m, pos, Quaternion.identity, Vector3.one);

        /// <summary>Unshare every triangle's vertices and recompute normals: faceted shading.</summary>
        public static Mesh Flat(Mesh m)
        {
            var v = m.vertices; var uv = m.uv; var t = m.triangles;
            var nv = new Vector3[t.Length]; var nuv = new Vector2[t.Length]; var nt = new int[t.Length];
            for (int i = 0; i < t.Length; i++) { nv[i] = v[t[i]]; nuv[i] = uv != null && uv.Length == v.Length ? uv[t[i]] : Vector2.zero; nt[i] = i; }
            m.Clear();
            m.vertices = nv; m.uv = nuv; m.triangles = nt;
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh Finish(Vector3[] v, Vector3[] n, Vector2[] uv, int[] t)
        {
            var m = new Mesh();
            if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.normals = n; m.uv = uv; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Orthonormal X, Y with Cross(X, Y) == n.</summary>
        public static void Basis(Vector3 n, out Vector3 X, out Vector3 Y)
        {
            Vector3 a = Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right;
            X = Vector3.Cross(n, a).normalized;
            Y = Vector3.Cross(n, X);
        }

        static float Cross2(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross2(b - a, p - a), d2 = Cross2(c - b, p - b), d3 = Cross2(a - c, p - c);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
        static Vector2[] Normalise(Vector2[] poly)
        {
            float area = 0f;
            for (int i = 0; i < poly.Length; i++) area += Cross2(poly[i], poly[(i + 1) % poly.Length]);
            if (area >= 0f) return poly;
            var r = new Vector2[poly.Length];
            for (int i = 0; i < poly.Length; i++) r[i] = poly[poly.Length - 1 - i];
            return r;
        }
        static void Bounds2(Vector2[] p, out Vector2 lo, out Vector2 hi)
        {
            lo = new Vector2(float.MaxValue, float.MaxValue); hi = new Vector2(float.MinValue, float.MinValue);
            foreach (var q in p) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
        }
        // Inset a CCW polygon by d along each vertex's bisector (fine for the gentle outlines used here).
        static Vector2[] Inset(Vector2[] poly, float d)
        {
            int n = poly.Length;
            var r = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[(i - 1 + n) % n], c = poly[i], q = poly[(i + 1) % n];
                Vector2 e0 = (c - p).normalized, e1 = (q - c).normalized;
                Vector2 n0 = new Vector2(e0.y, -e0.x), n1 = new Vector2(e1.y, -e1.x);   // outward normals for CCW
                Vector2 bis = (n0 + n1);
                float len = bis.magnitude;
                if (len < 1e-4f) { r[i] = c - n0 * d; continue; }
                bis /= len;
                float cosHalf = Vector2.Dot(bis, n0);
                float scale = Mathf.Clamp(1f / Mathf.Max(cosHalf, 0.35f), 1f, 3f);
                r[i] = c - bis * d * scale;
            }
            return r;
        }
    }
}
