using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The Unity side of the nation table: resolves each <see cref="CupNationTable"/> row to its
    /// JerseyDesigns Nations design (by NAME - the table rows are keys into the jersey library),
    /// hands out the 48x48 thumb that IS the flag badge everywhere in the cup, and samples the two
    /// kit colours from it (the scoreboard colours, the confetti). Everything is cached per row.
    ///
    /// The table's order is the Nations tab's order, so a row index normally equals its position
    /// in <c>JerseyDesigns.InTab(DesignTab.Nations)</c>; nothing here relies on that - the lookup
    /// is by name - but <see cref="Validate"/> mentions it when it stops being true.
    /// </summary>
    public static class CupNations
    {
        static Design[] _designs;
        static bool _validated;
        static Color[] _primary, _secondary;
        static bool[] _coloursDone;

        /// <summary>Fallbacks when a thumb cannot be read (missing design, unreadable texture).</summary>
        public static readonly Color FallbackPrimary = Color.white;
        public static readonly Color FallbackSecondary = Color.black;

        /// <summary>Two sampled colours must differ by at least this much (RGB distance, 0..sqrt(3)) to count as a pair.</summary>
        const float MinColourDistance = 0.35f;

        // ---- passthroughs ---------------------------------------------------------------------

        public static int Count => CupNationTable.Count;
        public static CupNation Row(int nationIndex) => CupNationTable.Get(nationIndex);
        public static string Name(int nationIndex) => CupNationTable.NameOf(nationIndex);
        public static string Code(int nationIndex) => CupNationTable.CodeOf(nationIndex);
        public static int Strength(int nationIndex) => CupNationTable.StrengthOf(nationIndex);
        public static bool IsNovelty(int nationIndex) => CupNationTable.IsNovelty(nationIndex);
        public static bool IsValid(int nationIndex) => CupNationTable.IsValid(nationIndex);

        // ---- designs --------------------------------------------------------------------------

        /// <summary>The jersey design of a table row, or null when the name no longer resolves.</summary>
        public static Design Design(int nationIndex)
        {
            if (!CupNationTable.IsValid(nationIndex)) return null;
            EnsureMap();
            return _designs[nationIndex];
        }

        public static bool HasDesign(int nationIndex) => Design(nationIndex) != null;

        /// <summary>The 48x48 flag badge (JerseyDesigns' cached thumb), or null without a design.</summary>
        public static Texture2D Thumb(int nationIndex)
        {
            var d = Design(nationIndex);
            return d == null ? null : JerseyDesigns.Thumb(d);
        }

        /// <summary>The most common colour of the flag badge (white when it cannot be read).</summary>
        public static Color PrimaryColor(int nationIndex)
        {
            if (!CupNationTable.IsValid(nationIndex)) return FallbackPrimary;
            EnsureColours(nationIndex);
            return _primary[nationIndex];
        }

        /// <summary>The second most common, visibly different colour of the badge (black when it cannot be read).</summary>
        public static Color SecondaryColor(int nationIndex)
        {
            if (!CupNationTable.IsValid(nationIndex)) return FallbackSecondary;
            EnsureColours(nationIndex);
            return _secondary[nationIndex];
        }

        /// <summary>Every table row that resolves to a design, in table order (the picker's list).</summary>
        public static List<int> Resolved()
        {
            EnsureMap();
            var list = new List<int>(CupNationTable.Count);
            for (int i = 0; i < CupNationTable.Count; i++) if (_designs[i] != null) list.Add(i);
            return list;
        }

        /// <summary>
        /// The AI draw pool at runtime: non-novelty rows that resolve to a design. Pass it as the
        /// <c>aiPool</c> of <see cref="CupBracket.Build"/> so a drifted table row is skipped rather
        /// than drawn as a nation with no kit. Every peer runs the same code, so the pool agrees.
        /// </summary>
        public static List<int> ResolvedPool()
        {
            EnsureMap();
            var list = new List<int>(CupNationTable.PoolCount);
            foreach (int i in CupNationTable.PoolIndices) if (_designs[i] != null) list.Add(i);
            return list;
        }

        /// <summary>
        /// Log a warning per table row whose name resolves to no Nations design, and per Nations
        /// design missing from the table (drift either way). Runs once, lazily, the first time a
        /// design is asked for; call it directly from the editor menu to re-run after a rebuild.
        /// Returns the number of unresolved rows.
        /// </summary>
        public static int Validate()
        {
            EnsureMap();
            _validated = true;
            int missing = 0;
            var inTab = JerseyDesigns.InTab(DesignTab.Nations);
            for (int i = 0; i < CupNationTable.Count; i++)
            {
                if (_designs[i] != null) continue;
                missing++;
                CupLog.Warn("nation table row " + i + " '" + CupNationTable.NameOf(i) + "' has no JerseyDesigns Nations design - it is skipped from the pool");
            }
            var known = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < CupNationTable.Count; i++) known.Add(CupNationTable.NameOf(i));
            for (int i = 0; i < inTab.Count; i++)
            {
                if (known.Contains(inTab[i].Name)) continue;
                CupLog.Warn("JerseyDesigns Nations design '" + inTab[i].Name + "' has no nation table row - add one (append at the end; indices ride the wire)");
            }
            bool sameOrder = inTab.Count == CupNationTable.Count;
            for (int i = 0; sameOrder && i < inTab.Count; i++)
                if (!string.Equals(inTab[i].Name, CupNationTable.NameOf(i), System.StringComparison.OrdinalIgnoreCase)) sameOrder = false;
            if (!sameOrder)
                CupLog.Info("nation table order differs from the Nations tab order (harmless: lookups are by name)");
            return missing;
        }

        /// <summary>Drop every cache so the next call re-resolves (editor tooling; not needed at runtime).</summary>
        public static void ClearCache()
        {
            _designs = null;
            _primary = _secondary = null;
            _coloursDone = null;
            _validated = false;
        }

        // ---- internals --------------------------------------------------------------------------

        static void EnsureMap()
        {
            if (_designs != null) return;
            var designs = new Design[CupNationTable.Count];
            var byName = new Dictionary<string, Design>(System.StringComparer.OrdinalIgnoreCase);
            var inTab = JerseyDesigns.InTab(DesignTab.Nations);
            for (int i = 0; i < inTab.Count; i++)
                if (inTab[i] != null && !string.IsNullOrEmpty(inTab[i].Name) && !byName.ContainsKey(inTab[i].Name))
                    byName.Add(inTab[i].Name, inTab[i]);
            for (int i = 0; i < CupNationTable.Count; i++)
            {
                Design d;
                designs[i] = byName.TryGetValue(CupNationTable.NameOf(i), out d) ? d : null;
            }
            _designs = designs;
            _primary = new Color[CupNationTable.Count];
            _secondary = new Color[CupNationTable.Count];
            _coloursDone = new bool[CupNationTable.Count];
            if (!_validated) Validate();
        }

        static void EnsureColours(int i)
        {
            EnsureMap();
            if (_coloursDone[i]) return;
            _coloursDone[i] = true;
            Color primary, secondary;
            SampleColours(Thumb(i), out primary, out secondary);
            _primary[i] = primary;
            _secondary[i] = secondary;
        }

        // Quantise every opaque pixel to 3 bits per channel (512 bins), average each bin, and take the
        // two most populous bins that are visibly different colours. Two shades of the same red are
        // one colour to a scoreboard, so the runner-up must clear MinColourDistance from the winner.
        static void SampleColours(Texture2D tex, out Color primary, out Color secondary)
        {
            primary = FallbackPrimary;
            secondary = FallbackSecondary;
            if (tex == null) return;
            Color32[] px;
            try { px = tex.GetPixels32(); }
            catch (System.Exception) { return; }
            if (px == null || px.Length == 0) return;

            const int bins = 512;
            var count = new int[bins];
            var sumR = new float[bins];
            var sumG = new float[bins];
            var sumB = new float[bins];
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a < 128) continue;
                int key = ((c.r >> 5) << 6) | ((c.g >> 5) << 3) | (c.b >> 5);
                count[key]++;
                sumR[key] += c.r;
                sumG[key] += c.g;
                sumB[key] += c.b;
            }
            var order = new List<int>();
            for (int k = 0; k < bins; k++) if (count[k] > 0) order.Add(k);
            if (order.Count == 0) return;
            order.Sort((a, b) => count[b].CompareTo(count[a]));

            Color Avg(int key) => new Color(sumR[key] / count[key] / 255f, sumG[key] / count[key] / 255f, sumB[key] / count[key] / 255f, 1f);

            primary = Avg(order[0]);
            bool found = false;
            for (int n = 1; n < order.Count; n++)
            {
                var c = Avg(order[n]);
                float dr = c.r - primary.r, dg = c.g - primary.g, db = c.b - primary.b;
                if (Mathf.Sqrt(dr * dr + dg * dg + db * db) >= MinColourDistance)
                {
                    secondary = c;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                if (order.Count > 1) secondary = Avg(order[1]);
                else
                {
                    // A single-colour badge: contrast against it so two-colour confetti still reads.
                    float lum = 0.299f * primary.r + 0.587f * primary.g + 0.114f * primary.b;
                    secondary = lum > 0.5f ? Color.black : Color.white;
                }
            }
        }
    }
}
