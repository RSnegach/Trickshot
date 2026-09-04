export const meta = {
  name: 'cup-editor-solo',
  description: 'Trickshot Cup phase 2b: drive the Solo cup in the Unity editor through Unity MCP, screenshot every beat, fix what is wrong',
  phases: [
    { title: 'Editor verify', detail: 'play mode: fork, nation pick, bracket, coin toss, a full round, stage complete; fix + re-verify' },
  ],
}

const NOTES = 'C:/Users/evrik/downloads/Trickshot/Trickshot/docs/cup-build/cup-build-notes.md'
const DOC = 'docs/trickshot-cup-design.md'
const REPO = 'C:/Users/evrik/downloads/Trickshot/Trickshot'
const SHOTS = 'C:/Users/evrik/downloads/Trickshot/Trickshot/docs/cup-build/shots'

const REPORT = {
  type: 'object',
  required: ['summary', 'filesEdited', 'compileClean', 'editorClean', 'beatsVerified', 'beatsFailing', 'screenshots', 'notesForNextAgents', 'openIssues'],
  properties: {
    summary: { type: 'string' },
    filesEdited: { type: 'array', items: { type: 'string' } },
    compileClean: { type: 'boolean' },
    editorClean: { type: 'boolean', description: 'no errors in the Unity console after the final refresh' },
    beatsVerified: { type: 'array', items: { type: 'string' } },
    beatsFailing: { type: 'array', items: { type: 'string' } },
    screenshots: { type: 'array', items: { type: 'string' } },
    notesForNextAgents: { type: 'string' },
    openIssues: { type: 'array', items: { type: 'string' } },
  },
}

const p = args || {}
const FILES = (p.reportFiles || []).join('\n  ')

