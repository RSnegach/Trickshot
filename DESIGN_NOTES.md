# Trickshot: Replayability Brainstorm

Status: **early brainstorm, not a spec.** Core scrimmage gameplay (AI, tackles, keeper, shot UI)
needs to be solid first. Capturing ideas here so they aren't lost, not committing to them yet.

## Problem

Online modes need a reason to come back beyond "play scrimmage with friends again." Need
asymmetric/short-session modes and a progression layer, not just the main match mode.

## Ideas so far

- **Matchmade 1v1: keeper vs. striker.** One player wants to play keeper, the other wants to
  take free kicks/pens. Matchmaking pairs them by role preference instead of both needing to
  agree on the same mode. Reuses Set Pieces' shootout structure (`ScrimmageGame` / net driver
  already has keeper-ability and goal-size knobs — see `MULTIPLAYER.md`), pointed at a queue
  instead of a private lobby.

- **Practice vs. Ranked split.** Practice: unlimited attempts, no leaderboard. Ranked: capped at
  X attempts/day, only those count toward the leaderboard. Keeps the leaderboard meaningful
  (can't just grind runs) while still letting people warm up freely.

- **Steam achievements.** Standard hooks once Steamworks is wired (see `MULTIPLAYER.md` Steam
  section) — milestone streaks, clean sheets, goals scored, etc.

- **Career stats, Rocket League-style.** Persistent per-player stats across sessions/modes
  (goals, save %, streak PBs, matches played) rather than only a single match's scoreboard.
  Implies some form of save-file or account-tied persistence that doesn't exist yet.

- **Face paint / face tattoo: unlockable "draw on your face" mode.** The old fixed Face Tattoo
  accessory (three cheek-line boxes) was removed from the catalog; this is the idea it pointed
  at. An unlockable cosmetic mode that lets the player PAINT on the character's face directly -
  the same flow as the jersey painter (a brush over a face UV region), saved into the player's
  appearance and synced to peers like the jersey texture is. Unlock gate not decided yet
  (achievement, level, currency — needs the same unresolved persistence question as Career
  Stats). Needs face-UV groundwork on the head mesh that doesn't exist yet. Not scoped beyond
  this — just an idea, not being built yet.

- **"Zoo": custom-animal creation + social/multiplayer viewing.** A character-creation interface
  where players build/customize their own animal (builds on the existing `Species`/`AnatomySim`
  system — human, horse, elephant, etc. already have anatomy scaling). Play with your own
  creation, play with other people's in multiplayer, browse/visit other players' zoos. Creation
  should be LIMITED somehow (a cap on slots, a cooldown, a currency/unlock cost — not decided) so
  making a character feels like a real choice, not something to spam-generate dozens of. Not
  scoped beyond this — just an idea, not being built yet.

- **Scrimmage: team jersey design + vote in the prematch.** Right now every player on a
  scrimmage team wears the same shared team torso material — nobody actually picks the kit.
  Idea: a prematch screen (or a couple of screens) where any human on a team can submit a jersey
  design (reusing the existing jersey-painting flow), and every human on that team votes on which
  submitted design the whole team wears that match. Needs to be robust (works with 1 human on a
  side, works with nobody submitting anything, works with a tie), visually clean, and feel like a
  native step in the prematch flow rather than a bolted-on extra screen. Not scoped beyond this —
  just an idea, not being built yet.

- **MP "Challenges" mode: a menu of solo skill challenges with leaderboards and badges.**
  Supersedes the earlier standalone Goalkeeper Streak mode idea — that's now just one challenge
  among several here, not its own mode. A new top-level MP mode (sibling to Match's
  Friendlies/Online) holding a set of challenge types: goalkeeper (longest streak of consecutive
  saves without conceding, shots get incrementally harder as the streak grows — more pace,
  tighter corners, less telegraph, an escalating-difficulty curve rather than fixed AI tiers;
  needs a shot generator that can dial difficulty continuously, not just pick from
  {Easy..Insane}), free kick (separate playlists by distance, most consecutive scored from a
  given distance), accuracy, crossbar challenge, headers only, bicycles, etc. — open-ended list,
  more can be added later.
  Each challenge has its own **All-Time / Daily / Weekly leaderboard** across all players (global,
  not per-friend-group), and finishing at the top of a leaderboard earns a **badge** displayed on
  the player's card. Not scoped beyond this — just an idea, not being built yet. Depends on the
  same unresolved persistence/backend question as the Career Stats idea above (global
  cross-player leaderboards need a real backend, not just a local save file — Steam Leaderboards
  API could cover this once Steamworks is actually wired, see `MULTIPLAYER.md`), plus a
  "player card" display concept that doesn't exist in the game yet.

- **Match-mode AI shooters, deliberately much worse at scoring than humans.** Goal: the majority
  of goals in a Match should come from human players, not AI teammates/opponents filling out the
  roster. Right now AI shot aim uses the same model regardless of who's on the pitch (see commit
  `45bb047`, "Fix the AI shot aim: it was arithmetically incapable of missing" — i.e. AI shooting
  is already tuned to be COMPETENT, the opposite direction from this idea). Would need a
  Match-specific (not Striker/SetPieces/Accuracy - those modes are ABOUT AI shot quality) aim
  penalty for AI-controlled bodies specifically, tuned so AI still looks like it's trying (doesn't
  read as broken/comedic) but rarely actually finishes. Not scoped beyond this — just an idea, not
  being built yet. Open question: does this apply to ALL AI shots in Match (AI keeper saves are
  presumably fine to keep sharp — a keeper that can't save isn't the same ask as a striker that
  can't score) or just AI outfield shooters specifically?

- **Match-mode free kicks: choose shoot vs. deliver a cross.** Match has no dedicated Crosser
  role today (`NetSession.SlotAllowed` — the crosser slot is "a claimable human role in Striker
  only," by deliberate decision, not because other modes lack a body for it). Idea: on a Match
  free kick specifically (not open play), let the taker choose between a direct shot at goal OR
  delivering a cross into the box, reusing Striker's existing crossing mechanics as-is (`Crosser`/
  `AimReticle`/launch-point aiming) rather than inventing a second crossing input scheme. Doesn't
  touch Set Pieces (a separate mode, dead-ball shootout structure, not "Match with a choice").
  Not scoped beyond this — just an idea, not being built yet. Open question: does choosing "cross"
  hand control to a teammate to receive/finish it (mirroring Striker's separate crosser+finisher
  split) or does the taker's own aim just place the ball for whoever's in the box, AI or human?

- **Match playlists: 3v3 / 5v5 / max 8v8, not 11v11 — with real per-size formations.** Replaces
  11v11 with an 8v8 cap. Default formations: 3v3 = 1:1 (one defender, one attacker, plus keeper),
  5v5 = 2:2, 8v8 = 3:3:1. Always at least one defender and one attacker regardless of size. This
  is the fix for a real bug a capacity-planning workflow found on 2026-08-31: `NetSession`'s
  8-slot wire board hard-clamps `ScrimSlotsPerTeam` to 4 per team, so today's "5v5"/"11v11" Online
  playlists both silently simulate 4v4 — the labels are cosmetic right now. An 8v8 cap fits
  exactly within the existing 8-slot-per-team ceiling (`MaxSlots` would need to become 16, or
  `ScrimSlotsPerTeam` become 8 — a wire-format change, every `byte`-sized slot field and
  `MaxSlots`-sized array in `NetSession`/`NetMessages` would need re-checking, not just a UI
  relabel). User explicitly flagged: AI brains for all positions need a significant upgrade to
  support this — today's positional AI wasn't designed with formation-aware roles beyond what
  3-4-a-side already asks of it. Not scoped beyond this — just an idea, not being built yet.

## Open questions (not answered yet)

- Where does progression/stats persistence live — local save file, or does it need an account
  system? Steam stats/leaderboards API could cover a chunk of this for free once Steam is wired.
  Not yet decided.
- Shot-difficulty curve for the goalkeeper streak challenge is not designed. Needs its own tuning
  pass once the regular AI shot-aim model (see commit `45bb047`) has a difficulty axis to build
  the curve from.
- Matchmaking (finding a stranger by role) is a different problem from the current lobby model
  (`NetSession`, direct-IP/Tailscale, Steam P2P stub) — those assume you already know who you're
  playing with. A real matchmaking queue is new infrastructure, not covered by anything in
  `MULTIPLAYER.md` today.
