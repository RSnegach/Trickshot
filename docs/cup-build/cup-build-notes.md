# Trickshot Cup - builder notes (codebase facts verified 2026-09-03)

Read this whole file before touching code. It is the condensed output of four deep code explorations.
Line numbers are approximate (files drift); search for the symbol. The design spec is
`docs/trickshot-cup-design.md` in the repo - it is the source of truth for BEHAVIOUR; this file is the
source of truth for WHAT EXISTS. `CLAUDE.md` at the repo root has the project rules (read it too).

## Ground rules for every builder

- Repo: `C:/Users/evrik/downloads/Trickshot/Trickshot`. Unity 6000.4.1f1, C# 9 (langversion 9.0: no
  file-scoped namespaces, no records with primary ctors beyond C# 9, no `is not` patterns beyond C# 9).
- Compile check WITHOUT the editor: `bash docs/compile-check.sh` from the repo root (about 7 s). It prints
  errors and `exit=0` when clean. Run it after every file you finish. Never return with errors.
- Do NOT try to open Unity or use Unity MCP: the editor is not connected in this session. Compile-clean +
  code review is the verification bar. Do not run `graphify`.
- Scripts are CRLF. After you create or edit a `.cs` file, normalise it:
  `sed -i 's/\r$//; s/$/\r/' <file>` (run once per file at the end of your work).
- No `Date.now`-style nondeterminism in gameplay logic that peers must agree on; everything seeded comes
  from `SeededRng`.
- Namespace: EVERY script is inside `namespace Trickshot { ... }` (157 files) or `namespace Trickshot.Net`
  (the 13 files under Assets/Scripts/Net). Put all cup code in `namespace Trickshot` and add
  `using Trickshot.Net;` when you touch net types.
- `NetWriter` has `U8, U32, F, B, Str, V3, V2, Bytes(byte[]), Col`; `NetReader` has `U8, U32, F, B, Str,
  V3, V2, Bytes(), Col, More`. There is no U16 (write a ushort as U32 or two U8s).
- IMGUI rules (learned the hard way): every control must be allocated on EVERY OnGUI pass (never `return`
  early out of a half-drawn panel; disable with `GUI.enabled` rather than skipping); `MenuScale.Begin()`
  at the top and `MenuScale.End()` on EVERY exit path; fire navigation callbacks AFTER `MenuScale.End()`;
  use `UITheme.Label/Button/Toggle/Shadowed`, never raw `GUI.Label`; large text via `UIFont.Heavy(style)`;
  cache `GUIStyle`s in statics when drawn over a running match; `UITheme.ClickBlocker(w,h,keep,keep2)`
  must be called unconditionally for a modal.
- Materials/meshes you create at runtime must be destroyed on teardown (materials from `Make.*` and
  `MeshGen` meshes are NOT freed with their GameObjects). Use `GeneratedMeshOwner` for meshes on
  cosmetic pieces (`Cosmetics.Piece` attaches one) or track and `Destroy` explicitly.
- Global mutable statics you borrow (`SimConfig.KeeperAbility`, `WallCount`, `GoalWidth/Height`,
  `BallSpeedMul`, `StrikerMoveSpeed`, `PenaltyMode`, `SetPiece*`, `AttackGoalCenter`,
  `SkillTree.MaxShootingOverride`, `PlayerProfile.UniformBodyOverride`, `HumanKeeperSpeedMul`) must be
  restored in `OnDestroy`.
- Every file you own is listed in your task. Do not edit files owned by another agent running in
  parallel with you. If you MUST touch a shared file, make the smallest possible edit and say so in
  your report.

## Mode registration (existing)

- `Assets/Scripts/Play/MenuUI.cs:11` `public enum GameMode { Striker, Goalkeeper, Accuracy, FreeKick, Match, SetPieces }`
  - APPEND new values at the end (the byte rides the wire in `MatchConfig.mode`).
- SP grid list: `MenuUI.SoloModes` (`MenuUI.cs:~237`). MP grid list: `MultiplayerHubUI.NetModes`
  (`Assets/Scripts/Play/MultiplayerHubUI.cs:19`).
- Mode name: `PauseMenu.ModeName(GameMode)` (`Assets/Scripts/Play/PauseMenu.cs:~88`); `default` returns
  "Match" - add a case.
- Wire/advert word: `NetSession.ModeWord(GameMode)` (`Assets/Scripts/Net/NetSession.cs:~724`), `ModeLabel()`
  (~705) and `LabelIsMode(label, mode)` (~730, whole-word prefix match). `SessionBrowserUI.ApplyFilter`
  (`SessionBrowserUI.cs:~120`) filters on `LabelIsMode`.
- Vignette: `MenuSceneStage.Create(GameMode)` (`Assets/Scripts/Play/MenuScenes/MenuSceneStage.cs:~129`)
  switch returning a `MenuScene` subclass; `default: return null` (panel draws without a picture).
  Scene files: `StrikerScene, KeeperScene, MatchScene, AccuracyScene, FreeKickScene, DeadBallScene`,
  `ScriptedInput.cs`. `MenuScene` (`MenuScene.cs:18`) abstract members: `Build()`, `Tick(float dt)`,
  `Reset()`, `Frame(out Vector3 camPos, out Vector3 lookAt, out float fov)`; virtuals `Freeze/Thaw/Destroy`;
  helpers `BuildPlayerBody(name,pos,rot,bool)`, `BuildAiBody`, `BuildBall`, `BuildFloor`, `FitCamera`.
