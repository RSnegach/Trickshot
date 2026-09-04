# Trickshot Cup - design

Status: **planning draft, nothing built.** This is the spec for the whole mode across its three
ways to play. Every claim about existing code was verified against the tree on 2026-09-03; every
number that is a guess is marked *(tune)*. The user's review answers of 2026-09-03 are folded in
(section 12.1 is the decisions log); the one reading still to confirm is in 12.2.

Naming, fixed by the user:

| Name | Where | What it is |
|---|---|---|
| **Solo** | Single Player | One human, 31 AI nations, simulated AI rounds, a podium at the end. |
| **Head to Head** | Multiplayer | Up to 8 humans share one bracket. Each plays their own round against AI at the same time; when two humans are drawn together that round is played head to head. Winner on the podium. |
| **Co-op** | Multiplayer | Up to 8 humans are ONE nation. A shooting order and a human keeper per stage, a lineup with arms round shoulders, the AI nation doing the same across the box. A cinematic trophy lift if they win. |

Terminology: a **round** is one match between two nations (never "tie", which reads as a draw).
The five bracket levels are **stages**: the Round of 32, the Round of 16, the Quarter-finals, the
Semi-finals and the Final. Every player plays exactly one round per stage.

The mode is `GameMode.TrickshotCup`, appended at the END of the enum (it rides the wire as a byte).
Penalties or Free Kicks is a format choice inside the cup, not a separate mode.

---

## 1. The three ways to play, side by side

| | Solo | Head to Head | Co-op |
|---|---|---|---|
| Players | 1 | 2-8 | 2-8 |
| Nation | you pick one | each picks their own (distinct) | the lobby votes for one |
| Opponent per round | AI nation | AI nation, or another human | AI nation |
| Who shoots for you | you | you | the stage's shooting order, cycling |
| Who keeps for you | you | you | the stage's keeper (always a human) |
| Who calls the coin | you | you; a seeded random side when two humans meet | the Captain |
| Rounds per stage | 1 played, rest simulated | one per human, all at once; human-vs-human rounds afterwards, one at a time | 1 played, rest simulated |
| Between stages | stage results + bracket | the shared Cup lobby | bracket, then the shooting-order screen |
| Ending | podium with trophy and emotes | podium with trophy and emotes | Game Over + results, or the trophy lift |
| Shooting standardised | yes | yes | yes |

Everything below the flows (round rules, ramp, nations, draw, referee, choreography, podium) is
shared.

---

## 2. Shared rules

### 2.1 A round

A round is one bracket match between two nations. Penalties and Free Kicks use the same skeleton:

- **5 kicks each, alternating.** The side that wins the coin toss (7.1) kicks first.
- **Both roles every round.** On your kick you take; on the opponent's kick you keep. In Co-op
  the active shooter takes and the stage's keeper keeps.
- **Early finish.** The round ends the moment one side cannot be caught (the standard shootout
  rule: lead greater than the kicks the other side has left). An early finish is what makes a 5-5
  format average 8 kicks, not 10.
- **Sudden death.** Level after 5 each: pairs of kicks until one scores and the other does not.
  Each pair is one more kick per side, alternating in the same order.
- **Kick clock.** The taker has 30 s from the whistle (`CupTuning.KickClock`). The last 5 s deplete
  the frame around the power meter; there is no clock dial and no countdown number. Expiry auto-fires a weak shot (the
  existing `AutoLaunch(0.6)` watchdog). The keeper has no clock.
- **Verdicts.** GOAL, SAVED, MISS. A free kick stopped by the wall reads SAVED too, never
  "blocked". Free kicks and penalties use the same goal test (`BallFullyInGoal`, identical bodies
  in `FreeKickGame` and `NetSetPieceMatch`). No EPIC SAVE tier in the cup (same reasoning as
  accuracy: a save is a strike against the shooter).
- **Replay.** Only after a GOAL or a SAVE, never after a miss: 3 s window at 0.45 slow *(tune)*, so
  about 6.7 s watched, played after the celebration window. Skipping it is a vote that must be
  UNANIMOUS among the humans with a body in the round (the whole team in Co-op, both participants
  in a human-vs-human round, just you against AI); spectators do not vote. Small yellow text near
  the bottom of the screen reads "CLICK TO SKIP  0/3" before anyone clicks and "2/3" as clicks
  land (the existing `VoteSkip` plumbing, with the rule and the counter changed). The cup passes
  its own window to `ReplaySystem.Setup` rather than changing the global `SimConfig.ReplayWindow`.

**Penalties:** spot at 11 m (`SimConfig.PenaltyMode` layout, `FreeKickGame.PenaltyDistance`), no
wall, keeper on the line (`KeeperPenaltyStart`), the penalty camera (7.9).

**Free Kicks:** every kick PAIR has its own spot, and both sides take from the same spot for that
pair (kick 1 of A and kick 1 of B share spot 1); the spot changes once both have shot. Spots come
from `SetPieceMap.RandomSpot(rng)` on the round's seeded RNG, generated lazily so sudden death
never runs off the end (the current 10-entry schedule would repeat spot 10 forever). Cup band
**17-28 m** from goal, plus or minus 18 m across *(tune the width)*; 4-man wall at regulation
9.15 m (`WallCount`/`WallDistance`), hopping on the strike as today. The wall is built once per
pair, cleared before rebuild (it owns 3 materials).

### 2.2 The stage ramp (AI strength)

Difficulty is a pure function of the stage, never a knob. The user's numbers:

| Stage | R32 | R16 | QF | SF | Final |
|---|---|---|---|---|---|
| AI keeper ability (`SimConfig.KeeperAbility`) | 0.20 | 0.40 | 0.60 | 0.80 | 1.00 |
| AI taker strength `t` | 0.20 | 0.40 | 0.60 | 0.80 | 1.00 |

The taker strength maps through two tunables: `combined = Lerp(TakerMin, TakerMax, t)` for the aim
model (`SetPieceTaker.Begin(combinedOverride)`, exactly the MP remote-shooter path) and the power
bar target `Lerp(0.55, 0.85, t)`. Starting values `TakerMin 0.35, TakerMax 0.95` *(tune; the user
will tune accuracy in playtesting)*. The AI's launch speed ceiling is the human ceiling
(`HumanShotPowerMax`, `ShotAt(1,1)`), never above it.

Known quirk to design around: keeper 0.20 sits under the 0.30 claim/rush gates, so the R32 keeper
never rushes or claims. For penalties that reads as "easy" and is fine; note it when tuning.

`KeeperAbility` is a global static: the cup writes it per kick and restores it in `OnDestroy`,
like every other borrower.

### 2.3 The human keeper

