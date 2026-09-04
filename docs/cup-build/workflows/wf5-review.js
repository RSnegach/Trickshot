export const meta = {
  name: 'cup-review',
  description: 'Trickshot Cup phase 5: three-lens adversarial review against the design doc, then fixes, then final verification and docs',
  phases: [
    { title: 'Review', detail: 'gameplay-vs-spec | UI/IMGUI | net/lifecycle (parallel)' },
    { title: 'Fix', detail: 'two fixers on disjoint file sets' },
    { title: 'Verify', detail: 'compile, re-review the fixes, CLAUDE.md invariants, handoff' },
  ],
}

const NOTES = 'C:/Users/evrik/downloads/Trickshot/Trickshot/docs/cup-build/cup-build-notes.md'
const DOC = 'docs/trickshot-cup-design.md'
const REPO = 'C:/Users/evrik/downloads/Trickshot/Trickshot'

const FINDINGS = {
  type: 'object',
  required: ['summary', 'findings'],
  properties: {
    summary: { type: 'string' },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        required: ['id', 'severity', 'file', 'symbol', 'problem', 'fix', 'specRef'],
        properties: {
          id: { type: 'string' },
          severity: { type: 'string', enum: ['blocker', 'major', 'minor', 'polish'] },
          file: { type: 'string' },
          symbol: { type: 'string' },
          problem: { type: 'string' },
          fix: { type: 'string' },
          specRef: { type: 'string' },
        },
      },
    },
  },
}

const REPORT = {
  type: 'object',
  required: ['summary', 'filesEdited', 'compileClean', 'fixed', 'skipped', 'notes'],
  properties: {
    summary: { type: 'string' },
    filesEdited: { type: 'array', items: { type: 'string' } },
    compileClean: { type: 'boolean' },
    fixed: { type: 'array', items: { type: 'string' } },
    skipped: { type: 'array', items: { type: 'string' } },
    notes: { type: 'string' },
  },
}

