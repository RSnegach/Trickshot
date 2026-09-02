# Graph Report - Trickshot  (2026-09-02)

## Corpus Check
- 159 files · ~710,786 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 3821 nodes · 9739 edges · 178 communities (164 shown, 12 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 920 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `bf7689cc`
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
- DefensiveWall
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
- QuickChatFeed
- .Update
- CrossMap
- FreeKickGame
- SetPieceTaker
- HairSim
- Vector3
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
- UITheme
- .Draw
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
- .SetPoseOverride
- MenuBackground
- LocalTransport
- BallController
- skyprep.py
- com.unity.nuget.newtonsoft-json
- Turf
- StadiumStyle
- FlexNet
- com.unity.modules.screencapture
- NetMessages.cs
- .BroadcastSnapshot
- com.unity.modules.unitywebrequest
- Celebration
- AudioManager
- Playlist
- .Box
- BallController.cs
- HostSetupUI
- .BuildGoal
- grassprep.py
- TitleGlyph
- MonoBehaviour
- .Scrim
- AssetImportRules
- .Set
- .Place
- SessionBrowserUI
- SlotKind
- postprep.py
- Cat
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
- DecorTint
- .Init
- JoinRefusal
- Role
- State
- Phase
- Achievements.cs
- NotificationToastUI
- CallLimiter
- CareerStatsData
- Phase
- AtomicFileWriter
- Phase
- PassKind
- Kind
- State
- IPlayerController
- SpeciesBias
- .Draw
- Category
- CrosserSetupMsg
- .Label
- Band
- .StartRebind
- Stage
- StudioSplash
- Tab
- State

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 206 edges
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

## Communities (178 total, 12 thin omitted)

### Community 0 - "CareerStatsUI"
Cohesion: 0.14
Nodes (6): Action, label, CareerStatsUI, Cat, mp, sp

### Community 1 - "JerseyDesigns"
Cohesion: 0.11
Nodes (24): Action, Color32, Dictionary, IReadOnlyList, List, Texture2D, List, List (+16 more)

### Community 2 - "SimConfig"
Cohesion: 0.09
Nodes (19): AiDifficulty, ScrimPos, Color, Vector2, Vector3, AiDifficulty, Easy, Hard (+11 more)

### Community 3 - "Dribble"
Cohesion: 0.11
Nodes (15): Action, Vector3, Dribble, CaptureRadius, Carrying, CloseControl, Holder, Tightness (+7 more)

### Community 4 - "SetPieceMap"
Cohesion: 0.23
Nodes (10): Color, Random, Rect, Vector2, Vector3, SetPieceMap, BottomZ, HalfW (+2 more)

### Community 5 - "DirectIpTransport"
Cohesion: 0.10
Nodes (15): Action, ConcurrentQueue, data, Dictionary, from, Func, IPEndPoint, List (+7 more)

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

### Community 10 - "DefensiveWall"
Cohesion: 0.15
Nodes (14): CapsuleCollider, Collider, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion (+6 more)

### Community 11 - "AdultQuiz"
Cohesion: 0.50
Nodes (3): AdultQuiz, Q, Q

### Community 12 - "Species"
Cohesion: 0.15
Nodes (10): BodyPlan, Biped, Quadruped, HeaderAction, Biped, Species, Current, SpeciesAxis (+2 more)

### Community 13 - "CareerStats"
Cohesion: 0.16
Nodes (5): name, CareerStats, Data, FilePath, min

### Community 14 - "Goalkeeper"
Cohesion: 0.12
Nodes (11): Func, Quaternion, Renderer, Vector3, Goalkeeper, Body, HasBall, Parked (+3 more)

### Community 15 - "Bone"
Cohesion: 0.08
Nodes (20): Renderer, Transform, Vector3, Bone, CalfL, CalfR, Count, FootL (+12 more)

### Community 16 - ".RouteMessage"
Cohesion: 0.24
Nodes (5): Color, NetReader, More, Type, BinaryReader

### Community 17 - "MenuIcons"
Cohesion: 0.26
Nodes (4): Color32, Dictionary, Texture2D, MenuIcons

### Community 18 - "PitchLayout"
Cohesion: 0.12
Nodes (14): PitchLayout, AttackGoalLineZ, FarGoalLineZ, HalfWidth, PitchCenterZ, RowDepth, RowRise, StandBaseHeight (+6 more)

### Community 19 - "DirectIpTransport.cs (direct-IP UDP)"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 20 - "GameBootstrap"
Cohesion: 0.11
Nodes (12): GameMode, Accuracy, FreeKick, Goalkeeper, Match, SetPieces, Striker, GameObject (+4 more)

### Community 21 - "com.unity.modules.jsonserialize"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "ActiveRagdoll"
Cohesion: 0.06
Nodes (24): Bounds, Collider, ConfigurableJoint, Dictionary, IReadOnlyList, List, PhysicsMaterial, Rigidbody (+16 more)

### Community 23 - "NetSession"
Cohesion: 0.04
Nodes (45): appr, JerseyChunkMsg, LobbySlot, NetRole, Crosser, Keeper, Shooter, Spectator (+37 more)

### Community 24 - "BuildAll"
Cohesion: 0.12
Nodes (8): Action, BuildAll, ZipEnabled, Plat, BuildTarget, MenuItem, Plat, Type

### Community 25 - "MatchProbe"
Cohesion: 0.14
Nodes (9): List, Vector3, MatchProbe, Overlay, ProbeTackle, Ai, Human, Slide (+1 more)

### Community 26 - ".Mat"
Cohesion: 0.26
Nodes (7): Camera, Material, Refs, Renderer, Rigidbody, Texture2D, Transform

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.09
Nodes (10): Body, List, Material, Rigidbody, ShootoutState, Snapshot, Transform, Vector3 (+2 more)

### Community 28 - "PauseMenu"
Cohesion: 0.18
Nodes (7): Action, List, Entry, PauseMenu, Paused, Entry, Kind

### Community 29 - "Passing"
Cohesion: 0.11
Nodes (9): Bar, List, Vector3, Bar, AnyArmed, Option, Passing, Option (+1 more)

### Community 30 - "NetStrikerMatch"
Cohesion: 0.11
Nodes (8): Crosser, Material, Rigidbody, Transform, Vector3, Body, NetStrikerMatch, CrossMapAvailable

### Community 31 - "BodyLayoutDef"
Cohesion: 0.14
Nodes (16): Vector3, BodyLayout, BodyLayoutDef, ParentByBone, BoneSpec, ColliderKind, Box, CapsuleY (+8 more)

### Community 32 - "Emote"
Cohesion: 0.06
Nodes (34): Emote, Backflip, Bow, Charleston, Cheer, Clap, Crip, Dab (+26 more)

### Community 33 - "KeeperController"
Cohesion: 0.14
Nodes (11): Func, Quaternion, Vector2, Vector3, KeeperController, Body, Hands, HasBall (+3 more)

### Community 34 - "GameManager"
Cohesion: 0.15
Nodes (5): CrowdCheer, Transform, Vector3, GameManager, CrossMapEscapeOwned

### Community 35 - "CustomizeUI"
Cohesion: 0.09
Nodes (14): Action, Color, Dictionary, Func, GUIStyle, IEnumerator, Rect, Texture2D (+6 more)

### Community 36 - "AnatomySim"
Cohesion: 0.18
Nodes (11): CapsuleCollider, Collider, Color, GameObject, Material, Transform, Vector3, AnatomySim (+3 more)

### Community 37 - "KeeperGame"
Cohesion: 0.19
Nodes (4): KeeperGame, Vector3, ShotServer, JustFired

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

### Community 42 - "AccuracyGame"
Cohesion: 0.07
Nodes (16): Transform, Vector3, AccuracyBoard, Count, Vector3, AccuracyGame, Phase, Armed (+8 more)

### Community 43 - "QuickChatFeed"
Cohesion: 0.10
Nodes (10): Dictionary, ChatCensor, List, Queue, Line, QuickChatFeed, AnyOpen, EscapeOwned (+2 more)

### Community 44 - ".Update"
Cohesion: 0.13
Nodes (4): State, Default, DeliveryType, State

### Community 45 - "CrossMap"
Cohesion: 0.17
Nodes (15): Color, GUIStyle, Rect, Vector2, Vector3, CrossMap, BottomZ, EscapeOwned (+7 more)

### Community 46 - "FreeKickGame"
Cohesion: 0.13
Nodes (13): Collider, Random, Vector3, FreeKickGame, Outcome, Blocked, Miss, Save (+5 more)

### Community 47 - "SetPieceTaker"
Cohesion: 0.09
Nodes (19): Action, Func, Quaternion, Vector2, Vector3, Commit, SetPieceTaker, Active (+11 more)

### Community 48 - "HairSim"
Cohesion: 0.11
Nodes (20): Material, Matrix4x4, Mesh, MeshFilter, MeshRenderer, Transform, Vector2, Vector3 (+12 more)

### Community 49 - "Vector3"
Cohesion: 0.16
Nodes (9): BoxCollider, CapsuleCollider, Material, Quaternion, SphereCollider, Vector3, Color, PlayerAppearance (+1 more)

### Community 50 - "SkyDome"
Cohesion: 0.24
Nodes (7): Color, Dictionary, Light, Material, Shader, Texture2D, SkyDome

### Community 51 - "com.unity.modules.hierarchycore"
Cohesion: 0.15
Nodes (13): com.unity.modules.hierarchycore, dependencies, depth, source, version, dependencies, depth, source (+5 more)

### Community 52 - "NetWriter"
Cohesion: 0.17
Nodes (6): PlayerAppearance, Vector3, NetCodec, NetWriter, BinaryWriter, MemoryStream

### Community 53 - "PropKit"
Cohesion: 0.19
Nodes (13): Bounds, Collider, Dictionary, GameObject, HashSet, Material, MeshFilter, MeshRenderer (+5 more)

### Community 54 - "NetMatch"
Cohesion: 0.11
Nodes (9): StatRow, Bar, Material, Refs, Rigidbody, Transform, Vector3, Body (+1 more)

### Community 55 - "AccuracyTarget"
Cohesion: 0.16
Nodes (12): Action, BoxCollider, Collider, Color, Material, Renderer, Transform, Vector3 (+4 more)

### Community 56 - "dependencies"
Cohesion: 0.06
Nodes (31): com.coplaydev.unity-mcp, com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+23 more)