- GameBootstrap (`Assets/Scripts/Sim/GameBootstrap.cs`): SP chain `ShowMainMenu(126) -> ShowStadiumSelect(400)
  -> AfterStadium(427) -> ShowSpeciesSelect(445) -> ShowCustomize(453) -> ShowPrematch(469)` where Accuracy
  forks to `ShowAccuracyModePick()` (475) else `ShowPrematchPanel(mode)` (491), `onStart: BuildMode(m)`.
  Predicates: `UsesCustomPlayer` (415), `CustomizeSkipsSkill` (418), `PicksSpecies` (425).
  `BuildMode(GameMode)` (578): creates `_matchRoot` (586), `PauseMenu` (595-606), sets
  `SimConfig.AttackGoalCenter = SimConfig.GoalCenter` (615), forces statics for Accuracy challenge
  (631-638, BEFORE `Arena.Build` because the goal frame is built at the current size), `Arena.Build(root,
  boundaryWalls:false)` (646), `PitchBuilder.Build` (650), `StadiumBuilder.Build` (651), `Crowd.Create`
  (652), ball (655-662), `GameCamera` (664); net dispatch `BuildNetStrikerMode` (668), `BuildNetSetPieces`
  (675), `BuildNetAccuracy` (682); SP switch (688-698). `BuildFreeKickMode` (965-987) is the reference for
  building a dead-ball SP mode: `BuildStrikerPlayer(root, ball, out striker, out ragdoll, out dribble)`,
  `dribble.Enabled=false; dribble.SetPieceActive=true`, `BuildAiKeeper(root, ball, out keeperRagdoll)`
  (returns null when `KeeperAbility <= 0.001`), `gameCam.Init(cam, ball.transform, ragdoll.Pelvis.transform,
  null, arena.goalCenter)`, `gameCam.SetFollow(...)`, `striker.SetCameraYaw(() => gameCam.Yaw, () => gameCam.Pitch)`.
  MP chain: `ShowMultiplayerHub(177) -> ShowHostOrFind(187) -> ShowHostSetup(197) -> ShowHostStadium(211)
  -> ShowLobby(257) -> StartNetworkedMatch(316-386)` (reads `s.Config`, applies statics, `BuildMode`).
  Pause closures (595-606): `onLeave = Multiplayer.IsClient ? LeaveNetworkedMatch : null`, `onRestart =
  Multiplayer.IsActive ? null : () => RestartMatch(mode)`, `onFullSetup = Multiplayer.IsActive ? null :
  () => ReturnToMatchSetup(mode)`; `pauseGo.AddComponent<PauseMenu>().Init(ReturnToMainMenu, onFullSetup,
  GetInput(), onLeave, onRestart, mode)`. `ReturnToMainMenu` (523): `TearDownMatch(); Multiplayer.End();
  ShowMainMenu()`. `TearDownMatch` (509) destroys `_matchRoot` (which owns the `NetPump`), resets
  timeScale, `AudioManager.EndMatch()`. `RestartMatch` is deferred a frame (`_restartPending`).
  Every `Show*` = `new GameObject("XUI")` + `AddComponent<XUI>().Init(callbacks)`; callbacks `Destroy(go)` first.
  `GameBootstrap.AutoStart` sets `Application.runInBackground = true`. `OnApplicationQuit` flushes
  `AtomicFileWriter`.

## Menus / UI kit

- `MenuScale` (`Assets/Scripts/Play/MenuScale.cs`): design canvas 1280x760, one uniform factor
  (0.62..2.1). `Begin()`/`End()` (8-deep matrix stack, nestable), `Width`, `Height`, `ToScreen(Rect)`.
  Coordinates inside Begin/End are VIRTUAL; `Event.current.mousePosition` is already virtual.
- `UITheme` (`Assets/Scripts/Play/UITheme.cs`): palette `Ink, Dim, Faint, Gold, Blue, Red, Green`; tints
  `SelTint, WarnTint, BadTint, GoodTint` (multiply, >1 to brighten). Widgets: `Fill(Rect, Color)`,
  `Disc(Rect, Color)` (flat circle - use for pips), `Dot(cx,cy,col,rad=3)` (lit dot + halo; only small),
  `Glow(Rect, Color)`, `Shadow`, `Panel(Rect, Color? accent=null, bool shadow=true, float alpha=1)`,
  `Chip(Rect, Color body, Color? edge=null)` (rounded square), `Frame(Rect, Color?)`, `FrameOutline(Rect,
  Color)`, `Shadowed(Rect, string, GUIStyle, Color, float shadowAlpha=0.7f, float off=2f)`, `Title(Rect,
  string, int fontSize=54, Color? rule=null, bool showRule=true)`, `Section(Rect, string)`, `Divider(x,y,w)`,
  `Hint(Rect, string, TextAnchor=MiddleCenter)`, `PulseHint`, `Label(Rect, string[, GUIStyle])`, `Spinner(Rect,
  Color)`, `Bar(Rect, float t01, Color lo, Color hi)`, `Button(Rect, string, GUIStyle, bool bad=false)`,
  `Tease(Rect, string, GUIStyle)`, `Toggle(Rect, string, bool on, GUIStyle, Color? tint=null)`,
  `ModeCard(...)`, `ScenePanel(Rect, string title, Texture scene, bool hot)`, `SceneRect(Rect)`,
  `ClickBlocker(w,h,keep,keep2)`, `Scrim(w,h, float tint=0.55f, float discW=760f, float disc=0.5f, float top=-1f)`.
  Hover reads from plate + `Glow(Gold @ 0.10)`; no accent bars. `Skin`/`Install()` (called by `MenuScale.Begin`).
- `UIFont.Heavy(GUIStyle)` for anything above ~20pt.
- `Hud` (`Assets/Scripts/Play/Hud.cs`): `W`/`H`, `Begin()/End()`, `P PanelStart(string title, int stats)`
  (232 wide at 14,14; rows 23px) + `Stat(ref P p, string key, string val)`, `Clock(seconds, urgent)`,
  `Scoreboard(homeName, homeCol, homeScore, awayScore, awayName, awayCol, float seconds=-1, bool urgent=false,
  string sub=null)` (400x52 top centre), `Flash(text, alpha, sub=null)` (pill at top), `Banner(big, sub,
  hint)` (520x200 centred, accent from `KindOf`), `KindOf(text)` classifier with an ORDERED rule list
  (failures first: StartsWith("STRIKE"), contains "NO GOAL"/"MISSED"/"ALL OUT"/" IS OUT" -> Bad; then
  informational: StartsWith("CROSS:"), contains "REPLAY", StartsWith("TIE")/"GAME OVER"/"+" -> Neutral;
  then "EPIC" -> Epic; then good; then bad), `Legend(line)`, `Meter(t01, label)`, `Card(Rect, header,
  accent)`, `PageDots(cx,cy,pages,current)`, `Seg(Rect, label, on)`, `SlotColor(slot)`, `WorldToGui(world,
  out gui)`, `PlayerMarker(body, col)`, `Star(cx,cy,r,col)` (private), `Scrim(alpha)`.
  Styles: `_flash` 22 Heavy, `_bannerBig` 46 Heavy, `_bannerSub` 22 Gold, `_score` 30 Heavy, `_title` 14 Gold.
- `SetupPanel` (`Assets/Scripts/Play/SetupPanel.cs`): `PanelW 480, RowH 52, HeadH 78, FootH 18`,
  `Height(rows, goalPicture=true)`, `Origin(panelH)`, `Begin(x,y,panelH,title,reset)` (returns first row y),
  `GoalRow(editor, x, ref row, ref width, ref height, ref keeperLevel, locked=false, yesNo=false)`,
  `Map(...)`. Back/Start pin to the SCREEN: `by = MenuScale.Height - 72`, `bw 170`, `edge 24`, h 48,
  fontSize 22 bold; Start uses `GoodTint` (see `PrematchUI.DrawNav` ~L321).