const p = args || {}
const FILES = (p.reportFiles || []).join('\n  ')
const KNOWN = `
KNOWN ISSUES CARRIED FORWARD FROM THE BUILD REPORTS (verify each is either already fixed or still open; if open it is a finding):
- Walk-back: the spot-to-lineup walk is about 6.9 m at CupTuning.WalkSpeed 1.6 m/s = 4.3 s, longer than WalkBackMax 3.5 s, so the driver's cut always hides a snap. Decision: raise WalkSpeed to 2.0 (a brisk walk) so the arrival is visible within the cap, unless the editor pass already changed the numbers.
- Free kicks (SUPERSEDED, owner's decision, built in phase 3): FREE KICKS have NO lineup and no walk-in / walk-back at all - every taker starts at the run-up mark, the non-playing bodies stand scattered on seeded per-kick marks (CupSpots.FreeKickMarks: own side 3-8 m behind the ball, the other side 10-14 m back on the taker's left, never on the spot or the run-up path), a missed/saved free kick is a 3 s dejection on the spot (RoundPhase.WalkBack doubles as that beat), a goal frees the whole scoring side. Do NOT re-introduce a free-kick lineup; verify the scatter keeps every body off the run-up path and that CupBody.LineupMark's doc comment (CupBodies.cs, still says "position in its side' + "'" + 's lineup") is corrected for that format.
- Penalty camera: from 3 m back / 1.5 m high the posts cannot land at 11%/89% of the frame with the ball visible. Decision: keep the ball in frame as the hard rule and let the FOV solve place the posts as close to 11/89 as the ball allows; tune CupTuning.PenaltyCamBack/Height toward about 5 m back / 1.1 m high if the editor pass did not already settle it. Check the rig's current numbers and the comment explaining the tension.
- Solo stage-complete lobby: the Customize button is disabled (OnCustomizeRequested null). Decision: route it to CustomizeUI in appearance-only mode over the menu backdrop if that can be done safely in under an hour of work; otherwise leave it disabled with a tooltip-free hint and log the gap.
- CupHud reaches C1/D members through a cached reflection seam (DriverBridge). Decision: replace with direct calls now that the tree is stable (mechanical).
- CupLoadingUI / CupIntroCard draw at GUI depth -1, in front of the pause menu. Decision: draw at depth 0 after the pause menu when PauseMenu.Paused (or return early on Paused like every other card).
- HUD bound right after Configure shows a faint 0-0 scoreboard through the loading scrim. Decision: bind the HUD after Loading.Hide.
- Lateral pose sign convention (KeeperPose vs the EmotePose header) - the editor pass decided which is right; make sure CupPoses and the three Celebration hunks agree with the editor-verified result and that the older wheel emotes were not mirrored by accident.
- The director's TryBeginPodium is now a direct CupPodium.Begin call (phase 3); verify no reflection probe remains anywhere in the director (CupHud's DriverBridge is the other one, above).
- Coin toss: director.CallCoin drops calls unless Phase == CoinToss; ResolveCoinCalls is not idempotent; ClearCoinCalls must run before each toss - verify every style's flow does this in the right order. PHASE 4 CHANGED THIS: CallCoin also accepts a call while a ceremony is open (Head to Head's parallel tosses run under the Round phase, and the host's request gate accepts CallCoin in that phase); ResolveCoinCalls judges only the local call for a Local-authority Head to Head round and never counts a judged call twice; a changed call clears its old verdict. Verify those three seams are still in place and consistent, do not undo them.
- Phase 3 podium / lift carry-overs (agent G): the TrophyLift pose's lateral sign follows the CupPoses/KeeperPose rule (+Z on the LEFT arm = inboard); if the older EmotePose header still claims the opposite, fix the HEADER, not the numbers. The podium builds everything at once (confetti + fanfare at Begin, the hand-over cut 1.4 s later, buttons at 3 s) - acceptable. The Co-op trophy lift teleports the team to the centre circle under its first cut because PitchLayout.PitchCenterZ is 52 m from the goal - acceptable, documented. The podium / free-window hint strings are consts on CupPodium / CupTrophyLift rather than CupText - acceptable. A wall deflection flying back into the scattered free-kick group can touch a body and re-launch under SetPieceShot (rare; IgnoreBody per body is the fix if cheap). KeeperHands' hold semantics were never inspected: if a claimed ball carries the keeper's velocity, a human keeper walking toward his own goal reads as \`approaching\` and runs to the 20 s hard cap - check and, if so, exclude a held ball from the approaching test.
- Phase 4 wire (agents H / I / J / R1; ProtocolVersion 8, CupNet.StateVersion 1): MsgType CupState 23 / CupRequest 24 / CupStream 25 / CupRoundState 26. CupStream is DELIBERATELY NOT in IsHostOnly (clients send it; the two direction checks are inline in RouteMessage) - not a finding. A client's rebuilt draw is checked against the host's FNV bracket hash and a mismatch is logged once, not repaired; a nation-table change must bump ProtocolVersion - verify that rule is written next to CupNations' table. If you add a replicated field: CupStateMsg + NetCodec.CupState/ReadCupState + CupNet.BuildState + CupDirector.Net.NetApplyState, bump StateVersion, keep CupNet.SizeOf honest (worst case 639 B today).
- Phase 4 DELIBERATE gaps (accepted, do not report as findings unless you have a cheap complete fix): no client-side keeper prediction (the local keeper on a client is a puppet answering the host a round trip late); the host's EndMatch sends CupState(Ended) once and shuts the socket (a lost packet leaves a client on the 5 s host timeout); the Co-op lever-reel gate is host-local time (a slow client's last reel may stop ~RTT after the gate opens); a leaving keeper's gloved body is RE-SLOTTED to the lowest-ordered shooter for the rest of that round (keeps the leaver's look); ApplyLeave's shed rule for a bench leaver in a partial order is a guess the Captain corrects; during a parallel Head to Head wave a bodiless peer can spectate but cannot call that round's coin (design 6.11's spectator call holds for host rounds only); the host goes straight from the last parallel round to the Interstitial / Podium without a lobby beat (design 4.7); a client participant's coin ceremony runs on its own clock and may still be in the air when the host's Intro state arrives (cosmetic).
- Phase 4 OPEN (verify and fix where the fix is contained): (a) client puppets are posed with DisplaySnap / DisplayEmote only, so a running AI taker or a diving keeper reads as a sliding statue on a client - BodyState.anim already carries the AnimState hint; teach CupRoundDriver's ApplyBodyPose a DisplayAnim path the way NetSetPieceMatch does. (b) Esc on a LOBBY spectator's CupSpectatorView closes it only on the host's echo (~RTT) because a local clear of LocalPlayer.SpectatingSlot would be re-applied by a stale CupState - add a pending-unspectate latch in NetApplyPlayers (the coin call already has one). (c) A refused RoundResult (rules, wrong first kicker, or a host-simulated round) is settled by the wave watchdog only after HeadToHeadResultGrace (10 s) - the client then follows the simulated verdict (NetApplyResult); verify that path and shorten the wait if a refusal can be answered at once. (d) CupHud.cs has an unused local \`teamW\` (line ~279) - remove it. (e) The client TAKER's power meter is a DISPLAY-ONLY SetPieceTaker armed on the host's Armed edge (ClientSyncDisplayTaker, sharing \`_chargeGate\` with ArmTaker; the two never coexist on one driver) - verify it resets on Live / role change and never launches. (f) A client puppet must NEVER get Celebration.Play (the HUD wheel and the lift pass a null Celebration on a client; the podium's winner is a LIVE body on every peer) - verify. (g) Head to Head's HandSlotToAi (CupRoundDriver.Leaver.cs, Host-authority rounds only) and Co-op's CupRoundDriver.HumanLeft (every peer, from the players' Left flags) are two separate leaver paths that must never both run on one round - verify the style gates. (h) IsAuthority is false on a client whose session died (\`_netLostAsClient\`) until HostConnectionLost tears the match down - verify no flow reacts to that frame. (i) Nothing in phase 4 was run on loopback or in the editor; the first loopback pass is listed in phase4-multiplayer.json's r1.notesForNextAgents - carry it into the handoff verbatim.
- Cup lobby Customize is disabled in BOTH Solo and Head to Head (TODO(h2h-customize)) for the same reason (GameBootstrap.ShowLobbyCustomize returns to the multiplayer LobbyUI; its preview camera was never checked against a standing arena) - treat the Solo bullet above as covering both.
`
const COMMON = `
You are working on the "Trickshot Cup" mode implementation in the Unity game at ${REPO}. It was built by a team of agents over four phases; their reports are JSON files (read them all, each "result" entry has publicApi/notes/openIssues):
  ${FILES}
FIRST read: ${NOTES}, ${REPO}/${DOC} (the spec), ${REPO}/CLAUDE.md, then EVERY file under ${REPO}/Assets/Scripts/Cup/ and the touched existing files (git diff --stat shows them).
${KNOWN}
Rules: C# 9 / Unity 6; compile with \`bash docs/compile-check.sh\` (exit=0); no Unity editor / MCP / graphify; CRLF-normalise edited .cs files; never say "tie". Do not commit.`

