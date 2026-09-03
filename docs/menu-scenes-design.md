# Live mode-panel scenes (Single Player / Multiplayer submenus)

Design for pass 2 of the submenu rework. Pass 1 (done, uncommitted) broke "Other Modes" into per-mode
buttons on the Multiplayer hub. This pass turns both submenus into large panel buttons, each showing a
LIVE ragdoll scene from that mode, rendered into the button.

## Behaviour (the user's spec)

- Title ("SINGLE PLAYER" / "MULTIPLAYER") at the top of the screen, Back at the bottom.
- Each mode is a large panel: mode label at the top of the panel, the scene below it.
- Scenes: Striker = jump, bicycle kick, land on the back. Goalkeeper = keeper dives and saves a shot.
  Match = a striker slide-tackles the ball away from another striker. Accuracy = a free kick hits a
  pop-up target. Free Kick / Set Pieces = a free kick over a defensive wall.
- Live physics, but ONLY the hovered panel simulates. Unhovered panels show a frozen first frame.
  Hover starts the scene from its initial pose; it plays once and then holds. Mouse-off resets it to
  the initial pose "as if it hasn't been run". A hover outline is drawn around the hovered panel.
- Up to 6 panels per page; more pages via on-screen arrows and Left/Right or A/D (from pass 1).
- Panels must be big enough to read the scene.

## Decision: live, not pre-rendered

Live wins here: the one-at-a-time rule makes the cost one ragdoll scene (13 bodies x 2 actors) plus one
small render per frame; the frozen panels cost nothing (kinematic bones, no per-frame render). Pre-rendered
flipbooks would need an external capture pipeline, go stale with every cosmetic/physics change, cannot show
the player's own look, and are fixed-resolution on a UI that rescales from 0.62x to 2.1x. The protagonist of
every scene is built with the player's roster look (jersey, skin, cosmetics), forced to the Human species so
the choreographies (authored for the biped) stay reliable.

## Constraints that shape the mechanism (from the codebase)

- One global PhysX world, NO layers or culling masks anywhere. Isolation is spatial. Freeze is per body
  (`ActiveRagdoll.BecomeDisplayBody` / `BecomeLiveBody`) and per ball (`Rigidbody.isKinematic`).
- `SimConfig.GoalCenter` is `static readonly (0,0,17)`. `KickDetector`'s bicycle bonus and `DefensiveWall`'s
  facing read it. `Goalkeeper` has a 4-arg `Init(rag, ball, goalCenter, outSign)`; `AccuracyTarget.Spawn`,
  `Crosser.ServeNow`, `BallController.LaunchTo` take explicit points. `SimConfig.AttackGoalCenter` is mutable.
- `MenuBackground` (the title reel) is ALIVE on the Single Player submenu (same MenuUI object) at the real
  goal, and sets global `Time.timeScale = 0.7`, `fixedDeltaTime = 0.014`, `KeeperAbility = 0.6`,
  `BallSpeedMul = 1.5`, restored in its OnDestroy (end of frame). It is DEAD on the Multiplayer hub.
- Directional lights are global; MenuBackground parks every enabled one at Setup and adds its own key + fill.
- The only RenderTexture precedent is the editor gallery: disabled camera, `targetTexture = rt; Render()`.
- IMGUI: virtual coordinates under `MenuScale`; hover = `rect.Contains(Event.current.mousePosition)`; gate
  one-shot state changes on `EventType.Repaint`; controls must be created unconditionally in a fixed order;
  RT pixel size comes from `MenuScale.ToScreen(rect)` and changes on resize / UI Scale slider.
- Drivers are owner-ticked and each `Tick` begins with `ClearPoseOverrides`; one poser per body per frame.
- Materials from `Make.*` and RenderTextures are not freed with their GameObjects: track and Destroy.
- `HairSim` has no teleport handling: after `ResetTo` the hair whips for a fraction of a second.

## Mechanism

### Stage placement
All scene sub-stages sit ON THE Z AXIS at large negative z: sub-stage i at `S_i = (0, 0, -3000 - 120 i)`,
its goal line at `S_i + (0,0,20)` with the mouth facing -Z, play area between. Because x = 0 exactly, the
direction from any stage point to the real `SimConfig.GoalCenter` is +Z, i.e. toward the sub-stage's own
goal, so `KickDetector` and `DefensiveWall` behave without modification. `SimConfig.AttackGoalCenter` is
pointed at the live sub-stage's goal while a scene is live and restored after. Both full-screen cameras
have far = 900 and sit within ~50 m of the origin; PlayerPreview (1000,0,1000) and CosmeticGallery
(2000,0,2000) are far off-axis. The stage camera's far plane (70 m) never reaches a neighbouring sub-stage.

