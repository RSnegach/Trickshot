# Trickshot Cup - handoff (2026-09-04)

## Status in one line

The whole mode is BUILT and COMPILE-CLEAN, the pure core passes its self-test, and **nothing has
ever been run in the Unity editor or on loopback**. Everything below marked "verified" means
verified by reading the code and by `bash docs/compile-check.sh` / `dotnet run` - never by playing it.

## What was built

`Assets/Scripts/Cup/` (~50 files) implements `GameMode.TrickshotCup` in three styles - **Solo** (SP),
**Head to Head** (MP) and **Co-op** (MP) - to the spec in `docs/trickshot-cup-design.md`. Five
stages (Round of 32 .. Final), 32 nations from the `JerseyDesigns` Nations tab, a seeded draw, a
referee, a coin toss, penalties or free kicks, a per-stage difficulty ramp, replay + emote wheel,
a podium (Solo / H2H) and a trophy lift (Co-op). Wire: `NetCodec.ProtocolVersion` 8,
`MatchConfig.cupStyle/cupFormat`, `MsgType` CupState 23 / CupRequest 24 / CupStream 25 /
CupRoundState 26, `CupNet.StateVersion` 1, `NetRole.Entrant`.

Build history and per-agent detail: `docs/cup-build/reports/phase{1,2,2b,3,4}-*.json`; the codebase
facts the builders worked from: `docs/cup-build/cup-build-notes.md`. Durable invariants live in the
"Trickshot Cup" section of `CLAUDE.md` - read that before touching this code.

## Verification state

| What | State |
|---|---|
| `bash docs/compile-check.sh` | exit=0 (whole runtime assembly) |
| `dotnet run` in `docs/cup-build/cuptest` | ALL PASSED, 562277 checks (pure core: rules, bracket, sim, RNG, nation table, wire cap) |
| Editor / play mode | **never run** |
| Loopback multiplayer | **never run** |
| Any visual, layout, camera or choreography claim | **unverified** - a clean compile says nothing about what it looks like |

## In-editor verification still owed (do this first)

The editor was not available to any agent in this build. Use Unity MCP per the "Verifying UI in the
editor" rules in `CLAUDE.md`: `manage_editor play`, navigate by reflection on each screen's private
`_onPicked` / `_onDone` / `_onStart` callback (set `enabled = false` first, as the real button
does), `ScreenCapture.CaptureScreenshot` in a SEPARATE `execute_code` call, then Read the PNG.

1. **Self-test in the editor.** Menu `Trickshot > Cup > Run self-test`. It also validates the nation
   table against the live `JerseyDesigns` library, which the console project cannot do (one warning
   per drifted row). Expect no errors.
2. **Solo, end to end.** SP chain: Choose(TrickshotCup) -> StadiumSelect -> SpeciesSelect ->
   Customize -> the cup setup fork (`CupSetupUI`: Penalties / Free Kicks) -> `NationPickerUI` ->
   bracket -> a played Round of 32 round. Check in order: the loading card (>= 1.5 s), the intro
   card (3 s), the coin ceremony (the face shown must match the seeded first kicker plus your call),
   the referee raising a hand 0.4 s before every whistle, the penalty camera framing (posts near the
   frame edges WITH the ball in view - it lands around 31%/69%, not the design's 11%/89%; that trade
   is documented on `CupPenaltyCam`), the kick clock, a GOAL / SAVED / MISS callout under the
   scoreboard, the replay and its skip vote, the walk-back ARRIVING rather than snapping inside
   3.5 s, the stage-complete lobby with its staggered simulated rows, and the podium.
3. **Free Kicks specifically.** No lineup, no walk-in / walk-back. Confirm the seeded scatter keeps
   every body OFF the run-up path and off the spot - a body touching the dead ball under
   `SetPieceShot` re-launches it at full power, the one physics hazard in this format - and that a
   missed kick plays the 3 s dejection on the spot.
4. **Escape ownership.** With the emote wheel open, Escape must CLOSE THE WHEEL and not open the
   pause menu (new this pass: `CupEmoteWheel.AnyOpen` feeding `CupEscape.Owned`). Then press Escape
   during the loading card and during the intro card: the pause menu must be VISIBLE and clickable,
   because the cards now drop behind it. Check this in a networked style too, where
   `PauseMenu.Overlay` is true and `Frozen` is therefore always false.
