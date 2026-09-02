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

- **Adult-mode "Touch Tips" achievement ladder.** Unlocks when your erect third leg (the ThirdLeg
  bind, `AnatomySim` hitbox) touches another player's: **Touch Tips** the first time, **Serial Tip
  Toucher** at 10, **Tip Terrorizer** at 100. Needs a member-vs-member contact detector (both
  hitboxes are compound colliders on the pelvis bodies, so an OnCollision on either side can see
  the other's `AnatomySim.IsHitbox`), a lifetime counter on `CareerStats`, and three
  `Achievements.All` entries in the existing StatThreshold shape. Not scoped beyond this — just an
  idea, not being built yet.

- **Sniper role (with a buried in-game reference).** Somewhere in the game there should be a
  buried, easter-egg reference to a sniper role. The role itself: random people can request to join
  (or just join public lobbies) that advertise they are looking for a sniper. The sniper sits
  somewhere around the stadium with a functional sniper rifle and shoots at players and the ball
  over the course of a match or kickabout to impact it somehow (knockdowns, deflections - the
  effect is undecided). Lobby-side it is a join-request / "looking for sniper" flag on the lobby
  (see `MULTIPLAYER.md` for what the lobby advertises today); play-side it is a new seat with its
  own camera and input, not one of the existing roles. To be fleshed out later — just an idea, not
  being built yet.

- **Non-playing seats: camera operators and referees (besides the sniper).** Two more ways to join
  a match without kicking a ball, both new `NetRole`s on top of the existing slot-less spectator
  (`NetRole.Spectator`), each with its own camera and input rather than a body.
  - **Camera operator.** Joins to film: a free camera plus the framed shot types the replay orbit
    already has (`GameCamera.BroadcastUpdate`), and their feed is what the replay / a broadcast view
    shows. Natural feeder for Trickshot Studio (below): an operator's live camera track is a clip.
  - **Referee.** Calls fouls (tackles and knockdowns already resolve through `MatchGame` /
    `Knockdown`, so a call is a flag on a contact the sim saw), gives cards — a card is a TIMEOUT
    (a timed spell off the pitch, sin-bin style) rather than a sending-off — and appoints free kicks
    and penalties by placing the spot on a version of the cross map (`CrossMap`'s placement UI,
    re-skinned for a set-piece spot; Match already has free kicks to hand the spot to). EVERY
    decision goes to a lobby-wide vote: every player votes uphold / overturn, and the roster's
    jersey vote (`LobbySlot.nominated` / `voteFor`, every peer deriving the same result off its own
    roster) is the shape to copy for the ballot. Lose three votes in a row and the ref is EJECTED;
    the match carries on to full time with no ref, as it does today. The incentive is the point:
    call it straight and the lobby backs you, call it badly and you're out — or the lobby gangs up
    on a player through the ref's calls, which is also fine. Open: how a ref sees the play (an
    operator-style free camera, or the broadcast orbit), whether votes are timed, and what a ref
    does during a vote.
  - **VAR (later).** A review the ref runs off the replay recorder (`ReplaySystem`, scrub + the orbit
    camera) before confirming a call, with every player spectating the review as it happens — the
    same clip on every screen, since each peer already records its own replay window.
  Not scoped beyond this — just an idea, not being built yet.

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