- `PrematchUI` (`Assets/Scripts/Play/PrematchUI.cs`): `LadderPicker(lx, ref row, lw, label, string[] names,
  int cur, int perRow=0, int fontSize=13)` (public static) is THE ladder picker; `RowLabel()`,
  `RowValue()`, `PickStyle(sel)`, `EndRow(lx, ref row, lw)` are private statics (copy the numbers).
- `GoalEditor` (`Assets/Scripts/Play/GoalEditor.cs`): `ContentH 280`, `Draw(Rect p, ref float width, ref
  float height, ref int keeperLevel, bool framed=true, bool locked=false, KeeperRow keeperRow=Ladder)`.
  `GoalSetup.Apply(width, height, keeperLevel)` (259-281) pushes to SimConfig and rebuilds the goal.
- `AccuracyModeUI` (`Assets/Scripts/Play/AccuracyModeUI.cs`, 91 lines): the two-card fork template.
  `Init(onPractice, onChallenge, onBack)`; cards 380x110 gap 22; `static bool Card(Rect r, string title)`
  draws Panel + hover glow + title 40 Heavy and returns `GUI.Button(r, GUIContent.none, GUIStyle.none)`
  allocated LAST; a `System.Action fire` deferred callback fired after `MenuScale.End()`.
- `StadiumSelectUI.Init(onPicked, onBack, goalPanel, goalW, goalH, keeperLevel)`; rows pick AND advance.
- `CustomizeUI` (`Assets/Scripts/Play/CustomizeUI.cs`, 2049 lines): stages `Body, Skill, Name, Jersey`;
  `Init(onDone, onBack)`; `SkipSkill` flag; name field 12 chars. `DesignPicker` (~1574) is the jersey
  grid: swatch 52x66, `JerseyDesigns.Thumb(d)`.
- `JerseyDesigns` (`Assets/Scripts/Play/JerseyDesigns.cs` + `JerseyDesigns.Nations1..10.cs`):
  `enum DesignTab { Nations, ClassicKits, Patterns, Bold }`; `class Design { string Name; DesignTab Tab;
  Action<Color32[]> Apply; }`; `IReadOnlyList<Design> All`, `InTab(DesignTab)`, `Texture2D Thumb(Design)`
  (48x48 cached, point filter). 214 Nations designs, sorted A-Z, keyed by NAME string. Batches are found
  by reflection: methods named `BuildNationsBatchN(List<Design>)`. Primitives like `VTriband`, `HTriband`,
  `NordicCross`, `Saltire` draw flags into a 256x256 region. Atlas: `W=256, RegionH=256, Scale=2,
  AtlasW=512, AtlasRegionH=512, AtlasH=1032, BackY0=0, FrontY0=512, PlainY0=1024`. There are NO flag
  textures; the thumb IS the flag badge. To paint a kit on a body: `Make.MatTex(tex)` on the torso, or
  `ActiveRagdoll.SetTorsoMaterial(mat)` (finds the child named "v").
  Full nation name list (214): Afghanistan, Albania, Algeria, Andorra, Angola, Antarctica, Antigua and
  Barbuda, Argentina, Armenia, Aruba, Australia, Austria, Azerbaijan, Bahamas, Bahrain, Bangladesh,
  Barbados, Belarus, Belgium, Belize, Benin, Bermuda, Bhutan, Bolivia, Bosnia and Herzegovina, Botswana,
  Brazil, Brunei, Bulgaria, Burkina Faso, Burundi, Cabo Verde, Cambodia, Cameroon, Canada, Catalonia,
  Central African Republic, Chad, Chile, China, Colombia, Comoros, Congo (DR), Congo (Republic), Cook
  Islands, Costa Rica, Cote d'Ivoire, Croatia, Cuba, Cyprus, Czechia, Denmark, Djibouti, Dominica,
  Dominican Republic, Ecuador, Egypt, El Salvador, England, Equatorial Guinea, Eritrea, Estonia, Eswatini,
  Ethiopia, European Union, Faroe Islands, Fiji, Finland, France, Gabon, Gambia, Georgia, Germany, Ghana,
  Gibraltar, Greece, Greenland, Grenada, Guatemala, Guinea, Guinea-Bissau, Guyana, Haiti, Honduras, Hong
  Kong, Hungary, Iceland, India, Indonesia, Iran, Iraq, Ireland, Israel, Italy, Jamaica, Japan, Jolly
  Roger, Jordan, Kazakhstan, Kenya, Kiribati, Kosovo, Kuwait, Kyrgyzstan, Laos, Latvia, Lebanon, Lesotho,
  Liberia, Libya, Liechtenstein, Lithuania, Luxembourg, Madagascar, Malawi, Malaysia, Maldives, Mali,
  Malta, Marshall Islands, Mauritania, Mauritius, Mexico, Micronesia, Moldova, Monaco, Mongolia,
  Montenegro, Morocco, Mozambique, Myanmar, Namibia, Nauru, Nepal, Netherlands, New Zealand, Nicaragua,
  Niger, Nigeria, North Korea, North Macedonia, Northern Ireland, Norway, Olympic, Oman, Pakistan, Palau,
  Panama, Papua New Guinea, Paraguay, Peru, Philippines, Poland, Portugal, Pride Rainbow, Puerto Rico,
  Qatar, Romania, Russia, Rwanda, Saint Kitts and Nevis, Saint Lucia, Saint Vincent and the Grenadines,
  Samoa, San Marino, Sao Tome and Principe, Saudi Arabia, Scotland, Senegal, Serbia, Seychelles, Sierra
  Leone, Singapore, Slovakia, Slovenia, Solomon Islands, Somalia, South Africa, South Korea, South Sudan,
  Soviet Union, Spain, Sri Lanka, Sudan, Suriname, Sweden, Switzerland, Syria, Taiwan, Tajikistan,
  Tanzania, Thailand, Timor-Leste, Togo, Tonga, Trinidad and Tobago, Tunisia, Turkey, Turkmenistan,
  Tuvalu, USA, Uganda, Ukraine, United Arab Emirates, Uruguay, Uzbekistan, Vanuatu, Vatican City,
  Venezuela, Vietnam, Wales, Yemen, Zambia, Zimbabwe.
