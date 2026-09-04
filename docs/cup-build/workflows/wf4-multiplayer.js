export const meta = {
  name: 'cup-multiplayer',
  description: 'Trickshot Cup phase 4: wire + session, then Head to Head and Co-op in parallel, then MP integration review',
  phases: [
    { title: 'Wire', detail: 'CupState/CupRequest/CupStream/CupRoundState, session plumbing, relay, host/client round authority' },
    { title: 'Styles', detail: 'Head to Head | Co-op (parallel partials)' },
    { title: 'MP integrate', detail: 'seams, lifecycle, leavers, Play Again' },
  ],
}

const NOTES = 'C:/Users/evrik/downloads/Trickshot/Trickshot/docs/cup-build/cup-build-notes.md'
const DOC = 'docs/trickshot-cup-design.md'
const REPO = 'C:/Users/evrik/downloads/Trickshot/Trickshot'

const REPORT = {
  type: 'object',
  required: ['summary', 'filesCreated', 'filesEdited', 'compileClean', 'verification', 'publicApi', 'notesForNextAgents', 'openIssues'],
  properties: {
    summary: { type: 'string' },
    filesCreated: { type: 'array', items: { type: 'string' } },
    filesEdited: { type: 'array', items: { type: 'string' } },
    compileClean: { type: 'boolean' },
    verification: { type: 'string' },
    publicApi: { type: 'string' },
    notesForNextAgents: { type: 'string' },
    openIssues: { type: 'array', items: { type: 'string' } },
  },
}

const p = args || {}
const FILES = (p.reportFiles || []).join('\n  ')
const API = `
=== WHAT IS ALREADY IN THE TREE (Assets/Scripts/Cup/, compiles clean; Solo plays end to end) ===
Earlier phases wrote detailed reports as JSON files (each has a "result" object whose entries carry publicApi, notesForNextAgents, openIssues, filesCreated/filesEdited). READ EVERY ONE fully before writing code, then read the actual files:
  ${FILES}
Naming facts: the round DRIVER is \`CupRoundDriver\` (partial MonoBehaviour, with Local/Host/Client authority seams and BuildState/ApplyState); \`CupRound\` is the pure round record (CupBracket.cs); \`RoundLine\` the kick-sequence state; \`RoundPhase\`/\`RoundAuthority\` in CupRoundState.cs; \`CupDirector\` is a partial class (CupDirector.cs + .Solo.cs/.HeadToHead.cs/.Coop.cs) with the read model, intents (local when IsAuthority else the RequestRaised(kind, arg, payload) event), Apply* appliers for remote slots, CupRequestKind enum, StartRound/EndRound/NewRoundRoot/AuthorityFor/CoinCallerFor; \`CupDirector.Instance\`; screens via director.AddGuiHook. Cameras \`CupCameraRig\` (CamPos/CamRot/CamFov, MirrorView); HUD \`CupHud\`; choreography \`CupChoreo\`/\`CupCoinToss\`; podium \`CupPodium\`/\`CupTrophyLift\`; screens NationPickerUI, CupBracketView/Screen, CupLobbyUI, CupResultsUI, CupKnockedOutUI, CupLoadingUI, CupIntroCard. PauseMenu.Overlay is true in networked styles (Frozen never true), PauseMenu.Paused gates local input only. NetSession already seats 8 entrants in the cup, MatchConfig carries cupStyle/cupFormat, ProtocolVersion is 8. CupText strings, CupTuning numbers, CupSalts salts.
=== END ===`