- **Trickshot Studio: cinematic replays + a scene builder + a social feed (potential headline
  feature).** The pitch: players recreate real goals — from history or from last weekend — as
  playable scenes with customizable characters, render them cinematically, post them, and the
  community votes and follows the best creators. Nobody has shipped this in a game format; if it
  lands it is a reason to open the game that has nothing to do with winning a match. Three pillars,
  each buildable on something that already exists:

  - **Cinematic replay mode.** Today's `ReplaySystem` is a rolling ring of raw bone/ball poses
    sampled every FixedUpdate and written straight back onto the transforms in slow-mo, with the
    camera switched to `GameCamera.Mode.Broadcast`. Studio needs that made first-class: save a clip
    (not just the last few seconds of a match), scrub it, and author camera moves over it — a
    small set of shot types (broadcast, ball-cam, orbit, dolly, player-follow, drone) with keyframed
    cuts and speed ramps, plus a free camera. Replays must be POSE recordings, not input
    recordings: the active ragdoll is PhysX and not deterministic across machines, so "replay the
    inputs" would drift; poses are what plays back identically everywhere. Cost is size — a bone set
    at 50 Hz is tens of KB per second raw, so clips want quantized poses + delta compression (the
    `BodyState` snapshot already quantizes for the wire; same idea, denser).
  - **Scene builder.** Place characters around the pitch (the existing `PlayerAppearance` /
    `Cosmetics` / `SkillTree` body builder and the jersey painter are the character customizer
    already — real kits of real teams are user-painted jerseys), give each one a timeline of
    ACTIONS with a time and a target — run to X, receive, pass to Y (ground/lofted/chip), dribble
    along a path, shoot at a spot, header, dive, celebrate — and chain them so one body's action
    triggers the next's ("when the pass arrives, B shoots"). The natural render path is the
    networked PUPPET path, not the live physics: `ActiveRagdoll.DisplayAnim`/`DisplayEmote` (the
    canned `AnimState` set + `Celebration` emotes) on kinematic bodies, and the ball flown by
    `BallController.LaunchTo`/`KickTo`, which already solve a ball onto a target point at a chosen
    time. That makes a scene deterministic, fast to preview, and the same on every machine — the
    exact property a shareable, votable artefact needs. Physics-driven "acting" (a real ragdoll
    header) can come later as an opt-in per action. Authoring is a timeline UI: scrub, drop
    actions, drag their times, snap chains; the goal-line/kick-off/set-piece furniture from the
    match modes gives the scene its context. Import a real goal's shape by tracing it: place the
    players where they were in the footage, then set the times.
  - **Social layer.** Posts (a scene = its script + a rendered clip + the camera track), user
    pages, follow, upvote/downvote, a feed (following / hot / new / by competition or week), and
    search with filters (team, competition, season, scorer, "recreation of <real goal>", tags,
    creator). The incentive loop is votes + follows; the side-by-side view is the killer feature
    for voting: the creator links the real goal (a public video URL, embedded or opened alongside —
    the game never hosts copyrighted footage) and voters watch the recreation next to it. Scenes
    should be posted as SCRIPTS (small, remixable, rendered on the viewer's machine with their own
    quality settings) with a rendered clip as the thumbnail/preview; "remix this scene" is free
    content. Two paths for the backend: (a) **Steam Workshop** gives upload, subscriptions, vote up/
    down, tags, search, creator pages and moderation for free once Steam is wired (`MULTIPLAYER.md`
    Steam section) — the cheapest way to prove the loop; (b) a **first-party service** (posts,
    users, feed ranking, comments, reports) is needed for anything Workshop cannot do — a proper
    feed algorithm, cross-platform, comments threads, and the side-by-side view — and it is the same
    unresolved persistence/account question the Career Stats and Zoo ideas hit (see Open questions).
    A staged plan: Workshop first, first-party later, with the scene format versioned from day one
    so nothing posted early is orphaned.

  Open points to flesh out: the scene file format (versioned, forward-compatible, small); the
  action vocabulary and how far it leans on the AI's own skills (`Footballer` brains could "act"
  a role between authored keyframes); rendering clips to video in-engine (a frame recorder +
  encoder, or offer OBS/Steam's own capture and only host scripts); moderation and takedowns
  (user-painted jerseys and player likenesses are UGC); whether votes need Steam identity or an
  account; and how much of the studio should be usable from a replay of the player's OWN goal
  ("save this, make it cinematic, post it") since that is the on-ramp that needs zero authoring.
  Not scoped beyond this — just an idea, not being built yet, but the one with the biggest upside.

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
- Trickshot Studio's social layer (posts, feed, votes, follows, search) is the first idea that
  needs a real user-content service rather than a save file or Steam stats. Steam Workshop covers
  the first version; anything beyond it is the same account/backend decision as above, so the two
  should be decided together.