- `PauseMenu` (`Assets/Scripts/Play/PauseMenu.cs`): `static bool Paused`; `Init(onMainMenu, onMatchSetup=null,
  input=null, onLeave=null, onRestart=null, GameMode? mode=null)`; `BuildEntries()` (~203) builds the list
  in order Resume / Restart / Match Setup / Settings / Leave Match / End Match|Main Menu / Quit; `Entry`
  has `Label, Act, Kind {Normal,Bad}, ConfirmTitle, ConfirmBody`. `DrawConfirm` (357) is the confirm
  card (440x200). `GameCamera.LateUpdate` early-outs when `PauseMenu.Paused`. Check what else reads
  `Paused` (grep) before adding an overlay mode: end cards return early on it, `MatchGame`/drivers may stop.
- `PauseMatchSetup.RowsFor(GameMode)` (`Assets/Scripts/Play/PauseMatchSetup.cs:~48`) - return 0 for the cup
  so `HasLiveSettings` is false.
- Text input: `GUI.TextField(rect, text, maxLen, style)`; handle Enter/Esc BEFORE the field draws
  (`QuickChatFeed.cs:~187`). `PlayerProfile.PlayerName` (static, in-memory).
- `CareerStatsUI` (`Assets/Scripts/Play/CareerStatsUI.cs`): `enum Cat {...}`, `CatName`, `RowsFor(cat)`
  returning `(label, sp, mp)[]`, `DrawRows(x,y,w,rows)` 28px rows + 6 gap. Panel 700x650.
- `LobbyUI` (`Assets/Scripts/Play/LobbyUI.cs`): w 560; `DrawFlatRoster` (rowH 30), `DrawMatchTeams`
  (cellH 46), tabs at 188-190; START MATCH host-only gated on `_s.AllReady()`; `_onStart` from
  `NetSession.MatchStarting`. `ShowLobbyCustomize` exists in GameBootstrap (278/302).
- `HostSetupUI` (`Assets/Scripts/Play/HostSetupUI.cs`): panel w 480; `panelH = mode == Accuracy ?
  SetupPanel.Height(2) : 470 + (Match ? 58 : 0) - 116`; `PickerVals` rows +58 each; `Toggle` +40;
  `Create()` (~187-246) builds `MatchConfig`, `Multiplayer.Host(maxPlayers)`, checks `Session.Active`,
  `SetConfig`. `LookingForRow` is Match-only.
- `SessionBrowserUI` (`Assets/Scripts/Play/SessionBrowserUI.cs`): w 560, 6 rows of 46; `ApplyFilter` ~120.
- `InviteFriendsUI` static class drawn last by LobbyUI (modal precedent).
- `QuickChatFeed` (`Assets/Scripts/Play/QuickChatFeed.cs`): `Bind(NetSession)`, `Draw()`, `Typing`,
  `static AnyOpen`, `EscapeOwned`; drivers add it with `gameObject.AddComponent<QuickChatFeed>()`.

## Dead-ball gameplay (existing parts to reuse)

- `SetPieceTaker` (`Assets/Scripts/Play/SetPieceTaker.cs`): `enum State { Idle, Charging, Runup, Struck,
  Settle }`; `Begin(IStrikerInput input, ActiveRagdoll ragdoll, BallController ball, Vector3 ballSpot,
  Vector3 goalCenter, bool displayOnly=false, float combinedOverride=-1f, Func<Vector3> aimPoint=null, int
  leftFootedOverride=-1, bool chargeWithLegs=false, Action<Commit> launch=null, bool dualAxisSpin=false,
  float meterRate=-1f, bool meterHoldAtMax=false, float curlChargeMul=1f)`; `Reset()`; `Tick()`;
  `IsCharging`, `HasCharged`, `Meter`; `static Vector3 LookAimPoint(Vector3 from, float yaw, float pitch,
  float goalPlaneZ)`. `combinedOverride >= 0` forces the skill stat (0..1); `<0` uses the local profile.
  `_awaitingRelease`: if Space is already held when an attempt arms, charging waits for a release.
  The taker owns the ball for the attempt (`SetColliders(false)`), launched by code via
  `BallController.LaunchSetPiece(power01, spin, spinCharge, botch, combined, goalCenter, overcharge,
  powerStat)` - see `NetSetPieceMatch.AutoLaunch` (~1026-1039) for the AI call:
  `_ball.IgnoreBody(b.ragdoll, true); spin = random of {None, CurveLeft, CurveRight, TopSpin};
  _ball.LaunchSetPiece(Random.Range(0.55f,0.8f), spin, Random.Range(0.4f,0.9f), 0f, Clamp01(combined),
  SimConfig.AttackGoalCenter, 0f, Clamp01(combined))`. The AI aim model: corner picked by spin flavour,
  `combined` pulls the aim from centre toward the corner (`BallController.LaunchSetPiece` ~L386-410).
  There is NO autonomous AI taker with a run-up today; `AutoLaunch` fires from a standing body. A bot that
  drives `SetPieceTaker` through a synthetic `IStrikerInput` gets the real charge/run-up/strike.
- `IStrikerInput` (`Assets/Scripts/Input/IStrikerInput.cs`): small interface (move, look, buttons, `Scroll`,
  `EmoteId`, ...). Read it fully before implementing `CupBotTaker`. `GameInput` implements it for the
  local device; `NetInputSource` (`Assets/Scripts/Net/NetInputSource.cs`) implements it from `InputFrame`s.
  `GameInput.CaptureCursor(bool)` is the single cursor owner. `GameInput.SetEmotePick(int)` / `EmoteId`.
- `BallController` (`Assets/Scripts/Play/BallController.cs`): `SetPieceShot` flag, `ResetTo(pos)`,
  `IgnoreBody(ragdoll, bool)`, `Speed`, `Rb`, `LaunchSetPiece(...)` (~309), `LaunchTo`. Its under-crossbar cap
  uses `SimConfig.AttackGoalCenter` (mutable static; set to `SimConfig.GoalCenter` for the real goal).
- Goal test (copy it): `c.z - r >= goalLineZ && c.z <= goalLineZ + SimConfig.GoalDepth && Abs(c.x) <= halfW - r
  && c.y >= r && c.y <= SimConfig.GoalHeight - r` (`FreeKickGame.BallFullyInGoal` ~310, `NetSetPieceMatch.BallInGoal` ~1336).
- Attempt state machine constants (both drivers): `KickSpeed 2.5` (ball speed that marks the kick taken),
  `RestSpeed 0.7`, `RestHold 0.6 s`, `MaxLiveTime 6 s`, `ResetDelay 1.4`; out of play: `c.y < -3 || |c.x| >
  FieldWidth || |c.z| > FieldLength`. Verdict order: goal -> save touched -> wall touched -> miss.
- `SaveWatch` (`Assets/Scripts/Play/SaveWatch.cs`): `Arm()`, `Disarm()`, `Poll(ball, keeperRagdoll,
  keeperHighDive)`, `Touched`, `Callout(allowEpic)`.
