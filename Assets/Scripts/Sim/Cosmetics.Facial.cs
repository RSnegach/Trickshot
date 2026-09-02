using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // FACIAL HAIR: everything is a surface that CONFORMS to the head sphere - a pillowed shell a
    // few millimetres proud of the skin, shaped by an outline in (azimuth, latitude) - or a decal
    // painted onto it. Nothing here is a box or a ball any more.
    //
    // THE CHEST SEAM. The head sphere sinks into the torso box (its top crosses the sphere at
    // head-local y = -0.15 x height; the front face is at z = +0.11 x girth), so below a latitude
    // that swings with the build (about -55 deg at az 0 on a default body, -41 at girth 1.2,
    // -81 at girth 0.8) the sphere is INSIDE the jersey and invisible. Every lower outline is
    // clamped to SeamLat(az) and every under-chin volume is kept in FRONT of the chest face.
    public static partial class Cosmetics
    {
        // Measured per build in SCALED head-local metres (see MeasureChest): the chest top plane,
        // the chest front face, the chest half width, and whether the head is bald (sideburns).
        static float _chinFloorY = -0.15f, _chestFrontZ = 0.11f, _chestHalfW = 0.18f;
        static bool _bald;

        static void MeasureChest(ActiveRagdoll rag, Transform head)
        {
            _chinFloorY = -0.15f * rag.HeightScale; _chestFrontZ = 0.11f * rag.GirthScale; _chestHalfW = 0.18f * rag.GirthScale;
            var torso = rag.Phys(Bone.Torso);
            var bc = torso != null ? torso.GetComponent<BoxCollider>() : null;
            if (bc == null) return;
            Vector3 top = head.InverseTransformPoint(torso.TransformPoint(bc.center + new Vector3(0f, bc.size.y * 0.5f, 0f)));
            Vector3 front = head.InverseTransformPoint(torso.TransformPoint(bc.center + new Vector3(0f, 0f, bc.size.z * 0.5f)));
            Vector3 side = head.InverseTransformPoint(torso.TransformPoint(bc.center + new Vector3(bc.size.x * 0.5f, 0f, 0f)));
            _chinFloorY = top.y; _chestFrontZ = front.z; _chestHalfW = Mathf.Abs(side.x);
        }

        /// <summary>Latitude (degrees) at which the head sphere enters the chest box at this azimuth,
        /// in the current build. Outlines stay ABOVE this.</summary>
        static float SeamLat(float azDeg)
        {
            float R = HeadR * _cosScale;
            float latY = _chinFloorY <= -R ? -90f : Mathf.Asin(Mathf.Clamp(_chinFloorY / R, -1f, 1f)) * Mathf.Rad2Deg;
            float cosAz = Mathf.Cos(azDeg * Mathf.Deg2Rad);
            if (cosAz <= 0.01f) return latY;                        // behind the chest face: the top plane binds
            // Is the sphere point at latY in front of the chest face? Then burial starts lower,
            // where the sphere curves back behind the face.
            float zAtLatY = R * Mathf.Cos(latY * Mathf.Deg2Rad) * cosAz;
            if (zAtLatY <= _chestFrontZ) return latY;
            float c = Mathf.Clamp01(_chestFrontZ / (R * cosAz));
            float latZ = -Mathf.Acos(c) * Mathf.Rad2Deg;
            return Mathf.Max(latY, latZ);
        }

        // ---- face-space helpers (girth-1 metres; callers scale) --------------------------------
        static Vector3 FaceDir(float azDeg, float latDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, lat = latDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(lat) * Mathf.Sin(az), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(az));
        }
        static Vector3 OnHead(float azDeg, float latDeg, float h) => FaceDir(azDeg, latDeg) * (HeadR + 0.003f + h);
        static float Pillow(float t) => Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
        static float Smooth(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        // Shared face map so every style agrees where features are (degrees).
        const float MustacheLat = -16f;      // centreline (top -11, bottom -21)
        const float MouthLat = -26f;         // mouth oval centre, 0.06 x 0.02 m
        const float ChinLat = -50f;          // chin point at az 0 (the seam is just below)
        const float EarAz = 84f;

        /// <summary>
        /// A parametric face surface: P(u,v) returns girth-1 head-local metres; the mesh is scaled,
        /// wound outward (checked, re-wound if the parameterisation runs the other way) and gets
        /// smooth analytic normals. Single-sided.
        /// </summary>
        static Mesh FaceShellMesh(int cols, int rows, Func<float, float, Vector3> P, bool wrapU = false)
        {
            var m = MeshGen.Param(P, cols, rows, wrapU: wrapU, wrapV: false, flip: false);
            var v = m.vertices; var n = m.normals;
            int inward = 0;
            for (int i = 0; i < v.Length; i++) if (Vector3.Dot(n[i], v[i]) < 0f) inward++;
            if (inward > v.Length / 2)
            {
                UnityEngine.Object.Destroy(m);
                m = MeshGen.Param(P, cols, rows, wrapU: wrapU, wrapV: false, flip: true);
                v = m.vertices;
            }
            // The facial shells wear the HAIR shader: mesh.normals carry the comb direction (down
            // the face) for the sheen, mesh.tangents the geometric normal for the form shading.
            n = m.normals;
            var comb = new Vector3[v.Length]; var tans = new Vector4[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                v[i] *= _cosScale;
                Vector3 gn = n[i]; if (Vector3.Dot(gn, v[i]) < 0f) gn = -gn;
                tans[i] = new Vector4(gn.x, gn.y, gn.z, 1f);
                Vector3 down = Vector3.down - gn * Vector3.Dot(Vector3.down, gn);
                comb[i] = down.sqrMagnitude > 1e-6f ? down.normalized : Vector3.forward;
            }
            m.vertices = v; m.normals = comb; m.tangents = tans;
            m.RecalculateBounds();
            return m;
        }
        static GameObject FaceShell(Transform head, Material mat, int cols, int rows, Func<float, float, Vector3> P, bool wrapU = false)
            => Piece(head, FaceShellMesh(cols, rows, P, wrapU), mat);

        // ---- mustache ------------------------------------------------------------------------
        /// <summary>A chevron mustache ribbon on the upper lip: two lobes, a philtrum notch, ends
        /// that droop and taper. azHalf = half span in degrees, hMax = relief in metres.</summary>
        static void MustacheShell(Transform head, Material mat, float azHalf, float hMax, bool taperEnds)
        {
            FaceShell(head, mat, 32, 10, (u, v) =>
            {
                float uu = u * 2f - 1f, vv = v * 2f - 1f;          // -1..1 across, top->bottom
                float az = uu * azHalf;
                float a = Mathf.Abs(uu);
                float centre = MustacheLat - 2.4f * Smooth(0.55f, 1f, a);
                float half = taperEnds ? (a < 0.5f ? Mathf.Lerp(3.6f, 4.8f, a / 0.5f) : Mathf.Lerp(4.8f, 0.3f, (a - 0.5f) / 0.5f))
                                       : 4.2f;
                float top = centre + half, bot = centre - half;
                if (a < 0.15f) top -= 0.9f * (1f - a / 0.15f);   // philtrum notch
                float lat = Mathf.Lerp(top, bot, v);
                float endTaper = taperEnds ? 1f - Smooth(0.7f, 1f, a) : 1f;
                float lobe = Mathf.Lerp(0.6f, 1f, Smooth(0f, 0.35f, a));
                float h = hMax * Pillow(vv) * endTaper * lobe;
                return OnHead(az, lat, h);
            });
        }

        // ---- swept tube (handlebar curls, straps) -------------------------------------------
        static void SweepTube(Transform head, Material mat, Vector3[] ctrl, float r0, float r1, int sides = 8, int perSeg = 6)
        {
            var path = MeshGen.Spline(ctrl, perSeg);
            var radii = new float[path.Length];
            for (int i = 0; i < radii.Length; i++) radii[i] = Mathf.Lerp(r0, r1, Mathf.SmoothStep(0f, 1f, i / (float)(radii.Length - 1)));
            var tube = MeshGen.Tube(path, radii, sides, false, true);
            MeshGen.Transform(tube, Vector3.zero, Quaternion.identity, Vector3.one * _cosScale);
            Piece(head, tube, mat);
        }

        // ---- beard annulus -------------------------------------------------------------------
        /// <summary>
        /// One shell around the mouth: 48 spokes from the mouth oval out to a boundary made of the
        /// mustache top, the cheek line, the ear column and the chest seam, with per-sector relief
        /// and an optional under-chin loft kept in front of the chest face.
        /// </summary>
        static void BeardAnnulus(Transform head, Material mat, float cheekTopLatAtEar, float hMustache, float hCheek, float hJaw,
                                 float loftLen, bool softEdge, out List<Vector3> bottomEdge, out List<Vector3> bottomOut)
        {
            Vector3 M = FaceDir(0f, MouthLat);
            Vector3 Up = new Vector3(0f, Mathf.Cos(MouthLat * Mathf.Deg2Rad), -Mathf.Sin(MouthLat * Mathf.Deg2Rad));   // meridian up at M
            Vector3 Side = Vector3.right;
            const int spokes = 48, rings = 8;

            bool Inside(float az, float lat)
            {
                if (Mathf.Abs(az) > 88f) return false;
                // Top edge: mustache top over the lip, then the cheek line rising toward the ear.
                float top = Mathf.Abs(az) <= 23f ? MustacheLat + 5f
                          : Mathf.Abs(az) <= 27f ? Mathf.Lerp(MustacheLat + 5f, -20f, (Mathf.Abs(az) - 23f) / 4f)
                          : Mathf.Lerp(-20f, cheekTopLatAtEar, (Mathf.Abs(az) - 27f) / 61f);
                if (lat > top) return false;
                if (lat < SeamLat(az) + 1f) return false;
                // Mouth slit (a narrow gap, not a hole: on a featureless sphere a big oval reads as a mouth hole).
                float ox = az / 6.5f, oy = (lat - MouthLat) / 1.6f;
                if (ox * ox + oy * oy < 1f) return false;
                return true;
            }
            void ToAzLat(Vector3 d, out float az, out float lat)
            {
                lat = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg;
                az = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            Vector3 SpokeDir(float a, float rhoDeg)
            {
                Vector3 T = Up * Mathf.Cos(a) + Side * Mathf.Sin(a);
                float rho = rhoDeg * Mathf.Deg2Rad;
                return (M * Mathf.Cos(rho) + T * Mathf.Sin(rho)).normalized;
            }
            // Inner radius: the mouth oval; outer radius: march until the region ends.
            var rhoIn = new float[spokes + 1]; var rhoOut = new float[spokes + 1];
            for (int s = 0; s <= spokes; s++)
            {
                float a = s / (float)spokes * Mathf.PI * 2f;
                float ca = Mathf.Cos(a) / 1.6f, sa = Mathf.Sin(a) / 6.5f;
                rhoIn[s] = 1f / Mathf.Sqrt(ca * ca + sa * sa) + 0.6f;
                float r = rhoIn[s];
                while (r < 95f)
                {
                    ToAzLat(SpokeDir(a, r + 0.5f), out float az, out float lat);
                    if (!Inside(az, lat)) break;
                    r += 0.5f;
                }
                rhoOut[s] = Mathf.Max(r, rhoIn[s] + 1f);
            }
            var edge = new List<Vector3>(); var edgeOut = new List<Vector3>();
            Vector3 P(float u, float v)
            {
                int s0 = Mathf.Clamp(Mathf.RoundToInt(u * spokes), 0, spokes);
                float a = u * Mathf.PI * 2f;
                float rho = Mathf.Lerp(rhoIn[s0], rhoOut[s0], v);
                Vector3 d = SpokeDir(a, rho);
                ToAzLat(d, out float az, out float lat);
                // Relief by sector: mustache above the lip, cheeks, jaw/chin.
                float hMax = lat > MouthLat + 4f && Mathf.Abs(az) < 27f ? hMustache
                           : lat < MouthLat - 6f ? hJaw : hCheek;
                float prof = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(v / 0.3f)) * (softEdge ? (1f - Smooth(0.7f, 1f, v)) : (1f - Smooth(0.9f, 1f, v)));
                float h = hMax * prof;
                Vector3 pos = d * (HeadR + 0.003f + h);
                // Under-chin loft: the lowest rings of the downward spokes leave the sphere along a
                // forward-down direction, never behind the chest face.
                float down = Mathf.Clamp01(-Mathf.Cos(a));                 // 1 straight down the chin
                if (loftLen > 0f && down > 0.3f && v > 0.72f)
                {
                    float t = Smooth(0.72f, 1f, v) * Smooth(0.3f, 0.7f, down);
                    pos += new Vector3(0f, -0.5f, 0.85f).normalized * (loftLen * t);
                    float minZ = (_chestFrontZ + 0.006f) / Mathf.Max(_cosScale, 1e-3f);
                    if (pos.y * _cosScale < _chinFloorY && pos.z < minZ) pos.z = minZ;
                }
                return pos;
            }
            var mesh = FaceShellMesh(spokes, rings, P, wrapU: true);
            Piece(head, mesh, mat);
            // Bottom edge samples for a fringe (scaled): the outer ring on the downward spokes.
            for (int s = 0; s <= spokes; s++)
            {
                float a = s / (float)spokes * Mathf.PI * 2f;
                if (-Mathf.Cos(a) < 0.45f) continue;
                Vector3 p = P(s / (float)spokes, 1f);
                edge.Add(p * _cosScale); edgeOut.Add(p.normalized);
            }
            bottomEdge = edge; bottomOut = edgeOut;
        }

        // ---- fringe cards (static hair-card quads hanging off an edge) ----------------------
        static void FringeCards(Transform head, Material tuftMat, List<Vector3> edgePts, List<Vector3> edgeOut,
                                float length, float width, int count, Vector3 hang, float jitter, uint seed = 99)
        {
            if (edgePts == null || edgePts.Count == 0) return;
            var rng = new Lcg(seed);
            var parts = new List<Mesh>();
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                int k = Mathf.Clamp(Mathf.FloorToInt(t * (edgePts.Count - 1)), 0, edgePts.Count - 2);
                float f = t * (edgePts.Count - 1) - k;
                Vector3 root = Vector3.Lerp(edgePts[k], edgePts[k + 1], f);
                Vector3 outw = Vector3.Slerp(edgeOut[k], edgeOut[k + 1], f);
                Vector3 dir = (hang.normalized + outw * 0.35f + new Vector3(rng.Sym(), rng.Sym(), rng.Sym()) * jitter).normalized;
                float len = length * rng.Range(0.55f, 1.15f);
                var path = new Vector3[3];
                path[0] = root - dir * (len * 0.15f);
                path[1] = root + dir * (len * 0.45f);
                path[2] = root + dir * len + Vector3.down * (len * 0.15f);
                Vector2 strip = HairSim.AtlasStripsU[i % 4];
                parts.Add(MeshGen.Ribbon(path, width * _cosScale, Vector3.zero, strip, HairSim.AtlasVRoot, HairSim.AtlasVTip, 0.4f));
            }
            var mesh = MeshGen.Combine(parts.ToArray());
            Piece(head, mesh, tuftMat, castShadows: false);
        }

        // ---- decal grid + painted stubble ---------------------------------------------------
        /// <summary>A plain (az, lat) grid flush on the head; the SHAPE lives in the decal texture.</summary>
        static void HeadDecalGrid(Transform head, Material mat, float azHalf, float latTop, float latBot, int cols, int rows)
        {
            var m = MeshGen.Param((u, v) =>
            {
                float az = Mathf.Lerp(-azHalf, azHalf, u), lat = Mathf.Lerp(latBot, latTop, v);
                return FaceDir(az, lat) * (HeadR + 0.003f) * _cosScale;
            }, cols, rows);
            var verts = m.vertices; var n = new Vector3[verts.Length]; var uv = new Vector2[verts.Length]; var col = new Color32[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                n[i] = verts[i].normalized;
                int ci = i % (cols + 1), rj = i / (cols + 1);
                uv[i] = new Vector2(ci / (float)cols, rj / (float)rows);
                col[i] = new Color32(255, 255, 255, 255);
            }
            m.normals = n; m.uv = uv; m.colors32 = col;
            Piece(head, m, mat, castShadows: false);
        }

        static class FacialDecals
        {
            static Texture2D _stubble;
            /// <summary>Stubble alpha map over az -100..100 (u) and lat -56..+2 (v): the beard
            /// region with a 12 mm feather, the mouth oval cut out, and a speckle so it reads as
            /// clipped hair rather than paint. Static singleton, never destroyed.</summary>
            public static Texture2D Stubble()
            {
                if (_stubble != null) return _stubble;
                const int W = 1024, H = 320;
                const float azHalf = 100f, latTop = 2f, latBot = -56f;
                var px = new byte[W * H];
                var rng = new Lcg(4242);
                // Jittered-grid speckle centres in texel space.
                var specks = new List<Vector3>();
                for (int gy = 0; gy < H; gy += 5)
                for (int gx = 0; gx < W; gx += 5)
                    if (rng.Next() < 0.42f) specks.Add(new Vector3(gx + rng.Next() * 5f, gy + rng.Next() * 5f, rng.Range(1.6f, 2.4f)));
                var speckMap = new float[W * H];
                foreach (var sp in specks)
                {
                    int cx = (int)sp.x, cy = (int)sp.y; float r = sp.z;
                    for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= W || y >= H) continue;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float v = Mathf.Clamp01(1f - (d - r + 0.9f) / 0.9f);
                        if (v > speckMap[y * W + x]) speckMap[y * W + x] = v;
                    }
                }
                float Top(float az)
                {
                    float a = Mathf.Abs(az);
                    return a <= 23f ? MustacheLat + 5f : Mathf.Lerp(MustacheLat + 5f, -8f, Mathf.Clamp01((a - 23f) / 65f));
                }
                for (int y = 0; y < H; y++)
                {
                    float lat = Mathf.Lerp(latBot, latTop, y / (float)(H - 1));
                    for (int x = 0; x < W; x++)
                    {
                        float az = Mathf.Lerp(-azHalf, azHalf, x / (float)(W - 1));
                        // Signed distance (deg) to the region edge: positive inside.
                        float dTop = Top(az) - lat;
                        float dEar = 88f - Mathf.Abs(az);
                        float ox = az / 6.5f, oy = (lat - MouthLat) / 1.6f;
                        float dMouth = (Mathf.Sqrt(ox * ox + oy * oy) - 1f) * 1.6f;   // ~deg outside the slit
                        float dist = Mathf.Min(Mathf.Min(dTop, dEar * 0.6f), dMouth * 2f);
                        float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dist / 9f));   // ~3 cm feather
                        float v = a * (0.30f + 0.25f * speckMap[y * W + x]) / 0.55f;
                        px[y * W + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
                    }
                }
                _stubble = new Texture2D(W, H, TextureFormat.R8, true) { name = "StubbleDecal", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _stubble.SetPixelData(px, 0);
                _stubble.Apply(true, true);
                return _stubble;
            }
        }

        // ---- sideburns and chinstrap ---------------------------------------------------------
        static void SideburnShell(Transform head, Material mat, float side, float topLat)
        {
            FaceShell(head, mat, 8, 16, (u, v) =>
            {
                float uu = u * 2f - 1f, vv = v * 2f - 1f;
                float bottom = -26f - 4f * (side * uu > 0f ? 0f : 1f) * 0.0f;   // (slant below)
                float halfDeg = Mathf.Lerp(4.2f, 5.4f, v);
                float az = side * EarAz + uu * halfDeg;
                // Front edge (toward the face) 4 mm lower than the back edge: a forward slant.
                float slant = 1.2f * (side * uu < 0f ? 1f : 0f) * Mathf.Abs(uu);
                float lat = Mathf.Lerp(topLat, -26f - slant, v);
                // Rounded-rectangle profile: distance to the outline in (u,v) with 6 mm corners.
                float cornerU = 6f / (halfDeg * HeadR * Mathf.Deg2Rad * 1000f);   // 6 mm as a fraction of half-width
                float eu = Mathf.Max(0f, Mathf.Abs(uu) - (1f - cornerU)), ev = Mathf.Max(0f, Mathf.Abs(vv) - 0.85f);
                float d = Mathf.Sqrt(eu * eu / (cornerU * cornerU) + ev * ev / (0.15f * 0.15f));
                float h = 0.004f * Pillow(Mathf.Min(1f, Mathf.Max(Mathf.Abs(uu), d)));
                return OnHead(az, lat, h);
            });
        }

        /// <summary>A pillowed strip riding a spline of (az, lat) waypoints, edges flat on the head.</summary>
        static void HeadSplineStrip(Transform head, Material mat, Vector2[] azLat, float width, float hMax, int cols = 5, int rows = 48)
        {
            var dirs = new Vector3[azLat.Length];
            for (int i = 0; i < azLat.Length; i++) dirs[i] = FaceDir(azLat[i].x, Mathf.Max(azLat[i].y, SeamLat(azLat[i].x) + 1f));
            var path = MeshGen.Spline(dirs, 8);
            for (int i = 0; i < path.Length; i++) path[i].Normalize();
            FaceShell(head, mat, cols, rows, (u, v) =>
            {
                float t = v * (path.Length - 1);
                int k = Mathf.Clamp(Mathf.FloorToInt(t), 0, path.Length - 2);
                Vector3 d = Vector3.Slerp(path[k], path[k + 1], t - k).normalized;
                Vector3 tan = (path[Mathf.Min(k + 1, path.Length - 1)] - path[Mathf.Max(k - 1, 0)]).normalized;
                Vector3 edge = Vector3.Cross(tan, d).normalized;
                float uu = u * 2f - 1f;
                float ends = Smooth(0f, 0.08f, v) * Smooth(0f, 0.08f, 1f - v);   // rounded ends
                float h = hMax * Pillow(uu) * ends;
                float w = width * 0.5f * Mathf.Lerp(0.3f, 1f, ends);
                Vector3 pos = d * (HeadR + 0.003f + h) + edge * (uu * w);
                return pos;
            });
        }

        // ---- the catalog (index 0 = Clean; order is wire state) ----------------------------
        static readonly List<FacialEntry> _facial = new List<FacialEntry>
        {
            new FacialEntry { Name = "Clean", Build = (h, m) => { } },
            new FacialEntry { Name = "Mustache", Build = (h, m) => MustacheShell(h, m, 23f, 0.007f, true) },
            new FacialEntry { Name = "Handlebar", Build = (h, m) =>
            {
                MustacheShell(h, m, 25f, 0.009f, false);
                for (int side = -1; side <= 1; side += 2)
                {
                    var ctrl = new[]
                    {
                        OnHead(side * 21f, -18f, 0.006f), OnHead(side * 28f, -19.5f, 0.008f),
                        OnHead(side * 33f, -14f, 0.011f), OnHead(side * 34f, -8f, 0.012f),
                    };
                    SweepTube(h, m, ctrl, 0.006f, 0.002f);
                }
            } },
            new FacialEntry { Name = "Goatee", Build = (h, m) =>
            {
                MustacheShell(h, m, 20f, 0.007f, true);
                // Chin shell: a tapered patch under the mouth to the chin point, lofting forward off
                // the sphere at the point so it rests in front of the chest.
                FaceShell(h, m, 12, 12, (u, v) =>
                {
                    float uu = u * 2f - 1f;
                    float lat = Mathf.Lerp(-31f, ChinLat, v);
                    lat = Mathf.Max(lat, SeamLat(0f) + 1f);
                    float halfW = Mathf.Lerp(0.0175f, 0.035f, Mathf.SmoothStep(0f, 1f, v)) * (1f - 0.9f * Smooth(0.85f, 1f, v));
                    float az = uu * (halfW / (HeadR * Mathf.Deg2Rad));
                    float hh = 0.009f * Pillow(uu) * Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI * 0.9f + 0.1f);
                    Vector3 pos = OnHead(az, lat, hh);
                    float loft = Smooth(0.8f, 1f, v);
                    pos += new Vector3(0f, -0.6f, 0.8f) * (0.015f * loft);
                    return pos;
                });
                // Two thin strips from the mustache ends down to the chin shell, closing the ring.
                for (int side = -1; side <= 1; side += 2)
                {
                    int sd = side;
                    FaceShell(h, m, 3, 12, (u, v) =>
                    {
                        float uu = u * 2f - 1f;
                        float az = Mathf.Lerp(sd * 20f, sd * 12f, v) + uu * 1.4f;
                        float lat = Mathf.Lerp(-20f, -40f, v);
                        return OnHead(az, lat, 0.003f * Pillow(uu));
                    });
                }
            } },
            new FacialEntry { Name = "Stubble",
                Mat = (facial, skin) => Make.Decal(Color.Lerp(facial, skin, 0.4f), FacialDecals.Stubble()),
                Build = (h, m) => HeadDecalGrid(h, m, 100f, 2f, -56f, 40, 16) },
            new FacialEntry { Name = "Short Beard", Build = (h, m) =>
                BeardAnnulus(h, m, -14f, 0.008f, 0.005f, 0.010f, 0.012f, false, out _, out _) },
            new FacialEntry { Name = "Full Beard", Build = (h, m) =>
            {
                // Deep relief and a long under-chin loft: this beard stands well off the face and
                // hangs, with a longer fringe of cards below it.
                BeardAnnulus(h, m, -5f, 0.010f, 0.014f, 0.034f, 0.062f, true, out var edge, out var outw);
                _fringeEdge = edge; _fringeOut = outw;
            }, Fringe = (h, tm) => FringeCards(h, tm, _fringeEdge, _fringeOut, 0.11f, 0.035f, 36, new Vector3(0f, -1f, 0.25f), 0.2f) },
            new FacialEntry { Name = "Sideburns", Build = (h, m) =>
            {
                float top = _bald ? 6f : 10f;
                SideburnShell(h, m, -1f, top);
                SideburnShell(h, m, 1f, top);
            } },
            new FacialEntry { Name = "Chinstrap", Build = (h, m) =>
            {
                var pts = new[]
                {
                    new Vector2(-EarAz, 8f), new Vector2(-86f, -20f), new Vector2(-70f, -38f), new Vector2(-40f, -45f),
                    new Vector2(0f, -49f),
                    new Vector2(40f, -45f), new Vector2(70f, -38f), new Vector2(86f, -20f), new Vector2(EarAz, 8f),
                };
                HeadSplineStrip(h, m, pts, 0.020f, 0.003f, 5, 64);
                // Soul patch.
                FaceShell(h, m, 8, 6, (u, v) =>
                {
                    float uu = u * 2f - 1f, vv = v * 2f - 1f;
                    float d = Mathf.Sqrt(uu * uu + vv * vv);
                    return OnHead(uu * 3f, -33f + vv * 2.2f, 0.003f * Pillow(Mathf.Min(1f, d)));
                });
            } },
        };
        // Scratch handed from a beard's Build to its Fringe delegate (builds are sequential).
        static List<Vector3> _fringeEdge, _fringeOut;
    }
}
