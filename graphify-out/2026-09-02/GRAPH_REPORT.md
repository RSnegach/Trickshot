# Graph Report - Trickshot  (2026-09-02)

## Corpus Check
- 160 files · ~709,307 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 3824 nodes · 9730 edges · 180 communities (162 shown, 16 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 918 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `9d6a4a54`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- CareerStatsUI
- JerseyDesigns
- SimConfig
- Dribble
- SetPieceMap
- DirectIpTransport
- SkillTree
- SkillIcons
- Trickshot
- Cosmetics
- .BuildAt
- AdultQuiz
- Species
- CareerStats
- Goalkeeper
- Bone
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
- .Mat
- NetSetPieceMatch
- PauseMenu
- Passing
- NetStrikerMatch
- BodyLayoutDef
- Emote
- KeeperController
- GameManager
- CustomizeUI
- AnatomySim
- KeeperGame
- NetEndpoint
- com.unity.modules.uielements
- com.unity.modules.physics
- com.unity.modules.imageconversion
- AccuracyGame
- .CaptureCursor
- AimReticle
- CrossMap
- FreeKickGame
- SetPieceTaker
- HairSim
- .Build
- SkyDome
- com.unity.modules.hierarchycore
- NetWriter
- PropKit
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
- .MatTex
- .Label
- SteamTransport
- graphify knowledge graph
- .Rect
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
- EmotePose
- NetInputSource
- StatRadar
- Hair Atlas Asset License
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- CrosserControl
- MsgType
- PeerId
- .Pose
- ScrimPos
- ReliableChannel
- PlayerProfile
- PlayerPreview
- Hud
- .Divider
- Knockdown
- .Build
- .RequestFriendsList
- .Build
- Color32
- SurroundBuilder
- .Build
- ReplaySystem
- Gait
- MenuBackground
- .Chan
- BallController
- skyprep.py
- com.unity.nuget.newtonsoft-json
- Turf
- StadiumStyle
- com.unity.modules.screencapture
- AnimState
- .BeginMatch
- com.unity.modules.unitywebrequest
- Celebration
- AudioManager
- Playlist
- .Box
- BallController.cs
- .Clean
- .BuildGoal
- grassprep.py
- ShotServer
- MonoBehaviour
- .Button
- AssetImportRules
- .Set
- InputFrame
- SessionBrowserUI
- .SlotSubMenu
- postprep.py
- Phase
- Striker
- SetPieceSpin
- .DriveTowardRotation
- Trickshot: Replayability Brainstorm
- UIFont
- GameCamera
- GameInput
- Crosser
- Trick
- Achievements
- Touch
- ShotType
- Goal
- .Init
- NetMessages.cs
- Role
- Reason
- graphify
- Phase
- TackleResult
- NotificationToastUI
- CallLimiter
- Channel
- Phase
- AtomicFileWriter
- Phase
- .Awake
- .AutoStart
- State
- IPlayerController
- .JerseyVoteTex
- CrossPathLine
- ShotBand
- .Draw
- Category
- CrosserSetupMsg
- OptionsMenu
- Band
- .StartRebind
- Stage
- StudioSplash
- Tab
- State

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 204 edges
2. `NetSession` - 142 edges
3. `Trickshot` - 134 edges
4. `BallController` - 121 edges
5. `MatchGame` - 102 edges
6. `CustomizeUI` - 85 edges
7. `JerseyDesigns` - 79 edges
8. `GameInput` - 78 edges
9. `NetSetPieceMatch` - 72 edges
10. `Footballer` - 71 edges

## Surprising Connections (you probably didn't know these)
- `PlayerInputManager (local multiplayer seam)` --semantically_similar_to--> `Slot / role model (NetSession.MaxSlots=8)`  [INFERRED] [semantically similar]
  README.md → MULTIPLAYER.md
- `ScrimmageGame` --shares_data_with--> `ActiveRagdoll.cs`  [INFERRED]
  MULTIPLAYER.md → README.md
- `Trickshot Multiplayer Framework` --conceptually_related_to--> `Trickshot (3D trick-shot football prototype)`  [INFERRED]
  MULTIPLAYER.md → README.md
- `Trickshot (3D trick-shot football prototype)` --references--> `Unity 6000.4.1f1 editor version`  [EXTRACTED]
  README.md → ProjectSettings/ProjectVersion.txt
- `GameInput` --implements--> `IStrikerInput`  [EXTRACTED]
  Assets/Scripts/Input/GameInput.cs → Assets/Scripts/Input/IStrikerInput.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **graphify CLI commands** — claude_graphify_query, claude_graphify_path, claude_graphify_explain, claude_graphify_update [EXTRACTED 0.85]
- **Hair Atlas License Terms Set** — assets_resources_hair_hairatlas_license_royalty_free_use, assets_resources_hair_hairatlas_license_no_attribution_required, assets_resources_hair_hairatlas_license_no_resale_restriction, assets_resources_hair_hairatlas_license_bundled_license_requirement [EXTRACTED 1.00]
- **Four strand-card tiles compose the hair atlas** — assets_resources_hair_hairatlas_wavy_scattered_strands, assets_resources_hair_hairatlas_flowing_wavy_strands, assets_resources_hair_hairatlas_dense_wavy_strands, assets_resources_hair_hairatlas_straight_sleek_strands, assets_resources_hair_hairatlas_atlas [EXTRACTED 1.00]
- **Interchangeable transports behind INetTransport seam** — multiplayer_inettransport, multiplayer_directiptransport, multiplayer_localtransport, multiplayer_steamtransport [EXTRACTED 1.00]
- **Active-ragdoll bicycle-kick mechanic** — readme_activeragdoll, readme_ragdollpose, readme_kickdetector, readme_jointmath, readme_bicycle_kick [INFERRED 0.85]
- **Host-authoritative frame loop (poll, input, snapshot)** — multiplayer_multiplayer, multiplayer_netsession, multiplayer_netmessages, multiplayer_host_authoritative [INFERRED 0.85]

## Communities (180 total, 16 thin omitted)

### Community 0 - "CareerStatsUI"
Cohesion: 0.10
Nodes (13): Action, label, CareerStatsUI, Cat, Accuracy, FreeKick, Friends, Match (+5 more)

### Community 1 - "JerseyDesigns"
Cohesion: 0.11
Nodes (24): Action, Color32, Dictionary, IReadOnlyList, List, Texture2D, List, List (+16 more)

### Community 2 - "SimConfig"
Cohesion: 0.09
Nodes (18): AiDifficulty, ScrimPos, Color, Vector2, Vector3, AiDifficulty, Easy, Hard (+10 more)

### Community 3 - "Dribble"
Cohesion: 0.15
Nodes (9): Action, Vector3, Dribble, CaptureRadius, Carrying, CloseControl, Holder, Tightness (+1 more)

### Community 4 - "SetPieceMap"
Cohesion: 0.22
Nodes (10): Color, Random, Rect, Vector2, Vector3, SetPieceMap, BottomZ, HalfW (+2 more)

### Community 5 - "DirectIpTransport"
Cohesion: 0.10
Nodes (15): ConcurrentQueue, data, Dictionary, from, Func, IPEndPoint, List, DirectIpTransport (+7 more)

### Community 6 - "SkillTree"
Cohesion: 0.10
Nodes (15): Dictionary, HashSet, IEnumerable, List, Effect, Node, Preset, SkillTree (+7 more)

### Community 7 - "SkillIcons"
Cohesion: 0.19
Nodes (4): Color32, Dictionary, Texture2D, SkillIcons

### Community 8 - "Trickshot"
Cohesion: 0.06
Nodes (5): AchievementsPanelUI, GoalSetup, KeeperLevel, Trickshot.Net, Trickshot

### Community 9 - "Cosmetics"
Cohesion: 0.12
Nodes (26): AccessoryEntry, Action, Collider, IReadOnlyList, List, Material, Mesh, MeshFilter (+18 more)

### Community 10 - ".BuildAt"
Cohesion: 0.24
Nodes (8): CapsuleCollider, Collider, GameObject, Material, Quaternion, Rigidbody, Transform, Vector3

### Community 11 - "AdultQuiz"
Cohesion: 0.50
Nodes (3): AdultQuiz, Q, Q

### Community 12 - "Species"
Cohesion: 0.13
Nodes (12): BodyPlan, Biped, Quadruped, HeaderAction, Biped, Species, Current, SpeciesAxis (+4 more)

### Community 13 - "CareerStats"
Cohesion: 0.14
Nodes (9): name, CareerStats, Data, FilePath, CareerStatsData, ModeStats, OnlineRanks, RankData (+1 more)

### Community 14 - "Goalkeeper"
Cohesion: 0.13
Nodes (11): Func, Quaternion, Renderer, Vector3, Goalkeeper, Body, HasBall, Parked (+3 more)

### Community 15 - "Bone"
Cohesion: 0.10
Nodes (18): Vector3, Bone, CalfL, CalfR, Count, FootL, FootR, ForearmL (+10 more)

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
Cohesion: 0.12
Nodes (12): GameMode, Accuracy, FreeKick, Goalkeeper, Match, SetPieces, Striker, GameObject (+4 more)

### Community 21 - "com.unity.modules.jsonserialize"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "ActiveRagdoll"
Cohesion: 0.05
Nodes (27): Vector3, Rigidbody, Bounds, ConfigurableJoint, Dictionary, IReadOnlyList, List, PhysicsMaterial (+19 more)

### Community 23 - "NetSession"
Cohesion: 0.04
Nodes (44): appr, JerseyChunkMsg, LobbySlot, NetRole, Crosser, Keeper, Shooter, Spectator (+36 more)

### Community 24 - "BuildAll"
Cohesion: 0.06
Nodes (22): Action, BuildAll, ZipEnabled, Plat, List, Color32, Texture2D, TitleGlyph (+14 more)

### Community 25 - "MatchProbe"
Cohesion: 0.14
Nodes (9): List, Vector3, MatchProbe, Overlay, ProbeTackle, Ai, Human, Slide (+1 more)

### Community 26 - ".Mat"
Cohesion: 0.24
Nodes (6): Camera, Material, Refs, Renderer, Rigidbody, Transform

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.10
Nodes (9): Body, List, Material, ShootoutState, Snapshot, Transform, Vector3, Body (+1 more)

### Community 28 - "PauseMenu"
Cohesion: 0.11
Nodes (14): Action, GUIStyle, PauseMatchSetup, Action, GUIStyle, List, Entry, Kind (+6 more)

### Community 29 - "Passing"
Cohesion: 0.09
Nodes (13): Bar, List, Vector3, Bar, AnyArmed, Option, Passing, PassKind (+5 more)

### Community 30 - "NetStrikerMatch"
Cohesion: 0.07
Nodes (16): Goalkeeper, Striker, KickDetector, Rigidbody, Crosser, List, Material, name (+8 more)

### Community 31 - "BodyLayoutDef"
Cohesion: 0.11
Nodes (23): Vector3, BodyLayout, BodyLayoutDef, ParentByBone, BoneSpec, ColliderKind, Box, CapsuleY (+15 more)

### Community 32 - "Emote"
Cohesion: 0.06
Nodes (34): Emote, Backflip, Bow, Charleston, Cheer, Clap, Crip, Dab (+26 more)

### Community 33 - "KeeperController"
Cohesion: 0.14
Nodes (11): Func, Quaternion, Vector2, Vector3, KeeperController, Body, Hands, HasBall (+3 more)

### Community 34 - "GameManager"
Cohesion: 0.11
Nodes (11): Transform, Vector3, GameManager, CrossMapEscapeOwned, SaveWatch, Armed, Epic, Touched (+3 more)

### Community 35 - "CustomizeUI"
Cohesion: 0.09
Nodes (13): Action, Dictionary, Func, GUIStyle, IEnumerator, Rect, Texture2D, Vector2 (+5 more)

### Community 36 - "AnatomySim"
Cohesion: 0.18
Nodes (11): CapsuleCollider, Collider, Color, GameObject, Material, Transform, Vector3, AnatomySim (+3 more)

### Community 38 - "NetEndpoint"
Cohesion: 0.23
Nodes (4): IPAddress, IPEndPoint, List, NetEndpoint

### Community 39 - "com.unity.modules.uielements"
Cohesion: 0.12
Nodes (16): dependencies, depth, source, url, version, depth, source, version (+8 more)

### Community 40 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.physics, com.unity.modules.physics

### Community 41 - "com.unity.modules.imageconversion"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 42 - "AccuracyGame"
Cohesion: 0.18
Nodes (6): Vector3, AccuracyGame, Phase, Armed, Cooldown, Live

### Community 43 - ".CaptureCursor"
Cohesion: 0.07
Nodes (9): List, Queue, Line, QuickChatFeed, AnyOpen, EscapeOwned, Typing, DeliveryType (+1 more)

### Community 44 - "AimReticle"
Cohesion: 0.19
Nodes (8): Collider, Material, Renderer, Transform, Vector3, AimReticle, Active, TargetPoint

### Community 45 - "CrossMap"
Cohesion: 0.10
Nodes (23): Color, GUIStyle, List, name, Rect, slot, Vector2, Vector3 (+15 more)

### Community 46 - "FreeKickGame"
Cohesion: 0.09
Nodes (15): IReadOnlyList, List, PhysicsMaterial, DefensiveWall, Blockers, HasBlockers, Collider, Random (+7 more)

### Community 47 - "SetPieceTaker"
Cohesion: 0.08
Nodes (25): Action, Func, Quaternion, Vector2, Vector3, Commit, SetPieceTaker, Active (+17 more)

### Community 48 - "HairSim"
Cohesion: 0.11
Nodes (20): Material, Matrix4x4, Mesh, MeshFilter, MeshRenderer, Transform, Vector2, Vector3 (+12 more)

### Community 49 - ".Build"
Cohesion: 0.15
Nodes (9): BoxCollider, CapsuleCollider, Collider, Material, Renderer, SphereCollider, Transform, PlayerAppearance (+1 more)

### Community 50 - "SkyDome"
Cohesion: 0.24
Nodes (7): Color, Dictionary, Light, Material, Shader, Texture2D, SkyDome

### Community 51 - "com.unity.modules.hierarchycore"
Cohesion: 0.15
Nodes (13): com.unity.modules.hierarchycore, dependencies, depth, source, version, dependencies, depth, source (+5 more)

### Community 52 - "NetWriter"
Cohesion: 0.18
Nodes (5): PlayerAppearance, NetCodec, NetWriter, BinaryWriter, MemoryStream

### Community 53 - "PropKit"
Cohesion: 0.19
Nodes (13): Bounds, Collider, Dictionary, GameObject, HashSet, Material, MeshFilter, MeshRenderer (+5 more)

### Community 54 - "NetMatch"
Cohesion: 0.18
Nodes (7): Bar, Material, Refs, Transform, Vector3, Body, NetMatch

### Community 55 - "AccuracyTarget"
Cohesion: 0.09
Nodes (16): Transform, Vector3, AccuracyBoard, Count, Action, BoxCollider, Collider, Color (+8 more)

### Community 56 - "dependencies"
Cohesion: 0.06
Nodes (31): com.coplaydev.unity-mcp, com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+23 more)