### MenuSceneStage (MonoBehaviour, parented under the owning UI GameObject)
- `Setup(GameMode[] modes)`: creates the stage camera (disabled, clear Skybox = the global menu sky, no
  AudioListener, never a GameCamera), then queues one sub-stage build per frame (ground slab with turf +
  box lines, goal frame + FlexNet + backstops, the scene's actors and props). No lights of its own: on the
  SP submenu MenuBackground's key/fill light the world; on the MP hub the gameplay sun is back.
- Per scene: a `RenderTexture` sized from the panel's inner rect in device pixels (reallocated when the
  size changes, stills re-rendered), a `still` flag, and a settle counter.
- `SetHover(int index)` (called from the owner's OnGUI on Repaint only): on change, the old scene gets
  `Reset` + `Freeze` and keeps rendering for ~0.5 s of settle frames (hair, net), the new scene gets
  `Thaw` (BecomeLiveBody, ResetTo, ball live, clock = 0). While a scene is live the stage sets
  `Time.timeScale = 0.7` / `fixedDeltaTime = 0.014` (slow-mo like the reel), `KeeperAbility = 0.6`,
  `AttackGoalCenter = stage goal`. Every global is saved AT THE MOMENT IT IS SET and restored
  CONDITIONALLY (only if the current value is still the one this stage wrote), so the SP submenu's
  MenuBackground (which owns the same globals, and whose OnDestroy runs at end of frame) and this stage
  can be torn down in either order without leaking slow-mo or ability into a match.
- `Update`: ticks the live scene's choreography. `LateUpdate`: renders the live scene (and any settling
  scene) into its RT via `cam.targetTexture = rt; cam.Render()`. Stills are rendered once after build and
  after each reset+settle.
- `OnDestroy`: restores globals, frees RTs and every tracked Material.

### MenuScene (abstract, one subclass per scene)
`Build(stage, origin)`, `Reset()` (ForceRecover / Knock.Cancel / ResetTo / ball.ResetTo / props), `Freeze()`,
`Thaw()`, `Tick(dt)`, `Frame(out camPos, out lookAt, out fov)`, `Done` (holds after finishing), `Destroy()`.
Shared helpers: body build (player look via `BuildScaled` + appearance forced Human; AI looks via `Build`),
ball build, a `ScriptedInput : IStrikerInput` with edge derivation (NetInputSource idiom), the run-up jog
(`RunGait` from MenuBackground), goal + net + backstop build (MenuBackground.BuildScene parameterised).

Scenes (initial geometry; tuned in play mode):
- **Striker**: striker (player look, `Striker` + `KickDetector`s) 8 m out with his BACK to the goal; an
  orange crosser at the side lofts the ball (`ball.LaunchTo` exact time of flight) to a point above and in
  front of the striker; scripted input: jump when the ball is 0.35 s out, `Scroll = -1` for 3 airborne
  frames (lean back past 55 deg arms the bicycle window), hold one leg after take-off; contact while
  `TrickActive` sends the ball goalward; a tipped landing (upness < 0.6) tumbles him onto his back. Camera
  side-on, elevated, goal in frame. Holds after ~3.5 s.
- **Goalkeeper**: keeper (player look, gloves, `Goalkeeper` AI via the 4-arg Init) on the line; orange
  crosser jogs 3 m and rifles a flat shot at a corner 1.2-1.6 m high, 2 m off centre (the mid band, a full
  layout dive). Camera three-quarter from behind the shooter. Holds after ~2.5 s.
- **Match** (slide tackle): red AI dribbler (`Striker` + `Dribble` with a scripted forward input, the
  human dribble system captures and pushes the ball) runs across; the tackler (player look) sprints in
  and commits the slide (both click edges + both legs held + `Move.y > 0.35`); on contact (slider sliding,
  flat distance < 1.7 m) replicate `MatchGame.TrySlideTackle` deterministically: `victim.Knock.Fell(dir)`,
  `slider.Knock.Fell(dir)`, release the carrier, `ball.KickTo(fwd * 4.5 + up * 0.4, sliderRag)`. Camera
  side-on. Holds after ~3 s.
- **Accuracy**: taker (player look) jogs 2.5 m and strikes (Crosser swing + code launch) at one of three
  `AccuracyTarget`s in the goal mouth; the hit target pops and hides. Camera behind the taker, elevated.
  Reset re-spawns the targets. Holds after ~2.5 s.
