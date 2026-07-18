using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The customized player: height, weight, jersey art, name and number, plus the
    /// derived TRAIT multipliers those physical attributes produce. One static Active
    /// profile is read by the ragdoll builder (scale + mass + jersey) and by the
    /// movement / jump / shot / push code (trait multipliers), so a build the player
    /// dials in on the Customize screen is reflected everywhere.
    ///
    /// Trait philosophy: REALISTIC TRADEOFFS - every build is viable.
    ///   Tall + heavy  -> more mass, harder shot, stronger push, higher reach,
    ///                    but slower acceleration/sprint and lower jump.
    ///   Short + light -> faster, more agile, higher jump,
    ///                    but weaker shot/push and easily shoved off the ball.
    /// The two axes are separated: HEIGHT mainly drives reach/leverage + a mild
    /// speed/jump cost; WEIGHT drives mass/power/push + the bigger agility cost.
    /// </summary>
    public static class PlayerProfile
    {
        // ---- Raw attributes (the sliders) ----
        // Height in metres (default 1.80). Range covers a short-and-nippy to a
        // tall-target-man build.
        public const float MinHeight = 1.60f, MaxHeight = 2.05f, DefaultHeight = 1.80f;
        // Weight in kg (default 75). Light winger to heavy powerhouse.
        public const float MinWeight = 55f, MaxWeight = 110f, DefaultWeight = 75f;

        public static float Height = DefaultHeight;
        public static float Weight = DefaultWeight;

        // ---- Identity ----
        public static string PlayerName = "PLAYER";
        public static int Number = 10;

        // Strong foot: shots off the strong-side leg/foot get full accuracy; the weak
        // side gets half. (Head is governed by heading rules; body contacts are weak.)
        public static bool LeftFooted = false;

        // ---- Jersey art (painted on the 2D canvas; applied to the torso material) ----
        public static Texture2D JerseyTex;      // null -> plain team colour
        public static Color JerseyBase = new Color(0.2f, 0.45f, 0.85f);

        // ---- Normalized positions on each axis (0 = min, 1 = max) ----
        public static float HeightT => Mathf.InverseLerp(MinHeight, MaxHeight, Height);
        public static float WeightT => Mathf.InverseLerp(MinWeight, MaxWeight, Weight);

        // Physical scale factors for the ragdoll geometry.
        public static float HeightScale => Height / DefaultHeight;                 // vertical
        // Girth from weight, but partly discounted by height (a tall heavy player is
        // lean, a short heavy player is stocky). Kept in a sane visual band.
        public static float GirthScale
        {
            get
            {
                float bmiIsh = Weight / (HeightScale * HeightScale); // weight adjusted for frame
                float t = Mathf.InverseLerp(MinWeight, MaxWeight, bmiIsh);
                return Mathf.Lerp(0.82f, 1.35f, t);
            }
        }

        // Mass multiplier vs the default build (drives push resistance + shot inertia).
        public static float MassMul => Weight / DefaultWeight;

        // ---- Derived TRAIT multipliers (1.0 = default build) ----
        // Lighter = quicker off the mark; heavier = sluggish. Height adds a mild cost.
        public static float MoveSpeedMul =>
            Mathf.Clamp(1f + (0.5f - WeightT) * 0.30f + (0.5f - HeightT) * 0.10f, 0.75f, 1.25f);

        // Sprint is hit harder by weight (big players top out slower).
        public static float SprintSpeedMul =>
            Mathf.Clamp(1f + (0.5f - WeightT) * 0.40f + (0.5f - HeightT) * 0.12f, 0.7f, 1.3f);

        // Jump: light + short leap highest; heavy + tall lowest.
        public static float JumpMul =>
            Mathf.Clamp(1f + (0.5f - WeightT) * 0.45f + (0.5f - HeightT) * 0.18f, 0.65f, 1.35f);

        // Shot/header power: mass + a little height leverage make the ball fly faster.
        public static float ShotPowerMul =>
            Mathf.Clamp(1f + (WeightT - 0.5f) * 0.45f + (HeightT - 0.5f) * 0.15f, 0.75f, 1.35f);

        // Push strength when bodies collide: dominated by mass/weight.
        public static float PushMul =>
            Mathf.Clamp(1f + (WeightT - 0.5f) * 0.6f + (HeightT - 0.5f) * 0.2f, 0.7f, 1.5f);

        // Reach (arms/legs/head extension for headers + tackles): height.
        public static float ReachMul =>
            Mathf.Clamp(1f + (HeightT - 0.5f) * 0.35f, 0.85f, 1.2f);

        public static void ResetToDefault()
        {
            Height = DefaultHeight;
            Weight = DefaultWeight;
            PlayerName = "PLAYER";
            Number = 10;
            LeftFooted = false;
            JerseyTex = null;
        }
    }
}