### Community 57 - "com.unity.modules.androidjni"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.androidjni

### Community 58 - "MatchGame"
Cohesion: 0.06
Nodes (20): Dictionary, Func, HashSet, List, Vector3, MatchGame, AwayScore, ClockRemaining (+12 more)

### Community 59 - "IStrikerInput"
Cohesion: 0.07
Nodes (27): Vector2, IStrikerInput, CloseControlHeld, CrossPressed, EmoteId, Fresh, JumpHeld, JumpPressed (+19 more)

### Community 60 - "Make"
Cohesion: 0.27
Nodes (9): Color, Material, Shader, Texture2D, JerseyFaces, Chest, Flank, Make (+1 more)

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

### Community 67 - ".Label"
Cohesion: 0.08
Nodes (34): Action, Rect, Color, GUIStyle, List, Rect, Vector2, GUIStyle (+26 more)

### Community 68 - "SteamTransport"
Cohesion: 0.15
Nodes (8): Func, SteamTransport, AdvertProvider, Available, HostPeer, IsHost, IsRunning, LocalPeer

### Community 69 - "graphify knowledge graph"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - ".Rect"
Cohesion: 0.20
Nodes (5): Action, GUIStyle, ScrimPos, Vector3, PrematchUI

### Community 71 - "com.unity.ext.nunit"
Cohesion: 0.17
Nodes (12): com.unity.ext.nunit, com.unity.test-framework, dependencies, depth, source, version, dependencies, depth (+4 more)

