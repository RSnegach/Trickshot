using System;
using System.Collections.Generic;
using System.Text;

// PURE FILE: no UnityEngine. It also compiles in a plain .NET console app (see CupSelfTest).

namespace Trickshot
{
    /// <summary>
    /// One of the 32 nations in a bracket. An entrant is a NATION; the human fields say who plays
    /// it. In Co-op the whole team is one entrant (pass the Captain's slot and a team name, e.g.
    /// CupText.YourTeam, to <see cref="CupBracket.Build"/>).
    /// </summary>
    public sealed class CupEntrant
    {
        /// <summary>Index into <see cref="CupNationTable"/>.</summary>
        public int NationIndex;
        /// <summary>The net slot of the human playing this nation, or -1 for an AI nation.</summary>
        public int HumanSlot = -1;
        /// <summary>The human's display name (null for an AI nation).</summary>
        public string HumanName;
        /// <summary>Set when the human left mid-cup: an AI plays the nation from here, the row reads "Alice (AI)".</summary>
        public bool ReplacedByAi;

        /// <summary>A human was ever attached to this nation (still true for a leaver).</summary>
        public bool WasHuman => HumanSlot >= 0;
        /// <summary>A human is CURRENTLY in control (false for a pure AI nation and for a leaver).</summary>
        public bool IsHuman => HumanSlot >= 0 && !ReplacedByAi;

        public CupNation Nation => CupNationTable.Get(NationIndex);
        public string Name => CupNationTable.NameOf(NationIndex);
        public string Code => CupNationTable.CodeOf(NationIndex);
        public int Strength => CupNationTable.StrengthOf(NationIndex);

        /// <summary>The name a roster row shows: the human's name, "Alice (AI)" for a leaver, the nation for an AI.</summary>
        public string DisplayName
        {
            get
            {
                if (!WasHuman) return Name;
                string n = string.IsNullOrEmpty(HumanName) ? Name : HumanName;
                return ReplacedByAi ? CupText.AiName(n) : n;
            }
        }

        public CupEntrant() { }

        public CupEntrant(int nationIndex, int humanSlot = -1, string humanName = null)
        {
            NationIndex = nationIndex;
            HumanSlot = humanSlot;
            HumanName = humanName;
        }

        public CupEntrant Clone()
        {
            return new CupEntrant(NationIndex, HumanSlot, HumanName) { ReplacedByAi = ReplacedByAi };
        }

        public bool SameAs(CupEntrant o)
        {
            return o != null && NationIndex == o.NationIndex && HumanSlot == o.HumanSlot
                && string.Equals(HumanName ?? "", o.HumanName ?? "", StringComparison.Ordinal)
                && ReplacedByAi == o.ReplacedByAi;
        }

        public void WriteTo(CupByteWriter w)
        {
            w.U16(NationIndex);
            w.Slot(HumanSlot);
            w.Str(HumanName);
            w.U8(ReplacedByAi ? 1 : 0);
        }

        public static CupEntrant ReadFrom(CupByteReader r)
        {
            var e = new CupEntrant();
            e.NationIndex = r.U16();
            e.HumanSlot = r.Slot();
            e.HumanName = r.Str();
            if (e.HumanName.Length == 0) e.HumanName = null;
            e.ReplacedByAi = (r.U8() & 1) != 0;
            if (!CupNationTable.IsValid(e.NationIndex))
                throw new FormatException("CupEntrant: nation index " + e.NationIndex + " is out of the table");
            return e;
        }

        public override string ToString()
        {
            return WasHuman ? Code + " (" + DisplayName + ", slot " + HumanSlot + ")" : Code;
        }
    }

    /// <summary>One kick of a round: who took it and what happened. Two bits + one on the wire.</summary>
    public sealed class KickRecord
    {
        public CupSide Side;
        public KickOutcome Outcome;

        public KickRecord() { }

        public KickRecord(CupSide side, KickOutcome outcome)
        {
            Side = side;
            Outcome = outcome;
        }

        public bool Scored => Outcome == KickOutcome.Goal;

        public KickRecord Clone() => new KickRecord(Side, Outcome);

        public bool SameAs(KickRecord o) => o != null && Side == o.Side && Outcome == o.Outcome;

        /// <summary>Packs into a nibble: bit 2 = side, bits 0-1 = outcome.</summary>
        public int ToNibble() => ((int)Side << 2) | ((int)Outcome & 3);

        public static KickRecord FromNibble(int n)
        {
            return new KickRecord((CupSide)((n >> 2) & 1), (KickOutcome)(n & 3));
        }

        public override string ToString() => CupSides.Name(Side) + ":" + CupText.Verdict(Outcome);
    }

