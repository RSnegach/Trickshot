# Graph Report - Trickshot  (2026-09-01)

## Corpus Check
- 155 files · ~678,653 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 3606 nodes · 9144 edges · 169 communities (153 shown, 14 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 860 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `9d6a4a54`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- CustomizeUI
- JerseyDesigns
- SimConfig
- Dribble
- SetPieceMap
- DirectIpTransport
- SkillTree
- SkillIcons
- Trickshot
- Cosmetics
- HairSim
- AdultQuiz
- Species
- CareerStats
- Goalkeeper
- Bone
- .RouteMessage
- MenuIcons
- .Build
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
- NetMessages.cs
- AnatomySim
- KeeperHands
- NetEndpoint
- com.unity.modules.uielements
- com.unity.modules.physics
- com.unity.modules.imageconversion
- AccuracyGame
- QuickChatFeed
- AimReticle
- CrossMap
- FreeKickGame
- SetPieceTaker
- .DrawKeybindings
- EmotePose
- SkyDome
- com.unity.modules.hierarchycore
- NetWriter
- PropKit
- .Scrim
- AccuracyTarget
- dependencies
- com.unity.modules.androidjni
- MatchGame
- IStrikerInput
- FlexNet
- Sniper
- com.unity.modules.imgui
- com.unity.modules.animation
- com.unity.modules.audio
- dependencies
- DefensiveWall
- UITheme
- TitleGlyph
- graphify knowledge graph
- .Rect
- com.unity.ext.nunit
- TailnetDiscovery
- dependencies
- Trickshot (3D trick-shot football prototype)
- AudioManager
- Multiplayer
- Hair Strand Texture Atlas
- LobbyProbe
- DisplaySettings
- Footballer
- NetMatch
- NetInputSource
- StatRadar
- Hair Atlas Asset License
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- CrosserControl
- MsgType
- INetTransport
- .Pose
- Crowd
- ReliableChannel
- PlayerProfile
- PlayerPreview
- Hud
- .DrawLobby
- Knockdown
- .Build
- .RequestFriendsList
- .Build
- .SlotSubMenu
- SurroundBuilder
- .Build
- MonoBehaviour
- Gait
- MenuBackground
- .Place
- BallController
- skyprep.py
- com.unity.nuget.newtonsoft-json
- Turf
- StadiumStyle
- LocalTransport
- com.unity.modules.screencapture
- KeeperGame
- .Draw
- com.unity.modules.unitywebrequest
- .Chan
- Playlist
- .Box
- BallController.cs
- AnimState
- .Build
- grassprep.py
- SetPieceSpin
- Goal
- OptionsMenu
- AssetImportRules
- .Set
- .Clean
- SessionBrowserUI
- .TickNetPass
- postprep.py
- Phase
- Striker
- Reason
- .DriveTowardRotation
- Trickshot: Replayability Brainstorm
- UIFont
- GameCamera
- GameInput
- Crosser
- State
- Achievements
- Touch
- ShotType
- .Fell
- State
- State
- Role
- .Update
- graphify
- Phase
- Channel
- NotificationToastUI
- CallLimiter
- Outcome
- Phase
- Phase
- StadiumSelectUI
- TackleResult
- Trick
- .Awake
- .WhistleTriple
- .AutoStart
- ShotBand

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 192 edges
2. `Trickshot` - 129 edges
3. `NetSession` - 123 edges
4. `BallController` - 115 edges
5. `MatchGame` - 102 edges
6. `CustomizeUI` - 81 edges
7. `JerseyDesigns` - 79 edges
8. `GameInput` - 75 edges
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

## Communities (169 total, 14 thin omitted)

### Community 0 - "CustomizeUI"
Cohesion: 0.08
Nodes (15): Action, Color, Color32, Dictionary, Func, GUIStyle, IEnumerator, Rect (+7 more)

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
Cohesion: 0.22
Nodes (10): Color, Random, Rect, Vector2, Vector3, SetPieceMap, BottomZ, HalfW (+2 more)

### Community 5 - "DirectIpTransport"
Cohesion: 0.10
Nodes (15): ConcurrentQueue, data, Dictionary, from, Func, IPEndPoint, List, DirectIpTransport (+7 more)

### Community 6 - "SkillTree"
Cohesion: 0.06
Nodes (26): Dictionary, HashSet, IEnumerable, List, Category, Agility, Control, Heading (+18 more)

### Community 7 - "SkillIcons"
Cohesion: 0.19
Nodes (4): Color32, Dictionary, Texture2D, SkillIcons

### Community 8 - "Trickshot"
Cohesion: 0.06
Nodes (3): AchievementsPanelUI, Trickshot.Net, Trickshot

### Community 9 - "Cosmetics"
Cohesion: 0.13
Nodes (26): AccessoryEntry, Action, Collider, IReadOnlyList, List, Material, Mesh, MeshFilter (+18 more)

### Community 10 - "HairSim"
Cohesion: 0.11
Nodes (20): Material, Matrix4x4, Mesh, MeshFilter, MeshRenderer, Transform, Vector2, Vector3 (+12 more)

### Community 11 - "AdultQuiz"
Cohesion: 0.50
Nodes (3): AdultQuiz, Q, Q

### Community 12 - "Species"
Cohesion: 0.13
Nodes (12): BodyPlan, Biped, Quadruped, HeaderAction, Biped, Species, Current, SpeciesAxis (+4 more)

### Community 13 - "CareerStats"
Cohesion: 0.06
Nodes (22): Action, label, CareerStatsUI, Cat, Accuracy, FreeKick, Friends, Match (+14 more)

### Community 14 - "Goalkeeper"
Cohesion: 0.10
Nodes (15): Func, Quaternion, Vector3, Band, High, Jump, Low, Mid (+7 more)

### Community 15 - "Bone"
Cohesion: 0.09
Nodes (19): Transform, Vector3, Bone, CalfL, CalfR, Count, FootL, FootR (+11 more)

### Community 16 - ".RouteMessage"
Cohesion: 0.25
Nodes (5): Color, NetReader, More, Type, BinaryReader

### Community 17 - "MenuIcons"
Cohesion: 0.26
Nodes (4): Color32, Dictionary, Texture2D, MenuIcons

### Community 18 - ".Build"
Cohesion: 0.23
Nodes (5): Material, Renderer, SphereCollider, PlayerAppearance, Default

### Community 19 - "DirectIpTransport.cs (direct-IP UDP)"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 20 - "GameBootstrap"
Cohesion: 0.10
Nodes (14): GameMode, Accuracy, FreeKick, Goalkeeper, Match, SetPieces, Striker, Action (+6 more)

### Community 21 - "com.unity.modules.jsonserialize"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "ActiveRagdoll"
Cohesion: 0.06
Nodes (27): Collider, Transform, CrosserBubble, Bounds, Collider, ConfigurableJoint, Dictionary, IReadOnlyList (+19 more)

### Community 23 - "NetSession"
Cohesion: 0.04
Nodes (40): appr, PeerId, IsValid, JerseyChunkMsg, NetRole, Crosser, Keeper, Shooter (+32 more)

### Community 24 - "BuildAll"
Cohesion: 0.12
Nodes (8): Action, BuildAll, ZipEnabled, Plat, BuildTarget, MenuItem, Plat, Type

### Community 25 - "MatchProbe"
Cohesion: 0.14
Nodes (9): List, Vector3, MatchProbe, Overlay, ProbeTackle, Ai, Human, Slide (+1 more)

### Community 26 - ".Mat"
Cohesion: 0.28
Nodes (6): Camera, Material, Refs, Renderer, Rigidbody, Transform

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.10
Nodes (3): ShootoutState, Vector3, NetSetPieceMatch

### Community 28 - "PauseMenu"
Cohesion: 0.15
Nodes (10): Action, List, Entry, Kind, Bad, Normal, PauseMenu, Paused (+2 more)

### Community 29 - "Passing"
Cohesion: 0.12
Nodes (12): List, Vector3, Bar, AnyArmed, Option, Passing, PassKind, Air (+4 more)

### Community 30 - "NetStrikerMatch"
Cohesion: 0.07
Nodes (14): Texture2D, Goalkeeper, Striker, KickDetector, Rigidbody, Body, Crosser, Material (+6 more)

### Community 31 - "BodyLayoutDef"
Cohesion: 0.11
Nodes (23): Vector3, BodyLayout, BodyLayoutDef, ParentByBone, BoneSpec, ColliderKind, Box, CapsuleY (+15 more)

### Community 32 - "Emote"
Cohesion: 0.06
Nodes (34): Emote, Backflip, Bow, Charleston, Cheer, Clap, Crip, Dab (+26 more)

### Community 33 - "KeeperController"
Cohesion: 0.10
Nodes (11): Func, Quaternion, Vector2, Vector3, KeeperController, Body, Hands, HasBall (+3 more)

### Community 34 - "GameManager"
Cohesion: 0.11
Nodes (11): Transform, Vector3, GameManager, CrossMapEscapeOwned, SaveWatch, Armed, Epic, Touched (+3 more)

### Community 35 - "NetMessages.cs"
Cohesion: 0.15
Nodes (10): BodyState, JoinRefusal, MatchRunning, None, NoSlot, Version, LobbySlot, ShootoutState (+2 more)

### Community 36 - "AnatomySim"
Cohesion: 0.26
Nodes (7): Collider, Color, GameObject, Material, Transform, Vector3, AnatomySim

### Community 37 - "KeeperHands"
Cohesion: 0.18
Nodes (5): Collider, Vector3, KeeperHands, HeldFor, Holding

### Community 38 - "NetEndpoint"
Cohesion: 0.20
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
Cohesion: 0.19
Nodes (6): Vector3, AccuracyGame, Phase, Armed, Cooldown, Live

### Community 43 - "QuickChatFeed"
Cohesion: 0.13
Nodes (8): List, Queue, Line, QuickChatFeed, AnyOpen, EscapeOwned, Typing, Line

### Community 44 - "AimReticle"
Cohesion: 0.18
Nodes (8): Collider, Material, Renderer, Transform, Vector3, AimReticle, Active, TargetPoint

### Community 45 - "CrossMap"
Cohesion: 0.31
Nodes (8): Color, Rect, Vector2, Vector3, CrossMap, BottomZ, HalfW, TopZ

### Community 46 - "FreeKickGame"
Cohesion: 0.19
Nodes (5): Collider, Random, Vector3, FreeKickGame, Outcome

### Community 47 - "SetPieceTaker"
Cohesion: 0.14
Nodes (11): Func, Quaternion, Vector3, SetPieceTaker, Active, Done, HasCharged, IsCharging (+3 more)

### Community 48 - ".DrawKeybindings"
Cohesion: 0.13
Nodes (7): Action, RebindingOperation, action, Dictionary, label, Keybinds, Current

### Community 49 - "EmotePose"
Cohesion: 0.45
Nodes (4): Action, Vector3, EmotePose, Emote

### Community 50 - "SkyDome"
Cohesion: 0.24
Nodes (7): Color, Dictionary, Light, Material, Shader, Texture2D, SkyDome

### Community 51 - "com.unity.modules.hierarchycore"
Cohesion: 0.15
Nodes (13): com.unity.modules.hierarchycore, dependencies, depth, source, version, dependencies, depth, source (+5 more)

### Community 52 - "NetWriter"
Cohesion: 0.15
Nodes (7): PlayerAppearance, Vector3, NetCodec, NetWriter, Vector3, BinaryWriter, MemoryStream

### Community 53 - "PropKit"
Cohesion: 0.19
Nodes (13): Bounds, Collider, Dictionary, GameObject, HashSet, Material, MeshFilter, MeshRenderer (+5 more)

### Community 54 - ".Scrim"
Cohesion: 0.09
Nodes (10): Action, Rect, Matrix4x4, MenuScale, Active, Factor, Height, UserScale (+2 more)

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
Cohesion: 0.06
Nodes (19): Dictionary, Func, HashSet, List, Vector3, MatchGame, AwayScore, ClockRemaining (+11 more)

### Community 59 - "IStrikerInput"
Cohesion: 0.07
Nodes (25): Vector2, IStrikerInput, CloseControlHeld, EmoteId, Fresh, JumpHeld, JumpPressed, JumpReleased (+17 more)

### Community 60 - "FlexNet"
Cohesion: 0.13
Nodes (13): Func, List, Material, Mesh, MeshFilter, MeshRenderer, Transform, Vector3 (+5 more)

### Community 61 - "Sniper"
Cohesion: 0.28
Nodes (6): Action, Rigidbody, Transform, Vector3, Sniper, LineRenderer

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

### Community 66 - "DefensiveWall"
Cohesion: 0.15
Nodes (14): CapsuleCollider, Collider, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion (+6 more)

### Community 67 - "UITheme"
Cohesion: 0.10
Nodes (29): Action, Rect, GUIStyle, Color, GUIStyle, Matrix4x4, Rect, Texture2D (+21 more)

### Community 68 - "TitleGlyph"
Cohesion: 0.33
Nodes (4): Color32, Texture2D, TitleGlyph, K

### Community 69 - "graphify knowledge graph"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - ".Rect"
Cohesion: 0.16
Nodes (5): Action, GUIStyle, ScrimPos, Vector3, PrematchUI

### Community 71 - "com.unity.ext.nunit"
Cohesion: 0.17
Nodes (12): com.unity.ext.nunit, com.unity.test-framework, dependencies, depth, source, version, dependencies, depth (+4 more)

### Community 72 - "TailnetDiscovery"
Cohesion: 0.17
Nodes (14): Action, LobbyInfo, Action, ConcurrentQueue, IPAddress, IPEndPoint, List, TailnetDiscovery (+6 more)

### Community 73 - "dependencies"
Cohesion: 0.17
Nodes (12): com.unity.modules.physics2d, dependencies, depth, hash, source, version, dependencies, depth (+4 more)

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "AudioManager"
Cohesion: 0.19
Nodes (5): Dictionary, GUIStyle, AudioManager, Instance, Channel

### Community 76 - "Multiplayer"
Cohesion: 0.08
Nodes (17): List, RuntimeInitializeOnLoadMethod, Multiplayer, IsActive, IsClient, IsHost, Session, SteamLinked (+9 more)

### Community 77 - "Hair Strand Texture Atlas"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 79 - "DisplaySettings"
Cohesion: 0.21
Nodes (7): DisplaySettings, Available, FovOffset, UiScale, VSync, FullScreenMode, Resolution

### Community 80 - "Footballer"
Cohesion: 0.09
Nodes (19): AiTuning, List, Vector2, Vector3, Footballer, IsDown, Keeper, KeeperHoldingBall (+11 more)

### Community 81 - "NetMatch"
Cohesion: 0.08
Nodes (14): e, name, Celebration, CurrentEmote, Playing, Progress01, Bar, Material (+6 more)

### Community 82 - "NetInputSource"
Cohesion: 0.06
Nodes (30): InputFrame, Vector2, NetInputSource, CloseControlHeld, EmoteId, Fresh, JumpHeld, JumpPressed (+22 more)

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
Cohesion: 0.24
Nodes (4): Func, CrosserControl, Charge01, Holding

### Community 88 - "MsgType"
Cohesion: 0.09
Nodes (22): MsgType, AssignSlot, BallKick, CastJerseyVote, Hello, JerseyChunk, MatchEvent, MatchStats (+14 more)

### Community 89 - "INetTransport"
Cohesion: 0.06
Nodes (22): Action, Func, List, INetTransport, AdvertProvider, HostPeer, IsHost, IsRunning (+14 more)

### Community 90 - ".Pose"
Cohesion: 0.25
Nodes (3): Vector3, KickSwing, LocalFoot

### Community 91 - "Crowd"
Cohesion: 0.08
Nodes (26): Collider, Color, Material, Transform, Vector3, Crowd, FanCount, IEnumerable (+18 more)

### Community 92 - "ReliableChannel"
Cohesion: 0.19
Nodes (7): Dictionary, List, Pending, ReliableChannel, CumAck, HasUnacked, Pending

### Community 93 - "PlayerProfile"
Cohesion: 0.03
Nodes (59): Color, label, Texture2D, PlayerProfile, AgilityStat, AirFlipMul, Bias, BicycleSkill01 (+51 more)

### Community 94 - "PlayerPreview"
Cohesion: 0.10
Nodes (14): Color, GameObject, Light, Material, Quaternion, Rect, Renderer, Texture2D (+6 more)

### Community 95 - "Hud"
Cohesion: 0.09
Nodes (15): Color, GUIStyle, List, Rect, Vector2, Vector3, Hud, H (+7 more)

### Community 96 - ".DrawLobby"
Cohesion: 0.15
Nodes (6): Action, GUIStyle, Vector3, HostSetupUI, Color, LobbyUI

### Community 97 - "Knockdown"
Cohesion: 0.24
Nodes (4): Knockdown, Beaten, Down, Strk

### Community 98 - ".Build"
Cohesion: 0.31
Nodes (9): Collider, Color, Material, PhysicsMaterial, Transform, Vector3, StadiumBuilder, CornerPylonScale (+1 more)

### Community 99 - ".RequestFriendsList"
Cohesion: 0.20
Nodes (7): Action, List, SteamFriendInfo, SteamFriendsAPI, Available, List, FriendsPanelUI

### Community 100 - ".Build"
Cohesion: 0.33
Nodes (7): PhysicsMaterial, Collider, Material, Transform, Vector3, PitchBuilder, PhysicsMaterialCombine

### Community 101 - ".SlotSubMenu"
Cohesion: 0.18
Nodes (7): SlotKind, Skin, StyleA, StyleB, StyleC, Color, SpeciesCosmetics

### Community 102 - "SurroundBuilder"
Cohesion: 0.21
Nodes (11): CapsuleCollider, Collider, Color, Material, Texture2D, Transform, Vector3, SurroundBuilder (+3 more)

### Community 103 - ".Build"
Cohesion: 0.29
Nodes (10): Collider, Material, MeshFilter, MeshRenderer, PhysicsMaterial, Renderer, Transform, Vector3 (+2 more)

### Community 104 - "MonoBehaviour"
Cohesion: 0.10
Nodes (15): NetPump, Action, MatchModeUI, Action, MultiplayerHubUI, List, Quaternion, Rigidbody (+7 more)

### Community 105 - "Gait"
Cohesion: 0.34
Nodes (4): Vector3, Gait, Profile, Profile

### Community 106 - "MenuBackground"
Cohesion: 0.14
Nodes (11): Collider, Color, Light, List, Material, PhysicsMaterial, Quaternion, Renderer (+3 more)

### Community 107 - ".Place"
Cohesion: 0.20
Nodes (9): GameObject, Material, Mesh, MeshFilter, MeshRenderer, Transform, Vector2, Vector3 (+1 more)

### Community 108 - "BallController"
Cohesion: 0.09
Nodes (18): Collision, Rigidbody, SphereCollider, Vector3, BallController, DribbleCarrier, DribbleHold, Guided (+10 more)

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
Cohesion: 0.14
Nodes (11): Color, Vector3, StadiumStyle, Active, FirstPickable, SelectedIndex, Surroundings, Flags (+3 more)

### Community 113 - "LocalTransport"
Cohesion: 0.11
Nodes (13): Action, data, Dictionary, from, Func, List, Queue, LocalTransport (+5 more)

### Community 114 - "com.unity.modules.screencapture"
Cohesion: 0.33
Nodes (6): com.unity.modules.screencapture, dependencies, depth, source, version, com.unity.modules.screencapture

### Community 115 - "KeeperGame"
Cohesion: 0.14
Nodes (5): CrowdCheer, KeeperGame, Vector3, ShotServer, JustFired

### Community 116 - ".Draw"
Cohesion: 0.16
Nodes (9): Color, GUIStyle, List, MatchStatsUI, Tab, Away, Home, Rect (+1 more)

### Community 117 - "com.unity.modules.unitywebrequest"
Cohesion: 0.33
Nodes (6): com.unity.modules.unitywebrequest, dependencies, depth, source, version, com.unity.modules.unitywebrequest

### Community 120 - "Playlist"
Cohesion: 0.25
Nodes (7): AudioClip, Playlist, Count, Current, Track, Song, Track

### Community 121 - ".Box"
Cohesion: 0.16
Nodes (18): BoxCollider, CapsuleCollider, Rigidbody, Collider, Color, GameObject, Material, MeshFilter (+10 more)

### Community 123 - "AnimState"
Cohesion: 0.10
Nodes (17): AnimState, Dive, Down, Idle, Jump, Kick, Run, Sit (+9 more)

### Community 124 - ".Build"
Cohesion: 0.17
Nodes (14): Goal, GoalFrame, Refs, NetBackstop, Collider, Material, MeshFilter, MeshRenderer (+6 more)

### Community 125 - "grassprep.py"
Cohesion: 0.60
Nodes (4): load(), main(), member(), Build the turf detail layer in Assets/Resources/Turf from an ambientCG scan.…

### Community 126 - "SetPieceSpin"
Cohesion: 0.33
Nodes (6): SetPieceSpin, CurveLeft, CurveRight, Knuckle, None, TopSpin

### Community 127 - "Goal"
Cohesion: 0.33
Nodes (3): Action, Collider, Goal

### Community 128 - "OptionsMenu"
Cohesion: 0.12
Nodes (14): Action, GUIStyle, RebindingOperation, Rect, Vector2, OptionsMenu, IsRebinding, Tab (+6 more)

### Community 131 - ".Set"
Cohesion: 0.38
Nodes (4): e, Vector3, KeeperPose, b

### Community 133 - "SessionBrowserUI"
Cohesion: 0.27
Nodes (3): Action, List, SessionBrowserUI

### Community 136 - "Phase"
Cohesion: 0.33
Nodes (6): Phase, CareerStats, Hub, SinglePlayer, Splash, Zoo

### Community 137 - "Striker"
Cohesion: 0.06
Nodes (20): IPlayerController, Collider, Func, Vector3, Striker, FacingForward, HasLookAim, IsBusy (+12 more)

### Community 138 - "Reason"
Cohesion: 0.40
Nodes (5): Reason, NoCli, NoPeers, Ok, TailnetDown

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
Cohesion: 0.09
Nodes (19): Func, Transform, Vector3, GameCamera, BallCam, KeeperLookDownFraction, KeeperLookYaw, Pitch (+11 more)

### Community 143 - "GameInput"
Cohesion: 0.05
Nodes (40): Vector2, GameInput, BallCamPressed, CloseControlHeld, CrossMapPressed, CursorCaptured, EmoteHeld, EmoteId (+32 more)

### Community 144 - "Crosser"
Cohesion: 0.21
Nodes (8): Quaternion, Transform, Vector3, Crosser, JustServed, Origin, Ragdoll, ReadyToServe

### Community 145 - "State"
Cohesion: 0.50
Nodes (4): State, Diving, Guard, Holding

### Community 146 - "Achievements"
Cohesion: 0.14
Nodes (13): SteamAchievementsAPI, Available, Func, List, AchievementDef, AchievementKind, LeaderboardTop, StatThreshold (+5 more)

### Community 147 - "Touch"
Cohesion: 0.29
Nodes (7): Touch, Carry, Contact, Keeper, Pass, Shot, Tackle

### Community 148 - "ShotType"
Cohesion: 0.29
Nodes (6): ShotType, Bicycle, DivingHeader, Header, Normal, Volley

### Community 150 - "State"
Cohesion: 0.33
Nodes (6): State, Charging, Idle, Runup, Settle, Struck

### Community 151 - "State"
Cohesion: 0.33
Nodes (6): State, Diving, Holding, Ready, Saving, Stumble

### Community 152 - "Role"
Cohesion: 0.33
Nodes (5): Role, Crosser, Goalkeeper, Sniper, Striker

### Community 154 - "graphify"
Cohesion: 0.40
Nodes (5): graphify, unityMCP, C:/Users/evrik/AppData/Local/Microsoft/WinGet/Packages/astral-sh.uv_Microsoft.Winget.Source_8wekyb3d8bbwe/uvx.exe, graphify-mcp, mcp-for-unity

### Community 155 - "Phase"
Cohesion: 0.40
Nodes (5): Phase, Attack, Defend, Loose, Restart

### Community 156 - "Channel"
Cohesion: 0.40
Nodes (5): Channel, Crowd, Master, Music, Sfx

### Community 157 - "NotificationToastUI"
Cohesion: 0.33
Nodes (4): List, NotificationToastUI, Toast, Toast

### Community 159 - "Outcome"
Cohesion: 0.50
Nodes (4): Outcome, Blocked, Miss, Save

### Community 160 - "Phase"
Cohesion: 0.50
Nodes (4): Phase, Armed, Live, Settle

### Community 161 - "Phase"
Cohesion: 0.50
Nodes (4): Phase, Armed, Cooldown, Live

### Community 163 - "TackleResult"
Cohesion: 0.33
Nodes (6): TackleResult, Beaten, Foul, NoCarrier, Won, WrongSide

### Community 164 - "Trick"
Cohesion: 0.50
Nodes (4): Trick, Dive, None, SlideLimp

### Community 169 - "ShotBand"
Cohesion: 0.50
Nodes (4): ShotBand, Chip, Drive, Placed

## Knowledge Gaps
- **819 isolated node(s):** `mcp-for-unity`, `graphify-mcp`, `ZipEnabled`, `CursorCaptured`, `Move` (+814 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1151 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **14 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `Trickshot` to `JerseyDesigns`, `SimConfig`, `Dribble`, `SetPieceMap`, `SkillTree`, `Cosmetics`, `HairSim`, `AdultQuiz`, `Species`, `CareerStats`, `Bone`, `MenuIcons`, `GameBootstrap`, `ActiveRagdoll`, `BuildAll`, `MatchProbe`, `PauseMenu`, `BodyLayoutDef`, `GameManager`, `AnatomySim`, `AccuracyGame`, `QuickChatFeed`, `AimReticle`, `CrossMap`, `FreeKickGame`, `SetPieceTaker`, `.DrawKeybindings`, `SkyDome`, `PropKit`, `.Scrim`, `AccuracyTarget`, `MatchGame`, `IStrikerInput`, `FlexNet`, `Sniper`, `DefensiveWall`, `TitleGlyph`, `.Rect`, `AudioManager`, `Multiplayer`, `DisplaySettings`, `Footballer`, `NetMatch`, `StatRadar`, `CrosserControl`, `.Pose`, `Crowd`, `PlayerProfile`, `PlayerPreview`, `.DrawLobby`, `Knockdown`, `.Build`, `.RequestFriendsList`, `.SlotSubMenu`, `.Build`, `MonoBehaviour`, `Gait`, `MenuBackground`, `.Place`, `Turf`, `StadiumStyle`, `KeeperGame`, `.Draw`, `Playlist`, `.Build`, `Goal`, `OptionsMenu`, `AssetImportRules`, `.Set`, `.Clean`, `Striker`, `.DriveTowardRotation`, `UIFont`, `GameInput`, `Crosser`, `Achievements`, `ShotType`, `Role`, `NotificationToastUI`, `CallLimiter`, `StadiumSelectUI`?**
  _High betweenness centrality (0.176) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `ActiveRagdoll` to `SimConfig`, `Dribble`, `Trickshot`, `Striker`, `HairSim`, `Species`, `Goalkeeper`, `Bone`, `Crosser`, `.Build`, `.Fell`, `.Mat`, `NetSetPieceMatch`, `Passing`, `NetStrikerMatch`, `BodyLayoutDef`, `KeeperController`, `GameManager`, `AnatomySim`, `KeeperHands`, `AccuracyGame`, `AimReticle`, `FreeKickGame`, `SetPieceTaker`, `MatchGame`, `Sniper`, `Footballer`, `NetMatch`, `.Pose`, `PlayerPreview`, `Hud`, `Knockdown`, `MonoBehaviour`, `MenuBackground`, `BallController`, `KeeperGame`, `.Box`?**
  _High betweenness centrality (0.141) - this node is a cross-community bridge._
- **Why does `NetSession` connect `NetSession` to `.DrawLobby`, `.Update`, `NetMessages.cs`, `SkillTree`, `Trickshot`, `AnimState`, `Multiplayer`, `QuickChatFeed`, `GameCamera`, `.RouteMessage`, `NetMatch`, `NetInputSource`, `NetWriter`, `MsgType`, `INetTransport`, `NetSetPieceMatch`, `NetStrikerMatch`?**
  _High betweenness centrality (0.115) - this node is a cross-community bridge._
- **What connects `mcp-for-unity`, `graphify-mcp`, `ZipEnabled` to the rest of the system?**
  _819 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `CustomizeUI` be split into smaller, more focused modules?**
  _Cohesion score 0.0821917808219178 - nodes in this community are weakly interconnected._
- **Should `JerseyDesigns` be split into smaller, more focused modules?**
  _Cohesion score 0.11428571428571428 - nodes in this community are weakly interconnected._
- **Should `SimConfig` be split into smaller, more focused modules?**
  _Cohesion score 0.05757575757575758 - nodes in this community are weakly interconnected._