### Community 72 - "TailnetDiscovery"
Cohesion: 0.14
Nodes (16): Action, LobbyInfo, Action, List, Action, ConcurrentQueue, IPAddress, IPEndPoint (+8 more)

### Community 73 - "dependencies"
Cohesion: 0.17
Nodes (12): com.unity.modules.physics2d, dependencies, depth, hash, source, version, dependencies, depth (+4 more)

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "KeeperHands"
Cohesion: 0.15
Nodes (5): Collider, Vector3, KeeperHands, HeldFor, Holding

### Community 76 - "Multiplayer"
Cohesion: 0.08
Nodes (14): List, RuntimeInitializeOnLoadMethod, Multiplayer, IsActive, IsClient, IsHost, Session, SteamLinked (+6 more)

### Community 77 - "Hair Strand Texture Atlas"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 79 - "DisplaySettings"
Cohesion: 0.11
Nodes (15): DisplaySettings, Available, CrowdScale, FovOffset, Graphics, UiScale, VSync, GraphicsTier (+7 more)

### Community 80 - "Footballer"
Cohesion: 0.09
Nodes (19): AiTuning, List, Vector2, Vector3, Footballer, IsDown, Keeper, KeeperHoldingBall (+11 more)

### Community 81 - "EmotePose"
Cohesion: 0.41
Nodes (4): Action, Vector3, EmotePose, Emote