No handicap of any kind (MP accuracy's 0.75 speed multiplier is not applied). The intended
difficulty curve for a human keeper is the AI taker's ramp itself: by the Semi-finals and the
Final the ball is fast and accurate enough that a save is partly a guess, which is the point. The
keeper's tools (strafe, jump, lunge, dive, dash dive) are unchanged.

### 2.4 Nations

- **Pool:** the 214 `DesignTab.Nations` jersey designs (`JerseyDesigns.InTab(DesignTab.Nations)`),
  each already drawn from its flag. `JerseyDesigns.Thumb(d)` is the 48x48 badge the cup uses as
  "the flag" everywhere (there are no separate flag textures).
- **Table `CupNations`:** one row per design name: `Name` (the design key), `Code` (3 letters,
  FIFA-style where one exists, hand table for the rest), `Strength` 1-99, `Novelty` flag. Novelty
  kits (Jolly Roger, Antarctica, Olympic, Pride Rainbow, European Union, Soviet Union, Catalonia,
  Vatican City, Greenland and similar) are excluded from the AI pool but a human may still pick one.
  An editor-time check logs any table row whose name does not resolve to a design.
- **Strength** is hidden flavour: it biases the simulated AI-vs-AI results (2.8) and is never
  shown anywhere (no stars, no sort by it). It never touches the ramp. Authored in code,
  real-world-ish.
- **Kit rule:** a nation's kit is painted on the torso of every body wearing that nation
  (`Make.MatTex(thumb atlas)` / `SetTorsoMaterial`). A human's custom jersey is replaced by the
  nation kit for the cup in every style; hair, face, accessories, species all stay.

### 2.5 The draw and the bracket

- **Field:** 32 nations, always. Humans' nations are in; the rest are drawn at random from the
  non-novelty pool with the seeded RNG.
- **Placement:** random, with one constraint: no two humans in the same Round of 32 match. Humans
  are dealt to distinct matches first (chosen by the RNG), AI nations fill the rest. With 8 humans
  and 16 matches this always succeeds; humans can only meet from the Round of 16 on, and those
  rounds are played head to head.
- **Bracket shape:** standard 32 -> 16 -> 8 -> 4 -> 2 tree, left half and right half, final in the
  middle. Round `i` of stage `s` feeds round `i/2` of stage `s+1`. Which side kicks first is the
  coin toss (7.1), not the draw.
- **Determinism:** the whole draw, every free-kick spot, every coin result and every simulated AI
  round derive from ONE `uint` seed (Head to Head / Co-op: the host's `MatchConfig.fkSeed`, which
  already exists; Solo: rolled at the fork screen). Peers never sync the bracket shape, only the
  results of rounds that humans played. This is the `fkSeed` pattern already used three times.
- `SeededRng`: a small xorshift/PCG utility with `Next01`, `Range`, `Shuffle`, `Fork(salt)`.
  The tree has six independent hand-rolled LCGs and one seeded `System.Random`; the cup adds the
  shared one and does not touch the others.

### 2.6 Standardised shooting

Every style sets `SkillTree.MaxShootingOverride = true` and `PlayerProfile.UniformBodyOverride =
true` for the whole cup, exactly like MP accuracy: every human shoots at the species ceiling from
the default height and weight, still looking like themselves. Both are cleared in the director's
`OnDestroy`. Solo included, so the stage ramp means the same thing for every player.

### 2.7 Timings

| Beat | Length |
|---|---|
| Bracket screen (all styles) | 5 s, no button |
| Coin toss: the HEADS / TAILS calls | until the official caller clicks, 5 s timeout picks heads |
| Coin toss ceremony (7.1) | about 3 s after the call, then the flash |
| Loading card before a round | at least 1.5 s; in MP also the "everyone loaded" barrier, 10 s timeout |
| Round intro card (nations, stage, first kick) | 3 s |
| Referee's whistle raise, before EVERY whistle | 0.4 s, then the whistle plays |
| Kick clock | 30 s, then the existing weak auto-shot |
| Scored: shooter's free run-and-emote window | 5 s; the scorer may click to skip it for everyone |
| Missed/saved: walk back to the lineup | up to 3.5 s, camera covers it |
| Won the round: whole lineup free to move and emote | 5 s; the scorer (or the keeper who made the winning save) may click to skip |
| Lost the round: dejection beat | 4 s, then the results / Game Over |
| Replay (goal or save only) | about 6.7 s, after the window; skipped only when every human in the round clicks |

### 2.8 Simulated AI rounds

`CupSim.Play(round, rng)` produces a kick-by-kick line, not just a winner, so the results list can
show pips and "SD" tags: per kick `P(goal) = clamp(0.72 + 0.20 * (takerStrength01 - keeperStrength01), 0.45, 0.92)`
using the two nations' `Strength`, the same 5 + sudden death + early-finish rules as a played
round. They resolve instantly on the results screen; nothing is spawned.

---

## 3. Solo flow (single player)

1. **Main menu > Single Player > "Trickshot Cup" panel.** Live vignette (`CupScene`, in the
   `DeadBallScene` family): a penalty struck low into the corner past a diving keeper, the taker
   fist-pumps, hold. Panel title from `PauseMenu.ModeName`.
2. **Stadium > Species > Customize** (existing chain, `UsesCustomPlayer`/`PicksSpecies` true;
   Customize's Jersey stage still runs, but the cup paints the nation kit over it, see 2.4).
3. **Cup fork screen** (`CupSetupUI`, shaped like `AccuracyModeUI`): two cards, PENALTIES and FREE
   KICKS. Back returns to Customize. No sliders, no goal editor: the cup has no settings. There is
   no resume: leaving a Solo cup ends it (12.1, answer 10).
4. **CHOOSE YOUR NATION** (6.1). Picking is confirming.
5. **Bracket screen** (6.2), 5 s: the 32 revealed, your nation with a gold spine and "YOU", your
   first opponent pulsing.
6. **Loading card > coin toss (you and the referee) > round intro > the round** (sections 6.4,
   6.5, 7).
7. **Stage complete screen** = the Cup lobby (6.3) with a single row: your result, "Simulating
   the rest of the stage" reveal (rows stagger in at 0.05 s), View Bracket, Customize (appearance
   only), Ready (= continue), Quit to Menu (confirm: "Quit the cup? This ends it.").
8. Repeat R16, QF, SF, Final. **Win the Final > podium** (section 8). **Lose > KNOCKED OUT** card
   (6.7) with "Simulate the rest" (the bracket fills stage by stage on each press, ending with the
   AI champion crowned), New Cup, Main Menu.

---

## 4. Head to Head flow (multiplayer)

1. **MP hub > "Trickshot Cup" > Host or Find.** Host setup (6.9) picks **Play style: Head to Head /
   Co-op** and **Format**. The advert label carries both ("Trickshot Cup - Head to Head -
   Penalties"), the session browser shows the tag on every row and filters on it.
2. **Standard lobby** (existing `LobbyUI`): flat roster, every slot an "Entrant" (no keeper/crosser
   roles - `RoleForSlot` must be mode-aware or the eighth row reads "Crosser"). Ready. Host starts.
3. **CHOOSE YOUR NATION** (6.1, Head to Head variant): the top strip shows every player's name; a
   flag pops in beside a name the moment they pick. Picking = ready. Nations are distinct: a taken
   one greys out; a race is settled by the host (first request wins, the loser's card snaps back).
4. **Bracket screen**, 5 s, all humans seeded into distinct Round of 32 matches, names beside flags.
5. **Stage, parallel phase.** Every human-vs-AI round starts at once on its owner's machine
   (locally simulated, no input lag). Loading card, coin toss, round intro, the round.
6. **Cup lobby** (6.3) as each player finishes: rows show live status for players still playing
   (opponent, score, kick number), Spectate on those rows, View Bracket, Customize, Ready, Quit to
   Menu. Eliminated players are auto-ready and may spectate to the end.
7. **Stage, head-to-head phase.** When every parallel round is done, each human-vs-human round of
   the stage is played ONE AT A TIME on the host (the existing host-authoritative set-piece path),
   both participants controlling, everyone else in the lobby watching through Spectate or the
   lobby's live row. The two participants see a "HEAD TO HEAD - up next" interstitial instead of
   waiting in the lobby. The phase is skipped when no humans met.
8. **Ready gate:** all rounds of the stage finished AND every surviving human ready. Then bracket
   screen (shrunk, 5 s) and the next stage.
9. **Final:** podium (section 8) for everyone still connected: the champion on the pedestal, the
   other humans around it. Buttons: **Play Again** (host; back to CHOOSE YOUR NATION with the same
   lobby, new seed) and **End Match** (dissolves the session, everyone to the main menu). A client
   sees End Match and a "waiting for the host" hint.

**Spectate = the spectated player's exact view.** Their client streams its round (bodies, ball,
camera pose) to the host at 20 Hz, the host forwards it to whoever pressed Spectate on that row.
No control on the spectator's side beyond Esc back to the lobby. When the round ends, the player
and all their spectators return to the lobby together. Spectating a head-to-head round works the
same way with the host as the source. A spectator already watching when a round's coin toss comes
up calls it like everyone else (6.11).

**Quit to Menu** mid-cup while others remain: the leaver's nation becomes AI for the rest of the
bracket (its later rounds are simulated), their row reads "Alice (AI)". A leaver mid-round forfeits
nothing: the round finishes with AI on their side. The host leaving ends the session for everyone,
as today.

---

## 5. Co-op flow (multiplayer)

1. **Host setup: Play style Co-op, Format.** Lobby as in Head to Head; the host is the **Captain**.
2. **CHOOSE YOUR NATION** (6.1, Co-op variant): everyone votes; a flag with several votes shows a
   small counter disc in its bottom-right corner; no top strip. Proceed when everyone has picked and
   one nation holds a majority. Picks can be changed until then. **No majority once everyone has
   picked: the Captain decides** - a CAPTAIN DECIDES button appears for the Captain and confirms
   the Captain's own pick.
3. **Bracket screen**, 5 s, the team's nation outlined and highlighted, "YOUR TEAM" with every
   player's name.
4. **Shooting order screen** (6.8): the Captain fills N-1 shooter slots and 1 keeper slot (2
   players: one of each; 8 players: seven shooters and the keeper) by drag-and-drop, or pulls the
   slot-machine lever for a random assignment. Everyone else watches it fill live and readies.
   Repeated before every stage.
5. **Loading card > coin toss (the Captain, the AI captain, the referee; the whole team calls it)
   > the round.** Shooters cycle in order across the round's kicks (kick 6 wraps to shooter 1);
   the keeper keeps every opponent kick and stands in the lineup otherwise.
6. **The round** with the full choreography (section 7): the lineup at the edge of the box, arms
   round shoulders and fixed in place with a look cone, the AI nation mirrored across the box; the
   scoring shooter's 5-second run and emote with a scorer-only skip; walk-back cinematic on a
   miss; dejection trio on a losing miss; the whole lineup freed for 5 s on a winning kick while
   the AI lineup dejects.
7. **Won:** the 5-second free window, then the bracket (5 s), then the next stage's shooting order.
   **Lost:** dejection, then **GAME OVER + results** (6.6) with **End Match** / **Play Again**
   (Captain; restarts from CHOOSE YOUR NATION with the same lobby).
8. **Won the Final:** the trophy lift cinematic (8.2), then the results screen with the same two
   buttons.

Every human is standardised (2.6). A player who quits is dropped from the order and the slot count
drops by one (their slots collapse); if the keeper quits the Captain is prompted to pick a new
keeper at the next order screen, and for the rest of the current round the lowest-ordered shooter
keeps.

---

## 6. Screens

All screens follow the house IMGUI rules: `MenuScale.Begin/End` on every path, callbacks fired
after `End`, controls allocated every pass, `UITheme.Label` never raw `GUI.Label`, large text
through `UIFont.Heavy`, hover = plate + gold glow, sizes against `MenuScale.Width/Height`.

### 6.1 CHOOSE YOUR NATION (`NationPickerUI`)

- Full screen, `Scrim(W, H, 0.40, 900)`. Title 44 pt "CHOOSE YOUR NATION". Cup title tag under it
  ("TRICKSHOT CUP - HEAD TO HEAD - PENALTIES", `Hint` style).
- **Head to Head strip** (top, 64 px tall, only this variant): one cell per player, name 15 pt with
  a 40 px flag slot to its right, empty until they pick, then the flag pops in (scale 0 to 1 with
  `EaseOutBack` over 0.25 s, unscaled time). Local player's cell has the lit band + slot-colour
  spine. Solo shows the single cell.
- **List:** one `GUI.BeginScrollView`, rows of 46 px: flag thumb 40 px at the left, name 16 pt,
  `Divider` between rows, alphabetical. No strength shown, no sort by it. Search field above the
  list (`GUI.TextField`, filters as you type; handle Enter/Esc before it draws). Novelty entries at
  the bottom under a "NOVELTY" `Section`.
- **Selection:** click a row = pick. The row lights (lit band + gold spine). In Head to Head the
  pick is the ready; a taken nation shows in `Dim` with "taken by Bob" and does not respond.
- **Co-op counters:** a 20 px `Disc` in the flag's bottom-right corner with the vote count when
  more than one player is on that nation; the leading nation's row gets the gold spine; a "majority
  reached" line replaces the hint when the gate is met; CAPTAIN DECIDES appears for the Captain
  when everyone has picked without a majority.
- **Keys:** Up/Down move, Enter pick, Esc back (Solo only: back to the fork; MP: no back, the
  lobby already committed).
- **MP transport:** picks are `CupRequest.PickNation`, echoed in `CupState`. Everyone renders the
  strip / counters from `CupState`, so host and clients agree.

### 6.2 Bracket screen (`CupBracketView` full-screen use)

- Header: "THE DRAW" on first showing, then "ROUND OF 16" etc. Nation count and format under it.
- Tree: 16 rows per half at the design height (760): row 20 px, code 12 pt + 16 px flag, scores
  when played, connector hairlines with `UITheme.Fill`. Later stages collapse the tree to the
  remaining nations (8 rows per half, then 4, 2, 1) so it "shrinks".
- Humans: name in 11 pt `Dim` beside the flag; in Head to Head the local player is gold-spined;
  in Co-op the team's nation is outlined (`FrameOutline`, gold) and highlighted with a gold band.
- Reveal: Round of 32 names fade in over 1.2 s in tree order.
- 5-second bar along the bottom (`Bar`, gold), then auto-advance. No buttons. In MP the host's
  timer is authoritative (the phase change is a `CupState`), clients just animate locally.
- The same renderer draws the Cup lobby's View Bracket overlay and the in-round Tab peek, at
  smaller scale.

### 6.3 Cup lobby (`CupLobbyUI`, Head to Head between stages; Solo with one row)

- Panel 640 wide (`Panel`, gold accent), title "TRICKSHOT CUP - ROUND OF 16" 28 pt.
- One row per entrant human, 46 px: flag, name, status cell, right-side button.
  Status values: "Playing vs GHA - 2-1 - kick 4" (live, updates from `CupState`), "Won 4-2",
  "Won 5-4 SD", "Out (lost 2-3 to BRA)", "Spectating Alice", "Ready", "(AI)".
  Row button: **Spectate** while that player is playing; nothing otherwise.
- Local player's row lit with the slot-colour spine.
- Footer buttons (screen-pinned like `DrawNav`): **View Bracket** (overlay, Esc closes),
  **Customize** (appearance only; the Jersey stage is skipped and the nation kit is re-applied on
  return - the lobby customize path already exists: `ShowLobbyCustomize`), **Ready** (toggle;
  disabled with "your round is still on" while playing, auto-on when eliminated), **Quit to Menu**
  (`bad`, confirm card: Solo "Quit the cup? This ends it."; Head to Head "Quit the cup? An AI
  plays your nation from here.").
- Gate line above the footer: "Waiting for 2 rounds to finish" / "Waiting for Bob, Cara" /
  "Head to head next: Alice vs Bob". Then the bracket screen.

### 6.4 Loading card (`CupLoadingUI`)

- Centered `Panel` 420x180: stage, "BRA vs GHA", the two flags, `Spinner`. Solid `Scrim` 0.9 so
  the round build (arena already standing, bodies + referee + wall + ball) is never seen popping
  in.
- Minimum 1.5 s. MP: shown until every peer has sent `CupRequest.Loaded` or 10 s pass; the host
  then broadcasts the coin toss.

### 6.5 In-round HUD (`CupHud`)

- **Scoreboard** top-centre: `Hud.Scoreboard(homeName, homeCol, homeScore, awayScore, awayName,
  awayCol, sub:)` with codes for names and the nations' primary kit colours; sub-line "KICK 3 of 5"
  or "SUDDEN DEATH - KICK 7". Under it two rows of pips (5 each side, `UITheme.Disc`, green /
  red / empty at 0.14 alpha; sudden-death pips append).
- **Role panel** top-left (`Hud.PanelStart("TRICKSHOT CUP", 3)`): Stage, You ("Taking" /
  "Keeping" / "In the lineup" / "Watching Alice"), Nation.
- **Kick clock ring** around the power meter for the last 5 s.
- **Callouts** through `Hud.Flash`: GOAL / SAVED / MISS (a wall stop is SAVED), and HEADS / TAILS
  after the toss. The round-end line through `Hud.Banner`: "BRAZIL WIN 4-2" / "KNOCKED OUT 2-3". `Hud.KindOf`
  gains "KNOCKED OUT" in the failure tier (rule 1), "HEADS"/"TAILS" in the informational tier and
  " WIN" in the plain-good tier; the order note in CLAUDE.md applies.
- **No calling during play.** The only prediction in the cup is the coin call before the flip
  (6.11); the HUD carries nothing for it once the shot stage begins.
- **Skips:** small yellow text near the bottom of the screen. During a scored window only the
  SCORER sees "CLICK TO SKIP"; during a goal replay everyone with a body in the round sees
  "CLICK TO SKIP  0/3", counting up as clicks land (2.1).
- **Tab** holds the bracket peek (small tree, the live round pulsing).
- **Legend** per role: taker (charge, aim, spin), keeper (move, dive), lineup ("Look around",
  "B emotes" once freed), spectator ("Esc back to lobby").
- Quick chat feed (existing) in MP.

### 6.6 Results / GAME OVER (`CupResultsUI`)

- Co-op **GAME OVER** (lost) or **CHAMPIONS** (won the Final): title 54 pt, accent red / gold.
- Tabs across the top, one per stage played plus TOTAL (`Hud.Seg`). Each tab is a table: one row
  per player (names), columns Kicks, Goals, Missed, Saved-against, GK Saves, GK Conceded; the
  keeper's row shows the keeper columns, shooters' rows the shooter columns. TOTAL sums. Table
  styling from `CareerStatsUI.DrawRows` (28 px rows, 6 px gap, dividers).
- Head to Head / Solo use the same screen after the podium as "CUP SUMMARY": one row per human,
  stage reached, rounds won, goals, saves, coin calls right; Solo adds career best stage.
- Buttons: **End Match** (`bad`; MP host: dissolves the session; client: leaves) and **Play
  Again** (host/Captain only; clients see "waiting for the captain"). Solo: **New Cup** / **Main
  Menu**.

### 6.7 KNOCKED OUT card (Solo)

- `Panel` 520x300, red accent, "KNOCKED OUT IN THE ROUND OF 16", the losing line, career best
  stage if beaten. Buttons: **Simulate the rest** (opens the bracket screen; each press fills one
  stage; the last shows the champion crowned), **New Cup**, **Main Menu**.

### 6.8 Shooting order screen (`CupOrderUI`, Co-op)

- Title "SHOOTING ORDER - ROUND OF 16". The Captain's name and "decides" tag; others see
  "Captain is choosing".
- **Slots:** a horizontal row of N tall slots (140x190 each, gap 14): slot 0 is the KEEPER slot
  (green frame, glove icon drawn like `GoalEditor.DrawKeeper`), slots 1..N-1 are numbered shooter
  slots. A filled slot shows the player's chip: name 16 pt, their body preview colour, and "1st",
  "2nd"... Empty slots show a dashed frame.
- **Bench:** a row of draggable player chips beneath (96x40). Captain drags a chip into a slot;
  dropping on an occupied slot swaps the two. Dragging is a hand-rolled IMGUI drag: latch the chip
  on mouse-down (Repaint-safe), draw it at the mouse, drop on mouse-up over a slot.
- **Slot machine lever** at the left: a 60x220 plate with a knob. Click (or Space) pulls it: the
  knob arcs down over 0.3 s, every slot's face spins through the roster names for 1.8 s
  (`unscaledTime`), slots stop left to right 0.25 s apart, each landing on its assigned player.
  The permutation is rolled by the host from the cup RNG (`Fork(stage)`), broadcast in `CupState`
  before the animation starts, so every client's reels land on the same faces.
- **Rules:** exactly one keeper, must be a human (only humans are on the bench, so it always is);
  every slot filled before Ready enables; a player may not be in two slots.
- **Ready** for everyone; gate = all slots filled and all ready. Then the loading card.
- Everything the Captain does is a `CupRequest.SetOrder`, echoed in `CupState`, so clients see the
  chips move.

### 6.9 Host setup (`HostSetupUI`, cup branch)

- `SetupPanel.Begin` plate; goal picture LOCKED (regulation) with no keeper row (the ramp owns it).
- Rows (52 px each, `LadderPicker` style): **Play style** [Head to Head] [Co-op] with a one-line
  description below the selected one in `Hint` style; **Format** [Penalties] [Free Kicks];
  **List in Find a Session** toggle. No map, no sliders, no field-size picker (always 32).
- `Create()` writes `MatchConfig.cupStyle`, `cupFormat`, `fkSeed` (fresh), `maxPlayers = 8`.
- `SessionBrowserUI`: the row's meta shows "Head to Head - Penalties"; a two-button style filter
  under the mode title (like the Match role row).

### 6.10 Pause menu in the cup

- Entries: Resume, Settings, then per style: Solo **Quit to Menu** (confirm: "Quit the cup? This
  ends it."); Head to Head / Co-op client **Quit to Menu** (confirm: "An AI plays your nation from
  here" / "You are dropped from the order"); host **End Match** (confirm: "Ends the cup for
  everyone"). Quit to Desktop.
- No Restart, no Match Setup: `PauseMatchSetup.RowsFor(TrickshotCup) = 0` and `onFullSetup` null.
- **Pausing freezes the game in Solo only.** In Head to Head and Co-op the pause menu is an
  overlay: the sim, the kick clock, the camera and everyone else keep running; the local player's
  input is cut and the cursor freed while it is up. `PauseMenu` gains an overlay mode for this
  (`Paused` stays true for the menu's own guards, but nothing reads it to stop time or the
  camera). Like every real multiplayer game.
- Esc during the lineup look opens the pause menu normally; the emote wheel owns Esc while open
  (existing `SetWheelOpen` contract).

### 6.11 Coin toss call (`CupCoinToss` overlay)

- Shown over the pitch with the referee and captains already on their marks (7.1). Header
  "COIN TOSS - call it". Two buttons **HEADS** / **TAILS** (180x60 each, side by side, centred in
  the lower third, gold glow on hover) for EVERY human present at the toss: the participants or
  the whole Co-op team, plus in Head to Head any spectator already watching. The official caller's
  buttons carry a gold "DECIDES KICK-OFF" tag; everyone else's pick is a prediction. A pick lights
  and can be changed until the flip.
- The flip starts once the official caller has picked (5 s timeout picks HEADS for them; anyone
  else still undecided simply makes no call). After the coin settles: `Hud.Flash("HEADS")`
  (neutral grey), the sub-line "GHANA KICK FIRST", and in Co-op the calls band (6.12) for 3 s.
  Head to Head shows nothing for the predictions; they are only counted (9.7).
- Once the flip ends and the round advances to the shot stage there is no calling of any kind.
- Wire: `CupRequest.CallCoin(guess)` from every caller; the host records them all, echoes the
  official call and the result in `CupRoundState`, and the right/wrong tallies ride `CupState`.

### 6.12 Calls band (Co-op only)

- Right after the coin settles, a 220 px band on the right for 3 s: title "CALLS", one 26 px row
  per team member who called: name, HEADS or TAILS, and a green check or red X. Then gone. Data
  from `CupState`.

---

## 7. Choreography and cameras

The kick cycle, shared by every style (a 1v1 round is the Co-op choreography with a lineup of one).

### 7.1 The referee and the coin toss

**The referee** is in every round, in every style. A Human-species AI body with no cosmetics, a
black-and-white vertical-striped kit (a `Referee` design added to `JerseyDesigns`, built from the
existing stripe primitive) and black shorts. He never touches play: his colliders ignore the ball
(`BallController.IgnoreBody`) and he is not in the snapshot's player set (a virtual slot like the
AI bodies).

- **Mark during play:** 3 m to the side of the ball spot, level with it, facing the taker, still.
- **Whistle raise:** before EVERY whistle (each kick's arm, and once for the full-time triple) he
  plays `WhistleRaise` (right forearm to the mouth, 0.4 s ease-in) and the whistle audio fires at
  the end of the raise; he holds the pose through the whistle and drops it 0.5 s later. The cup
  driver owns whistle timing: raise -> `PlayWhistle` -> Armed. (`FreeKickGame` and
  `NetSetPieceMatch` play the whistle at Arm; the cup driver sequences it.)
- **Coin toss ceremony, at the start of every round:** the referee walks to the penalty spot; the
  two captains stand 1.2 m either side of him, facing him (Head to Head vs AI: the human and the
  AI nation's body; human vs human: both humans; Co-op: the Captain and the AI captain; Solo: the
  player alone with the referee). The HEADS / TAILS overlay (6.11) waits for the official call
  while everyone present makes theirs. Then: the ref flips a large coin off his hand - a gold
  `MeshGen.Cylinder` disc 0.25 m across and 0.02 m thick, launched from the hand on a scripted arc
  (1.4 s up and down, 6 revolutions per second), landing 1 m in front of him with one small bounce
  and settling face-up on the seeded result (`Fork(roundIndex)`, so every peer's coin lands the
  same). 0.6 s hold, `Hud.Flash("HEADS")`, the "X KICK FIRST" sub-line, cut to placement. About
  3 s after the call.
- **Framing:** one static wide shot from the goal-side low angle, 6 m from the group, 1.6 m high,
  FOV 55, aimed at the referee's chest, so all three characters and the coin's landing spot are in
  frame together.
- **The official caller:** the human against AI, the Captain in Co-op, and a seeded random side
  when two humans meet. A correct call kicks first; otherwise the other side does. Everyone else
  present calls too, as a prediction (6.11).

### 7.2 Kick cycle

```
Coin toss (round start, 7.1)
  -> Intro card (3 s)
  -> Place: taker to the spot (+3 m run-up), keeper to the line, lineups and referee to marks
  -> Referee raises, whistle, Armed (kick clock 30 s)
  -> Live (strike) -> verdict
  -> GOAL: shooter free run + emote, 5 s, scorer may click to skip (replay after)
     MISS/SAVED: walk-back cinematic, up to 3.5 s (replay after, save only)
  -> Round decided?  no  -> next kick (roles swap / next shooter in order)
                     yes -> WIN beat (5 s free lineup, skippable by the scorer) or
                            LOSE beat (4 s dejection) -> results
```

### 7.3 Bodies and marks

- Penalty spot 11 m from goal. Lineups on the edge of the box, 1 m outside the 18-yard line
  (`GoalCenter.z - PenaltyBoxDepth - 1`), the human team at x = -6 and the AI team at x = +6, each
  a line of bodies 0.62 m apart *(same as `DefensiveWall.ShoulderSpacing`)*, facing the goal.
- **Arms-round-shoulders pose:** a new static pose set applied through `SetPoseOverride`: both
  upper arms raised sideways to shoulder height, forearms bent down so the forearm end rests on the
  neighbour's far shoulder; end bodies drape one arm only. Bodies are live (balance on, locomotion
  off) so they sway naturally.
- **Fixed in place with a look cone:** each waiting human's camera is a cone from their own body
  facing the goal (`KeeperFollow`-style: yaw plus or minus 50 degrees, pitch -10 to +25), mouse
  look only, no movement. Close enough to the box that the goal is what there is to look at.
- **The keeper** stands in the lineup on own kicks and is placed on the line for opponent kicks
  (`ResetHumanKeepers` path, `KeeperFollow` camera, `KeeperLookYaw` on the wire).
- Every round needs two bodies per HUMAN who both takes and keeps (Solo, Head to Head): a shooter
  body and a gloved keeper body, because gloves and keeper hitboxes are baked at `Build`
  (`withGloves`). Only one is live and visible per kick; the other is parked hidden behind the
  goal. The swap happens under the placement cut, never on camera. Co-op needs one body per
  human (fixed roles) plus the AI team's bodies and the referee.
- AI takers walk from their lineup to the spot (`Footballer` gait at walking speed) while the
  camera holds on the lineup, then the whistle; they walk back like a human would.

### 7.4 Scored (5-second window)

Locomotion and the emote wheel (B) are enabled for the scorer only; the camera stays on them.
**The scorer, and only the scorer, may click to skip the window for everyone** (a
`CupRequest.SkipCelebration` in MP, immediate locally); small yellow "CLICK TO SKIP" text near
the bottom of the scorer's screen. At 5 s or on the skip, a 0.3 s cut puts the scorer back in the
lineup, and the goal replay follows with its own unanimous vote (2.1). Crowd:
`AudioManager.OnSetPieceGoal`, `CrowdCheer.Celebrate`. Same in every style, Co-op included.

### 7.5 Missed / saved (walk-back cinematic)

Shooter turns and walks toward their lineup slot at 1.6 m/s; the `CupCinematicCam` runs a
two-shot sequence: 1.5 s low tracking shot from beside the goal following the shooter's face,
then a cut to a wide shot from behind the lineup as they arrive. If they have not arrived at 3.5 s,
the cut to the next placement hides the snap. The keeper (if human) sees their own camera
throughout. Crowd: `PlayMissBoosMaybe`.

### 7.6 Losing miss (dejection trio)

The shooter's camera holds. One of three, rolled by the round RNG so peers agree:

1. **Knees + face in hands:** upright-locked sink (`EmoteHeightOffset` to a kneel), forearms to the
   face (Facepalm arm targets, both sides), head down 30 deg.
2. **Hands on hips, head down:** stand, upper arms back, forearms in, head pitch 35 deg.
3. **Arms on head, fall straight back:** 0.8 s arms-on-head pose, then balance off, upright lock
   off and a small backward nudge; gravity does the rest; the pose overrides hold the arms.

These are three new `Celebration.Emote` values appended at the END of the enum (wire ids), playable
on AI bodies (`Celebration` needs only `Init(ragdoll)`). Lineup cameras stay free within their
cone. 4 s, then the result screen. The AI lineup uses the same three when the humans win.

### 7.7 Winning kick (5-second free window)

Every human in the lineup gets locomotion and the wheel; the shooter too. The AI lineup plays the
dejection trio. `PlayGoalCelebration`, crowd celebrate. After 5 s, or when the scorer (or the
keeper who made the winning save) clicks to skip, a cut to the results. Winner by an AI miss: the
keeper gets the window and the skip.

### 7.8 Cinematic camera

`GameCamera.Broadcast` does not orbit on its own (it re-derives a fixed vantage every frame), so the
cup adds `CupCinematicCam`: a scripted shot list (position, look-at, fov, duration, ease) with a
user-orbit takeover for the podium; it covers the coin toss, the walk-back, the trophy lift and the
podium. It borrows the menu stage's discipline for `Time.timeScale` save/restore.

### 7.9 The shooter's cameras

- **Penalties: `CupPenaltyCam`, a copy of the FIFA penalty camera.** Behind the taker on the
  ball-to-goal line, close and low (3 m behind the ball, 1.5 m high *(tune)*), with the vertical
  FOV solved each frame from the distance and aspect so that, looking at the goal centre, the
  goal is big in the view and the posts sit at about 11% and 89% of the frame width: a little
  space left and right of each post, no more. **Aiming is unchanged:** the mouse looks left,
  right, up and down as normal and the look ray is the aim (`LookAimPoint`), with the look
  clamped so the goal never leaves the frame (yaw plus or minus 25 degrees, pitch -5 to +20).
  Charge, power and spin inputs are unchanged.
- **Free kicks:** the standard `Follow` camera and look-ray aim (`LookAimPoint`), the existing
  set-piece feel; the wall and the range need the yaw-driven aim.
- **Head to Head spectators** mirror the shooter's camera exactly (the stream carries the camera
  pose).
- **Lineup:** the cone in 7.3. **Keeper:** `KeeperFollow`, unchanged.

---

## 8. Endings

### 8.1 Podium (Solo, Head to Head)

- Built on the real pitch at the penalty spot after the Final's objects are cleared (the arena,
  crowd and stadium persist across the cup, see 9.5), so the crowd is the backdrop.
- **Pedestal:** `MeshGen.Lathe` stepped dais, 1.6 m across, 0.6 m high, one shared stone material
  plus a gold trim ring (`Make.Mat(0.85, 0.70, 0.30, smooth 0.85, metal 0.75)`, the Cosmetics gold).
- **Trophy:** lathe cup + two `Torus(arcDeg 200)` handles + a small plinth, combined, about 0.45 m,
  collider-less, parented to the LEFT forearm at local (0, -0.22, 0) (the `AddGlove` offset), the
  arm that celebration emotes leave alone. Freed through `GeneratedMeshOwner` like every piece.
- **Winner** on the dais in the nation kit. Default pose: new emote `TrophyLift` (both arms up)
  re-played whenever `Playing` drops. Emote wheel (B) with a curated page of standing emotes that
  do not drive the left arm (candidates: FistPump, Point, Salute, Wave, Bow, Cheer; verify each
  against `EmotePose` when building); physics emotes Backflip, KneeSlide, FishFlop, Moonwalk are
  excluded, as are the sinking ones.
- **Losers stand idle, no emotes, cheap:** display bodies (`BecomeDisplayBody` + `DisplayPose`,
  no solver cost) in a horseshoe around the dais facing it, each looking down in one of three
  static poses rolled by the seed: **hands on hips**, **hands on head**, **hands behind the back**.
  Head to Head = every other human still connected, plus AI bodies of the beaten finalist and
  semi-finalists to make at least three; Solo = the beaten finalist, both semi-finalists and the
  four quarter-finalists (seven AI bodies).
- **Camera:** slow orbit, radius 6 m, height 2.2 m, 0.08 rev/s; mouse drag takes over for 4 s;
  wheel zooms 3-10 m. Confetti: 200 Verlet quads in the nation's two kit colours from 8 m up
  (hand-rolled like `HairSim`; the project has no `ParticleSystem` yet).
- **Audio:** `PlayWhistleTriple` at the Final's end (with the referee's raise),
  `PlayGoalCelebration` on the podium, `Resources/Audio/fanfare` if present (`AudioManager.Clip`
  null-skips).
