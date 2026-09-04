export const meta = {
  name: 'cup-polish',
  description: 'Trickshot Cup phase 6: smoothness, game flow, menu flow and multiplayer correctness - 6 reviewers, 3 fixers, 1 verifier',
  phases: [
    { title: 'Review', detail: 'smoothness | game flow | menu flow | MP state | MP authority | MP lifecycle' },
    { title: 'Fix', detail: 'three fixers on disjoint file sets' },
    { title: 'Verify', detail: 'compile, self-test, re-verify every blocker/major, docs' },
  ],
}

const REPO = 'C:/Users/evrik/downloads/Trickshot/Trickshot'
const NOTES = REPO + '/docs/cup-build/cup-build-notes.md'
const DOC = 'docs/trickshot-cup-design.md'

const FINDINGS = {
  type: 'object',
  required: ['summary', 'findings'],
  properties: {
    summary: { type: 'string' },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        required: ['id', 'severity', 'file', 'symbol', 'problem', 'fix', 'evidence'],
        properties: {
          id: { type: 'string' },
          severity: { type: 'string', enum: ['blocker', 'major', 'minor', 'polish'] },
          file: { type: 'string' },
          symbol: { type: 'string' },
          problem: { type: 'string' },
          fix: { type: 'string' },
          evidence: { type: 'string' },
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

const COMMON = `
You are reviewing the finished "Trickshot Cup" mode in the Unity game at ${REPO}. It was built by agents over five phases and has NEVER been run in the Unity editor or on loopback - every claim about it is compile- and desk-checked only. Your job is to find what is actually WRONG by reading code.

FIRST read: ${NOTES}, ${REPO}/${DOC} (the spec), ${REPO}/CLAUDE.md (the project invariants - the "Trickshot Cup" section especially), then every file under ${REPO}/Assets/Scripts/Cup/ plus the existing files the cup touches (\`git diff --stat HEAD~4 HEAD\` and \`git show --stat HEAD\` show them). Earlier phase reports (each entry has publicApi / notesForNextAgents / openIssues):
  ${FILES}

RULES OF EVIDENCE - this is the most important part of your task. The previous review pass produced findings that were WRONG in ways that would have shipped regressions (one prescribed fix would have broken 25 of 38 emotes). So:
- Quote the actual code line and file:line for every finding. If you cannot quote it, do not report it.
- Trace the call path that reaches the bug. Name the callers.
- Before reporting "X is never called" or "Y is never set", grep for it and say what you found.
- Prefer ONE certain finding to five speculative ones. An empty findings list is a valid, respectable answer.
- If a fix has a side effect (it changes a shared constant, it touches a file other modes use), SAY SO in the fix field.
- Do NOT report as bugs the things CLAUDE.md or the phase-5 report already record as deliberate decisions (the invisible kick clock, no client keeper prediction, the inert arm clamp, the solo-only choreography skip, Customize disabled in the cup lobby, the free-kick dejection using the WalkBack phase).

Rules: C# 9 / Unity 6; compile with \`bash docs/compile-check.sh\` (exit=0); no Unity editor / MCP / graphify; CRLF-normalise any .cs you edit; never say "tie". Do not commit. REVIEWERS: do not edit code at all, only report.`

phase('Review')
const LENSES = [
  { key: 'smooth', prompt: `${COMMON}

YOUR LENS: SMOOTHNESS - anything that stutters, snaps, pops or jitters on screen.
Read CupCameraRig, CupPenaltyCam, CupChoreo, CupPoses, CupRoundDriver.Scene/.Kick, CupTrophyLift, CupPodium, CupCoinToss, CupHud.
Hunt specifically for: camera cuts that snap because a rotation/position is not carried across a phase change; a camera solved per-frame from a value that changes discontinuously; smoothing that uses Time.deltaTime where the surrounding code is unscaled (or the reverse) so it changes speed under the pause overlay or the replay's slow motion; a lerp with a rate that is frame-rate dependent (t = rate*dt used directly as a Lerp factor is wrong above ~60fps unless intended); bodies teleported without their velocity zeroed (a snap then a slide); poses applied in Update that fight a FixedUpdate solver; a walk/gait phase reset mid-stride; an emote or pose override cleared one frame late so a limb flicks; and anything that runs while PauseMenu.Frozen when it should hold, or holds when it should run. For each, say WHAT THE PLAYER SEES.` },

  { key: 'gameflow', prompt: `${COMMON}

YOUR LENS: GAME FLOW - the round and the cup as a sequence of states that must always advance and never strand the player.
Read CupRoundDriver (all partials), CupRoundRules, CupBracket, CupSim, CupDirector (all partials).
Hunt specifically for: a phase with no exit under some input (an if/else where a branch never fires, a timer that is only decremented in a path that can be skipped, a wait on a flag nothing sets); a watchdog that can fire over a legitimate slow case and destroy real state; a phase entered twice (re-entrancy) so its entry work runs twice; kick indices / pair indices off by one at the sudden-death boundary; a round that can end with no winner or two winners; the bracket advancing with an unfinished round; state carried between rounds that should have been reset (grep the reset method and compare it field by field against the fields the round writes); a Solo pause that leaves a timer running. Trace at least one full round end-to-end and one full cup end-to-end and say where you looked.` },

  { key: 'menuflow', prompt: `${COMMON}

YOUR LENS: MENU AND SCREEN FLOW - every screen, every button, every way in and out.
Read CupSetupUI, NationPickerUI, CupBracketScreen, CupBracketView, CupLobbyUI, CupOrderUI, CupResultsUI, CupKnockedOutUI, CupLoadingUI, CupIntroCard, CupPodium's OnGUI, CupEmoteWheel, and the seams in GameBootstrap / MenuUI / MultiplayerHubUI / HostSetupUI / LobbyUI / PauseMenu.
Hunt specifically for: the IMGUI control-count rule (every control allocated on EVERY event pass - an early return or a conditional control between Layout and Repaint breaks every click on the screen; GUI.enabled is the correct tool, and CLAUDE.md states this); a Back button that returns to the wrong screen or to a screen that is no longer valid; a screen that can be opened twice or left open under another; cursor capture wrong on a transition (captured on a screen with buttons, or freed during play); Escape ownership (CupEscape.Owned vs PauseMenu vs the wheel); a dead button (a null callback with no hint); a path that reaches a screen with null data and throws; the cup's entries in the single-player and multiplayer menu chains. Say which screens you actually opened in code.` },

  { key: 'mpstate', prompt: `${COMMON}

YOUR LENS: MULTIPLAYER STATE AND THE WIRE. The owner's bar is "absolutely no multiplayer bugs", so be exhaustive and concrete.
Read CupNet, CupDirector.Net, CupRoundDriver.Net, CupSpectatorView, CupRoundState, NetMessages/NetCodec/NetSession (the cup parts).
Hunt specifically for: a codec whose write order and read order differ by even one field (check EVERY message field by field, in order, and say you did); a field written conditionally but read unconditionally (or the reverse); a size that can exceed one datagram (CupNet.SizeOf vs the real worst case - the phase-5 pass found a 4224 byte case that was documented as 639); a value that is seed-derived on one peer and wire-carried on another so they can disagree; an enum or index that is wire state and could be reordered; a byte cast that can overflow (a slot, a count, a score above 255); ProtocolVersion / StateVersion not bumped where the layout changed. Report each as: message, field, what the host writes, what the client reads.` },

  { key: 'mpauth', prompt: `${COMMON}

YOUR LENS: MULTIPLAYER AUTHORITY AND RACES. The owner's bar is "absolutely no multiplayer bugs".
Read CupDirector (all partials, especially .Net/.HeadToHead/.Coop), CupRoundDriver.Net/.Leaver, NetSession's cup routing.
Hunt specifically for: a client that can move a phase (only the host may - grep every SetPhase call and say which authority reaches it); an intent applied locally on a client before the host echo, then re-applied or contradicted by the echo; a host request handler missing a validation (slot ownership, phase, captain-only, scorer-only) - check EVERY CupRequestKind against its handler and list them; two peers deriving a value from different inputs; a leaver path that runs on one peer and not another (HandSlotToAi is Head to Head only, HumanLeft is Co-op only - verify the gates and that neither can run twice); an event subscribed without a matching unsubscribe (list every += and its -=); an ordering assumption between a reliable message and a snapshot. For every request kind, state the validation the host performs.` },

  { key: 'mplife', prompt: `${COMMON}

YOUR LENS: MULTIPLAYER LIFECYCLE - joining, leaving, ending, restarting, and what leaks.
Read CupDirector.Net's bind/unbind, CupLaunch, GameBootstrap's cup build/teardown, PauseMenu's cup labels and Overlay, CupRoundDriver's Configure/EndRound/OnDestroy, CupPodium/CupTrophyLift teardown, CupDirector's ResetForNewCup / PlayAgain / EndMatch.
Hunt specifically for: a static left set after a match (CLAUDE.md lists the statics the cup borrows - verify EVERY one is restored on every exit path including a host disconnect and an aborted round); a material, mesh or RenderTexture created and not destroyed (Play Again runs the whole cup again in one process, so a per-cup leak compounds); a GameObject root not destroyed between rounds; a coroutine or Update hook still running after teardown; an event handler on a session that outlives the match; Play Again leaving state from the previous cup (compare ResetForNewCup field by field against everything a cup writes); the host leaving mid-round vs a client leaving mid-round; two teardown paths racing. Say which exit paths you traced.` },
]

const reviews = (await parallel(LENSES.map(l => () => agent(l.prompt, { label: `review:${l.key}`, phase: 'Review', schema: FINDINGS })))).filter(Boolean)
const all = reviews.flatMap(r => r.findings || [])
const seen = new Set()
const findings = all.filter(f => {
  const k = ((f.file || '') + '|' + (f.symbol || '') + '|' + (f.problem || '').slice(0, 60)).toLowerCase()
  if (seen.has(k)) return false
  seen.add(k); return true
})
const rank = { blocker: 0, major: 1, minor: 2, polish: 3 }
findings.sort((a, b) => (rank[a.severity] ?? 9) - (rank[b.severity] ?? 9))
log(`Review: ${all.length} findings, ${findings.length} after dedup (${findings.filter(f => f.severity === 'blocker').length} blockers, ${findings.filter(f => f.severity === 'major').length} major)`)

phase('Fix')
// Partition by file so no two fixers ever touch the same file.
const byFile = {}
for (const f of findings) (byFile[f.file] = byFile[f.file] || []).push(f)
const files = Object.keys(byFile).sort((x, y) => byFile[y].length - byFile[x].length)
const groups = [[], [], []]
const loads = [0, 0, 0]
for (const file of files) {
  let g = 0
  for (let i = 1; i < 3; i++) if (loads[i] < loads[g]) g = i
  groups[g].push(file); loads[g] += byFile[file].length
}
const fixPrompt = (n, group) => `${COMMON}

YOUR TASK (fixer ${n}): apply these reviewed findings. You may edit ONLY these files - another fixer owns the rest, and editing outside your list will be overwritten: ${group.join(', ')}.

Findings:
${group.flatMap(f => byFile[f]).map(f => `- ${f.id} | ${f.severity} | ${f.file} | ${f.symbol}\n    PROBLEM: ${f.problem}\n    PROPOSED FIX: ${f.fix}\n    EVIDENCE: ${f.evidence}`).join('\n')}

VERIFY EACH FINDING AGAINST THE CODE BEFORE CHANGING ANYTHING. A reviewer can be wrong, and a confidently-worded wrong fix is the worst outcome here: the last pass had one that would have broken 25 of 38 emotes, and the correct call was to skip it and document why. If a finding is not real, or its fix would cause a regression, SKIP it and explain in detail. Prefer the smallest correct change. If a fix needs a file outside your list, skip it and say which file it needs.
Compile (\`bash docs/compile-check.sh\`, exit=0) after each file and before returning. CRLF-normalise every file you touch. Report fixed / skipped ids honestly.`

const fixes = (await parallel(groups.filter(g => g.length).map((g, n) => () => agent(fixPrompt(n + 1, g), { label: `fix:${n + 1}`, phase: 'Fix', schema: REPORT })))).filter(Boolean)
log(`Fix: ${fixes.reduce((a, f) => a + (f.fixed || []).length, 0)} fixed, ${fixes.reduce((a, f) => a + (f.skipped || []).length, 0)} skipped`)

phase('Verify')
const final = await agent(`${COMMON}

Fixers reported: ${JSON.stringify(fixes.map(f => ({ fixed: f.fixed, skipped: f.skipped, notes: f.notes })))}
All findings: ${JSON.stringify(findings.map(f => ({ id: f.id, severity: f.severity, file: f.file, symbol: f.symbol, problem: (f.problem || '').slice(0, 400) })))}

YOUR TASK (final verifier):
1. \`bash docs/compile-check.sh\` -> exit=0. Fix any breakage (the fixers ran in parallel; a seam may have been dropped).
2. Run the pure self-test: \`dotnet run -c Release\` in ${REPO}/docs/cup-build/cuptest. It must print ALL PASSED. Fix the pure files if not.
3. RE-VERIFY EVERY blocker and major by READING THE CODE at the named site - do not trust the fixer reports. Fix anything still wrong that you can fix safely. If a fixer skipped something for a good reason, keep it skipped and record why.
4. Check the fixers did not contradict each other (they ran in parallel on disjoint files, but a shared assumption can still break: grep for any symbol two of them touched).
5. Update ${REPO}/CLAUDE.md: merge in ONLY genuinely new durable invariants this pass established, in the existing terse bullet style, into the existing "Trickshot Cup" section. Do not duplicate what is already there and prune nothing.
6. Rewrite ${REPO}/docs/claude-handoff.md: what this pass changed, what is verified how, and every still-open issue. Keep the honest verification-state table (nothing has run in the editor or on loopback) and the in-editor steps still owed.
CRLF for all docs. Report honestly what is verified and what is not - an accurate "not verified" is worth more than a confident overstatement.`, { label: 'final verify + docs', phase: 'Verify', schema: REPORT })

log(`Final: compile=${final && final.compileClean}`)
return { findings, fixes, final }
