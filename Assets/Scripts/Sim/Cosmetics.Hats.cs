using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // HATS. Non-axisymmetric hats (cap, fedora, cowboy, top hat, sombrero, wizard) mount a
    // downloaded low-poly model through CosmeticMesh; every one of those keeps a generated
    // fallback so a missing model never blanks the slot. The rest are lathes and lofts.
    public static partial class Cosmetics
    {
        // Rotate a finished piece about the HEAD CENTRE: a sphere-flush ring stays flush under any
        // rotation about the centre, which is why hat tilts are applied this way.
        static GameObject HatPiece(Transform head, Mesh mesh, Material mat, Vector3 tiltEuler)
        {
            MeshGen.Transform(mesh, Vector3.zero, Quaternion.Euler(tiltEuler), Vector3.one * _cosScale);
            return Piece(head, mesh, mat);
        }

        /// <summary>Turn an open (r, y) profile into a thin closed shell so a brim has an underside.</summary>
        static Vector2[] Thicken(Vector2[] pts, float t)
        {
            int n = pts.Length;
            var outp = new List<Vector2>(pts);
            for (int i = n - 1; i >= 0; i--)
            {
                Vector2 prev = pts[Mathf.Max(i - 1, 0)], next = pts[Mathf.Min(i + 1, n - 1)];
                Vector2 tan = (next - prev).normalized;
                Vector2 nrm = new Vector2(tan.y, -tan.x);          // outward for a profile traversed upward
                Vector2 q = pts[i] - nrm * t;
                if (q.x < 0f) q.x = 0f;
                outp.Add(q);
            }
            return outp.ToArray();
        }

        /// <summary>A shell hugging the head sphere at radius R from y = yFrom up to the pole: a
        /// hat-coloured skull-cap under a model whose crown is narrower than the head.</summary>
        static Mesh SkullCap(float yFrom, float R, int steps = 10)
        {
            float phi0 = Mathf.Acos(Mathf.Clamp(yFrom / R, -1f, 1f));
            var prof = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float phi = Mathf.Lerp(phi0, 0f, i / (float)steps);
                prof[i] = new Vector2(R * Mathf.Sin(phi), R * Mathf.Cos(phi));
            }
            return MeshGen.Lathe(prof, 32);
        }

        // Ring texture for the trapper hat's raccoon tail: v runs root (0) -> tip (1); the first
        // TailBand0 is buried under the fur brim, then six bands alternating light/dark, dark at
        // the tip. Band edges wobble with u (periodic, so the tube seam is clean) and a fine
        // speckle rides on top so the rings read as fur rather than paint.
        const float TailBand0 = 0.2f, TailBandW = 0.8f / 6f;
        static Texture2D _tailTex;
        static Texture2D TailRings()
        {
            if (_tailTex != null) return _tailTex;
            const int w = 32, hgt = 256;
            var px = new Color32[w * hgt];
            for (int y = 0; y < hgt; y++)
            {
                float v = (y + 0.5f) / hgt;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, a = u * Mathf.PI * 2f;
                    float vb = v + 0.012f * Mathf.Sin(a * 3f + v * 40f) + 0.008f * Mathf.Sin(a * 7f + 1.3f);
                    int band = Mathf.FloorToInt((vb - TailBand0) / TailBandW);
                    bool dark = vb >= TailBand0 && (band & 1) == 1;
                    float lum = (dark ? 0.18f : 1f) * (0.86f + 0.28f * Mathf.PerlinNoise(3f + 2.5f * Mathf.Cos(a), 3f + 2.5f * Mathf.Sin(a) + v * 60f));
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(lum) * 255f);
                    px[y * w + x] = new Color32(b, b, b, 255);
                }
            }
            _tailTex = new Texture2D(w, hgt, TextureFormat.RGB24, true) { name = "TrapperTail", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _tailTex.SetPixels32(px); _tailTex.Apply(true, true);
            return _tailTex;
        }

        // ---- A items: downloaded models with generated fallbacks -----------------------------
        static void BuildCap(Transform h, Material m)
        {
            // The OBJ import mirrors X, so yaw +90 (not -90) is what sends the bill to +Z. The mount
            // anchors the BOUNDS centre and the bill is in the bounds: the crown sits 0.145 model
            // units (0.08 m at size 0.56) behind that centre, so the pivot goes +0.08 forward to
            // put the crown over the skull with the bill root at the forehead (z ~0.19).
            var go = CosmeticMesh.Mount(h, "Cosmetics/Models/cap", 0.56f, CosmeticMesh.Axis.X, CosmeticMesh.Anchor.Bottom,
                                        new Vector3(0f, 0.03f, 0.08f), new Vector3(0f, 90f, 0f), m, "gray_tone2", "0f0f0f", "black");
            if (go != null) return;
            // Fallback: a lathe dome and a curved bill.
            var dome = MeshGen.Lathe(new[] { new Vector2(0.195f, 0.06f), new Vector2(0.205f, 0.12f), new Vector2(0.19f, 0.19f), new Vector2(0.12f, 0.245f), new Vector2(0f, 0.255f) }, 32);
            HatPiece(h, dome, m, Vector3.zero);
            var bill = MeshGen.Param((u, v) =>
            {
                float th = Mathf.Lerp(-40f, 40f, u) * Mathf.Deg2Rad;
                float rho = Mathf.Lerp(0.19f, 0.30f, v);
                float y = 0.075f - 0.03f * v * v - 0.012f * Mathf.Abs(Mathf.Sin(th));
                return new Vector3(rho * Mathf.Sin(th), y, rho * Mathf.Cos(th));
            }, 16, 6);
            HatPiece(h, bill, m, Vector3.zero);
            var button = MeshGen.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.01f, 0f), new Vector2(0.008f, 0.008f), new Vector2(0f, 0.01f) }, 12);
            MeshGen.Transform(button, new Vector3(0f, 0.255f, 0f)); HatPiece(h, button, Dark(), Vector3.zero);
        }
        static void BuildFedora(Transform h, Material m)
        {
            // Generated: neither downloaded candidate had a fedora's proportions (one a stovepipe, one a
            // straw hat). A truncated crown with a centre dent, a warped brim and a band.
            FeltHatFallback(h, m, 0.31f, 0.02f, 0.30f, 0.32f, dent: true);
        }
        static void BuildCowboyHat(Transform h, Material m)
        {
            var go = CosmeticMesh.Mount(h, "Cosmetics/Models/cowboy", 0.74f, CosmeticMesh.Axis.Z, CosmeticMesh.Anchor.Bottom,
                                        new Vector3(0f, 0.05f, 0f), new Vector3(-4f, 0f, 0f), m);
            if (go != null) return;
            FeltHatFallback(h, m, 0.33f, 0.09f, 0.28f, 0.32f);
        }
        static void BuildTopHat(Transform h, Material m)
        {
            var go = CosmeticMesh.Mount(h, "Cosmetics/Models/tophat", 0.56f, CosmeticMesh.Axis.X, CosmeticMesh.Anchor.Bottom,
                                        new Vector3(0f, 0.06f, 0f), Vector3.zero, m, "0c0c0c");
            if (go != null) return;
            var brim = MeshGen.Lathe(Thicken(new[] { new Vector2(0.19f, 0.07f), new Vector2(0.28f, 0.075f), new Vector2(0.285f, 0.09f) }, 0.008f), 48);
            var crown = MeshGen.Lathe(new[] { new Vector2(0.19f, 0.07f), new Vector2(0.198f, 0.37f), new Vector2(0.19f, 0.375f), new Vector2(0f, 0.375f) }, 48);
            HatPiece(h, MeshGen.Combine(brim, crown), Own(Make.Mat(m.color, 0.5f)), Vector3.zero);
            var band = MeshGen.Lathe(new[] { new Vector2(0.194f, 0.075f), new Vector2(0.194f, 0.12f) }, 48);
            HatPiece(h, band, Dark(), Vector3.zero);
        }
        static void BuildSombrero(Transform h, Material m)
        {
            // Bounds bottom = the tassel balls, 0.085 m below the brim underside: anchoring at
            // -0.045 seats the brim at ~0.04 (just above the eyes) and the crown, r 0.224 at
            // y 0.07 and 0.16 at y 0.13-0.17, closes over the top of the skull.
            var go = CosmeticMesh.Mount(h, "Cosmetics/Models/sombrero", 0.92f, CosmeticMesh.Axis.X, CosmeticMesh.Anchor.Bottom,
                                        new Vector3(0f, -0.045f, 0f), Vector3.zero, m, "000000", "band", "ribbon");
            if (go != null) return;
            var prof = new[] { new Vector2(0f, 0.37f), new Vector2(0.06f, 0.365f), new Vector2(0.10f, 0.36f), new Vector2(0.14f, 0.24f), new Vector2(0.20f, 0.09f), new Vector2(0.30f, 0.08f), new Vector2(0.42f, 0.15f) };
            HatPiece(h, MeshGen.Lathe(Thicken(prof, 0.006f), 48), m, Vector3.zero);
            var band = MeshGen.Lathe(new[] { new Vector2(0.20f, 0.095f), new Vector2(0.20f, 0.125f) }, 48);
            HatPiece(h, band, Dark(), Vector3.zero);
        }
        static void BuildWizardHat(Transform h, Material m)
        {
            var go = CosmeticMesh.Mount(h, "Cosmetics/Models/wizard", 0.68f, CosmeticMesh.Axis.X, CosmeticMesh.Anchor.Bottom,
                                        new Vector3(0f, 0.05f, 0f), Vector3.zero, m, "band", "buckle");
            if (go != null)
            {
                // The model's cone is narrower than the skull at its base (r 0.137 at y 0.095
                // against the head's 0.164), so the head showed between brim and cone. A
                // hat-coloured shell hugging the sphere (HeadR + 0.01, from just above the brim
                // to the pole) fills that: hidden inside the cone above y ~0.15, and wide enough
                // to bury the model's band ring (r 0.144) below it.
                HatPiece(h, SkullCap(0.06f, HeadR + 0.01f), m, Vector3.zero);
                return;
            }
            var brim = MeshGen.Lathe(Thicken(new[] { new Vector2(0.185f, 0.075f), new Vector2(0.36f, 0.075f) }, 0.006f), 48);
            HatPiece(h, brim, m, Vector3.zero);
            var path = MeshGen.Spline(new[] { new Vector3(0f, 0.075f, 0f), new Vector3(0f, 0.30f, -0.02f), new Vector3(0f, 0.44f, -0.14f), new Vector3(0f, 0.40f, -0.26f) }, 8);
            var radii = new float[path.Length];
            for (int i = 0; i < radii.Length; i++) { float t = i / (float)(radii.Length - 1); radii[i] = Mathf.Lerp(0.185f * Mathf.Pow(1f - t, 1.15f), 0.01f, Mathf.Pow(t, 8f)); }
            HatPiece(h, MeshGen.Tube(path, radii, 24, false, true), m, Vector3.zero);
            var band = MeshGen.Lathe(new[] { new Vector2(0.19f, 0.08f), new Vector2(0.175f, 0.12f) }, 32);
            HatPiece(h, band, Dark(), Vector3.zero);
        }
        // A generic felt hat: truncated dome crown, thickened brim with a warp, band.
        static void FeltHatFallback(Transform h, Material m, float brimR, float sideRoll, float crownTop, float apex, bool dent = false)
        {
            var crown = MeshGen.Lathe(new[] { new Vector2(0.19f, 0.08f), new Vector2(0.185f, 0.2f), new Vector2(0.17f, crownTop), new Vector2(0.10f, apex - 0.01f), new Vector2(0f, apex) }, 40);
            if (dent)
            {
                // Centre dent along the crown top and a front pinch: the fedora signature.
                var cv = crown.vertices;
                for (int i = 0; i < cv.Length; i++)
                {
                    if (cv[i].y > crownTop - 0.03f) cv[i].y -= 0.025f * Mathf.Max(0f, 1f - Mathf.Abs(cv[i].x) / 0.05f) * Mathf.Clamp01((cv[i].y - (crownTop - 0.03f)) / 0.03f);
                    if (cv[i].z > 0.08f && cv[i].y > 0.2f) cv[i].x *= 1f - 0.2f * Mathf.Clamp01((cv[i].z - 0.08f) / 0.08f) * Mathf.Clamp01((cv[i].y - 0.2f) / 0.1f);
                }
                crown.vertices = cv; crown.RecalculateNormals(); crown.RecalculateBounds();
            }
            var brim = MeshGen.Lathe(Thicken(new[] { new Vector2(0.19f, 0.08f), new Vector2(brimR, 0.08f) }, 0.006f), 48);
            var bv = brim.vertices;
            for (int i = 0; i < bv.Length; i++)
            {
                float s = Mathf.Clamp01((new Vector2(bv[i].x, bv[i].z).magnitude - 0.19f) / (brimR - 0.19f));
                float th = Mathf.Atan2(bv[i].x, bv[i].z);
                bv[i].y += sideRoll * s * Mathf.Sin(th) * Mathf.Sin(th) - 0.015f * s * Mathf.Max(Mathf.Cos(th), 0f);
            }
            brim.vertices = bv; brim.RecalculateNormals(); brim.RecalculateBounds();
            HatPiece(h, MeshGen.Combine(crown, brim), m, new Vector3(-4f, 0f, 0f));
            var band = MeshGen.Lathe(new[] { new Vector2(0.194f, 0.085f), new Vector2(0.192f, 0.125f) }, 40);
            HatPiece(h, band, Dark(), new Vector3(-4f, 0f, 0f));
        }

        // ---- B items --------------------------------------------------------------------------
        static void BuildBucketHat(Transform h, Material m)
        {
            var prof = new[] { new Vector2(0.30f, 0.01f), new Vector2(0.205f, 0.07f), new Vector2(0.20f, 0.185f), new Vector2(0.15f, 0.225f), new Vector2(0f, 0.225f) };
            HatPiece(h, MeshGen.Lathe(Thicken(prof, 0.006f), 32), m, Vector3.zero);
            var stitch = MeshGen.Lathe(new[] { new Vector2(0.205f, 0.075f), new Vector2(0.215f, 0.068f) }, 32);
            HatPiece(h, stitch, Dark(), Vector3.zero);
            var row1 = MeshGen.Lathe(new[] { new Vector2(0.24f, 0.049f), new Vector2(0.245f, 0.046f) }, 32);
            var row2 = MeshGen.Lathe(new[] { new Vector2(0.28f, 0.024f), new Vector2(0.285f, 0.021f) }, 32);
            HatPiece(h, MeshGen.Combine(row1, row2), Dark(), Vector3.zero);
        }
        static void BuildBeret(Transform h, Material m)
        {
            var prof = new[]
            {
                new Vector2(0.167f, 0.090f), new Vector2(0.164f, 0.118f), new Vector2(0.225f, 0.150f), new Vector2(0.225f, 0.162f),
                new Vector2(0.18f, 0.195f), new Vector2(0.10f, 0.205f), new Vector2(0f, 0.207f),
            };
            var beret = MeshGen.Lathe(prof, 32);
            var v = beret.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                float w = Mathf.Clamp01((v[i].y - 0.118f) / 0.08f);
                v[i].y -= 0.045f * w * Mathf.Pow(Mathf.Max(v[i].x, 0f) / 0.225f, 1.5f);
                v[i].x += 0.01f * w * (v[i].y - 0.10f);
            }
            beret.vertices = v; beret.RecalculateNormals(); beret.RecalculateBounds();
            HatPiece(h, beret, m, Vector3.zero);
            var stalk = MeshGen.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.006f, 0f), new Vector2(0.007f, 0.01f), new Vector2(0f, 0.014f) }, 10);
            MeshGen.Transform(stalk, new Vector3(0.01f, 0.20f, 0f)); HatPiece(h, stalk, m, Vector3.zero);
        }
        static void BuildPeakyCap(Transform h, Material m)
        {
            // The pre-overhaul primitive build, kept verbatim by request: a low ellipsoid crown, a
            // second ellipsoid swept forward over the brim, a stubby box brim with a dark
            // underside, and a top button.
            LegacyBall(h, new Vector3(0f, 0.14f, -0.01f), new Vector3(0.46f, 0.22f, 0.46f), m);     // low rounded crown
            LegacyBall(h, new Vector3(0f, 0.15f, 0.10f), new Vector3(0.44f, 0.16f, 0.30f), m);      // front peak swept over the brim
            LegacyBlk(h, new Vector3(0f, 0.09f, 0.20f), new Vector3(0.30f, 0.03f, 0.12f), m);       // short stubby brim
            LegacyBlk(h, new Vector3(0f, 0.078f, 0.20f), new Vector3(0.28f, 0.01f, 0.11f), Dark()); // brim underside
            LegacyBall(h, new Vector3(0f, 0.24f, -0.01f), new Vector3(0.05f, 0.05f, 0.05f), m);     // top button
        }
        static void BuildHeadband(Transform h, Material m)
        {
            HeadRing(h, m, 1.07f, 0.045f);
        }
        static void BuildTrapperHat(Transform h, Material m)
        {
            var dome = MeshGen.Lathe(new[] { new Vector2(0.20f, 0.075f), new Vector2(0.205f, 0.12f), new Vector2(0.18f, 0.185f), new Vector2(0.10f, 0.225f), new Vector2(0f, 0.232f) }, 32);
            HatPiece(h, dome, m, Vector3.zero);
            var fur = MeshGen.Torus(0.200f, 0.032f, 32, 12);
            MeshGen.Transform(fur, new Vector3(0f, 0.075f, 0f)); HatPiece(h, fur, Light(), Vector3.zero);
            // Front flap folded up over the brow.
            var flap = MeshGen.Param((u, v) =>
            {
                float th = Mathf.Lerp(-35f, 35f, u) * Mathf.Deg2Rad;
                float y = Mathf.Lerp(0.08f, 0.17f, v);
                float R = 0.215f + 0.02f * v;
                float rr = Mathf.Sqrt(Mathf.Max(R * R - y * y, 1e-6f));
                return new Vector3(rr * Mathf.Sin(th), y, rr * Mathf.Cos(th));
            }, 12, 4);
            HatPiece(h, flap, Light(), Vector3.zero);
            // Ear flaps: thick patches hanging over the ears, with a fur rim and ties.
            for (int side = -1; side <= 1; side += 2)
            {
                int sd = side;
                var ear = MeshGen.Param((u, v) =>
                {
                    float th = (sd * 90f + Mathf.Lerp(-28f, 28f, u)) * Mathf.Deg2Rad;
                    float y = Mathf.Lerp(0.06f, -0.13f, v);
                    float R = HeadR + 0.022f;
                    float rr = Mathf.Sqrt(Mathf.Max(R * R - y * y, 1e-6f));
                    return new Vector3(rr * Mathf.Sin(th), y, rr * Mathf.Cos(th));
                }, 8, 8);
                HatPiece(h, ear, m, Vector3.zero);
                var rim = MeshGen.Param((u, v) =>
                {
                    float th = (sd * 90f + Mathf.Lerp(-30f, 30f, u)) * Mathf.Deg2Rad;
                    float y = Mathf.Lerp(-0.10f, -0.15f, v);
                    float R = HeadR + 0.03f;
                    float rr = Mathf.Sqrt(Mathf.Max(R * R - y * y, 1e-6f));
                    return new Vector3(rr * Mathf.Sin(th), y, rr * Mathf.Cos(th));
                }, 8, 2);
                HatPiece(h, rim, Light(), Vector3.zero);
            }
            // Raccoon tail: a fat tapered tube rooted between head and dome at the back (the root
            // ring hides in the fur), emerging under the fur brim and drooping down the nape and
            // upper back (~0.35 m showing). Fluff: each ring band is a bead (radius swells mid-band),
            // the surface is ruffled by Perlin along the normals, and the stripe texture has
            // jagged, speckled edges.
            var tailCtrl = new[]
            {
                new Vector3(0f, 0.09f, -0.175f), new Vector3(0f, 0.055f, -0.235f), new Vector3(0f, -0.02f, -0.27f),
                new Vector3(0f, -0.10f, -0.275f), new Vector3(0f, -0.18f, -0.255f), new Vector3(0f, -0.245f, -0.215f),
            };
            var tailPath = MeshGen.Spline(tailCtrl, 7);
            var tailR = new float[tailPath.Length];
            for (int i = 0; i < tailR.Length; i++)
            {
                float t = i / (float)(tailR.Length - 1);
                float body = t < TailBand0 ? Mathf.Lerp(0.034f, 0.048f, t / TailBand0)
                                           : Mathf.Lerp(0.048f, 0.012f, Mathf.Pow((t - TailBand0) / (1f - TailBand0), 1.5f));
                float bead = Mathf.Sin(Mathf.Repeat((t - TailBand0) / TailBandW, 1f) * Mathf.PI);
                tailR[i] = body * (1f + 0.12f * bead);
            }
            var tail = MeshGen.Tube(tailPath, tailR, 16, true, true);
            var tv = tail.vertices; var tn = tail.normals;
            for (int i = 0; i < tv.Length; i++)
            {
                float k = Mathf.PerlinNoise(tv[i].x * 55f + tv[i].y * 70f + 3.1f, tv[i].z * 60f - tv[i].y * 45f + 7.7f) - 0.5f;
                tv[i] += tn[i] * (k * 0.010f);
            }
            tail.vertices = tv; tail.RecalculateNormals(); tail.RecalculateBounds();
            HatPiece(h, tail, Own(Make.MatTexTint(TailRings(), new Color(0.74f, 0.68f, 0.60f), 0.05f)), Vector3.zero);
        }
        static void BuildPartyHat(Transform h, Material m)
        {
            // Cone in six alternating stripes (two materials, no texture), frill, pom-pom, elastic.
            float baseY = Mathf.Sqrt(HeadR * HeadR - 0.09f * 0.09f) - 0.008f;
            var mine = new List<Mesh>(); var white = new List<Mesh>();
            for (int s = 0; s < 6; s++)
            {
                var wedge = MeshGen.Lathe(new[] { new Vector2(0.09f, baseY), new Vector2(0f, 0.42f) }, 4, true, s * 60f, 60f);
                (s % 2 == 0 ? mine : white).Add(wedge);
            }
            var tilt = new Vector3(8f, 0f, 0f);
            HatPiece(h, MeshGen.Combine(mine.ToArray()), m, tilt);
            HatPiece(h, MeshGen.Combine(white.ToArray()), Light(), tilt);
            var frill = MeshGen.Torus(0.09f, 0.01f, 32, 8); MeshGen.Transform(frill, new Vector3(0f, baseY, 0f));
            HatPiece(h, frill, Light(), tilt);
            var pom = MeshGen.Lathe(new[] { new Vector2(0f, -0.018f), new Vector2(0.013f, -0.012f), new Vector2(0.018f, 0f), new Vector2(0.013f, 0.012f), new Vector2(0f, 0.018f) }, 14);
            MeshGen.Transform(pom, new Vector3(0f, 0.42f, 0f)); HatPiece(h, pom, Light(), tilt);
            // Elastic: from each side of the (tilted) base, down the side of the head in front of
            // the ear and under the jaw, meeting beneath the chin; a Catmull-Rom through those
            // waypoints, renormalised so the tube hugs the sphere at HeadR + 0.004 the whole way.
            Quaternion tiltQ = Quaternion.Euler(tilt);
            for (int side = -1; side <= 1; side += 2)
            {
                var ctrl = new[]
                {
                    (tiltQ * new Vector3(side * 0.09f, baseY, 0f)).normalized,
                    Dir(side * 0.185f, 0.04f, 0.06f), Dir(side * 0.15f, -0.10f, 0.08f),
                    Dir(side * 0.06f, -0.165f, 0.09f), Dir(0f, -0.88f, 0.47f),
                };
                var dirs = MeshGen.Spline(ctrl, 6);
                for (int i = 0; i < dirs.Length; i++) dirs[i].Normalize();
                SweptTube(h, Light(), dirs, 0.004f, 0.002f, 6);
            }
        }
    }
}