### Community 82 - "NetInputSource"
Cohesion: 0.07
Nodes (29): Vector2, NetInputSource, CloseControlHeld, CrossPressed, EmoteId, Fresh, JumpHeld, JumpPressed (+21 more)

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
Cohesion: 0.10
Nodes (14): Collider, Transform, CrosserBubble, Func, Vector3, CrosserControl, Acc01, InStance (+6 more)

### Community 88 - "MsgType"
Cohesion: 0.08
Nodes (23): MsgType, AssignSlot, BallKick, CastJerseyVote, CrosserSetup, Hello, JerseyChunk, MatchEvent (+15 more)

### Community 89 - "PeerId"
Cohesion: 0.05
Nodes (28): Action, Func, List, INetTransport, AdvertProvider, HostPeer, IsHost, IsRunning (+20 more)

### Community 90 - ".Pose"
Cohesion: 0.26
Nodes (3): Vector3, KickSwing, LocalFoot

### Community 91 - "ScrimPos"
Cohesion: 0.17
Nodes (12): ScrimPos, CAM, CB, CM, GK, LB, LM, LW (+4 more)

### Community 92 - "ReliableChannel"
Cohesion: 0.19
Nodes (8): Dictionary, List, Queue, Pending, ReliableChannel, CumAck, HasUnacked, Pending