const P3SEAMS = `
=== PHASE 3 SEAMS THE MULTIPLAYER BUILD MUST HONOUR (from reports/phase3-endings.json - read the whole report, these are the load-bearing bits) ===
- Podium (CupPodium, agent G): HEAD TO HEAD enters it exactly as Solo does at the Final: RecordResult, EndRound (the podium spawns its OWN bodies from the bracket; the round's are gone), SimulateAiRounds, CupCareer.Won, SetPhase(Podium); in the Podium entry call TryBeginPodium() (fallback: PlayFanfare + SetPhase(Results)); close it with EndPodium() from the style's own CloseScreens (Solo's CloseSoloScreens does). No humans alive: SimulateRest to the end first (Bracket.Champion must be set; Begin refuses with -1 otherwise). The podium's buttons already branch on Style / IsAuthority (host Play Again / Continue / End Match; client End Match + "waiting for the host"); Continue is ContinueFromResults (Podium -> Results, CupResultsUI.Summary). Podium bodies are spawned identically on every peer from bracket + players, so ONLY the champion's emote crosses the wire: CupPodium.Bodies (winner first; CupBody.VirtualSlot = the slot for a human, AiBodyIdBase up for AI), WinnerEmoteId / WinnerEmotePhase for the snapshot, PlayWinnerEmote(e) for a remote champion's pick (only the curated page is accepted). A client puppet posed through ActiveRagdoll.DisplayEmote DROPS the trophy arm during Wave / Salute / Point / Thinker (no additive hold on the puppet path): build the client's winner as a live body the way the podium does, or add the left-arm hold (CupPodium.HoldUpperArmL / HoldForearmL) to the puppet path.
- Trophy lift (CupTrophyLift, Co-op only): at Driver.Phase == Over of a WON Final: RecordResult(CurrentRound, Driver.Line); RecordLocalRoundCareer; SimulateAiRounds; CupCareer.Won; SetPhase(TrophyLift); in the TrophyLift ENTRY call BeginTrophyLift() and DO NOT call EndRound before it (the lift runs on the round's bodies; EndRound ends a live lift, so a stray EndRound just cancels the cinematic). Its Continue ends the lift, ends the round and calls ContinueFromResults (-> CupResultsUI.Champions). Lost Finals never see it. The lift moves bodies only where the round is simulated (Host): the host must keep publishing the round's body snapshots (positions + emoteId / emotePhase, the referee's Clap included) past Driver.Phase == Over for the whole 14 s cinematic + free window, and HostTick's FeedRemoteInputs must keep running at Over so a remote human's Striker moves in the free window (today the driver does not feed remote inputs at Over).
- Free kicks (agent K): FREE KICKS have NO lineup and no walk-in / walk-back. CupBody.LineupMark / LineupFacing are the per-kick scatter marks there (rewritten by AssignFreeKickMarks at every Placing and the Intro, AssignFreeKickIdleMarks at Configure); RoundPhase.WalkBack DOUBLES as the 3 s miss-dejection beat in that format (a client-side reader naming phases must not call it a walk-back). The kick clock is zeroed on the Armed -> Live edge (SetKickClock(0)), so KickClockRemaining reaches clients as 0 from the Live state; CupHud's ring keys off Phase == Armed && 0 < remaining <= 5 and needs nothing else. The rest / 6 s verdicts wait while the ball is still \`approaching\` (CupTuning.LiveApproachSpeed) under a 20 s hard cap (LiveHardCap). The clock, the watchdog and the verdict run ONLY on the authority; clients mirror them through CupRoundState.
- SetPieceTaker.Begin is wrapped in a ChargeGate (one-call MaskHeld) so a Space already held at the whistle charges from the whistle: keep it on every path that arms a human taker, including the host arming a REMOTE human from NetInputSource.
- The podium hint / free-window hint strings are consts on CupPodium / CupTrophyLift, not CupText; leave them where they are.
- The menu vignette (Assets/Scripts/Play/MenuScenes/CupScene.cs) is finished and out of scope: do not touch it.
=== END ===`

const COMMON = `
You are one builder in a multi-agent implementation of the "Trickshot Cup" mode for the Unity game at ${REPO}.
FIRST read, in full: (1) ${NOTES}, (2) ${REPO}/${DOC} (sections 4, 5, 6, 9, 10 matter most now), (3) ${REPO}/CLAUDE.md, (4) ${REPO}/MULTIPLAYER.md, (5) every file under ${REPO}/Assets/Scripts/Cup/ and the net files you touch.
${API}
${P3SEAMS}
Rules: C# 9 / Unity 6, \`namespace Trickshot\` (+ \`using Trickshot.Net;\`); compile with \`bash docs/compile-check.sh\` after every file and before returning (exit=0); no Unity editor / MCP / graphify; CRLF-normalise every .cs you touch (\`sed -i 's/\\r$//; s/$/\\r/' <file>\`); never say "tie"; production quality, commented, exhaustive; host-authoritative for everything the doc says is host-authoritative; every host-authored MsgType goes into NetSession.IsHostOnly; nothing seed-derivable crosses the wire. Edit only your owned files (small listed seam edits allowed). Do not commit. Return the structured report.`

