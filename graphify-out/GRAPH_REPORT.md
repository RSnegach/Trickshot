# Graph Report - Trickshot  (2026-09-02)

## Corpus Check
- 175 files · ~2,735,432 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 4789 nodes · 11635 edges · 225 communities (214 shown, 10 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 1034 edges (avg confidence: 0.82)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `a889f62b`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Cosmetics
- JerseyDesigns
- SimConfig
- Dribble
- SetPieceMap
- DirectIpTransport
- SkillTree
- SkillIcons
- Trickshot
- Transform
- .Piece
- AdultQuiz
- .SlotSubMenu
- CareerStats
- Goalkeeper
- .BuildVenetianMask
- .RouteMessage
- MenuIcons
- PitchLayout
- DirectIpTransport.cs (direct-IP UDP)
- GameBootstrap
- com.unity.modules.jsonserialize
- ActiveRagdoll
- NetSession
- BuildAll
- MatchProbe
- GameCamera
- NetSetPieceMatch
- PauseMenu
- Passing
- NetStrikerMatch
- Bone
- Emote
- KeeperController
- .List
- CustomizeUI
- AnatomySim
- KeeperGame
- NetEndpoint
- com.unity.modules.uielements
- com.unity.modules.physics
- com.unity.modules.imageconversion
- .Lathe
- QuickChatFeed
- CosmeticGallery
- CrossMap
- FreeKickGame
- SetPieceTaker
- HairSim
- CareerStatsUI
- SkyDome
- com.unity.modules.hierarchycore
- NetWriter
- .Mount
- NetMatch
- AccuracyTarget
- dependencies
- com.unity.modules.androidjni
- MatchGame
- IStrikerInput
- Make
- Sniper
- com.unity.modules.imgui
- com.unity.modules.animation
- com.unity.modules.audio
- dependencies
- .AttachAppearance
- UITheme
- .Draw
- graphify knowledge graph
- PrematchUI
- com.unity.ext.nunit
- TailnetDiscovery
- dependencies
- Trickshot (3D trick-shot football prototype)
- KeeperHands
- Multiplayer
- Hair Strand Texture Atlas
- LobbyProbe
- DisplaySettings
- Footballer
- Cosmetics overhaul — execution plan
- NetInputSource
- StatRadar
- Hair Atlas Asset License
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- CrosserControl
- MsgType
- INetTransport
- GameMode
- CareerStatsData
- ReliableChannel
- PlayerProfile
- PlayerPreview
- Hud
- .Label
- Striker
- .Build
- Trickshot.Net
- .Build
- CreditsData
- SurroundBuilder
- .Build
- Category
- .SetPoseOverride
- MenuBackground
- LocalTransport
- BallController
- skyprep.py
- com.unity.nuget.newtonsoft-json
- Turf
- StadiumStyle
- NetMessages.cs
- com.unity.modules.screencapture
- Snapshot
- FlexNet
- com.unity.modules.unitywebrequest
- EmotePose
- AudioManager
- Playlist
- .Box
- BallController.cs
- MenuScale
- .BuildGoal
- grassprep.py
- TitleGlyph
- SteamTransport
- MonoBehaviour
- AssetImportRules
- Phase
- .Place
- .DrawBrowser
- Achievements.cs
- postprep.py
- .Rect
- Striker
- SetPieceSpin
- .DriveTowardRotation
- Trickshot: Replayability Brainstorm
- UIFont
- OtherModesUI
- GameInput
- Crosser
- Achievements
- Touch
- ShotType
- Role
- TackleResult
- Goal
- CrossPathLine
- NotificationToastUI
- horse_mane
- .CaptureCursor
- face_props_jewelry
- AtomicFileWriter
- human_hair
- State
- ShotBand
- .Draw
- CrosserSetupMsg
- hats
- SettingsMenu
- elephant
- StudioSplash
- 0Wsi-ygmiIX
- 14ZGcuiRJ9d
- State
- 1TJPsi4VIT
- 2Givq4Q3YTH
- 2uKEHjO_QL0
- 46Bl5Ook_xw
- 4Tdb1s3-kug
- 6VWnuNVkJ5
- 7fGDqHvHap1
- 7NZp449iJq
- 7VVumyY7L_u
- 8TpZrCG3aRf
- 9i5mmOwt7cu
- 9KxfvGBAxri
- 9SQY3Gsq2s
- 9xOJlCsQzX
- a6B0wtVteV
- aaC5GgcWEhM
- aWxhfEnYwl
- aWzUlZtGLC0
- cKVNEpmNy36
- CxDnECpFJH
- d_AsyX_R-S3
- dAwE-2WVHIt
- DBEk0SMQCt
- dCm3NXrMtSr
- Dz9SyIEq7w
- fNEK0SGJ6D
- fy1Elzr3nl
- j3xPyO1mvt
- jcXfae4GiZ
- jfVp7cW8E5
- lNN3PlrjSa
- LYEp20yfFh
- oc8MPJuSud
- oQtjZCNFoo
- p5QgQxkMBE
- SyNFHIhIDd
- tPrk0HHagr
- WEGNXQAOfy
- WoXpAJT0oD
- WoYlUvyUAb
- XLysBbtilu
- YchMXfQNU0
- yYdsPoULg1
- CallLimiter
- Stage
- eyewear
- horse_markings_tack
- human_facial
- cosmetics-verdicts.md
- Cosmetics/manifest.json
- Comb
- Downloaded cosmetic assets
- Tab
- Trick
- compile-check.sh
- sheet.py

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 210 edges
2. `Cosmetics` - 160 edges
3. `Trickshot` - 144 edges
4. `NetSession` - 143 edges
5. `BallController` - 121 edges
6. `MatchGame` - 102 edges
7. `CustomizeUI` - 85 edges
8. `JerseyDesigns` - 79 edges
9. `GameInput` - 78 edges
10. `NetSetPieceMatch` - 72 edges

## Surprising Connections (you probably didn't know these)
- `PlayerInputManager (local multiplayer seam)` --semantically_similar_to--> `Slot / role model (NetSession.MaxSlots=8)`  [INFERRED] [semantically similar]
  README.md → MULTIPLAYER.md
- `ScrimmageGame` --shares_data_with--> `ActiveRagdoll.cs`  [INFERRED]
  MULTIPLAYER.md → README.md
- `Trickshot Multiplayer Framework` --conceptually_related_to--> `Trickshot (3D trick-shot football prototype)`  [INFERRED]
  MULTIPLAYER.md → README.md
- `Trickshot (3D trick-shot football prototype)` --references--> `Unity 6000.4.1f1 editor version`  [EXTRACTED]
  README.md → ProjectSettings/ProjectVersion.txt
- `CosmeticGallery` --references--> `Camera`  [EXTRACTED]
  Assets/Scripts/DevTools/CosmeticGallery.cs → Assets/Scripts/Play/SettingsMenu.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **graphify CLI commands** — claude_graphify_query, claude_graphify_path, claude_graphify_explain, claude_graphify_update [EXTRACTED 0.85]
- **Hair Atlas License Terms Set** — assets_resources_hair_hairatlas_license_royalty_free_use, assets_resources_hair_hairatlas_license_no_attribution_required, assets_resources_hair_hairatlas_license_no_resale_restriction, assets_resources_hair_hairatlas_license_bundled_license_requirement [EXTRACTED 1.00]
- **Four strand-card tiles compose the hair atlas** — assets_resources_hair_hairatlas_wavy_scattered_strands, assets_resources_hair_hairatlas_flowing_wavy_strands, assets_resources_hair_hairatlas_dense_wavy_strands, assets_resources_hair_hairatlas_straight_sleek_strands, assets_resources_hair_hairatlas_atlas [EXTRACTED 1.00]
- **Interchangeable transports behind INetTransport seam** — multiplayer_inettransport, multiplayer_directiptransport, multiplayer_localtransport, multiplayer_steamtransport [EXTRACTED 1.00]
- **Active-ragdoll bicycle-kick mechanic** — readme_activeragdoll, readme_ragdollpose, readme_kickdetector, readme_jointmath, readme_bicycle_kick [INFERRED 0.85]
- **Host-authoritative frame loop (poll, input, snapshot)** — multiplayer_multiplayer, multiplayer_netsession, multiplayer_netmessages, multiplayer_host_authoritative [INFERRED 0.85]

## Communities (225 total, 10 thin omitted)

### Community 0 - "Cosmetics"
Cohesion: 0.12
Nodes (18): AccessoryEntry, Collider, Color, GameObject, List, Material, Quaternion, Renderer (+10 more)

### Community 1 - "JerseyDesigns"
Cohesion: 0.11
Nodes (24): Action, Color32, Dictionary, IReadOnlyList, List, Texture2D, List, List (+16 more)

### Community 2 - "SimConfig"
Cohesion: 0.06
Nodes (31): AiDifficulty, ScrimPos, Color, Vector2, Vector3, AiDifficulty, Easy, Hard (+23 more)

### Community 3 - "Dribble"
Cohesion: 0.14
Nodes (9): Action, Vector3, Dribble, CaptureRadius, Carrying, CloseControl, Holder, Tightness (+1 more)

### Community 4 - "SetPieceMap"
Cohesion: 0.23
Nodes (10): Color, Random, Rect, Vector2, Vector3, SetPieceMap, BottomZ, HalfW (+2 more)

### Community 5 - "DirectIpTransport"
Cohesion: 0.10
Nodes (16): Action, ConcurrentQueue, data, Dictionary, from, Func, IPEndPoint, List (+8 more)

### Community 6 - "SkillTree"
Cohesion: 0.09
Nodes (16): Dictionary, HashSet, IEnumerable, List, Effect, Node, Preset, SkillTree (+8 more)

### Community 7 - "SkillIcons"
Cohesion: 0.18
Nodes (4): Color32, Dictionary, Texture2D, SkillIcons

### Community 9 - "Transform"
Cohesion: 0.13
Nodes (21): Action, Collider, Color, Func, HairDef, Material, Mesh, MeshFilter (+13 more)

### Community 10 - ".Piece"
Cohesion: 0.10
Nodes (26): Func, HairDef, Color, Func, GameObject, List, Material, Mesh (+18 more)

### Community 11 - "AdultQuiz"
Cohesion: 0.50
Nodes (3): AdultQuiz, Q, Q

### Community 12 - ".SlotSubMenu"
Cohesion: 0.08
Nodes (19): BodyPlan, Biped, Quadruped, HeaderAction, Biped, SlotKind, Skin, StyleA (+11 more)

### Community 13 - "CareerStats"
Cohesion: 0.16
Nodes (5): name, CareerStats, Data, FilePath, min

### Community 14 - "Goalkeeper"
Cohesion: 0.09
Nodes (17): Func, Quaternion, Renderer, Vector3, Band, High, Jump, Low (+9 more)

### Community 15 - ".BuildVenetianMask"
Cohesion: 0.17
Nodes (11): Func, Mesh, Color32, Dictionary, Func, Material, Mesh, Texture2D (+3 more)

### Community 16 - ".RouteMessage"
Cohesion: 0.24
Nodes (5): Color, NetReader, More, Type, BinaryReader

### Community 17 - "MenuIcons"
Cohesion: 0.26
Nodes (4): Color32, Dictionary, Texture2D, MenuIcons

### Community 18 - "PitchLayout"
Cohesion: 0.10
Nodes (19): IEnumerable, Quaternion, Vector3, PitchLayout, AttackGoalLineZ, FarGoalLineZ, HalfWidth, PitchCenterZ (+11 more)

### Community 19 - "DirectIpTransport.cs (direct-IP UDP)"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 20 - "GameBootstrap"
Cohesion: 0.14
Nodes (6): GameObject, Light, RuntimeInitializeOnLoadMethod, Texture2D, GameBootstrap, AudioListener

### Community 21 - "com.unity.modules.jsonserialize"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "ActiveRagdoll"
Cohesion: 0.05
Nodes (39): e, name, Celebration, CurrentEmote, Playing, Progress01, Bounds, BoxCollider (+31 more)

### Community 23 - "NetSession"
Cohesion: 0.04
Nodes (47): appr, PeerId, IsValid, JerseyChunkMsg, LobbySlot, NetRole, Crosser, Keeper (+39 more)

### Community 24 - "BuildAll"
Cohesion: 0.12
Nodes (8): Action, BuildAll, ZipEnabled, Plat, BuildTarget, MenuItem, Plat, Type

### Community 25 - "MatchProbe"
Cohesion: 0.13
Nodes (9): List, Vector3, MatchProbe, Overlay, ProbeTackle, Ai, Human, Slide (+1 more)

### Community 26 - "GameCamera"
Cohesion: 0.11
Nodes (20): Func, Transform, Vector3, GameCamera, BallCam, KeeperLookDownFraction, KeeperLookYaw, Pitch (+12 more)

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.07
Nodes (16): Body, Material, Transform, NetAccuracyMatch, List, Material, ShootoutState, Snapshot (+8 more)

### Community 28 - "PauseMenu"
Cohesion: 0.15
Nodes (9): Action, List, Entry, Kind, Bad, Normal, PauseMenu, Paused (+1 more)

### Community 29 - "Passing"
Cohesion: 0.09
Nodes (13): Bar, List, Vector3, Bar, AnyArmed, Option, Passing, PassKind (+5 more)

### Community 30 - "NetStrikerMatch"
Cohesion: 0.09
Nodes (13): Goalkeeper, ai, Crosser, List, Material, name, slot, Snapshot (+5 more)

### Community 31 - "Bone"
Cohesion: 0.05
Nodes (45): Vector3, BodyLayout, BodyLayoutDef, ParentByBone, BoneSpec, ColliderKind, Box, CapsuleY (+37 more)

### Community 32 - "Emote"
Cohesion: 0.06
Nodes (34): Emote, Backflip, Bow, Charleston, Cheer, Clap, Crip, Dab (+26 more)

### Community 33 - "KeeperController"
Cohesion: 0.12
Nodes (11): Func, Quaternion, Vector2, Vector3, KeeperController, Body, Hands, HasBall (+3 more)

### Community 34 - ".List"
Cohesion: 0.22
Nodes (5): Func, Mesh, Vector2, Vector3, MeshGen

### Community 35 - "CustomizeUI"
Cohesion: 0.08
Nodes (14): Color, Color32, Dictionary, Func, GUIStyle, IEnumerator, Rect, Texture2D (+6 more)

### Community 36 - "AnatomySim"
Cohesion: 0.16
Nodes (11): CapsuleCollider, Collider, Color, GameObject, Material, Transform, Vector3, AnatomySim (+3 more)

### Community 37 - "KeeperGame"
Cohesion: 0.09
Nodes (11): CrowdCheer, KeeperGame, SaveWatch, Armed, Epic, Touched, TouchSpeed, TouchTime (+3 more)

### Community 38 - "NetEndpoint"
Cohesion: 0.21
Nodes (5): IPAddress, IPEndPoint, List, NetEndpoint, Action

### Community 39 - "com.unity.modules.uielements"
Cohesion: 0.12
Nodes (16): dependencies, depth, source, url, version, depth, source, version (+8 more)

### Community 40 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.physics, com.unity.modules.physics

### Community 41 - "com.unity.modules.imageconversion"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 42 - ".Lathe"
Cohesion: 0.31
Nodes (7): GameObject, Material, Mesh, Transform, Vector2, Vector3, Quaternion

### Community 43 - "QuickChatFeed"
Cohesion: 0.06
Nodes (17): Dictionary, ChatCensor, Kind, Gap, Heading, Line, Strong, Sub (+9 more)

### Community 44 - "CosmeticGallery"
Cohesion: 0.14
Nodes (12): GameObject, IEnumerable, IEnumerator, Light, Renderer, Texture2D, Vector3, CosmeticGallery (+4 more)

### Community 45 - "CrossMap"
Cohesion: 0.10
Nodes (24): ai, Color, GUIStyle, List, name, Rect, slot, Vector2 (+16 more)

### Community 46 - "FreeKickGame"
Cohesion: 0.05
Nodes (33): Vector3, AccuracyGame, Phase, Armed, Cooldown, Live, CapsuleCollider, Collider (+25 more)

### Community 47 - "SetPieceTaker"
Cohesion: 0.06
Nodes (27): Action, Func, Quaternion, Vector2, Vector3, Commit, SetPieceTaker, Active (+19 more)

### Community 48 - "HairSim"
Cohesion: 0.08
Nodes (28): List, Material, Matrix4x4, Mesh, MeshFilter, MeshRenderer, Transform, Vector2 (+20 more)

### Community 49 - "CareerStatsUI"
Cohesion: 0.18
Nodes (6): Action, label, CareerStatsUI, Cat, mp, sp

### Community 50 - "SkyDome"
Cohesion: 0.24
Nodes (7): Color, Dictionary, Light, Material, Shader, Texture2D, SkyDome

### Community 51 - "com.unity.modules.hierarchycore"
Cohesion: 0.15
Nodes (13): com.unity.modules.hierarchycore, dependencies, depth, source, version, dependencies, depth, source (+5 more)

### Community 52 - "NetWriter"
Cohesion: 0.15
Nodes (7): PlayerAppearance, Vector3, NetCodec, NetWriter, Vector3, BinaryWriter, MemoryStream

### Community 53 - ".Mount"
Cohesion: 0.08
Nodes (31): Anchor, Collider, GameObject, Material, MeshRenderer, Transform, Vector3, Anchor (+23 more)

### Community 54 - "NetMatch"
Cohesion: 0.10
Nodes (9): StatRow, Bar, Material, Refs, Rigidbody, Transform, Vector3, Body (+1 more)

### Community 55 - "AccuracyTarget"
Cohesion: 0.08
Nodes (16): Transform, Vector3, AccuracyBoard, Count, Action, BoxCollider, Collider, Color (+8 more)

### Community 56 - "dependencies"
Cohesion: 0.06
Nodes (31): com.coplaydev.unity-mcp, com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+23 more)

