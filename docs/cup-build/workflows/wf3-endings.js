export const meta = {
  name: 'cup-endings',
  description: 'Trickshot Cup phase 3: podium + trophy lift, and the menu vignette (parallel)',
  phases: [
    { title: 'Endings', detail: 'podium, trophy, trophy lift | CupScene vignette' },
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
=== WHAT IS ALREADY IN THE TREE (Assets/Scripts/Cup/, compiles clean) ===
Earlier phases wrote detailed reports as JSON files (each has a "result" object whose entries carry publicApi, notesForNextAgents, openIssues, filesCreated/filesEdited). READ EVERY ONE fully before writing code, then read the actual files:
  ${FILES}
Naming facts: the round DRIVER is \`CupRoundDriver\` (partial MonoBehaviour); \`CupRound\` is the pure round record (CupBracket.cs); \`RoundLine\` the kick-sequence state; \`RoundPhase\`/\`RoundAuthority\` in CupRoundState.cs; \`CupDirector\` is a partial class (CupDirector.cs + .Solo.cs/.HeadToHead.cs/.Coop.cs) with the read model, intents, Apply* appliers, StartRound/EndRound/NewRoundRoot; \`CupDirector.Instance\` singleton; screens register via director.AddGuiHook. Cameras: \`CupCameraRig\`; HUD: \`CupHud\`; choreography: \`CupChoreo\` + \`CupCoinToss\` + \`CupPoses\`; screens: NationPickerUI, CupBracketView/CupBracketScreen, CupLobbyUI, CupResultsUI, CupKnockedOutUI, CupLoadingUI, CupIntroCard. PauseMenu.Paused (menu up) vs PauseMenu.Frozen (sim frozen; never in MP because Overlay). Emotes TrophyLift=33, DejectKnees=34, DejectHips=35, DejectFall=36, WhistleRaise=37. CupText for exact strings, CupTuning for every number, CupSalts for RNG salt families.
=== END ===`

const COMMON = `
You are one builder in a multi-agent implementation of the "Trickshot Cup" mode for the Unity game at ${REPO}.
FIRST read, in full: (1) ${NOTES}, (2) ${REPO}/${DOC}, (3) ${REPO}/CLAUDE.md, (4) every file under ${REPO}/Assets/Scripts/Cup/.
${API}
Rules: C# 9 / Unity 6, everything inside \`namespace Trickshot\`; compile with \`bash docs/compile-check.sh\` after every file and before returning (exit=0); no Unity editor / MCP / graphify; CRLF-normalise every .cs you touch at the end (\`sed -i 's/\\r$//; s/$/\\r/' <file>\`); never say "tie"; production quality, commented, exhaustive; free every material/mesh; restore statics. Edit only your owned files (small listed seam edits allowed). Do not commit. Return the structured report.`

phase('Endings')
const [k, g, v] = await parallel([
  () => agent(`${COMMON}

YOUR TASK (agent K, "Free-kick choreography and the kick clock"): the owner played the Solo cup and asked for these changes to the FREE KICKS format specifically (penalties keep the doc's lineup choreography). You own edits to Assets/Scripts/Cup/CupRoundDriver.Kick.cs, CupRoundDriver.Scene.cs, CupChoreo.cs, CupPoses.cs, CupSpots.cs and CupTuning in CupTypes.cs; do not touch the podium/vignette files (other agents own them right now).
1. In free kicks there is NO lineup and NO walking to or from it: every taker starts AT the ball (the run-up start behind the spot), human and AI alike - no AI walk-in, no walk-back. Bodies that are not taking or keeping stand SCATTERED behind the taker: the taker's own team (Co-op / Head to Head human side) 3-8 m behind the ball at seeded, natural-looking offsets (a loose group, not a line; seeded from the round RNG so peers agree), facing the goal, live and idle (balance on, locomotion off, free look cone like the lineup); the opposing side's non-keeping bodies further back and off to the side (10-14 m, away from the camera line). Recompute the marks per kick pair (the spot moves).
2. On a MISS / SAVED free kick the shooter plays one of the three dejection animations where he stands (the losing-miss trio, seeded variant), about 3 s, then the cut to the next kick; no walk-back phase. On a GOAL the scored window runs as designed (run + emote 5 s, scorer skip), and in Co-op / Head to Head the WHOLE scoring side's team is freed for those 5 s (locomotion + wheel), not just the scorer; the AI side stays put. The decisive kick keeps the win/lose beats.
3. The kick clock: it must stop the instant the kick is taken. The Armed->Live transition was made robust already (ball speed, or the taker reporting Struck, or the ball 30 cm off the spot) - verify it end to end by reading SetPieceTaker.Tick/LaunchSetPiece and the cup's input gate (CupBodies.cs) for any path where a human's charge/release does NOT launch the ball in free kicks (e.g. the run-up start or aim computed for the penalty spot, the wall built on the ball, a kinematic ball, a gate that swallows the release), and fix the root cause you find. The HUD ring must never show once Live.
4. Goal determination must match the other set-piece modes (owner's call): the per-frame BallFullyInGoal box test is now used alongside the physics-rate line-crossing latch; keep both, and make sure no path resolves a verdict while the ball is still travelling toward the goal (RestSpeed/RestHold/MaxLiveTime only after the ball has clearly stopped or left play; a ball rolling slowly toward the line must not be called a miss before it crosses).
Compile clean (bash docs/compile-check.sh). Do NOT use the Unity editor (another agent owns it now). Desk-check one free-kick round: human goal, human miss, AI goal, AI miss, listing the phases and marks.`, { label: 'K free-kick choreo', phase: 'Endings', schema: REPORT }),

  () => agent(`${COMMON}

YOUR TASK (agent G, "Podium and trophy lift"): you own NEW files Assets/Scripts/Cup/CupPodium.cs, CupTrophy.cs, CupConfetti.cs, CupTrophyLift.cs, and you may edit CupDirector.Solo.cs (to enter the podium after a won Final and the summary after it), CupDirector.cs (a Podium/TrophyLift phase entry helper usable by the Head to Head and Co-op partials later), and Celebration.cs (only to polish the TrophyLift pose). Implement design doc 8.1 and 8.2 completely:
- CupTrophy: MeshGen lathe cup + two Torus(arcDeg 200) handles + a small plinth, combined (MeshGen.Combine destroys inputs), about 0.45 m tall, ONE shared gold material (Make.Mat(new Color(0.85f,0.70f,0.30f), 0.85f, 0.75f)), collider-less, attached with Cosmetics.Piece(parent, mesh, mat) to the LEFT forearm (Bone.ForearmL via ActiveRagdoll.Phys) with MeshGen.Transform baking the hand offset (0, -0.22, 0) like AddGlove; a Detach/Destroy that frees the material; also a free-standing variant (on the pedestal) for the hand-over cut.
- CupPodium (Solo / Head to Head): built at the penalty spot on the real pitch after the Final's round objects are cleared: a MeshGen.Lathe stepped dais 1.6 m across, 0.6 m high, stone material + gold trim ring (two shared materials, freed); the winner body (spawn a shooter-style body in the nation kit with the human's appearance for the local player, AI look otherwise) on the dais, TrophyLift emote re-played whenever Celebration.Playing drops, the emote wheel with ONE curated page of standing emotes that leave the left arm alone (verify each candidate against EmotePose.Apply: FistPump, Point, Salute, Wave, Bow, Cheer - drop any that drives UpperArmL/ForearmL or sinks/launches the body) + a "Lift" entry that re-plays TrophyLift; losers as display bodies (BecomeDisplayBody + DisplayPose with the CupPoses loser poses, seeded variant, looking down) in a horseshoe facing the dais (Head to Head: every other human still connected + AI bodies of the beaten finalist and semi-finalists to make at least three; Solo: finalist + both semi-finalists + four quarter-finalists from the bracket, in their nation kits); rig.PodiumOrbit; CupConfetti: 200 Verlet quads (hand-rolled like HairSim: positions/prev positions, gravity, drag, flutter, a single mesh rebuilt each LateUpdate, one material) in the nation's two kit colours from 8 m up, recycled for 20 s; audio: PlayWhistleTriple already played by the round; on entry PlayGoalCelebration + CrowdCheer.Celebrate + AudioManager.PlayFanfare; UI (its own OnGUI): "CHAMPIONS - <NATION> - <name>" strip, bottom hint "B emotes - drag to orbit - Esc", after 3 s the buttons (Solo: New Cup / Main Menu; Head to Head host: Play Again / End Match; client: End Match) via director intents; then CupResultsUI CUP SUMMARY on Continue (or directly from the buttons per doc). MP hooks: expose the body list so the net agent can stream emotes (winner) - losers are static.
- CupTrophyLift (Co-op win, doc 8.2): a 14 s scripted cinematic driven by a shot list on CupCameraRig (add a generic \`Cinematic(IList<CupShot>)\` API to the rig if it lacks one - a small edit to CupCameraRig.cs, listed): the team jogs from the lineup to the centre circle (gait), the AI nation walks off, the referee applauds (Clap emote); the Captain is handed the trophy under a cut, plays TrophyLift; teammates Cheer/HandsUp/FistPump on staggered starts; confetti; fanfare; the camera arcs low-front to a high slow orbit; then a free window (everyone can move + emote; the Captain keeps the trophy); Continue -> CupResultsUI CHAMPIONS. Provide \`static CupTrophyLift Begin(CupDirector d, IList<CupBody> team, CupBody captain, CupBody referee, CupCameraRig rig, Action onContinue)\`.
- Wire the Solo ending: CupDirector.Solo.cs enters CupPodium after a won Final (replace the TODO(podium) fallthrough), then the summary; New Cup / Main Menu intents work.
Compile clean; desk-check the trophy mesh construction (profile points, handle placement) and the horseshoe layout for 3 and 7 losers in your report.`, { label: 'G podium + trophy lift', phase: 'Endings', schema: REPORT }),

  () => agent(`${COMMON}

YOUR TASK (agent V, "Vignette and menu polish"): you own the NEW file Assets/Scripts/Play/MenuScenes/CupScene.cs and edits to Assets/Scripts/Play/MenuScenes/MenuSceneStage.cs (the Create switch case for TrickshotCup) only. Build the live mode-panel vignette for the cup exactly in the house style of the existing scenes (read StrikerScene, KeeperScene, FreeKickScene/DeadBallScene, AccuracyScene, ScriptedInput, MenuScene, MenuSceneStage and docs/menu-scenes-design.md first): a penalty from the spot against a diving keeper - the taker (the player's roster look, forced Human like the other scenes) runs up, strikes low into the corner past the keeper (choose aim and flight time against the keeper dead band documented in the notes so the keeper DIVES and misses: about 4.2 m/s of reach at ability 0.6 - use a corner offset > the dead band or a flight under ~0.5 s), the ball hits the net, the taker plays FistPump, hold; a small gold trophy (MeshGen lathe, one material, freed in Destroy) sits on a low plinth at the side of the frame as the mode's signature prop. Follow every rule in the "Menu scene panels" section of CLAUDE.md: stage at positive Z on x = 0, Goalkeeper outSign correct (he must react - a wrong sign silently never dives), freeze = BecomeDisplayBody + disabling ActiveRagdoll/HairSim/AnatomySim, materials freed at the source, Frame() framing that fits the panel aspect (PanelAspect), Reset() restores the initial pose exactly, 2-3 s total. Then wire MenuSceneStage.Create(TrickshotCup) to it. Compile clean; in your report give the timeline (t = 0 whistle ... t = strike ... t = net) and the numbers you chose for the aim vs the dead band.`, { label: 'V vignette', phase: 'Endings', schema: REPORT }),
])

log(`Endings done: K=${k && k.compileClean} G=${g && g.compileClean} V=${v && v.compileClean}`)
return { k, g, v }