phase('Wire')
const h = await agent(`${COMMON}

YOUR TASK (agent H, "Wire and session"): implement design doc 9.3, 9.4, 9.5 and the host/client sides of the round. You own edits to Assets/Scripts/Net/NetMessages.cs, NetSession.cs, Multiplayer.cs (if needed), Assets/Scripts/Cup/CupNet.cs (NEW: codecs + helpers for the cup messages, the CupStream relay client/server helpers, the spectate mirror), Assets/Scripts/Cup/CupRound.cs (finish HostTick / ClientTick / BuildState / ApplyState / snapshot publish+apply for the round's bodies incl. emoteId/emotePhase, replay skip vote via the existing SkipVote plumbing with the unanimous rule), and Assets/Scripts/Cup/CupDirector.cs (the shared MP plumbing: broadcasting CupState on every state change on the host, applying it on clients, request routing, the Loaded barrier with a 10 s timeout, Play Again keeping MatchStarted true, End Match, tick monotonic across rounds, ClearSnapshotBuffer + ResetSlotInput between rounds). Do NOT write the Head to Head or Co-op flow bodies (two agents do that next in the partial files) - but give them everything they need:
- MsgType: append CupState, CupRequest, CupStream, CupRoundState. Codecs in NetCodec (or CupNet): CupState = phase u8, stage u8, style u8, format u8, seed u32, per-slot: nation (2 x u8 or u32), status flags u8 (alive/out/ai/ready/playing), spectating slot u8, live opponent nation, live scores (u8 x2), live kick u8, coin call/right, coin tallies (u8 x2); then the Co-op order (8 x u8), vote counts (as slot->nation, derived), and the bracket results as CupBracket.ToBytes() via NetWriter.Bytes (host authoritative; clients rebuild the bracket from the seed + results: prefer sending only played rounds' results if ToBytes is large - measure: keep the whole message under ~1100 bytes, and if the full bracket does not fit send it on NetChannel.ReliableBulk using the jersey-chunk pattern). CupRequest = kind u8 (PickNation, Ready, Spectate, Unspectate, RoundResult, Loaded, SetOrder, PullLever, CallCoin, SkipCelebration, CaptainDecides, Continue, PlayAgain, Quit) + payload. CupStream = from-slot u8, cam pos/rot(quaternion as 4 floats)/fov, ball pos, n bodies (BodyState) - unreliable; the host forwards it ONLY to slots whose Spectating == from-slot. CupRoundState = the CupRoundState class fields.
- NetSession: RouteMessage cases (client accepts host-only types only from HostPeer; host rejects them; requests only on the host), IsHostOnly += CupState, CupRoundState, CupStream-from-host? (CupStream is relayed: the host validates the from-slot equals SlotOf(peer) before forwarding; clients accept CupStream only from the host), public methods BroadcastCupState(bytes), SendCupRequest(bytes), SendCupStream(bytes), BroadcastCupRoundState(bytes), events CupStateReceived(CupState), CupRequestReceived(slot, CupRequest), CupStreamReceived(CupStream), CupRoundStateReceived; keep MatchStarted true through Play Again; nothing else in the session changes.
- CupRound host/client: Host authority = the Local simulation plus: remote humans fed from NetInputSource(ConsumeInputForSlot) with wireYaw semantics (KeeperLookYaw for a keeping human, Yaw for a taker), snapshots of every body (virtual slots for AI bodies 8..15 and the referee; use the existing Snapshot/BodyState encoding with emoteId/emotePhase filled from each body's Celebration) at NetSnapshotInterval, CupRoundState broadcast on every phase change and each second; Client authority = bodies as display puppets interpolated from snapshots (reuse the interpolation approach of NetSetPieceMatch's client path: read it and port the minimum), CupRoundState applied for HUD/phases, the local human's input sent via session.SetLocalInput with the correct yaw source, cameras from ApplyState (taker/keeper/lineup views), the local keeper predicted+reconciled like NetSetPieceMatch.ReconcileLocalBody if feasible (else note it). Replay skip: host counts SkipVote from humans with a body in the round; unanimous. Scored-window skip: CupRequest.SkipCelebration honoured only from the scorer's slot.
- Spectate mirror (CupNet + a small CupSpectatorView MonoBehaviour you own): a client whose director says it is spectating slot S receives CupStream and renders it: display bodies created from the stream's body list (nation kits from CupState), ball, and rig.MirrorView(pos, rot, fov); the sender side: a local round (Head to Head parallel phase) streams at 20 Hz while anyone spectates it (the host tells the owner via CupState that it has spectators; else do not send).
- Director MP plumbing (shared partial): host builds CupState from its model and broadcasts on StateChanged (coalesce to at most 10/s); clients apply it and fire StateChanged; every intent method: on the host apply directly, on a client send CupRequest; the host handles CupRequestReceived by validating (slot ownership, phase) and applying. Loaded barrier. Leaver hook: NetSession.RosterChanged -> mark the slot ReplacedByAi (the style partials decide what that means).
Compile clean; in your report give the byte layout of each message and the measured worst-case CupState size, plus the exact events/methods the Head to Head and Co-op agents must use.`, { label: 'H wire + session', phase: 'Wire', schema: REPORT })