phase('Review')
const LENSES = [
  { key: 'spec', prompt: `${COMMON}
YOUR LENS: GAMEPLAY VERSUS THE SPEC. Walk the design doc section by section (2.1-2.8, 3, 4, 5, 7, 8, 10, 12.1) and check the code does exactly what it says: rules (five kicks alternating, early finish, sudden death pairs, coin toss decides the first kicker, 12 s clock + weak auto-shot, GOAL/SAVED/MISS with a wall stop = SAVED, no EPIC), the stage ramp values and where they are applied, standardised shooting in every style, 32 nations with humans in distinct Round of 32 rounds and no novelty AI, every timing in 2.7, the kick cycle 7.2, lineup geometry 7.3, scored window + scorer-only skip, walk-back, dejection trio, win beat, referee whistle raise before EVERY whistle, the coin toss ceremony + calls (everyone present; official caller decides; no calling after the flip; H2H silent; Co-op band), the podium (winner emotes, static losers in three poses), the trophy lift, career stats, leaver rules, Solo quit ends the cup, Play Again semantics. Also hunt logic bugs: phase machines that can stall, events subscribed twice or never unsubscribed, timers using scaled time under the Solo pause, null paths (Choreo/Rig null), off-by-one in kick indices, RNG streams shared where they should be forked (peer disagreement), statics not restored, materials/meshes leaked, bodies not destroyed between rounds. Be adversarial: for each finding give file, symbol, the precise problem, the precise fix, and the spec reference. Report only real, verified findings (quote the code line).` },
  { key: 'ui', prompt: `${COMMON}
YOUR LENS: UI, IMGUI AND UX. For every OnGUI in Assets/Scripts/Cup/ and the touched existing screens (HostSetupUI cup branch, SessionBrowserUI filter, PauseMenu overlay/labels, LobbyUI role name): controls allocated on EVERY event pass (no early returns before a control, no conditional controls between Layout and Repaint - GUI.enabled instead), MenuScale.Begin/End on every path with callbacks after End, UITheme.Label/Button/Toggle instead of raw GUI.Label, UIFont.Heavy for large text, cached GUIStyles over a running match, ClickBlocker for modals, cursor capture exactly right on every transition (captured during play, freed on screens, re-asserted after PauseMenu.Resume), Esc ownership (pause vs overlays vs wheel), the yellow skip texts and counters, the calls band, the nation picker's three variants, the bracket tree fitting 760 design height with 16 rows per half, the loading card covering the build, the intro card, the HUD (scoreboard codes/colours, pips, role panel, kick ring, callouts through Hud.Flash/Banner with the KindOf words), the emote wheel pages (podium page leaves the left arm alone), the results tabs, the knocked-out card, host-setup panel height math. Also verify design-doc screen specs 6.1-6.12 numerically (sizes, fonts, positions). Report only real, verified findings with file/symbol/problem/fix/specRef.` },
  { key: 'net', prompt: `${COMMON}
YOUR LENS: NETWORKING, LIFECYCLE AND DETERMINISM (doc 9.3-9.6, 10). Check: every new MsgType has a codec pair writing/reading identical field order; host-only types in NetSession.IsHostOnly; clients accept host-only types only from HostPeer; the host validates every CupRequest (slot ownership, phase, captain-only, scorer-only skip, distinct nations, majority); CupStream relayed only to spectators of that slot, accepted on clients only from the host, and the sender only streams while it has spectators; worst-case message sizes under ~1100 bytes on Reliable or chunked on ReliableBulk; CupState coalescing; the Loaded barrier + 10 s timeout; MatchStarted stays true through Play Again; End Match / Quit / host-leave reach ReturnToMainMenu / LeaveNetworkedMatch without re-entrancy loops; one _matchRoot for the cup with RoundRoot rebuilt; ClearSnapshotBuffer + ResetSlotInput between rounds; monotonic tick; NetPump survives; all event subscriptions removed on destroy; statics restored; authority per style (Head to Head parallel rounds Local on the owner + RoundResult reporting; human rounds and Co-op Host; clients display-only; the local keeper prediction); snapshots carry emoteId/emotePhase; the replay skip vote unanimous among humans with a body; kick clock/watchdog only on the authority; determinism: bracket, coin faces (always consistent with the seeded first kicker and the official call), free-kick spots, dejection variants, loser poses, slot-machine permutation all seed-derived and never disagreeing between peers; leaver handling in each style; the pause overlay never freezes anything in MP; PauseMenu.Paused cutting local input. Report only real, verified findings with file/symbol/problem/fix/specRef.` },
]
const reviews = (await parallel(LENSES.map(l => () => agent(l.prompt, { label: `review:${l.key}`, phase: 'Review', schema: FINDINGS })))).filter(Boolean)
const all = reviews.flatMap(r => r.findings)
const seen = new Set()
const findings = all.filter(f => { const k = (f.file + '|' + f.symbol + '|' + f.problem.slice(0, 60)).toLowerCase(); if (seen.has(k)) return false; seen.add(k); return true })
log(`Review: ${all.length} findings, ${findings.length} after dedup (${findings.filter(f => f.severity === 'blocker').length} blockers, ${findings.filter(f => f.severity === 'major').length} major)`)

