using System;
using System.Collections.Generic;
using System.Text;

// PURE FILE: no UnityEngine. It also compiles in a plain .NET console app: copy the pure Cup files
// beside a Program.cs that prints CupSelfTest.Run(). Inside Unity: Trickshot > Cup > Run self-test.

namespace Trickshot
{
    /// <summary>Thrown by <see cref="CupSelfTest.Run"/> on the first failed check; the message carries the report so far.</summary>
    public sealed class CupSelfTestException : Exception
    {
        public CupSelfTestException(string message) : base(message) { }
    }

    /// <summary>
    /// The cup foundation's own test suite: RNG determinism, the nation table's invariants, the
    /// shootout rules against worked examples, the simulator's termination and bias, and 1000
    /// brackets (seeds 1..1000, 0..8 humans) through draw, simulation, advancing and the wire
    /// round-trip. Returns a multi-line report; throws <see cref="CupSelfTestException"/> on the
    /// first failure.
    /// </summary>
    public static class CupSelfTest
    {
        static StringBuilder _out;
        static int _checks;

        public static string Run()
        {
            _out = new StringBuilder();
            _checks = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Line("Trickshot Cup self-test");
            TestStages();
            TestRng();
            TestBytes();
            TestNations();
            TestRules();
            TestSimCore();
            TestBrackets();
            Line("ALL PASSED: " + _checks + " checks in " + sw.ElapsedMilliseconds + " ms");
            return _out.ToString();
        }

        static void Line(string s) { _out.AppendLine(s); }

        static void Check(bool cond, string what)
        {
            _checks++;
            if (cond) return;
            throw new CupSelfTestException("FAILED: " + what + Environment.NewLine + "--- report so far ---" + Environment.NewLine + _out);
        }

        static void CheckThrows<T>(Action a, string what) where T : Exception
        {
            _checks++;
            try { a(); }
            catch (T) { return; }
            catch (Exception e) { throw new CupSelfTestException("FAILED: " + what + " threw " + e.GetType().Name + " instead of " + typeof(T).Name); }
            throw new CupSelfTestException("FAILED: " + what + " did not throw " + typeof(T).Name);
        }

        // ---- stages ----------------------------------------------------------------------------

        static void TestStages()
        {
            Check(CupStages.Count == 5, "five stages");
            Check(CupStages.RoundsIn(CupStage.RoundOf32) == 16 && CupStages.RoundsIn(CupStage.RoundOf16) == 8
                && CupStages.RoundsIn(CupStage.QuarterFinal) == 4 && CupStages.RoundsIn(CupStage.SemiFinal) == 2
                && CupStages.RoundsIn(CupStage.Final) == 1, "rounds per stage 16/8/4/2/1");
            Check(CupStages.Name(CupStage.RoundOf32) == "Round of 32" && CupStages.Name(CupStage.RoundOf16) == "Round of 16"
                && CupStages.Name(CupStage.QuarterFinal) == "Quarter-finals" && CupStages.Name(CupStage.SemiFinal) == "Semi-finals"
                && CupStages.Name(CupStage.Final) == "Final", "stage names");
            Check(CupStages.Short(CupStage.RoundOf32) == "R32" && CupStages.Short(CupStage.QuarterFinal) == "QF"
                && CupStages.Short(CupStage.Final) == "F", "stage short tags");
            Check(CupStages.Next(CupStage.RoundOf32) == CupStage.RoundOf16 && CupStages.Next(CupStage.SemiFinal) == CupStage.Final
                && CupStages.Next(CupStage.Final) == CupStage.Final, "Next chain");
            CupStage n;
            Check(!CupStages.TryNext(CupStage.Final, out n) && CupStages.TryNext(CupStage.QuarterFinal, out n) && n == CupStage.SemiFinal, "TryNext");
            Check(CupStages.Header(CupStage.RoundOf16) == "ROUND OF 16", "Header");
            Check(CupText.KnockedOutIn(CupStage.RoundOf16) == "KNOCKED OUT IN THE ROUND OF 16", "KnockedOutIn");
            Check(CupText.WinLine("Brazil", 4, 2) == "BRAZIL WIN 4-2", "WinLine");
            Check(CupText.KnockedOutLine(2, 3) == "KNOCKED OUT 2-3", "KnockedOutLine");
            Check(CupText.KickFirst("Ghana") == "GHANA KICK FIRST", "KickFirst");
            Check(CupText.ScoreLine(5, 4, true) == "5-4 SD" && CupText.ScoreLine(4, 2, false) == "4-2", "ScoreLine");
            Check(CupText.Label(CupStyle.HeadToHead, CupFormat.Penalties) == "Trickshot Cup - Head to Head - Penalties", "Label");
            Check(CupText.TitleTag(CupStyle.Coop, CupFormat.FreeKicks) == "TRICKSHOT CUP - CO-OP - FREE KICKS", "TitleTag");
            Check(CupText.ClickToSkipVotes(2, 3) == "CLICK TO SKIP  2/3", "ClickToSkipVotes");
            // The ladder was cut 20% (owner's call); assert the SHAPE as well as the ends, so a
            // future retune that flattens or inverts a step is caught rather than just a value change.
            Check(Math.Abs(CupTuning.KeeperAbility(CupStage.RoundOf32) - 0.16f) < 1e-5f
                  && Math.Abs(CupTuning.KeeperAbility(CupStage.Final) - 0.80f) < 1e-5f, "keeper ramp");
            for (int st = 1; st < CupStages.Count; st++)
                Check(CupTuning.KeeperAbility(CupStages.At(st)) > CupTuning.KeeperAbility(CupStages.At(st - 1)),
                      "keeper ramp rises at " + CupStages.Short(CupStages.At(st)));
            Check(Math.Abs(CupTuning.TakerCombined(CupStage.RoundOf32) - 0.47f) < 1e-5f && Math.Abs(CupTuning.TakerCombined(CupStage.Final) - 0.95f) < 1e-5f, "taker combined ramp");
            Check(Math.Abs(CupTuning.TakerPower(CupStage.QuarterFinal) - 0.73f) < 1e-5f, "taker power ramp");
            Line("stages: names, counts, ramp, text builders OK");
        }