log(`H done: compile=${h && h.compileClean}`)

const MPAPI = `
=== WIRE/SESSION API JUST ADDED BY AGENT H ===
${h ? h.publicApi : ''}
H notes: ${h ? h.notesForNextAgents : ''} | open: ${JSON.stringify(h ? h.openIssues : [])}
=== END ===`

phase('Styles')
const [i, j] = await parallel([
  () => agent(`${COMMON}
${MPAPI}

YOUR TASK (agent I, "Head to Head"): you own Assets/Scripts/Cup/CupDirector.HeadToHead.cs (and may make listed seam edits in CupLobbyUI.cs / NationPickerUI.cs / CupRound.cs / CupDirector.cs when a hook is missing). Implement design doc section 4 and 6.3 completely on top of H's plumbing:
lobby -> host StartMatch -> NationPick (strip variant; distinct nations, first request wins, "taken by") -> Bracket 5 s (host timer authoritative) -> stage parallel phase: every human-vs-AI round runs LOCALLY on its owner's client (CupRound Local authority under that client's RoundRoot, its own loading card/coin toss/intro; the owner reports RoundResult to the host (scores, kick line, coin call right) and streams CupStream while it has spectators) -> the Cup lobby as each player finishes (live rows from CupState: opponent, score, kick; Spectate -> CupSpectatorView mirroring; Esc back; when the spectated round ends everyone watching returns) -> head-to-head phase: when all parallel rounds are done, each human-vs-human round of the stage is played ONE AT A TIME on the host (CupRound Host authority; both participants get bodies and control; the "HEAD TO HEAD - up next" interstitial for the two participants; everyone else spectates via the host's snapshots + the spectated participant's camera pose) -> ready gate (all rounds of the stage finished AND every surviving human ready; eliminated auto-ready) -> Advance -> bracket 5 s -> next stage ... -> Final -> CupPodium for everyone (the champion on the dais, the other connected humans + AI fill around it; the winner's emotes stream through the round/podium snapshot path) -> Play Again (host: back to NationPick with a new seed, same lobby, MatchStarted stays true) / End Match (host: ReturnToMainMenu for all via the existing paths; client: LeaveNetworkedMatch). Leavers: a leaver's nation becomes AI for the rest of the bracket (simulate its later rounds; a round in progress finishes with a CupBotTaker/AI keeper on that side), row reads "(AI)"; no humans left alive -> simulate the remaining stages and show the podium with the AI champion. Pause menu: PauseMenu.Overlay = true in this style; SetCupLabels per role (client "Quit to Menu"/"An AI plays your nation from here"; host "End Match"/"Ends the cup for everyone"). Coin calls: everyone present calls; H2H shows no verdict and no band; CupCareer records. Career stats for the local player. Compile clean; in your report trace an 8-human cup (two humans meet in the Round of 16) phase by phase naming methods and messages.`, { label: 'I head to head', phase: 'Styles', schema: REPORT }),

  () => agent(`${COMMON}
${MPAPI}

YOUR TASK (agent J, "Co-op"): you own Assets/Scripts/Cup/CupDirector.Coop.cs and NEW Assets/Scripts/Cup/CupOrderUI.cs (and may make listed seam edits in NationPickerUI.cs / CupRound.cs / CupChoreo.cs / CupResultsUI.cs / CupDirector.cs when a hook is missing). Implement design doc section 5, 6.8 and the Co-op parts of 6.1, 6.6, 6.12, 7 and 8.2 on top of H's plumbing:
lobby -> host StartMatch (host = Captain) -> NationPick (vote variant: counters on flags, changeable picks, majority gate, CAPTAIN DECIDES when everyone picked without a majority) -> Bracket 5 s (team nation outlined + "YOUR TEAM" names) -> CupOrderUI (doc 6.8: N-1 shooter slots + 1 keeper slot 140x190, chips bench 96x40, hand-rolled IMGUI drag-and-drop with swap on occupied, the slot-machine lever 60x220 whose permutation the host rolls from the cup RNG Fork(stage) and broadcasts BEFORE the 1.8 s reel animation (stops left to right 0.25 s apart) so every client lands the same; rules: exactly one keeper, all filled, nobody twice; Ready for everyone; gate) -> loading barrier -> coin toss (Captain calls officially, everyone calls, calls band 3 s after the flip) -> the round on the HOST (CupRound Host authority; humans in the lineup by order; shooters cycle (kick 6 wraps); the keeper keeps every opponent kick and stands in the lineup otherwise; the AI team mirrored across the box; lineup free-look cone; scored window + scorer skip; walk-back; dejection trio on a losing miss; win beat frees the whole lineup) -> Won: 5 s beat -> bracket 5 s -> next stage's order screen ... ; Lost: dejection -> CupResultsUI GAME OVER with stage tabs and per-player columns (Kicks, Goals, Missed, Saved-against, GK Saves, GK Conceded; keep per-player-per-stage stats in the director) -> End Match / Play Again (Captain; restart at NationPick with the same lobby); Won the Final -> CupTrophyLift.Begin(...) -> CHAMPIONS results. Leavers: dropped from the order, slot count -1; a leaving keeper -> the lowest-ordered shooter keeps for the rest of the round and the Captain is prompted at the next order screen. Pause overlay + labels per role. Career stats (Team Player on a won Final). Compile clean; in your report trace a 3-player Co-op cup through the Round of 32 (one scored kick, one saved, a losing miss) naming methods and messages.`, { label: 'J co-op', phase: 'Styles', schema: REPORT }),
])

