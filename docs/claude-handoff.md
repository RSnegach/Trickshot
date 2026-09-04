# Handoff: Trickshot Cup build (multi-agent, phase 5 is the last)

## Current task

Implementing the whole Trickshot Cup mode (design: `docs/trickshot-cup-design.md`) with a
sequence of Workflow-orchestrated agent phases. The runbook with the exact resume commands is
`docs/cup-build/README.md`; the codebase facts the agents rely on are
`docs/cup-build/cup-build-notes.md`; every finished phase's report is under `docs/cup-build/reports/`.

## Desired end state

All three styles (Solo, Head to Head, Co-op) playable end to end per the design doc, the podium and
trophy lift, the menu vignette, multiplayer on the existing direct-IP transport, a three-lens review
with fixes, CLAUDE.md carrying the cup's invariants, this handoff rewritten (or deleted) by the final
verifier. Then the owner commits and the graph is updated.

## Where it stands (2026-09-04, phase 4 complete)

- Phases 1, 2, 2b, 3 and 4 are DONE and compile-clean (`bash docs/compile-check.sh` -> `exit=0`).
  3b (editor check of the endings) was SKIPPED on the owner's instruction.
- Phase 4 (`wf4-multiplayer.js`, 4 agents, 2.56M agent tokens, 93 min) built the wire and both
  networked styles: `reports/phase4-multiplayer.json`. New files: `CupNet.cs`, `CupDirector.Net.cs`,
  `CupRoundDriver.Net.cs`, `CupSpectatorView.cs` (H), `CupRoundDriver.Leaver.cs` (I), `CupOrderUI.cs`
  (J); `CupDirector.HeadToHead.cs` / `CupDirector.Coop.cs` are now the full flows; R1 fixed twelve
  seam defects. MsgType CupState 23 / CupRequest 24 / CupStream 25 / CupRoundState 26,
  ProtocolVersion 8 unchanged, CupNet.StateVersion 1.
- EVERYTHING in phases 3 and 4 is compile- and desk-checked only: no editor, no loopback. The first
  loopback pass is spelled out in phase 4's `r1.notesForNextAgents` (item 1) and should follow phase 5.
- Everything through phase 4 is COMMITTED and PUSHED on `main` (2026-09-04) with the graph refreshed;
  the six new scripts got hand-written two-line `.meta` files in the project's form.
- Phase 5 (`wf5-review.js`: 3 reviewers, 2 fixers, 1 final verifier) is NOT started. Its KNOWN block
  already carries every open issue from the phase 3 and 4 reports (superseded free-kick bullet
  rewritten, phase 4 deliberate gaps listed as accepted, phase 4 open items (a)-(i) listed to fix).

## Known bugs / open questions

- Phase 4 open items a reviewer should settle (all in `wf5-review.js` KNOWN): client puppets have no
  run/dive animation (DisplaySnap/DisplayEmote only); a lobby spectator's Esc closes on the host's
  echo; a refused RoundResult waits for the 10 s wave watchdog; `CupHud.cs` unused `teamW`; the
  display-only client taker meter; puppets must never get `Celebration.Play`; the two leaver paths
  (`HandSlotToAi` Head to Head / `HumanLeft` Co-op) must be style-gated.
- Deliberate gaps (accepted): no client keeper prediction; EndMatch's single `Ended` packet; the
  Co-op reel gate on host time; a leaving keeper's gloves re-slotted, not rebuilt.
- Free kicks: the owner saw the kick clock keep counting after a strike and the auto-shot overwrite
  the real result. Agent K found and fixed two real causes (lineup bodies inside the free-kick band
  striking the dead ball; `SetPieceTaker.Begin` swallowing a Space held at the whistle -> `ChargeGate`)
  but it was never reproduced in play mode. Still pending: may an agent use play mode for it?
- The editor pass in phase 2b ran `graphify update .` on its own; the owner wants that only at
  commits or on "about to clear" (done at the phase 4 commit).

## Decisions already made

- Round / stage naming; no "tie". Solo is standardised like MP. No human keeper handicap.
- Editor use is for animation checks only. Agent budget about 22 for the whole build (22 used;
  phase 5's six are the last).
- Phase order and agent counts are fixed in the scripts; do not add agents without asking.
- Free kicks have no lineup: scatter marks, dejection on the spot, whole side freed on a goal.

## Exact next steps (from a fresh session)

Preconditions: `/effort ultracode`; Unity is NOT needed. Launch phase 5 only with well over an hour
of session limit left: it is six agents, about the size of phase 4, and a killed workflow cannot be
resumed (it restarts from scratch).

1. `bash docs/compile-check.sh` must print `exit=0`.
2. Phase 5: `Workflow({ scriptPath: "C:/Users/evrik/downloads/Trickshot/Trickshot/docs/cup-build/workflows/wf5-review.js", args: { reportFiles: [P1, P2, P2B, P3, P4] } })`
   with P1 = docs/cup-build/reports/phase1-foundations.json, P2 = phase2-solo.json,
   P2B = phase2b-editor-solo.json, P3 = phase3-endings.json, P4 = phase4-multiplayer.json (absolute
   paths). Its final verifier updates CLAUDE.md, rewrites this handoff and the DESIGN_NOTES pointer.
   When it completes, copy its `tasks/<id>.output` to `docs/cup-build/reports/phase5-review.json`.
3. Report, then (the owner's call) commit and push, and only then `graphify update .`.
4. After phase 5: the loopback pass (Head to Head lobby of two, then one Co-op stage) and the
   in-editor animation checks the skipped 3b would have done (podium hand-over, trophy lift, the
   free-kick scatter and dejection, the menu vignette).