- `DefensiveWall` (`Assets/Scripts/Play/DefensiveWall.cs`, plain class): `Build(root, ballPos, wallCenter,
  count)`, `BuildFacing(root, wallCenter, shotDir, count)`, `TriggerJump()`, `Tick()`, `Ground()`,
  `Clear()` (frees its 3 materials - call before rebuild and on teardown). `ShoulderSpacing 0.62`,
  `HopHeight 0.7`, `HopDuration 0.6`. Wall touched detection: see how `FreeKickGame` sets `_wallTouched`.
- Spot generator: `SetPieceMap.RandomSpot(System.Random rng)` (`Assets/Scripts/Play/SetPieceMap.cs:~153`):
  band 16.5 m (box front) to ~35 m, x within min(HalfWidth-2, 24). The cup wants 17-28 m: write its own
  `CupSpots` helper using `SeededRng` (same shape, narrower band), or clamp.
- `SimConfig` (`Assets/Scripts/Sim/SimConfig.cs`): `GoalCenter` (static readonly (0,0,17)), `AttackGoalCenter`
  (mutable), `GoalWidth/GoalHeight` (+`GoalWidthBase 7.32`, `GoalHeightBase 2.44`), `GoalDepth`, `BallRadius`,
  `PenaltyBoxDepth` (16.5), `KeeperStart`, `KeeperPenaltyStart`, `KeeperFaceDir`, `PenaltyMode`,
  `FreeKickDistance 20`, `WallCount 4`, `WallDistance 9.15`, `SetPiecePlaced/SetPieceBallSpot/
  SetPieceWallCenter/SetPieceRandomSpots`, `KeeperAbility` (0..1, default 0.5), `AiLevelNames {None,Easy,
  Normal,Hard,Insane}`, `AiLevelAbility {0,0.15,0.30,0.55,0.80}`, `NearestAiLevel(f)`, `KeeperClaimMinAbility
  0.30`, `HumanKeeperSpeedMul`, `AccuracyKeeperHandicap`, `BallSpeedMul`, `StrikerMoveSpeed`,
  `ReplayWindow 4`, `ReplaySlowMul 0.36`, `ReplayHold 1.3`, `NetSnapshotInterval 0.05`, `NetInterpDelay 0.1`,
  `CamDistance 6.2`, `ReplayCam*`, `SitDrop 0.72`, `MoonwalkGlideSpeed`. `PitchLayout.PitchLength`,
  `PitchLayout.HalfWidth`. `SimConfig.ResetToDefaults` exists (~1665).
- `Goalkeeper` (`Assets/Scripts/Play/Goalkeeper.cs`): AI keeper, `Init(rag, ball, goalCenter, outSign)`,
  `Tick()`, `ResetTo(pos)`, `Park()/Unpark()` at ability <= 0.001, reads `SimConfig.KeeperAbility` each Tick
  (249). `IsHighDive`. Dead band: `KeeperStrafeSpeed * lerp(0.45,2.0,ability) * 0.55 * timeRemaining`.
- `KeeperController` (`Assets/Scripts/Play/KeeperController.cs`): the human keeper. `Init(input, ...)`,
  `SetLookYawSource(Func<float>)`, `InputLocked`, `ForceRecover()`, `Tick()`. Reset idiom:
  `NetSetPieceMatch.ResetHumanKeepers()` (~538-546): unlock input, `ForceRecover()`, `ragdoll.ResetTo(
  SimConfig.KeeperStart, LookRotation(KeeperFaceDir))`.
- `KeeperHands` (`Assets/Scripts/Play/KeeperHands.cs`): claim/parry; `CanClaim(ability)`.
- `Striker` (`Assets/Scripts/Play/Striker.cs`): `Init(input, ragdoll)`, `ControlEnabled`, `ForceRecover()`,
  `SetCameraYaw(Func<float> yaw, Func<float> pitch)`, `IgnoreAcrobat`. `KickDetector` attach: see
  `NetSetPieceMatch.AttachKick` (~389-400) which adds a `KickDetector` per `StrikeBone`.
- `ActiveRagdoll` (`Assets/Scripts/Ragdoll/ActiveRagdoll.cs`): `Build(Vector3 basePos, Quaternion facing,
  Material torsoMat, Material limbMat, bool withGloves=true, PlayerAppearance? appearance=null)` (306);
  `BuildScaled(...)` (292); `appearance == null` = AI body (no cosmetics). Gloves + keeper hitboxes are
  BAKED at Build (`withGloves`) - a shooter body cannot become a keeper. `Pelvis`, `Phys(Bone)`,
  `FacingRotation`, `ResetTo(pos, facing)` (1505), `BecomeDisplayBody()` (1529: all 13 bones kinematic),
  `BecomeLiveBody()` (1546), `DisplaySnap(pos, facing)`, `DisplayPose(basePos, facing, rootPitch, rootRoll,
  Vector3[] boneEuler)` (1758, FK puppet - needs BecomeDisplayBody first), `DisplayEmote(pos, facing,
  emoteId, phase)` (1612), `DisplayAnim`, `SetPose(Vector3[] pose, float rate)`, `SetPoseOverride(Bone,
  Vector3)`, `AddPoseOverride`, `ClearPoseOverrides()`, `EmoteHeightOffset`, `UprightLock`,
  `BalanceEnabled`, `LocomotionEnabled`, `MoveInput`, `BodyOrientTarget`, `SnapFacing`, `AddGlove(Bone)`
  (442-465: child at localPosition (0,-0.19,0) of the forearm = the hand point), `RegisterExtraCollider`,
  `IgnoreOwnCollisionsWith`, `SetTorsoMaterial(Material)`, `RegisterCosmeticMaterial`, `IsGrounded` (flickers;
  gate on edges). `Bone` enum (`RagdollPose.cs:6`): Pelvis, Torso, Head, ThighL, ThighR, CalfL, CalfR,
  FootL, FootR, UpperArmL, UpperArmR, ForearmL, ForearmR, Count=13. NO hand bone; hand point =
  `forearm.position + forearm.rotation * Vector3.down * 0.31f`. `RagdollPose.Stand/Load/Bicycle/Sit/Slide`
  static pose arrays. Idle idiom (`Footballer.cs:~845`): `SetPoseOverride` per bone then `SetPose(Stand, 5f)`.
  Human forearm layout: parent UpperArm, rest (∓0.26, 1.08, 0), capsule r 0.045 len 0.30.