- **UI:** "CHAMPIONS - BRAZIL - Alice" title strip; bottom hint "B emotes - drag to orbit - Esc";
  after 3 s the buttons: Solo **New Cup** / **Main Menu**; Head to Head host **Play Again** /
  **End Match**, client **End Match**. Then the CUP SUMMARY table (6.6).
- **MP sync:** the podium is a cup phase; bodies spawn identically on every peer from the bracket;
  the winner's emotes ride the snapshot's `emoteId/emotePhase` (the cup driver fills them; the
  set-piece driver hardcodes 255). Losers are static, nothing to sync.

### 8.2 Trophy lift (Co-op win)

A 14 s scripted cinematic, then a free window:

1. Cut to the centre circle: the team jogs in from the lineup (gait), the AI nation walks off, the
   referee applauds from the spot.
2. The Captain is handed the trophy under a cut (parented as in 8.1), lifts it (`TrophyLift`),
   teammates around in `Cheer` / `HandsUp` / `FistPump` on staggered starts; confetti; fanfare;
   the crowd celebrates; the camera arcs from low front to a high slow orbit.
3. The free window: everyone can move and emote; the Captain keeps the trophy in hand.
4. Results screen (6.6, CHAMPIONS) on Continue.

---

## 9. Technical architecture

### 9.1 New code (all under `Assets/Scripts/Cup/`)

