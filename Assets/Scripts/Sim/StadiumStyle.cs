using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A selectable venue. One StadiumStyle drives everything that must agree across the
    /// build: the stand geometry (read by PitchLayout so seats + terraces line up), the
    /// StadiumBuilder colors/roof/surroundings, and the Crowd density + jersey palette.
    ///
    /// Pitch dimensions are NOT part of the style - the pitch is always regulation so
    /// gameplay is identical at every venue; only the bowl around it changes.
    /// </summary>
    public enum Surroundings { None, Trees, Track, Palms, Flags }

    public class StadiumStyle
    {
        public string Name;
        public string Blurb;

        // ---- Stands (read by PitchLayout) ----
        public int   StandRows = 22;
        public float RowRise = 0.9f;
        public float RowDepth = 1.1f;
        public float StandBaseHeight = 1.2f;
        public bool  HasRoof = true;
        // When true, the built stadium shell (terraces, walls, roof, corners, pylons) and the
        // crowd are SKIPPED entirely - an open venue defined only by its Surroundings (e.g. the
        // beach: water, sand, palms, chairs, tiki huts). The pitch itself is unchanged.
        public bool  NoStands = false;
        // False = authored but NOT offered on the picker yet. The entry STAYS in All: the wire byte
        // (MatchConfig.stadium) and any index carried over from an earlier build are raw All indices,
        // so deleting an entry would silently repoint them at a different venue. Same treatment as
        // SpeciesDef.ModelReady. The pickers skip these rows and show one Coming Soon card instead.
        public bool  Pickable = true;

        // ---- Colors ----
        public Color Grass    = new Color(0.24f, 0.42f, 0.24f);
        public Color Seats    = new Color(0.16f, 0.16f, 0.18f);   // terraces: dark grey by default
        public Color Concrete = new Color(0.55f, 0.55f, 0.58f);   // structure/back walls
        public Color Roof     = new Color(0.13f, 0.13f, 0.16f);
        public Color Accent   = new Color(0.15f, 0.45f, 0.90f);   // perimeter wall + nosing
        public Color Pylon    = new Color(0.28f, 0.28f, 0.31f);
        public Color Lamp     = new Color(1.00f, 0.97f, 0.85f);
        public Color Sky      = new Color(0.5f, 0.62f, 0.78f);    // fallback camera background

        // ---- Sky, sun, haze (read by SkyDome) ----
        // The sky is a photographed panorama out of Resources/Sky, not a gradient, so a venue's
        // mood is a choice of photo plus the angle its sun genuinely sits at. Tools/skyprep.py
        // measured that angle out of the pixels and SunEuler.x below is what it reported.
        //
        // SkyRotation yaws the sky until its sun lands on SunEuler.y. That is what lets each venue
        // keep the shadow direction it was tuned with, rather than every venue inheriting whatever
        // time of day the photographer happened to be standing in.
        //
        // SkyTint is a straight multiply over the photo, so white leaves it alone. SkyGround is
        // everything below the horizon: these panoramas are sky only, so it has to read as far-off
        // haze rather than as a dark hole under the stands.
        public string  SkyTex       = "Sky/kloofendal_48d_partly_cloudy_puresky";
        public float   SkyRotation  = 0f;
        public Color   SkyTint      = new Color(1.00f, 1.00f, 1.00f);
        public Color   SkyGround    = new Color(0.44f, 0.46f, 0.45f);
        public float   SkyExposure  = 1.00f;
        public Vector3 SunEuler     = new Vector3(52f, -35f, 0f);
        public Color   SunColor     = new Color(1.00f, 0.97f, 0.90f);
        public float   SunIntensity = 1.15f;
        public float   AmbientBoost = 1.00f;
        // How dark the sun's shadows land, 0 = none, 1 = Unity's default. A separate knob from
        // SunIntensity on purpose: intensity moves the lit side too, so dimming the sun to tame a
        // shadow costs the whole venue its brightness. This only touches the shadow.
        public float   ShadowStrength = 1.00f;
        public float   FogDensity   = 0.0022f;   // 0 = no haze
        public Color   FogColor     = new Color(0.64f, 0.72f, 0.82f);

        // ---- Crowd ----
        public int   MaxFans = 4000;
        public Color[] Jerseys;                 // partisan palette; null -> default set
        public int[]   SideHomeJersey;          // index per PitchLayout.Side; null -> default

        public Surroundings Surroundings = Surroundings.None;

        // ---- Catalog ----
        public static StadiumStyle[] All;
        // Backing store. Deliberately a literal: static field initializers run BEFORE the static
        // constructor body that fills All, so this must not reach the guard below.
        static int _selected = 0;

        /// <summary>
        /// The chosen venue, as an index into All. GUARDED ON WRITE, because the writes come from
        /// outside this screen's control: the host's MatchConfig.stadium byte off the wire
        /// (GameBootstrap.StartNetworkedMatch) can name a venue this build does not offer. Without
        /// the guard, Active's clamp turned that into "quietly build the last venue in the catalog",
        /// which reads as the host's pick being ignored. Land on the first offered venue instead.
        /// </summary>
        public static int SelectedIndex
        {
            get => _selected;
            set => _selected = CanPick(value) ? value : FirstPickable;
        }

        /// <summary>True when this index names a venue the pickers may offer.</summary>
        public static bool CanPick(int i) => i >= 0 && i < All.Length && All[i].Pickable;

        /// <summary>First offered venue, the fallback for a stale or wire-received index.</summary>
        public static int FirstPickable
        {
            get
            {
                for (int i = 0; i < All.Length; i++) if (All[i].Pickable) return i;
                return 0;
            }
        }

        /// <summary>All indices the pickers may offer, in catalog order. Values, not positions:
        /// the wire byte is an All index, so the picker has to carry the real index.</summary>
        public static int[] PickableIndices()
        {
            int n = 0;
            for (int i = 0; i < All.Length; i++) if (All[i].Pickable) n++;
            var idx = new int[n];
            n = 0;
            for (int i = 0; i < All.Length; i++) if (All[i].Pickable) idx[n++] = i;
            return idx;
        }
        public static StadiumStyle Active => All[Mathf.Clamp(SelectedIndex, 0, All.Length - 1)];

        static StadiumStyle()
        {
            var brightKit = new[]
            {
                new Color(0.75f, 0.15f, 0.15f), new Color(0.15f, 0.30f, 0.75f),
                new Color(0.90f, 0.90f, 0.92f), new Color(0.85f, 0.75f, 0.20f),
                new Color(0.20f, 0.55f, 0.25f), new Color(0.85f, 0.45f, 0.15f),
            };

            All = new[]
            {
                // 1. Town Park - small, low open stands, trees + houses, modest crowd.
                new StadiumStyle
                {
                    Name = "Town Park", Blurb = "Small park ground. Low terraces.",
                    StandRows = 6, RowRise = 0.75f, RowDepth = 1.2f, HasRoof = false,
                    Grass = new Color(0.15f, 0.30f, 0.15f),
                    Seats = new Color(0.20f, 0.20f, 0.22f),
                    Concrete = new Color(0.62f, 0.60f, 0.55f),
                    Accent = new Color(0.20f, 0.55f, 0.30f),
                    Sky = new Color(0.55f, 0.68f, 0.82f),
                    // Bright partly-cloudy blue. Its sun is at 47.9 degrees, which is where this
                    // venue was already lit from, so only the yaw needed solving.
                    SkyTex = "Sky/kloofendal_48d_partly_cloudy_puresky", SkyRotation = 269.3f,
                    SkyExposure = 1.00f, SkyGround = new Color(0.46f, 0.48f, 0.46f),
                    SunEuler = new Vector3(48f, -35f, 0f),
                    FogDensity = 0.0020f, FogColor = new Color(0.68f, 0.75f, 0.84f),
                    MaxFans = 700, Surroundings = Surroundings.Trees,
                    Jerseys = brightKit, SideHomeJersey = new[] { 4, 2, 3, 0 },
                },
                // 2. Olympic Stadium - huge tiered bowl, full roof, running track ring.
                new StadiumStyle
                {
                    Name = "Olympic Stadium", Blurb = "Roofed bowl with a running track.",
                    Pickable = false,
                    StandRows = 34, RowRise = 0.95f, RowDepth = 1.05f, HasRoof = true,
                    Grass = new Color(0.13f, 0.27f, 0.14f),
                    Seats = new Color(0.13f, 0.13f, 0.15f),
                    Concrete = new Color(0.72f, 0.72f, 0.74f),
                    Roof = new Color(0.16f, 0.16f, 0.20f),
                    Accent = new Color(0.90f, 0.70f, 0.15f),
                    Sky = new Color(0.5f, 0.62f, 0.80f),
                    // Hazy near-white noon. Held back below 1 so it reads as bright rather than
                    // blown, and so the sky-driven ambient does not flatten the players out.
                    SkyTex = "Sky/qwantani_noon_puresky", SkyRotation = 281.2f,
                    SkyExposure = 0.86f, SkyTint = new Color(0.94f, 0.97f, 1.00f),
                    SkyGround = new Color(0.48f, 0.50f, 0.52f),
                    SunEuler = new Vector3(50f, -25f, 0f), SunIntensity = 1.25f,
                    FogDensity = 0.0015f, FogColor = new Color(0.62f, 0.72f, 0.86f),
                    MaxFans = 6000, Surroundings = Surroundings.Track,
                    Jerseys = brightKit, SideHomeJersey = new[] { 3, 1, 0, 2 },
                },
                // 3. Arena - modern enclosed, tall steep stands, bright accent, packed.
                new StadiumStyle
                {
                    Name = "Arena", Blurb = "Steep enclosed stands. Loud.",
                    Pickable = false,
                    StandRows = 28, RowRise = 1.05f, RowDepth = 0.95f, HasRoof = true,
                    Grass = new Color(0.12f, 0.28f, 0.14f),
                    Seats = new Color(0.10f, 0.10f, 0.12f),
                    Concrete = new Color(0.30f, 0.32f, 0.38f),
                    Roof = new Color(0.08f, 0.08f, 0.10f),
                    Accent = new Color(0.90f, 0.20f, 0.30f),
                    Pylon = new Color(0.20f, 0.20f, 0.24f),
                    Sky = new Color(0.40f, 0.50f, 0.62f),
                    // Flat overcast, which is the right sky for a closed roofed bowl and matches
                    // the cool light this venue was already using.
                    SkyTex = "Sky/kloofendal_overcast_puresky", SkyRotation = 181.1f,
                    SkyExposure = 0.94f, SkyTint = new Color(0.94f, 0.96f, 1.00f),
                    SkyGround = new Color(0.40f, 0.42f, 0.45f),
                    SunEuler = new Vector3(22f, -120f, 0f), SunColor = new Color(0.92f, 0.94f, 1.00f),
                    SunIntensity = 0.95f, AmbientBoost = 1.10f,
                    FogDensity = 0.0034f, FogColor = new Color(0.50f, 0.55f, 0.62f),
                    MaxFans = 5500, Surroundings = Surroundings.None,
                    Jerseys = brightKit, SideHomeJersey = new[] { 0, 0, 2, 2 },
                },
                // 4. Sunset Beach - OPEN seaside: no stands or crowd, just sand, sea, palms,
                // beach chairs, tiki huts. StandRows kept small only for the layout math that
                // still reads them (surroundings outset); nothing stand-shaped is built.
                new StadiumStyle
                {
                    Name = "Sunset Beach", Blurb = "Seaside pitch. Sand and palms.",
                    StandRows = 8, RowRise = 0.7f, RowDepth = 1.25f, HasRoof = false, NoStands = true,
                    Grass = new Color(0.18f, 0.34f, 0.17f),
                    Seats = new Color(0.22f, 0.20f, 0.20f),
                    Concrete = new Color(0.80f, 0.74f, 0.60f),
                    Accent = new Color(0.95f, 0.55f, 0.25f),
                    Sky = new Color(0.98f, 0.72f, 0.45f),   // warm sunset
                    SkyTex = "Sky/qwantani_sunset_puresky", SkyRotation = 101.0f,
                    SkyExposure = 1.05f, SkyTint = new Color(1.00f, 0.96f, 0.92f),
                    SkyGround = new Color(0.40f, 0.33f, 0.30f),
                    // The photo's own sun measures as almost pure orange, which the pitch would not
                    // survive, so the light keeps its hand-picked colour: warm, still readable.
                    //
                    // WHY THE SUN IS NOT AT THE PHOTO'S OWN ELEVATION. skyprep measures this sky's
                    // sun at 2.7 degrees and the pitch used to be lit from 6, which is honest and
                    // unplayable: shadow length is cot(elevation), so 6 degrees stretches a 1.8m
                    // player into a 17m streak and Ultra's 150m shadow distance draws every metre of
                    // it, so a handful of bodies covers the playable half of the pitch. 18 degrees
                    // costs 3.1x instead of 9.5x and still rakes low enough to read as evening.
                    //
                    // The yaw is untouched, so SkyRotation above stays correct - shadows still run
                    // the direction the sky's sun says they should, just not to the touchline.
                    SunEuler = new Vector3(18f, 155f, 0f), SunColor = new Color(1.00f, 0.70f, 0.42f),
                    SunIntensity = 1.30f, AmbientBoost = 1.15f, ShadowStrength = 0.72f,
                    FogDensity = 0.0030f, FogColor = new Color(0.98f, 0.70f, 0.48f),
                    MaxFans = 0, Surroundings = Surroundings.Palms,
                    Jerseys = brightKit, SideHomeJersey = new[] { 5, 2, 3, 1 },
                },
                // 5. National Stadium - classic big two-tier feel, flags + statues.
                new StadiumStyle
                {
                    Name = "National Stadium", Blurb = "Big steep tiers. Flags and statues.",
                    Pickable = false,
                    StandRows = 30, RowRise = 1.0f, RowDepth = 1.0f, HasRoof = true,
                    Grass = new Color(0.14f, 0.29f, 0.15f),
                    Seats = new Color(0.12f, 0.12f, 0.14f),
                    Concrete = new Color(0.66f, 0.64f, 0.62f),
                    Roof = new Color(0.18f, 0.16f, 0.14f),
                    Accent = new Color(0.80f, 0.15f, 0.20f),
                    Sky = new Color(0.52f, 0.62f, 0.76f),
                    // Warm late afternoon: long shadows across the pitch without going full sunset.
                    SkyTex = "Sky/qwantani_late_afternoon_puresky", SkyRotation = 244.1f,
                    SkyExposure = 1.00f, SkyTint = new Color(1.00f, 0.99f, 0.96f),
                    SkyGround = new Color(0.44f, 0.44f, 0.42f),
                    SunEuler = new Vector3(19f, -62f, 0f), SunColor = new Color(1.00f, 0.93f, 0.80f),
                    SunIntensity = 1.20f,
                    FogDensity = 0.0024f, FogColor = new Color(0.70f, 0.72f, 0.80f),
                    MaxFans = 5000, Surroundings = Surroundings.Flags,
                    Jerseys = brightKit, SideHomeJersey = new[] { 0, 2, 0, 2 },
                },
            };
        }
    }
}