- `PlayerAppearance` (`Assets/Scripts/Sim/PlayerProfile.cs:275-308`): `SpeciesId, Skin, HairStyle, HairColor,
  FacialStyle, FacialColor, Accessory, AccessoryColor, Adult, MemberLen, MemberGirth, BallSize`; `Default`.
  Style indices are species-reinterpreted. `PlayerProfile` is a static in-memory class: `PlayerName`,
  `Number`, `LeftFooted`, `JerseyTex/JerseyPng/JerseyBase`, `Appearance`, `Height/Weight`,
  `UniformBodyOverride` (static bool), `HeightScale`, `MassMul`. `SkillTree.MaxShootingOverride` (static bool).
- `Celebration` (`Assets/Scripts/Play/Celebration.cs`): `enum Emote { FistPump, KneeSlide, Backflip, Wave,
  TPose, Griddy, Bow, PushUps, Robot, Dab, Floss, Clap, Salute, HeartHands, Shrug, MuscleFlex, Point,
  Sprinkler, HandsUp, Facepalm, Charleston, Cheer, Twirl, Disco, Thinker, Twerk, FishFlop, Moonwalk, Wave2,
  Crip, Vibe, Kick, Slide2 }` - APPEND ONLY (wire ids). `Pages` (4 wheel pages of (Emote, name)), `Menu`.
  `Init(ActiveRagdoll)`, `Play(Emote)` (guard with `if (!celeb.Playing)` or `Cancel()` first - Play
  snapshots control flags), `Playing`, `CurrentEmote`, `Progress01`, `Cancel()`, `DurationFor(e)` (default
  1.6 s, one-shot, no loop). Physics emotes: Backflip, KneeSlide, FishFlop, Moonwalk. `EmotePose.Apply(
  Emote, float p, Action<Bone,Vector3> set)` and `EmotePose.RootLift(Emote, p)` (static, side-effect free)
  - add pose cases for new emotes in the same switch style (`Facepalm` at ~599 is the dejection precedent:
  head +22 down, hand to face, torso +10 forward). `ClampArms` abducts arms away from the torso.
  Wheel UI: `MatchGame.DrawEmoteWheel` (~515) / `NetStrikerMatch.DrawEmoteWheel` (~915): rad 210, buttons
  132x42, `Hud.Scrim(0.55)`, `Hud.PageDots`; `SetWheelOpen(bool)` -> `GameInput.CaptureCursor(!open)`.
  Keybind "Emote" default B (`Keybinds.cs:34/66`); `GameInput.EmotePressed/EmoteHeld`.
  Net: `InputFrame.emoteId` (255 none) uplink; `BodyState.emoteId/emotePhase` downlink; client applies
  `ragdoll.DisplayEmote(pos, facing, emoteId, phase)` (`NetMatch.cs:~655`). `NetSetPieceMatch.BroadcastSnapshot`
  hardcodes emoteId 255 - the cup must fill it.
- `MeshGen` (`Assets/Scripts/Sim/MeshGen.cs`): `Lathe(Vector2[] profile (radius,y bottom->top), int seg=32,
  bool smooth=true, float startDeg=0, float sweepDeg=360)`, `Cylinder(r1, r2, height, seg=24, capBottom,
  capTop)` (about +Y, base at y=0), `Torus(R, r, segRing=32, segTube=12, arcDeg=360, capEnds=true)`,
  `Tube(path, radius, seg, capStart, capEnd)`, `Extrude(outline, thickness, bevel, bulge)`, `Disc(centre,
  normal, radius, seg)`, `Combine(params Mesh[])` (DESTROYS inputs), `Transform(mesh, pos, rot, scale)` (in
  place), `Param(...)`, `Superellipse`, `Flat`.
