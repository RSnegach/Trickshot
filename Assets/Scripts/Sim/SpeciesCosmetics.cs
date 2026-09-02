using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The option lists behind each species' appearance tabs, plus the preset swatch row for its
    /// skin / coat / hide. One lookup per (species, slot) so CustomizeUI has exactly one cosmetic
    /// sub-menu instead of one per species.
    ///
    /// HUMAN slots delegate to the real Cosmetics catalogs (Hair / Facial / Accessories), so
    /// nothing about the human path changes: same counts, same labels, same headgear rule.
    ///
    /// ANIMAL slots DRAW. Each name here is a GATE on the species' decor table: BodyLayout.DecorSpec
    /// carries a (Gate, GateMask) pair and ActiveRagdoll.AddDecor skips any piece whose bit is clear
    /// for the live style index. So the index the player picks on this screen is the index the body
    /// builds from, and these arrays are the contract between the two. Reordering an array silently
    /// repaints every saved profile, so append rather than insert.
    ///
    /// The HORSE's MANE slot is the exception: it delegates to the HUMAN hair catalog (see
    /// UsesHumanHair) and is drawn by Cosmetics.AttachAppearance as real simulated hair cards on the
    /// neck crest, not by a decor box. Same styles, same atlas, same colour.
    /// </summary>
    public static class SpeciesCosmetics
    {
        // ---- Option counts / labels ----

        /// <summary>
        /// True when this (species, slot) reads the HUMAN hair catalog instead of a species list.
        ///
        /// The horse's MANE is human hair: the same styles, the same cards, the same atlas, just
        /// anchored on the neck crest rather than the crown (see Cosmetics.AttachAppearance). It has
        /// to route through here rather than through a parallel horse list, because the index this
        /// screen hands out is the index that indexes Cosmetics.Hair when the body builds. Two lists
        /// of different lengths would put a picked style and a drawn style out of step.
        ///
        /// The visible cost, accepted: the MANE tab shows human style names ("L: Ponytail").
        /// </summary>
        static bool UsesHumanHair(byte species, SlotKind kind)
            => kind == SlotKind.StyleA && species == Species.HumanId;

        public static int Count(byte species, SlotKind kind)
        {
            if (UsesHumanHair(species, kind)) return Cosmetics.Hair.Count;
            if (species == Species.HumanId)
            {
                switch (kind)
                {
                    case SlotKind.StyleB: return Cosmetics.Facial.Count;
                    case SlotKind.StyleC: return Cosmetics.Accessories.Count;
                }
                return 0;
            }
            var list = List(species, kind);
            return list == null ? 0 : list.Length;
        }

        public static string Label(byte species, SlotKind kind, int i)
        {
            // Keep the existing human hair label exactly: group initial, then the name. The horse
            // shares it, because it shares the catalog.
            if (UsesHumanHair(species, kind))
                return i >= 0 && i < Cosmetics.Hair.Count
                     ? Cosmetics.Hair[i].Group.ToString()[0] + ": " + Cosmetics.Hair[i].Name : "?";
            if (species == Species.HumanId)
            {
                switch (kind)
                {
                    case SlotKind.StyleB:
                        return i >= 0 && i < Cosmetics.Facial.Count ? Cosmetics.Facial[i].Name : "?";
                    case SlotKind.StyleC:
                        return i >= 0 && i < Cosmetics.Accessories.Count ? Cosmetics.Accessories[i].Name : "?";
                }
                return "?";
            }
            var list = List(species, kind);
            return list != null && i >= 0 && i < list.Length ? list[i] : "?";
        }

        /// <summary>
        /// Whether an option can be picked. Only the human path has a rule (headgear needs bald
        /// hair, see Cosmetics.AttachAppearance); animals accept everything.
        /// </summary>
        public static bool Enabled(byte species, SlotKind kind, int i)
        {
            if (species == Species.HumanId && kind == SlotKind.StyleC)
                return i == 0
                    || !Cosmetics.Accessories[i].Headgear
                    || Cosmetics.IsBald(PlayerProfile.Appearance.HairStyle);
            return true;
        }

        /// <summary>True when this species' StyleC list has an entry that conflicts with hair.</summary>
        public static bool HasHeadgearRule(byte species) => species == Species.HumanId;

        static string[] List(byte species, SlotKind kind)
        {
            switch (species)
            {
                case 1: // Horse
                    if (kind == SlotKind.StyleA) return Cosmetics.ManeNames;
                    if (kind == SlotKind.StyleB) return _horseMarkings;
                    if (kind == SlotKind.StyleC) return _horseTack;
                    break;
                case 2: // Elephant
                    if (kind == SlotKind.StyleA) return _elephantEars;
                    if (kind == SlotKind.StyleB) return _elephantTusks;
                    if (kind == SlotKind.StyleC) return _elephantTack;
                    break;
                case 3: // Gorilla
                    if (kind == SlotKind.StyleA) return _gorillaCrest;
                    if (kind == SlotKind.StyleB) return _gorillaFace;
                    if (kind == SlotKind.StyleC) return _gorillaExtras;
                    break;
                case 4: // Ostrich
                    if (kind == SlotKind.StyleA) return _ostrichNeck;
                    if (kind == SlotKind.StyleB) return _ostrichBeak;
                    if (kind == SlotKind.StyleC) return _ostrichExtras;
                    break;
            }
            return null;
        }

        // Index 0 is the unadorned option, so a fresh species reads plain and the clamp in
        // Species.ApplySelection has a safe landing index. It is named "None" everywhere it means
        // "nothing drawn", and "Plain" where the part is anatomy that always draws (ears).
        //
        // The index is the GateMask bit in the species decor table (BodyLayout), so these arrays and
        // that table have to move together. There is no horse MANE array: it delegates to the human
        // hair catalog, see UsesHumanHair.
        static readonly string[] _horseMarkings  = { "None", "Star", "Blaze", "Snip", "Stockings", "Dappled" };
        static readonly string[] _horseTack      = { "None", "Bridle", "Halter", "Blinkers", "Saddle Pad" };

        // "Plain", not "None": an earless elephant reads as a broken model, so index 0 still draws
        // the default ear. "None" survives on the tusks, which plenty of real elephants lack.
        static readonly string[] _elephantEars   = { "Plain", "Notched", "Wide", "Torn" };
        static readonly string[] _elephantTusks  = { "None", "Short", "Curved", "Long", "Banded" };
        // "Painted" was dropped for "Blanket": the Hide picker already recolours the whole animal, so
        // a paint option was the same result by a second route. The count stays 4, so no saved index
        // shifts and Species.ApplySelection's clamp is unaffected.
        static readonly string[] _elephantTack   = { "None", "Head Cloth", "Ankle Bands", "Blanket" };

        static readonly string[] _gorillaCrest   = { "None", "Low", "Silverback", "Ridge" };
        static readonly string[] _gorillaFace    = { "None", "Scarred", "Pale Muzzle", "Grey Brow" };
        static readonly string[] _gorillaExtras  = { "None", "Chest Strap", "Wristbands", "Headband" };

        static readonly string[] _ostrichNeck    = { "None", "Bare", "Plumed", "Ruffed" };
        static readonly string[] _ostrichBeak    = { "None", "Short", "Hooked", "Broad" };
        static readonly string[] _ostrichExtras  = { "None", "Leg Bands", "Tail Fan", "Collar" };

        // ---- Skin / coat / hide presets ----
        // Twelve to sixteen entries per species, ordered light to dark, laid out eight per row by
        // the Skin sub-menu. The colour wheel below the swatches still allows anything, so these are
        // shortcuts. Every pair of entries differs by more than CustomizeUI.ApproxColor's tolerance
        // (0.04 per channel) on at least one channel, or two swatches would light up for one pick.

        /// <summary>
        /// The three slot colours a species starts with when the player switches TO it: a horse's mane
        /// dark, its markings white and its tack brown; an elephant's tusks ivory and its tack red.
        /// Humans keep whatever they had. Called from Species.ApplySelection on a change only. Each
        /// seed is also the first entry of that slot's SlotSwatches list, so it reads as picked.
        /// </summary>
        public static void SeedStyleColors(byte species, ref PlayerAppearance a)
        {
            switch (species)
            {
                case 1:
                    a.HairColor = new Color(0.08f, 0.06f, 0.05f);
                    a.FacialColor = new Color(0.95f, 0.94f, 0.90f);
                    a.AccessoryColor = new Color(0.32f, 0.20f, 0.10f);
                    break;
                case 2:
                    a.HairColor = new Color(0.46f, 0.46f, 0.48f);
                    a.FacialColor = new Color(0.93f, 0.90f, 0.80f);
                    a.AccessoryColor = new Color(0.70f, 0.12f, 0.10f);
                    break;
            }
        }

        public static string SkinGroupLabel(byte species)
        {
            switch (species)
            {
                case 1: return "Coat";
                case 2: return "Hide";
                case 3: return "Fur";
                case 4: return "Plumage";
                default: return "Human";
            }
        }

        public static Color[] SkinSwatches(byte species)
        {
            switch (species)
            {
                case 1: return _horseCoats;
                case 2: return _elephantHides;
                case 3: return _gorillaFur;
                case 4: return _ostrichPlumage;
                default: return _humanSkins;
            }
        }

        /// <summary>
        /// Preset swatches for a STYLE slot's colour (hair, mane, tusks, tack ...), drawn above that
        /// slot's colour wheel. Human hair and facial hair share one natural-hair list; the horse's
        /// mane has its own, narrower one (no dye). Everything else is the material the slot is made
        /// of: leather for horse tack, ivory for tusks, cloth for an elephant's blanket, hide for its
        /// ears, fur for a gorilla's crest. The loose accessory slots get a general accent palette.
        /// Never null: the Skin kind returns SkinSwatches so a caller can treat every slot alike.
        /// </summary>
        public static Color[] SlotSwatches(byte species, SlotKind kind)
        {
            if (kind == SlotKind.Skin) return SkinSwatches(species);
            switch (species)
            {
                case 1: // Horse: mane, markings, tack
                    return kind == SlotKind.StyleA ? _maneColors
                         : kind == SlotKind.StyleB ? _markingColors : _leathers;
                case 2: // Elephant: ear tint, tusks, cloth
                    return kind == SlotKind.StyleA ? _elephantHides
                         : kind == SlotKind.StyleB ? _ivories : _cloths;
                case 3: // Gorilla: crest fur, face skin, accessories
                    return kind == SlotKind.StyleA ? _gorillaFur
                         : kind == SlotKind.StyleB ? _gorillaFaces : _accents;
                case 4: // Ostrich: neck skin, beak horn, accessories
                    return kind == SlotKind.StyleA ? _ostrichNecks
                         : kind == SlotKind.StyleB ? _beakHorns : _accents;
                default: // Human: hair, facial hair, accessories
                    return kind == SlotKind.StyleC ? _accents : _hairColors;
            }
        }

        // Sixteen tones, very pale to very deep. The original eight are still in here (every other
        // entry from the second), so a saved skin keeps its lit swatch; the new ones sit between
        // them and vary the undertone a little (a pink fair, an olive light) instead of one warm ramp.
        static readonly Color[] _humanSkins =
        {
            new Color(0.99f, 0.91f, 0.85f),   // porcelain
            new Color(0.98f, 0.85f, 0.75f),   // pale
            new Color(0.96f, 0.80f, 0.72f),   // fair, pink undertone
            new Color(0.94f, 0.78f, 0.66f),   // fair
            new Color(0.90f, 0.74f, 0.60f),   // light
            new Color(0.87f, 0.69f, 0.55f),   // light, warm
            new Color(0.82f, 0.66f, 0.50f),   // light olive
            new Color(0.80f, 0.61f, 0.46f),   // medium light
            new Color(0.74f, 0.55f, 0.40f),   // medium
            new Color(0.68f, 0.49f, 0.35f),   // medium tan
            new Color(0.61f, 0.43f, 0.30f),   // tan
            new Color(0.55f, 0.38f, 0.26f),   // brown
            new Color(0.48f, 0.33f, 0.22f),   // deep tan
            new Color(0.42f, 0.28f, 0.19f),   // deep brown
            new Color(0.30f, 0.20f, 0.14f),   // dark
            new Color(0.21f, 0.14f, 0.10f),   // deepest
        };

        // Sixteen coats, light to dark: grey, cream, palomino, dun, dapple grey, roan, buckskin,
        // sorrel, chestnut, dark grey, red bay, bay, liver, dark bay, smoky black, black.
        static readonly Color[] _horseCoats =
        {
            new Color(0.92f, 0.91f, 0.89f),   // grey (white)
            new Color(0.92f, 0.88f, 0.79f),   // cream
            new Color(0.83f, 0.68f, 0.38f),   // palomino
            new Color(0.72f, 0.60f, 0.42f),   // dun
            new Color(0.62f, 0.61f, 0.60f),   // dapple grey
            new Color(0.66f, 0.58f, 0.54f),   // strawberry roan
            new Color(0.74f, 0.58f, 0.34f),   // buckskin
            new Color(0.72f, 0.42f, 0.22f),   // sorrel
            new Color(0.58f, 0.31f, 0.15f),   // chestnut
            new Color(0.34f, 0.33f, 0.32f),   // dark grey
            new Color(0.52f, 0.26f, 0.12f),   // red bay
            new Color(0.42f, 0.24f, 0.12f),   // bay
            new Color(0.33f, 0.18f, 0.11f),   // liver chestnut
            new Color(0.26f, 0.17f, 0.11f),   // dark bay
            new Color(0.18f, 0.15f, 0.13f),   // smoky black
            new Color(0.12f, 0.10f, 0.09f),   // black
        };

        // Twelve hides, light to dark, greys with the warm, red-dust and blue casts real herds carry.
        static readonly Color[] _elephantHides =
        {
            new Color(0.74f, 0.64f, 0.62f),   // albino pink
            new Color(0.66f, 0.65f, 0.64f),   // pale ash
            new Color(0.58f, 0.55f, 0.50f),   // dusty tan
            new Color(0.52f, 0.50f, 0.47f),   // warm grey
            new Color(0.52f, 0.41f, 0.34f),   // red dust (Tsavo)
            new Color(0.46f, 0.46f, 0.48f),   // grey
            new Color(0.44f, 0.40f, 0.36f),   // mud
            new Color(0.40f, 0.42f, 0.47f),   // blue grey
            new Color(0.39f, 0.39f, 0.41f),   // slate
            new Color(0.38f, 0.34f, 0.31f),   // brown grey
            new Color(0.33f, 0.33f, 0.35f),   // dark grey
            new Color(0.28f, 0.28f, 0.30f),   // charcoal
        };

        // Twelve furs, light to dark: the silvers and greys of an old male, the auburn and rust of a
        // lowland crown, then the browns and blacks.
        static readonly Color[] _gorillaFur =
        {
            new Color(0.70f, 0.70f, 0.72f),   // silver
            new Color(0.58f, 0.57f, 0.58f),   // light grey
            new Color(0.45f, 0.44f, 0.45f),   // grey
            new Color(0.42f, 0.25f, 0.14f),   // auburn
            new Color(0.34f, 0.33f, 0.34f),   // ash
            new Color(0.32f, 0.20f, 0.13f),   // rust brown
            new Color(0.28f, 0.24f, 0.21f),   // brown
            new Color(0.22f, 0.19f, 0.17f),   // dark brown
            new Color(0.20f, 0.20f, 0.22f),   // blue black
            new Color(0.16f, 0.14f, 0.13f),   // sable
            new Color(0.11f, 0.11f, 0.17f),   // jet
            new Color(0.10f, 0.09f, 0.09f),   // black
        };

        // Twelve plumages: the natural white-to-black ramp first, then two dyed feathers, which are
        // as real as an ostrich plume gets in a hat shop.
        static readonly Color[] _ostrichPlumage =
        {
            new Color(0.96f, 0.95f, 0.92f),   // white
            new Color(0.90f, 0.87f, 0.80f),   // cream
            new Color(0.80f, 0.74f, 0.63f),   // sand
            new Color(0.66f, 0.58f, 0.46f),   // buff
            new Color(0.50f, 0.43f, 0.34f),   // tawny
            new Color(0.55f, 0.36f, 0.26f),   // rust
            new Color(0.42f, 0.43f, 0.48f),   // slate
            new Color(0.34f, 0.29f, 0.24f),   // umber
            new Color(0.20f, 0.18f, 0.16f),   // dark brown
            new Color(0.11f, 0.10f, 0.10f),   // black
            new Color(0.85f, 0.45f, 0.60f),   // dyed pink
            new Color(0.20f, 0.55f, 0.58f),   // dyed teal
        };

        // ---- Style slot presets ----
        // Drawn four to a row in the wheel column of a style slot's sub-menu, so counts are
        // multiples of four. Same ApproxColor spacing rule as the skins.

        // Human hair and facial hair: sixteen natural shades, dark to light, then the greys, then
        // one dyed accent for the wheel-shy.
        static readonly Color[] _hairColors =
        {
            new Color(0.05f, 0.04f, 0.04f),   // black
            new Color(0.12f, 0.10f, 0.09f),   // soft black
            new Color(0.22f, 0.14f, 0.09f),   // dark brown
            new Color(0.36f, 0.23f, 0.14f),   // medium brown
            new Color(0.45f, 0.25f, 0.13f),   // chestnut
            new Color(0.55f, 0.38f, 0.24f),   // light brown
            new Color(0.50f, 0.20f, 0.10f),   // auburn
            new Color(0.75f, 0.35f, 0.12f),   // copper / ginger
            new Color(0.85f, 0.58f, 0.36f),   // strawberry blonde
            new Color(0.68f, 0.53f, 0.32f),   // dark blonde
            new Color(0.88f, 0.72f, 0.42f),   // golden blonde
            new Color(0.93f, 0.89f, 0.78f),   // platinum
            new Color(0.45f, 0.43f, 0.42f),   // salt and pepper
            new Color(0.66f, 0.65f, 0.64f),   // grey
            new Color(0.94f, 0.94f, 0.93f),   // white
            new Color(0.12f, 0.20f, 0.62f),   // dyed deep blue
        };

        // Horse mane: twelve natural mane and tail shades. Index 0 is the SeedStyleColors mane.
        static readonly Color[] _maneColors =
        {
            new Color(0.08f, 0.06f, 0.05f),   // black
            new Color(0.18f, 0.16f, 0.15f),   // smoky
            new Color(0.24f, 0.15f, 0.10f),   // dark brown
            new Color(0.30f, 0.17f, 0.11f),   // liver
            new Color(0.36f, 0.22f, 0.13f),   // brown
            new Color(0.58f, 0.31f, 0.15f),   // chestnut
            new Color(0.70f, 0.40f, 0.20f),   // sorrel
            new Color(0.80f, 0.64f, 0.36f),   // gold
            new Color(0.86f, 0.74f, 0.50f),   // flaxen
            new Color(0.50f, 0.49f, 0.48f),   // grey
            new Color(0.72f, 0.72f, 0.72f),   // silver
            new Color(0.95f, 0.94f, 0.90f),   // white
        };

        // Horse markings: eight whites, creams and greys. Index 0 is the SeedStyleColors marking.
        static readonly Color[] _markingColors =
        {
            new Color(0.95f, 0.94f, 0.90f),   // white
            new Color(0.99f, 0.99f, 0.98f),   // snow
            new Color(0.94f, 0.91f, 0.82f),   // ivory
            new Color(0.90f, 0.85f, 0.72f),   // cream
            new Color(0.90f, 0.80f, 0.76f),   // pink (skin showing through)
            new Color(0.78f, 0.78f, 0.77f),   // light grey
            new Color(0.62f, 0.61f, 0.60f),   // grey
            new Color(0.42f, 0.41f, 0.40f),   // dark grey
        };

        // Horse tack: twelve leathers and webbing colours. Index 0 is the SeedStyleColors tack.
        static readonly Color[] _leathers =
        {
            new Color(0.32f, 0.20f, 0.10f),   // havana brown
            new Color(0.08f, 0.07f, 0.07f),   // black leather
            new Color(0.24f, 0.14f, 0.08f),   // dark oak
            new Color(0.48f, 0.26f, 0.12f),   // chestnut leather
            new Color(0.66f, 0.42f, 0.22f),   // tan
            new Color(0.80f, 0.60f, 0.38f),   // raw hide
            new Color(0.42f, 0.12f, 0.10f),   // oxblood
            new Color(0.65f, 0.12f, 0.10f),   // red webbing
            new Color(0.12f, 0.16f, 0.34f),   // navy webbing
            new Color(0.10f, 0.30f, 0.18f),   // racing green
            new Color(0.45f, 0.45f, 0.47f),   // grey
            new Color(0.92f, 0.92f, 0.90f),   // white
        };

        // Elephant tusks: eight ivories, fresh to stained. Index 0 is the SeedStyleColors tusk.
        static readonly Color[] _ivories =
        {
            new Color(0.93f, 0.90f, 0.80f),   // ivory
            new Color(0.97f, 0.96f, 0.93f),   // bright white
            new Color(0.90f, 0.86f, 0.74f),   // bone
            new Color(0.88f, 0.82f, 0.66f),   // cream
            new Color(0.82f, 0.74f, 0.56f),   // aged ivory
            new Color(0.74f, 0.64f, 0.46f),   // tea-stained
            new Color(0.66f, 0.52f, 0.32f),   // amber
            new Color(0.52f, 0.42f, 0.30f),   // mud-stained
        };

        // Elephant tack: twelve cloth dyes for the head cloth and blanket. Index 0 is the
        // SeedStyleColors tack.
        static readonly Color[] _cloths =
        {
            new Color(0.70f, 0.12f, 0.10f),   // red
            new Color(0.55f, 0.08f, 0.12f),   // crimson
            new Color(0.95f, 0.60f, 0.10f),   // saffron
            new Color(0.85f, 0.68f, 0.20f),   // gold
            new Color(0.10f, 0.50f, 0.28f),   // emerald
            new Color(0.10f, 0.45f, 0.50f),   // teal
            new Color(0.12f, 0.25f, 0.65f),   // royal blue
            new Color(0.20f, 0.12f, 0.45f),   // indigo
            new Color(0.45f, 0.15f, 0.55f),   // purple
            new Color(0.75f, 0.15f, 0.50f),   // magenta
            new Color(0.92f, 0.88f, 0.76f),   // cream
            new Color(0.08f, 0.08f, 0.09f),   // black
        };

        // Gorilla face: eight bare-skin tones, black through pale, plus a dusky pink and a brown.
        static readonly Color[] _gorillaFaces =
        {
            new Color(0.08f, 0.07f, 0.07f),   // black
            new Color(0.16f, 0.15f, 0.15f),   // charcoal
            new Color(0.26f, 0.25f, 0.25f),   // dark grey
            new Color(0.40f, 0.39f, 0.39f),   // grey
            new Color(0.60f, 0.60f, 0.60f),   // silver
            new Color(0.78f, 0.74f, 0.70f),   // pale muzzle
            new Color(0.55f, 0.40f, 0.38f),   // dusky pink
            new Color(0.30f, 0.20f, 0.15f),   // brown
        };

        // Ostrich neck: eight bare-skin tones, the pinks of a red-necked bird and the blue-greys of
        // a Somali one.
        static readonly Color[] _ostrichNecks =
        {
            new Color(0.85f, 0.60f, 0.58f),   // pink
            new Color(0.80f, 0.62f, 0.52f),   // flesh
            new Color(0.90f, 0.55f, 0.45f),   // salmon
            new Color(0.75f, 0.30f, 0.28f),   // flushed red
            new Color(0.85f, 0.80f, 0.74f),   // pale
            new Color(0.60f, 0.58f, 0.56f),   // grey
            new Color(0.55f, 0.60f, 0.68f),   // blue grey
            new Color(0.45f, 0.48f, 0.55f),   // slate
        };

        // Ostrich beak: eight horn colours.
        static readonly Color[] _beakHorns =
        {
            new Color(0.78f, 0.66f, 0.48f),   // horn
            new Color(0.86f, 0.78f, 0.62f),   // pale horn
            new Color(0.85f, 0.60f, 0.55f),   // pink
            new Color(0.90f, 0.50f, 0.18f),   // orange
            new Color(0.92f, 0.72f, 0.22f),   // yellow
            new Color(0.55f, 0.53f, 0.50f),   // grey
            new Color(0.32f, 0.30f, 0.28f),   // dark grey
            new Color(0.10f, 0.09f, 0.09f),   // black
        };

        // Accessories (human extras, gorilla straps, ostrich bands): sixteen general accent colours,
        // neutrals first, then the hue circle, then the leathers and metals.
        static readonly Color[] _accents =
        {
            new Color(0.06f, 0.06f, 0.07f),   // black
            new Color(0.25f, 0.25f, 0.27f),   // charcoal
            new Color(0.55f, 0.55f, 0.57f),   // grey
            new Color(0.95f, 0.95f, 0.94f),   // white
            new Color(0.80f, 0.12f, 0.12f),   // red
            new Color(0.95f, 0.50f, 0.10f),   // orange
            new Color(0.95f, 0.80f, 0.15f),   // yellow
            new Color(0.15f, 0.60f, 0.25f),   // green
            new Color(0.10f, 0.55f, 0.55f),   // teal
            new Color(0.15f, 0.35f, 0.80f),   // blue
            new Color(0.10f, 0.14f, 0.35f),   // navy
            new Color(0.50f, 0.20f, 0.65f),   // purple
            new Color(0.92f, 0.40f, 0.62f),   // pink
            new Color(0.40f, 0.24f, 0.12f),   // brown
            new Color(0.76f, 0.58f, 0.38f),   // tan
            new Color(0.85f, 0.68f, 0.22f),   // gold
        };
    }
}