phase('Fix')
// Partition by file so the two fixers never touch the same file.
const byFile = {}
for (const f of findings) { (byFile[f.file] = byFile[f.file] || []).push(f) }
const files = Object.keys(byFile).sort((x, y) => byFile[y].length - byFile[x].length)
const groups = [[], []]
const loads = [0, 0]
for (const file of files) { const g = loads[0] <= loads[1] ? 0 : 1; groups[g].push(file); loads[g] += byFile[file].length }
const fixPrompt = (n, group) => `${COMMON}
YOUR TASK (fixer ${n}): apply these reviewed findings. You may edit ONLY these files (another fixer owns the rest; if a fix truly needs a file outside your list, skip it and say so): ${group.join(', ')}.
Findings (id | severity | file | symbol | problem | fix | spec):
${group.flatMap(file => byFile[file]).map(f => `- ${f.id} | ${f.severity} | ${f.file} | ${f.symbol} | ${f.problem} | FIX: ${f.fix} | ${f.specRef}`).join('\n')}
Verify each finding against the code before changing it (a reviewer can be wrong - if a finding is not real, skip it and explain). Apply real fixes properly, not band-aids. Compile clean after each file. Report fixed / skipped ids.`
const fixes = await parallel(groups.filter(g => g.length).map((g, n) => () => agent(fixPrompt(n + 1, g), { label: `fix:${n + 1}`, phase: 'Fix', schema: REPORT })))
log(`Fix: ${fixes.filter(Boolean).map(f => f.fixed.length).reduce((a, b) => a + b, 0)} fixed, ${fixes.filter(Boolean).map(f => f.skipped.length).reduce((a, b) => a + b, 0)} skipped`)

