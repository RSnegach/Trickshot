using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The round driver's LEAVER seam (design 10, Head to Head: "a leaver mid-round forfeits
    /// nothing: the round finishes with AI on their side"). A human who drops out of a
    /// host-simulated round leaves two bodies behind driven by a NetInputSource nobody feeds any
    /// more - a taker who never charges (the kick clock fires the weak auto-shot) and a keeper who
    /// stands still. <see cref="HandSlotToAi"/> turns that side into an ordinary AI side in
    /// place: the shooter body gets a CupBotTaker (the real charge / run-up / strike at the
    /// stage's strength), the gloved body a Goalkeeper brain, the setup forgets the human slot so
    /// every role lookup (TakerBodyFor / KeeperBodyFor / SideIsHuman / the twin park rule) reads
    /// the side as AI from the next kick on, and a kick already armed on the stale input is
    /// re-armed on the bot at once. Wire ids (VirtualSlot) never change, so every client's puppets
    /// keep following the same snapshot bodies.
    /// </summary>
    public partial class CupRoundDriver
    {
        /// <summary>
        /// Host / Local authority: hand a human's bodies to the AI for the rest of the round.
        /// Returns true when that slot had bodies here. A no-op on a Client (its bodies are
        /// puppets of the host's).
        /// </summary>
        public bool HandSlotToAi(int slot)
        {
            if (!_sceneBuilt || slot < 0 || Setup == null || Authority == RoundAuthority.Client) return false;
            var mine = new List<CupBody>();
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Slot == slot && b.Role != CupBodyRole.Referee) mine.Add(b);
            }
            if (mine.Count == 0) return false;

            var side = mine[0].Side;
            for (int i = 0; i < mine.Count; i++)
            {
                var b = mine[i];
                b.Slot = -1;                        // reads as an AI body from here (IsHuman false)
                b.Name = CupText.AiName(b.Name);
                b.NetInput = null;
                b.Freed = false;
                if (b.IsKeeperBody)
                {
                    // The human controller stays on the body (Tick-driven only; nothing calls it
                    // once an AI brain is present - TickKeeperOnLine prefers Ai) but locked, so
                    // nothing else that pokes it can move him.
                    if (b.Keeper != null) b.Keeper.InputLocked = true;
                    if (b.Ai == null && b.Alive && b.Go != null)
                    {
                        // Two-argument Init: faces him out from the +Z goal (outSign -1), exactly
                        // as BuildBodies does for an AI keeper. His "home" becomes where he stands
                        // now; the next placement puts him on the line like any AI keeper.
                        b.Ai = b.Go.AddComponent<Goalkeeper>();
                        b.Ai.Init(b.Ragdoll, Setup.Ball);
                    }
                }
                else
                {
                    if (b.Striker != null) b.Striker.ControlEnabled = false;
                    if (b.Bot == null) b.Bot = new CupBotTaker(_botRng);
                }
            }

            // The side is an AI side now: SideIsHuman / HumanSlotOf / TakerSlotForNextKick and
            // the twin park rule all read the setup.
            if (Setup.HumanSlotA == slot) Setup.HumanSlotA = -1;
            if (Setup.HumanSlotB == slot) Setup.HumanSlotB = -1;
            _netInputs.Remove(slot);
            _skipVoted.Remove(slot);

            // Penalties: the human's two bodies shared ONE lineup mark (one person, one slot in
            // the line); as two AI bodies - a shooter and a keeper, both visible off the ball -
            // they need two, laid out like any AI side's. Free Kicks recompute every mark per
            // placement (AssignFreeKickMarks), so nothing to do there.
            if (Setup.Format == CupFormat.Penalties)
            {
                var line = new List<CupBody>();
                for (int i = 0; i < _bodies.Count; i++)
                {
                    var b = _bodies[i];
                    if (b.Side == side && b.Role != CupBodyRole.Referee) line.Add(b);
                }
                bool onTeamSide = side == Setup.TeamSide;
                for (int i = 0; i < line.Count; i++)
                {
                    line[i].LineupIndex = i;
                    line[i].LineupMark = CupSpots.LineupMark(onTeamSide, i, line.Count);
                    line[i].LineupFacing = CupSpots.LineupFacing;
                }
            }

            // A kick armed on the stale human input (the taker would only ever auto-fire at the
            // clock): re-armed on the bot right now, the ball re-ignored as ArmTaker's callers do.
            var tb = _takerBody;
            if (tb != null && tb.Side == side && !tb.IsHuman && Phase == RoundPhase.Armed && _takerArmed && tb.Bot != null && !tb.Bot.Armed)
            {
                _taker.Reset();
                ArmTaker();
                if (tb.Alive) Setup.Ball.IgnoreBody(tb.Ragdoll, true);
            }

            // The replay-skip vote counts the humans with a body: one fewer now.
            if (ReplayPlaying)
            {
                ReplaySkipNeeded = CountHumansWithBodies();
                if (ReplaySkipVotes >= ReplaySkipNeeded) _replayEndRequested = true;
            }
            RefreshLocalBody();
            CupLog.Info("CupRoundDriver: slot " + slot + " handed to the AI on side " + CupSides.Name(side) + " (" + mine.Count + " bodies)");
            return true;
        }
    }
}