### Community 93 - "PlayerProfile"
Cohesion: 0.03
Nodes (59): Color, label, Texture2D, PlayerProfile, AgilityStat, AirFlipMul, Bias, BicycleSkill01 (+51 more)

### Community 94 - "PlayerPreview"
Cohesion: 0.07
Nodes (19): MatchStatsUI, Tab, Away, Home, Color, GameObject, Light, Material (+11 more)

### Community 95 - "Hud"
Cohesion: 0.08
Nodes (15): Color, GUIStyle, List, Rect, Vector2, Vector3, Hud, H (+7 more)

### Community 96 - ".Divider"
Cohesion: 0.14
Nodes (7): Action, GUIStyle, Vector3, HostSetupUI, Action, Color, LobbyUI

### Community 97 - "Knockdown"
Cohesion: 0.24
Nodes (4): Knockdown, Beaten, Down, Strk

### Community 98 - ".Build"
Cohesion: 0.34
Nodes (9): Collider, Color, Material, PhysicsMaterial, Transform, Vector3, StadiumBuilder, CornerPylonScale (+1 more)

### Community 99 - ".RequestFriendsList"
Cohesion: 0.20
Nodes (7): Action, List, SteamFriendInfo, SteamFriendsAPI, Available, List, FriendsPanelUI

### Community 100 - ".Build"
Cohesion: 0.39
Nodes (5): Collider, Material, Transform, Vector3, PitchBuilder

### Community 102 - "SurroundBuilder"
Cohesion: 0.23
Nodes (10): Collider, Color, Material, Texture2D, Transform, Vector3, SurroundBuilder, BowlHalfX (+2 more)

### Community 103 - ".Build"
Cohesion: 0.19
Nodes (13): GoalFrame, PhysicsMaterial, Collider, Material, MeshFilter, MeshRenderer, PhysicsMaterial, Renderer (+5 more)