| File | Responsibility |
|---|---|
| `CupNations.cs` | The 214-row table (name, code, strength, novelty) + lookup + editor validation. |
| `SeededRng.cs` (Sim) | Shared seeded RNG. |
| `CupBracket.cs` | Pure data: entrants, rounds, stages; `Build(seed, humans)`, `NextRounds(stage)`, `Advance`, wire read/write. No Unity. |
| `CupSim.cs` | AI-vs-AI kick-by-kick simulation (2.8). |
| `CupRoundRules.cs` | Pure round logic: kick order, roles, early finish, sudden death, shooter cycling for Co-op, the coin outcome. Unit-testable. |
| `CupRound.cs` | The round driver (MonoBehaviour): bodies, referee, taker, keeper, wall, ball, verdicts, replays, whistle sequencing, choreography states. Runs as authority locally (Solo, Head to Head parallel rounds) or on the host (Head to Head human rounds, Co-op), and as a display client. |
| `CupBotTaker.cs` | `IStrikerInput` that runs a taker: delay 0.8-1.6 s, charge to a target meter, spin choice; strength from the ramp via `combinedOverride`. The existing `AutoLaunch` stays as the watchdog. |
| `CupReferee.cs` | The referee body, its kit, marks, `WhistleRaise`, the walk to and from the spot. |
| `CupCoinToss.cs` | The ceremony: marks, the call overlay for everyone present, the coin mesh and arc, the seeded result, the flash, the calls band. |
| `CupChoreo.cs` | Lineup marks and pose, walk-back, celebration windows and the scorer skip, dejection trio, win beat. |
| `CupPenaltyCam.cs` | The FIFA-style penalty camera (7.9). |
| `CupCinematicCam.cs` | Shot lists, podium orbit. |
| `CupDirector.cs` | The cup's phase machine per style; owns the bracket, the tick counter, the per-round root, `CupState` broadcasting (MP host), the barrier and gates. Lives for the whole cup under `_matchRoot`. |
| `CupPodium.cs`, `CupTrophyLift.cs` | Section 8. |
| `CupHud.cs`, `NationPickerUI.cs`, `CupBracketView.cs`, `CupLobbyUI.cs`, `CupOrderUI.cs`, `CupLoadingUI.cs`, `CupResultsUI.cs`, `CupSetupUI.cs` | Section 6. |
| `MenuScenes/CupScene.cs` | The grid vignette. |