### Community 57 - "com.unity.modules.androidjni"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.androidjni

### Community 58 - "MatchGame"
Cohesion: 0.07
Nodes (19): Dictionary, Func, HashSet, List, Vector3, MatchGame, AwayScore, ClockRemaining (+11 more)

### Community 59 - "IStrikerInput"
Cohesion: 0.06
Nodes (27): Vector2, IStrikerInput, CloseControlHeld, CrossPressed, EmoteId, Fresh, JumpHeld, JumpPressed (+19 more)

### Community 60 - "Make"
Cohesion: 0.19
Nodes (11): Texture2D, Color, Material, Shader, Texture2D, JerseyFaces, Chest, Flank (+3 more)

### Community 61 - "Sniper"
Cohesion: 0.28
Nodes (6): Action, LineRenderer, Rigidbody, Transform, Vector3, Sniper

### Community 62 - "com.unity.modules.imgui"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.imgui, com.unity.modules.imgui

### Community 63 - "com.unity.modules.animation"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.animation, com.unity.modules.animation

### Community 64 - "com.unity.modules.audio"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.audio

### Community 65 - "dependencies"
Cohesion: 0.17
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 66 - ".AttachAppearance"
Cohesion: 0.12
Nodes (14): BoxCollider, BoxCollider, Func, GameObject, List, Material, Mesh, Texture2D (+6 more)