### Community 104 - "ReplaySystem"
Cohesion: 0.16
Nodes (9): List, Quaternion, Rigidbody, Transform, Vector3, Frame, ReplaySystem, IsPlaying (+1 more)

### Community 105 - "Gait"
Cohesion: 0.34
Nodes (4): Vector3, Gait, Profile, Profile

### Community 106 - "MenuBackground"
Cohesion: 0.08
Nodes (23): Func, List, Material, Mesh, MeshFilter, MeshRenderer, Transform, Vector3 (+15 more)

### Community 107 - ".Chan"
Cohesion: 0.24
Nodes (3): AudioClip, IEnumerator, Vector3

### Community 108 - "BallController"
Cohesion: 0.09
Nodes (19): Collision, Rigidbody, SphereCollider, Vector3, BallController, DribbleCarrier, DribbleHold, Guided (+11 more)

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

### Community 114 - "com.unity.modules.screencapture"
Cohesion: 0.33
Nodes (6): com.unity.modules.screencapture, dependencies, depth, source, version, com.unity.modules.screencapture

### Community 115 - "AnimState"
Cohesion: 0.09
Nodes (11): InputFrame, AnimState, Dive, Down, Idle, Jump, Kick, KickL (+3 more)

### Community 117 - "com.unity.modules.unitywebrequest"
Cohesion: 0.33
Nodes (6): com.unity.modules.unitywebrequest, dependencies, depth, source, version, com.unity.modules.unitywebrequest

### Community 118 - "Celebration"
Cohesion: 0.18
Nodes (6): e, name, Celebration, CurrentEmote, Playing, Progress01

### Community 119 - "AudioManager"
Cohesion: 0.20
Nodes (4): Dictionary, GUIStyle, AudioManager, Instance

### Community 120 - "Playlist"
Cohesion: 0.25
Nodes (7): AudioClip, Playlist, Count, Current, Track, Song, Track

### Community 121 - ".Box"
Cohesion: 0.13
Nodes (16): Collider, Color, Material, Renderer, Transform, Vector3, Crowd, FanCount (+8 more)

### Community 124 - ".BuildGoal"
Cohesion: 0.16
Nodes (17): Outcome, Blocked, Goal, Miss, Save, Refs, NetBackstop, Collider (+9 more)

### Community 125 - "grassprep.py"
Cohesion: 0.60
Nodes (4): load(), main(), member(), Build the turf detail layer in Assets/Resources/Turf from an ambientCG scan.…

### Community 126 - "ShotServer"
Cohesion: 0.36
Nodes (3): Vector3, ShotServer, JustFired

### Community 127 - "MonoBehaviour"
Cohesion: 0.11
Nodes (12): Action, MatchModeUI, Action, MultiplayerHubUI, Material, Transform, NetAccuracyMatch, Action (+4 more)

### Community 128 - ".Button"
Cohesion: 0.11
Nodes (11): Action, Rect, Matrix4x4, MenuScale, Active, Factor, Height, UserScale (+3 more)

### Community 131 - ".Set"
Cohesion: 0.38
Nodes (4): e, Vector3, KeeperPose, b

### Community 132 - "InputFrame"
Cohesion: 0.33
Nodes (3): Vector2, InputFrame, Sticky

### Community 133 - "SessionBrowserUI"
Cohesion: 0.27
Nodes (3): Action, List, SessionBrowserUI

### Community 134 - ".SlotSubMenu"
Cohesion: 0.14
Nodes (8): Color, SlotKind, Skin, StyleA, StyleB, StyleC, Color, SpeciesCosmetics

### Community 136 - "Phase"
Cohesion: 0.33
Nodes (6): Phase, CareerStats, Hub, SinglePlayer, Splash, Zoo

### Community 137 - "Striker"
Cohesion: 0.07
Nodes (20): Collider, Func, Vector3, Striker, FacingForward, HasLookAim, IsBusy, IsDiving (+12 more)

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

### Community 142 - "GameCamera"
Cohesion: 0.11
Nodes (14): Func, Transform, Vector3, GameCamera, BallCam, KeeperLookDownFraction, KeeperLookYaw, Pitch (+6 more)

### Community 143 - "GameInput"
Cohesion: 0.05
Nodes (39): Vector2, GameInput, BallCamPressed, CloseControlHeld, CrossHeld, CrossMapPressed, CrossPressed, CursorCaptured (+31 more)