### 9.2 Reuse map

Reused as-is: `SetPieceTaker` (charge / run-up / strike, `combinedOverride`, `LookAimPoint`, the
`aimPoint` delegate), `BallController.LaunchSetPiece` and `SetPieceShot`, the goal test,
`DefensiveWall` (`Build(root, ball, wallCenter, count)`, `TriggerJump`, `Clear`),
`SetPieceMap.RandomSpot`, `Goalkeeper` + `KeeperController` + `ResetHumanKeepers` idiom,
`SaveWatch` (with `allowEpic:false`), `ReplaySystem` + `Broadcast` replay camera + skip vote, the
idle/run-up watchdogs, `Celebration`/`EmotePose` and the wheel, `Hud` widgets, `SetupPanel`, `GoalEditor` (locked), `LobbyUI`, `SessionBrowserUI`,
`QuickChatFeed`, `CareerStats`, `Achievements`, `AtomicFileWriter`, `JerseyDesigns.Thumb`.

Changed: `GameMode` (+1), `PauseMenu.ModeName`, `NetSession.ModeWord/ModeLabel/LabelIsMode`
(style + format in the label), `NetSession.RoleForSlot` (mode-aware: "Entrant"), `MultiplayerHubUI
.NetModes` and `MenuUI.SoloModes` (+1), `MenuSceneStage.Create` (+1), `GameBootstrap` (routes,
`BuildCup`, closures), `Hud.KindOf` (four words), `Celebration.Emote` (+5 appended: `TrophyLift`,
`DejectKnees`, `DejectHips`, `DejectFall`, `WhistleRaise`), `JerseyDesigns` (+ `Referee` design),
`HostSetupUI` (branch), `PauseMatchSetup.RowsFor`, `PauseMenu` (overlay mode for MP),
`CareerStats` (+fields), `NetCodec.ProtocolVersion` (8).