5. **Loopback multiplayer** (`Multiplayer.UseDirectIp = false`, two editors or a build plus editor).
   Carried verbatim from `phase4-multiplayer.json` r1, which is the intended first pass:

   > Head to Head lobby of two. Watch in order: `director.LastStateBytes` on the host after the pick
   > (~60-200 B), the parallel wave (both loading cards, both tosses under the Round phase, live rows
   > in the other peer's lobby, Spectate on the playing row -> CupStream puppets on the watcher), the
   > Round of 16 human-vs-human round (interstitial on both; the client gets ONE intro card, its
   > cursor captured, its power meter sweeping while it charges - the display taker - and its release
   > landing on the host's body a round trip later), a client Quit mid-host-round (`HandSlotToAi`
   > arms the bot), Play Again from the podium (the seed changes, both peers back to CHOOSE YOUR
   > NATION, the podium's emote cursor reset), and End Match from the host (clients reach the main
   > menu through the Ended echo, never "connection lost"). Then one Co-op stage: the client's career
   > round write must land on the Bracket entry, a client keeper diving on the host's ball a round
   > trip late, the trophy lift's free window moving a remote human.

   Also new this pass and worth watching there: client puppets now animate (`DisplayAnim` off
   `BodyState.anim`), so a running AI taker and a diving keeper should no longer slide as statues.
6. **Co-op order screen** (never seen): the drag (MouseDown latch, MouseUp drop, the `Mouse.current`
   fallback when the pointer leaves the window), the reel faces and the knob arc, eight slots plus
   the lever at 1280 wide, and the keeper-left prompt.

## Fixed in this verification pass

- **NET-1 (blocker), the kick-line wire cap.** A kick line was bounded only by the codec's u8, so a
  modified client could report 255 kicks, and 31 such rounds is about 4 KB in one reliable datagram
  that `DirectIpTransport` never fragments. Now `CupRoundRules.Validate` takes a `maxKicks`
  parameter, passed as `CupTuning.MaxKicksInLine` (30) by the host's `CupDirector.ApplyRoundResult`
  and by a client's `CupRoundDriver.ApplyState`, and `CupRoundDriver.CapOutcome` overrides the last
  allowed kick so a live line always ends DECIDED (`CupBracket.SetResult` accepts nothing else).
  Four self-test checks added for it.
- **cup-ui-6, Escape and the emote wheel.** `CupEmoteWheel.AnyOpen` now feeds `CupEscape.Owned`. It
  is an EXPIRING stamp rather than a reference count, because no owner closes its wheel in
  OnDestroy and a leaked count would swallow Escape for the rest of the session. `CloseOnEscape` /
  `EscapePressed` are wired into CupHud, CupPodium and CupTrophyLift.

## Verified already fixed (by reading the code, not just the reports)

CUP-01 (client ball spot plus wall, `ClientSyncBallSpot`), CUP-02 / cup-ui-2 (the walk-back solves
its speed against `WalkBackMax`), CUP-04 / NET-5 driver half (`ApplyBodyPose` -> `DisplayAnim`),
CUP-05 / NET-2 / NET-3 / cup-ui-1 (both cards behind the pause menu), CUP-06
(`Celebration.OnWheel` bound), CUP-07 (the podium freezes HairSim and AnatomySim), CUP-08 / NET-4
(the unspectate latch), CUP-09 / NET-6 / cup-ui-3 (the DriverBridge reflection seam is deleted),
CUP-10 / cup-ui-8 (penalty-cam constants and docs), CUP-11 (the nation-table wire rule is
documented), CUP-12 (row stagger), CUP-13 / cup-ui-7 (the dead `teamW`), CUP-14 (a refused Head to
Head result settles at once), cup-ui-9 (`LineupMark` docs), cup-ui-10 (cached style). Phase-4 items:
(e) the display taker resets on any phase or role change, (f) a client puppet can never reach
`Celebration.Play` - doubly gated, since `SimTick` runs only under Local/Host authority and
`NetInput` is assigned only on the host, (g) `HandSlotToAi` (Head to Head, host rounds) and
`HumanLeft` (Co-op) are gated by style so they never both run on one round, (h) `_netLostAsClient`.
The three coin seams - the ceremony-open gate, local-only judging for a Local-authority Head to Head
round, idempotence through `CoinCallRight`, and a changed call clearing its old verdict - are all in
place and consistent.

## Still open

**Needs an editor - hand to whoever has one next:**

- **`Celebration.ClampArms` is INERT and its sign is wrong** (CUP-03). The arm-clip safety net
  abducts INWARD, so it has never fired on any emote in the project. The diagnosis was confirmed
  numerically, running forward kinematics through Quaternion.Euler's true ZXY order: `+Z` on a LEFT
  arm swings the elbow ACROSS the chest, and outward is `-Z` on a left limb and `+Z` on a right.
  `CupPoses`' header states this correctly; `EmotePose`'s stated the opposite and has been
  corrected. The CODE was deliberately not changed: flipping the two call-site signs makes the clamp
  fire on 25 of 38 emotes and destroys them, because its premise - that abduction cannot change the
  read of a pose - is false wherever a hand near the body IS the pose. Clap's hands stop meeting,
  HeartHands comes apart, Facepalm's hand leaves the face, the referee's WhistleRaise hand leaves
  his mouth, DejectHips' hands slide off the hips. Making the net live needs the box test to exclude
  intentional hand-to-body poses plus an editor eyeball over those 25. A measured before/after table
  is recorded in the code.
- **Every visual, layout and choreography claim in the cup.** Nothing has been looked at.

**Contained code gaps:**

- **Spectator puppets still slide** (the spectator half of NET-5). `CupSpectatorView` poses with
  `DisplayEmote` / `DisplaySnap` only. The fix needs an `anim` byte added to `CupStreamBody` in
  `CupNet.cs` (bump `CupNet.StateVersion`, keep `CupNet.SizeOf` honest) plus the same `DisplayAnim`
  path in the view. Deliberately not done during verification, because it widens the wire.
- **The cup lobby Customize button is dead in both Solo and Head to Head** (`TODO(h2h-customize)`;
  `OnCustomizeRequested` is null in both). It now shows an honest "not yet available" hint rather
  than an unexplained grey button. Real routing needs `GameBootstrap.ShowLobbyCustomize` - which
  returns to the multiplayer `LobbyUI`, and whose preview camera was never checked against a
  standing arena - plus both directors.
- **A claimed ball may read as `approaching`.** `KeeperHands.Holding` is public, but neither
  `Goalkeeper` nor `KeeperController` exposes its private `_hands`, so the driver's `approaching`
  test cannot see a held ball; a human keeper walking toward his own goal with the ball could hold
  the verdict out to the 20 s `LiveHardCap`. Bounded, since that cap is unconditional, and never
  observed. The fix is one passthrough property on each keeper plus a term in the test - left alone
  rather than touching shared gameplay files during a verification pass.
- **A wall deflection flying back into the scattered free-kick group** can touch a body and
  re-launch under `SetPieceShot`. Rare; `IgnoreBody` per body is the fix if cheap.

**Accepted phase-4 design gaps - do NOT report these as bugs:** no client-side keeper prediction (a
client's local keeper is a puppet answering the host a round trip late); the host's `EndMatch` sends
`CupState(Ended)` once and shuts the socket, so a lost packet leaves a client on the 5 s timeout;
the Co-op lever-reel gate is host-local time; a leaving keeper's gloved body is re-slotted to the
lowest-ordered shooter for the rest of that round, keeping the leaver's look; `ApplyLeave`'s shed
rule for a bench leaver in a partial order is a guess the Captain corrects; during a parallel Head
to Head wave a bodiless peer can spectate but cannot call that round's coin, so design 6.11's
spectator call holds for host rounds only; the host goes from the last parallel round straight to
the Interstitial / Podium with no lobby beat (design 4.7); a client participant's coin ceremony runs
on its own clock and may still be in the air when the host's Intro state arrives (cosmetic).

## Rules for whoever picks this up

- Compile with `bash docs/compile-check.sh` (exit=0). The open editor holds `Temp/UnityLockfile`, so
  batchmode is out.
- Scripts are CRLF. Normalise after any programmatic edit: `sed -i 's/\r$//; s/$/\r/' <file>`.
- Never use the word "tie" in cup code or UI. A ROUND is one match; the five levels are STAGES.
- A nation-table edit - a new row, a removed row, a flipped Novelty flag, an edited Strength - is a
  WIRE change and needs a `NetCodec.ProtocolVersion` bump. The only symptom otherwise is a one-off
  bracket-hash warning that is logged and never repaired.
- Adding a replicated field means `CupStateMsg` plus `NetCodec.CupState/ReadCupState` plus
  `CupNet.BuildState` plus `CupDirector.Net.NetApplyState`, a `CupNet.StateVersion` bump, and
  keeping `CupNet.SizeOf` honest (721 B worst case today against a roughly 1.2 KB budget).
- Nothing in this build was committed.