### Community 57 - "com.unity.modules.androidjni"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.androidjni

### Community 58 - "MatchGame"
Cohesion: 0.06
Nodes (19): Dictionary, Func, HashSet, List, Vector3, MatchGame, AwayScore, ClockRemaining (+11 more)

### Community 59 - "IStrikerInput"
Cohesion: 0.07
Nodes (27): Vector2, IStrikerInput, CloseControlHeld, CrossPressed, EmoteId, Fresh, JumpHeld, JumpPressed (+19 more)

### Community 60 - "Make"
Cohesion: 0.26
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

### Community 67 - "UITheme"
Cohesion: 0.10
Nodes (27): GUIStyle, Color, GUIStyle, Matrix4x4, Rect, Texture2D, UITheme, BtnDownTex (+19 more)

### Community 68 - ".Draw"
Cohesion: 0.15
Nodes (10): Color, GUIStyle, List, MatchStatsUI, Tab, Away, Home, Rect (+2 more)

### Community 69 - "graphify knowledge graph"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - ".Rect"
Cohesion: 0.21
Nodes (5): Action, GUIStyle, ScrimPos, Vector3, PrematchUI

### Community 71 - "com.unity.ext.nunit"
Cohesion: 0.17
Nodes (12): com.unity.ext.nunit, com.unity.test-framework, dependencies, depth, source, version, dependencies, depth (+4 more)

