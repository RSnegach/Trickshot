export const meta = {
  name: 'cup-editor-endings',
  description: 'Trickshot Cup phase 3b: check the podium, trophy lift and vignette animations in the Unity editor',
  phases: [
    { title: 'Editor verify endings', detail: 'podium orbit + emotes + static losers, trophy lift cinematic, the menu vignette' },
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
    editorClean: { type: 'boolean' },
    beatsVerified: { type: 'array', items: { type: 'string' } },
    beatsFailing: { type: 'array', items: { type: 'string' } },
    screenshots: { type: 'array', items: { type: 'string' } },
    notesForNextAgents: { type: 'string' },
    openIssues: { type: 'array', items: { type: 'string' } },
  },
}

const p = args || {}
const FILES = (p.reportFiles || []).join('\n  ')

phase('Editor verify endings')
const r = await agent(`
You are the in-editor verifier for the ENDINGS of the "Trickshot Cup" mode in the Unity game at ${REPO}. The Unity editor is OPEN and the "UnityMCP" MCP server is connected; load its tools with ToolSearch ("select:mcp__UnityMCP__manage_editor,mcp__UnityMCP__execute_code,mcp__UnityMCP__read_console,mcp__UnityMCP__refresh_unity" and ReadMcpResourceTool for mcpforunity://editor/state). You may edit any file under ${REPO}/Assets/Scripts/Cup/ and Assets/Scripts/Play/MenuScenes/CupScene.cs, plus small listed seam fixes.
FIRST read: ${NOTES}, ${REPO}/${DOC} (sections 8.1, 8.2, 3.1), ${REPO}/CLAUDE.md ("Verifying UI in the editor", "Menu scene panels"), and the phase reports (JSON; read each "result" entry's publicApi/notes/openIssues - the previous editor pass's report tells you the harness tricks that worked, reuse them):
  ${FILES}
Then read CupPodium.cs, CupTrophy.cs, CupConfetti.cs, CupTrophyLift.cs, CupPoses.cs, CupCameraRig.cs, CupDirector.Solo.cs and MenuScenes/CupScene.cs.

HARNESS RULES: stop play mode before any refresh; refresh_unity mode=force compile=request after edits (or AssetDatabase.Refresh(ForceUpdate) + CompilationPipeline.RequestScriptCompilation()), wait for ready_for_tools; a second refresh if external_changes_dirty was already true; stale line numbers = stale compile. execute_code is CodeDom C# 6 by default (compiler "roslyn" for newer syntax). Screenshots via ScreenCapture.CaptureScreenshot(path) in one call and Read the PNG after a separate call; save under ${SHOTS}/ as 30-podium-*.png, 40-trophylift-*.png, 50-vignette-*.png. Read the console after every step; exceptions are yours to fix.

SCOPE - the owner's instruction: use the editor ONLY to check the choreographed ANIMATIONS. Do not evaluate menus, HUD or screens. Reaching the animations may require driving through them; exceptions on the way are yours.

HOW TO REACH THE BEATS QUICKLY (do not play a whole cup): enter play mode, navigate the SP chain by reflection to the cup (MenuUI Choose(TrickshotCup) with enabled=false first, the stadium/species/customize callbacks, CupSetupUI's pick), pick a nation, then FORCE the state: on CupDirector.Instance build the bracket, then use CupSim/Bracket APIs (SimulateAiRounds/RecordResult/AdvanceStage or SimulateRest) from execute_code to make the human the champion and call the director's podium entry (SetPhase(CupPhase.Podium) / the method the Solo partial uses after a won Final - read it) so CupPodium builds. For the trophy lift, construct the Co-op scene directly: CupTrophyLift.Begin(...) needs a team of bodies - build 3 bodies with CupBodies helpers under a temporary root (or reuse a round's bodies by starting a round, then calling Begin at RoundPhase.Over) and run it. For the vignette: stop, return to the main menu, set MenuUI's phase to SinglePlayer by reflection, wait ~3 s for the sub-stages, set menu.enabled=false, stage.SetHover(null) then SetHover(GameMode.TrickshotCup), wait 2-3 s, screenshot (the memory notes and CLAUDE.md describe this exact harness).

THE ANIMATED BEATS TO VERIFY:
1. Podium [8.1]: the dais at the penalty spot (1.6 m across, 0.6 m high, gold trim), the winner on it in the nation kit holding the trophy in the LEFT hand (trophy about 0.45 m, gold, attached to the forearm, not floating/clipping badly), the TrophyLift pose (both arms up) re-playing when it ends, the curated emote page working (play three of them from code and screenshot: the trophy must stay in hand and the body must not leave the dais), the losers (seven in Solo) in a horseshoe facing the dais, static, looking down, in the three poses, in their nation kits; the slow orbit camera (radius 6, height 2.2), the mouse-drag takeover for 4 s then resume; confetti falling in the two kit colours for about 20 s and recycled; fanfare/celebration audio call fired (check the log or AudioSource).
2. Trophy lift [8.2]: the team jogs to the centre circle, the AI nation walks off, the referee claps, the captain receives the trophy under a cut and lifts it, teammates cheer on staggered starts, confetti, the camera arc low-front to high orbit, then the free window with everyone movable; total about 14 s.
3. Vignette [3.1]: on hover the panel plays a penalty: run-up, strike low into a corner, the keeper DIVES (if he never moves, the outSign is wrong - fix it), the ball hits the net, the taker fist-pumps, hold; mouse-off resets to the initial pose; a small gold trophy sits at the side of the frame; the framing fits the panel.
For each beat: screenshot mid-motion, judge against the spec, FIX the code when wrong (marks, poses, trophy offset, camera numbers, timing, keeper sign), re-run. Iterate until every beat passes or you have a precise open issue. Stop play mode when done and leave the editor error-free (run the Roslyn compile check too: bash docs/compile-check.sh).
Return the structured report.`, { label: 'editor verify endings', phase: 'Editor verify endings', schema: REPORT })

log(`Endings editor verify: compile=${r && r.compileClean} editor=${r && r.editorClean} verified=${r ? r.beatsVerified.length : 0} failing=${r ? r.beatsFailing.length : 0}`)
return r