### Community 67 - "UITheme"
Cohesion: 0.08
Nodes (31): Action, Rect, Rect, Vector2, GUIStyle, Color, GUIStyle, Matrix4x4 (+23 more)

### Community 68 - ".Draw"
Cohesion: 0.20
Nodes (8): Color, GUIStyle, List, MatchStatsUI, Tab, Away, Home, Tab

### Community 69 - "graphify knowledge graph"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - "PrematchUI"
Cohesion: 0.19
Nodes (4): GUIStyle, ScrimPos, Vector3, PrematchUI

### Community 71 - "com.unity.ext.nunit"
Cohesion: 0.17
Nodes (12): com.unity.ext.nunit, com.unity.test-framework, dependencies, depth, source, version, dependencies, depth (+4 more)

### Community 72 - "TailnetDiscovery"
Cohesion: 0.14
Nodes (17): Action, ConcurrentQueue, IPAddress, IPEndPoint, List, Reason, NoCli, NoPeers (+9 more)

### Community 73 - "dependencies"
Cohesion: 0.17
Nodes (12): com.unity.modules.physics2d, dependencies, depth, hash, source, version, dependencies, depth (+4 more)

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "KeeperHands"
Cohesion: 0.25
Nodes (4): Vector3, KeeperHands, HeldFor, Holding