### Community 72 - "TailnetDiscovery"
Cohesion: 0.13
Nodes (18): Action, ConcurrentQueue, IPAddress, IPEndPoint, List, Reason, NoCli, NoPeers (+10 more)

### Community 73 - "dependencies"
Cohesion: 0.17
Nodes (12): com.unity.modules.physics2d, dependencies, depth, hash, source, version, dependencies, depth (+4 more)

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "KeeperHands"
Cohesion: 0.21
Nodes (5): Collider, Vector3, KeeperHands, HeldFor, Holding

### Community 76 - "Multiplayer"
Cohesion: 0.09
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
Cohesion: 0.56
Nodes (3): Action, Vector3, EmotePose

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
Cohesion: 0.08
Nodes (19): Collider, Transform, CrosserBubble, Func, Vector3, CrosserControl, Acc01, InStance (+11 more)

### Community 88 - "MsgType"
Cohesion: 0.08
Nodes (23): MsgType, AssignSlot, BallKick, CastJerseyVote, CrosserSetup, Hello, JerseyChunk, MatchEvent (+15 more)

### Community 89 - "PeerId"
Cohesion: 0.05
Nodes (25): Action, Func, List, INetTransport, AdvertProvider, HostPeer, IsHost, IsRunning (+17 more)

### Community 90 - ".Pose"
Cohesion: 0.25
Nodes (3): Vector3, KickSwing, LocalFoot