### Community 144 - "Crosser"
Cohesion: 0.12
Nodes (13): Quaternion, Transform, Vector3, Crosser, JustServed, Origin, Ragdoll, ReadyToServe (+5 more)

### Community 145 - "Trick"
Cohesion: 0.40
Nodes (5): Trick, Dive, None, SlideLimp, Tumble

### Community 146 - "Achievements"
Cohesion: 0.14
Nodes (13): SteamAchievementsAPI, Available, Func, List, AchievementDef, AchievementKind, LeaderboardTop, StatThreshold (+5 more)

### Community 147 - "Touch"
Cohesion: 0.29
Nodes (7): Touch, Carry, Contact, Keeper, Pass, Shot, Tackle

### Community 148 - "ShotType"
Cohesion: 0.25
Nodes (7): ShotType, Bicycle, DivingHeader, Header, Normal, ThirdLeg, Volley

### Community 149 - "Goal"
Cohesion: 0.33
Nodes (3): Action, Collider, Goal

### Community 150 - ".Init"
Cohesion: 0.40
Nodes (3): InputAction, InputActionAsset, PlayerInput

### Community 151 - "NetMessages.cs"
Cohesion: 0.10
Nodes (12): Vector3, BodyState, JoinRefusal, MatchRunning, None, NoSlot, Version, ShootoutState (+4 more)

### Community 152 - "Role"
Cohesion: 0.33
Nodes (5): Role, Crosser, Goalkeeper, Sniper, Striker

### Community 153 - "Reason"
Cohesion: 0.40
Nodes (5): Reason, NoCli, NoPeers, Ok, TailnetDown

### Community 154 - "graphify"
Cohesion: 0.40
Nodes (5): graphify, unityMCP, C:/Users/evrik/AppData/Local/Microsoft/WinGet/Packages/astral-sh.uv_Microsoft.Winget.Source_8wekyb3d8bbwe/uvx.exe, graphify-mcp, mcp-for-unity

### Community 155 - "Phase"
Cohesion: 0.40
Nodes (5): Phase, Attack, Defend, Loose, Restart

### Community 156 - "TackleResult"
Cohesion: 0.33
Nodes (6): TackleResult, Beaten, Foul, NoCarrier, Won, WrongSide

### Community 157 - "NotificationToastUI"
Cohesion: 0.33
Nodes (4): List, NotificationToastUI, Toast, Toast

### Community 159 - "Channel"
Cohesion: 0.40
Nodes (5): Channel, Crowd, Master, Music, Sfx

### Community 160 - "Phase"
Cohesion: 0.50
Nodes (4): Phase, Armed, Live, Settle

### Community 161 - "AtomicFileWriter"
Cohesion: 0.29
Nodes (4): Dictionary, AtomicFileWriter, Job, Job

### Community 162 - "Phase"
Cohesion: 0.50
Nodes (4): Phase, Connecting, Playlist, Searching

### Community 165 - "State"
Cohesion: 0.50
Nodes (4): State, Diving, Guard, Holding

### Community 168 - "CrossPathLine"
Cohesion: 0.29
Nodes (5): LineRenderer, Material, Vector3, CrossPathLine, Mat

### Community 169 - "ShotBand"
Cohesion: 0.50
Nodes (4): ShotBand, Chip, Drive, Placed

### Community 170 - ".Draw"
Cohesion: 0.24
Nodes (8): GUIStyle, Rect, Vector2, GoalEditor, MaxH, MaxW, MinH, MinW

### Community 171 - "Category"
Cohesion: 0.20
Nodes (10): Category, Agility, Control, Heading, Instinct, Pace, Passing, Shooting (+2 more)

### Community 176 - "OptionsMenu"
Cohesion: 0.10
Nodes (14): action, Dictionary, label, Keybinds, Current, Action, GUIStyle, RebindingOperation (+6 more)

### Community 177 - "Band"
Cohesion: 0.40
Nodes (5): Band, High, Jump, Low, Mid

### Community 179 - "Stage"
Cohesion: 0.50
Nodes (4): Stage, Jersey, Name, Skill

### Community 180 - "StudioSplash"
Cohesion: 0.38
Nodes (3): Action, GUIStyle, StudioSplash

