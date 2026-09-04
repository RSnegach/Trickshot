# Claude handoff - Trickshot Cup final verification pass

## Current task

Final verifier over the Trickshot Cup mode (5 build phases + 2 review/fix passes). This pass
re-verified every blocker and major from the review by READING THE CODE at each named site,
checked the parallel fixers had not contradicted each other, and fixed the one real finding
they had all skipped on file-ownership grounds. Reviewing is done; the mode is code-complete
and compile-clean, and has STILL never run in the Unity editor or on loopback.

## Verification state - what has actually been run

| Check | State | Notes |
| --- | --- | --- |
| `bash docs/compile-check.sh` | PASS (`exit=0`) | Run at the start of this pass and again after my two edits. |
| `dotnet run -c Release` in `docs/cup-build/cuptest` | PASS | `ALL PASSED: 562277 checks in 1021 ms`. Pure core only: bracket draw, round rules, kick lines, sudden death, wire sizes. |
| CRLF on every edited `.cs` | PASS | `grep -c` for CR equals `wc -l` on all nine touched files. |
| Unity editor / play mode | **NEVER RUN** | No editor, no MCP this session. Nothing visual, no layout, no timing, no cameras. |
| Loopback / two-peer multiplayer | **NEVER RUN** | Every net claim below is desk-traced only. |
| Physics, choreography, camera framing | **NEVER RUN** | All camera and pose numbers are solved on paper. |

Read that table literally. Everything below is "correct by reading", which is a strictly weaker
claim than "seen working".

## What this pass changed

Only one code change of my own, plus the seven files the parallel fixers had already edited
(which I re-verified rather than re-wrote).

- `Assets/Scripts/Cup/CupPodium.cs` and `Assets/Scripts/Cup/CupTrophyLift.cs` - fixed SMOOTH-01,
  the champion's trophy arm dropping to his hip and re-rising once every 2.5 s for the whole
  podium and the whole Co-op trophy lift. Both hold guards now also apply while
  `celeb.Progress01 < 0.35f`, which is exactly the emote's own ease-in window.

Both previous fixers skipped this correctly-but-incompletely: the first could not edit these two
files, and both rightly refused to fix it in `Celebration.cs`. I own all the files, so I applied
the finding's own prescribed fix in the two cup files and left `Celebration.cs` untouched.

Why the fix is safe (checked, not assumed): `Celebration.Update` writes its pose through
`SetPoseOverride` (ASSIGNMENT) into the same `_poseOverride` slots the cup holds write, and both
cup holds run in `LateUpdate`, which Unity runs after every `Update`. So the hold OVERWRITES the
ramping emote value rather than summing with it. This matters because `ActiveRagdoll` blends
`Lerp(_poseFrom, _poseTo) + _poseOverride` (ActiveRagdoll.cs:881) - had either side used
`AddPoseOverride`, the overlap window would have produced a ~327 deg arm instead of a held one.

## Findings re-verified this pass (I read the code; I did not trust the reports)

All CONFIRMED as correctly fixed:

- **h2h-late-wave-never-prepared** (blocker). `CupDirector.HeadToHead.cs:228` now reads
  `if (!wave || phase == CupPhase.Loading) _h2hWavePrepared = false;`. I checked the load-bearing
  claim myself: grepping `SetPhase(CupPhase.Loading)` finds exactly two sites in this flow, :369
  (H2HStartWave) and :1092 (H2HTickInterstitial), and both OPEN a wave. The late-wave path is real -
  H2HTickRound:548 -> H2HHostAdvance:788 `H2HStartWave(false)` -> :369, fired while Phase is still
  `Round`. Clients mirror it through `CupDirector.Net.cs:522 SetPhase(phase, m.phaseTime)`, which
  bumps PhaseSerial and re-dispatches `HeadToHeadEnter`. The fixer was RIGHT to reject the review's
  proposed serial-latch mechanism: CoinToss and Round are separate SetPhases inside a wave, so a
  serial test would re-prepare mid-wave and wipe `_h2hHostRound`, `_h2hLoadedSent`, `_h2hTossDone`
  and `_h2hLocal`.
- **cup-cointoss-paused-early-return** and **cup-h2h-watchbar-paused-early-return** (major, same
  class). Both now allocate an identical control count on every event pass and hide by parking +
  `GUI.enabled`. I verified the underlying premise at `PauseMenu.cs:410 -> Activate -> Resume ->
  Paused = false`, all inside an IMGUI pass, and confirmed `UITheme.ClickBlocker`
  (UITheme.cs:975-983) forces its own `GUI.enabled = true` and honours a hole by parking - so the
  coin overlay's full-screen-hole trick is the right mechanism. I additionally checked the watch
  bar's button COUNT is stable within a pass: it varies with `e.IsHuman`, which only moves via
  `Bracket.MarkReplacedByAi`, called from `CupDirector.cs:1322` and `CupDirector.Net.cs:627`, both
  on the net tick.
- **cup-net-1 / h2h-host-live-row-never-broadcast** (major, one defect reported twice).
  `CupDirector.cs:1743-1748` now Notifies on change. `!me.Playing` is correctly part of the change
  test because `SetLive` sets `Playing = true` (CupDirector.cs:158-165). I grepped both
  `StateChanged` subscribers (`CupDirector.Net.cs:136`, `CupResultsUI.cs:67`); neither re-enters
  `UpdateLiveRow`, so the added Notify cannot recurse.