phase('Editor verify')
const r = await agent(`
You are the in-editor verifier for the "Trickshot Cup" mode in the Unity game at ${REPO}. The Unity editor (6000.4.1f1) is OPEN and the "UnityMCP" MCP server is connected to this session; load its tools with ToolSearch ("select:mcp__UnityMCP__manage_editor,mcp__UnityMCP__execute_code,mcp__UnityMCP__read_console,mcp__UnityMCP__refresh_unity" and ReadMcpResourceTool for mcpforunity://editor/state). You may edit any file under ${REPO}/Assets/Scripts/Cup/ and make small listed seam fixes elsewhere.
FIRST read: ${NOTES}, ${REPO}/${DOC}, ${REPO}/CLAUDE.md (especially "Verifying UI in the editor", "Menu scene panels" and the IMGUI rules), and the phase reports (JSON, read each "result" entry's publicApi/notes/openIssues):
  ${FILES}
Then read every file under Assets/Scripts/Cup/ so you know the phase machine, the screens and the round driver.

HARNESS RULES (learned the hard way in this project):
- Unity does NOT see externally written files by itself. After ANY edit: stop play mode first (a domain reload in play throws unrelated FlexNet errors), then refresh_unity with mode=force and compile=request, or execute_code \`UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();\`, then wait until mcpforunity://editor/state reports ready_for_tools and is_compiling false; if external_changes_dirty was already true before your write, refresh a SECOND time. A console error whose line number does not match the file on disk is a STALE compile, not a real error.
- execute_code compiles with CodeDom (C# 6) by default: no \`out var\`, no local functions, no string interpolation edge cases; pass compiler "roslyn" if you need newer syntax.
- Screenshots: call \`UnityEngine.ScreenCapture.CaptureScreenshot(path)\` in ONE execute_code call, then in a SEPARATE call (it fires at end of frame) do nothing / wait, then Read the PNG (the Read tool shows images). Save every screenshot under ${SHOTS}/ (create the dir) with a beat name: 01-fork.png, 02-nation.png, 03-bracket.png, 04-loading.png, 05-cointoss-overlay.png, 06-cointoss-flip.png, 07-intro.png, 08-armed-penaltycam.png, 09-whistle-raise.png, 10-live.png, 11-verdict.png, 12-scored-window.png, 13-walkback.png, 14-keeping.png, 15-decided.png, 16-stage-complete.png, 17-knockedout-or-podium.png ... Also capture a free-kick round (wall, spot band) in a second run.
- Navigate the REAL game by reflection: manage_editor play; the main menu is a MenuUI component: set its private phase / call its private Choose(GameMode.TrickshotCup) (set enabled=false first as the real button does; see the "Unity menu screenshot check" pattern: each screen's private callbacks _onPicked/_onDone/_onPractice/_onStart etc.). SP chain: Choose -> StadiumSelectUI -> SpeciesSelectUI -> CustomizeUI -> CupSetupUI (pick Penalties via its callback) -> the cup launches with CupDirector.Instance. Then drive the cup through its PUBLIC intents on CupDirector.Instance (PickNation(index), the bracket auto-advances after 5 s, CallCoin, SkipCelebration, SetReady(true) ...) and read its state (Phase, PhaseTime, Driver.Phase, ScoreA/ScoreB, KickIndex, Kicker, Players). To take a kick as the human you cannot press keys: instead drive the round with code - e.g. find the driver's SetPieceTaker/ball and launch a shot via the same BallController.LaunchSetPiece call the bot uses, or temporarily let the AI take the human's kick by calling the driver's auto-launch path if exposed (add a small \`[UnityEngine.Scripting.Preserve] public void DebugAutoKick()\` to CupRoundDriver if nothing exists - keep it, it is useful). Let the AI kicks happen naturally. Use \`System.Threading.Thread.Sleep\` never; instead poll state across separate execute_code calls (each call is one frame-ish; the editor keeps running between calls in play mode).
- Read the console (errors + exceptions with stack traces) after every step; every exception is a finding you fix.

SCOPE - THE OWNER'S INSTRUCTION: use the editor ONLY to check the CHOREOGRAPHED ANIMATIONS. Do not spend time judging menus, HUD layout, screens, pause behaviour or leaks (other agents review those in code). You still have to drive through the menus to reach a round, but do not evaluate them. Exceptions thrown anywhere on the way ARE yours to fix, because they block reaching the animations.

THE ANIMATED BEATS TO VERIFY (design doc sections in brackets), each with a screenshot (or two, mid-motion) that you actually look at and judge:
1. Coin toss ceremony [7.1]: the referee walks to the penalty spot, the captain(s) stand 1.2 m either side facing him, the coin leaves his hand on an arc (about 1.4 s, spinning), lands about 1 m in front of him with one small bounce and settles flat, face up; the wide shot keeps all bodies and the landing spot in frame. 2. Whistle raise [7.1]: the referee's right forearm comes to his mouth over 0.4 s BEFORE the whistle sound, holds through it, and drops after; he stands 3 m to the side of the ball spot facing the taker and never touches play. 3. Placement [7.3]: taker at the spot with a 3 m run-up facing the goal, keeper on the line, lineup bodies at the box edge 0.62 m apart with arms round each other's shoulders (end bodies one arm), swaying but fixed in place. 4. AI taker walk [7.3]: the AI shooter walks from its lineup mark to the run-up mark, takes the kick, walks back. 5. Scored window [7.4]: the scorer can run and emote for 5 s (drive an emote via the body's Celebration.Play from code and screenshot it), then the 0.3 s cut puts them back in the lineup. 6. Walk-back [7.5]: after a miss/save the shooter turns and walks to its lineup slot at about 1.6 m/s with the two-shot camera (1.5 s low tracking, then the wide shot from behind the lineup), snapping only if 3.5 s elapse. 7. Dejection trio [7.6]: on a losing miss the shooter does one of: knees + face in hands; hands on hips head down; arms on head then falls straight onto its back under gravity (verify the fall actually happens and the arms stay on the head); the losing lineup dejects too; 4 s. 8. Win beat [7.7]: the winning side's bodies are freed and can move/emote for 5 s while the AI lineup dejects. 9. The two-bodies swap [7.3]: on the role swap the parked body is never visible on camera (no popping in frame). 10. Free kicks: the 4-man wall hops on the strike and the AI shooter's walk works from the free-kick spot too.
For each beat: screenshot mid-motion (several frames apart if needed), look at it, judge against the spec, and FIX the choreography/pose/camera code when it is wrong (pose angles, marks, timings, missing steps, bodies popping, a keeper that never dives because of an outSign mistake), then re-run that beat. Iterate until every beat passes or you have a precise open issue you could not fix. Stop play mode when done and leave the editor error-free. Be thorough: this is the first time any of this code has run.
Return the structured report: beats verified/failing, screenshots (paths), files edited, open issues with exact symptoms.`, { label: 'editor verify Solo', phase: 'Editor verify', schema: REPORT })

log(`Editor verify done: compile=${r && r.compileClean} editor=${r && r.editorClean} verified=${r ? r.beatsVerified.length : 0} failing=${r ? r.beatsFailing.length : 0}`)
return r