- **Free Kick / Set Pieces**: taker (player look) jogs and lofts over a 4-man `DefensiveWall` (built
  once; `TriggerJump` when the ball leaves, `Ground()` on reset) into the top corner past a yellow AI
  keeper. Camera behind-side of the taker. Holds after ~2.5 s.

### Panel widget (UITheme.ScenePanel)
Panel plate -> RT drawn in the inner rect (`GUI.color` white, `GUI.DrawTexture`) -> mode label at the top
(crisp Heavy) -> hover outline (a coloured `Frame` overload; `FrameTex` is white so `GUI.color` sets it) ->
invisible `GUI.Button` last. Returns clicked; the owner tracks the hovered index on Repaint.

### Grid (shared by MenuUI.DrawSinglePlayer and MultiplayerHubUI)
`ModeGridUI` (plain class owned by the screen): title at top, up to 6 panels per page in up to 3 columns
(a short last row is centred), Back centred at the bottom, arrows + keys when pages > 1. Panels size off
the real canvas (`MenuScale.Width/Height`) between the title band and the Back row, aspect clamped so the
scene stays readable. The screen owns a MenuSceneStage for the modes on the current page.

## What the build changed against this plan

The design above is the plan; these are the corrections the implementation and the review forced.

- **No pitch, no goal, no sky.** The panels show the figures and the ball only, on a transparent
  clear. Each sub-stage keeps an INVISIBLE floor collider (the ragdoll's ground probe, balance and
  FloorRescue all need one) and, where a struck ball would otherwise fly off, an invisible catcher.
- **The stages sit at POSITIVE z** (3000 + 120 per scene), not negative. `BallController`'s
  under-the-crossbar cap solves its goal plane as `Sign(ballVz) * Abs(AttackGoalCenter.z)`, so a
  negative-z stage silently disables the clamp.
- **Freezing disables components.** `BecomeDisplayBody` only makes bones kinematic;
  `ActiveRagdoll.FixedUpdate`, `HairSim` and `AnatomySim` keep running regardless, so `Freeze` also
  sets `enabled = false` on the ragdoll, the hair and anatomy sims and the ball.
- **Reset runs on hover-ENTER as well as exit**, before `Thaw`. A freshly built scene has never had
  a reset, and a scene frozen mid-settle would otherwise resume from wherever its bodies stopped.
- **The keeper's `outSign` is -1, not +1.** `Goalkeeper` reads a ball as incoming only when it
  travels AGAINST the out direction (`closing = ballVz * -out`). With +1 the shot never registered
  and he stood still through every attempt - the single hardest bug in this pass to see, because
  nothing errors: he simply never reacts.
- **The keeper's shot is SOLVED, not guessed.** He dives only when the offset beats his dead band,
  which is `KeeperStrafeSpeed * lerp(0.45, 2.0, ability) * 0.55 * timeRemaining`. At ability 0.6
  that is about 4.2 m/s of reach, so a 2.4 m offset needs a flight under ~0.5 s or he just steps.
- **The striker's scroll is gated on frames since the jump, not `!IsGrounded`.** The ground probe
  keeps reading grounded through the first part of the rise, which ate most of the airborne window.
- **`Striker.IgnoreAcrobat`** is new: the scene's whole payoff is the back-landing, and a player who
  owns the Acrobat perk would otherwise get the other branch and land on his feet.
- **`DefensiveWall.BuildFacing`** is new: both existing overloads take the ball-to-goal direction
  from the readonly `SimConfig.GoalCenter`, which from a far stage points backwards and put the wall
  behind the taker.
- **Cameras are fitted, not hand-placed.** `MenuScene.FitCamera` solves a camera that contains a
  world-space box for the panel's aspect, so framing survives a scene's geometry moving.
- **Ambient is pinned around each render.** It is a global the menu does not own (the title reel's
  sky ambient on one screen, the customize preview's raised value on another), and left alone the
  same figure renders correctly on one submenu and blown white on the other.
- **Material leaks fixed at the source** in `AccuracyTarget`, `DefensiveWall`, `BallController` and
  `ActiveRagdoll` (gloves, slick physics material), since a scene cannot reach those private fields.

## Verification plan
Roslyn compile (`docs/compile-check.sh`), then editor: refresh, play mode, drive the screens by
reflection, hover a panel by feeding `SetHover` directly, capture the RT / screen every few frames to
PNGs, and tune each choreography until it reads. Console must stay error-free. Check both submenus
(different timescale/lighting owners) and the resize/UI-scale path.

NOTE for anyone testing: `MenuUI.OnGUI` latches hover from the REAL mouse every frame, so a
`SetHover` driven by reflection is overwritten immediately. Disable the `MenuUI` component first,
then drive `SetHover` directly.