### Community 76 - "Multiplayer"
Cohesion: 0.07
Nodes (18): List, RuntimeInitializeOnLoadMethod, Multiplayer, IsActive, IsClient, IsHost, Session, SteamLinked (+10 more)

### Community 77 - "Hair Strand Texture Atlas"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 79 - "DisplaySettings"
Cohesion: 0.11
Nodes (15): DisplaySettings, Available, CrowdScale, FovOffset, Graphics, UiScale, VSync, GraphicsTier (+7 more)

### Community 80 - "Footballer"
Cohesion: 0.08
Nodes (24): AiTuning, List, Vector2, Vector3, Footballer, IsDown, Keeper, KeeperHoldingBall (+16 more)

### Community 81 - "Cosmetics overhaul — execution plan"
Cohesion: 0.09
Nodes (22): 1. Credits screen, and Options becomes Settings, 2. Removals approved, 3. Scope — groundwork first, then human head, Already built and verified, Assets acquired, Build order, Cosmetics overhaul — execution plan, CrownPatch reads as a bowl cut (+14 more)

### Community 82 - "NetInputSource"
Cohesion: 0.06
Nodes (32): Vector2, NetInputSource, CloseControlHeld, CrossPressed, EmoteId, Fresh, JumpHeld, JumpPressed (+24 more)

### Community 83 - "StatRadar"
Cohesion: 0.36
Nodes (4): Color, Rect, Vector2, StatRadar

### Community 84 - "Hair Atlas Asset License"
Cohesion: 0.60
Nodes (5): Bundled License Inclusion Requirement, Hair Atlas Asset License, No Attribution Required, No-Resale Restriction, Royalty-Free Unlimited Use Grant

### Community 85 - "Kyrgyz Sun Emblem (kyrgyz_sun.png)"
Cohesion: 0.60
Nodes (5): Forty-Ray Golden Sun, Kyrgyz Sun Emblem (kyrgyz_sun.png), Kyrgyzstan Flag Emblem, Team / National Emblem Game Asset, Tunduk (Yurt Crown) Motif

### Community 86 - "Soviet Emblem Sprite"
Cohesion: 0.60
Nodes (5): Hammer and Sickle, Soviet Emblem Sprite, Five-Pointed Star, Team Emblem / Logo, Soviet Union Symbolism

### Community 87 - "CrosserControl"
Cohesion: 0.11
Nodes (14): Collider, Transform, CrosserBubble, Func, Vector3, CrosserControl, Acc01, InStance (+6 more)

### Community 88 - "MsgType"
Cohesion: 0.08
Nodes (23): MsgType, AssignSlot, BallKick, CastJerseyVote, CrosserSetup, Hello, JerseyChunk, MatchEvent (+15 more)

### Community 89 - "INetTransport"
Cohesion: 0.09
Nodes (14): Action, Func, List, INetTransport, AdvertProvider, HostPeer, IsHost, IsRunning (+6 more)

### Community 90 - "GameMode"
Cohesion: 0.25
Nodes (7): GameMode, Accuracy, FreeKick, Goalkeeper, Match, SetPieces, Striker

### Community 91 - "CareerStatsData"
Cohesion: 0.33
Nodes (4): CareerStatsData, ModeStats, OnlineRanks, RankData

### Community 92 - "ReliableChannel"
Cohesion: 0.16
Nodes (8): Dictionary, List, Queue, Pending, ReliableChannel, CumAck, HasUnacked, Pending

### Community 93 - "PlayerProfile"
Cohesion: 0.03
Nodes (59): Color, label, Texture2D, PlayerProfile, AgilityStat, AirFlipMul, Bias, BicycleSkill01 (+51 more)

### Community 94 - "PlayerPreview"
Cohesion: 0.09
Nodes (15): Action, Color, GameObject, Light, Material, Quaternion, Rect, Renderer (+7 more)

### Community 95 - "Hud"
Cohesion: 0.09
Nodes (15): Color, GUIStyle, List, Rect, Vector2, Vector3, Hud, H (+7 more)

### Community 96 - ".Label"
Cohesion: 0.15
Nodes (5): GUIStyle, Vector3, HostSetupUI, Color, LobbyUI