        // ---- rng -------------------------------------------------------------------------------

        static void TestRng()
        {
            var a = new SeededRng(12345);
            var b = new SeededRng(12345);
            bool same = true;
            for (int i = 0; i < 1000; i++) if (a.NextUInt() != b.NextUInt()) { same = false; break; }
            Check(same, "same seed, same sequence");

            var s0 = new SeededRng(0);
            var s1 = new SeededRng(1);
            Check(s0.State != 0 && s1.State != 0, "state never 0 at start");
            Check(s0.NextUInt() != s1.NextUInt(), "seed 0 and seed 1 differ");

            var z = new SeededRng(0);
            bool nonZero = true;
            for (int i = 0; i < 100000; i++) if (z.NextUInt() == 0) { nonZero = false; break; }
            Check(nonZero && z.State != 0, "state never becomes 0 over 100k draws");

            var parent = new SeededRng(777);
            uint before = parent.State;
            var f1 = parent.Fork(1);
            var f2 = parent.Fork(2);
            var f1b = parent.Fork(1);
            Check(parent.State == before, "Fork does not advance the parent");
            bool forksDiffer = false;
            for (int i = 0; i < 16; i++) if (f1.NextUInt() != f2.NextUInt()) { forksDiffer = true; break; }
            Check(forksDiffer, "Fork(1) != Fork(2)");
            var f1c = parent.Fork(1);
            bool forkStable = true;
            for (int i = 0; i < 16; i++) if (f1b.NextUInt() != f1c.NextUInt()) { forkStable = false; break; }
            Check(forkStable, "Fork(1) twice is the same stream");
            Check(new SeededRng(1).Fork(5).NextUInt() != new SeededRng(2).Fork(5).NextUInt(), "forks of different seeds differ");

            var r = new SeededRng(99);
            bool in01 = true, inRange = true, inFloat = true;
            for (int i = 0; i < 100000; i++)
            {
                float f = r.Next01();
                if (f < 0f || f >= 1f) in01 = false;
                int v = r.Range(-7, 12);
                if (v < -7 || v >= 12) inRange = false;
                int w = r.Range(0, 1);
                if (w != 0) inRange = false;
                float g = r.Range(2.5f, 3.5f);
                if (g < 2.5f || g >= 3.5f) inFloat = false;
            }
            Check(in01, "Next01 in [0,1)");
            Check(inRange, "Range(int) within bounds");
            Check(inFloat, "Range(float) within bounds");
            Check(r.Range(5, 5) == 5 && r.Range(5, 4) == 5, "empty int range returns min");
            Check(!r.Chance(0f) && r.Chance(1f), "Chance(0) never, Chance(1) always");

            int heads = 0, chance = 0;
            for (int i = 0; i < 20000; i++)
            {
                if (r.Coin() == CoinFace.Heads) heads++;
                if (r.Chance(0.5f)) chance++;
            }
            Check(heads > 9400 && heads < 10600, "Coin roughly fair (" + heads + "/20000 heads)");
            Check(chance > 9400 && chance < 10600, "Chance(0.5) roughly half (" + chance + "/20000)");

            var list = new List<int>();
            for (int i = 0; i < 100; i++) list.Add(i);
            r.Shuffle(list);
            var seen = new bool[100];
            bool perm = list.Count == 100, moved = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] < 0 || list[i] >= 100 || seen[list[i]]) perm = false; else seen[list[i]] = true;
                if (list[i] != i) moved = true;
            }
            Check(perm && moved, "Shuffle is a permutation that moved something");
            Check(r.Pick(list) >= 0 && r.Pick(new List<int>()) == 0, "Pick");
            Line("rng: determinism, scramble, fork, ranges, coin, shuffle OK (heads " + heads + "/20000)");
        }

        // ---- bytes -----------------------------------------------------------------------------

        static void TestBytes()
        {
            var w = new CupByteWriter();
            w.U8(7); w.U16(65000); w.U32(0xDEADBEEFu); w.Slot(-1); w.Slot(200); w.Str("Cote d'Ivoire"); w.Bool(true); w.F(3.25f);
            w.Str(new string('x', 300));
            var r = new CupByteReader(w.ToArray());
            Check(r.U8() == 7 && r.U16() == 65000 && r.U32() == 0xDEADBEEFu, "byte scalars round-trip");
            Check(r.Slot() == -1 && r.Slot() == 200, "Slot round-trip");
            Check(r.Str() == "Cote d'Ivoire" && r.Bool() && r.F() == 3.25f, "Str/Bool/F round-trip");
            Check(r.Str().Length == 255 && !r.More, "long string truncates to 255 bytes and buffer ends");
            CheckThrows<FormatException>(() => new CupByteReader(new byte[] { 1 }).U16(), "truncated read throws FormatException");

            var k = new KickRecord(CupSide.B, KickOutcome.Miss);
            var k2 = KickRecord.FromNibble(k.ToNibble());
            Check(k2.Side == CupSide.B && k2.Outcome == KickOutcome.Miss, "kick nibble round-trip");
            Line("bytes: writer/reader, nibbles OK");
        }

        // ---- nations ---------------------------------------------------------------------------

        static void TestNations()
        {
            Check(CupNationTable.Count == 214, "214 nations (got " + CupNationTable.Count + ")");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var novelty = new List<string>();
            bool sorted = true;
            for (int i = 0; i < CupNationTable.Count; i++)
            {
                var n = CupNationTable.Get(i);
                Check(!string.IsNullOrEmpty(n.Name), "nation " + i + " has a name");
                Check(names.Add(n.Name), "duplicate nation name " + n.Name);
                Check(n.Code != null && n.Code.Length == 3, "code of " + n.Name + " is 3 letters");
                bool upper = true;
                foreach (char c in n.Code) if (c < 'A' || c > 'Z') upper = false;
                Check(upper, "code of " + n.Name + " is upper-case A-Z (" + n.Code + ")");
                Check(codes.Add(n.Code), "duplicate code " + n.Code + " (" + n.Name + ")");
                Check(n.Strength >= CupTuning.StrengthMin && n.Strength <= CupTuning.StrengthMax, "strength of " + n.Name + " in range");
                if (n.Novelty) novelty.Add(n.Name);
                if (i > 0 && string.Compare(CupNationTable.Get(i - 1).Name, n.Name, StringComparison.OrdinalIgnoreCase) >= 0) sorted = false;
                Check(CupNationTable.IndexOf(n.Name) == i, "IndexOf round-trips " + n.Name);
                Check(CupNationTable.IndexOfCode(n.Code) == i, "IndexOfCode round-trips " + n.Code);
            }
            Check(sorted, "table is sorted A-Z (ordinal, case-insensitive: the JerseyDesigns Nations order)");
            var expectedNovelty = new[] { "Antarctica", "Catalonia", "European Union", "Greenland", "Jolly Roger", "Olympic", "Pride Rainbow", "Soviet Union", "Vatican City" };
            Check(novelty.Count == expectedNovelty.Length, "9 novelty nations (got " + novelty.Count + ")");
            foreach (var e in expectedNovelty) Check(novelty.Contains(e), e + " is novelty");
            foreach (var must in new[] { "Cote d'Ivoire", "Congo (DR)", "Congo (Republic)", "USA", "Saint Vincent and the Grenadines", "Timor-Leste", "Bosnia and Herzegovina", "England", "Brazil" })
                Check(CupNationTable.IndexOf(must) >= 0, "table has " + must);
            Check(CupNationTable.IndexOf("Nowhere") == -1 && CupNationTable.IndexOf(null) == -1, "IndexOf unknown is -1");
            Check(CupNationTable.IndexOf("Uruguay") < CupNationTable.IndexOf("USA"), "Uruguay sorts before USA (ordinal order)");

            int poolCount = 0;
            foreach (int i in CupNationTable.PoolIndices)
            {
                poolCount++;
                Check(!CupNationTable.IsNovelty(i), "pool excludes novelty");
            }
            Check(poolCount == 214 - 9 && CupNationTable.PoolCount == poolCount, "pool has 205 nations (got " + poolCount + ")");
            Check(CupNationTable.StrengthOf(CupNationTable.IndexOf("Brazil")) >= 88 && CupNationTable.StrengthOf(CupNationTable.IndexOf("San Marino")) <= 20, "strengths read real-world-ish");
            CheckThrows<ArgumentOutOfRangeException>(() => CupNationTable.Get(214), "Get past the end throws");
            Line("nations: 214 rows, unique names/codes, 9 novelty, sorted, 205 in the pool OK");
        }

        // ---- rules -----------------------------------------------------------------------------

        // Script: outcomes for consecutive kicks from the first kicker: G = goal, S = saved, M = miss.
        static RoundLine Play(CupSide first, string script)
        {
            var line = new RoundLine(first);
            foreach (char c in script)
            {
                if (c == ' ') continue;
                var o = c == 'G' ? KickOutcome.Goal : c == 'S' ? KickOutcome.Saved : KickOutcome.Miss;
                CupRoundRules.Record(line, o);
            }
            return line;
        }

        static void Decided(string script, CupSide first, CupSide expectWinner, string what)
        {
            var line = Play(first, script);
            CupSide w;
            bool d = CupRoundRules.IsDecided(line, out w);
            Check(d && w == expectWinner, what + " -> decided for " + CupSides.Name(expectWinner) + " (" + CupRoundRules.Describe(line) + ")");
        }

        static void Undecided(string script, CupSide first, string what)
        {
            var line = Play(first, script);
            CupSide w;
            Check(!CupRoundRules.IsDecided(line, out w), what + " -> NOT decided (" + CupRoundRules.Describe(line) + ")");
        }

        static void TestRules()
        {
            var A = CupSide.A;
            var B = CupSide.B;

            // Early finish in regulation (A first: kicks alternate A B A B ...). The Play helper
            // throws if a script continues past a decision, so every "decided" case is decided
            // exactly on its last kick and every "undecided" case never was.
            Decided("GM GM GM", A, A, "3-0 after 3 each (B has 2 left)");
            Undecided("GM GM G", A, "3-0 with B on 2 taken (B has 3 left)");
            Undecided("GM GG GM M", A, "3-1 after A 4 / B 3 (B has 2 left, can level)");
            Decided("GM GG GM MM", A, A, "3-1 after 4 each (B has 1 left)");
            Decided("GM GG GM G", A, A, "4-1 after A 4 / B 3 (B has 2 left)");
            Decided("MG MG GG GG", A, B, "2-4 after 4 each, A has 1 left");
            Undecided("MG MG GG G", A, "2-3 after A 4 / B 3: A has 1 left and can level");
            Decided("GG MG MG M", A, B, "1-3 after A 4 / B 3: A has 1 left, cannot catch");
            Undecided("GG GG GG GG G", A, "5-4 with B's 5th to come");
            Decided("GG GG GG GG GM", A, A, "5-4 after 5 each: A wins on the last regulation kick");
            Decided("SS SS SS SS SG", A, B, "0-1 after 5 each: B wins on the last regulation kick");
            Decided("MG GM MG GM MG", A, B, "2-3 after 5 each: B wins");
            Undecided("GG GG GG GG GG", A, "5-5 after regulation -> sudden death");

            // B kicks first: alternation B A B A ...
            Decided("GM GM GM", B, B, "B first: 3-0 to B after 3 each");
            Undecided("GM GM G", B, "B first: 3-0 with A on 2 taken");

            // Sudden death.
            var sd = Play(A, "GG GG GG GG GG");
            Check(CupRoundRules.IsSuddenDeath(sd) && CupRoundRules.RegulationOver(sd) && CupRoundRules.NextKicker(sd) == A, "5-5 enters sudden death, A kicks next");
            Check(CupRoundRules.KickNumberFor(sd, A) == 6 && CupRoundRules.PairIndex(sd) == 5 && CupRoundRules.PairComplete(sd), "sudden death kick numbering");
            CupRoundRules.Record(sd, KickOutcome.Goal);
            CupSide w;
            Check(!CupRoundRules.IsDecided(sd, out w) && CupRoundRules.NextKicker(sd) == B, "6-5 mid-pair is NOT decided, B to kick");
            Check(CupRoundRules.KicksLeft(sd, B) == 1 && CupRoundRules.KicksLeft(sd, A) == 0, "kicks left inside a sudden-death pair");
            CupRoundRules.Record(sd, KickOutcome.Saved);
            Check(CupRoundRules.IsDecided(sd, out w) && w == A, "6-5 after a completed pair: A wins");
            Check(CupRoundRules.IsSuddenDeath(sd) && CupRoundRules.ScoreLine(sd) == "6-5 SD", "sudden-death score line");

            var sd2 = Play(A, "GG GG GG GG GG MM GG MG");
            Check(CupRoundRules.IsDecided(sd2, out w) && w == B && sd2.Count == 16, "pairs MM and GG continue, MG decides for B");
            Undecided("GG GG GG GG GG MM GG", A, "level pairs keep going");
            Check(!CupRoundRules.IsSuddenDeath(Play(A, "GM GM GM")), "an early finish is not sudden death");
            Check(!CupRoundRules.IsSuddenDeath(Play(A, "SS SS SS SS SG")), "a regulation decision on the 10th kick is not sudden death");

            // Recording guards.
            var done = Play(A, "GM GM GM");
            CheckThrows<InvalidOperationException>(() => CupRoundRules.RecordKick(done, B, KickOutcome.Goal), "kick after decision throws");
            var live = Play(A, "GM");
            CheckThrows<InvalidOperationException>(() => CupRoundRules.RecordKick(live, B, KickOutcome.Goal), "kick out of turn throws");
            Check(CupRoundRules.KickNumberFor(live, A) == 2 && CupRoundRules.KickNumberFor(live, B) == 2 && CupRoundRules.NextKicker(live) == A, "kick numbers after one pair");
            Check(CupRoundRules.PairIndex(Play(A, "")) == 0 && CupRoundRules.PairIndex(Play(A, "G")) == 0 && CupRoundRules.PairIndex(Play(A, "GG")) == 1 && CupRoundRules.PairIndex(Play(A, "GGG")) == 1, "free-kick spot index per pair");

            // Validate.
            RoundLine vl; string err;
            Check(CupRoundRules.Validate(done.Kicks, A, 5, true, out vl, out err) && vl.GoalsA == 3 && vl.GoalsB == 0, "Validate accepts a decided line");
            var bad = new List<KickRecord>(done.Kicks) { new KickRecord(B, KickOutcome.Goal) };
            Check(!CupRoundRules.Validate(bad, A, 5, true, out vl, out err) && err.Contains("after"), "Validate rejects a kick after the decision");
            var wrongOrder = new List<KickRecord> { new KickRecord(B, KickOutcome.Goal) };
            Check(!CupRoundRules.Validate(wrongOrder, A, 5, false, out vl, out err) && err.Contains("expected A"), "Validate rejects wrong alternation");
            Check(!CupRoundRules.Validate(live.Kicks, A, 5, true, out vl, out err) && err.Contains("not decided"), "Validate requires a decision when asked");
            Check(CupRoundRules.Validate(live.Kicks, A, 5, false, out vl, out err), "Validate accepts a live line when not asked for a decision");

            // The WIRE CAP (CupTuning.MaxKicksInLine). Alternation and the decidedness rules bound
            // nothing on their own, so this is the only thing standing between a modified client
            // and a 4 KB CupState. Build a level line of exactly the cap, which is legal, then one
            // pair longer, which is not.
            var atCap = new List<KickRecord>();
            for (int i = 0; i < CupTuning.MaxKicksInLine; i++)
                atCap.Add(new KickRecord(i % 2 == 0 ? A : B, KickOutcome.Goal));
            Check(CupRoundRules.Validate(atCap, A, 5, false, out vl, out err, CupTuning.MaxKicksInLine),
                  "Validate accepts a line exactly at the cap: " + err);
            var overCap = new List<KickRecord>(atCap) { new KickRecord(A, KickOutcome.Goal), new KickRecord(B, KickOutcome.Goal) };
            Check(!CupRoundRules.Validate(overCap, A, 5, false, out vl, out err, CupTuning.MaxKicksInLine) && err.Contains("cap"),
                  "Validate rejects a line over the cap");
            Check(CupRoundRules.Validate(overCap, A, 5, false, out vl, out err),
                  "no cap passed means no length limit (the pure tests and CupSim rely on this)");
            Check(CupSim.SimulateLine(50, 50, A, new SeededRng(12345)).Count <= CupTuning.MaxKicksInLine,
                  "a simulated line never exceeds the wire cap");

            // Co-op cycling and the coin.
            Check(CupRoundRules.CoopShooterFor(0, 5) == 0 && CupRoundRules.CoopShooterFor(4, 5) == 4 && CupRoundRules.CoopShooterFor(5, 5) == 0 && CupRoundRules.CoopShooterFor(6, 5) == 1, "co-op shooters cycle (kick 6 wraps to shooter 1)");
            Check(CupRoundRules.CoopShooterFor(3, 1) == 0 && CupRoundRules.CoopShooterFor(7, 7) == 0 && CupRoundRules.CoopShooterFor(2, 0) == 0, "co-op cycling edge cases");
            Check(CupRoundRules.FirstKickerFromCall(A, CoinFace.Heads, CoinFace.Heads) == A, "correct call kicks first (A)");
            Check(CupRoundRules.FirstKickerFromCall(A, CoinFace.Heads, CoinFace.Tails) == B, "wrong call hands it over (A calls, B kicks)");
            Check(CupRoundRules.FirstKickerFromCall(B, CoinFace.Tails, CoinFace.Tails) == B, "correct call kicks first (B)");
            Check(CupRoundRules.FirstKickerFromCall(B, CoinFace.Heads, CoinFace.Tails) == A, "wrong call hands it over (B calls, A kicks)");
            Check(CupRoundRules.KickerAt(B, 0) == B && CupRoundRules.KickerAt(B, 1) == A && CupRoundRules.KickerAt(B, 12) == B, "KickerAt alternation");
            Line("rules: early finish, sudden death pairs, guards, validate, cycling, coin OK");
        }

        // ---- sim -------------------------------------------------------------------------------

        static void TestSimCore()
        {
            // With strengths 1..99 the formula spans base-slope .. base+slope = 0.52 .. 0.92: the
            // ceiling is met exactly at the top, the 0.45 floor is a safety margin below the range.
            Check(Math.Abs(CupSim.GoalProbability(99, 1) - CupTuning.SimMaxP) < 1e-5f, "P(99 vs 1) clamps to max");
            float pLow = CupSim.GoalProbability(1, 99);
            Check(Math.Abs(pLow - (CupTuning.SimBaseGoalP - CupTuning.SimStrengthSlope)) < 1e-5f && pLow >= CupTuning.SimMinP, "P(1 vs 99) is base-slope, above the floor");
            Check(Math.Abs(CupSim.GoalProbability01(0f, 1f) - CupTuning.SimMinP) < 1e-5f || CupTuning.SimBaseGoalP - CupTuning.SimStrengthSlope >= CupTuning.SimMinP, "floor clamp consistent");
            Check(Math.Abs(CupSim.GoalProbability(50, 50) - CupTuning.SimBaseGoalP) < 1e-5f, "P(equal) is the base");
            Check(CupSim.GoalProbability(80, 40) > CupSim.GoalProbability(40, 80), "P rises with the taker's edge");

            int maxKicks = CupTuning.KicksEach * 2 + CupTuning.SimMaxSuddenDeathPairs * 2;
            var rng = new SeededRng(4242);
            int strongWins = 0, minK = int.MaxValue, maxK = 0, sdRounds = 0, capped = 0;
            const int N = 20000;
            for (int i = 0; i < N; i++)
            {
                var line = CupSim.SimulateLine(99, 1, CupSide.A, rng.Fork((uint)i));
                CupSide w;
                Check(CupRoundRules.IsDecided(line, out w), "simulated line is decided");
                if (w == CupSide.A) strongWins++;
                if (line.Count < minK) minK = line.Count;
                if (line.Count > maxK) maxK = line.Count;
            }
            Check(strongWins > N * 0.75, "99 beats 1 most of the time (" + strongWins + "/" + N + ")");
            Check(minK >= 6 && maxK <= maxKicks, "line length within [6, " + maxKicks + "] (got " + minK + ".." + maxK + ")");

            int evenWinsA = 0;
            minK = int.MaxValue; maxK = 0;
            for (int i = 0; i < N; i++)
            {
                var line = CupSim.SimulateLine(90, 90, i % 2 == 0 ? CupSide.A : CupSide.B, rng.Fork(100000u + (uint)i));
                CupSide w;
                Check(CupRoundRules.IsDecided(line, out w), "equal-strength line is decided");
                if (w == CupSide.A) evenWinsA++;
                if (CupRoundRules.IsSuddenDeath(line)) sdRounds++;
                if (line.Count == maxKicks) capped++;
                if (line.Count < minK) minK = line.Count;
                if (line.Count > maxK) maxK = line.Count;
                RoundLine v; string err;
                Check(CupRoundRules.Validate(line.Kicks, line.FirstKicker, line.KicksEach, true, out v, out err), "simulated line validates: " + err);
                Check(v.GoalsA == line.GoalsA && v.GoalsB == line.GoalsB, "validated line reproduces the score");
            }
            Check(maxK <= maxKicks, "sudden death terminates within " + maxKicks + " kicks (max " + maxK + ")");
            Check(evenWinsA > N * 0.45 && evenWinsA < N * 0.55, "equal strengths are even (" + evenWinsA + "/" + N + " for A)");
            Check(sdRounds > 0, "some equal rounds go to sudden death (" + sdRounds + ")");

            var d1 = CupSim.SimulateLine(70, 60, CupSide.B, new SeededRng(5));
            var d2 = CupSim.SimulateLine(70, 60, CupSide.B, new SeededRng(5));
            Check(CupRoundRules.Describe(d1) == CupRoundRules.Describe(d2), "simulation is deterministic per seed");
            Line("sim: bias, clamp, termination (99v1 wins " + strongWins + "/" + N + "; 90v90 SD " + sdRounds + ", capped " + capped + ", max " + maxK + " kicks) OK");
        }

        // ---- brackets --------------------------------------------------------------------------

        static void TestBrackets()
        {
            int sdTotal = 0, minKicks = int.MaxValue, maxKicks = 0, humansTotal = 0, bytesMax = 0, bytesMin = int.MaxValue;
            for (uint seed = 1; seed <= 1000; seed++)
            {
                int H = (int)(seed % 9);   // 0..8 humans
                var humans = new List<(int nationIndex, int humanSlot, string humanName)>();
                var used = new HashSet<int>();
                for (int k = 0; k < H; k++)
                {
                    // Distinct nations, with a novelty pick every fifth seed for the first human.
                    int idx = (k == 0 && seed % 5 == 0) ? CupNationTable.IndexOf("Jolly Roger") : (int)((seed * 37 + k * 53) % CupNationTable.Count);
                    while (!used.Add(idx)) idx = (idx + 1) % CupNationTable.Count;
                    humans.Add((idx, k, "P" + k));
                }
                humansTotal += H;

                var b = CupBracket.Build(seed, seed % 2 == 0 ? CupFormat.Penalties : CupFormat.FreeKicks, humans);
                Check(b.Seed == seed && b.Entrants.Count == 32, "seed " + seed + ": 32 entrants");

                var nations = new HashSet<int>();
                var humanNations = new HashSet<int>();
                foreach (var h in humans) humanNations.Add(h.nationIndex);
                for (int i = 0; i < 32; i++)
                {
                    var e = b.Entrants[i];
                    Check(CupNationTable.IsValid(e.NationIndex) && nations.Add(e.NationIndex), "seed " + seed + ": distinct nations");
                    if (!e.WasHuman)
                    {
                        Check(!CupNationTable.IsNovelty(e.NationIndex), "seed " + seed + ": no novelty AI");
                        Check(!humanNations.Contains(e.NationIndex), "seed " + seed + ": AI does not reuse a human nation");
                    }
                    var r = b.Stages[0][i / 2];
                    Check((i % 2 == 0 ? r.EntrantA : r.EntrantB) == i, "seed " + seed + ": entrant index == R32 slot");
                }
                for (int k = 0; k < H; k++)
                {
                    int e = b.EntrantOfHuman(k);
                    Check(e >= 0 && b.Entrants[e].NationIndex == humans[k].nationIndex && b.Entrants[e].HumanName == "P" + k && b.Entrants[e].IsHuman, "seed " + seed + ": human " + k + " seated");
                }
                var r32 = b.Stages[0];
                for (int i = 0; i < r32.Length; i++)
                {
                    int hc = (b.Entrants[r32[i].EntrantA].IsHuman ? 1 : 0) + (b.Entrants[r32[i].EntrantB].IsHuman ? 1 : 0);
                    Check(hc <= 1, "seed " + seed + ": humans in distinct R32 rounds");
                }
                Check(b.HumanRounds(CupStage.RoundOf32).Count == H && b.AiRounds(CupStage.RoundOf32).Count == 16 - H, "seed " + seed + ": human/AI round split");
                for (int s = 1; s < CupStages.Count; s++)
                    foreach (var r in b.Stages[s]) Check(!r.Ready && !r.Done, "seed " + seed + ": later stages empty");
                Check(b.CurrentStage == CupStage.RoundOf32 && b.Champion == -1 && !b.IsComplete, "seed " + seed + ": fresh state");

                // Determinism of the draw.
                var again = CupBracket.Build(seed, b.Format, humans);
                Check(again.DeepEquals(b), "seed " + seed + ": draw is deterministic");

                // Wire round-trip of the fresh draw.
                var fresh = b.ToBytes();
                var back = CupBracket.FromBytes(fresh);
                Check(back.DeepEquals(b) && Same(back.ToBytes(), fresh), "seed " + seed + ": fresh round-trip");
                if (fresh.Length < bytesMin) bytesMin = fresh.Length;

                // Play the whole cup by simulation, stage by stage (AI first, then everything).
                for (int s = 0; s < CupStages.Count; s++)
                {
                    var stage = (CupStage)s;
                    int ai = CupSim.SimulateStage(b, stage, new SeededRng(seed), true);
                    Check(ai == b.AiRounds(stage).Count, "seed " + seed + ": ai rounds simulated at " + CupStages.Short(stage));
                    if (s == 0)
                    {
                        // A human round left pending: NextRoundOf points at it; the stage is not complete.
                        if (H > 0)
                        {
                            int e = b.EntrantOfHuman(0);
                            var nr = b.NextRoundOf(e);
                            Check(nr != null && nr.Stage == CupStage.RoundOf32 && nr.Involves(e) && !nr.Done, "seed " + seed + ": NextRoundOf a pending human");
                            Check(!b.StageComplete(stage) && b.IsAlive(e, CupStage.RoundOf16), "seed " + seed + ": pending stage / alive");
                            CheckThrows<InvalidOperationException>(() => b.Advance(stage), "seed " + seed + ": Advance before complete throws");
                        }
                    }
                    CupSim.SimulateStage(b, stage, new SeededRng(seed), false);
                    Check(b.StageComplete(stage), "seed " + seed + ": " + CupStages.Short(stage) + " complete");
                    bool fed = b.Advance(stage);
                    Check(fed == !CupStages.IsLast(stage), "seed " + seed + ": Advance return");
                }
                Check(b.IsComplete && b.Champion >= 0 && b.IsChampion(b.Champion), "seed " + seed + ": champion crowned");
                Check(b.StageReached(b.Champion) == CupStage.Final && !b.IsEliminated(b.Champion) && b.NextRoundOf(b.Champion) == null, "seed " + seed + ": champion progress");

                // Every round: consistent, chained, and valid under the rules.
                for (int s = 0; s < CupStages.Count; s++)
                {
                    var stage = (CupStage)s;
                    foreach (var r in b.Stages[s])
                    {
                        Check(r.Done && r.Simulated && r.Ready && r.ScoreA != r.ScoreB && r.FirstKicker.HasValue, "seed " + seed + ": round resolved");
                        Check(r.WinnerEntrant == (r.ScoreA > r.ScoreB ? r.EntrantA : r.EntrantB) && r.WinnerSide.HasValue && r.LoserEntrant >= 0, "seed " + seed + ": winner matches score");
                        Check(r.GoalsOf(CupSide.A) == r.ScoreA && r.GoalsOf(CupSide.B) == r.ScoreB, "seed " + seed + ": kick line matches score");
                        RoundLine v; string err;
                        Check(CupRoundRules.Validate(r.Kicks, r.FirstKicker.Value, CupTuning.KicksEach, true, out v, out err), "seed " + seed + ": line validates: " + err);
                        Check(CupRoundRules.IsSuddenDeath(v) == r.SuddenDeath, "seed " + seed + ": SD flag matches the line");
                        if (r.SuddenDeath) sdTotal++;
                        if (r.Kicks.Count < minKicks) minKicks = r.Kicks.Count;
                        if (r.Kicks.Count > maxKicks) maxKicks = r.Kicks.Count;
                        var fed = b.FeedsInto(r);
                        if (fed != null)
                            Check(fed.Entrant(CupBracket.FeedSide(r)) == r.WinnerEntrant, "seed " + seed + ": winner fed to the next stage");
                        CupRound fa, fb;
                        b.FedBy(r, out fa, out fb);
                        if (s > 0) Check(fa.WinnerEntrant == r.EntrantA && fb.WinnerEntrant == r.EntrantB, "seed " + seed + ": FedBy");
                        var loserOut = b.EliminatedAt(r.LoserEntrant);
                        Check(loserOut.HasValue && loserOut.Value == stage && b.StageReached(r.LoserEntrant) == stage, "seed " + seed + ": loser eliminated at " + CupStages.Short(stage));
                        Check(!b.IsAlive(r.LoserEntrant, CupStages.Next(stage)) || CupStages.IsLast(stage), "seed " + seed + ": loser not alive next stage");
                        Check(b.IsAlive(r.LoserEntrant, stage), "seed " + seed + ": loser was alive for the stage it lost");
                    }
                }
                Check(b.Stages[4][0].Kicks.Count <= 30, "seed " + seed + ": final within 30 kicks");

                // Wire round-trip of the finished cup, and determinism of the whole simulation.
                var bytes = b.ToBytes();
                if (bytes.Length > bytesMax) bytesMax = bytes.Length;
                var copy = CupBracket.FromBytes(bytes);
                Check(copy.DeepEquals(b) && Same(copy.ToBytes(), bytes) && copy.Describe() == b.Describe(), "seed " + seed + ": finished round-trip");
                var replay = CupBracket.Build(seed, b.Format, humans);
                CupSim.SimulateRemaining(replay, CupStage.RoundOf32, new SeededRng(seed));
                Check(replay.DeepEquals(b), "seed " + seed + ": SimulateRemaining reproduces the stage-by-stage result");
                Check(b.Clone().DeepEquals(b), "seed " + seed + ": Clone");

                // Leaver: mark replaced, round-trips, HumanRounds excludes them.
                if (H > 0)
                {
                    var b2 = CupBracket.Build(seed, b.Format, humans);
                    int e0 = b2.EntrantOfHuman(0);
                    b2.MarkReplacedByAi(e0);
                    Check(!b2.Entrants[e0].IsHuman && b2.Entrants[e0].WasHuman && b2.Entrants[e0].DisplayName == "P0 (AI)", "seed " + seed + ": leaver marked");
                    Check(b2.HumanRounds(CupStage.RoundOf32).Count == H - 1 && b2.AiRounds(CupStage.RoundOf32).Count == 17 - H, "seed " + seed + ": leaver's round is an AI round");
                    Check(CupBracket.FromBytes(b2.ToBytes()).Entrants[e0].ReplacedByAi, "seed " + seed + ": leaver flag rides the wire");
                    Check(b2.HumanEntrants(false).Count == H - 1 && b2.HumanEntrants(true).Count == H, "seed " + seed + ": HumanEntrants with/without leavers");
                }
            }

            // Simulate-the-rest, one press per stage.
            var press = CupBracket.Build(31337, CupFormat.Penalties, new List<(int, int, string)> { (CupNationTable.IndexOf("Brazil"), 3, "Alice") });
            int presses = 0;
            while (CupSim.SimulateNextStage(press, new SeededRng(31337)).HasValue) presses++;
            Check(presses == 5 && press.IsComplete, "SimulateNextStage completes in 5 presses");
            var whole = CupBracket.Build(31337, CupFormat.Penalties, new List<(int, int, string)> { (CupNationTable.IndexOf("Brazil"), 3, "Alice") });
            CupSim.SimulateRemaining(whole, CupStage.RoundOf32, new SeededRng(31337));
            Check(whole.DeepEquals(press), "press-by-press equals one SimulateRemaining");

            // A played (not simulated) human result set from a RoundLine, then the stage advances.
            var played = CupBracket.Build(2024, CupFormat.FreeKicks, new List<(int, int, string)> { (CupNationTable.IndexOf("Wales"), 0, "Bob") });
            int bob = played.EntrantOfHuman(0);
            var bobRound = played.RoundOfEntrant(CupStage.RoundOf32, bob);
            var line = new RoundLine(CupRoundRules.FirstKickerFromCall(bobRound.SideOf(bob).Value, CoinFace.Heads, CoinFace.Heads));
            while (!CupRoundRules.IsOver(line)) CupRoundRules.Record(line, CupRoundRules.NextKicker(line) == bobRound.SideOf(bob).Value ? KickOutcome.Goal : KickOutcome.Saved);
            played.SetResult(bobRound, line, false);
            Check(bobRound.Done && !bobRound.Simulated && bobRound.WinnerEntrant == bob && bobRound.Kicks.Count == 6, "played result from a RoundLine");
            CupSim.SimulateStage(played, CupStage.RoundOf32, new SeededRng(2024), true);
            played.Advance(CupStage.RoundOf32);
            var bobNext = played.NextRoundOf(bob);
            Check(bobNext != null && bobNext.Stage == CupStage.RoundOf16 && bobNext.Ready && bobNext.Involves(bob), "human advanced to the Round of 16");
            CheckThrows<ArgumentException>(() => played.SetResult(bobNext, 2, 2, null, false, CupSide.A, false), "level result throws");
            CheckThrows<InvalidOperationException>(() => played.SetResult(played.Stages[2][0], 1, 0, null, false, CupSide.A, false), "result on a round without entrants throws");

            // Build guards.
            CheckThrows<ArgumentException>(() => CupBracket.Build(1, CupFormat.Penalties, new List<(int, int, string)> { (0, 0, "a"), (0, 1, "b") }), "two humans on one nation throws");
            CheckThrows<ArgumentException>(() => CupBracket.Build(1, CupFormat.Penalties, new List<(int, int, string)> { (0, 0, "a"), (1, 0, "b") }), "two humans on one slot throws");
            CheckThrows<ArgumentException>(() => CupBracket.Build(1, CupFormat.Penalties, new List<(int, int, string)> { (999, 0, "a") }), "bad nation index throws");
            CheckThrows<FormatException>(() => CupBracket.FromBytes(new byte[] { 99, 0, 0 }), "wrong wire version throws");
            CheckThrows<FormatException>(() => CupBracket.FromBytes(new byte[] { CupBracket.WireVersion, 1, 2 }), "truncated bracket throws");

            Line("brackets: 1000 seeds, " + humansTotal + " human seats, all 31000 rounds resolved; kicks " + minKicks + ".." + maxKicks
                + ", sudden death " + sdTotal + "; wire " + bytesMin + ".." + bytesMax + " bytes OK");
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