### Community 91 - "ScrimPos"
Cohesion: 0.17
Nodes (12): ScrimPos, CAM, CB, CM, GK, LB, LM, LW (+4 more)

### Community 92 - "ReliableChannel"
Cohesion: 0.19
Nodes (8): Dictionary, List, Queue, Pending, ReliableChannel, CumAck, HasUnacked, Pending

### Community 93 - "PlayerProfile"
Cohesion: 0.03
Nodes (58): label, Texture2D, PlayerProfile, AgilityStat, AirFlipMul, Bias, BicycleSkill01, BodyHeightScale (+50 more)

### Community 94 - "PlayerPreview"
Cohesion: 0.09
Nodes (14): Color, GameObject, Light, Material, Quaternion, Rect, Renderer, Texture2D (+6 more)

### Community 95 - "Hud"
Cohesion: 0.09
Nodes (19): Color, GUIStyle, List, Rect, Vector2, Vector3, Hud, H (+11 more)

### Community 97 - "Knockdown"
Cohesion: 0.18
Nodes (5): Vector3, Knockdown, Beaten, Down, Strk

### Community 98 - ".Build"
Cohesion: 0.22
Nodes (14): IEnumerable, Quaternion, Vector3, Seat, Collider, Color, Material, PhysicsMaterial (+6 more)

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
Cohesion: 0.23
Nodes (12): PhysicsMaterial, Collider, Material, MeshFilter, MeshRenderer, PhysicsMaterial, Renderer, Transform (+4 more)

### Community 104 - "ReplaySystem"
Cohesion: 0.16
Nodes (9): List, Quaternion, Rigidbody, Transform, Vector3, Frame, ReplaySystem, IsPlaying (+1 more)

### Community 105 - ".SetPoseOverride"
Cohesion: 0.31
Nodes (4): Vector3, Gait, Profile, Profile

### Community 106 - "MenuBackground"
Cohesion: 0.12
Nodes (14): Goalkeeper, Collider, Color, Light, List, Material, MeshFilter, MeshRenderer (+6 more)

### Community 107 - "LocalTransport"
Cohesion: 0.12
Nodes (14): LobbyInfo, Action, data, Dictionary, from, Func, List, Queue (+6 more)

### Community 108 - "BallController"
Cohesion: 0.06
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