    /// <summary>
    /// One match between two nations (a ROUND, never a "tie"). Entrants are indices into
    /// <see cref="CupBracket.Entrants"/>; -1 means still to be decided by the stage before.
    /// </summary>
    public sealed class CupRound
    {
        public CupStage Stage;
        /// <summary>0-based position within the stage; round i feeds round i/2 of the next stage, side i%2.</summary>
        public int Index;
        public int EntrantA = -1;
        public int EntrantB = -1;
        public int ScoreA;
        public int ScoreB;
        /// <summary>The kick line in order taken (the results list shows it as pips).</summary>
        public List<KickRecord> Kicks = new List<KickRecord>();
        /// <summary>The round went past regulation.</summary>
        public bool SuddenDeath;
        /// <summary>Who took the first kick (the coin winner); null until the toss.</summary>
        public CupSide? FirstKicker;
        /// <summary>Entrant index of the winner; -1 while pending.</summary>
        public int WinnerEntrant = -1;
        /// <summary>Resolved by CupSim rather than played.</summary>
        public bool Simulated;
        /// <summary>The result is in.</summary>
        public bool Done;

        public CupRound() { }

        public CupRound(CupStage stage, int index)
        {
            Stage = stage;
            Index = index;
        }

        /// <summary>Both entrants are known, so the round can be played or simulated.</summary>
        public bool Ready => EntrantA >= 0 && EntrantB >= 0;

        /// <summary>The winning side, or null while pending.</summary>
        public CupSide? WinnerSide
        {
            get
            {
                if (!Done || WinnerEntrant < 0) return null;
                return WinnerEntrant == EntrantA ? CupSide.A : CupSide.B;
            }
        }

        /// <summary>The losing entrant, or -1 while pending.</summary>
        public int LoserEntrant
        {
            get
            {
                if (!Done || WinnerEntrant < 0) return -1;
                return WinnerEntrant == EntrantA ? EntrantB : EntrantA;
            }
        }

        public int Entrant(CupSide s) => s == CupSide.A ? EntrantA : EntrantB;
        public int ScoreOf(CupSide s) => s == CupSide.A ? ScoreA : ScoreB;

        public bool Involves(int entrant) => entrant >= 0 && (EntrantA == entrant || EntrantB == entrant);

        /// <summary>Which side an entrant is on, or null if not in this round.</summary>
        public CupSide? SideOf(int entrant)
        {
            if (entrant < 0) return null;
            if (EntrantA == entrant) return CupSide.A;
            if (EntrantB == entrant) return CupSide.B;
            return null;
        }

        /// <summary>The other entrant of the round, or -1.</summary>
        public int OpponentOf(int entrant)
        {
            if (entrant < 0) return -1;
            if (EntrantA == entrant) return EntrantB;
            if (EntrantB == entrant) return EntrantA;
            return -1;
        }

        /// <summary>"4-2" or "5-4 SD" (A first).</summary>
        public string ScoreLine => CupText.ScoreLine(ScoreA, ScoreB, SuddenDeath);

        public int KicksTaken(CupSide side)
        {
            int n = 0;
            for (int i = 0; i < Kicks.Count; i++) if (Kicks[i].Side == side) n++;
            return n;
        }

        public int GoalsOf(CupSide side)
        {
            int n = 0;
            for (int i = 0; i < Kicks.Count; i++) if (Kicks[i].Side == side && Kicks[i].Scored) n++;
            return n;
        }

        /// <summary>Clear the result (entrants stay). Used when a stage is re-advanced.</summary>
        public void ResetResult()
        {
            ScoreA = ScoreB = 0;
            Kicks.Clear();
            SuddenDeath = false;
            FirstKicker = null;
            WinnerEntrant = -1;
            Simulated = false;
            Done = false;
        }

        /// <summary>
        /// The kick line as a <see cref="RoundLine"/> for the rules (a copy). Needs FirstKicker; a
        /// round without one gets side A as the first kicker, which is only right when Kicks is empty.
        /// </summary>
        public RoundLine ToLine(int kicksEach = CupTuning.KicksEach)
        {
            var line = new RoundLine(FirstKicker ?? CupSide.A, kicksEach);
            for (int i = 0; i < Kicks.Count; i++) line.Kicks.Add(Kicks[i].Clone());
            return line;
        }

        public CupRound Clone()
        {
            var r = new CupRound(Stage, Index)
            {
                EntrantA = EntrantA, EntrantB = EntrantB, ScoreA = ScoreA, ScoreB = ScoreB,
                SuddenDeath = SuddenDeath, FirstKicker = FirstKicker, WinnerEntrant = WinnerEntrant,
                Simulated = Simulated, Done = Done,
            };
            for (int i = 0; i < Kicks.Count; i++) r.Kicks.Add(Kicks[i].Clone());
            return r;
        }