phase('Verify')
const final = await agent(`${COMMON}
Fixers applied these: ${JSON.stringify(fixes.filter(Boolean).map(f => ({ fixed: f.fixed, skipped: f.skipped, notes: f.notes })))}
Original findings: ${JSON.stringify(findings.map(f => ({ id: f.id, severity: f.severity, file: f.file, problem: f.problem })))}
YOUR TASK (final verifier): (1) run the compile check; fix any breakage. (2) Re-verify every blocker/major finding is actually resolved in the code (read it); fix anything still wrong. (3) Run the pure self-test through the scratch console project at C:/Users/evrik/downloads/Trickshot/Trickshot/docs/cup-build/cuptest (dotnet run) if it still builds against the current pure files; fix the pure files if the test fails. (4) Update ${REPO}/CLAUDE.md: add a concise "Trickshot Cup" section in the style of the existing sections (short factual invariants only: naming round/stage, CupRoundDriver vs CupRound, authority per style, RoundRoot/one match root, PauseMenu.Frozen vs Paused vs Overlay, the coin-face/first-kicker contract, the emote wire ids, the referee kit, statics hygiene, ProtocolVersion 8, the self-test menu, "no tie"), pruning nothing else. (5) Write ${REPO}/docs/claude-handoff.md fresh: what was built, what is compile-verified only (everything - the editor was not available), the exact in-editor verification steps to run next (play mode via Unity MCP: the SP chain to the cup, the fork, nation pick, bracket, a round; loopback MP for Head to Head and Co-op; the self-test menu), and every remaining open issue from all reports that is still open. Keep it tight. (6) Append a one-line pointer about the implementation to the "Update" line in DESIGN_NOTES.md's Trickshot Cup bullet. CRLF for all three docs. Report honestly what is verified and what is not.`, { label: 'final verify + docs', phase: 'Verify', schema: REPORT })

log(`Final: compile=${final && final.compileClean}`)
return { findings, fixes, final }
