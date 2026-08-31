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

        // ---- imported model sets (Kenney nature-kit, CC0), replacing the primitive trees ----
        // These are picked by LOOP INDEX, never by Rand(). Consuming a random number to choose a variant
        // would shift every subsequent draw and silently rearrange the entire venue, and the whole point
        // of the fixed LCG seed above is that a venue looks the same every run. Index selection keeps the
        // existing layout byte for byte and still gives variety, because position and height stay random.
        // Selection is (i * 3 + i / count) % count. The stride of 3 stops the list being walked in
        // order, and the + i / count term advances the phase by one on every full cycle, which is what
        // actually kills the repeat: a plain stride still has period 8 over 60 trees, so the same eight
        // trees recur seven times around the ring. With the phase term there is no repeating period at
        // all, and the spread across models stays even (7 or 8 of each).
        static readonly string[] ParkTrees =
        {
            "Props/Trees/tree_oak",       "Props/Trees/tree_default",  "Props/Trees/tree_blocks",
            "Props/Trees/tree_tall",      "Props/Trees/tree_small",    "Props/Trees/tree_pineRoundA",
            "Props/Trees/tree_pineTallB", "Props/Trees/tree_default_fall",
        };
        static readonly string[] BeachPalms =
        {
            "Props/Trees/tree_palm",     "Props/Trees/tree_palmTall",
            "Props/Trees/tree_palmBend", "Props/Trees/tree_palmShort",
        };
        // The city beyond the stands. Mixed low blocks and towers so the skyline has a profile rather
        // than being a row of equal slabs.
        // ONE of each model, in a deliberate left-to-right order, low at the edges and tall in the
        // middle. That order IS the skyline's profile - the array is read as a row, not sampled at
        // random - so the silhouette rises to a centre and falls away, which is what a city looks like
        // from outside it.
        static readonly string[] SkylineRow =
        {
            "Props/City/skyline_e",   // low, left edge
            "Props/City/skyline_c",
            "Props/City/tower_b",
            "Props/City/tower_a",     // tallest, centre
            "Props/City/tower_c",
            "Props/City/skyline_b",
            "Props/City/skyline_d",
            "Props/City/skyline_a",   // low, right edge
        };
        // Paired with the row above, in metres. Hand-set rather than randomised, because the whole point
        // is that the profile is composed.
        static readonly float[] SkylineHeights = { 22f, 34f, 58f, 72f, 54f, 36f, 27f, 20f };
        static readonly string[] Houses =
        {
            "Props/City/house_a", "Props/City/house_b", "Props/City/house_c",
            "Props/City/house_d", "Props/City/house_e", "Props/City/house_f",
        };
        static readonly string[] Cars =
        {
            "Props/City/car_sedan", "Props/City/car_suv", "Props/City/car_taxi",
            "Props/City/car_hatch", "Props/City/car_van",
        };

        // UNLIKE THE NATURE KIT, the city and car models are atlas-textured: every one has a single
        // material slot named "colormap" whose colour lives in a 512x512 texture, and the slot imports
        // with NO texture bound at all - so left alone they render as flat white blocks. The atlas is
        // therefore loaded here and pushed in through PropKit's palette, which also keeps it to ONE
        // material for the whole ring and so one batch. The three kits ship DIFFERENT atlases (verified
        // by hash), so a building must be painted with its own kit's texture or it samples the wrong
        // patch of the map.
        static Material Atlas(string name)
        {
            var tex = Resources.Load<Texture2D>("Props/Textures/" + name);
            return tex == null ? null : Make.MatTex(tex, 0.05f);
        }

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

            // A city on the horizon for every venue that HAS stands, including Arena, whose Surroundings
            // is None and which therefore had nothing outside the shell at all. Deliberately AFTER the
            // switch: the LCG draws for the venue's own dressing happen first, so adding this cannot
            // shift any existing layout.
            //
            // The beach is excluded on purpose. It is a pitch on an island ringed by open water, and a
            // skyline behind it would contradict the whole venue.
            if (!s.NoStands) Skyline(p);

            // Geography, beyond everything else. Last, so the draws it consumes cannot shift any
            // layout above it.
            Terrain(p, s);
        }

        // Distant land: rolling hills in the middle distance, and low islands instead at the beach.
        // Generated (see Landform for why they are not Kenney models), collider-free and static.
        //
        // AERIAL PERSPECTIVE IS DOING MOST OF THE WORK HERE. Distance in a landscape reads almost
        // entirely as loss of contrast toward the sky colour, not as size, so the hills are washed 32%
        // toward the venue's own sky colour. Without that wash they read as solid green cardboard
        // standing right behind the stands; with too much of it (0.45 was tried) they read as haze
        // rather than as ground.
        static void Terrain(Transform p, StadiumStyle s)
        {
            Color sky = s.Sky;

            if (s.NoStands)
            {
                // Sunset Beach: no inland range. A pitch on an island wants ISLANDS - low, far out,
                // sitting on the water with no snow on them.
                var isle = Make.Mat(Blend(new Color(0.30f, 0.34f, 0.28f), sky, 0.62f), 0.05f);
                for (int i = 0; i < 7; i++)
                {
                    float t = i / 7f + 0.031f;
                    Vector3 pos = RingPoint(t, Range(430f, 640f), 60f);
                    float h = Range(14f, 46f);
                    var mesh = Landform.Cone(7, h * Range(3.4f, 5.2f), h, h * 2f,   // snow above the peak = none
                                             0.34f, new Vector2(Range(-0.3f, 0.3f), Range(-0.3f, 0.3f)),
                                             (uint)(0x1518E + i * 977));
                    Landform.Place(p, "Island", pos, mesh, isle, null, Range(0f, 360f));
                }
                return;
            }

            // ---- middle distance: rolling hills, green but hazed toward the sky ----
            // Haze pulled back from 0.45 to 0.32. At 0.45 against this sky the green washed to a pale
            // desaturated teal that read as ATMOSPHERE rather than as ground - a flat band behind the
            // stand rather than land. 0.32 still separates the hills from the pitch without dissolving
            // them.
            //
            // A handful clustered behind the short end OPPOSITE the buildings, not a full ring around
            // the venue - Skyline sits at bearing 0 (+Z, behind the attacking goal; see its own doc
            // comment), so this cluster sits at bearing 0.5 (-Z) instead, on the same narrow arc width
            // Skyline uses for its own district. Both long sides and the buildings' own end now show a
            // clean horizon instead of land.
            var hillMat = Make.Mat(Blend(new Color(0.26f, 0.36f, 0.22f), sky, 0.32f), 0.05f);
            const int HillCount = 8;
            const float HillSpan = 0.16f;   // ~58 degrees, matching Skyline's ClusterSpan
            for (int i = 0; i < HillCount; i++)
            {
                float t = 0.5f + ((i / (float)(HillCount - 1)) - 0.5f) * HillSpan;
                Vector3 pos = RingPoint(t, Range(250f, 400f), 55f);
                float h = Range(26f, 62f);
                // FOOTPRINT MATTERS MORE THAN HEIGHT at this distance, and the first attempt got it
                // badly wrong: a radius of 4.2-6.5x height put the widest hill at 403 m across, sitting
                // 321 m away, so one hill subtended 103 degrees - a third of the horizon. That is why it
                // read as a flat green wall instead of a hill. 1.8-2.8x brings the widest to 174 m and
                // about 57 degrees, so several read as separate landforms across the same arc, which is
                // what makes them look like hills at all. More facets than a peak because a rounded
                // landform needs them, and they cost one triangle each.
                var mesh = Landform.Cone(9, h * Range(1.8f, 2.8f), h, h * 2f,
                                         0.30f, new Vector2(Range(-0.22f, 0.22f), Range(-0.22f, 0.22f)),
                                         (uint)(0x41115 + i * 631));
                Landform.Place(p, "Hill", pos, mesh, hillMat, null, Range(0f, 360f));
            }

            // NO FAR MOUNTAIN RANGE. There was one - 20 snow-capped peaks at 470-760 m of outset - and
            // it was removed on request. The hills stay, so the horizon still has land on it rather than
            // going straight from stand to sky.
            //
            // Kept rather than deleted: Landform still knows how to cut a snowline, and the sizing
            // lessons are recorded above on the hills, so restoring a range is a loop, not a rewrite.
        }

        // Wash a colour toward the sky. This is aerial perspective and it is the only reason distance
        // reads at all on flat-shaded geometry with no fog.
        static Color Blend(Color c, Color sky, float amount)
        {
            amount = Mathf.Clamp01(amount);
            return new Color(Mathf.Lerp(c.r, sky.r, amount),
                             Mathf.Lerp(c.g, sky.g, amount),
                             Mathf.Lerp(c.b, sky.b, amount), 1f);
        }

        /// <summary>
        /// A distant city, on ONE bearing. Eight buildings, one of each model.
        ///
        /// REPLACES A RING OF 26, which looked haphazard for three separate reasons, all of them the
        /// placement rather than the models:
        ///   1. DEPTH SCATTER. outset was Range(120, 260) with jitter 40, i.e. an effective 80..300 m
        ///      spread on every bearing. Neighbouring buildings sat 200 m apart in depth, so there was no
        ///      readable distance to the city at all.
        ///   2. RANDOM YAW. Each building took i * 29 degrees, so no two shared an orientation. Real
        ///      cities are built on a grid and read as one because their faces line up; rotating each
        ///      building individually is the single strongest "randomly scattered" cue there is.
        ///   3. REPETITION. 26 buildings out of 8 models is three copies of each, which the eye picks up
        ///      as duplicates precisely because they were scattered rather than grouped.
        ///
        /// So: one instance of each model, spread along a narrow arc at a nearly constant distance, all
        /// sharing a yaw, with the height profile composed by hand (see SkylineHeights).
        ///
        /// The bearing is +Z, behind the attacking goal. That is the direction the reel camera looks at
        /// for most of its lap, and the only end whose stand is low enough (7.95 m back wall, no roof) to
        /// see a horizon over. It also means three quarters of the lap has a clean empty horizon, which is
        /// correct: a city is a place you can see in one direction, not a wall around the ground.
        /// </summary>
        static void Skyline(Transform p)
        {
            var mat = Atlas("colormap_city");
            if (mat == null) return;        // no city models in this build; the venue is fine without one
            var paint = new PropKit.Paint[] { new PropKit.Paint("colormap", mat) };

            const float ClusterBearing = 0f;      // +Z, behind the attacking goal
            const float ClusterSpan    = 0.155f;  // ~56 degrees of arc for the whole district
            int n = SkylineRow.Length;

            // One shared facing, so the faces line up like a grid instead of pointing every which way.
            // Angled off the viewing axis rather than square to it, so the buildings show two faces and
            // read as solid rather than as flats.
            float districtYaw = 24f;

            for (int i = 0; i < n; i++)
            {
                float t = ClusterBearing + ((i / (float)(n - 1)) - 0.5f) * ClusterSpan;
                // Nearly constant depth: a tight band, with the centre of the district set slightly
                // further back so the tall middle does not tower over the low edges quite so hard.
                float depth = 205f + Mathf.Abs((i / (float)(n - 1)) - 0.5f) * -18f + Range(0f, 12f);
                Vector3 pos = RingPoint(t, depth, 0f);
                PropKit.Place(SkylineRow[i], pos, SkylineHeights[i], districtYaw + Range(-4f, 4f), p, paint);
            }
        }

        /// <summary>
        /// A ring of positions just outside the bowl, evenly spaced, jittered OUTWARD only.
        ///
        /// THIS USED TO WALK AN ELLIPSE, AND THAT WAS THE BUG BEHIND "TREES INSIDE THE STADIUM".
        /// The bowl is a RECTANGLE - four straight terraces, one per PitchLayout.AllSides - but the old
        /// version placed props on the ellipse inscribed in the same half-extents:
        ///
        ///     rx = BowlHalfX + outset + Range(-jitter, jitter)      // signed jitter, so also inward
        ///     pos = Center + (sin(ang) * rx, 0, cos(ang) * rz)
        ///
        /// An inscribed ellipse touches the rectangle only at the four axis points and dips inside it
        /// everywhere else, worst at 45 degrees where it cuts in by a factor of 1/sqrt(2). Measured on the
        /// Town Park ring: 20 of 60 trees stood inside the bowl rectangle and 9 stood inside a terrace
        /// band outright, at positions like (36.3, 0, 28.2) - which is in the side stand's seating.
        ///
        /// Worth being explicit about why this went unnoticed: testing containment with an ELLIPSE test
        /// reports zero, because the ellipse the props sit on is by construction inside the rectangle but
        /// outside the inscribed ellipse. The shape of the test has to match the shape of the bowl.
        ///
        /// So: project the direction out to the RECTANGLE boundary, and let jitter push outward only.
        /// A minimum outset now genuinely means minimum clearance from the structure on every bearing.
        /// </summary>
        static Vector3 RingPoint(float t, float outset, float jitter)
        {
            float ang = t * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));   // unit
            float rx = BowlHalfX + outset;
            float rz = BowlHalfZ + outset;
            // Distance along dir to the rectangle |x| <= rx, |z| <= rz: whichever axis saturates first.
            // dir is a unit vector so one of the two components is always well clear of zero.
            float reach = 1f / Mathf.Max(Mathf.Abs(dir.x) / rx, Mathf.Abs(dir.z) / rz);
            // Outward-only, along the same bearing, so jitter can never reduce clearance.
            return Center + dir * (reach + Range(0f, jitter));
        }

        // ---- Town Park: leafy trees + a few blocky houses ----
        static void BuildTrees(Transform p)
        {
            var trunk = Make.Mat(new Color(0.35f, 0.24f, 0.14f), 0.1f);
            var leaf  = Make.Mat(new Color(0.18f, 0.42f, 0.18f), 0.05f);
            // TWO MATERIALS FOR SIXTY TREES, shared deliberately. Every instance rebinds its slots to
            // these same two, so the lot collapses into the stadium's static batch (StadiumBuilder calls
            // Combine right after this returns). A material per tree would cost a draw call per tree.
            //
            // HEIGHTS AND OUTSETS ARE A SIGHTLINE FIX, not a style tweak. The complaint that trees looked
            // like they were "inside the stadium" was real but not a layout bug - measured live, ZERO
            // trees sat inside the bowl footprint. They were breaking the ROOFLINE: the stand's back wall
            // behind the attacking goal tops out at 7.95 m, the reel camera sits as low as 4.0 m, and that
            // sightline rises about 0.10 m per metre of depth, so anything over ~8.5 m at the near edge of
            // the ring showed above the wall from inside the ground.
            //
            // The old primitive tree was the culprit twice over. Its h was the TRUNK height with a canopy
            // sphere stacked on top, so h = 6 actually stood 10.2 m tall (measured: 10.1). PropKit's
            // height is TOTAL height, so the same sort of number is now honest, and the ring starts a
            // little further out as well. Max 8 m at a near edge of z = 40 stays under the 8.8 m ceiling
            // there, and the side sightline is slacker still (~9.1 m), so nothing crests the wall.
            for (int i = 0; i < 60; i++)
            {
                Vector3 pos = RingPoint(i / 60f, Range(6f, 18f), 4f);
                float h = Range(4.5f, 8f);
                // Yaw by index for the same determinism reason as the model choice. 37 degrees a step
                // walks all the way round without a short repeat.
                string model = ParkTrees[(i * 3 + i / ParkTrees.Length) % ParkTrees.Length];
                if (PropKit.PlaceTree(model, pos, h, i * 37f, p, leaf, trunk) == null)
                {
                    // No model in this build: fall back to the primitive. Its argument is TRUNK height and
                    // it builds ~1.7x that overall, so divide to land on the same total height.
                    Tree(p, pos, trunk, leaf, h / 1.7f);
                }
            }
            // Real houses instead of a box with a flatter box on top. Suburban kit, so its own atlas.
            var wall  = Make.Mat(new Color(0.80f, 0.76f, 0.68f), 0.1f);
            var roof  = Make.Mat(new Color(0.5f, 0.22f, 0.18f), 0.1f);
            var brick = Atlas("colormap_suburb");
            for (int i = 0; i < 14; i++)
            {
                Vector3 pos = RingPoint(i / 14f + 0.03f, Range(20f, 34f), 6f);
                float h = Range(6f, 9f);
                // Face the pitch, so a row of houses reads as a street backing onto the ground rather
                // than as boxes at arbitrary angles.
                Vector3 toC = Center - pos; toC.y = 0f;
                float faceYaw = Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg;
                string model = Houses[(i * 3 + i / Houses.Length) % Houses.Length];
                if (brick == null || PropKit.Place(model, pos, h, faceYaw, p,
                        new PropKit.Paint[] { new PropKit.Paint("colormap", brick) }) == null)
                    House(p, pos, wall, roof);
            }

            // A handful of cars parked up outside, which is most of what makes a small ground look used.
            var paint = Atlas("colormap_car");
            if (paint != null)
            {
                for (int i = 0; i < 9; i++)
                {
                    Vector3 pos = RingPoint(i / 9f + 0.07f, Range(17f, 20f), 2f);
                    Vector3 toC = Center - pos; toC.y = 0f;
                    // Parked broadside to the pitch, i.e. along the kerb, not nose-on.
                    float yaw = Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg + 90f;
                    string model = Cars[(i * 3 + i / Cars.Length) % Cars.Length];
                    PropKit.Place(model, pos, 1.5f, yaw, p,
                                  new PropKit.Paint[] { new PropKit.Paint("colormap", paint) });
                }
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

            // Palms dotted around the sand, now real models. This venue has NoStands, so there is no
            // roofline to break and nothing to hide behind - the palms are the silhouette of the whole
            // place, which is why they stay tall here (5-9 m) where the park trees were capped.
            for (int i = 0; i < 34; i++)
            {
                Vector3 pos = RingPoint(i / 34f, Range(4f, 20f), 6f);
                float h = Range(5f, 9f);
                string model = BeachPalms[(i * 3 + i / BeachPalms.Length) % BeachPalms.Length];
                if (PropKit.PlaceTree(model, pos, h, i * 53f, p, frond, trunk) == null)
                {
                    // The primitive palm's argument IS its trunk height and the fronds sit at the top, so
                    // it needs no correction factor the way the park tree does.
                    Palm(p, pos, trunk, frond, h);
                }
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
