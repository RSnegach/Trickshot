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

- **Standalone Goalkeeper Streak mode.** Solo (or leaderboard-driven) mode: longest streak of
  consecutive saves without conceding. Shots get incrementally harder as the streak grows (more
  pace, tighter corners, less telegraph) — an escalating-difficulty curve rather than fixed AI
  tiers. Needs: a shot generator that can dial difficulty continuously, not just pick from
  {Easy..Insane}.

- **Practice vs. Ranked split.** Practice: unlimited attempts, no leaderboard. Ranked: capped at
  X attempts/day, only those count toward the leaderboard. Keeps the leaderboard meaningful
  (can't just grind runs) while still letting people warm up freely.

- **Steam achievements.** Standard hooks once Steamworks is wired (see `MULTIPLAYER.md` Steam
  section) — milestone streaks, clean sheets, goals scored, etc.

- **Career stats, Rocket League-style.** Persistent per-player stats across sessions/modes
  (goals, save %, streak PBs, matches played) rather than only a single match's scoreboard.
  Implies some form of save-file or account-tied persistence that doesn't exist yet.

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

## Open questions (not answered yet)

- Where does progression/stats persistence live — local save file, or does it need an account
  system? Steam stats/leaderboards API could cover a chunk of this for free once Steam is wired.
  Not yet decided.
- Shot-difficulty curve for the streak mode is not designed. Needs its own tuning pass once the
  regular AI shot-aim model (see commit `45bb047`) has a difficulty axis to build the curve from.
- Matchmaking (finding a stranger by role) is a different problem from the current lobby model
  (`NetSession`, direct-IP/Tailscale, Steam P2P stub) — those assume you already know who you're
  playing with. A real matchmaking queue is new infrastructure, not covered by anything in
  `MULTIPLAYER.md` today.
