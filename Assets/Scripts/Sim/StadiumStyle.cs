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

        // ---- Colors ----
        public Color Grass    = new Color(0.24f, 0.42f, 0.24f);
        public Color Seats    = new Color(0.16f, 0.16f, 0.18f);   // terraces: dark grey by default
        public Color Concrete = new Color(0.55f, 0.55f, 0.58f);   // structure/back walls
        public Color Roof     = new Color(0.13f, 0.13f, 0.16f);
        public Color Accent   = new Color(0.15f, 0.45f, 0.90f);   // perimeter wall + nosing
        public Color Pylon    = new Color(0.28f, 0.28f, 0.31f);
        public Color Lamp     = new Color(1.00f, 0.97f, 0.85f);
        public Color Sky      = new Color(0.5f, 0.62f, 0.78f);    // camera background

        // ---- Crowd ----
        public int   MaxFans = 4000;
        public Color[] Jerseys;                 // partisan palette; null -> default set
        public int[]   SideHomeJersey;          // index per PitchLayout.Side; null -> default

        public Surroundings Surroundings = Surroundings.None;

        // ---- Catalog ----
        public static StadiumStyle[] All;
        public static int SelectedIndex = 0;
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
                    Name = "Town Park", Blurb = "Small community ground. Low open terraces, trees and houses beyond.",
                    StandRows = 6, RowRise = 0.75f, RowDepth = 1.2f, HasRoof = false,
                    Grass = new Color(0.26f, 0.46f, 0.24f),
                    Seats = new Color(0.20f, 0.20f, 0.22f),
                    Concrete = new Color(0.62f, 0.60f, 0.55f),
                    Accent = new Color(0.20f, 0.55f, 0.30f),
                    Sky = new Color(0.55f, 0.68f, 0.82f),
                    MaxFans = 700, Surroundings = Surroundings.Trees,
                    Jerseys = brightKit, SideHomeJersey = new[] { 4, 2, 3, 0 },
                },
                // 2. Olympic Stadium - huge tiered bowl, full roof, running track ring.
                new StadiumStyle
                {
                    Name = "Olympic Stadium", Blurb = "Vast tiered bowl with a full roof and a running track ringing the pitch.",
                    StandRows = 34, RowRise = 0.95f, RowDepth = 1.05f, HasRoof = true,
                    Grass = new Color(0.22f, 0.40f, 0.22f),
                    Seats = new Color(0.13f, 0.13f, 0.15f),
                    Concrete = new Color(0.72f, 0.72f, 0.74f),
                    Roof = new Color(0.16f, 0.16f, 0.20f),
                    Accent = new Color(0.90f, 0.70f, 0.15f),
                    Sky = new Color(0.5f, 0.62f, 0.80f),
                    MaxFans = 6000, Surroundings = Surroundings.Track,
                    Jerseys = brightKit, SideHomeJersey = new[] { 3, 1, 0, 2 },
                },
                // 3. Arena - modern enclosed, tall steep stands, bright accent, packed.
                new StadiumStyle
                {
                    Name = "Arena", Blurb = "Modern enclosed arena. Tall, steep stands right on the pitch, packed and loud.",
                    StandRows = 28, RowRise = 1.05f, RowDepth = 0.95f, HasRoof = true,
                    Grass = new Color(0.20f, 0.44f, 0.22f),
                    Seats = new Color(0.10f, 0.10f, 0.12f),
                    Concrete = new Color(0.30f, 0.32f, 0.38f),
                    Roof = new Color(0.08f, 0.08f, 0.10f),
                    Accent = new Color(0.90f, 0.20f, 0.30f),
                    Pylon = new Color(0.20f, 0.20f, 0.24f),
                    Sky = new Color(0.40f, 0.50f, 0.62f),
                    MaxFans = 5500, Surroundings = Surroundings.None,
                    Jerseys = brightKit, SideHomeJersey = new[] { 0, 0, 2, 2 },
                },
                // 4. Sunset Beach - open seaside, small stands, palms + sand.
                new StadiumStyle
                {
                    Name = "Sunset Beach", Blurb = "Open seaside pitch. Small stands, palm trees and sand all around.",
                    StandRows = 8, RowRise = 0.7f, RowDepth = 1.25f, HasRoof = false,
                    Grass = new Color(0.30f, 0.50f, 0.26f),
                    Seats = new Color(0.22f, 0.20f, 0.20f),
                    Concrete = new Color(0.80f, 0.74f, 0.60f),
                    Accent = new Color(0.95f, 0.55f, 0.25f),
                    Sky = new Color(0.98f, 0.72f, 0.45f),   // warm sunset
                    MaxFans = 1100, Surroundings = Surroundings.Palms,
                    Jerseys = brightKit, SideHomeJersey = new[] { 5, 2, 3, 1 },
                },
                // 5. National Stadium - classic big two-tier feel, flags + statues.
                new StadiumStyle
                {
                    Name = "National Stadium", Blurb = "Classic national ground. Big steep tiers, flags flying, statues outside.",
                    StandRows = 30, RowRise = 1.0f, RowDepth = 1.0f, HasRoof = true,
                    Grass = new Color(0.23f, 0.42f, 0.23f),
                    Seats = new Color(0.12f, 0.12f, 0.14f),
                    Concrete = new Color(0.66f, 0.64f, 0.62f),
                    Roof = new Color(0.18f, 0.16f, 0.14f),
                    Accent = new Color(0.80f, 0.15f, 0.20f),
                    Sky = new Color(0.52f, 0.62f, 0.76f),
                    MaxFans = 5000, Surroundings = Surroundings.Flags,
                    Jerseys = brightKit, SideHomeJersey = new[] { 0, 2, 0, 2 },
                },
            };
        }
    }
}