### Community 113 - "FlexNet"
Cohesion: 0.16
Nodes (11): Func, List, Material, Mesh, MeshFilter, MeshRenderer, Transform, Vector3 (+3 more)

### Community 114 - "com.unity.modules.screencapture"
Cohesion: 0.33
Nodes (6): com.unity.modules.screencapture, dependencies, depth, source, version, com.unity.modules.screencapture

### Community 115 - "NetMessages.cs"
Cohesion: 0.08
Nodes (18): InputFrame, Vector2, AnimState, Dive, Down, Idle, Jump, Kick (+10 more)

### Community 116 - ".BroadcastSnapshot"
Cohesion: 0.13
Nodes (9): List, name, slot, Perms, SinglePlayer, List, name, slot (+1 more)

### Community 117 - "com.unity.modules.unitywebrequest"
Cohesion: 0.33
Nodes (6): com.unity.modules.unitywebrequest, dependencies, depth, source, version, com.unity.modules.unitywebrequest

### Community 118 - "Celebration"
Cohesion: 0.18
Nodes (7): e, name, Celebration, CurrentEmote, Playing, Progress01, Emote

### Community 119 - "AudioManager"
Cohesion: 0.08
Nodes (15): AudioClip, Dictionary, GUIStyle, IEnumerator, RuntimeInitializeOnLoadMethod, Vector3, AudioManager, Instance (+7 more)

### Community 120 - "Playlist"
Cohesion: 0.25
Nodes (7): AudioClip, Playlist, Count, Current, Track, Song, Track

### Community 121 - ".Box"
Cohesion: 0.15
Nodes (15): Collider, Color, Material, Renderer, Transform, Vector3, Crowd, FanCount (+7 more)

### Community 123 - "HostSetupUI"
Cohesion: 0.32
Nodes (4): Action, GUIStyle, Vector3, HostSetupUI

### Community 124 - ".BuildGoal"
Cohesion: 0.19
Nodes (14): Goal, GoalFrame, Refs, NetBackstop, Collider, Material, MeshFilter, MeshRenderer (+6 more)

### Community 125 - "grassprep.py"
Cohesion: 0.60
Nodes (4): load(), main(), member(), Build the turf detail layer in Assets/Resources/Turf from an ambientCG scan.…

### Community 126 - "TitleGlyph"
Cohesion: 0.33
Nodes (4): Color32, Texture2D, TitleGlyph, K

### Community 127 - "MonoBehaviour"
Cohesion: 0.09
Nodes (15): Action, Collider, Goal, Action, MatchModeUI, Action, MultiplayerHubUI, Material (+7 more)

### Community 128 - ".Scrim"
Cohesion: 0.08
Nodes (16): Matrix4x4, MenuScale, Active, Factor, Height, UserScale, Width, Action (+8 more)

### Community 131 - ".Set"
Cohesion: 0.38
Nodes (4): e, Vector3, KeeperPose, b

### Community 132 - ".Place"
Cohesion: 0.20
Nodes (9): GameObject, Material, Mesh, MeshFilter, MeshRenderer, Transform, Vector2, Vector3 (+1 more)

### Community 133 - "SessionBrowserUI"
Cohesion: 0.24
Nodes (3): Action, List, SessionBrowserUI

### Community 134 - "SlotKind"
Cohesion: 0.14
Nodes (8): List, SlotKind, Skin, StyleA, StyleB, StyleC, Color, SpeciesCosmetics

### Community 136 - "Cat"
Cohesion: 0.29
Nodes (7): Cat, Accuracy, FreeKick, Friends, Match, Overall, Rank

### Community 137 - "Striker"
Cohesion: 0.06
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
Nodes (15): Func, Transform, Vector3, GameCamera, BallCam, KeeperLookDownFraction, KeeperLookYaw, Pitch (+7 more)

