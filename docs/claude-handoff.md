# Claude handoff - Trickshot (2026-09-04)

## Where things stand

The Trickshot Cup is CODE-COMPLETE through six phases (5 build + review, then a 10-agent polish
pass), and several game-wide changes landed on top of it. Everything is committed and pushed on
`main`; the working tree is clean. Nothing in any of it has run in the Unity editor or on loopback.

Recent commits, newest first:

| Commit | What |
| --- | --- |
| `dbded4e` | Cross slider labels, epic save removed, keepers 20% weaker, striker post-goal slide fixed |
| `4b1f0a9` | Keeper unleashed sideways everywhere; roams outfield in a match |
| `4ddd185` | Keeper camera: the same three views, front yields to an inbound shot |
| `6d69d64` | Camera: first-person and front views on T, every mode |
| `c996ac0` | Penalty camera frames the whole taker; 10-agent polish pass (phase 6) |
| `dbc51ed` | Cup replay shows the actual goal; solo click-through for walking beats |

## Verification state - read this literally

| Check | State |
| --- | --- |
| `bash docs/compile-check.sh` | PASS (`exit=0`), run after every change |
| `dotnet run -c Release` in `docs/cup-build/cuptest` | PASS - `ALL PASSED: 562281 checks` (pure core only) |
| Unity editor / play mode | **NEVER RUN** |
| Loopback / two-peer multiplayer | **NEVER RUN** |
| Anything visual, timing, physics or camera framing | **NEVER RUN** - solved on paper |

Every camera number, every choreography beat and every networking claim is "correct by reading",
which is strictly weaker than "seen working".

## What is owed next, in order

1. **The editor pass.** The single biggest gap, owed since phase 3. Use the Unity MCP rules in
   `CLAUDE.md` ("Verifying UI in the editor"). Highest value first:
   - The new camera views (T) in a normal mode: third, first, front. Check the first-person eye is
     not inside the head, and that the front view's 180-degree swing reads as a cut, not a sweep.
   - The cup penalty camera: the whole taker should now be in frame with the goal about 61% of the
     frame width. Rebuilt from a report that it sat too close behind him, and entirely desk-solved.
   - A full Solo cup round: loading card, intro card, coin ceremony, referee whistle raise, the
     replay actually showing the goal, the walk-back arriving rather than snapping, the podium.
   - The Co-op trophy lift and the champion's trophy arm (it used to drop to his hip every 2.5 s).
2. **Loopback multiplayer.** Never run once. The full first-pass checklist is in
   `docs/cup-build/reports/phase4-multiplayer.json` under `r1.notesForNextAgents` item 1. Head to
   Head lobby of two first, then one Co-op stage.
3. **The roaming keeper**, new and untested: leave your own box and confirm the striker controls
   take over, that returning restores keeper controls, that you cannot handle the ball outfield,
   and that crossing the line drops a held ball. In the opponent's box he must stay a striker.

## Open issues, all documented and deliberate

- **Celebration's arm clamp has the wrong abduction sign.** Diagnosis confirmed twice and
  reproduced numerically. The obvious fix (flipping the two call-site signs) makes the clamp fire
  on 25 of 38 emotes and pulls hands off faces, hips and each other. Left INERT and documented in
  the file. Needs an editor plus a box test that excludes intentional hand-to-body poses. Top item
  for whoever has the editor open.
- **`CupSpectatorView` puppets do not animate** (DisplaySnap/DisplayEmote only). Fixing it needs an
  anim byte on the `CupStreamBody` wire record plus a `CupNet.StateVersion` bump.
- **`DefensiveWall` leaks a PhysicsMaterial** per built wall. The fix is inside a file three other
  modes share, so it wants a deliberate decision rather than being folded into a cup change.
- **A Co-op lever-pull salt collides with the podium salt** after 256 pulls in one stage. Real but
  unreachable in practice; changing the arithmetic is not wire-compatible mid-cup.
- **Cup lobby Customize is disabled** in Solo and Head to Head (`TODO(h2h-customize)`); it needs
  `GameBootstrap.ShowLobbyCustomize`, whose preview camera was never checked against a live arena.
- **A keeper's residual momentum** is not zeroed when he hands the body to the Striker or when
  `ForceRecover` runs; locomotion damping settles it. If a body still drifts after a goal, that is
  the next place to look.

## Ground rules that bit during this work

- Compile without the editor: `bash docs/compile-check.sh` (exit=0). Never `-quit`/batchmode; the
  open editor holds the lockfile.
- Scripts are CRLF. A Workflow `.js` is the exception - the approval dialog refuses carriage
  returns, so those stay LF.
- Never say "tie" in cup code or UI. A ROUND is one match; the five bracket levels are STAGES.

## Build artefacts

`docs/cup-build/` holds the whole multi-agent build: `README.md` (runbook), `cup-build-notes.md`
(the codebase facts the agents worked from), `workflows/wf1..wf6` (the Workflow scripts) and
`reports/phase1..phase6` (every agent's full report). The reports record WHY things are the way
they are; read the relevant one before changing cup code. Delete the folder once the mode is
verified and settled.
