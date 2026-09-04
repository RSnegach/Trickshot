# Trickshot Cup build - orchestration runbook

This folder is the durable copy of everything the multi-agent build of the Trickshot Cup needs to
resume in a fresh Claude Code session. The design is `docs/trickshot-cup-design.md`; the current
task state is `docs/claude-handoff.md`. Delete this folder once the build is committed and done.

## Contents

| Path | What |
|---|---|
| `cup-build-notes.md` | Verified codebase facts + ground rules every builder agent reads first. |
| `workflows/wf1-foundations.js` .. `wf5-review.js` | The Workflow scripts, one per phase, already re-pointed at repo paths. |
| `reports/phaseN-*.json` | Each finished phase's full result (`result.<agent>.publicApi / notesForNextAgents / openIssues`). Later phases read these. |
| (restore point) | Everything through phase 4 is committed on `main` (2026-09-04); restore a half-edited file with `git checkout -- <path>`. The earlier `tree-snapshot-clean.tgz` was dropped at that commit. |
| `cuptest/` | The plain .NET console project that runs `CupSelfTest` outside Unity (`dotnet run -c Release` in that folder; it includes the pure cup files by absolute path). |
| `shots/` | Screenshots the editor passes take (created on demand). |

## The phases

| # | Script | Agents | Status |
|---|---|---|---|
| 1 | `wf1-foundations.js` | 3 (serial) | DONE - `reports/phase1-foundations.json` |
| 2 | `wf2-solo.js` | 5 | DONE - `reports/phase2-solo.json` |
| 2b | `wf2b-editor-solo.js` | 1 (editor) | DONE - `reports/phase2b-editor-solo.json` |
| 3 | `wf3-endings.js` | 3 parallel: K free-kick choreography, G podium + trophy lift, V vignette | DONE - `reports/phase3-endings.json` |
| 3b | `wf3b-editor-endings.js` | 1 (editor, animations only) | SKIPPED on the owner's instruction (the endings are not editor-checked) |
| 4 | `wf4-multiplayer.js` | 4: H wire, then I Head to Head + J Co-op, then R1 integration | DONE 2026-09-04 - `reports/phase4-multiplayer.json` (2.56M agent tokens, 93 min) |
| 5 | `wf5-review.js` | 3 reviewers, 2 fixers, 1 final verifier (also writes CLAUDE.md + handoff) | NEXT - its KNOWN block already carries the phase 3 and 4 open issues |

## How to resume (exactly)

Preconditions: Ultracode on (`/effort ultracode`), workflow size guideline large. For 3b: Unity
6000.4.1f1 open on this project with the MCP for Unity server started, then `/mcp` to reconnect
"UnityMCP". The owner's rules: the editor is used ONLY to check choreographed animations (nothing
else); `graphify update .` only at a commit or when the owner says they are about to clear;
"round" = one match, "stage" = a bracket level, never "tie"; about 22 agents total for the whole
build (22 used so far; phase 5's six are the last), keep it there.

1. `bash docs/compile-check.sh` from the repo root must print `exit=0` (it did at the phase 4 commit).
   If it does not, `git status --short` shows the file a killed agent left half-edited: restore it from
   git (everything up to the end of phase 4 is committed) and compile again.
2. Phases 1-4 are DONE (their reports are in `reports/`); 3b was skipped by the owner.
3. Phase 5: `Workflow({ scriptPath: "C:/Users/evrik/downloads/Trickshot/Trickshot/docs/cup-build/workflows/wf5-review.js", args: { reportFiles: [P1, P2, P2B, P3, P4] } })`
   with the five absolute report paths (`phase1-foundations`, `phase2-solo`, `phase2b-editor-solo`,
   `phase3-endings`, `phase4-multiplayer`). Six agents, roughly the size of phase 4: launch it only
   with well over an hour of session limit left (a killed workflow restarts from scratch). Its final
   verifier updates `CLAUDE.md`, rewrites `docs/claude-handoff.md` and the DESIGN_NOTES pointer.
   Copy the output to `reports/phase5-review.json`.
4. Report to the owner, then (their call) commit, and only then `graphify update .`.

Between phases, read each report's `openIssues` and `notesForNextAgents` and fold anything the next
script's prompt does not already cover into that script before launching it (the scripts take the
reports as input, but a decision the owner made mid-session belongs in the prompt text).

## Owner decisions made after the design doc was written (already in the code or in the prompts)

- No "TRICKSHOT CUP - style - format" tag line on any screen; the results block in the Cup lobby is a
  plain RESULTS section (no "simulating" reveal) and a long shootout wraps its pips onto extra rows;
  the View Bracket overlay draws on a solid dark plate.
- Callouts (GOAL / SAVED / MISS / HEADS / TAILS) sit below the scoreboard (`Hud.Flash(..., top)`).
- The results screen's "you won" strip is a 26 pt headline.
- "Simulate to end" (renamed from "Simulate the rest") reveals the next stage's pairings on the
  same press (feed-forward after the simulation).
- Nation picker: one alphabetical run (novelty kits interleaved, no NOVELTY section), a four-column
  grid of cards with 56 px flags, arrows move in the grid.
- Keepers may not leave their line before the strike (`KeeperController.HoldLine`, `Goalkeeper.HoldLine`).
- Armed -> Live also triggers on the taker reporting Struck or the ball leaving its spot; the goal
  verdict is the other modes' box test OR the physics-rate line-crossing latch.
- FREE KICKS: no lineup, no walk to or from it, every taker starts at the ball; teammates scattered
  behind the taker (seeded), the other side further back; a missed/saved free kick plays a dejection
  animation on the spot; a scored one frees the whole scoring side for 5 s in Head to Head / Co-op.
  (Agent K in phase 3 implements this.)
- Everything through phase 4 was committed and pushed on `main` on 2026-09-04 with the graph refreshed;
  `tree-snapshot-clean.tgz` is superseded by git from here on.
- Pending question to the owner: may an agent use play mode to reproduce the free-kick kick-clock
  bug if the code fix did not cure it? (The owner limited the editor to animation checks.)
