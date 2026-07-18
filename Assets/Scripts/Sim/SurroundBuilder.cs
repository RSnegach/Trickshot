using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Builds the simple decorative surroundings OUTSIDE the stadium bowl for a venue:
    /// trees + houses (Town Park), a running track ring (Olympic), palms + sand (Beach),
    /// flags + statues (National). All cheap primitives, all visual (no colliders), all
    /// parented under a "Surroundings" empty. Deterministic layout (fixed LCG seed) so a
    /// venue looks the same every time without Random-in-Awake surprises.
    /// </summary>
    public static class SurroundBuilder
    {
        static uint _seed;
        static float Rand() { _seed = _seed * 1664525u + 1013904223u; return (_seed >> 8) / 16777216f; }
        static float Range(float a, float b) => a + (b - a) * Rand();

        // Bowl half-extents at pitch level (X to the side stands, Z to the end stands),
        // plus how far the rows climb back, so decorations sit clear of the structure.
        static float BowlHalfX => PitchLayout.HalfWidth + PitchLayout.StandFrontGap
                                  + PitchLayout.StandRows * PitchLayout.RowDepth + 6f;
        static float BowlHalfZ => PitchLayout.PitchLength * 0.5f + PitchLayout.StandFrontGap
                                  + PitchLayout.StandRows * PitchLayout.RowDepth + 6f;
        static Vector3 Center => new Vector3(0f, 0f, PitchLayout.PitchCenterZ);

        public static void Build(Transform root, StadiumStyle s)
        {
            _seed = 0x51ED5EED;
            var p = Make.Empty("Surroundings", Vector3.zero, root).transform;
            switch (s.Surroundings)
            {
                case Surroundings.Trees: BuildTrees(p); break;
                case Surroundings.Track: BuildTrack(p); break;
                case Surroundings.Palms: BuildPalms(p); break;
                case Surroundings.Flags: BuildFlags(p); break;
            }
        }

        // A ring of positions just outside the bowl, evenly spaced with jitter.
        static Vector3 RingPoint(float t, float outset, float jitter)
        {
            // t in 0..1 around an ellipse hugging the bowl.
            float ang = t * Mathf.PI * 2f;
            float rx = BowlHalfX + outset + Range(-jitter, jitter);
            float rz = BowlHalfZ + outset + Range(-jitter, jitter);
            return Center + new Vector3(Mathf.Sin(ang) * rx, 0f, Mathf.Cos(ang) * rz);
        }

        // ---- Town Park: leafy trees + a few blocky houses ----
        static void BuildTrees(Transform p)
        {
            var trunk = Make.Mat(new Color(0.35f, 0.24f, 0.14f), 0.1f);
            var leaf  = Make.Mat(new Color(0.18f, 0.42f, 0.18f), 0.05f);
            for (int i = 0; i < 60; i++)
            {
                Vector3 pos = RingPoint(i / 60f, Range(3f, 16f), 4f);
                Tree(p, pos, trunk, leaf, Range(3f, 6f));
            }
            var wall = Make.Mat(new Color(0.80f, 0.76f, 0.68f), 0.1f);
            var roof = Make.Mat(new Color(0.5f, 0.22f, 0.18f), 0.1f);
            for (int i = 0; i < 14; i++)
            {
                Vector3 pos = RingPoint(i / 14f + 0.03f, Range(20f, 34f), 6f);
                House(p, pos, wall, roof);
            }
        }

        static void Tree(Transform p, Vector3 pos, Material trunk, Material leaf, float h)
        {
            Make.Cylinder("Trunk", 0.25f, h, pos + Vector3.up * (h * 0.5f), 1, trunk, p, null);
            var c = Make.Sphere("Canopy", h * 0.9f, pos + Vector3.up * (h + h * 0.25f), leaf, p);
            Object.Destroy(c.GetComponent<Collider>());
        }

        static void House(Transform p, Vector3 pos, Material wall, Material roof)
        {
            float w = Range(5f, 9f), d = Range(5f, 9f), h = Range(3f, 5f);
            Make.Box("House", new Vector3(w, h, d), pos + Vector3.up * (h * 0.5f), wall, p, collider: false);
            Make.Box("Roof", new Vector3(w + 0.6f, 0.6f, d + 0.6f), pos + Vector3.up * (h + 0.3f), roof, p, collider: false);
        }

        // ---- Olympic: a red running track ring just outside the pitch runoff ----
        static void BuildTrack(Transform p)
        {
            var track = Make.Mat(new Color(0.72f, 0.28f, 0.20f), 0.05f);
            var lane  = Make.Unlit(new Color(0.95f, 0.95f, 0.95f));
            // Track sits between the pitch touch/goal lines and the stands (in the runoff
            // band). Build it as a flat ring of thin boxes framing the pitch rectangle.
            float innerX = PitchLayout.HalfWidth + 1.5f;
            float innerZ = PitchLayout.PitchLength * 0.5f + 1.5f;
            float bandW = PitchLayout.StandFrontGap - 1.0f;   // width of the track band
            float y = 0.03f;
            // Two long sides (along Z) and two ends (along X).
            Make.Box("TrackPX", new Vector3(bandW, 0.04f, innerZ * 2f), Center + new Vector3(innerX + bandW * 0.5f, y, 0f), track, p, collider: false);
            Make.Box("TrackMX", new Vector3(bandW, 0.04f, innerZ * 2f), Center + new Vector3(-innerX - bandW * 0.5f, y, 0f), track, p, collider: false);
            Make.Box("TrackPZ", new Vector3(innerX * 2f + bandW * 2f, 0.04f, bandW), Center + new Vector3(0f, y, innerZ + bandW * 0.5f), track, p, collider: false);
            Make.Box("TrackMZ", new Vector3(innerX * 2f + bandW * 2f, 0.04f, bandW), Center + new Vector3(0f, y, -innerZ - bandW * 0.5f), track, p, collider: false);
            // A couple of white lane stripes along the long sides.
            for (int i = 1; i <= 2; i++)
            {
                float off = innerX + bandW * (i / 3f);
                Make.Box("LanePX", new Vector3(0.08f, 0.05f, innerZ * 2f), Center + new Vector3(off, y + 0.01f, 0f), lane, p, collider: false);
                Make.Box("LaneMX", new Vector3(0.08f, 0.05f, innerZ * 2f), Center + new Vector3(-off, y + 0.01f, 0f), lane, p, collider: false);
            }
        }

        // ---- Beach: sand apron + palm trees ----
        static void BuildPalms(Transform p)
        {
            // A big sand plane just under the grass edges, extending outward.
            var sand = Make.Mat(new Color(0.90f, 0.82f, 0.60f), 0.05f);
            var apron = Make.Box("Sand", new Vector3(BowlHalfX * 2f + 80f, 0.2f, BowlHalfZ * 2f + 80f),
                                  Center + new Vector3(0f, -0.12f, 0f), sand, p, collider: false);
            var trunk = Make.Mat(new Color(0.45f, 0.32f, 0.18f), 0.1f);
            var frond = Make.Mat(new Color(0.20f, 0.50f, 0.24f), 0.05f);
            for (int i = 0; i < 40; i++)
            {
                Vector3 pos = RingPoint(i / 40f, Range(4f, 22f), 6f);
                Palm(p, pos, trunk, frond, Range(5f, 9f));
            }
        }

        static void Palm(Transform p, Vector3 pos, Material trunk, Material frond, float h)
        {
            Make.Cylinder("PalmTrunk", 0.18f, h, pos + Vector3.up * (h * 0.5f), 1, trunk, p, null);
            // A few flat fronds fanning out at the top.
            for (int f = 0; f < 6; f++)
            {
                float a = f / 6f * Mathf.PI * 2f;
                var leaf = Make.Box("Frond", new Vector3(2.6f, 0.08f, 0.7f),
                                    pos + Vector3.up * h + new Vector3(Mathf.Sin(a) * 1.3f, 0.1f, Mathf.Cos(a) * 1.3f),
                                    frond, p, collider: false);
                leaf.transform.rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 18f);
            }
        }

        // ---- National: flagpoles with colored flags + a few plinth statues ----
        static void BuildFlags(Transform p)
        {
            var pole = Make.Mat(new Color(0.75f, 0.75f, 0.78f), 0.3f, 0.5f);
            var flagCols = new[]
            {
                Make.Unlit(new Color(0.85f, 0.15f, 0.20f)), Make.Unlit(new Color(0.15f, 0.30f, 0.75f)),
                Make.Unlit(new Color(0.95f, 0.95f, 0.95f)), Make.Unlit(new Color(0.90f, 0.75f, 0.15f)),
            };
            for (int i = 0; i < 28; i++)
            {
                Vector3 pos = RingPoint(i / 28f, Range(4f, 10f), 2f);
                float h = 12f;
                Make.Cylinder("FlagPole", 0.12f, h, pos + Vector3.up * (h * 0.5f), 1, pole, p, null);
                var flag = flagCols[i % flagCols.Length];
                Make.Box("Flag", new Vector3(0.1f, 1.4f, 2.4f), pos + Vector3.up * (h - 1.0f) + new Vector3(0f, 0f, 1.2f), flag, p, collider: false);
            }
            var stone = Make.Mat(new Color(0.62f, 0.60f, 0.56f), 0.1f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 pos = RingPoint(i / 6f + 0.08f, Range(16f, 26f), 4f);
                Statue(p, pos, stone);
            }
        }

        static void Statue(Transform p, Vector3 pos, Material stone)
        {
            Make.Box("Plinth", new Vector3(2.2f, 2.0f, 2.2f), pos + Vector3.up * 1.0f, stone, p, collider: false);
            // A crude standing figure: torso, head, two legs.
            Make.Box("Torso", new Vector3(1.0f, 1.6f, 0.6f), pos + Vector3.up * 3.0f, stone, p, collider: false);
            var head = Make.Sphere("Head", 0.7f, pos + Vector3.up * 4.1f, stone, p);
            Object.Destroy(head.GetComponent<Collider>());
        }
    }
}
