using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A branching skill tree the player spends a FIXED pool of points into when creating
    /// a character (points are not earned in-match). Six categories, each an actual node
    /// GRAPH: a root splits into two directions, each direction climbs its own tier chain,
    /// and one branch ends in a unique CAPSTONE PERK. Nodes carry grid coordinates + an
    /// icon so the UI can draw them as a real clickable tree with connectors.
    ///
    /// Every node is FUNCTIONAL - it changes real gameplay (speed, power, accuracy, trap,
    /// air control, mass). Capstones are pure perks with a distinct on/off effect and carry
    /// NO vanity stat bonus. Node effects STACK on the height/weight body traits.
    ///
    /// Base power/accuracy are deliberately low (see SimConfig), so investing in a branch
    /// is clearly felt rather than a marginal tweak.
    /// </summary>
    public static class SkillTree
    {
        // The first seven are the football categories: shared by EVERY species, so the attribute
        // heptagon stays comparable across a human, a horse and an elephant. Every effect key
        // ("move", "shotpower", "push", ...) names a football action, not an anatomy, so none of
        // them needs a species variant.
        //
        // The last two are conditional tabs that spend from the same point pool:
        //   ThirdLeg - adult mode only (member length/girth + ball size). NOT a football stat: its
        //              effect keys feed the cosmetic appendage, so it never moves the heptagon, and
        //              it is excluded from Randomize.
        //   Instinct - species flavour. Its nodes are filtered by Node.Species, so each species
        //              sees only its own, and Human (which has none) never sees the tab. Its
        //              effects DO use real football keys, so they do move the heptagon - that is
        //              deliberate: a species perk that adds sprint should read on the card.
        //              Excluded from Randomize so a roll can't buy another species' nodes.
        //
        // Appended at the END: nothing serializes a Category today, but the ThirdLeg/Instinct tab
        // arithmetic in CustomizeUI counts on them being last.
        public enum Category { Pace, Shooting, Passing, Heading, Strength, Control, Agility, ThirdLeg, Instinct }

        public struct Effect { public string Key; public float Amount; public Effect(string k, float a){ Key=k; Amount=a; } }

        public class Node
        {
            public string Id;
            public string Name;
            public string Desc;
            public Category Cat;
            public int Cost;
            public string Requires;   // prereq node id (null = root)
            public Effect[] Effects;  // functional stat contributions (empty for a pure perk)
            public string Perk;       // capstone perk key (null for normal nodes)
            public string Icon;       // 1-2 char glyph drawn on the node badge
            public float GridX;       // 0..1 horizontal position within the category tree
            public int   GridY;       // tier row (0 = root at top)
            // Species gate: -1 = available to every species (all football nodes). Otherwise a
            // SpeciesDef.Id, and the node is hidden and inert for every other species. Used by
            // Category.Instinct.
            public int   Species = -1;
        }

        // Fixed pool. 100 points: buy nearly half of the whole tree (total cost of every node
        // is 216), so players can fully build a couple of categories and dip into others.
        public const int Budget = 100;

        public static readonly HashSet<string> Owned = new HashSet<string>();

        public static int Spent
        {
            get { int s = 0; foreach (var id in Owned) if (_byId.TryGetValue(id, out var n)) s += n.Cost; return s; }
        }
        public static int Remaining => Budget - Spent;

        // Points sunk into the adult-mode Third Leg tab (drives the "% of your skill points" gag
        // on Next). Same pool as everything else, so this is a subset of Spent.
        public static int ThirdLegSpent
        {
            get { int s = 0; foreach (var id in Owned) if (_byId.TryGetValue(id, out var n) && n.Cat == Category.ThirdLeg) s += n.Cost; return s; }
        }

        public static float Mul(string key)
        {
            float sum = 0f;
            foreach (var id in Owned)
                if (_byId.TryGetValue(id, out var n) && n.Effects != null)
                    foreach (var e in n.Effects) if (e.Key == key) sum += e.Amount;
            return 1f + sum;
        }

        /// <summary>
        /// The largest <see cref="Mul"/> a given species could ever reach for one key: every node
        /// carrying it that the species can SEE, owned or not. Species-gated nodes belonging to other
        /// species are excluded, which is the whole point - it is what makes a horse's Heavy Hoof
        /// invisible to the human maximum.
        ///
        /// Exists so the cross-species power ceiling in BallController can be DERIVED rather than
        /// written down. A literal would go stale the moment a shooting node is added or retuned, and
        /// silently: nothing would fail, one species would just quietly out-shoot the cap.
        ///
        /// Affordability is deliberately not modelled. It does not bind: the whole shooting line is
        /// 17 SP and heading is 12, against a Budget of 100, so every node of one key is buyable
        /// together. Modelling it would need a knapsack over Requires chains for no gain.
        /// </summary>
        public static float MaxMul(string key, byte speciesId)
        {
            float sum = 0f;
            foreach (var n in All)
            {
                if (n.Effects == null) continue;
                if (n.Species >= 0 && n.Species != speciesId) continue;
                foreach (var e in n.Effects) if (e.Key == key) sum += e.Amount;
            }
            return 1f + sum;
        }

        public static bool HasPerk(string perk)
        {
            foreach (var id in Owned)
                if (_byId.TryGetValue(id, out var n) && n.Perk == perk) return true;
            return false;
        }

        // ---------------------------------------------------------------- passing build over the wire
        // The PASSING line as one byte, so a networked host can derive a player's real passing stats
        // instead of substituting a neutral constant for everybody.
        //
        // WHY A NODE MASK AND NOT THE TWO MULTIPLIERS. Sending derived floats means the host can only
        // CLAMP what arrives, and a clamp to the per-key ceiling is not authority - it hands any
        // modified client a free maxed tree, because the ceiling IS the maxed tree. A mask is a claim
        // the host can CHECK: an unreachable set (a node whose prerequisite is missing) is provably
        // invalid and gets rejected outright rather than trimmed. It is also integral, which closes a
        // nastier hole - Mathf.Clamp(NaN, lo, hi) returns NaN, so a raw float on the wire would let a
        // client push NaN through Passing.Launch and destroy the ball for every peer at once. A byte
        // cannot express NaN. It is smaller too: one byte instead of two floats plus a perk flag, and
        // Maestro falls out of its own bit instead of needing separate carriage.
        //
        // RESIDUAL, stated honestly: this bounds what a passing claim can EXPRESS, not that it was
        // bought. The host cannot see the rest of the tree, and the whole Passing line is 31 SP against
        // a Budget of 100, so "I own all of Passing" is always affordable in isolation and cannot be
        // refuted from this byte alone. Closing that needs the entire tree replicated, which belongs
        // with the move off single-host hosting rather than here.
        //
        // BIT ORDER IS PERMANENT. It is the wire format. Appending a 9th Passing node means appending a
        // bit; reordering these breaks every peer silently, because a mask stays structurally valid.
        public static readonly string[] PassingBits =
            { "pa0", "pa1a", "pa1c", "pa1b", "pa2a", "pa2c", "pa2b", "pacap" };

        /// <summary>This build's Passing line as a bit mask (see PassingBits).</summary>
        public static byte PackPassing()
        {
            byte m = 0;
            for (int i = 0; i < PassingBits.Length; i++)
                if (Owned.Contains(PassingBits[i])) m |= (byte)(1 << i);
            return m;
        }

        /// <summary>
        /// Turn a received mask back into a node set, REJECTING one that could not have been bought.
        /// Every owned node's prerequisite must also be owned, which is checkable from Node.Requires
        /// alone and is what makes this a validated claim rather than a trusted number. Returns false
        /// on an unreachable mask; callers fall back to a neutral build rather than trusting it.
        /// </summary>
        public static bool TryUnpackPassing(byte mask, out HashSet<string> owned)
        {
            owned = new HashSet<string>();
            for (int i = 0; i < PassingBits.Length; i++)
                if ((mask & (1 << i)) != 0) owned.Add(PassingBits[i]);

            foreach (var id in owned)
            {
                var n = ById(id);
                if (n == null) { owned.Clear(); return false; }
                if (!string.IsNullOrEmpty(n.Requires) && !owned.Contains(n.Requires))
                { owned.Clear(); return false; }   // unreachable: a node bought without its prerequisite
            }
            return true;
        }

        /// <summary>
        /// <see cref="Mul"/> over an ARBITRARY owned set rather than the local player's. This is how a
        /// host evaluates a remote player's build: same tables, same arithmetic, no second source of
        /// truth that could drift from the one the owner sees.
        /// </summary>
        public static float Mul(string key, ICollection<string> owned)
        {
            if (owned == null) return 1f;
            float sum = 0f;
            foreach (var id in owned)
                if (_byId.TryGetValue(id, out var n) && n.Effects != null)
                    foreach (var e in n.Effects) if (e.Key == key) sum += e.Amount;
            return 1f + sum;
        }

        /// <summary><see cref="HasPerk"/> over an arbitrary owned set. See the Mul overload above.</summary>
        public static bool HasPerk(string perk, ICollection<string> owned)
        {
            if (owned == null) return false;
            foreach (var id in owned)
                if (_byId.TryGetValue(id, out var n) && n.Perk == perk) return true;
            return false;
        }

        public static Node ById(string id) => _byId.TryGetValue(id, out var n) ? n : null;

        public static bool CanBuy(Node n)
        {
            if (n == null || Owned.Contains(n.Id)) return false;
            if (n.Cost > Remaining) return false;
            if (!string.IsNullOrEmpty(n.Requires) && !Owned.Contains(n.Requires)) return false;
            return true;
        }

        // Any owned node can be refunded; refunding an upstream node also refunds
        // everything built on top of it (see RefundCascade).
        public static bool CanRefund(Node n) => n != null && Owned.Contains(n.Id);

        // True if some OWNED node (directly) depends on this one - refunding it will
        // therefore also drop dependents. Used only to label the action.
        public static bool HasOwnedDependents(Node n)
        {
            if (n == null) return false;
            foreach (var m in All)
                if (m.Requires == n.Id && Owned.Contains(m.Id)) return true;
            return false;
        }

        public static void Buy(Node n) { if (CanBuy(n)) Owned.Add(n.Id); }

        // Refund a node AND every node that (transitively) requires it: remove the node,
        // then repeatedly drop any owned node whose prerequisite is no longer owned. Each
        // pass breaks another link down the chain, so the whole downstream subtree clears.
        public static void RefundCascade(Node n)
        {
            if (n == null || !Owned.Contains(n.Id)) return;
            Owned.Remove(n.Id);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var m in All)
                    if (Owned.Contains(m.Id) && !string.IsNullOrEmpty(m.Requires) && !Owned.Contains(m.Requires))
                    {
                        Owned.Remove(m.Id);
                        changed = true;
                    }
            }
        }

        // All refunds cascade (a bare node with no dependents just removes itself).
        public static void Refund(Node n) => RefundCascade(n);

        // ---------------------------------------------------------------- presets
        // One-click builds. Each is a hand-verified valid spend: prereqs included and the
        // total cost <= Budget. ApplyPreset clears the tree and grants exactly these nodes.
        public class Preset { public string Name; public string Desc; public string[] Ids; }

        public static readonly Preset[] Presets =
        {
            // Accent per skill area: max out one category (all 8 nodes incl. capstone) then
            // spill the rest into a themed second area. Each is a hand-verified valid spend
            // with prereqs, total <= Budget (40).
            new Preset { Name = "Pace Merchant",   Desc = "Top speed.",
                // Pace full = 2+3+3+3+4+5+4+7 = 31, + Agility a0,a1a = 5 -> 36
                Ids = new[]{ "p0","p1a","p1c","p1b","p2a","p2c","p2b","pcap", "a0","a1a" } },
            new Preset { Name = "Sniper",          Desc = "Deadly shooting.",
                // Shooting full = 2+3+3+3+4+5+4+8 = 32, + Control c0 = 2 -> 34
                Ids = new[]{ "s0","s1a","s1c","s1b","s2a","s2c","s2b","scap", "c0" } },
            new Preset { Name = "Power Header",    Desc = "Aerial threat.",
                // Heading full = 31, + Strength st0,st1a = 5 -> 36
                Ids = new[]{ "h0","h1a","h1c","h1b","h2a","h2c","h2b","hcap", "st0","st1a" } },
            new Preset { Name = "Brick Shithouse", Desc = "Immovable.",
                // Strength full = 31, + Heading h0,h1a = 5 -> 36
                Ids = new[]{ "st0","st1a","st1c","st1b","st2a","st2c","st2b","stcap", "h0","h1a" } },
            new Preset { Name = "Silky Dribbler",  Desc = "Glued dribble.",
                // Control full = 2+3+3+3+4+5+4+7 = 31, + Pace p0,p1a = 5 -> 36
                Ids = new[]{ "c0","c1a","c1c","c1b","c2a","c2c","c2b","ccap", "p0","p1a" } },
            new Preset { Name = "Showboat",        Desc = "Flair and flips.",
                // Agility full = 31, + Pace p0,p1a = 5 -> 36
                Ids = new[]{ "a0","a1a","a1c","a1b","a2a","a2c","a2b","acap", "p0","p1a" } },
            new Preset { Name = "Playmaker",       Desc = "Pinpoint passing.",
                // Passing full = 2+3+3+3+4+5+4+7 = 31, + Control c0,c1a = 5 -> 36
                Ids = new[]{ "pa0","pa1a","pa1c","pa1b","pa2a","pa2c","pa2b","pacap", "c0","c1a" } },
            // Balanced jack-of-all-trades: every area's root + a first upgrade each.
            // 7 x (2+3) = 35, + 3 extras -> 44 (<= Budget 46).
            new Preset { Name = "Default Chud",    Desc = "Balanced spend.",
                Ids = new[]{ "p0","p1a", "s0","s1a", "pa0","pa1a", "h0","h1a", "st0","st1a", "c0","c1a", "a0","a1a",
                             "p1c","s1c","h1c" } },
        };

        // What a node costs to reach from the CURRENT spend: the node itself plus every
        // prerequisite above it that isn't owned yet. Already-owned nodes cost 0.
        public static int ChainCost(Node n)
        {
            int sum = 0;
            for (var cur = n; cur != null; cur = string.IsNullOrEmpty(cur.Requires) ? null : ById(cur.Requires))
            {
                if (Owned.Contains(cur.Id)) break;   // this node and everything above it is paid for
                sum += cur.Cost;
            }
            return sum;
        }

        // Grant a node AND any unowned prerequisites above it - but ONLY if the whole chain fits in
        // the remaining points. This is the all-or-nothing rule capstones need: if you can't afford
        // the complete capstone (plus the path to it) nothing is spent and it simply stays unset,
        // rather than dribbling points into a half-finished branch. Returns true if granted.
        public static bool TryGrantChain(Node n)
        {
            if (n == null || Owned.Contains(n.Id)) return false;
            if (ChainCost(n) > Remaining) return false;
            // Affordable: walk up collecting the unowned chain, then add it root-first.
            var chain = new List<Node>();
            for (var cur = n; cur != null && !Owned.Contains(cur.Id);
                 cur = string.IsNullOrEmpty(cur.Requires) ? null : ById(cur.Requires))
                chain.Add(cur);
            for (int i = chain.Count - 1; i >= 0; i--) Owned.Add(chain[i].Id);
            return true;
        }

        // Apply a preset ADDITIVELY on top of whatever is already bought, so several presets can be
        // stacked to max out multiple areas with single clicks (it used to wipe the tree, which
        // capped you at one capstone). Each of the preset's nodes is granted only if its full
        // unowned prereq chain fits the remaining points; anything that doesn't fit - a capstone
        // included - is skipped and left unset. Capstones are taken FIRST so a preset's headline
        // perk isn't lost to the cheap filler nodes eating the budget.
        public static void ApplyPreset(Preset p)
        {
            if (p == null) return;
            foreach (var id in p.Ids)
                if (_byId.TryGetValue(id, out var n) && n.Perk != null) TryGrantChain(n);
            foreach (var id in p.Ids)
                if (_byId.TryGetValue(id, out var n)) TryGrantChain(n);
        }

        // The distinct skill AREAS a preset spends into (its headline category plus whatever second
        // area it spills into). Used to undo a preset by area rather than node-by-node.
        public static List<Category> PresetCategories(Preset p)
        {
            var cats = new List<Category>();
            if (p == null) return cats;
            foreach (var id in p.Ids)
                if (_byId.TryGetValue(id, out var n) && !cats.Contains(n.Cat)) cats.Add(n.Cat);
            return cats;
        }

        // Undo a preset: drop EVERY owned node in each area that preset covers, refunding those
        // points. Clicking an applied quick build therefore deselects it and empties that area's
        // branch in the tree (not just the preset's own node list), so the area is left clean rather
        // than holding orphaned leftovers. Areas are wiped wholesale, so no prereq can be left
        // dangling above a still-owned child.
        public static void RemovePreset(Preset p)
        {
            if (p == null) return;
            foreach (var cat in PresetCategories(p))
                foreach (var n in InCategory(cat))
                    Owned.Remove(n.Id);
        }

        public static void Clear() => Owned.Clear();

        // Wipe the tree and roll a fresh, always-LEGAL random build: pick a random subset of the
        // areas, then greedily buy random buyable nodes within them until a random target count is
        // reached or nothing else is buyable. Every add goes through CanBuy, so prereqs and the
        // point budget are always respected - it can never produce an illegal spend. Gives a
        // different node count from different areas on each call.
        public static void Randomize()
        {
            Clear();

            // Choose how many of the football areas to draw from (at least 1), then that many
            // distinct ones. ThirdLeg and Instinct are excluded: RANDOMIZE never spends points on
            // the adult tab, nor on the species tab (a roll should be a football build).
            var all = (Category[])System.Enum.GetValues(typeof(Category));
            var pool = new List<Category>();
            foreach (var c in all) if (c != Category.ThirdLeg && c != Category.Instinct) pool.Add(c);
            int catCount = Random.Range(1, pool.Count + 1);
            var chosen = new List<Category>();
            for (int i = 0; i < catCount && pool.Count > 0; i++)
            {
                int k = Random.Range(0, pool.Count);
                chosen.Add(pool[k]);
                pool.RemoveAt(k);
            }

            // Candidate nodes = all nodes in the chosen areas.
            var candidates = new List<Node>();
            foreach (var c in chosen) candidates.AddRange(InCategory(c));

            // A random target number of picks; capped so it can't loop forever if budget runs out.
            int target = Random.Range(3, candidates.Count + 1);
            int bought = 0, guard = 0;
            while (bought < target && guard++ < 500)
            {
                // Gather everything buyable right now (prereqs met + affordable), pick one at random.
                var buyable = new List<Node>();
                foreach (var n in candidates) if (CanBuy(n)) buyable.Add(n);
                if (buyable.Count == 0) break;
                Buy(buyable[Random.Range(0, buyable.Count)]);
                bought++;
            }
        }

        // Nodes in a category that the CURRENT species can see. Species-gated nodes (Instinct)
        // belonging to another species are filtered out, so every caller - the tab drawing, the
        // preset undo, Randomize - only ever touches nodes this species could legally own.
        public static IEnumerable<Node> InCategory(Category c)
        {
            byte sp = Species.SelectedId;
            foreach (var n in All) if (n.Cat == c && (n.Species < 0 || n.Species == sp)) yield return n;
        }

        /// <summary>Does this species have any Instinct nodes? False -> CustomizeUI hides the tab.</summary>
        public static bool HasInstinct(byte species)
        {
            foreach (var n in All) if (n.Cat == Category.Instinct && n.Species == species) return true;
            return false;
        }

        /// <summary>
        /// Drop every owned node that the current species cannot own. Called from
        /// Species.ApplySelection: without it, an Instinct node bought as a horse would keep
        /// paying into Mul() as a human while being invisible in the UI and unrefundable.
        /// Refunds implicitly, since Spent is computed from Owned.
        ///
        /// Also drops ids the tree NO LONGER DEFINES, which is how a saved profile gets its points
        /// back after a node is deleted (the Equine and Pachyderm sets were). Every consumer already
        /// looks ids up through _byId, so an orphan was harmless but it sat in Owned forever and its
        /// cost stayed invisible rather than refunded.
        /// </summary>
        public static void DropForeignSpecies()
        {
            byte sp = Species.SelectedId;
            List<string> drop = null;
            foreach (var id in Owned)
                if (!_byId.TryGetValue(id, out var n) || (n.Species >= 0 && n.Species != sp))
                    (drop ?? (drop = new List<string>())).Add(id);
            if (drop == null) return;
            foreach (var id in drop) Owned.Remove(id);
        }

        // ---------------------------------------------------------------- the tree
        public static readonly Node[] All;
        static readonly Dictionary<string, Node> _byId = new Dictionary<string, Node>();

        static Effect E(string k, float a) => new Effect(k, a);

        static SkillTree()
        {
            var list = new List<Node>();
            void Node_(string id, string name, string desc, Category cat, int cost, string req,
                       string icon, float gx, int gy, string perk, params Effect[] fx)
                => list.Add(new Node { Id=id, Name=name, Desc=desc, Cat=cat, Cost=cost, Requires=req,
                                       Icon=icon, GridX=gx, GridY=gy, Perk=perk, Effects=fx });

            // ============================ PACE (move, sprint) ============================
            Node_("p0","Quick Feet","+20% move speed",Category.Pace,2,null,">",0.5f,0,null, E("move",0.20f));
            Node_("p1a","Acceleration","+28% move speed",Category.Pace,3,"p0","»",0.2f,1,null, E("move",0.28f));
            Node_("p1c","Agile Feet","+20% move, +12% sprint",Category.Pace,3,"p0","x",0.5f,1,null, E("move",0.20f), E("sprint",0.12f));
            Node_("p1b","Long Strides","+28% sprint speed",Category.Pace,3,"p0","=",0.8f,1,null, E("sprint",0.28f));
            Node_("p2a","Sharp Turns","+28% move speed",Category.Pace,4,"p1a","«",0.2f,2,null, E("move",0.28f));
            Node_("p2c","Explosive","+24% move, +24% sprint",Category.Pace,5,"p1c","!",0.5f,2,null, E("move",0.24f), E("sprint",0.24f));
            Node_("p2b","Flat Out","+36% sprint speed",Category.Pace,4,"p1b","==",0.8f,2,null, E("sprint",0.36f));
            Node_("pcap","Afterburners","Sprint ramps to a burst speed",Category.Pace,7,"p2b","A",0.8f,3,"afterburners");

            // ========================== SHOOTING (shotpower, shotacc) ====================
            Node_("s0","Clean Strike","+12% shot power",Category.Shooting,2,null,"O",0.5f,0,null, E("shotpower",0.12f));
            Node_("s1a","Power","+16% shot power",Category.Shooting,3,"s0","!",0.2f,1,null, E("shotpower",0.16f));
            Node_("s1c","Technique","+10% power, +12% accuracy",Category.Shooting,3,"s0","*",0.5f,1,null, E("shotpower",0.10f), E("shotacc",0.12f));
            Node_("s1b","Placement","+22% shot accuracy",Category.Shooting,3,"s0","+",0.8f,1,null, E("shotacc",0.22f));
            Node_("s2a","Rising Shot","+16% shot power",Category.Shooting,4,"s1a","^",0.2f,2,null, E("shotpower",0.16f));
            Node_("s2c","Drilled","+14% power, +14% accuracy",Category.Shooting,5,"s1c","v",0.5f,2,null, E("shotpower",0.14f), E("shotacc",0.14f));
            Node_("s2b","Finesse","+24% shot accuracy",Category.Shooting,4,"s1b","x",0.8f,2,null, E("shotacc",0.24f));
            Node_("scap","Cannon","Much higher shot-speed ceiling",Category.Shooting,8,"s2a","C",0.2f,3,"cannon");

            // ========================== PASSING (passpower, passacc) =====================
            Node_("pa0","Passer","+14% pass accuracy",Category.Passing,2,null,">",0.5f,0,null, E("passacc",0.14f));
            Node_("pa1a","Zip","+18% pass power (faster passes)",Category.Passing,3,"pa0","!",0.2f,1,null, E("passpower",0.18f));
            Node_("pa1c","Playmaking","+10% power, +12% accuracy",Category.Passing,3,"pa0","*",0.5f,1,null, E("passpower",0.10f), E("passacc",0.12f));
            Node_("pa1b","Precision","+22% pass accuracy",Category.Passing,3,"pa0","+",0.8f,1,null, E("passacc",0.22f));
            Node_("pa2a","Driven","+18% pass power",Category.Passing,4,"pa1a","»",0.2f,2,null, E("passpower",0.18f));
            Node_("pa2c","Tempo","+14% power, +14% accuracy",Category.Passing,5,"pa1c","~",0.5f,2,null, E("passpower",0.14f), E("passacc",0.14f));
            Node_("pa2b","Threaded","+24% pass accuracy",Category.Passing,4,"pa1b","x",0.8f,2,null, E("passacc",0.24f));
            Node_("pacap","Maestro","Near-perfect pass accuracy",Category.Passing,7,"pa2b","M",0.8f,3,"maestro");

            // ==================== HEADING (headpower, headacc, jump, reach) ==============
            Node_("h0","Timing","+18% header accuracy",Category.Heading,2,null,"o",0.5f,0,null, E("headacc",0.18f));
            Node_("h1a","Power Header","+22% header power",Category.Heading,3,"h0","!",0.2f,1,null, E("headpower",0.22f));
            Node_("h1c","Glancing","+14% header accuracy, +6% reach",Category.Heading,3,"h0","/",0.5f,1,null, E("headacc",0.14f), E("reach",0.06f));
            Node_("h1b","Leap","+12% jump height",Category.Heading,3,"h0","^",0.8f,1,null, E("jump",0.12f));
            Node_("h2a","Bullet Head","+22% header power",Category.Heading,4,"h1a",">>",0.2f,2,null, E("headpower",0.22f));
            Node_("h2c","Pinpoint","+18% header accuracy",Category.Heading,4,"h1c","+",0.5f,2,null, E("headacc",0.18f));
            Node_("h2b","Hang Time","+10% jump, +8% reach",Category.Heading,4,"h1b","T",0.8f,2,null, E("jump",0.10f), E("reach",0.08f));
            Node_("hcap","Aerial Threat","Hard, driven headers",Category.Heading,7,"h2a","H",0.2f,3,"aerial");

            // ============================ STRENGTH (push, massbonus) =====================
            Node_("st0","Core","+14% push strength",Category.Strength,2,null,"#",0.5f,0,null, E("push",0.14f));
            Node_("st1a","Frame","+12% effective mass",Category.Strength,3,"st0","[]",0.2f,1,null, E("massbonus",0.12f));
            Node_("st1c","Sturdy","+8% push, +8% mass",Category.Strength,3,"st0","=",0.5f,1,null, E("push",0.08f), E("massbonus",0.08f));
            Node_("st1b","Balance","+16% push strength",Category.Strength,3,"st0","|",0.8f,1,null, E("push",0.16f));
            Node_("st2a","Powerhouse","+16% push, +8% mass",Category.Strength,4,"st1a","#!",0.2f,2,null, E("push",0.16f), E("massbonus",0.08f));
            Node_("st2c","Bulldozer","+12% push, +12% mass",Category.Strength,5,"st1c","B",0.5f,2,null, E("push",0.12f), E("massbonus",0.12f));
            Node_("st2b","Anchor","+18% push strength",Category.Strength,4,"st1b","V",0.8f,2,null, E("push",0.18f));
            Node_("stcap","Immovable","Hard to shove, shoves back",Category.Strength,7,"st2a","M",0.2f,3,"immovable");

            // ==================== CONTROL (trap, weakfoot, shotacc) ======================
            // The trap stat also drives dribble tightness (closer carry, faster + sharper
            // turning with the ball), so the whole left branch is the "close control" line.
            Node_("c0","First Touch","+25% trap control (ball settles closer)",Category.Control,2,null,".",0.5f,0,null, E("trap",0.25f));
            Node_("c1a","Cushion","+25% trap control (tighter dribble)",Category.Control,3,"c0","..",0.2f,1,null, E("trap",0.25f));
            Node_("c1c","Close Control","+15% trap, +10% shot accuracy",Category.Control,3,"c0","o",0.5f,1,null, E("trap",0.15f), E("shotacc",0.10f));
            Node_("c1b","Weak Foot","+35% weak-foot accuracy & power",Category.Control,3,"c0","L",0.8f,1,null, E("weakfoot",0.35f));
            Node_("c2a","Composure","+15% shot accuracy",Category.Control,4,"c1a","+",0.2f,2,null, E("shotacc",0.15f));
            Node_("c2c","Dribbler","+20% trap control (glued dribble)",Category.Control,5,"c1c","d",0.5f,2,null, E("trap",0.20f));
            Node_("c2b","Two-Footed","+35% weak-foot accuracy & power",Category.Control,4,"c1b","LR",0.8f,2,null, E("weakfoot",0.35f));
            Node_("ccap","Silky","Both feet are strong",Category.Control,7,"c2b","S",0.8f,3,"silky");

            // ============================ AGILITY (flip, jump) ==========================
            Node_("a0","Spring","+10% jump height",Category.Agility,2,null,"^",0.5f,0,null, E("jump",0.10f));
            Node_("a1a","Nimble","+20% air-flip control, -15% ground recovery time",Category.Agility,3,"a0","@",0.2f,1,null, E("flip",0.20f), E("recovery",-0.15f));
            Node_("a1c","Balanced","+10% flip, -8% recovery",Category.Agility,3,"a0","+",0.5f,1,null, E("flip",0.10f), E("recovery",-0.08f));
            Node_("a1b","Bounce","+12% jump height",Category.Agility,3,"a0","^^",0.8f,1,null, E("jump",0.12f));
            Node_("a2a","Twist","+22% air-flip control, -20% ground recovery time",Category.Agility,4,"a1a","%",0.2f,2,null, E("flip",0.22f), E("recovery",-0.20f));
            Node_("a2c","Cat-Like","-22% ground recovery time",Category.Agility,4,"a1c","c",0.5f,2,null, E("recovery",-0.22f));
            Node_("a2b","Elevation","+12% jump height",Category.Agility,4,"a1b","^!",0.8f,2,null, E("jump",0.12f));
            Node_("acap","Acrobat","Scroll to flip. Chainable 360s",Category.Agility,7,"a2a","X",0.2f,3,"acrobat");

            // ====================== THIRD LEG (adult mode: length / girth / ballsize) ============
            // Only reachable via the adult-mode "Third Leg" tab; spends from the shared point pool.
            // Effects scale the cosmetic pelvis appendage (AnatomySim): "length" = member length,
            // "girth" = member thickness, "ballsize" = berry radius. Terminates in ANACONDA, a
            // capstone that (unlike the sport capstones) DOES carry a big stat boost to all three.
            Node_("tl0","Endowed","+10% length, +10% girth",Category.ThirdLeg,2,null,"|",0.5f,0,null, E("length",0.10f), E("girth",0.10f));
            Node_("tl1a","Lengthen","+30% member length",Category.ThirdLeg,3,"tl0","L",0.2f,1,null, E("length",0.30f));
            Node_("tl1b","Thicken","+30% member girth",Category.ThirdLeg,3,"tl0","G",0.5f,1,null, E("girth",0.30f));
            Node_("tl1c","Heavy Hangers","+30% ball size",Category.ThirdLeg,3,"tl0","O",0.8f,1,null, E("ballsize",0.30f));
            Node_("tl2a","Grower","+30% member length",Category.ThirdLeg,4,"tl1a","LL",0.2f,2,null, E("length",0.30f));
            Node_("tl2b","Girthmaxx","+30% member girth",Category.ThirdLeg,4,"tl1b","GG",0.5f,2,null, E("girth",0.30f));
            Node_("tl2c","Boulders","+30% ball size",Category.ThirdLeg,4,"tl1c","OO",0.8f,2,null, E("ballsize",0.30f));
            Node_("tlcap","Anaconda","Big length, girth and balls",Category.ThirdLeg,8,"tl2b","A",0.5f,3,"anaconda", E("length",0.60f), E("girth",0.40f), E("ballsize",0.50f));

            // ====================== INSTINCT (per-species tab) ===================================
            // BALANCING PLACEHOLDERS. Three nodes per animal species so the tab is real and
            // clickable today: it draws, buys, refunds and cascades through the same code as every
            // other category, which is what makes the scaffold verifiable. The numbers are round
            // and unconsidered on purpose - the cross-species balancing pass replaces them once the
            // real models exist. A species with no Instinct node here never sees the tab
            // (SpeciesDef.InstinctTab is null and CustomizeUI checks HasInstinct as well), which is
            // Human, Horse and Elephant: the Equine and Pachyderm slots were pulled by request.
            //
            // Deliberately reuses existing effect keys, so nothing downstream needs to learn a new
            // one and these bonuses show up on the attribute card like any other investment.
            void Instinct_(int species, string id, string name, string desc, int cost, string req,
                           string icon, float gx, int gy, params Effect[] fx)
                => list.Add(new Node { Id=id, Name=name, Desc=desc, Cat=Category.Instinct, Cost=cost,
                                       Requires=req, Icon=icon, GridX=gx, GridY=gy, Species=species, Effects=fx });

            // Horse (species 1) and Elephant (species 2) deliberately have NO Instinct nodes: the
            // Equine and Pachyderm tabs were removed. Points spent in them on an existing profile are
            // refunded automatically, because DropForeignSpecies now also drops ids the tree no longer
            // defines and Spent is computed from Owned.

            // Gorilla: upper-body force.
            Instinct_(3,"in3_0","Knuckle Drive","+12% push",3,null,"G",0.5f,0, E("push",0.12f));
            Instinct_(3,"in3_a","Broad Shoulders","+12% effective mass",3,"in3_0","S",0.25f,1, E("massbonus",0.12f));
            Instinct_(3,"in3_b","Vault","+12% jump height",3,"in3_0","V",0.75f,1, E("jump",0.12f));

            // Ostrich: top speed on a light frame.
            Instinct_(4,"in4_0","Ratite Sprint","+20% sprint speed",3,null,"R",0.5f,0, E("sprint",0.20f));
            Instinct_(4,"in4_a","Long Stride","+12% move speed",3,"in4_0","D",0.25f,1, E("move",0.12f));
            Instinct_(4,"in4_b","Light Frame","+12% jump height",3,"in4_0","F",0.75f,1, E("jump",0.12f));

            All = list.ToArray();
            foreach (var n in All) _byId[n.Id] = n;
        }
    }
}
