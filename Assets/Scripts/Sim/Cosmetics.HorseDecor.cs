using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // HORSE MARKINGS AND TACK. Markings are DECALS painted onto the skull sphere, the muzzle box
    // and the leg capsules (a star, a blaze, a snip, stockings, dapples, and the nostrils every
    // horse has); tack is straps that follow the real geometry - rings around the box muzzle,
    // strips over the skull, a sleeve around the neck capsule, a lofted saddle pad around the
    // barrel. Nothing here is a box piercing another box any more, and nothing is a collider.
    //
    // Sizes come from the BUILT decor pieces (ActiveRagdoll.TryGetDecor) so every strap stays
    // wrapped as the height and weight sliders move.
    public static partial class Cosmetics
    {
        // ---- painted decal masks (static, cached) -------------------------------------------
        static class HorseDecals
        {
            // 4 x 2 cells of 64 x 128 in a 256 x 256 mask atlas (r = opacity).
            public const int Star = 0, Snip = 1, Nostril = 2, Dapple = 3, Blaze = 4, SockEdge = 5, Solid = 6;
            static Texture2D _atlas, _leather, _webbing, _quilt;
            public static Rect Cell(int c) => new Rect((c % 4) * 0.25f + 0.01f, (c / 4) * 0.5f + 0.01f, 0.23f, 0.48f);
            static float Wobble(float ang, float seed) => 0.82f + 0.18f * Mathf.PerlinNoise(seed + Mathf.Cos(ang) * 1.7f + 3f, seed + Mathf.Sin(ang) * 1.7f + 5f);
            public static Texture2D Atlas
            {
                get
                {
                    if (_atlas != null) return _atlas;
                    const int W = 256, H = 256;
                    var px = new Color32[W * H];
                    for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int cell = (x / 64) + (y / 128) * 4;
                        float u = (x % 64 + 0.5f) / 64f, v = (y % 128 + 0.5f) / 128f;   // 0..1 in the cell
                        float cx = (u - 0.5f) * 2f, cy = (v - 0.5f) * 2f;               // -1..1
                        float ang = Mathf.Atan2(cy, cx), r = Mathf.Sqrt(cx * cx + cy * cy);
                        float a = 0f;
                        switch (cell)
                        {
                            case Star: a = Smooth(0.78f * Wobble(ang, 1f), 0.62f * Wobble(ang, 1f), r); break;
                            case Snip: a = Smooth(0.75f * Wobble(ang, 7f), 0.55f * Wobble(ang, 7f), r); break;
                            case Nostril: { float e = Mathf.Sqrt(cx * cx / 0.55f + cy * cy / 0.9f); a = Smooth(0.9f, 0.7f, e); break; }
                            case Dapple: { a = Smooth(0.95f, 0.55f, r * Wobble(ang, 4f) / 0.9f) * (0.62f + 0.38f * Smooth(0.15f, 0.6f, r)); break; }   // a soft blotch, brighter at the rim
                            case Blaze:
                            {
                                // A strip up the cell (v = along the face), width tapering; wavy edges.
                                float half = Mathf.Lerp(0.58f, 0.40f, v) * (0.9f + 0.1f * Mathf.PerlinNoise(v * 6f, 2f));
                                a = Smooth(half, half - 0.15f, Mathf.Abs(cx));
                                break;
                            }
                            case SockEdge:
                            {
                                // Opaque below a ragged diagonal top edge (v up the leg).
                                float edge = 0.70f + 0.12f * Mathf.PerlinNoise(u * 5f, 9f) + 0.06f * Mathf.Sin(u * 18f);
                                a = 1f - Smooth(edge - 0.06f, edge + 0.04f, v);
                                break;
                            }
                            case Solid: a = 1f; break;
                        }
                        byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                        px[y * W + x] = new Color32(b, b, b, 255);
                    }
                    _atlas = new Texture2D(W, H, TextureFormat.RGB24, true) { name = "HorseDecals", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    _atlas.SetPixels32(px); _atlas.Apply(true, true);
                    return _atlas;
                }
            }
            static Texture2D Tile(string name, int w, int h, Func<float, float, float> lum, ref Texture2D slot)
            {
                if (slot != null) return slot;
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(lum((x + 0.5f) / w, (y + 0.5f) / h)) * 255f); px[y * w + x] = new Color32(b, b, b, 255); }
                slot = new Texture2D(w, h, TextureFormat.RGB24, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                slot.SetPixels32(px); slot.Apply(true, true);
                return slot;
            }
            public static Texture2D Leather => Tile("Leather", 64, 16, (u, v) => (v < 0.12f || v > 0.88f) && Mathf.Repeat(u * 8f, 1f) < 0.55f ? 0.55f : 0.92f - 0.06f * Mathf.PerlinNoise(u * 20f, v * 6f), ref _leather);
            public static Texture2D Webbing => Tile("Webbing", 64, 16, (u, v) => 0.85f + 0.15f * (Mathf.Abs(Mathf.Sin(u * 40f + v * 12f)) > 0.6f ? 1f : 0f) - 0.12f, ref _webbing);
            public static Texture2D Quilt => Tile("Quilt", 64, 64, (u, v) => Mathf.Abs(Mathf.Repeat(u + v, 1f) - 0.5f) < 0.03f || Mathf.Abs(Mathf.Repeat(u - v, 1f) - 0.5f) < 0.03f ? 0.72f : 0.95f, ref _quilt);
        }

        // ---- decal surface helpers (all collider-less Pieces) --------------------------------
        static Mesh UvRect(Mesh m, Rect r)
        {
            var uv = m.uv;
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(r.x + uv[i].x * r.width, r.y + uv[i].y * r.height);
            m.uv = uv;
            return m;
        }
        /// <summary>A small patch flush on a sphere of radius R about the parent origin.</summary>
        static void SpherePatchH(Transform parent, Material mat, float R, float proud, Vector3 dir, float halfW, float halfH, Rect uv)
        {
            dir.Normalize();
            Vector3 side = Vector3.Cross(dir, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.forward);
            side.Normalize(); Vector3 up2 = Vector3.Cross(side, dir).normalized;
            var m = MeshGen.Param((u, v) =>
            {
                Vector3 d = Quaternion.AngleAxis((u * 2f - 1f) * halfW * Mathf.Rad2Deg, up2) * dir;
                Vector3 s2 = Quaternion.AngleAxis((u * 2f - 1f) * halfW * Mathf.Rad2Deg, up2) * side;
                d = Quaternion.AngleAxis(-(v * 2f - 1f) * halfH * Mathf.Rad2Deg, s2) * d;
                return d.normalized * (R + proud);
            }, 12, 10);
            { var vv = m.vertices; var nn = new Vector3[vv.Length]; for (int i = 0; i < vv.Length; i++) nn[i] = vv[i].normalized; m.normals = nn; }
            Piece(parent, UvRect(m, uv), mat, castShadows: false);
        }
        /// <summary>A strip on a sphere from one direction to another, sagging by `bow` toward down.</summary>
        static void SphereLineH(Transform parent, Material mat, float R, float proud, Vector3 from, Vector3 to, float width, float bow, Rect uv, int seg = 16, bool alongV = false)
        {
            var dirs = PathDirs(from, to, bow, seg + 1);
            var m = MeshGen.Param((u, v) =>
            {
                float t = u * seg; int k = Mathf.Clamp(Mathf.FloorToInt(t), 0, seg - 1); float f = t - k;
                Vector3 d = Vector3.Slerp(dirs[k], dirs[k + 1], f).normalized;
                Vector3 tan = (dirs[Mathf.Min(k + 1, seg)] - dirs[Mathf.Max(k - 1, 0)]).normalized;
                Vector3 edge = Vector3.Cross(tan, d).normalized;
                return d * (R + proud) + edge * ((v - 0.5f) * width);
            }, seg, 1);
            { var vv = m.vertices; var nn = new Vector3[vv.Length]; for (int i = 0; i < vv.Length; i++) nn[i] = vv[i].normalized; m.normals = nn; }
            if (alongV) { var uv2 = m.uv; for (int i = 0; i < uv2.Length; i++) uv2[i] = new Vector2(uv2[i].y, uv2[i].x); m.uv = uv2; }
            Piece(parent, UvRect(m, uv), mat, castShadows: false);
        }
        /// <summary>A flat quad `proud` above one face of a box (box-local frame). faceAxis is a
        /// signed unit axis; centre/size are in the face's own 2D coordinates.</summary>
        static void FaceDecal(Transform box, Vector3 dims, Vector3 faceAxis, Vector2 centre, Vector2 size, float proud, Rect uv)
        {
            Vector3 n = faceAxis.normalized;
            Vector3 ax = Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized;
            Vector3 ay = Vector3.Cross(n, ax).normalized;
            float half = Mathf.Abs(Vector3.Dot(dims, n)) * 0.5f;
            var m = MeshGen.Param((u, v) => n * (half + proud) + ax * (centre.x + (u - 0.5f) * size.x) + ay * (centre.y + (v - 0.5f) * size.y), 4, 4);
            { var vv = m.vertices; var nn = new Vector3[vv.Length]; for (int i = 0; i < vv.Length; i++) nn[i] = n; m.normals = nn; }
            // Wind toward the face normal.
            { var t = m.triangles; var vv = m.vertices; Vector3 c = Vector3.Cross(vv[t[1]] - vv[t[0]], vv[t[2]] - vv[t[0]]); if (Vector3.Dot(c, n) < 0f) { for (int i = 0; i < t.Length; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; } m.triangles = t; } }
            Piece(box, UvRect(m, uv), _decalMat, castShadows: false);
        }
        static Material _decalMat;   // scratch: the material FaceDecal/BoxRing emit with (set by the caller)

        /// <summary>A rounded-rectangle ribbon around a box's local XZ cross-section at local y.</summary>
        static void BoxRing(Transform box, Vector3 dims, float y, float width, float proud, float cornerR, Material mat, float vRepeat = 1f)
        {
            float hx = dims.x * 0.5f + proud, hz = dims.z * 0.5f + proud;
            var corners = new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
            var outline = RoundedPoly(corners, new[] { cornerR }, 4);
            int n = outline.Length;
            var m = MeshGen.Param((u, v) =>
            {
                int i0 = Wrap(Mathf.FloorToInt(u * n), n), i1 = Wrap(i0 + 1, n); float f = u * n - Mathf.Floor(u * n);
                Vector2 o = Vector2.Lerp(outline[i0], outline[i1], f);
                return new Vector3(o.x, y + (v - 0.5f) * width, o.y);
            }, n, 1, wrapU: true, uvRepeatU: vRepeat);
            { var vv = m.vertices; var nn = new Vector3[vv.Length]; for (int i = 0; i < vv.Length; i++) nn[i] = new Vector3(vv[i].x, 0f, vv[i].z).normalized; m.normals = nn; }
            { var t = m.triangles; var vv = m.vertices; Vector3 c = Vector3.Cross(vv[t[1]] - vv[t[0]], vv[t[2]] - vv[t[0]]); if (Vector3.Dot(c, new Vector3(vv[t[0]].x, 0f, vv[t[0]].z)) < 0f) { for (int i = 0; i < t.Length; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; } m.triangles = t; } }
            Piece(box, m, mat);
        }
        /// <summary>A sleeve hugging a capsule (radius r, full length len, axis local Y) between two local heights.</summary>
        static void CapsuleSleeve(Transform bone, float r, float len, float yFrom, float yTo, float proud, Material mat, Rect uv, int seg = 20)
        {
            float half = len * 0.5f - r;
            float Radius(float y)
            {
                float dy = Mathf.Abs(y) - half;
                return dy <= 0f ? r : Mathf.Sqrt(Mathf.Max(r * r - dy * dy, 1e-6f));
            }
            var m = MeshGen.Param((u, v) =>
            {
                float y = Mathf.Lerp(yFrom, yTo, v);
                float rr = Radius(y) + proud;
                float th = u * Mathf.PI * 2f;
                return new Vector3(Mathf.Sin(th) * rr, y, Mathf.Cos(th) * rr);
            }, seg, 6, wrapU: true);
            { var t = m.triangles; var vv = m.vertices; Vector3 c = Vector3.Cross(vv[t[1]] - vv[t[0]], vv[t[2]] - vv[t[0]]); if (Vector3.Dot(c, new Vector3(vv[t[0]].x, 0f, vv[t[0]].z)) < 0f) { for (int i = 0; i < t.Length; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; } m.triangles = t; } m.RecalculateNormals(); }
            Piece(bone, UvRect(m, uv), mat, castShadows: false);
        }
        /// <summary>Round decal dots scattered over a capsule's side (theta band), tangent to it.</summary>
        static void CapsuleDots(Transform bone, float r, float len, float yFrom, float yTo, float thetaCentre, float thetaHalf, int count, float dotR, float proud, Material mat, Rect uv, uint seed)
        {
            var rng = new Lcg(seed);
            var parts = new List<Mesh>();
            float half = len * 0.5f - r;
            for (int i = 0; i < count; i++)
            {
                float y = rng.Range(yFrom, yTo);
                float th = thetaCentre + rng.Sym() * thetaHalf;
                float dy = Mathf.Abs(y) - half;
                float rr = dy <= 0f ? r : Mathf.Sqrt(Mathf.Max(r * r - dy * dy, 1e-6f));
                Vector3 radial = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                Vector3 nrm = dy <= 0f ? radial : (radial * rr + new Vector3(0f, Mathf.Sign(y) * dy, 0f)).normalized;
                Vector3 c = radial * rr + new Vector3(0f, y, 0f) + nrm * proud;
                var disc = MeshGen.Disc(Vector3.zero, Vector3.up, dotR * rng.Range(0.8f, 1.2f), 12);
                MeshGen.Transform(disc, c, Quaternion.FromToRotation(Vector3.up, nrm));
                parts.Add(UvRect(disc, uv));
            }
            Piece(bone, MeshGen.Combine(parts.ToArray()), mat, castShadows: false);
        }
        static void BoxDots(Transform box, Vector3 dims, Vector3[] faces, int count, float dotR, float proud, Material mat, Rect uv, uint seed)
        {
            var rng = new Lcg(seed);
            var parts = new List<Mesh>();
            for (int i = 0; i < count; i++)
            {
                Vector3 n = faces[i % faces.Length].normalized;
                Vector3 ax = Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized;
                Vector3 ay = Vector3.Cross(n, ax).normalized;
                float hx = Mathf.Abs(Vector3.Dot(dims, ax)) * 0.5f, hy = Mathf.Abs(Vector3.Dot(dims, ay)) * 0.5f, hn = Mathf.Abs(Vector3.Dot(dims, n)) * 0.5f;
                Vector3 c = n * (hn + proud) + ax * (rng.Sym() * (hx - dotR)) + ay * (rng.Sym() * (hy - dotR));
                var disc = MeshGen.Disc(Vector3.zero, Vector3.up, dotR * rng.Range(0.8f, 1.2f), 12);
                MeshGen.Transform(disc, c, Quaternion.FromToRotation(Vector3.up, n));
                parts.Add(UvRect(disc, uv));
            }
            Piece(box, MeshGen.Combine(parts.ToArray()), mat, castShadows: false);
        }
        static void TorusAt(Transform parent, Material mat, Vector3 centre, Vector3 axis, float R, float r)
        {
            var t = MeshGen.Torus(R, r, 20, 8);
            MeshGen.Transform(t, centre, Quaternion.FromToRotation(Vector3.up, axis));
            Piece(parent, t, mat);
        }

        // ---- the horse's marking and tack pass ----------------------------------------------
        static void AttachHorseDecor(ActiveRagdoll rag, PlayerAppearance a)
        {
            var head = rag.Phys(Bone.Head);
            if (head == null) return;
            float g = rag.GirthScale, h = rag.HeightScale, R = rag.HeadVisualRadius;
            bool haveMuzzle = rag.TryGetDecor("D_Muzzle", out var muzzle, out var mdims);
            bool haveNeck = rag.TryGetDecor("D_Neck", out var neck, out var ndims);

            // Markings material: the decal shader, tinted with the markings colour.
            var markMat = Own(Make.Decal(new Color(a.FacialColor.r, a.FacialColor.g, a.FacialColor.b, 1f), HorseDecals.Atlas));
            var nostrilMat = Own(Make.Decal(new Color(0.12f, 0.10f, 0.09f, 1f), HorseDecals.Atlas));

            // Nostrils: anatomy, every horse. Two dark ellipses on the nose end face (muzzle local
            // +Y = along the jaw toward the nose; the end face is +Y, its 2D axes are X and Z).
            if (haveMuzzle)
            {
                _decalMat = nostrilMat;
                for (int side = -1; side <= 1; side += 2)
                    FaceDecal(muzzle, mdims, Vector3.up, new Vector2(side * 0.036f * g, -0.02f * g), new Vector2(0.020f * g, 0.026f * g), 0.003f, HorseDecals.Cell(HorseDecals.Nostril));
            }

            switch (a.FacialStyle)
            {
                case 1: // Star: a forehead patch on the skull.
                    SpherePatchH(head, markMat, R, 0.003f, new Vector3(0f, 0.50f, 0.87f), 0.30f, 0.27f, HorseDecals.Cell(HorseDecals.Star));
                    break;
                case 2: // Blaze: a strip from the forehead down the skull and along the muzzle top to the nose.
                    SphereLineH(head, markMat, R, 0.003f, new Vector3(0f, 0.62f, 0.78f), new Vector3(0f, 0.06f, 1.0f), 0.10f * g, 0f, HorseDecals.Cell(HorseDecals.Blaze), 16, alongV: true);
                    if (haveMuzzle)
                    {
                        _decalMat = markMat;
                        // Muzzle local -Z is the TOP face; its 2D axes are X (across) and Y (along the jaw).
                        FaceDecal(muzzle, mdims, Vector3.back, new Vector2(0f, 0f), new Vector2(0.09f * g, mdims.y), 0.003f, HorseDecals.Cell(HorseDecals.Blaze));
                        // Nose end: the strip continues from the top edge down to between the nostrils.
                        FaceDecal(muzzle, mdims, Vector3.up, new Vector2(0f, mdims.z * 0.5f - 0.0275f * g), new Vector2(0.05f * g, -0.055f * g), 0.003f, HorseDecals.Cell(HorseDecals.Blaze));
                    }
                    break;
                case 3: // Snip: a blob on the nose end.
                    if (haveMuzzle) { _decalMat = markMat; FaceDecal(muzzle, mdims, Vector3.up, new Vector2(0f, 0.012f * g), new Vector2(0.066f * g, 0.06f * g), 0.003f, HorseDecals.Cell(HorseDecals.Snip)); }
                    break;
                case 4: // Stockings: sleeves with a ragged top on all four legs.
                {
                    var socks = HorseDecals.Cell(HorseDecals.SockEdge);
                    foreach (var b in new[] { Bone.CalfL, Bone.CalfR })
                    {
                        var t = rag.Phys(b); if (t == null) continue;
                        CapsuleSleeve(t, 0.075f * g, 0.38f * h, -0.19f * h, 0.03f * h, 0.004f, markMat, socks);
                    }
                    foreach (var b in new[] { Bone.ForearmL, Bone.ForearmR })
                    {
                        var t = rag.Phys(b); if (t == null) continue;
                        CapsuleSleeve(t, 0.088f * g, 0.40f * h, -0.20f * h, 0.13f * h, 0.004f, markMat, socks);
                    }
                    break;
                }
                case 5: // Dappled: soft rings over the neck, rump and upper legs.
                {
                    // Dapples are SUBTLE: a lighter shade of the coat, not the marking colour itself.
                    var dc = Color.Lerp(a.Skin, a.FacialColor, 0.38f); dc.a = 0.8f;
                    var dapMat = Own(Make.Decal(dc, HorseDecals.Atlas));
                    var cell = HorseDecals.Cell(HorseDecals.Dapple);
                    if (haveNeck) CapsuleDots(neck, ndims.x, ndims.y, -0.20f * h, 0.02f * h, 0f, 2.1f, 26, 0.028f * g, 0.003f, dapMat, cell, 21);
                    var pelvis = rag.Phys(Bone.Pelvis);
                    if (pelvis != null) BoxDots(pelvis, new Vector3(0.30f * g, 0.30f * h, 0.32f * g), new[] { Vector3.right, Vector3.left, Vector3.back, Vector3.up }, 20, 0.03f * g, 0.003f, dapMat, cell, 22);
                    foreach (var b in new[] { Bone.ThighL, Bone.ThighR })
                    { var t = rag.Phys(b); if (t != null) CapsuleDots(t, 0.095f * g, 0.40f * h, -0.05f * h, 0.18f * h, 0f, Mathf.PI, 8, 0.024f * g, 0.003f, dapMat, cell, 23); }
                    foreach (var b in new[] { Bone.UpperArmL, Bone.UpperArmR })
                    { var t = rag.Phys(b); if (t != null) CapsuleDots(t, 0.105f * g, 0.44f * h, -0.05f * h, 0.18f * h, 0f, Mathf.PI, 8, 0.024f * g, 0.003f, dapMat, cell, 24); }
                    break;
                }
            }

            // ---- tack --------------------------------------------------------------------
            if (a.Accessory <= 0) return;
            var leather = Own(Make.MatTexTint(HorseDecals.Leather, a.AccessoryColor, 0.35f));
            var webbing = Own(Make.MatTexTint(HorseDecals.Webbing, a.AccessoryColor, 0.08f));
            var metal = Gunmetal();
            float tipY = haveMuzzle ? mdims.y * 0.5f : 0.13f * h;

            // Throatlatch ring height on the neck: the largest local y (<= +0.06h) whose ring clears the skull by 8 mm.
            float ThroatY()
            {
                if (!haveNeck) return 0f;
                float r = ndims.x, best = -0.1f * h;
                for (float y = 0.06f * h; y > -0.2f * h; y -= 0.005f)
                {
                    Vector3 axisPt = head.InverseTransformPoint(neck.TransformPoint(new Vector3(0f, y, 0f)));
                    float dist = Mathf.Sqrt(axisPt.sqrMagnitude + (r + 0.004f) * (r + 0.004f));
                    if (dist > R + 0.008f) { best = y; break; }
                }
                return best;
            }
            void CheekAndCrown(Material mat, float width, float exitY, float exitZ)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 exitDir = new Vector3(side * 0.38f, -0.13f, 0.91f).normalized;
                    Vector3 temple = new Vector3(side * 0.85f, 0.45f, -0.25f).normalized;
                    SphereLineH(head, mat, R, 0.005f, exitDir, temple, width, 0f, new Rect(0f, 0f, 1f, 1f));
                }
                Vector3 tl = new Vector3(-0.85f, 0.45f, -0.25f).normalized, tr = new Vector3(0.85f, 0.45f, -0.25f).normalized;
                Vector3 brow = new Vector3(0f, 0.40f, 0.92f).normalized, poll = new Vector3(0f, 0.95f, -0.30f).normalized;
                SphereLineH(head, mat, R, 0.005f, tl, brow, width, 0f, new Rect(0f, 0f, 1f, 1f));
                SphereLineH(head, mat, R, 0.005f, brow, tr, width, 0f, new Rect(0f, 0f, 1f, 1f));
                SphereLineH(head, mat, R, 0.005f, tl, poll, width, 0f, new Rect(0f, 0f, 1f, 1f));
                SphereLineH(head, mat, R, 0.005f, poll, tr, width, 0f, new Rect(0f, 0f, 1f, 1f));
            }
            switch (a.Accessory)
            {
                case 1: // Bridle: thin noseband, bit rings, cheekpieces, browband, crownpiece, throatlatch.
                    if (haveMuzzle)
                    {
                        BoxRing(muzzle, mdims, tipY - 0.06f * h, 0.025f, 0.005f, 0.01f, leather, 6f);
                        for (int side = -1; side <= 1; side += 2)
                            TorusAt(muzzle, metal, new Vector3(side * (mdims.x * 0.5f + 0.006f), tipY - 0.05f * h, 0.033f * g), Vector3.right, 0.02f, 0.004f);
                    }
                    CheekAndCrown(leather, 0.022f, 0f, 0f);
                    if (haveNeck) { float ty = ThroatY(); CapsuleSleeve(neck, ndims.x, ndims.y, ty - 0.01f, ty + 0.01f, 0.004f, leather, new Rect(0f, 0f, 6f, 1f)); }
                    break;
                case 2: // Halter: broad webbing, chin ring, cheek rings, no browband or bit.
                    if (haveMuzzle)
                    {
                        BoxRing(muzzle, mdims, tipY - 0.10f * h, 0.035f, 0.005f, 0.012f, webbing, 6f);
                        TorusAt(muzzle, metal, new Vector3(0f, tipY - 0.10f * h, mdims.z * 0.5f + 0.006f), Vector3.up, 0.02f, 0.004f);
                    }
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 exitDir = new Vector3(side * 0.38f, -0.15f, 0.91f).normalized;
                        Vector3 temple = new Vector3(side * 0.80f, 0.50f, -0.30f).normalized;
                        SphereLineH(head, webbing, R, 0.005f, exitDir, temple, 0.035f, 0f, new Rect(0f, 0f, 1f, 1f));
                    }
                    {
                        Vector3 tl = new Vector3(-0.80f, 0.50f, -0.30f).normalized, tr = new Vector3(0.80f, 0.50f, -0.30f).normalized, poll = new Vector3(0f, 0.95f, -0.30f).normalized;
                        SphereLineH(head, webbing, R, 0.005f, tl, poll, 0.035f, 0f, new Rect(0f, 0f, 1f, 1f));
                        SphereLineH(head, webbing, R, 0.005f, poll, tr, 0.035f, 0f, new Rect(0f, 0f, 1f, 1f));
                    }
                    if (haveNeck) { float ty = ThroatY(); CapsuleSleeve(neck, ndims.x, ndims.y, ty - 0.015f, ty + 0.015f, 0.004f, webbing, new Rect(0f, 0f, 6f, 1f)); }
                    break;
                case 3: // Blinkers: a cloth hood over the skull with ear holes, blinker cups beside the eyes, a noseband.
                {
                    var cloth = Own(Make.MatTexTint(HorseDecals.Webbing, a.AccessoryColor, 0.05f));
                    var hood = MeshGen.Param((u, v) =>
                    {
                        float th = -Mathf.PI + u * Mathf.PI * 2f;
                        float phi = v * 110f * Mathf.Deg2Rad;
                        return HairShape.Dir(phi, th) * (R + 0.008f);
                    }, 32, 12, wrapU: true, flip: true);
                    // Ear holes: drop the hood where it meets the ear roots by pulling those vertices inside.
                    {
                        var vv = hood.vertices; Vector3 earL = new Vector3(-0.45f, 0.87f, -0.18f).normalized, earR = new Vector3(0.45f, 0.87f, -0.18f).normalized;
                        for (int i = 0; i < vv.Length; i++) { Vector3 d = vv[i].normalized; if (Vector3.Angle(d, earL) < 13f || Vector3.Angle(d, earR) < 13f) vv[i] = d * (R - 0.02f); }
                        hood.vertices = vv; hood.RecalculateNormals();
                    }
                    Piece(head, hood, cloth);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 eye = new Vector3(side * 0.70f, 0.25f, 0.67f).normalized;
                        var cup = MeshGen.Lathe(new[] { new Vector2(0f, 0.045f), new Vector2(0.03f, 0.038f), new Vector2(0.043f, 0.02f), new Vector2(0.046f, -0.005f) }, 12, true, side > 0 ? 0f : 180f, 180f);
                        MeshGen.Transform(cup, eye * (R - 0.004f), Quaternion.FromToRotation(Vector3.up, eye));
                        Piece(head, cup, Own(Make.Mat(a.AccessoryColor, 0.45f)));
                    }
                    if (haveMuzzle) BoxRing(muzzle, mdims, tipY - 0.10f * h, 0.035f, 0.005f, 0.012f, cloth, 6f);
                    break;
                }
                case 4: // Saddle pad: a quilted pad lofted over the barrel with a girth strap and buckle.
                {
                    var torso = rag.Phys(Bone.Torso);
                    if (torso == null) break;
                    Vector3 bd = new Vector3(0.34f * g, 0.90f * h, 0.46f * g);   // barrel dims: local Y = length, local +Z = DOWN, +/-X flanks
                    var quilt = Own(Make.MatTexTint(HorseDecals.Quilt, a.AccessoryColor, 0.10f));
                    float len = 0.42f * h, y0 = 0.10f * h;
                    float hx = bd.x * 0.5f, hz = bd.z * 0.5f;
                    // Profile in local XZ around the top and flanks: -X flank up, over the top (local -Z), down +X.
                    Vector3 Prof(float s, float t)
                    {
                        // s 0..1 along the U: left flank bottom -> top-left corner -> top-right corner -> right flank bottom.
                        float drop = 0.22f * g;
                        Vector3 p;
                        if (s < 0.3f) { float k = s / 0.3f; p = new Vector3(-hx - t, 0f, Mathf.Lerp(-hz + drop, -hz + 0.02f, k)); }
                        else if (s < 0.7f) { float k = (s - 0.3f) / 0.4f; p = new Vector3(Mathf.Lerp(-hx, hx, k), 0f, -hz - t); float c = Mathf.Min(k, 1f - k) * 0.4f; p.z -= 0.02f * Mathf.Sin(Mathf.Clamp01(c / 0.1f) * Mathf.PI * 0.5f) * 0f; }
                        else { float k = (s - 0.7f) / 0.3f; p = new Vector3(hx + t, 0f, Mathf.Lerp(-hz + 0.02f, -hz + drop, k)); }
                        return p;
                    }
                    Mesh Sheet(float t, bool flip)
                    {
                        var mm = MeshGen.Param((u, v) =>
                        {
                            Vector3 p = Prof(u, t);
                            float along = Mathf.Lerp(-len * 0.5f, len * 0.5f, v);
                            // Round the pad's four corners.
                            float edge = Mathf.Min(u, 1f - u), endF = Mathf.Min(v, 1f - v);
                            float corner = Mathf.Clamp01(1f - Mathf.Max(0f, 0.08f - edge) / 0.08f * Mathf.Max(0f, 0.08f - endF) / 0.08f);
                            p.z = Mathf.Lerp(-hz + 0.02f, p.z, corner);
                            return p + new Vector3(0f, y0 + along, 0f);
                        }, 40, 12, uvRepeatU: 6f, uvRepeatV: 6f, flip: flip);
                        return mm;
                    }
                    Piece(torso, Sheet(0.010f, false), quilt);
                    Piece(torso, Sheet(0.002f, true), quilt);
                    // Girth strap all round the barrel and a buckle on the near flank.
                    BoxRing(torso, bd, y0, 0.045f, 0.006f, 0.03f, leather, 8f);
                    var buckle = MeshGen.Extrude(MeshGen.Superellipse(0.012f, 0.016f, 4f, 16), 0.006f, 0.001f);
                    MeshGen.Transform(buckle, new Vector3(hx + 0.010f, y0, 0f), Quaternion.Euler(0f, 90f, 0f));
                    Piece(torso, buckle, metal);
                    break;
                }
            }
        }
    }
}