### Community 97 - "Striker"
Cohesion: 0.08
Nodes (17): Cat, Accuracy, FreeKick, Friends, Match, Overall, Rank, Striker (+9 more)

### Community 98 - ".Build"
Cohesion: 0.31
Nodes (9): Collider, Color, Material, PhysicsMaterial, Transform, Vector3, StadiumBuilder, CornerPylonScale (+1 more)

### Community 99 - "Trickshot.Net"
Cohesion: 0.09
Nodes (10): Action, List, SteamFriendInfo, SteamFriendsAPI, Available, List, FriendsPanelUI, GoalSetup (+2 more)

### Community 100 - ".Build"
Cohesion: 0.39
Nodes (5): Collider, Material, Transform, Vector3, PitchBuilder

### Community 101 - "CreditsData"
Cohesion: 0.47
Nodes (3): CreditsData, Entry, Entry

### Community 102 - "SurroundBuilder"
Cohesion: 0.23
Nodes (10): Collider, Color, Material, Texture2D, Transform, Vector3, SurroundBuilder, BowlHalfX (+2 more)

### Community 103 - ".Build"
Cohesion: 0.23
Nodes (12): PhysicsMaterial, Collider, Material, MeshFilter, MeshRenderer, PhysicsMaterial, Renderer, Transform (+4 more)

### Community 104 - "Category"
Cohesion: 0.20
Nodes (10): Category, Agility, Control, Heading, Instinct, Pace, Passing, Shooting (+2 more)

### Community 105 - ".SetPoseOverride"
Cohesion: 0.29
Nodes (4): Vector3, Gait, Profile, Profile

### Community 106 - "MenuBackground"
Cohesion: 0.12
Nodes (14): Collider, Color, Light, List, Material, MeshFilter, MeshRenderer, PhysicsMaterial (+6 more)

### Community 107 - "LocalTransport"
Cohesion: 0.11
Nodes (14): LobbyInfo, Action, data, Dictionary, from, Func, List, Queue (+6 more)

### Community 108 - "BallController"
Cohesion: 0.07
Nodes (21): Collision, Rigidbody, SphereCollider, Vector3, BallController, DribbleCarrier, DribbleHold, Guided (+13 more)

### Community 109 - "skyprep.py"
Cohesion: 0.26
Nodes (12): dir_from_pixel(), find_sun(), lum(), main(), Turn the Poly Haven .hdr pureskies into game-ready equirectangular skybox…, Euler for a directional light whose forward is -d (i.e. shining from d)., Exposure-normalise, roll the highlights off, then gamma-encode to bytes., Decode a Radiance RGBE file to a float32 HxWx3 array of linear radiance. (+4 more)

### Community 110 - "com.unity.nuget.newtonsoft-json"
Cohesion: 0.29
Nodes (7): com.unity.nuget.newtonsoft-json, dependencies, depth, source, url, version, com.unity.nuget.newtonsoft-json

### Community 111 - "Turf"
Cohesion: 0.21
Nodes (5): Color, Dictionary, Material, Texture2D, Turf

### Community 112 - "StadiumStyle"
Cohesion: 0.15
Nodes (11): Color, Vector3, StadiumStyle, Active, FirstPickable, SelectedIndex, Surroundings, Flags (+3 more)

### Community 113 - "NetMessages.cs"
Cohesion: 0.12
Nodes (15): AnimState, Dive, Down, Idle, Jump, Kick, KickL, Run (+7 more)

### Community 114 - "com.unity.modules.screencapture"
Cohesion: 0.33
Nodes (6): com.unity.modules.screencapture, dependencies, depth, source, version, com.unity.modules.screencapture

### Community 115 - "Snapshot"
Cohesion: 0.16
Nodes (5): InputFrame, BodyState, Snapshot, StampedSnap, Snapshot

### Community 116 - "FlexNet"
Cohesion: 0.16
Nodes (11): Func, List, Material, Mesh, MeshFilter, MeshRenderer, Transform, Vector3 (+3 more)

### Community 117 - "com.unity.modules.unitywebrequest"
Cohesion: 0.33
Nodes (6): com.unity.modules.unitywebrequest, dependencies, depth, source, version, com.unity.modules.unitywebrequest

### Community 118 - "EmotePose"
Cohesion: 0.37
Nodes (4): Action, Vector3, EmotePose, Emote

### Community 119 - "AudioManager"
Cohesion: 0.08
Nodes (15): AudioClip, Dictionary, GUIStyle, IEnumerator, RuntimeInitializeOnLoadMethod, Vector3, AudioManager, Instance (+7 more)

### Community 120 - "Playlist"
Cohesion: 0.25
Nodes (7): AudioClip, Playlist, Count, Current, Track, Song, Track

### Community 121 - ".Box"
Cohesion: 0.15
Nodes (15): Collider, Color, Material, Renderer, Transform, Vector3, Crowd, FanCount (+7 more)

### Community 123 - "MenuScale"
Cohesion: 0.22
Nodes (7): Matrix4x4, MenuScale, Active, Factor, Height, UserScale, Width

### Community 124 - ".BuildGoal"
Cohesion: 0.24
Nodes (12): Goal, Refs, Collider, Material, MeshFilter, MeshRenderer, PhysicsMaterial, Renderer (+4 more)

### Community 125 - "grassprep.py"
Cohesion: 0.60
Nodes (4): load(), main(), member(), Build the turf detail layer in Assets/Resources/Turf from an ambientCG scan.…

### Community 126 - "TitleGlyph"
Cohesion: 0.33
Nodes (4): Color32, Texture2D, TitleGlyph, K

### Community 127 - "SteamTransport"
Cohesion: 0.12
Nodes (9): Func, List, SteamTransport, AdvertProvider, Available, HostPeer, IsHost, IsRunning (+1 more)

### Community 128 - "MonoBehaviour"
Cohesion: 0.16
Nodes (10): List, Quaternion, Rigidbody, Transform, Vector3, Frame, ReplaySystem, IsPlaying (+2 more)

### Community 131 - "Phase"
Cohesion: 0.33
Nodes (6): Phase, CareerStats, Hub, SinglePlayer, Splash, Zoo

### Community 132 - ".Place"
Cohesion: 0.20
Nodes (9): GameObject, Material, Mesh, MeshFilter, MeshRenderer, Transform, Vector2, Vector3 (+1 more)