### Community 181 - "Tab"
Cohesion: 0.50
Nodes (4): Tab, Audio, Keybindings, Quickchat

### Community 184 - "State"
Cohesion: 0.33
Nodes (6): State, Diving, Holding, Ready, Saving, Stumble

## Knowledge Gaps
- **870 isolated node(s):** `mcp-for-unity`, `graphify-mcp`, `ZipEnabled`, `CursorCaptured`, `Move` (+865 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1219 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **16 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `Trickshot` to `CareerStatsUI`, `JerseyDesigns`, `SimConfig`, `SetPieceMap`, `SkillTree`, `Cosmetics`, `AdultQuiz`, `Species`, `CareerStats`, `Bone`, `MenuIcons`, `PitchLayout`, `BuildAll`, `MatchProbe`, `PauseMenu`, `Passing`, `NetStrikerMatch`, `BodyLayoutDef`, `GameManager`, `AnatomySim`, `KeeperGame`, `AccuracyGame`, `.CaptureCursor`, `AimReticle`, `CrossMap`, `FreeKickGame`, `SetPieceTaker`, `HairSim`, `SkyDome`, `PropKit`, `AccuracyTarget`, `MatchGame`, `IStrikerInput`, `Make`, `Sniper`, `KeeperHands`, `Multiplayer`, `DisplaySettings`, `Footballer`, `StatRadar`, `CrosserControl`, `.Pose`, `PlayerProfile`, `PlayerPreview`, `.Divider`, `Knockdown`, `.RequestFriendsList`, `.Build`, `.Build`, `ReplaySystem`, `Gait`, `MenuBackground`, `Turf`, `StadiumStyle`, `Celebration`, `AudioManager`, `Playlist`, `.Box`, `.Clean`, `.BuildGoal`, `ShotServer`, `MonoBehaviour`, `.Button`, `AssetImportRules`, `.Set`, `.SlotSubMenu`, `Striker`, `.DriveTowardRotation`, `UIFont`, `GameCamera`, `GameInput`, `Crosser`, `Achievements`, `ShotType`, `Goal`, `Role`, `NotificationToastUI`, `CallLimiter`, `AtomicFileWriter`, `IPlayerController`, `CrossPathLine`, `OptionsMenu`, `StudioSplash`?**
  _High betweenness centrality (0.167) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `ActiveRagdoll` to `SimConfig`, `Dribble`, `Trickshot`, `Striker`, `Species`, `Goalkeeper`, `Bone`, `Crosser`, `.Mat`, `NetSetPieceMatch`, `Passing`, `NetStrikerMatch`, `BodyLayoutDef`, `KeeperController`, `GameManager`, `AnatomySim`, `KeeperGame`, `AccuracyGame`, `FreeKickGame`, `SetPieceTaker`, `HairSim`, `.Build`, `NetMatch`, `MatchGame`, `Sniper`, `KeeperHands`, `Footballer`, `CrosserControl`, `.Pose`, `PlayerPreview`, `Hud`, `Knockdown`, `ReplaySystem`, `MenuBackground`, `BallController`, `Celebration`, `MonoBehaviour`?**
  _High betweenness centrality (0.141) - this node is a cross-community bridge._
- **Why does `NetSession` connect `NetSession` to `InputFrame`, `Trickshot`, `.RouteMessage`, `NetMessages.cs`, `NetSetPieceMatch`, `NetStrikerMatch`, `.JerseyVoteTex`, `.CaptureCursor`, `CrosserSetupMsg`, `NetMatch`, `.MatTex`, `Multiplayer`, `MsgType`, `PeerId`, `.Divider`, `Color32`, `.ClientUpdate`, `AnimState`, `.Clean`?**
  _High betweenness centrality (0.096) - this node is a cross-community bridge._
- **What connects `mcp-for-unity`, `graphify-mcp`, `ZipEnabled` to the rest of the system?**
  _870 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `CareerStatsUI` be split into smaller, more focused modules?**
  _Cohesion score 0.10160427807486631 - nodes in this community are weakly interconnected._
- **Should `JerseyDesigns` be split into smaller, more focused modules?**
  _Cohesion score 0.11428571428571428 - nodes in this community are weakly interconnected._
- **Should `SimConfig` be split into smaller, more focused modules?**
  _Cohesion score 0.09247311827956989 - nodes in this community are weakly interconnected._