        public bool SameAs(CupRound o)
        {
            if (o == null) return false;
            if (Stage != o.Stage || Index != o.Index || EntrantA != o.EntrantA || EntrantB != o.EntrantB) return false;
            if (ScoreA != o.ScoreA || ScoreB != o.ScoreB || SuddenDeath != o.SuddenDeath) return false;
            if (FirstKicker != o.FirstKicker || WinnerEntrant != o.WinnerEntrant) return false;
            if (Simulated != o.Simulated || Done != o.Done) return false;
            if (Kicks.Count != o.Kicks.Count) return false;
            for (int i = 0; i < Kicks.Count; i++) if (!Kicks[i].SameAs(o.Kicks[i])) return false;
            return true;
        }

        // Flag bits of the wire record.
        const int FlagDone = 1, FlagSimulated = 2, FlagSuddenDeath = 4, FlagHasFirst = 8, FlagFirstIsB = 16;

        /// <summary>Stage, index, entrants, scores, flags, winner, and the kick line packed two per byte.</summary>
        public void WriteTo(CupByteWriter w)
        {
            w.U8((int)Stage);
            w.U8(Index);
            w.Slot(EntrantA);
            w.Slot(EntrantB);
            w.U8(Math.Max(0, Math.Min(255, ScoreA)));
            w.U8(Math.Max(0, Math.Min(255, ScoreB)));
            int flags = 0;
            if (Done) flags |= FlagDone;
            if (Simulated) flags |= FlagSimulated;
            if (SuddenDeath) flags |= FlagSuddenDeath;
            if (FirstKicker.HasValue)
            {
                flags |= FlagHasFirst;
                if (FirstKicker.Value == CupSide.B) flags |= FlagFirstIsB;
            }
            w.U8(flags);
            w.Slot(WinnerEntrant);
            int n = Math.Min(Kicks.Count, 255);
            w.U8(n);
            for (int i = 0; i < n; i += 2)
            {
                int lo = Kicks[i].ToNibble();
                int hi = i + 1 < n ? Kicks[i + 1].ToNibble() : 0;
                w.U8(lo | (hi << 4));
            }
        }

        public static CupRound ReadFrom(CupByteReader r)
        {
            var round = new CupRound();
            round.Stage = (CupStage)r.U8();
            if (!CupStages.IsValid(round.Stage)) throw new FormatException("CupRound: bad stage " + (int)round.Stage);
            round.Index = r.U8();
            round.EntrantA = r.Slot();
            round.EntrantB = r.Slot();
            round.ScoreA = r.U8();
            round.ScoreB = r.U8();
            int flags = r.U8();
            round.Done = (flags & FlagDone) != 0;
            round.Simulated = (flags & FlagSimulated) != 0;
            round.SuddenDeath = (flags & FlagSuddenDeath) != 0;
            round.FirstKicker = (flags & FlagHasFirst) != 0
                ? ((flags & FlagFirstIsB) != 0 ? CupSide.B : CupSide.A)
                : (CupSide?)null;
            round.WinnerEntrant = r.Slot();
            int n = r.U8();
            round.Kicks = new List<KickRecord>(n);
            for (int i = 0; i < n; i += 2)
            {
                int b = r.U8();
                round.Kicks.Add(KickRecord.FromNibble(b & 15));
                if (i + 1 < n) round.Kicks.Add(KickRecord.FromNibble((b >> 4) & 15));
            }
            return round;
        }

        /// <summary>One log line: "QF #1: BRA (Alice) 4-2 GHA -> BRA" (needs the bracket for names).</summary>
        public string Describe(CupBracket b)
        {
            var sb = new StringBuilder();
            sb.Append(CupStages.Short(Stage)).Append(" #").Append(Index).Append(": ");
            sb.Append(EntrantLabel(b, EntrantA));
            if (Done) sb.Append(' ').Append(ScoreLine).Append(' ');
            else sb.Append(" vs ");
            sb.Append(EntrantLabel(b, EntrantB));
            if (Done)
            {
                sb.Append(" -> ").Append(EntrantLabel(b, WinnerEntrant));
                if (Simulated) sb.Append(" (sim)");
                if (Kicks.Count > 0)
                {
                    sb.Append(" [");
                    for (int i = 0; i < Kicks.Count; i++)
                    {
                        if (i > 0) sb.Append(' ');
                        sb.Append(Kicks[i]);
                    }
                    sb.Append(']');
                }
            }
            return sb.ToString();
        }

        static string EntrantLabel(CupBracket b, int e)
        {
            if (e < 0) return "?";
            if (b == null || e >= b.Entrants.Count) return "#" + e;
            var en = b.Entrants[e];
            return en.WasHuman ? en.Code + " (" + en.DisplayName + ")" : en.Code;
        }