### Community 133 - ".DrawBrowser"
Cohesion: 0.26
Nodes (3): Action, List, SessionBrowserUI

### Community 134 - "Achievements.cs"
Cohesion: 0.40
Nodes (5): Func, AchievementDef, AchievementKind, LeaderboardTop, StatThreshold

### Community 136 - ".Rect"
Cohesion: 0.14
Nodes (5): Action, Rect, Action, MenuUI, GUIStyle

### Community 137 - "Striker"
Cohesion: 0.06
Nodes (21): IPlayerController, Collider, Func, Vector3, Striker, FacingForward, HasLookAim, IsBusy (+13 more)

### Community 138 - "SetPieceSpin"
Cohesion: 0.33
Nodes (6): SetPieceSpin, CurveLeft, CurveRight, Knuckle, None, TopSpin

### Community 139 - ".DriveTowardRotation"
Cohesion: 0.29
Nodes (6): ConfigurableJoint, Quaternion, Rigidbody, Vector3, JointMath, Space

### Community 140 - "Trickshot: Replayability Brainstorm"
Cohesion: 0.40
Nodes (4): Ideas so far, Open questions (not answered yet), Problem, Trickshot: Replayability Brainstorm

### Community 141 - "UIFont"
Cohesion: 0.40
Nodes (4): UIFont, Body, Display, Font

### Community 143 - "GameInput"
Cohesion: 0.04
Nodes (44): Action, RebindingOperation, Vector2, GameInput, BallCamPressed, CloseControlHeld, CrossHeld, CrossMapPressed (+36 more)

### Community 144 - "Crosser"
Cohesion: 0.06
Nodes (26): Collider, Material, Renderer, Transform, Vector3, AimReticle, Active, TargetPoint (+18 more)

### Community 146 - "Achievements"
Cohesion: 0.20
Nodes (8): SteamAchievementsAPI, Available, List, Achievements, FilePath, Unlocked, UnlockedSet, UnlockedSet

### Community 147 - "Touch"
Cohesion: 0.29
Nodes (7): Touch, Carry, Contact, Keeper, Pass, Shot, Tackle

### Community 148 - "ShotType"
Cohesion: 0.25
Nodes (7): ShotType, Bicycle, DivingHeader, Header, Normal, ThirdLeg, Volley

### Community 152 - "Role"
Cohesion: 0.33
Nodes (5): Role, Crosser, Goalkeeper, Sniper, Striker

### Community 153 - "TackleResult"
Cohesion: 0.33
Nodes (6): TackleResult, Beaten, Foul, NoCarrier, Won, WrongSide

### Community 154 - "Goal"
Cohesion: 0.33
Nodes (3): Action, Collider, Goal

### Community 156 - "CrossPathLine"
Cohesion: 0.27
Nodes (5): LineRenderer, Material, Vector3, CrossPathLine, Mat

### Community 157 - "NotificationToastUI"
Cohesion: 0.33
Nodes (4): List, NotificationToastUI, Toast, Toast

### Community 158 - "horse_mane"
Cohesion: 0.12
Nodes (16): 00 Bald (index 0) -> B_regenerate [S], 01 Buzz -> REMOVE [S], 02 Crew Cut -> B_regenerate [S], 03 Spiky -> REMOVE [S], 04 Fringe -> B_regenerate [S], 05 Mohawk -> B_regenerate [S], 06 Messy -> B_regenerate [S], 07 Curly -> REMOVE [S] (+8 more)

### Community 159 - ".CaptureCursor"
Cohesion: 0.10
Nodes (8): Action, Action, MatchModeUI, Action, MultiplayerHubUI, Action, Action, StadiumSelectUI

### Community 160 - "face_props_jewelry"
Cohesion: 0.13
Nodes (15): Bindi -> B_regenerate [S], Chain Necklace -> B_regenerate [L], Cigar -> B_regenerate [M], Dangle Earrings -> B_regenerate [M], Eyebrow Piercing -> B_regenerate [S], face_props_jewelry, Hoop Earrings -> B_regenerate [S], Lollipop -> B_regenerate [M] (+7 more)

### Community 161 - "AtomicFileWriter"
Cohesion: 0.29
Nodes (4): Dictionary, AtomicFileWriter, Job, Job

### Community 163 - "human_hair"
Cohesion: 0.14
Nodes (14): Afro -> B_regenerate [M], Bald -> KEEP [S], Buzz -> B_regenerate [S], Crew Cut -> B_regenerate [M], Curly -> B_regenerate [M], Fringe -> B_regenerate [M], human_hair, Long -> KEEP [S] (+6 more)

### Community 165 - "State"
Cohesion: 0.50
Nodes (4): State, Diving, Guard, Holding

### Community 168 - "ShotBand"
Cohesion: 0.50
Nodes (4): ShotBand, Chip, Drive, Placed

### Community 170 - ".Draw"
Cohesion: 0.17
Nodes (11): GUIStyle, Rect, Vector2, GoalEditor, MaxH, MaxW, MinH, MinW (+3 more)

### Community 175 - "hats"
Cohesion: 0.15
Nodes (13): Beret (human_acc_34) -> B_regenerate [M], Bucket Hat (human_acc_30) -> B_regenerate [M], Cap (human_acc_29) -> A_download [M], Cowboy Hat (human_acc_33) -> A_download [M], Fedora (human_acc_31) -> A_download [M], hats, Headband (human_acc_36) -> KEEP [S], Party Hat (human_acc_39) -> B_regenerate [M] (+5 more)

### Community 176 - "SettingsMenu"
Cohesion: 0.12
Nodes (12): action, Dictionary, label, Keybinds, Current, Action, GUIStyle, RebindingOperation (+4 more)

### Community 178 - "elephant"
Cohesion: 0.15
Nodes (13): eleph_ears_00_plain -> B_regenerate [L], eleph_ears_01_notched -> B_regenerate [S], eleph_ears_02_wide -> B_regenerate [S], eleph_ears_03_torn -> B_regenerate [S], eleph_tack_01_head_cloth -> B_regenerate [M], eleph_tack_02_ankle_bands -> B_regenerate [S], eleph_tack_03_blanket -> B_regenerate [M], eleph_tusk_00_none -> KEEP [S] (+5 more)

