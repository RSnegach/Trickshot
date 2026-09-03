# Handoff: live menu scene panels (pass 2)

## Current task

The Single Player and Multiplayer submenus now show large panel buttons, each rendering a LIVE
ragdoll vignette of that mode. Built and compiling; the last round of choreography changes is
**written but never run**, because the editor was uncompilable at the time (a parallel session was
mid-way through an Accuracy rework). **That rework has since landed and the tree compiles clean, so
the outstanding work is verification, not writing.**

Design and rationale: `docs/menu-scenes-design.md` (its "What the build changed against this plan"
section is the corrections list). Durable API rules: the "Menu scene panels" section of `CLAUDE.md`.

## Acceptance criteria (the user's own words)

- Striker: faces the camera "a little bit more off center"; goes up **instantly** on hover; the ball
  **hits the boot**; he **rolls onto his back** limp.
- Match: both start closer; the tackle launches **instantly**; the tackler **crashes into** the
  dribbler (he was running out from under it); **both end limp on the ground, on screen**; the whole
  scene is **2 seconds max**.
- Goalkeeper: zoomed out, starts centred, lands inside the frame; **several saves that cycle
  randomly** - a lunge, a catch, and a spread save.
- Free kick: zoomed out a little, wall further away so the ball does not hit it.
- All scenes: no background, no ground, no sky - just the character(s) and the ball.

## State of each scene

| Scene | State |
|---|---|
| Striker | Facing + instant jump + back-landing VERIFIED earlier (`MinUp` -0.58, `Tumbled` true). Boot contact **unverified** - last change is a rewrite, see below. |
| Match | Intercept aim, spacing, 2 s hold, both felled: **all unverified**. |
| Goalkeeper | Five random shots: **unverified**. Earlier single-dive version was verified working after the `outSign` fix. |
| Accuracy | Verified composing correctly. |
| Free Kick | Wall distance 6.2 m and reframing **unverified**; earlier version verified showing taker + wall. |

## The one substantive open risk: striker ball-to-boot

Four ballistic attempts all missed (closest approach 1.72 -> 1.19 m; a miss of 20 cm is a miss). The
reason is structural, not tuning: `BallController.LaunchTo` solves an arc THROUGH a point fixed at
launch, while the boot is swung by a whole-body torque whose rate varies with `PlayerProfile
.AirFlipMul` and the frame rate, so predicting the bone's position 0.2 s ahead is unreliable.

**Current approach (untested):** `StrikerScene.TickHoming` holds the ball kinematic and flies it
along a lifted lerp onto the boot's LIVE position, re-read every frame, then releases it into
physics on arrival with its carried velocity. The strike itself still goes through `KickDetector` /
`BallController`, so the bicycle classification and pace bonus remain real.

If it still reads wrong, the fallbacks are: shorten `ServeTime` (0.14) so there is less to
interpolate; raise `ServeLift` (0.35) so the ball rises into the foot more visibly; or move
`ServeAtUp` (0.18, the lean at which the serve fires) later/earlier to change where in the flip
contact lands.

## How to verify (the harness matters)

`MenuUI.OnGUI` re-latches hover from the real mouse EVERY frame, so a reflection-driven `SetHover`
is overwritten instantly. Sequence that works:

1. Play mode, then set `MenuUI._phase` to `SinglePlayer` by reflection; wait ~3 s for all five
   sub-stages to build (one per frame, `MenuSceneStage._built` reaches 5).
2. `menu.enabled = false` — this is the step that makes the test deterministic.
3. `stage.SetHover(null)` then `stage.SetHover(GameMode.X)` for a clean restart.
4. Wait ~2 s, then read the run record.

`StrikerScene` exposes a run record for exactly this: `MinFootBall` (want < ~0.4), `MinUp` (want
negative = went past horizontal), `Tumbled`, `PeakY`, `BootAtPeak`, `BallAtPeak`. Add the same to
`MatchScene` if its contact needs the same treatment.

~~The user has asked to stop using Unity MCP for verification.~~ **REVERSED (2026-09-03):** the
user re-enabled Unity MCP and asked for it to be used - a later session drove the whole accuracy
flow through it (play mode, reflection navigation, `ScreenCapture`, `read_console`) and found two
real UX bugs a compile pass could not. The harness sequence below still applies. Note
`execute_code`'s compiler is CodeDom (C# 6): no `out var`, no local functions.

## Known-good arithmetic (do not re-derive)

- Match spacing: slide launches at `SlideLunge` 8.5 m/s decaying by `SlideFriction` 0.955/frame. A
  3.5 m start gap gives contact at ~0.3 s after ~1.9 m of travel, leaving ~1.7 s of both bodies down
  inside the 2 s hold. A 2.1 m gap made contact at 0.03 s (slide invisible).
- Keeper dead band ≈ 4.2 m/s of reach at ability 0.6; a 2.4 m offset needs a flight under ~0.5 s or
  he sidesteps instead of diving.

## Also done this session (complete, not blocked)

- **Pass 1, "Other Modes" breakup** — finished and verified in the editor. See the "Multiplayer menu
  flow" section of `CLAUDE.md`.
- **Trickshot World Cup** idea added to `DESIGN_NOTES.md` (idea only, nothing built).
- Material-leak fixes at the source in `AccuracyTarget`, `DefensiveWall`, `BallController`,
  `ActiveRagdoll`.

## Everything is uncommitted

All of the above sits in the working tree on `main`, interleaved with the parallel session's
Accuracy work. Nothing has been committed. `bash docs/compile-check.sh` is clean.

**Update (2026-09-03):** that parallel Accuracy rework is COMPLETE and verified in the editor - it
is no longer a moving target under this task. The tree compiles clean in both Roslyn and Unity, and
Unity's console shows no errors. Accuracy's durable rules are in `CLAUDE.md` ("Accuracy mode
(practice / challenge)", "Single-player setup screens", "Setup panels", "HUD panels", "HUD
callouts"). The menu-scene verification described above is still the only outstanding work here.

**Update (2026-09-03, later session):** three more pieces landed, all COMPLETE and verified in the
editor with screenshots - nothing about them is outstanding, and their durable rules are already in
`CLAUDE.md` (see "HUD callouts", "Accuracy mode", "Steam", "IMGUI modal rules"):

1. Accuracy callouts shortened to `GOAL` / `STRIKE n` (practice: `GOAL` / `MISS`) in SP and MP.
2. SP accuracy CHALLENGE end card - it previously had no buttons at all, only "Press R", with the
   cursor still captured so nothing was clickable. Now Replay / Match Setup / Main Menu.
3. MP accuracy results card (winner crowned, losers below) and an in-game Steam invite friend
   picker (`InviteFriendsUI`) on the host lobby.

The menu-scene verification at the top of this file remains the ONLY open work in the tree. Note
that a parallel session was editing `NetSetPieceMatch.cs` at the same time as item 3; the combined
file compiles clean, but that session's own edits were not reviewed here.