Not reused: `NetSetPieceMatch` and `FreeKickGame` as drivers. Neither has a head-to-head round,
role swapping, sudden death for set pieces or a next-round path, and the first already interleaves
two modes across 1900 lines. `CupRound` is a new driver built from the same parts.

### 9.3 Net topology per style

- **Co-op:** fully host-simulated, the model the whole framework is built for. Human inputs to the
  host (`ConsumeInputForSlot`), snapshots back (`Snapshot` bodies keyed by `slot`; AI bodies and
  the referee use virtual slots 8..15, which the existing `n` + `slot` encoding allows; the cup's
  consumer maps them). `emoteId/emotePhase` filled.
- **Head to Head, parallel phase:** each human-vs-AI round is simulated on its owner's client (the
  Solo path with `Multiplayer.IsActive` true). The owner reports `CupRequest.RoundResult` (scores,
  kick line) to the host, which folds it into `CupState`. The owner also streams `CupStream` at
  20 Hz (camera pose, ball, up to 5 bodies as `BodyState`) unreliable to the host, which forwards
  it to the slots that pressed Spectate. Trust note: a client authors its own result; acceptable
  for a friends lobby, and the host validates shape (scores within the rules).
- **Head to Head, human rounds:** host-simulated like Co-op, one at a time; spectators get host
  snapshots plus the spectated participant's camera pose.
