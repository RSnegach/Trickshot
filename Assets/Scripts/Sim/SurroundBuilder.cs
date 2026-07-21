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

        // ---- Beach: open seaside. Sand apron, a big sea plane beyond it, and the shoreline
        // paraphernalia (palms, beach chairs + umbrellas, tiki huts, surfboards, beach balls,
        // ring floats). No stands, so this defines the whole venue. ----
        static void BuildPalms(Transform p)
        {
            // Sand apron just under the grass edges, extending well outward.
            var sand = Make.Mat(new Color(0.92f, 0.84f, 0.62f), 0.05f);
            float sandHalfX = BowlHalfX + 55f, sandHalfZ = BowlHalfZ + 55f;
            Make.Box("Sand", new Vector3(sandHalfX * 2f, 0.2f, sandHalfZ * 2f),
                     Center + new Vector3(0f, -0.12f, 0f), sand, p, collider: false);

            // The SEA: a large flat plane ringing the sand, sitting slightly lower, so the
            // pitch reads as an island of grass on a beach with water all around.
            var sea = Make.Unlit(new Color(0.15f, 0.55f, 0.68f));
            float seaHalfX = sandHalfX + 260f, seaHalfZ = sandHalfZ + 260f;
            Make.Box("Sea", new Vector3(seaHalfX * 2f, 0.16f, seaHalfZ * 2f),
                     Center + new Vector3(0f, -0.20f, 0f), sea, p, collider: false);

            var trunk = Make.Mat(new Color(0.45f, 0.32f, 0.18f), 0.1f);
            var frond = Make.Mat(new Color(0.20f, 0.50f, 0.24f), 0.05f);

            // Palms dotted around the sand.
            for (int i = 0; i < 34; i++)
            {
                Vector3 pos = RingPoint(i / 34f, Range(4f, 20f), 6f);
                Palm(p, pos, trunk, frond, Range(5f, 9f));
            }

            // Beach chairs + umbrellas: the "seating", an inner ring facing the pitch.
            var chairMat = Make.Mat(new Color(0.85f, 0.80f, 0.72f), 0.1f);
            var umbA = Make.Mat(new Color(0.90f, 0.25f, 0.25f), 0.1f);
            var umbB = Make.Mat(new Color(0.95f, 0.85f, 0.25f), 0.1f);
            for (int i = 0; i < 26; i++)
            {
                float t = i / 26f;
                Vector3 pos = RingPoint(t, Range(2f, 7f), 3f);
                Vector3 toCenter = Center - pos; toCenter.y = 0f;
                float faceYaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
                BeachChair(p, pos, faceYaw, chairMat, (i % 2 == 0) ? umbA : umbB);
            }

            // Tiki huts: thatched bars spaced further out.
            var hutPost = Make.Mat(new Color(0.42f, 0.30f, 0.17f), 0.1f);
            var thatch  = Make.Mat(new Color(0.62f, 0.48f, 0.24f), 0.05f);
            for (int i = 0; i < 7; i++)
            {
                Vector3 pos = RingPoint(i / 7f + 0.05f, Range(24f, 40f), 8f);
                TikiHut(p, pos, hutPost, thatch);
            }

            // Extras: surfboards stuck upright in the sand, beach balls, ring floats.
            var board1 = Make.Mat(new Color(0.95f, 0.35f, 0.45f), 0.2f);
            var board2 = Make.Mat(new Color(0.30f, 0.70f, 0.85f), 0.2f);
            for (int i = 0; i < 12; i++)
            {
                Vector3 pos = RingPoint(i / 12f + 0.02f, Range(8f, 26f), 7f);
                Surfboard(p, pos, (i % 2 == 0) ? board1 : board2, Range(0f, 360f));
            }
            var ballCols = new[]
            {
                Make.Mat(new Color(0.95f, 0.95f, 0.95f), 0.1f), Make.Mat(new Color(0.90f, 0.30f, 0.30f), 0.1f),
                Make.Mat(new Color(0.25f, 0.55f, 0.90f), 0.1f),
            };
            for (int i = 0; i < 14; i++)
            {
                Vector3 pos = RingPoint(i / 14f + 0.13f, Range(3f, 24f), 8f);
                var ball = Make.Sphere("BeachBall", Range(0.6f, 1.0f), pos + Vector3.up * 0.4f, ballCols[i % ballCols.Length], p);
                Object.Destroy(ball.GetComponent<Collider>());
            }
            var floatMat = Make.Mat(new Color(0.98f, 0.55f, 0.35f), 0.1f);
            for (int i = 0; i < 8; i++)
            {
                // Ring floats lying flat out on the water.
                Vector3 pos = RingPoint(i / 8f + 0.07f, Range(60f, 160f), 30f);
                var ring = Make.Cylinder("Float", Range(1.2f, 2.0f), 0.3f, pos + Vector3.up * 0.02f, 1, floatMat, p, null);
                if (ring.GetComponent<Collider>() is Collider rc) Object.Destroy(rc);
            }
        }

        // A slanted beach lounger + a parasol angled over it, facing the pitch (yaw deg).
        static void BeachChair(Transform p, Vector3 pos, float yaw, Material chairMat, Material umbMat)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            // Seat base + reclined back.
            var seat = Make.Box("ChairSeat", new Vector3(1.0f, 0.12f, 1.4f), pos + Vector3.up * 0.35f, chairMat, p, collider: false);
            seat.transform.rotation = rot;
            var back = Make.Box("ChairBack", new Vector3(1.0f, 1.0f, 0.12f), pos + rot * new Vector3(0f, 0.75f, -0.6f), chairMat, p, collider: false);
            back.transform.rotation = rot * Quaternion.Euler(-35f, 0f, 0f);
            // Parasol: pole + a flat tilted canopy.
            Make.Cylinder("UmbPole", 0.06f, 2.6f, pos + rot * new Vector3(0.6f, 1.3f, -0.4f), 1, chairMat, p, null);
            var canopy = Make.Cylinder("UmbTop", 1.5f, 0.12f, pos + rot * new Vector3(0.6f, 2.5f, -0.2f), 1, umbMat, p, null);
            canopy.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
            if (canopy.GetComponent<Collider>() is Collider cc) Object.Destroy(cc);
        }

        // A tiki hut: four posts + a raised pyramidal thatched roof.
        static void TikiHut(Transform p, Vector3 pos, Material post, Material thatch)
        {
            float w = Range(4f, 6f), h = 3f;
            foreach (var c in new[] { new Vector2(1,1), new Vector2(1,-1), new Vector2(-1,1), new Vector2(-1,-1) })
                Make.Cylinder("HutPost", 0.16f, h, pos + new Vector3(c.x * w * 0.4f, h * 0.5f, c.y * w * 0.4f), 1, post, p, null);
            // Flat platform under the roof.
            Make.Box("HutDeck", new Vector3(w, 0.16f, w), pos + Vector3.up * 0.08f, post, p, collider: false);
            // Thatched roof: two stacked shrinking slabs to fake a peaked palm roof.
            Make.Box("HutRoof0", new Vector3(w + 1.4f, 0.3f, w + 1.4f), pos + Vector3.up * (h + 0.2f), thatch, p, collider: false);
            Make.Box("HutRoof1", new Vector3(w * 0.6f, 0.3f, w * 0.6f), pos + Vector3.up * (h + 0.55f), thatch, p, collider: false);
        }

        // A surfboard planted upright in the sand, leaning a little, at a random facing.
        static void Surfboard(Transform p, Vector3 pos, Material mat, float yaw)
        {
            var board = Make.Box("Surfboard", new Vector3(0.7f, 3.2f, 0.12f), pos + Vector3.up * 1.5f, mat, p, collider: false);
            board.transform.rotation = Quaternion.Euler(8f, yaw, 6f);
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
