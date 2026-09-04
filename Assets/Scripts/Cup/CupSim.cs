using System;
using System.Collections.Generic;

// PURE FILE: no UnityEngine. It also compiles in a plain .NET console app (see CupSelfTest).

namespace Trickshot
{
    /// <summary>
    /// Instant AI-vs-AI rounds (design 2.8). Produces a kick-by-kick line, not just a winner, so
    /// the results list can show pips and "SD" tags. Per kick
    /// <c>P(goal) = clamp(0.72 + 0.20 * (taker01 - keeper01), 0.45, 0.92)</c> from the two
    /// nations' hidden strengths, under the same 5 + early finish + sudden death rules as a played
    /// round (<see cref="CupRoundRules"/>). Nothing is spawned; nothing here knows about Unity.
    ///
    /// Determinism: every method takes the <see cref="SeededRng"/> it draws from. The stage-level
    /// helpers fork a stream per round with <c>CupSalts.Sim(stage, index)</c>, so a round's
    /// result depends only on the cup seed and its position - not on which other rounds were
    /// simulated first, or on whether "Simulate to end" ran in one press or several.
    ///
    /// Termination: sudden death is capped at <see cref="CupTuning.SimMaxSuddenDeathPairs"/>
    /// pairs. Pairs are independent, so the eventual winner of unbounded play has exactly the
    /// distribution of the first DECISIVE pair; the last allowed pair is therefore drawn from that
    /// conditional distribution (A wins with weight pA(1-pB), B with pB(1-pA)) and is always
    /// decisive. The winner statistics are untouched; only the printed line is capped at
    /// 2 * KicksEach + 2 * SimMaxSuddenDeathPairs = 30 kicks.
    /// </summary>
    public static class CupSim
    {
        /// <summary>The per-kick goal probability from two 0..1 strengths.</summary>
        public static float GoalProbability01(float taker01, float keeper01)
        {
            float p = CupTuning.SimBaseGoalP + CupTuning.SimStrengthSlope * (taker01 - keeper01);
            if (p < CupTuning.SimMinP) p = CupTuning.SimMinP;
            if (p > CupTuning.SimMaxP) p = CupTuning.SimMaxP;
            return p;
        }

        /// <summary>The per-kick goal probability from two table strengths (1..99).</summary>
        public static float GoalProbability(int takerStrength, int keeperStrength)
        {
            return GoalProbability01(CupTuning.Strength01(takerStrength), CupTuning.Strength01(keeperStrength));
        }

        /// <summary>A simulated coin: the referee flips, the seeded result decides who kicks first.</summary>
        public static CoinFace SimulateCoin(SeededRng rng) => rng.Coin();

        /// <summary>
        /// Simulate one round's kick line between two strengths. Side A's kicks use
        /// P(strengthA vs strengthB), side B's the reverse. Returns a decided line of at most
        /// 2 * kicksEach + 2 * SimMaxSuddenDeathPairs kicks.
        /// </summary>
        public static RoundLine SimulateLine(int strengthA, int strengthB, CupSide firstKicker, SeededRng rng, int kicksEach = CupTuning.KicksEach)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            float pA = GoalProbability(strengthA, strengthB);
            float pB = GoalProbability(strengthB, strengthA);
            var line = new RoundLine(firstKicker, kicksEach);
            int maxKicks = kicksEach * 2 + CupTuning.SimMaxSuddenDeathPairs * 2;

            CupSide winner;
            int guard = 0;
            while (!CupRoundRules.IsDecided(line, out winner))
            {
                if (++guard > maxKicks + 2)
                    throw new InvalidOperationException("CupSim: the line did not terminate: " + CupRoundRules.Describe(line));

                bool lastPair = CupRoundRules.IsSuddenDeath(line) && CupRoundRules.PairComplete(line) && line.Count >= maxKicks - 2;
                if (lastPair)
                {
                    ForcedDecisivePair(line, pA, pB, rng);
                    continue;
                }

                var side = CupRoundRules.NextKicker(line);
                float p = side == CupSide.A ? pA : pB;
                var outcome = rng.Chance(p) ? KickOutcome.Goal : Fail(rng);
                CupRoundRules.RecordKick(line, side, outcome);
            }
            return line;
        }

        // The capped final pair: one side scores, the other does not, weighted like a decisive pair.
        static void ForcedDecisivePair(RoundLine line, float pA, float pB, SeededRng rng)
        {
            var first = CupRoundRules.NextKicker(line);
            var second = CupSides.Other(first);
            float pFirst = first == CupSide.A ? pA : pB;
            float pSecond = second == CupSide.A ? pA : pB;
            float wFirst = pFirst * (1f - pSecond);
            float wSecond = pSecond * (1f - pFirst);
            float total = wFirst + wSecond;
            bool firstWins = total <= 0f ? rng.Coin() == CoinFace.Heads : rng.Next01() * total < wFirst;
            if (firstWins)
            {
                CupRoundRules.RecordKick(line, first, KickOutcome.Goal);
                CupRoundRules.RecordKick(line, second, Fail(rng));
            }
            else
            {
                CupRoundRules.RecordKick(line, first, Fail(rng));
                CupRoundRules.RecordKick(line, second, KickOutcome.Goal);
            }
        }