- **Solo:** no session; the same driver in local-authority mode.

### 9.4 Wire

- `MatchConfig` + `cupStyle` (u8) + `cupFormat` (u8), appended after `goalScaleH` and read with
  `r.More` (trailing, so no layout change). `fkSeed` is the cup seed.
- New `MsgType` values (append): `CupState` (H to C, reliable, host-only), `CupRequest` (C to H,
  reliable), `CupStream` (both ways, unreliable, relayed), `CupRoundState` (H to C, reliable,
  host-only: round index, kick, scores, outcomes bitfield, active/keeper body ids, phase + timer,
  sudden death, official coin call + result, celebration window state).
- `CupRequest` kinds: `PickNation`, `Ready`, `Spectate(slot)`, `Unspectate`, `RoundResult`,
  `Loaded`, `SetOrder` (Captain), `CallCoin(guess)` (from every caller), `SkipCelebration`; the
  replay skip stays on the existing `SkipVote`.
- `CupState` carries: phase, stage, per-slot nation (u16), per-slot status (alive / out / AI /
  ready / spectating slot), the played rounds' results (stage, index, scores, kick line), Co-op
  order (8 bytes), vote counts, and the per-slot coin-call tallies. About 280 bytes worst case,
  well under the 1200-byte single-packet ceiling; it is a separate event-driven message, never a
  roster field.
- Every host-authored type goes into `NetSession.IsHostOnly`. Six touchpoints per new type, as
  documented in the net report (enum, codec pair, `RouteMessage` case, host-only gate, broadcast
  method + event).
- `ProtocolVersion` 7 -> 8 when the cup ships (a new mode value changes what a lobby means).

### 9.5 Session and match lifecycle (the biggest structural change)

- One `_matchRoot` for the whole cup: arena, pitch, stadium, crowd, camera and the `NetPump` are
  built once and persist. Each round's transient objects (bodies, referee, wall, ball, coin, wall
  materials) live under a child "RoundRoot" the director destroys and rebuilds. No stadium
  flicker between rounds.
- Between rounds in MP: `NetSession.ClearSnapshotBuffer()` and `ResetSlotInput` for every slot
  (both exist for this), and the director's tick counter keeps increasing across rounds so the
  input reorder guard never swallows a fresh round's input.
- `MatchStarted` stays true from Start until End Match, including through Play Again: a cup is
  closed to late joiners once the nations are picked. Play Again = a `CupState` phase change back
  to NationPick with a new seed; no session teardown, no `Multiplayer.End`.
- The pause menu's Restart / Match Setup closures stay null in MP; End Match uses the existing
  `ReturnToMainMenu` path; a client's Quit uses `LeaveNetworkedMatch`.

### 9.6 Statics hygiene

Written at cup start (before the arena is built, since the goal frame is built at the current
size): regulation goal, `BallSpeedMul 1`, `StrikerMoveSpeed` base, `WallCount 4`, `WallDistance
9.15`, `PenaltyMode` per format, `AccuracyNoKeeper false`. Written per kick: `KeeperAbility` from
the ramp. Written for the cup and cleared in `OnDestroy`: the two shooting overrides,
`KeeperAbility`, `HumanKeeperSpeedMul` (never set by the cup, but restored defensively).

### 9.7 Career and achievements