        public override string ToString() => Describe(null);
    }

    /// <summary>
    /// The whole cup as pure data: 32 entrants and 31 rounds in five stages, plus the draw and
    /// the advancing logic. Everything derives from ONE seed (the draw uses
    /// <c>new SeededRng(seed).Fork(CupSalts.Draw)</c>), so peers never sync the shape, only the
    /// results of rounds humans played.
    ///
    /// Layout: entrant index == Round-of-32 slot (round i has entrants 2i and 2i+1), round i of a
    /// stage feeds round i/2 of the next on side i%2, so the tree reads left half / right half with
    /// the Final in the middle.
    /// </summary>
    public sealed class CupBracket
    {
        /// <summary>Wire format version, the first byte of <see cref="ToBytes"/>.</summary>
        public const byte WireVersion = 1;
        public const int EntrantCount = CupTuning.Entrants;
        /// <summary>Humans must occupy distinct Round of 32 rounds, so at most 16 can be seeded.</summary>
        public const int MaxHumansInDraw = EntrantCount / 2;

        public uint Seed;
        public CupFormat Format;
        public List<CupEntrant> Entrants = new List<CupEntrant>(EntrantCount);
        /// <summary>Stages[stage][index]; 16, 8, 4, 2, 1 rounds.</summary>
        public CupRound[][] Stages;

        CupBracket()
        {
            Stages = EmptyStages();
        }

        static CupRound[][] EmptyStages()
        {
            var stages = new CupRound[CupStages.Count][];
            for (int s = 0; s < CupStages.Count; s++)
            {
                var stage = (CupStage)s;
                int n = CupStages.RoundsIn(stage);
                stages[s] = new CupRound[n];
                for (int i = 0; i < n; i++) stages[s][i] = new CupRound(stage, i);
            }
            return stages;
        }

        // ---- the draw ------------------------------------------------------------------------

        /// <summary>
        /// Draw a 32-nation bracket. Every human nation is in; the rest are drawn from the
        /// non-novelty pool with the seeded RNG; humans are dealt to DISTINCT Round of 32 rounds
        /// (rounds and sides chosen by the RNG), AI nations fill the remaining slots at random.
        /// Humans are sorted by slot before dealing, so caller order does not matter.
        /// </summary>
        /// <param name="seed">The cup seed (host's MatchConfig.fkSeed, or a Solo roll).</param>
        /// <param name="format">Penalties or Free Kicks (carried for the record; the draw ignores it).</param>
        /// <param name="humans">
        /// (nationIndex, humanSlot, humanName) per human nation - at most 16, distinct slots,
        /// distinct nations (novelty allowed). Co-op passes ONE entry for the whole team. Null or
        /// empty draws an all-AI bracket.
        /// </param>
        /// <param name="aiPool">
        /// Optional restriction of the AI pool (e.g. CupNations.ResolvedPool() to skip a table row
        /// whose design no longer resolves). Novelty rows and human nations are always removed.
        /// Defaults to CupNationTable.PoolIndices. Every peer must pass the same pool.
        /// </param>
        public static CupBracket Build(uint seed, CupFormat format,
            IList<(int nationIndex, int humanSlot, string humanName)> humans, IList<int> aiPool = null)
        {
            var list = new List<(int nationIndex, int humanSlot, string humanName)>();
            if (humans != null) list.AddRange(humans);
            if (list.Count > MaxHumansInDraw)
                throw new ArgumentException("CupBracket.Build: at most " + MaxHumansInDraw + " humans can be seeded, got " + list.Count);

            var humanNations = new HashSet<int>();
            var humanSlots = new HashSet<int>();
            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i];
                if (!CupNationTable.IsValid(h.nationIndex))
                    throw new ArgumentException("CupBracket.Build: human " + i + " has nation index " + h.nationIndex + " outside the table");
                if (h.humanSlot < 0)
                    throw new ArgumentException("CupBracket.Build: human " + i + " needs a slot >= 0 (got " + h.humanSlot + ")");
                if (!humanSlots.Add(h.humanSlot))
                    throw new ArgumentException("CupBracket.Build: slot " + h.humanSlot + " is listed twice");
                if (!humanNations.Add(h.nationIndex))
                    throw new ArgumentException("CupBracket.Build: nation " + CupNationTable.NameOf(h.nationIndex) + " is picked by two humans");
            }
            // Sort by slot (distinct, so the order is total) so the deal is a function of the seed
            // and the SET of humans only, never of the order a caller listed them in.
            list.Sort((x, y) => x.humanSlot.CompareTo(y.humanSlot));