- **cup-hud-recaptures-cursor-at-round-over** (minor). `CupHud.cs:142` is now
  `SetWheel(false, false)`. I confirmed the ordering that makes this necessary at
  `CupRoundDriver.cs:362-363`: `OnPhaseChanged` (-> EndRoundVisuals -> `CaptureCursor(false)` at
  Kick.cs:1075) runs BEFORE `PhaseChanged?.Invoke`, so the HUD would otherwise undo the driver's
  release.
- **cup-net-2, wire half** (polish). `CupNet.cs:63` wraps (`& 0xFF`) instead of clamping. I
  grepped every reader of `LeverPulls`; all compare by `!=` or `> 0`, none by magnitude, so wrap
  is safe.

## Still open - deliberately not fixed

- **CUP-MP-01** (minor, real): each cup `DefensiveWall` leaks one `PhysicsMaterial`.
  `DefensiveWall.cs:100-101` lazily creates `_bounce = Make.PhysMat("WallBlocker", ...)`, and
  `Clear()` (:247-257) destroys the blockers and the tracked `Material`s but never `_bounce`;
  nothing in the repo destroys a PhysicsMaterial. In FreeKicks the cup builds one wall per ROUND
  (`CupRoundDriver.Scene.cs:393`, from `OnConfigured`), so it leaks one small object per round.
  NOT fixed because `_bounce` is private and the only correct fix lives in
  `Assets/Scripts/Play/DefensiveWall.cs`, a SHARED file: the destroy belongs inside `Clear()`,
  which is behaviour-neutral for the three non-cup callers (`GameBootstrap.BuildFreeKickMode`,
  `NetSetPieceMatch.Configure`, `FreeKickScene`) only because the lazy `if (_bounce == null)` guard
  re-creates it and every `Build*` overload leads with `Clear()`. It is a small leak, not a
  correctness bug, so I left the shared-file edit to a deliberate decision rather than folding it
  into a cup review.
- **cup-net-2, salt-overlap half** (polish, real but not worth the churn):
  `CupDirector.cs:1290` forks `CupSalts.Order(Stage) + 16u * (uint)LeverPulls`, and
  `OrderFamily 0x6000 + 16*256 == 0x7000 == CupSalts.Podium` (CupTypes.cs:138/147). Reaching it
  needs 256 manual lever pulls within one stage's order screen; the only consequence is that a
  shuffle permutation shares a stream with podium loser poses, and both stay deterministic across
  peers. Changing the arithmetic changes which permutation a given (seed, stage, pull) yields, so
  it is NOT wire-compatible mid-cup and would have to land on all peers at once. Not worth it.

## Cross-fixer contradiction check

The three fixers ran in parallel on disjoint files. I checked the shared symbols they could have
broken between them: `PauseMenu.Paused` (two fixers independently applied the SAME
park-don't-skip pattern, in `CupCoinToss.DrawInner` and `H2HDrawWatchBar` - consistent, and they
are hooked into one shared control-id sequence, bar first, which is exactly why both had to be
fixed together); `_h2hWavePrepared` (single writer set, one file); `LeverPulls` (wire producer in
`CupNet.cs`, consumers unchanged); `Notify` / `StateChanged` (one new caller, two subscribers, no
recursion); `SetWheel` / `CupEmoteWheel.SetOpen` (HUD-local, podium and trophy lift unaffected).
No conflicts found.

The penalty camera rewrite (`CupPenaltyCam.cs`, `CupTypes.cs`: 7 m/2.4 m -> 9 m/2.2 m plus a new
third FOV rule that frames the taker) came in with no finding against it. I read it: it is
self-consistent, `TakerPos` is always set at the single `Latch` call site (`CupCameraRig.cs:218`,
with a run-up-mark fallback when the pelvis is null), the new rule guards `feet.z > 0.05f` before
dividing, and the result stays clamped to `MinFov..MaxFov`. Its framing claims (~37 deg, goal ~61%
of frame width) are desk-solved and CANNOT be confirmed without the editor.

## In-editor work still owed

Nothing below has been done. This is the whole remaining verification burden.

1. Solo cup, one full run: draw -> coin toss -> penalties and free kicks -> stage complete ->
   podium. Watch for the trophy arm holding steady across emote loops (the SMOOTH-01 fix) and for
   the penalty camera actually framing the taker and the goal at 9 m / 2.2 m.
2. Pause the game during a coin toss and during a H2H watch bar, then click Resume. Confirm no
   IMGUI click breakage anywhere on screen - that is the exact failure the park-don't-skip fixes
   prevent, and it is the one class of bug a compile cannot catch.
3. Loopback Head to Head, at least 3 peers: a parallel wave, then a LEAVER mid-wave to force the
   late-wave path (the blocker fix). Confirm the wave opens and does not sit until
   `HeadToHeadWaveCap`.
4. Loopback lobby: confirm a host's live row (score / kick number) actually ticks on the CLIENTS'
   lobby rows and that Spectate lights up - the change-gated Notify fix.
5. Co-op order screen: pull the lever repeatedly and confirm client reels keep animating.
6. Free-kick cup round: confirm no body sits on the ball spot or run-up path, and watch memory
   across many rounds for the known `_bounce` leak.

## Rules that still apply here

- Compile without the editor: `bash docs/compile-check.sh` (exit=0).
- Pure self-test: `dotnet run -c Release` in `docs/cup-build/cuptest` - must print ALL PASSED.
- Scripts are CRLF; normalise any `.cs` you edit.
- Never say "tie" in cup code or UI. A ROUND is one match; the five bracket levels are STAGES.
- Nothing in this mode has been committed by the review passes.
