using System;
using System.Collections.Generic;

// PURE FILE: no UnityEngine. It also compiles in a plain .NET console app (see CupSelfTest).

namespace Trickshot
{
    /// <summary>One row of the nation table.</summary>
    public sealed class CupNation
    {
        /// <summary>The design key: must match a JerseyDesigns Nations design name EXACTLY.</summary>
        public readonly string Name;
        /// <summary>Three upper-case letters, FIFA-style where one exists, unique in the table.</summary>
        public readonly string Code;
        /// <summary>1..99, hidden flavour: it biases CupSim only and is never shown or sorted on.</summary>
        public readonly int Strength;
        /// <summary>Novelty kits are excluded from the AI pool; a human may still pick one.</summary>
        public readonly bool Novelty;

        public CupNation(string name, string code, int strength, bool novelty)
        {
            Name = name;
            Code = code;
            Strength = strength;
            Novelty = novelty;
        }

        public override string ToString() => Code + " " + Name;
    }

    /// <summary>
    /// The 214 nations of the cup, one per JerseyDesigns Nations design, in the same order as
    /// <c>JerseyDesigns.InTab(DesignTab.Nations)</c> (ordinal, case-insensitive A-Z: "Uruguay"
    /// before "USA"), so a table index is also that list's index. The name is the key into the
    /// jersey library; keep every spelling identical to the design (CupNations.Validate logs any
    /// row that no longer resolves). Indices ride the wire, so append at the end if the library
    /// ever grows rather than re-sorting.
    ///
    /// EDITING THIS TABLE IS A WIRE CHANGE - bump <c>NetCodec.ProtocolVersion</c> (currently 8)
    /// with ANY edit here: a new row, a removed row, a flipped Novelty flag, or an edited
    /// Strength. Peers never replicate the bracket, only the results of rounds humans played
    /// (design 2.5), so every peer REBUILDS the draw from the seed - and both of those rebuilds
    /// read this table:
    ///
    ///   * the DRAW. CupBracket.Build walks <c>PoolIndices</c> in TABLE ORDER, skipping novelty
    ///     rows, and shuffles the result; so inserting or removing a row, or flipping a Novelty
    ///     flag, shifts every later index through that shuffle and changes which 31 nations are
    ///     drawn and who meets whom.
    ///   * every SIMULATED AI result. CupSim.Simulate reads Entrants[..].Strength to bias the
    ///     line it rolls, so an edited Strength changes rounds a peer re-runs from the seed.
    ///
    /// Without the bump two builds join happily and then disagree about the whole tournament. The
    /// only symptom is the client's one-off bracket-hash warning in CupDirector.Net: the shape is
    /// compared against the host's FNV hash and a mismatch is LOGGED, never repaired - so a
    /// mixed-build lobby plays on, silently, with two different brackets.
    /// </summary>
    public static class CupNationTable
    {
        public static readonly CupNation[] All =
        {
            new CupNation("Afghanistan", "AFG", 32, false),
            new CupNation("Albania", "ALB", 64, false),
            new CupNation("Algeria", "ALG", 74, false),
            new CupNation("Andorra", "AND", 25, false),
            new CupNation("Angola", "ANG", 58, false),
            new CupNation("Antarctica", "ATA", 40, true),
            new CupNation("Antigua and Barbuda", "ATG", 30, false),
            new CupNation("Argentina", "ARG", 94, false),
            new CupNation("Armenia", "ARM", 50, false),
            new CupNation("Aruba", "ARU", 22, false),
            new CupNation("Australia", "AUS", 76, false),
            new CupNation("Austria", "AUT", 78, false),
            new CupNation("Azerbaijan", "AZE", 48, false),
            new CupNation("Bahamas", "BAH", 18, false),
            new CupNation("Bahrain", "BHR", 55, false),
            new CupNation("Bangladesh", "BAN", 26, false),
            new CupNation("Barbados", "BRB", 28, false),
            new CupNation("Belarus", "BLR", 48, false),
            new CupNation("Belgium", "BEL", 85, false),
            new CupNation("Belize", "BLZ", 30, false),
            new CupNation("Benin", "BEN", 55, false),
            new CupNation("Bermuda", "BER", 35, false),
            new CupNation("Bhutan", "BHU", 18, false),
            new CupNation("Bolivia", "BOL", 55, false),
            new CupNation("Bosnia and Herzegovina", "BIH", 66, false),
            new CupNation("Botswana", "BOT", 42, false),
            new CupNation("Brazil", "BRA", 91, false),
            new CupNation("Brunei", "BRU", 16, false),
            new CupNation("Bulgaria", "BUL", 60, false),
            new CupNation("Burkina Faso", "BFA", 65, false),
            new CupNation("Burundi", "BDI", 45, false),
            new CupNation("Cabo Verde", "CPV", 64, false),
            new CupNation("Cambodia", "CAM", 32, false),
            new CupNation("Cameroon", "CMR", 72, false),
            new CupNation("Canada", "CAN", 74, false),
            new CupNation("Catalonia", "CAT", 60, true),
            new CupNation("Central African Republic", "CTA", 45, false),
            new CupNation("Chad", "CHA", 35, false),
            new CupNation("Chile", "CHI", 70, false),
            new CupNation("China", "CHN", 54, false),
            new CupNation("Colombia", "COL", 83, false),
            new CupNation("Comoros", "COM", 48, false),
            new CupNation("Congo (DR)", "COD", 70, false),
            new CupNation("Congo (Republic)", "CGO", 50, false),
            new CupNation("Cook Islands", "COK", 18, false),
            new CupNation("Costa Rica", "CRC", 64, false),
            new CupNation("Cote d'Ivoire", "CIV", 75, false),
            new CupNation("Croatia", "CRO", 83, false),
            new CupNation("Cuba", "CUB", 45, false),
            new CupNation("Cyprus", "CYP", 45, false),
            new CupNation("Czechia", "CZE", 73, false),
            new CupNation("Denmark", "DEN", 80, false),
            new CupNation("Djibouti", "DJI", 20, false),
            new CupNation("Dominica", "DMA", 20, false),
            new CupNation("Dominican Republic", "DOM", 48, false),
            new CupNation("Ecuador", "ECU", 78, false),
            new CupNation("Egypt", "EGY", 76, false),
            new CupNation("El Salvador", "SLV", 55, false),
            new CupNation("England", "ENG", 91, false),
            new CupNation("Equatorial Guinea", "EQG", 55, false),
            new CupNation("Eritrea", "ERI", 18, false),
            new CupNation("Estonia", "EST", 45, false),
            new CupNation("Eswatini", "SWZ", 38, false),
            new CupNation("Ethiopia", "ETH", 45, false),
            new CupNation("European Union", "EUR", 58, true),
            new CupNation("Faroe Islands", "FRO", 40, false),
            new CupNation("Fiji", "FIJ", 35, false),
            new CupNation("Finland", "FIN", 62, false),
            new CupNation("France", "FRA", 93, false),
            new CupNation("Gabon", "GAB", 60, false),
            new CupNation("Gambia", "GAM", 55, false),
            new CupNation("Georgia", "GEO", 68, false),
            new CupNation("Germany", "GER", 89, false),
            new CupNation("Ghana", "GHA", 72, false),
            new CupNation("Gibraltar", "GIB", 15, false),
            new CupNation("Greece", "GRE", 72, false),
            new CupNation("Greenland", "GRL", 42, true),
            new CupNation("Grenada", "GRN", 35, false),
            new CupNation("Guatemala", "GUA", 55, false),
            new CupNation("Guinea", "GUI", 60, false),
            new CupNation("Guinea-Bissau", "GNB", 52, false),
            new CupNation("Guyana", "GUY", 42, false),
            new CupNation("Haiti", "HAI", 55, false),
            new CupNation("Honduras", "HON", 62, false),
            new CupNation("Hong Kong", "HKG", 40, false),
            new CupNation("Hungary", "HUN", 74, false),
            new CupNation("Iceland", "ISL", 62, false),
            new CupNation("India", "IND", 45, false),
            new CupNation("Indonesia", "IDN", 52, false),
            new CupNation("Iran", "IRN", 76, false),
            new CupNation("Iraq", "IRQ", 66, false),
            new CupNation("Ireland", "IRL", 66, false),
            new CupNation("Israel", "ISR", 62, false),
            new CupNation("Italy", "ITA", 85, false),
            new CupNation("Jamaica", "JAM", 62, false),
            new CupNation("Japan", "JPN", 82, false),
            new CupNation("Jolly Roger", "JRG", 50, true),
            new CupNation("Jordan", "JOR", 64, false),
            new CupNation("Kazakhstan", "KAZ", 46, false),
            new CupNation("Kenya", "KEN", 52, false),
            new CupNation("Kiribati", "KIR", 15, false),
            new CupNation("Kosovo", "KOS", 55, false),
            new CupNation("Kuwait", "KUW", 48, false),
            new CupNation("Kyrgyzstan", "KGZ", 52, false),
            new CupNation("Laos", "LAO", 24, false),
            new CupNation("Latvia", "LVA", 42, false),
            new CupNation("Lebanon", "LBN", 45, false),
            new CupNation("Lesotho", "LES", 40, false),
            new CupNation("Liberia", "LBR", 42, false),
            new CupNation("Libya", "LBY", 50, false),
            new CupNation("Liechtenstein", "LIE", 22, false),
            new CupNation("Lithuania", "LTU", 42, false),
            new CupNation("Luxembourg", "LUX", 55, false),
            new CupNation("Madagascar", "MAD", 50, false),
            new CupNation("Malawi", "MWI", 45, false),
            new CupNation("Malaysia", "MAS", 48, false),
            new CupNation("Maldives", "MDV", 28, false),
            new CupNation("Mali", "MLI", 71, false),
            new CupNation("Malta", "MLT", 35, false),
            new CupNation("Marshall Islands", "MHL", 15, false),
            new CupNation("Mauritania", "MTN", 52, false),
            new CupNation("Mauritius", "MRI", 22, false),
            new CupNation("Mexico", "MEX", 79, false),
            new CupNation("Micronesia", "FSM", 15, false),
            new CupNation("Moldova", "MDA", 40, false),
            new CupNation("Monaco", "MON", 15, false),
            new CupNation("Mongolia", "MNG", 25, false),
            new CupNation("Montenegro", "MNE", 58, false),
            new CupNation("Morocco", "MAR", 84, false),
            new CupNation("Mozambique", "MOZ", 55, false),
            new CupNation("Myanmar", "MYA", 35, false),
            new CupNation("Namibia", "NAM", 50, false),
            new CupNation("Nauru", "NRU", 15, false),
            new CupNation("Nepal", "NEP", 30, false),
            new CupNation("Netherlands", "NED", 89, false),
            new CupNation("New Zealand", "NZL", 60, false),
            new CupNation("Nicaragua", "NCA", 45, false),
            new CupNation("Niger", "NIG", 48, false),
            new CupNation("Nigeria", "NGA", 77, false),
            new CupNation("North Korea", "PRK", 55, false),
            new CupNation("North Macedonia", "MKD", 60, false),
            new CupNation("Northern Ireland", "NIR", 60, false),
            new CupNation("Norway", "NOR", 79, false),
            new CupNation("Olympic", "OLY", 55, true),
            new CupNation("Oman", "OMA", 56, false),
            new CupNation("Pakistan", "PAK", 22, false),
            new CupNation("Palau", "PLW", 15, false),
            new CupNation("Panama", "PAN", 65, false),
            new CupNation("Papua New Guinea", "PNG", 32, false),
            new CupNation("Paraguay", "PAR", 70, false),
            new CupNation("Peru", "PER", 68, false),
            new CupNation("Philippines", "PHI", 46, false),
            new CupNation("Poland", "POL", 74, false),
            new CupNation("Portugal", "POR", 90, false),
            new CupNation("Pride Rainbow", "PRD", 50, true),
            new CupNation("Puerto Rico", "PUR", 40, false),
            new CupNation("Qatar", "QAT", 65, false),
            new CupNation("Romania", "ROU", 69, false),
            new CupNation("Russia", "RUS", 70, false),
            new CupNation("Rwanda", "RWA", 46, false),
            new CupNation("Saint Kitts and Nevis", "SKN", 32, false),
            new CupNation("Saint Lucia", "LCA", 28, false),
            new CupNation("Saint Vincent and the Grenadines", "VIN", 30, false),
            new CupNation("Samoa", "SAM", 22, false),
            new CupNation("San Marino", "SMR", 15, false),
            new CupNation("Sao Tome and Principe", "STP", 20, false),
            new CupNation("Saudi Arabia", "KSA", 68, false),
            new CupNation("Scotland", "SCO", 72, false),
            new CupNation("Senegal", "SEN", 80, false),
            new CupNation("Serbia", "SRB", 76, false),
            new CupNation("Seychelles", "SEY", 18, false),
            new CupNation("Sierra Leone", "SLE", 50, false),
            new CupNation("Singapore", "SGP", 36, false),
            new CupNation("Slovakia", "SVK", 68, false),
            new CupNation("Slovenia", "SVN", 70, false),
            new CupNation("Solomon Islands", "SOL", 35, false),
            new CupNation("Somalia", "SOM", 20, false),
            new CupNation("South Africa", "RSA", 66, false),
            new CupNation("South Korea", "KOR", 78, false),
            new CupNation("South Sudan", "SSD", 32, false),
            new CupNation("Soviet Union", "URS", 60, true),
            new CupNation("Spain", "ESP", 93, false),
            new CupNation("Sri Lanka", "SRI", 22, false),
            new CupNation("Sudan", "SDN", 50, false),
            new CupNation("Suriname", "SUR", 50, false),
            new CupNation("Sweden", "SWE", 74, false),
            new CupNation("Switzerland", "SUI", 80, false),
            new CupNation("Syria", "SYR", 52, false),
            new CupNation("Taiwan", "TPE", 35, false),
            new CupNation("Tajikistan", "TJK", 50, false),
            new CupNation("Tanzania", "TAN", 52, false),
            new CupNation("Thailand", "THA", 55, false),
            new CupNation("Timor-Leste", "TLS", 18, false),
            new CupNation("Togo", "TOG", 50, false),
            new CupNation("Tonga", "TGA", 18, false),
            new CupNation("Trinidad and Tobago", "TRI", 55, false),
            new CupNation("Tunisia", "TUN", 71, false),
            new CupNation("Turkey", "TUR", 79, false),
            new CupNation("Turkmenistan", "TKM", 45, false),
            new CupNation("Tuvalu", "TUV", 15, false),
            new CupNation("Uganda", "UGA", 58, false),
            new CupNation("Ukraine", "UKR", 77, false),
            new CupNation("United Arab Emirates", "UAE", 62, false),
            new CupNation("Uruguay", "URU", 83, false),
            new CupNation("USA", "USA", 79, false),
            new CupNation("Uzbekistan", "UZB", 66, false),
            new CupNation("Vanuatu", "VAN", 30, false),
            new CupNation("Vatican City", "VAT", 40, true),
            new CupNation("Venezuela", "VEN", 66, false),
            new CupNation("Vietnam", "VIE", 56, false),
            new CupNation("Wales", "WAL", 68, false),
            new CupNation("Yemen", "YEM", 30, false),
            new CupNation("Zambia", "ZAM", 58, false),
            new CupNation("Zimbabwe", "ZIM", 52, false),
        };