            // The AI pool: non-novelty, not a human's nation, no duplicates, in table order.
            var pool = new List<int>(CupNationTable.Count);
            var seen = new HashSet<int>();
            IEnumerable<int> source = aiPool != null ? (IEnumerable<int>)aiPool : CupNationTable.PoolIndices;
            foreach (int n in source)
            {
                if (!CupNationTable.IsValid(n) || CupNationTable.IsNovelty(n)) continue;
                if (humanNations.Contains(n) || !seen.Add(n)) continue;
                pool.Add(n);
            }
            int need = EntrantCount - list.Count;
            if (pool.Count < need)
                throw new InvalidOperationException("CupBracket.Build: the AI pool has " + pool.Count + " nations, need " + need);

            var rng = new SeededRng(seed).Fork(CupSalts.Draw);

            // 1. Which AI nations are in.
            rng.Shuffle(pool);
            var ai = pool.GetRange(0, need);

            // 2. Humans to distinct Round of 32 rounds, a random side each.
            int rounds = CupStages.RoundsIn(CupStage.RoundOf32);
            var roundOrder = new List<int>(rounds);
            for (int i = 0; i < rounds; i++) roundOrder.Add(i);
            rng.Shuffle(roundOrder);

            var slots = new CupEntrant[EntrantCount];
            for (int k = 0; k < list.Count; k++)
            {
                var h = list[k];
                int side = rng.Coin() == CoinFace.Heads ? 0 : 1;
                slots[roundOrder[k] * 2 + side] = new CupEntrant(h.nationIndex, h.humanSlot, h.humanName);
            }

            // 3. AI nations (already shuffled) into the free slots in order.
            int next = 0;
            for (int s = 0; s < EntrantCount; s++)
            {
                if (slots[s] != null) continue;
                slots[s] = new CupEntrant(ai[next++]);
            }