        // A non-goal is SAVED or MISS by a fixed share; the pips only care that it did not score.
        static KickOutcome Fail(SeededRng rng)
        {
            return rng.Chance(CupTuning.SimSaveShare) ? KickOutcome.Saved : KickOutcome.Miss;
        }

        /// <summary>
        /// Resolve one round of the bracket instantly with the given stream (the caller forks it,
        /// e.g. <c>new SeededRng(bracket.Seed).Fork(CupSalts.Sim(round.Stage, round.Index))</c>):
        /// the coin, then the line, then <see cref="CupBracket.SetResult(CupRound,RoundLine,bool)"/>
        /// with Simulated set. Throws if the round has no entrants. Returns the line.
        /// </summary>
        public static RoundLine Simulate(CupRound round, CupBracket bracket, SeededRng rng)
        {
            if (round == null) throw new ArgumentNullException(nameof(round));
            if (bracket == null) throw new ArgumentNullException(nameof(bracket));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (!round.Ready)
                throw new InvalidOperationException("CupSim.Simulate: " + CupStages.Short(round.Stage) + " #" + round.Index + " has no entrants yet");

            int sA = bracket.Entrants[round.EntrantA].Strength;
            int sB = bracket.Entrants[round.EntrantB].Strength;
            // Side A calls heads by convention; the coin decides who kicks first.
            var first = CupRoundRules.FirstKickerFromCall(CupSide.A, CoinFace.Heads, SimulateCoin(rng));
            var line = SimulateLine(sA, sB, first, rng);
            bracket.SetResult(round, line, true);
            return line;
        }

        /// <summary>The stream for one round's simulation, derived from the cup seed.</summary>
        public static SeededRng StreamFor(CupBracket bracket, CupRound round)
        {
            return new SeededRng(bracket.Seed).Fork(CupSalts.Sim(round.Stage, round.Index));
        }

        /// <summary>
        /// Simulate every pending round of a stage - only the AI-vs-AI ones when
        /// <paramref name="aiOnly"/> (the between-stages "Simulating the rest of the stage"),
        /// or every pending round (a knocked-out / left cup). Each round forks
        /// <paramref name="rng"/> with its own salt. Returns how many rounds were resolved. Does
        /// NOT advance the stage; see <see cref="SimulateRemaining"/>.
        /// </summary>
        public static int SimulateStage(CupBracket bracket, CupStage stage, SeededRng rng, bool aiOnly)
        {
            if (bracket == null) throw new ArgumentNullException(nameof(bracket));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            int n = 0;
            var rounds = bracket.Stages[(int)stage];
            for (int i = 0; i < rounds.Length; i++)
            {
                var r = rounds[i];
                if (!r.Ready || r.Done) continue;
                if (aiOnly && (bracket.Entrants[r.EntrantA].IsHuman || bracket.Entrants[r.EntrantB].IsHuman)) continue;
                Simulate(r, bracket, rng.Fork(CupSalts.Sim(stage, i)));
                n++;
            }
            return n;
        }

        /// <summary>
        /// Finish the cup instantly from a stage on ("Simulate to end"): every pending round of
        /// every stage from <paramref name="fromStage"/> is simulated (humans included - by now
        /// they are out or gone) and each completed stage advances. Returns the champion entrant,
        /// or -1 if a stage could not complete (a round without entrants, which cannot happen after
        /// a successful Advance chain). Pass <c>new SeededRng(bracket.Seed)</c> for the canonical
        /// results.
        /// </summary>
        public static int SimulateRemaining(CupBracket bracket, CupStage fromStage, SeededRng rng)
        {
            if (bracket == null) throw new ArgumentNullException(nameof(bracket));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            for (int s = (int)fromStage; s < CupStages.Count; s++)
            {
                var stage = (CupStage)s;
                SimulateStage(bracket, stage, rng, false);
                if (!bracket.StageComplete(stage)) return -1;
                bracket.Advance(stage);
            }
            return bracket.Champion;
        }

        /// <summary>
        /// One press of "Simulate to end": resolve the current stage and advance it, so the
        /// bracket fills stage by stage. Returns the stage that was completed, or null when the
        /// cup was already complete.
        /// </summary>
        public static CupStage? SimulateNextStage(CupBracket bracket, SeededRng rng)
        {
            if (bracket == null) throw new ArgumentNullException(nameof(bracket));
            if (bracket.IsComplete) return null;
            var stage = bracket.CurrentStage;
            SimulateStage(bracket, stage, rng, false);
            if (!bracket.StageComplete(stage)) return null;
            bracket.Advance(stage);
            return stage;
        }
    }
}