### Community 143 - "GameInput"
Cohesion: 0.05
Nodes (39): Vector2, GameInput, BallCamPressed, CloseControlHeld, CrossHeld, CrossMapPressed, CrossPressed, CursorCaptured (+31 more)

### Community 144 - "Crosser"
Cohesion: 0.09
Nodes (20): Collider, Material, Renderer, Transform, Vector3, AimReticle, Active, TargetPoint (+12 more)

### Community 145 - "Trick"
Cohesion: 0.40
Nodes (5): Trick, Dive, None, SlideLimp, Tumble

### Community 146 - "Achievements"
Cohesion: 0.20
Nodes (8): SteamAchievementsAPI, Available, List, Achievements, FilePath, Unlocked, UnlockedSet, UnlockedSet

### Community 147 - "Touch"
Cohesion: 0.29
Nodes (7): Touch, Carry, Contact, Keeper, Pass, Shot, Tackle

### Community 148 - "ShotType"
Cohesion: 0.25
Nodes (7): ShotType, Bicycle, DivingHeader, Header, Normal, ThirdLeg, Volley

### Community 149 - "DecorTint"
Cohesion: 0.29
Nodes (7): DecorTint, Dark, Limb, Skin, StyleA, StyleB, StyleC

### Community 150 - ".Init"
Cohesion: 0.40
Nodes (3): InputAction, InputActionAsset, PlayerInput

### Community 151 - "JoinRefusal"
Cohesion: 0.40
Nodes (5): JoinRefusal, MatchRunning, None, NoSlot, Version

### Community 152 - "Role"
Cohesion: 0.33
Nodes (5): Role, Crosser, Goalkeeper, Sniper, Striker

### Community 154 - "State"
Cohesion: 0.33
Nodes (6): State, Charging, Idle, Runup, Settle, Struck

### Community 155 - "Phase"
Cohesion: 0.40
Nodes (5): Phase, Attack, Defend, Loose, Restart

### Community 156 - "Achievements.cs"
Cohesion: 0.40
Nodes (5): Func, AchievementDef, AchievementKind, LeaderboardTop, StatThreshold

### Community 157 - "NotificationToastUI"
Cohesion: 0.33
Nodes (4): List, NotificationToastUI, Toast, Toast

### Community 159 - "CareerStatsData"
Cohesion: 0.73
Nodes (4): CareerStatsData, ModeStats, OnlineRanks, RankData

### Community 160 - "Phase"
Cohesion: 0.50
Nodes (4): Phase, Armed, Live, Settle

### Community 161 - "AtomicFileWriter"
Cohesion: 0.29
Nodes (4): Dictionary, AtomicFileWriter, Job, Job

### Community 162 - "Phase"
Cohesion: 0.50
Nodes (4): Phase, Connecting, Playlist, Searching

### Community 163 - "PassKind"
Cohesion: 0.50
Nodes (4): PassKind, Air, Chip, Ground

### Community 164 - "Kind"
Cohesion: 0.67
Nodes (3): Kind, Bad, Normal

### Community 165 - "State"
Cohesion: 0.50
Nodes (4): State, Diving, Guard, Holding

### Community 170 - ".Draw"
Cohesion: 0.16
Nodes (11): GUIStyle, Rect, Vector2, GoalEditor, MaxH, MaxW, MinH, MinW (+3 more)

### Community 171 - "Category"
Cohesion: 0.20
Nodes (10): Category, Agility, Control, Heading, Instinct, Pace, Passing, Shooting (+2 more)