### Community 180 - "StudioSplash"
Cohesion: 0.38
Nodes (3): Action, GUIStyle, StudioSplash

### Community 182 - "0Wsi-ygmiIX"
Cohesion: 0.18
Nodes (11): 0Wsi-ygmiIX, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 183 - "14ZGcuiRJ9d"
Cohesion: 0.18
Nodes (11): 14ZGcuiRJ9d, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 184 - "State"
Cohesion: 0.33
Nodes (6): State, Diving, Holding, Ready, Saving, Stumble

### Community 185 - "1TJPsi4VIT"
Cohesion: 0.18
Nodes (11): 1TJPsi4VIT, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 186 - "2Givq4Q3YTH"
Cohesion: 0.18
Nodes (11): 2Givq4Q3YTH, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 187 - "2uKEHjO_QL0"
Cohesion: 0.18
Nodes (11): 2uKEHjO_QL0, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 188 - "46Bl5Ook_xw"
Cohesion: 0.18
Nodes (11): 46Bl5Ook_xw, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 189 - "4Tdb1s3-kug"
Cohesion: 0.18
Nodes (11): 4Tdb1s3-kug, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 190 - "6VWnuNVkJ5"
Cohesion: 0.18
Nodes (11): 6VWnuNVkJ5, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 191 - "7fGDqHvHap1"
Cohesion: 0.18
Nodes (11): 7fGDqHvHap1, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 192 - "7NZp449iJq"
Cohesion: 0.18
Nodes (11): 7NZp449iJq, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 193 - "7VVumyY7L_u"
Cohesion: 0.18
Nodes (11): 7VVumyY7L_u, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 194 - "8TpZrCG3aRf"
Cohesion: 0.18
Nodes (11): 8TpZrCG3aRf, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 195 - "9i5mmOwt7cu"
Cohesion: 0.18
Nodes (11): 9i5mmOwt7cu, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 196 - "9KxfvGBAxri"
Cohesion: 0.18
Nodes (11): 9KxfvGBAxri, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 197 - "9SQY3Gsq2s"
Cohesion: 0.18
Nodes (11): 9SQY3Gsq2s, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 198 - "9xOJlCsQzX"
Cohesion: 0.18
Nodes (11): 9xOJlCsQzX, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 199 - "a6B0wtVteV"
Cohesion: 0.18
Nodes (11): a6B0wtVteV, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 200 - "aaC5GgcWEhM"
Cohesion: 0.18
Nodes (11): aaC5GgcWEhM, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 201 - "aWxhfEnYwl"
Cohesion: 0.18
Nodes (11): aWxhfEnYwl, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 202 - "aWzUlZtGLC0"
Cohesion: 0.18
Nodes (11): aWzUlZtGLC0, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 203 - "cKVNEpmNy36"
Cohesion: 0.18
Nodes (11): cKVNEpmNy36, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 204 - "CxDnECpFJH"
Cohesion: 0.18
Nodes (11): CxDnECpFJH, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 205 - "d_AsyX_R-S3"
Cohesion: 0.18
Nodes (11): d_AsyX_R-S3, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 206 - "dAwE-2WVHIt"
Cohesion: 0.18
Nodes (11): dAwE-2WVHIt, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 207 - "DBEk0SMQCt"
Cohesion: 0.18
Nodes (11): DBEk0SMQCt, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 208 - "dCm3NXrMtSr"
Cohesion: 0.18
Nodes (11): dCm3NXrMtSr, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 209 - "Dz9SyIEq7w"
Cohesion: 0.18
Nodes (11): Dz9SyIEq7w, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 210 - "fNEK0SGJ6D"
Cohesion: 0.18
Nodes (11): fNEK0SGJ6D, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 211 - "fy1Elzr3nl"
Cohesion: 0.18
Nodes (11): fy1Elzr3nl, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 212 - "j3xPyO1mvt"
Cohesion: 0.18
Nodes (11): j3xPyO1mvt, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 213 - "jcXfae4GiZ"
Cohesion: 0.18
Nodes (11): jcXfae4GiZ, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 214 - "jfVp7cW8E5"
Cohesion: 0.18
Nodes (11): jfVp7cW8E5, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 215 - "lNN3PlrjSa"
Cohesion: 0.18
Nodes (11): lNN3PlrjSa, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 216 - "LYEp20yfFh"
Cohesion: 0.18
Nodes (11): LYEp20yfFh, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 217 - "oc8MPJuSud"
Cohesion: 0.18
Nodes (11): oc8MPJuSud, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 218 - "oQtjZCNFoo"
Cohesion: 0.18
Nodes (11): oQtjZCNFoo, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 219 - "p5QgQxkMBE"
Cohesion: 0.18
Nodes (11): p5QgQxkMBE, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 220 - "SyNFHIhIDd"
Cohesion: 0.18
Nodes (11): SyNFHIhIDd, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 221 - "tPrk0HHagr"
Cohesion: 0.18
Nodes (11): tPrk0HHagr, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 222 - "WEGNXQAOfy"
Cohesion: 0.18
Nodes (11): WEGNXQAOfy, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 223 - "WoXpAJT0oD"
Cohesion: 0.18
Nodes (11): WoXpAJT0oD, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 224 - "WoYlUvyUAb"
Cohesion: 0.18
Nodes (11): WoYlUvyUAb, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 225 - "XLysBbtilu"
Cohesion: 0.18
Nodes (11): XLysBbtilu, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 226 - "YchMXfQNU0"
Cohesion: 0.18
Nodes (11): YchMXfQNU0, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 227 - "yYdsPoULg1"
Cohesion: 0.18
Nodes (11): yYdsPoULg1, author, author_slug, bytes, glb, id, license, license_url (+3 more)

### Community 230 - "Stage"
Cohesion: 0.50
Nodes (4): Stage, Jersey, Name, Skill

### Community 231 - "eyewear"
Cohesion: 0.20
Nodes (10): Aviators -> B_regenerate [M], Eyepatch -> B_regenerate [S], eyewear, Glasses -> B_regenerate [M], Monocle -> B_regenerate [S], Reading Glasses -> B_regenerate [S], Ski Goggles -> B_regenerate [L], Square Glasses -> B_regenerate [S] (+2 more)