            var b = new CupBracket { Seed = seed, Format = format };
            for (int s = 0; s < EntrantCount; s++) b.Entrants.Add(slots[s]);
            var r32 = b.Stages[0];
            for (int i = 0; i < r32.Length; i++)
            {
                r32[i].EntrantA = i * 2;
                r32[i].EntrantB = i * 2 + 1;
            }
            return b;
        }

        // ---- lookups -------------------------------------------------------------------------

        public IReadOnlyList<CupRound> RoundsOf(CupStage stage) => Stages[(int)stage];

        public CupRound Round(CupStage stage, int index)
        {
            var rounds = Stages[(int)stage];
            if (index < 0 || index >= rounds.Length)
                throw new ArgumentOutOfRangeException(nameof(index), "CupBracket: " + CupStages.Short(stage) + " has no round " + index);
            return rounds[index];
        }

        /// <summary>The round of a stage an entrant is in, or null (not there yet, or eliminated).</summary>
        public CupRound RoundOfEntrant(CupStage stage, int entrant)
        {
            if (entrant < 0) return null;
            var rounds = Stages[(int)stage];
            for (int i = 0; i < rounds.Length; i++)
                if (rounds[i].Involves(entrant)) return rounds[i];
            return null;
        }

        /// <summary>The round of the NEXT stage that this round feeds, or null at the Final.</summary>
        public CupRound FeedsInto(CupRound r)
        {
            if (r == null || CupStages.IsLast(r.Stage)) return null;
            return Stages[(int)r.Stage + 1][r.Index / 2];
        }

        /// <summary>The side of the next-stage round this round's winner takes: A for even indices, B for odd.</summary>
        public static CupSide FeedSide(CupRound r) => (r.Index & 1) == 0 ? CupSide.A : CupSide.B;

        /// <summary>The two rounds of the previous stage that feed a round (null, null at the Round of 32).</summary>
        public void FedBy(CupRound r, out CupRound fromA, out CupRound fromB)
        {
            fromA = fromB = null;
            if (r == null || CupStages.IsFirst(r.Stage)) return;
            var prev = Stages[(int)r.Stage - 1];
            fromA = prev[r.Index * 2];
            fromB = prev[r.Index * 2 + 1];
        }

        /// <summary>Rounds of the stage with at least one human (currently) in control, both entrants known.</summary>
        public List<CupRound> HumanRounds(CupStage stage)
        {
            var list = new List<CupRound>();
            var rounds = Stages[(int)stage];
            for (int i = 0; i < rounds.Length; i++)
            {
                var r = rounds[i];
                if (!r.Ready) continue;
                if (Entrants[r.EntrantA].IsHuman || Entrants[r.EntrantB].IsHuman) list.Add(r);
            }
            return list;
        }

        /// <summary>Rounds of the stage with no human in control, both entrants known (the ones CupSim resolves).</summary>
        public List<CupRound> AiRounds(CupStage stage)
        {
            var list = new List<CupRound>();
            var rounds = Stages[(int)stage];
            for (int i = 0; i < rounds.Length; i++)
            {
                var r = rounds[i];
                if (!r.Ready) continue;
                if (!Entrants[r.EntrantA].IsHuman && !Entrants[r.EntrantB].IsHuman) list.Add(r);
            }
            return list;
        }

        /// <summary>Rounds of the stage still to be resolved (both entrants known, not Done).</summary>
        public List<CupRound> PendingRounds(CupStage stage)
        {
            var list = new List<CupRound>();
            var rounds = Stages[(int)stage];
            for (int i = 0; i < rounds.Length; i++)
                if (rounds[i].Ready && !rounds[i].Done) list.Add(rounds[i]);
            return list;
        }

        public bool StageComplete(CupStage stage)
        {
            var rounds = Stages[(int)stage];
            for (int i = 0; i < rounds.Length; i++)
                if (!rounds[i].Done) return false;
            return true;
        }

        /// <summary>The lowest stage with a round still to play; the Final once everything is done.</summary>
        public CupStage CurrentStage
        {
            get
            {
                for (int s = 0; s < CupStages.Count; s++)
                    if (!StageComplete((CupStage)s)) return (CupStage)s;
                return CupStage.Final;
            }
        }

        /// <summary>The Final's winner, or -1 while pending.</summary>
        public int Champion
        {
            get
            {
                var f = Stages[(int)CupStage.Final][0];
                return f.Done ? f.WinnerEntrant : -1;
            }
        }

        public bool IsComplete => Champion >= 0;

        public bool IsChampion(int entrant) => entrant >= 0 && Champion == entrant;

        // ---- entrants ------------------------------------------------------------------------

        public bool IsValidEntrant(int entrant) => entrant >= 0 && entrant < Entrants.Count;

        public CupNation NationOf(int entrant) => Entrants[entrant].Nation;

        /// <summary>The entrant a human slot plays (a leaver's nation still answers), or -1.</summary>
        public int EntrantOfHuman(int humanSlot)
        {
            if (humanSlot < 0) return -1;
            for (int i = 0; i < Entrants.Count; i++)
                if (Entrants[i].HumanSlot == humanSlot) return i;
            return -1;
        }

        /// <summary>The entrant playing a nation, or -1.</summary>
        public int EntrantOfNation(int nationIndex)
        {
            for (int i = 0; i < Entrants.Count; i++)
                if (Entrants[i].NationIndex == nationIndex) return i;
            return -1;
        }

        /// <summary>Every entrant a human was ever attached to (leavers included), in entrant order.</summary>
        public List<int> HumanEntrants(bool includeLeavers = true)
        {
            var list = new List<int>();
            for (int i = 0; i < Entrants.Count; i++)
            {
                var e = Entrants[i];
                if (e.IsHuman || (includeLeavers && e.WasHuman)) list.Add(i);
            }
            return list;
        }

        /// <summary>Any human still in control anywhere in the bracket and not yet eliminated.</summary>
        public bool AnyHumanAlive()
        {
            for (int i = 0; i < Entrants.Count; i++)
                if (Entrants[i].IsHuman && !IsEliminated(i)) return true;
            return false;
        }

        /// <summary>A leaver: an AI plays the nation from here (later rounds get simulated).</summary>
        public void MarkReplacedByAi(int entrant)
        {
            if (!IsValidEntrant(entrant)) return;
            if (Entrants[entrant].WasHuman) Entrants[entrant].ReplacedByAi = true;
        }

        // ---- progress ------------------------------------------------------------------------

        /// <summary>The stage the entrant lost in, or null if not (yet) eliminated.</summary>
        public CupStage? EliminatedAt(int entrant)
        {
            if (entrant < 0) return null;
            for (int s = 0; s < CupStages.Count; s++)
            {
                var r = RoundOfEntrant((CupStage)s, entrant);
                if (r == null) return null;
                if (r.Done && r.WinnerEntrant != entrant) return (CupStage)s;
            }
            return null;
        }

        public bool IsEliminated(int entrant) => EliminatedAt(entrant).HasValue;

        /// <summary>
        /// Still in the cup when <paramref name="stage"/> begins: not knocked out in any stage
        /// before it. (True for every entrant at the Round of 32; true at the Round of 16 for an
        /// entrant whose Round of 32 round is not yet decided.)
        /// </summary>
        public bool IsAlive(int entrant, CupStage stage)
        {
            if (!IsValidEntrant(entrant)) return false;
            var outAt = EliminatedAt(entrant);
            return !outAt.HasValue || (int)outAt.Value >= (int)stage;
        }

        /// <summary>
        /// The furthest stage the entrant has a round in (the champion reports the Final too; use
        /// <see cref="IsChampion"/> to tell a beaten finalist from the winner).
        /// </summary>
        public CupStage StageReached(int entrant)
        {
            var reached = CupStage.RoundOf32;
            for (int s = 0; s < CupStages.Count; s++)
                if (RoundOfEntrant((CupStage)s, entrant) != null) reached = (CupStage)s;
            return reached;
        }

        /// <summary>
        /// The round an entrant plays next: the first undecided round they are in; if they have won
        /// their latest round and the next stage is not yet filled, the round they will feed (its
        /// <see cref="CupRound.Ready"/> is false until <see cref="Advance"/>); null once eliminated
        /// or crowned.
        /// </summary>
        public CupRound NextRoundOf(int entrant)
        {
            if (!IsValidEntrant(entrant)) return null;
            CupRound last = null;
            for (int s = 0; s < CupStages.Count; s++)
            {
                var r = RoundOfEntrant((CupStage)s, entrant);
                if (r == null) break;
                last = r;
                if (!r.Done) return r;
                if (r.WinnerEntrant != entrant) return null;
            }
            if (last == null) return null;
            return FeedsInto(last);   // null at the Final = champion
        }

        // ---- results -------------------------------------------------------------------------

        /// <summary>
        /// Record a round's result. Scores must differ (a round is never drawn); the kick line is
        /// copied (null = none). Overwriting a Done round is allowed but logged, and does NOT undo a
        /// later stage that was already advanced from the old result - re-run <see cref="Advance"/>.
        /// </summary>
        public void SetResult(CupRound r, int scoreA, int scoreB, IList<KickRecord> kicks, bool suddenDeath, CupSide firstKicker, bool simulated)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (!r.Ready) throw new InvalidOperationException("CupBracket.SetResult: " + CupStages.Short(r.Stage) + " #" + r.Index + " has no entrants yet");
            if (scoreA < 0 || scoreB < 0) throw new ArgumentException("CupBracket.SetResult: negative score");
            if (scoreA == scoreB) throw new ArgumentException("CupBracket.SetResult: a round cannot end level (" + scoreA + "-" + scoreB + ")");
            if (r.Done) CupLog.Warn("SetResult overwrites a finished round: " + r.Describe(this));

            r.ScoreA = scoreA;
            r.ScoreB = scoreB;
            r.Kicks = new List<KickRecord>(kicks != null ? kicks.Count : 0);
            if (kicks != null) for (int i = 0; i < kicks.Count; i++) r.Kicks.Add(kicks[i].Clone());
            r.SuddenDeath = suddenDeath;
            r.FirstKicker = firstKicker;
            r.WinnerEntrant = scoreA > scoreB ? r.EntrantA : r.EntrantB;
            r.Simulated = simulated;
            r.Done = true;
        }

        /// <summary>Record a result straight from a decided <see cref="RoundLine"/> (throws if it is not decided).</summary>
        public void SetResult(CupRound r, RoundLine line, bool simulated)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));
            CupSide winner;
            if (!CupRoundRules.IsDecided(line, out winner))
                throw new InvalidOperationException("CupBracket.SetResult: the line is not decided (" + CupRoundRules.Describe(line) + ")");
            SetResult(r, line.GoalsA, line.GoalsB, line.Kicks, CupRoundRules.IsSuddenDeath(line), line.FirstKicker, simulated);
        }

        /// <summary>
        /// Fill the next stage from this stage's winners (round i feeds round i/2, side i%2).
        /// Throws unless <see cref="StageComplete"/>. Returns false at the Final (nothing to feed).
        /// A next-stage round whose entrants change loses any result it had.
        /// </summary>
        public bool Advance(CupStage stage)
        {
            if (!StageComplete(stage))
                throw new InvalidOperationException("CupBracket.Advance: " + CupStages.Name(stage) + " is not complete");
            if (CupStages.IsLast(stage)) return false;
            var from = Stages[(int)stage];
            var to = Stages[(int)stage + 1];
            for (int i = 0; i < from.Length; i++)
            {
                var target = to[i / 2];
                int winner = from[i].WinnerEntrant;
                bool sideA = (i & 1) == 0;
                int before = sideA ? target.EntrantA : target.EntrantB;
                if (before != winner && target.Done) target.ResetResult();
                if (sideA) target.EntrantA = winner; else target.EntrantB = winner;
            }
            return true;
        }

        // ---- wire ----------------------------------------------------------------------------

        /// <summary>
        /// The whole bracket, versioned and compact: version, seed, format, entrants, then every
        /// round with its kick line. About 450 bytes for a fresh draw, under 1 KB for a finished
        /// cup with short names; worst case (every round to 30 kicks, eight 12-char names) is
        /// about 1.1 KB, so ship it on a reliable-bulk path if a transport's single packet is tight.
        /// </summary>
        public byte[] ToBytes()
        {
            var w = new CupByteWriter(1024);
            w.U8(WireVersion);
            w.U32(Seed);
            w.U8((int)Format);
            w.U8(Entrants.Count);
            for (int i = 0; i < Entrants.Count; i++) Entrants[i].WriteTo(w);
            w.U8(Stages.Length);
            for (int s = 0; s < Stages.Length; s++)
            {
                w.U8(Stages[s].Length);
                for (int i = 0; i < Stages[s].Length; i++) Stages[s][i].WriteTo(w);
            }
            return w.ToArray();
        }

        /// <summary>The inverse of <see cref="ToBytes"/>; throws <see cref="FormatException"/> on a bad or truncated record.</summary>
        public static CupBracket FromBytes(byte[] data)
        {
            var r = new CupByteReader(data);
            int version = r.U8();
            if (version != WireVersion)
                throw new FormatException("CupBracket: wire version " + version + ", expected " + WireVersion);
            var b = new CupBracket();
            b.Seed = r.U32();
            b.Format = (CupFormat)r.U8();
            int n = r.U8();
            if (n != EntrantCount) throw new FormatException("CupBracket: " + n + " entrants, expected " + EntrantCount);
            b.Entrants = new List<CupEntrant>(n);
            for (int i = 0; i < n; i++) b.Entrants.Add(CupEntrant.ReadFrom(r));
            int stages = r.U8();
            if (stages != CupStages.Count) throw new FormatException("CupBracket: " + stages + " stages, expected " + CupStages.Count);
            for (int s = 0; s < stages; s++)
            {
                int rounds = r.U8();
                if (rounds != CupStages.RoundsIn((CupStage)s))
                    throw new FormatException("CupBracket: " + rounds + " rounds in " + CupStages.Name((CupStage)s));
                for (int i = 0; i < rounds; i++)
                {
                    var round = CupRound.ReadFrom(r);
                    if ((int)round.Stage != s || round.Index != i)
                        throw new FormatException("CupBracket: round " + CupStages.Short(round.Stage) + " #" + round.Index + " found at " + CupStages.Short((CupStage)s) + " #" + i);
                    if ((round.EntrantA >= 0 && round.EntrantA >= n) || (round.EntrantB >= 0 && round.EntrantB >= n) || (round.WinnerEntrant >= 0 && round.WinnerEntrant >= n))
                        throw new FormatException("CupBracket: round refers to an entrant past " + n);
                    b.Stages[s][i] = round;
                }
            }
            return b;
        }

        /// <summary>A deep copy (through the wire format).</summary>
        public CupBracket Clone() => FromBytes(ToBytes());

        /// <summary>Field-by-field equality, kicks included.</summary>
        public bool DeepEquals(CupBracket o)
        {
            if (o == null) return false;
            if (Seed != o.Seed || Format != o.Format) return false;
            if (Entrants.Count != o.Entrants.Count) return false;
            for (int i = 0; i < Entrants.Count; i++) if (!Entrants[i].SameAs(o.Entrants[i])) return false;
            if (Stages.Length != o.Stages.Length) return false;
            for (int s = 0; s < Stages.Length; s++)
            {
                if (Stages[s].Length != o.Stages[s].Length) return false;
                for (int i = 0; i < Stages[s].Length; i++) if (!Stages[s][i].SameAs(o.Stages[s][i])) return false;
            }
            return true;
        }

        /// <summary>A multi-line dump for logs: header, entrants, then every round by stage.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("CupBracket seed=").Append(Seed).Append(" format=").Append(CupText.FormatName(Format));
            sb.Append(" stage=").Append(CupStages.Short(CurrentStage));
            if (IsComplete) sb.Append(" champion=").Append(Entrants[Champion].Code);
            sb.AppendLine();
            sb.Append("  entrants:");
            for (int i = 0; i < Entrants.Count; i++)
            {
                if (i % 8 == 0) { sb.AppendLine(); sb.Append("   "); }
                sb.Append(' ').Append(i).Append('=').Append(Entrants[i]);
            }
            sb.AppendLine();
            for (int s = 0; s < Stages.Length; s++)
            {
                sb.Append("  ").Append(CupStages.Name((CupStage)s)).AppendLine(":");
                for (int i = 0; i < Stages[s].Length; i++)
                    sb.Append("    ").AppendLine(Stages[s][i].Describe(this));
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return "CupBracket(seed=" + Seed + ", " + CupText.FormatName(Format) + ", stage " + CupStages.Short(CurrentStage) + ")";
        }
    }
}
