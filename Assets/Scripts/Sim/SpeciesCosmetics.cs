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
        // Eight entries per species, matching the two-row layout the Skin sub-menu reserves.
        // The colour wheel below the swatches still allows anything, so these are shortcuts.

        /// <summary>
        /// The three slot colours a species starts with when the player switches TO it: a horse's mane
        /// dark, its markings white and its tack brown; an elephant's tusks ivory and its tack red.
        /// Humans keep whatever they had. Called from Species.ApplySelection on a change only.
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

        static readonly Color[] _humanSkins =
        {
            new Color(0.98f, 0.85f, 0.75f), new Color(0.94f, 0.78f, 0.66f), new Color(0.87f, 0.69f, 0.55f),
            new Color(0.80f, 0.61f, 0.46f), new Color(0.68f, 0.49f, 0.35f), new Color(0.55f, 0.38f, 0.26f),
            new Color(0.42f, 0.28f, 0.19f), new Color(0.30f, 0.20f, 0.14f),
        };

        // Bay, chestnut, liver, black, dark grey, dapple grey, palomino, cream.
        static readonly Color[] _horseCoats =
        {
            new Color(0.42f, 0.24f, 0.12f), new Color(0.58f, 0.31f, 0.15f), new Color(0.33f, 0.18f, 0.11f),
            new Color(0.12f, 0.10f, 0.09f), new Color(0.34f, 0.33f, 0.32f), new Color(0.62f, 0.61f, 0.60f),
            new Color(0.83f, 0.68f, 0.38f), new Color(0.92f, 0.88f, 0.79f),
        };

        static readonly Color[] _elephantHides =
        {
            new Color(0.46f, 0.46f, 0.48f), new Color(0.39f, 0.39f, 0.41f), new Color(0.33f, 0.33f, 0.35f),
            new Color(0.28f, 0.28f, 0.30f), new Color(0.52f, 0.50f, 0.47f), new Color(0.58f, 0.55f, 0.50f),
            new Color(0.44f, 0.40f, 0.36f), new Color(0.36f, 0.34f, 0.33f),
        };

        static readonly Color[] _gorillaFur =
        {
            new Color(0.10f, 0.09f, 0.09f), new Color(0.16f, 0.14f, 0.13f), new Color(0.22f, 0.19f, 0.17f),
            new Color(0.28f, 0.24f, 0.21f), new Color(0.20f, 0.20f, 0.22f), new Color(0.34f, 0.33f, 0.34f),
            new Color(0.45f, 0.44f, 0.45f), new Color(0.58f, 0.57f, 0.58f),
        };

        static readonly Color[] _ostrichPlumage =
        {
            new Color(0.11f, 0.10f, 0.10f), new Color(0.20f, 0.18f, 0.16f), new Color(0.34f, 0.29f, 0.24f),
            new Color(0.50f, 0.43f, 0.34f), new Color(0.66f, 0.58f, 0.46f), new Color(0.80f, 0.74f, 0.63f),
            new Color(0.90f, 0.87f, 0.80f), new Color(0.55f, 0.36f, 0.26f),
        };
    }
}