`ModeStats` gains `CupsEntered`, `CupsWon`, `CupBestStage`, `CupRoundsWon`, `CupRoundsLost`,
`CupKicksScored`, `CupKicksTaken`, `CupSaves`, `CupConceded`, `CupCoinCallsMade`,
`CupCoinCallsRight`, and a `List<NationCups>` of (nation, entered, won) rows (`JsonUtility` cannot
serialise a dictionary; lists load empty on old saves, no migration). `CareerStatsUI` gains a
`Cat.Cup` page. Achievements (the empty `All` array): "Champion" (win a cup), "Giant Killer" (beat
a nation 30+ strength above yours), "Clean Sheet" (win a round conceding none), "Cold Blooded"
(win a sudden-death round), "Team Player" (win Co-op), "Pundit" (25 correct coin calls).

---

## 10. Edge cases

- **Leaving Solo** ends the cup (no save, no resume); the confirm card says so.
- **Leaver mid-round (Head to Head), others still in:** their side becomes AI for the rest of
  that round; later rounds of that nation are simulated. Their row reads "(AI)". Never a walkover.
- **Leaver in Co-op:** dropped from the order, the slot count drops by one; a leaving keeper is
  replaced by the lowest-ordered shooter until the next order screen prompts the Captain.
- **Host leaves:** the session ends for everyone (unchanged).
- **No humans left alive (Head to Head):** the director simulates the remaining stages and shows
  the podium with the AI champion; the remaining humans stand around it. Play Again still works.
- **Idle taker:** the 12-second watchdog; an idle keeper just stands. **Idle official caller:**
  5 s, heads.
- **Slow loader:** the loading barrier times out at 10 s and the toss starts without the ack; the
  late client joins from `CupRoundState` + snapshots.
- **Spectate target finishes:** the spectator is returned to the lobby with them.
- **Same nation picked twice (Head to Head):** the host refuses the second `PickNation`; the
  picker shows "taken".
- **No majority in Co-op:** the Captain decides (5.2).
- **Free kick sudden death past the schedule:** spots are generated lazily from the round RNG.
- **The referee** is cosmetic: ball collisions ignored, never a snapshot player, never in a lineup.
- **Nation table drift:** a table name that no longer matches a design is logged in the editor and
  skipped from the pool at runtime.

---

## 11. Build order and verification

Each phase compiles clean (`bash docs/compile-check.sh`) and is checked in the editor through Unity
MCP (play mode, reflection navigation, `ScreenCapture`) before the next; MP paths on
`LocalTransport` loopback first, then two machines.

1. **Foundations.** `GameMode` value, `SeededRng`, `CupNations` (codes, strengths, novelty),
   `CupBracket`, `CupSim`, `CupRoundRules`. An editor test builds 1000 brackets and asserts: 32
   distinct nations, no two humans in one Round of 32 match, every round resolves, sudden death
   terminates, early finish is correct.
2. **The round, single player.** `CupRound` in local-authority mode with `CupBotTaker`, both
   formats, role swap with two bodies, `CupPenaltyCam`, `CupHud`, kick clock, replays, verdicts. A dev entry that plays one round at a chosen stage.
3. **Solo flow.** Fork screen, `NationPickerUI`, `CupBracketView`, loading card, `CupLobbyUI` (one
   row), `CupDirector` (Solo), KNOCKED OUT, career stats.
4. **Referee, coin toss and choreography.** `CupReferee` + kit + whistle sequencing,
   `CupCoinToss` with everyone's calls, lineup pose and cone, walk-back, scored window + scorer
   skip, dejection trio, win beat, `CupCinematicCam`. Verified in Solo first (a 1v1 lineup).
5. **Podium.** Pedestal, trophy, `TrophyLift`, curated wheel page, static loser poses, orbit,
   confetti, audio, summary screen.
6. **Head to Head.** Wire (`CupState`, `CupRequest`, `CupStream`, `CupRoundState`, v8), host
   setup branch and browser tag, lobby role label, the nation strip, parallel local rounds
   reporting results, the Cup lobby with live rows, Spectate relay with the camera pose, coin
   calls counted, the pause overlay, host-simulated human rounds with the interstitial, ready
   gate, Play Again / End Match, leaver handling.
7. **Co-op.** Vote counters, majority gate and CAPTAIN DECIDES, `CupOrderUI` (drag-and-drop +
   slot machine), shooter cycling, keeper rules, full lineup with AI mirror, the calls band after
   the flip, GAME OVER results with tabs, trophy lift.
8. **Polish.** `CupScene` vignette, achievements, tuning pass on the ramp, the penalty camera and
   timings, free-kick band.

---

## 12. Decisions and open questions

### 12.1 Decisions log (user review, 2026-09-03)

1. Solo is standardised like the MP styles (2.6).
2. No human keeper handicap; the AI taker ramp is the difficulty (2.3).
3. Co-op vote with no majority: the Captain decides (5.2).
4. Every scored kick gives the scorer a 5 s free window, and the winning kick frees the lineup for
   5 s; only the scorer (or winning keeper) can click to skip, and the skip applies to everyone.
5. Podium losers stand idle in one of three looking-down poses (hands on hips / head / behind the
   back), no emotes, as display bodies.
6. Field is always 32. The shooter's camera is a FIFA-style penalty camera; Head to Head
   spectators share it; the Co-op lineup is fixed close to the box with a look cone (7.9, 7.3).
7. A referee in black-and-white stripes in every round, raising a hand to his mouth before every
   whistle; a coin-toss ceremony with a HEADS / TAILS call decides who kicks first (7.1, 6.11).
8. Free-kick band 17-28 m, the spot changing once both sides have shot (2.1).
9. Coin-call predictions, tracked in career stats, shown as a side band with checks and Xs (6.11,
   6.12, 9.7).
10. No re-entry. Leaving Solo ends the cup; leaving Co-op drops a slot; leaving Head to Head with
    others still in hands the nation to AI (section 10).
11. The ramp adds nothing to a human-vs-human round; intended.
12. The nation strength table is authored in code without review.
13. Coin caller when two humans meet: a seeded random side.
14. Call it is the coin: everyone present calls HEADS or TAILS before the flip, and there is no
    calling once the shot stage begins. Head to Head: counted only, nothing shown. Co-op: the calls
    band with checks and Xs right after the flip.
15. Skips: the scorer's click skips the celebration before the goal replay, shown as small yellow
    "CLICK TO SKIP" text near the bottom of the screen; the replay's own skip is a unanimous vote
    among the humans in the round, shown as "0/X" then "Y/X".
16. Coin call timeout 5 s, then heads.
17. The penalty camera copies FIFA: behind the taker, the goal big with a little space beside each
    post, normal look-around aiming. No reticle.
18. Pausing freezes the game only in Solo; in multiplayer the pause menu is an overlay.
19. Replay-skip voters are every human with a body in the round; spectators never vote.
20. In multiplayer, Esc frees the cursor and cuts the local player's input while the overlay is
    up; the kick clock keeps running.
21. The kick clock is 30 s (raised from the designed 12 s on the owner's call, 2026-09-04), the
    last 5 s depleting the meter frame, then the existing weak auto-shot. There is deliberately
    NO clock dial and no countdown number: the frame is the only tell.
22. Terminology: a **round** is one match between two nations (never "tie"); the five bracket
    levels are **stages** (Round of 32, Round of 16, Quarter-finals, Semi-finals, Final).
23. A free kick stopped by the wall reads SAVED, not "blocked".
24. Nation strength is never displayed (no stars, no sort); it still flavours the simulated
    results.

### 12.2 Reading to confirm

Nothing is blocked; one reading of the last answers is baked in and easy to flip:

1. **"Call it" is the coin itself:** every human present predicts HEADS or TAILS, the official
   caller's pick decides kick-off, the rest count toward career stats (and the Co-op band). Read
   from "only before the coin flip, by everyone in the lobby".
