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
        public enum Category { Pace, Shooting, Heading, Strength, Control, Agility }

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
        }

        // Fixed pool: enough to fully build one category (a full branch to its capstone plus
        // the other branch's lower nodes ~= 23) plus a dip elsewhere - never max everything.
        public const int Budget = 32;

        public static readonly HashSet<string> Owned = new HashSet<string>();

        public static int Spent
        {
            get { int s = 0; foreach (var id in Owned) if (_byId.TryGetValue(id, out var n)) s += n.Cost; return s; }
        }
        public static int Remaining => Budget - Spent;

        public static float Mul(string key)
        {
            float sum = 0f;
            foreach (var id in Owned)
                if (_byId.TryGetValue(id, out var n) && n.Effects != null)
                    foreach (var e in n.Effects) if (e.Key == key) sum += e.Amount;
            return 1f + sum;
        }

        public static bool HasPerk(string perk)
        {
            foreach (var id in Owned)
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
            // Accent per skill area (full branch to capstone + a themed dip). Each = 32.
            new Preset { Name = "Pace Merchant",   Desc = "Blistering speed; a dash of agility",
                Ids = new[]{ "p0","p1a","p1b","p2a","p2b","pcap", "a0","a1a","a2a" } },
            new Preset { Name = "Sniper",          Desc = "Deadly shooting; a touch of control",
                Ids = new[]{ "s0","s1a","s1b","s2a","s2b","scap", "c0","c1a","c1b" } },
            new Preset { Name = "Power Header",    Desc = "Aerial threat; extra strength to win duels",
                Ids = new[]{ "h0","h1a","h1b","h2a","h2b","hcap", "st0","st1a","st2a" } },
            new Preset { Name = "Brick Shithouse", Desc = "Immovable strength; aerial presence",
                Ids = new[]{ "st0","st1a","st1b","st2a","st2b","stcap", "h0","h1a","h2a" } },
            new Preset { Name = "Maestro",         Desc = "Silky control; pace to drive at defenders",
                Ids = new[]{ "c0","c1a","c1b","c2a","c2b","ccap", "p0","p1a","p2a" } },
            new Preset { Name = "Showboat",        Desc = "Acrobatic agility; pace for flair on the run",
                Ids = new[]{ "a0","a1a","a1b","a2a","a2b","acap", "p0","p1a","p2a" } },
            // Balanced jack-of-all-trades: every area's root + first upgrade (30/32).
            new Preset { Name = "Default Chud",    Desc = "Balanced spend across every area",
                Ids = new[]{ "p0","p1a", "s0","s1a", "h0","h1a", "st0","st1a", "c0","c1a", "a0","a1a" } },
        };

        // Wipe the tree and grant exactly the preset's nodes (they are self-consistent, so
        // add directly rather than routing through CanBuy).
        public static void ApplyPreset(Preset p)
        {
            Owned.Clear();
            if (p == null) return;
            foreach (var id in p.Ids)
                if (_byId.ContainsKey(id)) Owned.Add(id);
        }

        public static void Clear() => Owned.Clear();

        public static IEnumerable<Node> InCategory(Category c)
        {
            foreach (var n in All) if (n.Cat == c) yield return n;
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
            Node_("p0","Quick Feet","+10% move speed",Category.Pace,2,null,">",0.5f,0,null, E("move",0.10f));
            Node_("p1a","Acceleration","+14% move speed",Category.Pace,3,"p0","»",0.25f,1,null, E("move",0.14f));
            Node_("p1b","Long Strides","+14% sprint speed",Category.Pace,3,"p0","=",0.75f,1,null, E("sprint",0.14f));
            Node_("p2a","Sharp Turns","+14% move speed",Category.Pace,4,"p1a","«",0.25f,2,null, E("move",0.14f));
            Node_("p2b","Flat Out","+18% sprint speed",Category.Pace,4,"p1b","==",0.75f,2,null, E("sprint",0.18f));
            Node_("pcap","Afterburners","Holding sprint ramps to a burst top speed",Category.Pace,7,"p2b","A",0.75f,3,"afterburners");

            // ========================== SHOOTING (shotpower, shotacc) ====================
            Node_("s0","Clean Strike","+12% shot power",Category.Shooting,2,null,"O",0.5f,0,null, E("shotpower",0.12f));
            Node_("s1a","Power","+16% shot power",Category.Shooting,3,"s0","!",0.25f,1,null, E("shotpower",0.16f));
            Node_("s1b","Placement","+22% shot accuracy",Category.Shooting,3,"s0","+",0.75f,1,null, E("shotacc",0.22f));
            Node_("s2a","Rising Shot","+16% shot power",Category.Shooting,4,"s1a","^",0.25f,2,null, E("shotpower",0.16f));
            Node_("s2b","Finesse","+24% shot accuracy",Category.Shooting,4,"s1b","x",0.75f,2,null, E("shotacc",0.24f));
            Node_("scap","Cannon","Big rise to your shot-speed ceiling",Category.Shooting,8,"s2a","C",0.25f,3,"cannon");

            // ==================== HEADING (headpower, headacc, jump, reach) ==============
            Node_("h0","Timing","+18% header accuracy",Category.Heading,2,null,"o",0.5f,0,null, E("headacc",0.18f));
            Node_("h1a","Power Header","+22% header power",Category.Heading,3,"h0","!",0.25f,1,null, E("headpower",0.22f));
            Node_("h1b","Leap","+12% jump height",Category.Heading,3,"h0","^",0.75f,1,null, E("jump",0.12f));
            Node_("h2a","Bullet Head","+22% header power",Category.Heading,4,"h1a",">>",0.25f,2,null, E("headpower",0.22f));
            Node_("h2b","Hang Time","+10% jump, +8% reach",Category.Heading,4,"h1b","T",0.75f,2,null, E("jump",0.10f), E("reach",0.08f));
            Node_("hcap","Aerial Threat","Headers keep pace and drive hard to goal",Category.Heading,7,"h2a","H",0.25f,3,"aerial");

            // ============================ STRENGTH (push, massbonus) =====================
            Node_("st0","Core","+14% push strength",Category.Strength,2,null,"#",0.5f,0,null, E("push",0.14f));
            Node_("st1a","Frame","+12% effective mass",Category.Strength,3,"st0","[]",0.25f,1,null, E("massbonus",0.12f));
            Node_("st1b","Balance","+16% push strength",Category.Strength,3,"st0","|",0.75f,1,null, E("push",0.16f));
            Node_("st2a","Powerhouse","+16% push, +8% mass",Category.Strength,4,"st1a","#!",0.25f,2,null, E("push",0.16f), E("massbonus",0.08f));
            Node_("st2b","Anchor","+18% push strength",Category.Strength,4,"st1b","V",0.75f,2,null, E("push",0.18f));
            Node_("stcap","Immovable","Very hard to shove; shoves back on contact",Category.Strength,7,"st2a","M",0.25f,3,"immovable");

            // ==================== CONTROL (trap, weakfoot, shotacc) ======================
            Node_("c0","First Touch","+25% trap control (ball settles closer)",Category.Control,2,null,".",0.5f,0,null, E("trap",0.25f));
            Node_("c1a","Cushion","+25% trap control",Category.Control,3,"c0","..",0.25f,1,null, E("trap",0.25f));
            Node_("c1b","Weak Foot","+35% weak-foot accuracy & power",Category.Control,3,"c0","L",0.75f,1,null, E("weakfoot",0.35f));
            Node_("c2a","Composure","+15% shot accuracy",Category.Control,4,"c1a","+",0.25f,2,null, E("shotacc",0.15f));
            Node_("c2b","Two-Footed","+35% weak-foot accuracy & power",Category.Control,4,"c1b","LR",0.75f,2,null, E("weakfoot",0.35f));
            Node_("ccap","Silky","Both feet strike as your strong foot",Category.Control,7,"c2b","S",0.75f,3,"silky");

            // ============================ AGILITY (flip, jump) ==========================
            Node_("a0","Spring","+10% jump height",Category.Agility,2,null,"^",0.5f,0,null, E("jump",0.10f));
            Node_("a1a","Nimble","+20% air-flip control, -15% ground recovery time",Category.Agility,3,"a0","@",0.25f,1,null, E("flip",0.20f), E("recovery",-0.15f));
            Node_("a1b","Bounce","+12% jump height",Category.Agility,3,"a0","^^",0.75f,1,null, E("jump",0.12f));
            Node_("a2a","Twist","+22% air-flip control, -20% ground recovery time",Category.Agility,4,"a1a","%",0.25f,2,null, E("flip",0.22f), E("recovery",-0.20f));
            Node_("a2b","Elevation","+12% jump height",Category.Agility,4,"a1b","^!",0.75f,2,null, E("jump",0.12f));
            Node_("acap","Acrobat","Whip-fast air control + snap off the ground for chained flips",Category.Agility,7,"a2a","X",0.25f,3,"acrobat");

            All = list.ToArray();
            foreach (var n in All) _byId[n.Id] = n;
        }
    }
}