- `Make` (`Assets/Scripts/Sim/Make.cs`): `Mat(Color, smoothness=0.1, metallic=0)`, `Transparent`,
  `MatTex(tex, smoothness)`, `Unlit`, `Glow(Color)`, `Box(name, size, pos, mat, parent=null, collider=true)`,
  `Cylinder(name, radius, length, pos, axis, mat, parent, phys)`, `Sphere(name, diameter, pos, mat, parent)`,
  `Capsule`, `Empty(name, pos, parent)`, `PhysMat(...)`. Gold = `Make.Mat(new Color(0.85f,0.70f,0.30f),
  0.85f, 0.75f)` (Cosmetics' private `Gold()`); share ONE material per prop set.
- `Cosmetics.Piece(Transform parent, Mesh mesh, Material mat, bool castShadows=true)`
  (`Assets/Scripts/Sim/Cosmetics.Hair.cs:~52`): collider-less child "cz" with `GeneratedMeshOwner`. Do NOT
  use `PieceAt` (head-girth scaled). Bake offsets with `MeshGen.Transform`. `GeneratedMeshOwner`
  (`Cosmetics.cs:~491`) frees a mesh (and `Tex`) on destroy.
- `GameCamera` (`Assets/Scripts/Play/GameCamera.cs`): `enum Mode { Follow, Broadcast, KeeperFollow }`;
  `Init(Camera cam, Transform ball, Transform striker, Transform crosser, Transform goal)`; `SetFollow(
  target, Func<Vector2> look, Func<float> scroll=null)`; `SetKeeperFollow(target, Func<Quaternion> facing,
  Func<Vector2> look, Func<float> scroll=null)`; `SetMode(Mode)`; `Yaw`, `Pitch`, `KeeperLookYaw`,
  `LookDirection()`, `FreezeLook`, `TriggerSlowMo`, `PulseBallCam`, `ToggleBallCam`. `Broadcast` =
  auto vantage from `GroupCenter()` until the user moves the mouse (then orbit + wheel zoom), FOV 46;
  NO auto orbit. `LateUpdate` early-outs when `PauseMenu.Paused`; `UpdateSlowMo` owns `Time.timeScale`;
  `OnDisable` resets timeScale. Follow: FOV 58, offset `rot * (0,0,-CamDistance)`, SmoothDamp 0.08.
  KeeperFollow: cone yaw ±`KeeperLookYawLimit`, pitch ±`KeeperCamLookPitch`, camera `pivot - fwd*5.5 +
  up*(3.0*GoalHeight/2.44)`, FOV 60. A custom camera (podium, coin toss, penalty cam) is simplest as its
  own MonoBehaviour that sets the Camera transform in LateUpdate with `[DefaultExecutionOrder]` after
  GameCamera, or by disabling GameCamera while it runs (restore on exit, plus timeScale discipline).
- `ReplaySystem` (`Assets/Scripts/Play/ReplaySystem.cs`): `static TrackBody(List<Transform> tracked,
  List<MonoBehaviour> drivers, ActiveRagdoll rag)`, `Setup(tracked, drivers, float windowSeconds)`,
  `Play(float slowMul)`, `Stop()`, `IsPlaying`. Records pos/rot/scale per FixedUpdate. Re-run Setup after
  any tracked body is rebuilt. Net: `NetSession.BeginReplay()`, `VoteSkip()`, `ReplayStarted/ReplayEnded`
  events, `MsgType.ReplayStart/SkipVote/ReplayEnd`. In `NetSetPieceMatch`: `OnReplayStarted` (~803) sets
  `_cam.SetMode(Broadcast)`, `_camTarget = null`, `_replay.Play(SimConfig.ReplaySlowMul)`, `Flash("REPLAY
  (click to skip)")`; host ends the replay when `!_replay.IsPlaying`; LMB -> `_s.VoteSkip()`.
- `AudioManager` (`Assets/Scripts/Sim/AudioManager.cs`, singleton `Instance`): `BeginMatch(GameMode)`,
  `EndMatch()`, `PlayMenuMusic()`, `OnSetPieceGoal(int shooterKey)`, `OnSetPieceMiss(int)`, `PlayGoalCelebration
  (bool cutLively=true)`, `PlayApplauseOnly()`, `PlayBoos()`, `PlayMissBoosMaybe()`, `PlayBallKick(pos)`,
  `PlayPostHit(pos, speed)`, `PlayWhistle()`, `PlayWhistleTriple()`, `Clip(name)` (Resources/Audio/<name>,
  null-safe). `CrowdCheer.Register(crowd)` / `CrowdCheer.Celebrate()`; `Crowd.Celebrate()`.
- `CareerStats` (`Assets/Scripts/Sim/CareerStats.cs`): `[Serializable] class ModeStats` (ints per stat),
  `CareerStatsData { ModeStats SP, MP }`, `Data`, `Save()` (via `AtomicFileWriter.Write(FilePath, json,
  "CareerStats")`), `Record*()` one-liners that `Save()`. JsonUtility: new fields load as default; no
  Dictionary (use `List<T>` of `[Serializable]` rows). `Achievements` (`Assets/Scripts/Sim/Achievements.cs`):
  `AchievementDef { Id, Title, Description, Kind, Target, Func<CareerStatsData,int> CurrentValue }`, `All`
  (EMPTY array), `IsUnlocked(id)`, `CheckAll()` (call right after the Record* that moves a stat).
- `Footballer` (`Assets/Scripts/Play/Footballer.cs`): the outfield AI with gait/locomotion (walk/jog to a
  point) - reuse its movement for "walk to the spot / walk back" if practical, else drive `MoveInput` on
  the ragdoll with `LocomotionEnabled = true` toward a target (see `Celebration.Moonwalk` for the
  `MoveInput = FacingRotation * dir * speed` idiom) and `MenuScene.Jog/RunGait` helpers.
- Menu-scene rules if you build `CupScene`: stages at +Z (`MenuSceneStage.StageZ 3000`, spacing 120), on
  x=0; `Goalkeeper.Init(rag, ball, goalCenter, outSign)` - keeper reads a ball as incoming only when it
  travels AGAINST `outSign`; freeze = `BecomeDisplayBody` + disable `ActiveRagdoll`, `HairSim`, `AnimSim`;
  `DefensiveWall.BuildFacing` for off-pitch stages; materials freed at the source.

## Networking (existing)

- `Assets/Scripts/Net/NetMessages.cs`: `enum MsgType` (22 values, byte-tagged, APPEND): Hello, AssignSlot,
  PlayerInput, Snapshot, MatchEvent, RosterSync, ReadyToggle, StartMatch, ReplayStart, SkipVote,
  ReplayEnd, RequestSlot, ShootoutState, UpdateLoadout, JerseyChunk, BallKick, QuickChat, PostHit,
  MatchStats, NominateJersey, CastJerseyVote, CrosserSetup.
  `struct MatchConfig` (~156-190): mode u8, stadium u8, perSide u8, matchSec, publicLobby, goalScale,
  keeperAbility, fkPlaced, fkBallX/Z, fkWallX/Z, accSuddenDeath, fkRandom, fkSeed (uint), lookingFor,
  goalScaleH. Written by `NetCodec.Roster()` (~578-597) in a FIXED order with retired zero bytes; read by
  `ReadRoster()` (~599-621). Extension rule: append TRAILING fields after `goalScaleH` (before the slot
  count? NO - the slot list follows the config; a trailing config field must be appended after
  goalScaleH and read with `r.More`... BUT the slot loop follows, so `r.More` is always true there.
  => For new config fields either (a) bump `ProtocolVersion` and read them unconditionally, or (b) put
  them in a NEW message. The cup bumps ProtocolVersion to 8 anyway, so (a): write `cupStyle` u8 and
  `cupFormat` u8 right after `goalScaleH` and read them unconditionally.)
  `NetCodec.ProtocolVersion` (~410) = 7 -> 8. Version is checked in `NetSession.GrantSlot` (~1392).
  `NetWriter` (`U8, U16?, U32, F, V2, V3, B, Str, ToArray`) / `NetReader` (`U8, U32, F, V3, B, Str, More`).
  Check exact method names in the file before use. `InputFrame` (~238-250): tick u32, move V2, lookYaw,
  lookPitch, bits, emoteId, bits2. `BodyState` (~256): slot u8, pos, yaw, down, emoteId, emotePhase, anim,
  lastInputTick, erect (fixed stride). `Snapshot`: tick, ballPos/Vel, scores, clock, n + n×BodyState, trailing
  `guided` (read with `r.More`). `ShootoutState` (~194): activeShooter u8, over, scored[8], taken[8].
  `LookingRoles` flags + `Tag/Parse`. `NetRole { Shooter, Keeper, Spectator, Crosser }`.
- `Assets/Scripts/Net/NetSession.cs` (1644 lines): `MaxSlots 8`, `CrosserSlot 7`, `RoleForSlot(slot)` (34,
  no mode parameter - make mode-aware), `RoleOfSlot`, `SlotAllowed(slot)` (~1300-1319: the single seat
  gate; crosser claimable in Striker only), `Host(maxPlayers)` (303-327: host seats itself at slot 1),
  `GrantSlot` (1385-1470), `RequestSlot/ApplySlotRequest`, `SetSlotAi`, `_slotAi`, `AllReady()` (610),
  `SetReady`, `StartMatch()` (618-624: `MatchStarted = true`, sends `Start`, fires `MatchStarting`),
  `MatchStarted` (never cleared today), `HasFreeSlot` (675), `BuildAdvert` (747), `ModeLabel` (705),
  `ModeWord` (724), `LabelIsMode` (730), `SetConfig(cfg)` (330) -> `PushRoster()` (1630) (config + roster
  in ONE reliable message on every roster change - never put big data in it), `Config`, `Roster`,
  `RosterSlot(slot)` (`human, ai, ready, role, name, appearance, ...`), `LocalSlot` (255 = none),
  `IsHost`, `Transport`, `BroadcastEvent(tag)` (855: reliable, fires `MatchEvent` on CLIENTS only - host
  must handle locally), `BroadcastShootout(st)` (939: sets `LatestShootout`, sends, fires locally),
  `BroadcastSnapshot`, `LatestSnapshot/HasSnapshot`, `SetLocalInput(in InputFrame)` (819),
  `ConsumeInputForSlot(slot)` (791: newest frame + sticky presses), `InputTickForSlot`, `ResetSlotInput(slot)`
  (~1263), `ClearSnapshotBuffer()` (1038), `ClearMatchStats()` (929), `RouteMessage(peer, bytes)`
  (1069-1248: the switch; add cases), `IsHostOnly(MsgType)` (1323-1327: ADD every host-authored type),
  `OnPeerLeft` (1340), `RosterChanged` event, `MatchEvent` event, `ShootoutUpdated` event,
  `ReplayStarted/Ended`, `JerseyForSlot(slot)`, `LeftFootedForSlot`, `SendJerseyChunks` (454-478: the
  ReliableBulk chunking precedent), `Transport.SendToAll(bytes, NetChannel)`, `Transport.Send(peer, bytes,
  channel)`, `Transport.HostPeer`, `SlotOf(peer)`, `PeerOf(slot)`? (check). `NetChannel { Reliable,
  Unreliable, ReliableBulk }`. Adding a message type: enum + codec pair + RouteMessage case + IsHostOnly (if
  host-authored) + a public Broadcast/Request method + an event.
- `Assets/Scripts/Net/Multiplayer.cs`: `Session`, `IsActive`, `IsHost`, `IsClient`, `Host(max)`, `Join`,
  `End()`, `Poll()`, `InstallPump()`. `NetPump` (match-scoped pump added by GameBootstrap ~610).
- Transport: `DirectIpTransport` (UDP, ~1.2 KB safe packet), `LocalTransport` (loopback, `UseDirectIp=false`),
  `SteamTransport` stub. `PeerTimeout 5 s`.
- Drivers: `NetSetPieceMatch` (`Assets/Scripts/Play/NetSetPieceMatch.cs`, 1929 lines) is the reference for
  a networked dead-ball driver: `Configure(input, cam, gameCam, ball, goal, torso, limb, glove, root)`,
  `SpawnBody(slot, torso, limb, glove, root)` (279-360: builds a ragdoll per slot; human = own skin +
  `JerseyForSlot`; keeper = withGloves; shooter = `Striker` + `AttachKick`; host remote = `NetInputSource`
  + `SetCameraYaw` from `netInput.LookYaw/LookPitch`; client non-local = `BecomeDisplayBody`), `BeginTurn`
  (551-604), `HostTickAttempt` (1057-1090), `ResolveAttempt` (1094-1143), `HostDriveActiveShooter`
  (957-1021: arms the taker with the right input source; watchdogs `ArmedIdleTimeout 12`, `RunupWatchdog 4`),
  `AutoLaunch` (1026), `HostUpdate` (899-952: feeds remote inputs, ticks ai/keepers, publishes snapshots),
  `BroadcastSnapshot` (1310, `wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw`), client
  `ApplySnapshot`/interp + `ReconcileLocalBody` (1271), `FollowActiveShooter` (876), `OnRosterChanged`
  (484-520: leaver handling), `DrawScoreboard` (1538), `DrawAccuracyResults` (1734-1838: the results-card
  template with `Crown()` scanline polygon and `Figures()`), `Announce(tag)` (1364: BroadcastEvent + local
  Flash), `OnMatchEvent` (402: parses "WHISTLE", "MISS", "ACCOUT:<slot>:<score>"), `LockCursor`.
  `NetAccuracyMatch` (35 lines) is the marker-component pattern. `NetStrikerMatch` and `NetMatch` are the
  other drivers (emote wheel + snapshot emote fields there).
- Tick model: no fixed net tick; `_tick` increments per rendered frame; snapshots by accumulator every
  `NetSnapshotInterval`; client input bundles of 3 every frame; the host drops frames with `tick <=
  _slotInputTick[slot]` (so keep the tick monotonic across rounds, or `ResetSlotInput`).
- There is NO spectator implementation and NO match reset in MP today (`GameBootstrap` nulls Restart/Setup
  when `Multiplayer.IsActive`; `ReturnToMatchSetup` calls `Multiplayer.End()`).

## Persistence

- `AtomicFileWriter.Write(path, text, tag)` (worker thread, temp-then-swap). `Application.persistentDataPath`.

## Cup: fixed decisions (see the design doc for the rest)

- Names: Solo (SP), Head to Head (MP), Co-op (MP). A "round" = one match between two nations; the five
  bracket levels are "stages" (Round of 32, Round of 16, Quarter-finals, Semi-finals, Final). Never use
  the word "tie" in code or UI.
- 5 kicks each alternating, early finish, sudden death pairs; coin toss decides who kicks first; kick clock
  12 s -> existing weak auto-shot; verdict words GOAL / SAVED / MISS (a wall stop is SAVED); no EPIC.
- Stage ramp: keeper ability 0.2/0.4/0.6/0.8/1.0; taker strength t same steps, `combined = Lerp(0.35,
  0.95, t)`, power target `Lerp(0.55, 0.85, t)` (tunables in one place).
- Standardised shooting in every style (`SkillTree.MaxShootingOverride`, `PlayerProfile.UniformBodyOverride`).
- 32 nations always; humans never share a Round of 32 match; novelty kits excluded from the AI pool;
  strength hidden (biases `CupSim` only).
- Timings: bracket 5 s; coin call 5 s timeout -> heads; ceremony ~3 s; loading >= 1.5 s (MP barrier 10 s);
  intro 3 s; whistle raise 0.4 s; scored window 5 s (scorer-only skip, yellow "CLICK TO SKIP" text near the
  bottom); walk-back <= 3.5 s; win beat 5 s (skippable by the scorer / winning keeper); dejection 4 s;
  replay 3 s window at 0.45 (goal or save only), skip = UNANIMOUS among humans with a body in the round,
  shown "CLICK TO SKIP 0/N".
- Pause: freezes only in Solo; overlay in MP (sim keeps running, local input cut, cursor freed).
- Referee in every round (striped kit, whistle raise before every whistle, coin toss ceremony).
- Podium (Solo/H2H) and trophy lift (Co-op) per the doc; losers are static display bodies.