### Community 176 - ".Label"
Cohesion: 0.07
Nodes (18): action, Dictionary, label, Keybinds, Current, Action, Rect, Action (+10 more)

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
- **868 isolated node(s):** `ZipEnabled`, `CursorCaptured`, `Move`, `Look`, `JumpPressed` (+863 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1217 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **12 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `Trickshot` to `CareerStatsUI`, `JerseyDesigns`, `SimConfig`, `Dribble`, `SetPieceMap`, `SkillTree`, `Cosmetics`, `DefensiveWall`, `AdultQuiz`, `Species`, `Bone`, `MenuIcons`, `PitchLayout`, `GameBootstrap`, `BuildAll`, `MatchProbe`, `PauseMenu`, `Passing`, `BodyLayoutDef`, `KeeperController`, `GameManager`, `AnatomySim`, `KeeperGame`, `AccuracyGame`, `QuickChatFeed`, `CrossMap`, `FreeKickGame`, `SetPieceTaker`, `HairSim`, `Vector3`, `SkyDome`, `PropKit`, `AccuracyTarget`, `MatchGame`, `IStrikerInput`, `Make`, `Sniper`, `.Draw`, `KeeperHands`, `Multiplayer`, `DisplaySettings`, `Footballer`, `StatRadar`, `CrosserControl`, `.Pose`, `PlayerPreview`, `.Divider`, `Knockdown`, `.Build`, `.RequestFriendsList`, `.Build`, `.Build`, `ReplaySystem`, `.SetPoseOverride`, `MenuBackground`, `Turf`, `StadiumStyle`, `FlexNet`, `Celebration`, `AudioManager`, `Playlist`, `.Box`, `HostSetupUI`, `.BuildGoal`, `TitleGlyph`, `MonoBehaviour`, `.Scrim`, `AssetImportRules`, `.Set`, `.Place`, `SessionBrowserUI`, `SlotKind`, `Striker`, `.DriveTowardRotation`, `UIFont`, `GameInput`, `Crosser`, `ShotType`, `Role`, `Achievements.cs`, `NotificationToastUI`, `CallLimiter`, `CareerStatsData`, `AtomicFileWriter`, `IPlayerController`, `.Draw`, `.Label`, `StudioSplash`?**
  _High betweenness centrality (0.154) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `ActiveRagdoll` to `SimConfig`, `Dribble`, `Trickshot`, `Striker`, `Species`, `GameCamera`, `Goalkeeper`, `Crosser`, `Bone`, `.Mat`, `NetSetPieceMatch`, `Passing`, `NetStrikerMatch`, `BodyLayoutDef`, `KeeperController`, `GameManager`, `AnatomySim`, `KeeperGame`, `AccuracyGame`, `.Update`, `FreeKickGame`, `SetPieceTaker`, `HairSim`, `Vector3`, `NetMatch`, `MatchGame`, `Make`, `Sniper`, `.MatTex`, `KeeperHands`, `Footballer`, `CrosserControl`, `.Pose`, `PlayerPreview`, `Hud`, `Knockdown`, `ReplaySystem`, `.SetPoseOverride`, `MenuBackground`, `BallController`, `Celebration`, `MonoBehaviour`?**
  _High betweenness centrality (0.132) - this node is a cross-community bridge._
- **Why does `NetSession` connect `NetSession` to `.Divider`, `.MatTex`, `Color32`, `Trickshot`, `QuickChatFeed`, `CrosserSetupMsg`, `Multiplayer`, `.RouteMessage`, `NetMessages.cs`, `.BroadcastSnapshot`, `NetMatch`, `JoinRefusal`, `MsgType`, `PeerId`, `.Mat`, `NetSetPieceMatch`, `NetStrikerMatch`?**
  _High betweenness centrality (0.111) - this node is a cross-community bridge._
- **What connects `ZipEnabled`, `CursorCaptured`, `Move` to the rest of the system?**
  _868 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `CareerStatsUI` be split into smaller, more focused modules?**
  _Cohesion score 0.14245014245014245 - nodes in this community are weakly interconnected._
- **Should `JerseyDesigns` be split into smaller, more focused modules?**
  _Cohesion score 0.11428571428571428 - nodes in this community are weakly interconnected._
- **Should `SimConfig` be split into smaller, more focused modules?**
  _Cohesion score 0.08870967741935484 - nodes in this community are weakly interconnected._