log(`Styles done: I=${i && i.compileClean} J=${j && j.compileClean}`)

phase('MP integrate')
const r1 = await agent(`${COMMON}
${MPAPI}
=== STYLE AGENTS ===
I (Head to Head): ${i ? i.publicApi : ''} | notes: ${i ? i.notesForNextAgents : ''} | open: ${JSON.stringify(i ? i.openIssues : [])}
J (Co-op): ${j ? j.publicApi : ''} | notes: ${j ? j.notesForNextAgents : ''} | open: ${JSON.stringify(j ? j.openIssues : [])}
=== END ===

YOUR TASK (agent R1, "Multiplayer integration"): you may edit ANY file under Assets/Scripts/Cup/ and the net/session files. Review and fix the multiplayer seams end to end, as the most careful engineer on the team:
1. Message correctness: every codec writes and reads the same fields in the same order; every host-authored type is in IsHostOnly; clients never apply a CupState/CupRoundState from a non-host peer; the host validates every CupRequest (slot ownership, phase, captain-only kinds, scorer-only skip, distinct nations, majority rules); CupStream is relayed only to spectators of that slot and only accepted from the host on clients; worst-case sizes under ~1100 bytes on Reliable (else ReliableBulk chunking).
2. Lifecycle: one _matchRoot for the whole cup; RoundRoot destroyed/rebuilt; ClearSnapshotBuffer + ResetSlotInput between rounds; the director tick monotonic; MatchStarted stays true through Play Again; End Match / Quit / host-leave paths reach the existing ReturnToMainMenu / LeaveNetworkedMatch; NetPump survives; no subscription leaks (every += has a -= on destroy); statics restored.
3. Authority per style matches the doc: Head to Head parallel rounds Local on the owner + RoundResult; human rounds and Co-op Host; clients display-only; spectate mirror; unanimous replay skip; kick clock/watchdog only on the authority.
4. Determinism: the bracket, first kickers (via the coin rule), free-kick spots, dejection variants, loser poses and the slot-machine permutation derive from the seed and never disagree between peers; the coin face shown always matches the seeded first kicker and the official caller's call.
5. UI: every screen allocates its controls every pass; the overlay pause never freezes anything in MP; cursor capture correct on every transition; labels per style.
Fix everything you find (list each fix). Compile clean. Report remaining risks honestly.`, { label: 'R1 MP integration', phase: 'MP integrate', schema: REPORT })

log(`R1 done: compile=${r1 && r1.compileClean}`)
return { h, i, j, r1 }
