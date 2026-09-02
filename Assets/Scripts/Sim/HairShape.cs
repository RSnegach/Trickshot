using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The shared HAIRLINE and scalp-surface maths behind every short human hair style: the
    /// hairline curve, a signed distance to it, a deterministic noise field on the sphere, and the
    /// shell radius function. Both the scalp shell (Cosmetics.HairShell) and HairSim root placement
    /// call ShellRadius, which is the one rule that stops tuft roots floating off a shell.
    ///
    /// Everything is in HEAD-LOCAL unit directions (+Y crown, +Z face, +X side) and metres at
    /// girth 1; callers scale by Cosmetics._cosScale.
    /// </summary>
    public static class HairShape
    {
        /// <summary>
        /// Hairline as a polar angle phi (radians down from the crown) per azimuth theta (0 = the
        /// face, +/-pi = the nape). Catmull-Rom through: forehead 0.85, temple notch 0.75 at +/-0.55,
        /// sides 1.25 at +/-1.3, behind the ear 1.60 at +/-2.2, nape 1.88 at pi (well below ear
        /// level, so every style covers the back of the head). Symmetric in theta.
        /// </summary>
        public static float HairlinePhi(float theta)
        {
            float t = Mathf.Abs(Mathf.Repeat(theta + Mathf.PI, Mathf.PI * 2f) - Mathf.PI);   // 0..pi
            // Knots (theta, phi) on the half-circle, mirrored at both ends for the spline.
            for (int i = 0; i < KnotT.Length - 1; i++)
            {
                if (t <= KnotT[i + 1] || i == KnotT.Length - 2)
                {
                    float p0 = KnotP[Mathf.Max(i - 1, 0)], p1 = KnotP[i], p2 = KnotP[i + 1], p3 = KnotP[Mathf.Min(i + 2, KnotP.Length - 1)];
                    float u = Mathf.InverseLerp(KnotT[i], KnotT[i + 1], t);
                    return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
                }
            }
            return KnotP[KnotP.Length - 1];
        }
        static readonly float[] KnotT = { 0f, 0.55f, 1.3f, 2.2f, Mathf.PI };
        static readonly float[] KnotP = { 0.85f, 0.75f, 1.25f, 1.60f, 1.88f };

        /// <summary>Polar angle and azimuth of a unit direction (phi 0 = crown, theta 0 = +Z face).</summary>
        public static void Polar(Vector3 dir, out float phi, out float theta)
        {
            phi = Mathf.Acos(Mathf.Clamp(dir.y, -1f, 1f));
            theta = Mathf.Atan2(dir.x, dir.z);
        }

        public static Vector3 Dir(float phi, float theta)
        {
            float sp = Mathf.Sin(phi);
            return new Vector3(sp * Mathf.Sin(theta), Mathf.Cos(phi), sp * Mathf.Cos(theta));
        }

        /// <summary>Angular signed distance to the hairline: positive INSIDE (toward the crown), in radians.</summary>
        public static float HairlineSdf(Vector3 dir)
        {
            Polar(dir, out float phi, out float theta);
            return HairlinePhi(theta) - phi;
        }

        // ---------------------------------------------------------------- noise
        static float Hash(int x, int y, int z, int seed)
        {
            uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263) ^ (uint)(z * 2147483647) ^ (uint)(seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
        static float Smooth(float t) => t * t * (3f - 2f * t);
        static float ValueNoise(Vector3 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = Smooth(p.x - x0), fy = Smooth(p.y - y0), fz = Smooth(p.z - z0);
            float c00 = Mathf.Lerp(Hash(x0, y0, z0, seed), Hash(x0 + 1, y0, z0, seed), fx);
            float c10 = Mathf.Lerp(Hash(x0, y0 + 1, z0, seed), Hash(x0 + 1, y0 + 1, z0, seed), fx);
            float c01 = Mathf.Lerp(Hash(x0, y0, z0 + 1, seed), Hash(x0 + 1, y0, z0 + 1, seed), fx);
            float c11 = Mathf.Lerp(Hash(x0, y0 + 1, z0 + 1, seed), Hash(x0 + 1, y0 + 1, z0 + 1, seed), fx);
            return Mathf.Lerp(Mathf.Lerp(c00, c10, fy), Mathf.Lerp(c01, c11, fy), fz);
        }

        /// <summary>
        /// Deterministic smooth noise on the unit sphere in [-1, 1]. `freq` is cycles per unit
        /// (a sphere of radius 1: freq 4 gives ~1.5 rad features); octaves halve the wavelength.
        /// No UnityEngine.Random, so a body rebuilds identically everywhere.
        /// </summary>
        public static float Noise(Vector3 dir, float freq, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            Vector3 p = dir * freq + new Vector3(31.7f, 17.3f, 5.1f);
            for (int o = 0; o < octaves; o++)
            {
                sum += (ValueNoise(p, seed + o * 17) * 2f - 1f) * amp;
                norm += amp;
                p *= 2.03f; amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>A smooth random unit tangent field on the sphere (for RandomSmooth comb modes).</summary>
        public static Vector3 NoiseTangent(Vector3 dir, float freq, int seed)
        {
            var g = new Vector3(Noise(dir, freq, 2, seed), Noise(dir, freq, 2, seed + 101), Noise(dir, freq, 2, seed + 202));
            Vector3 t = g - dir * Vector3.Dot(g, dir);
            if (t.sqrMagnitude < 1e-6f) t = Vector3.Cross(dir, Vector3.up);
            return t.normalized;
        }

        // ---------------------------------------------------------------- shell
        /// <summary>How a shell's comb (Kajiya tangent) field is laid out.</summary>
        public enum Comb { Meridian, ForwardUp, TowardPoint, Outward, RandomSmooth }

        /// <summary>Parameters for one scalp shell. Metres at girth 1 (scaled by the caller).</summary>
        public class ShellParams
        {
            public float ThickCrown = 0.012f;      // thickness at the crown
            public float ThickSide = 0.006f;       // thickness at the sides (phi ~ 1.2)
            public float ThickEdge = 0.004f;       // thickness just inside the hairline
            public float NoiseAmp = 0f;            // +/- metres of surface noise
            public float NoiseFreq = 4f;
            public int NoiseOctaves = 2;
            public int Seed = 1;
            public System.Func<Vector3, float> ThickOverride;   // dir -> thickness (replaces the crown/side/edge blend)
            public Comb CombMode = Comb.Meridian;
            public Vector3 CombPoint = new Vector3(0f, 0.55f, -0.83f);   // for TowardPoint
            public float LipRings = 3;             // rounded lip rings (0 = none: a decal)
            public float EdgeFade = 0f;            // metres of vertex-alpha fade inside the hairline (decal use)
            public float PhiRampAlpha = 1f;        // alpha multiplier at phi 1.2 (1 = flat)
            public float Proud = 0.004f;           // base standoff above the head
            public float PlaneClampY = 0f;         // 0 = none: clamp the outer surface to y <= HeadR + this (a hint of flat top)
            public float QuiffLip = 0f;            // extra thickness over the front third (theta within +/-0.6, phi < 0.7)
            public int Cols = 32, Rows = 12;
            public bool Decal => LipRings <= 0f;
        }

        /// <summary>
        /// Outer shell radius (metres at girth 1, times headR/0.19) in direction dir. The ONE
        /// function shells and roots share.
        /// </summary>
        public static float ShellRadius(Vector3 dir, ShellParams p, float headR)
        {
            Polar(dir, out float phi, out float theta);
            float thick;
            if (p.ThickOverride != null) thick = p.ThickOverride(dir);
            else
            {
                // Crown -> side -> edge blend by how far down the hairline we are.
                float edge = HairlinePhi(theta);
                float t = Mathf.Clamp01(phi / Mathf.Max(edge, 1e-3f));
                float side = Mathf.Lerp(p.ThickCrown, p.ThickSide, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((phi - 0.3f) / 0.9f)));
                thick = Mathf.Lerp(side, p.ThickEdge, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.8f) / 0.2f)));
            }
            if (p.QuiffLip > 0f && Mathf.Abs(theta) < 0.6f && phi < 0.7f)
                thick += p.QuiffLip * (1f - Mathf.Abs(theta) / 0.6f) * Mathf.Clamp01((0.7f - phi) / 0.3f);
            if (p.NoiseAmp > 0f) thick += p.NoiseAmp * Noise(dir, p.NoiseFreq, p.NoiseOctaves, p.Seed);
            float r = headR + p.Proud + Mathf.Max(thick, 0f);
            if (p.PlaneClampY > 0f && dir.y > 0f)
            {
                float maxR = (headR + p.PlaneClampY) / dir.y;
                r = Mathf.Min(r, maxR);
            }
            return r;
        }
    }
}