        /// <summary>The number of nations the cup knows: 214.</summary>
        public static int Count => All.Length;

        static Dictionary<string, int> _byName;
        static Dictionary<string, int> _byCode;
        static int[] _pool;

        /// <summary>The row at a table index (throws on an invalid index; see <see cref="IsValid"/>).</summary>
        public static CupNation Get(int i)
        {
            if (i < 0 || i >= All.Length)
                throw new ArgumentOutOfRangeException(nameof(i), "CupNationTable: no nation at index " + i);
            return All[i];
        }

        public static bool IsValid(int i) => i >= 0 && i < All.Length;

        public static string NameOf(int i) => Get(i).Name;
        public static string CodeOf(int i) => Get(i).Code;
        public static int StrengthOf(int i) => Get(i).Strength;
        public static bool IsNovelty(int i) => Get(i).Novelty;
        /// <summary>The row's strength normalised to 0..1 (CupSim's input).</summary>
        public static float Strength01(int i) => CupTuning.Strength01(Get(i).Strength);

        /// <summary>Table index of a design name (case-insensitive), or -1.</summary>
        public static int IndexOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            EnsureMaps();
            int i;
            return _byName.TryGetValue(name, out i) ? i : -1;
        }

        /// <summary>Table index of a three-letter code (case-insensitive), or -1.</summary>
        public static int IndexOfCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return -1;
            EnsureMaps();
            int i;
            return _byCode.TryGetValue(code, out i) ? i : -1;
        }

        /// <summary>The indices of every NON-novelty nation: the AI draw pool (205 rows).</summary>
        public static IEnumerable<int> PoolIndices
        {
            get
            {
                EnsureMaps();
                return _pool;
            }
        }

        /// <summary>A fresh copy of <see cref="PoolIndices"/> as a list a caller may shuffle or trim.</summary>
        public static List<int> PoolList()
        {
            EnsureMaps();
            return new List<int>(_pool);
        }

        /// <summary>How many nations are in the AI pool.</summary>
        public static int PoolCount
        {
            get
            {
                EnsureMaps();
                return _pool.Length;
            }
        }

        static void EnsureMaps()
        {
            if (_byName != null) return;
            var byName = new Dictionary<string, int>(All.Length, StringComparer.OrdinalIgnoreCase);
            var byCode = new Dictionary<string, int>(All.Length, StringComparer.OrdinalIgnoreCase);
            var pool = new List<int>(All.Length);
            for (int i = 0; i < All.Length; i++)
            {
                // First definition wins, like JerseyDesigns' own dedupe; the self-test asserts there
                // are no duplicates at all.
                if (!byName.ContainsKey(All[i].Name)) byName.Add(All[i].Name, i);
                if (!byCode.ContainsKey(All[i].Code)) byCode.Add(All[i].Code, i);
                if (!All[i].Novelty) pool.Add(i);
            }
            _pool = pool.ToArray();
            _byCode = byCode;
            _byName = byName;
        }
    }
}