### Community 232 - "horse_markings_tack"
Cohesion: 0.20
Nodes (10): horse_mark_01_star -> B_regenerate [S], horse_mark_02_blaze -> B_regenerate [M], horse_mark_03_snip -> B_regenerate [S], horse_mark_04_stockings -> B_regenerate [M], horse_mark_05_dappled -> B_regenerate [M], horse_markings_tack, horse_tack_01_bridle -> B_regenerate [L], horse_tack_02_halter -> B_regenerate [M] (+2 more)

### Community 234 - "human_facial"
Cohesion: 0.22
Nodes (9): Chinstrap -> B_regenerate [M], Full Beard -> B_regenerate [L], Goatee -> B_regenerate [M], Handlebar -> B_regenerate [M], human_facial, Mustache -> B_regenerate [M], Short Beard -> B_regenerate [L], Sideburns -> B_regenerate [S] (+1 more)

### Community 236 - "cosmetics-verdicts.md"
Cohesion: 0.29
Nodes (6): Batman Mask (human_acc_10) -> rename 'Vigilante Cowl' -> B_regenerate [M], Gas Mask (human_acc_13) -> B_regenerate [L], Hockey Mask (human_acc_11) -> B_regenerate [M], masks, Venetian Mask (human_acc_12) -> B_regenerate [M], Welding Mask (human_acc_14) -> A_download [M]

### Community 238 - "Cosmetics/manifest.json"
Cohesion: 0.33
Nodes (5): 3enxGxYxEKF, author, author_slug, id, title

### Community 240 - "Comb"
Cohesion: 0.33
Nodes (6): Comb, ForwardUp, Meridian, Outward, RandomSmooth, TowardPoint

### Community 241 - "Downloaded cosmetic assets"
Cohesion: 0.40
Nodes (4): CC0 (public domain, no attribution required), CC-BY 3.0 (ATTRIBUTION REQUIRED), Downloaded cosmetic assets, Packs (downloaded separately, not in manifest.json)

### Community 242 - "Tab"
Cohesion: 0.40
Nodes (5): Tab, Audio, Credits, Keybindings, Quickchat

### Community 243 - "Trick"
Cohesion: 0.40
Nodes (5): Trick, Dive, None, SlideLimp, Tumble

## Knowledge Gaps
- **1468 isolated node(s):** `ZipEnabled`, `id`, `title`, `author`, `author_slug` (+1463 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1861 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **10 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ActiveRagdoll` connect `ActiveRagdoll` to `MonoBehaviour`, `Cosmetics`, `Dribble`, `Striker`, `.Piece`, `.SlotSubMenu`, `Goalkeeper`, `Crosser`, `GameCamera`, `NetSetPieceMatch`, `Passing`, `NetStrikerMatch`, `Bone`, `KeeperController`, `AnatomySim`, `KeeperGame`, `CosmeticGallery`, `FreeKickGame`, `SetPieceTaker`, `HairSim`, `NetMatch`, `MatchGame`, `IStrikerInput`, `Sniper`, `.AttachAppearance`, `KeeperHands`, `Footballer`, `CrosserControl`, `PlayerPreview`, `Hud`, `Striker`, `Trickshot.Net`, `.SetPoseOverride`, `MenuBackground`, `BallController`, `EmotePose`?**
  _High betweenness centrality (0.111) - this node is a cross-community bridge._
- **Why does `Trickshot` connect `Trickshot` to `JerseyDesigns`, `SimConfig`, `Dribble`, `SetPieceMap`, `SkillTree`, `SkillIcons`, `Transform`, `.Piece`, `AdultQuiz`, `.SlotSubMenu`, `MenuIcons`, `PitchLayout`, `ActiveRagdoll`, `BuildAll`, `MatchProbe`, `PauseMenu`, `Passing`, `Bone`, `AnatomySim`, `KeeperGame`, `QuickChatFeed`, `CosmeticGallery`, `CrossMap`, `FreeKickGame`, `SetPieceTaker`, `HairSim`, `SkyDome`, `.Mount`, `AccuracyTarget`, `MatchGame`, `IStrikerInput`, `Sniper`, `.Draw`, `PrematchUI`, `KeeperHands`, `Multiplayer`, `DisplaySettings`, `StatRadar`, `CareerStatsData`, `PlayerProfile`, `PlayerPreview`, `.Label`, `Striker`, `.Build`, `Trickshot.Net`, `.Build`, `CreditsData`, `.Build`, `.SetPoseOverride`, `MenuBackground`, `Turf`, `StadiumStyle`, `FlexNet`, `AudioManager`, `Playlist`, `.Box`, `MenuScale`, `.BuildGoal`, `TitleGlyph`, `MonoBehaviour`, `AssetImportRules`, `.Place`, `.DrawBrowser`, `Achievements.cs`, `Striker`, `.DriveTowardRotation`, `UIFont`, `OtherModesUI`, `GameInput`, `ShotType`, `Role`, `Goal`, `CrossPathLine`, `NotificationToastUI`, `.CaptureCursor`, `AtomicFileWriter`, `SettingsMenu`, `StudioSplash`, `CallLimiter`?**
  _High betweenness centrality (0.104) - this node is a cross-community bridge._
- **Why does `NetSession` connect `NetSession` to `.Label`, `Trickshot.Net`, `SkillTree`, `QuickChatFeed`, `CrosserSetupMsg`, `Multiplayer`, `.RouteMessage`, `NetMessages.cs`, `NetInputSource`, `Snapshot`, `NetWriter`, `GameBootstrap`, `NetMatch`, `MsgType`, `INetTransport`, `NetSetPieceMatch`, `Make`, `NetStrikerMatch`?**
  _High betweenness centrality (0.066) - this node is a cross-community bridge._
- **What connects `ZipEnabled`, `id`, `title` to the rest of the system?**
  _1468 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Cosmetics` be split into smaller, more focused modules?**
  _Cohesion score 0.11737089201877934 - nodes in this community are weakly interconnected._
- **Should `JerseyDesigns` be split into smaller, more focused modules?**
  _Cohesion score 0.11428571428571428 - nodes in this community are weakly interconnected._
- **Should `SimConfig` be split into smaller, more focused modules?**
  _Cohesion score 0.05708245243128964 - nodes in this community are weakly interconnected._