# Graph Report - Trickshot  (2026-09-01)

## Corpus Check
- 154 files · ~677,219 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 3590 nodes · 9069 edges · 170 communities (154 shown, 12 thin omitted)
- Extraction: 91% EXTRACTED · 9% INFERRED · 0% AMBIGUOUS · INFERRED: 854 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `9bcfbbec`
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
- EmotePose
- AdultQuiz
- Species
- CareerStatsUI
- Goalkeeper
- Crowd
- .RouteMessage
- MenuIcons
- NetMessages.cs
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
- Striker
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
- Keybinds
- .DestroyNetworkedUI
- SkyDome
- com.unity.modules.hierarchycore
- NetWriter
- PropKit
- .Rect
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
- PrematchUI
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
- CareerStats
- MsgType
- INetTransport
- .Pose
- NetRole
- ReliableChannel
- PlayerProfile
- PlayerPreview
- Hud
- Bone
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
- .Configure
- .Draw
- com.unity.modules.unitywebrequest
- .ResetTo
- .Box
- BallController.cs
- AnimState
- .Build
- grassprep.py
- .Build
- Phase
- .Label
- AssetImportRules
- Vector3
- SessionBrowserUI
- .TickNetPass
- postprep.py
- .DrawLobby
- Striker
- Color32
- .DriveTowardRotation
- Trickshot: Replayability Brainstorm
- UIFont
- GameCamera
- GameInput
- Crosser
- .Init
- Achievements
- Touch
- ShotType
- Celebration
- Category
- State
- Role
- CrosserControl
- graphify
- Phase
- Band
- NotificationToastUI
- CallLimiter
- .StartRebind
- Phase
- JerseyFaces
- .Set
- TackleResult
- IPlayerController
- .JerseyVoteTex
- ShotBand
- Phase
- Tab
- .BroadcastBallKick

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 192 edges
2. `Trickshot` - 128 edges
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

## Communities (170 total, 12 thin omitted)

### Community 0 - "CustomizeUI"
Cohesion: 0.09
Nodes (14): Action, Color, Dictionary, Func, GUIStyle, IEnumerator, Rect, Texture2D (+6 more)

### Community 1 - "JerseyDesigns"
Cohesion: 0.11
Nodes (24): Action, Color32, Dictionary, IReadOnlyList, List, Texture2D, List, List (+16 more)

### Community 2 - "SimConfig"
Cohesion: 0.06
Nodes (30): AiDifficulty, ScrimPos, Color, Vector2, Vector3, AiDifficulty, Easy, Hard (+22 more)

### Community 3 - "Dribble"
Cohesion: 0.16
Nodes (7): Action, Dribble, CaptureRadius, Carrying, CloseControl, Holder, Tightness

### Community 4 - "SetPieceMap"
Cohesion: 0.22
Nodes (10): Color, Random, Rect, Vector2, Vector3, SetPieceMap, BottomZ, HalfW (+2 more)

### Community 5 - "DirectIpTransport"
Cohesion: 0.09
Nodes (16): Action, ConcurrentQueue, data, Dictionary, from, Func, IPEndPoint, List (+8 more)

### Community 6 - "SkillTree"
Cohesion: 0.10
Nodes (15): Dictionary, HashSet, IEnumerable, List, Effect, Node, Preset, SkillTree (+7 more)

### Community 7 - "SkillIcons"
Cohesion: 0.19
Nodes (4): Color32, Dictionary, Texture2D, SkillIcons

### Community 8 - "Trickshot"
Cohesion: 0.07
Nodes (3): AchievementsPanelUI, Trickshot.Net, Trickshot

### Community 9 - "Cosmetics"
Cohesion: 0.06
Nodes (46): AccessoryEntry, Material, Matrix4x4, Mesh, MeshFilter, MeshRenderer, Transform, Vector2 (+38 more)

### Community 10 - "EmotePose"
Cohesion: 0.56
Nodes (3): Action, Vector3, EmotePose

### Community 11 - "AdultQuiz"
Cohesion: 0.50
Nodes (3): AdultQuiz, Q, Q

### Community 12 - "Species"
Cohesion: 0.13
Nodes (12): BodyPlan, Biped, Quadruped, HeaderAction, Biped, Species, Current, SpeciesAxis (+4 more)

### Community 13 - "CareerStatsUI"
Cohesion: 0.11
Nodes (14): Action, label, CareerStatsUI, Cat, Accuracy, FreeKick, Friends, Match (+6 more)

### Community 14 - "Goalkeeper"
Cohesion: 0.13
Nodes (13): Func, Quaternion, Vector3, Goalkeeper, Body, HasBall, PelvisPos, WasDivingSave (+5 more)

### Community 15 - "Crowd"
Cohesion: 0.18
Nodes (8): Collider, Color, Material, Transform, Vector3, Crowd, FanCount, CrowdCheer

### Community 16 - ".RouteMessage"
Cohesion: 0.25
Nodes (5): Color, NetReader, More, Type, BinaryReader

### Community 17 - "MenuIcons"
Cohesion: 0.26
Nodes (4): Color32, Dictionary, Texture2D, MenuIcons

### Community 18 - "NetMessages.cs"
Cohesion: 0.10
Nodes (14): Vector2, Vector3, BodyState, InputFrame, JoinRefusal, MatchRunning, None, NoSlot (+6 more)

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
Nodes (26): Collider, Transform, CrosserBubble, Bounds, ConfigurableJoint, Dictionary, IReadOnlyList, List (+18 more)

### Community 23 - "NetSession"
Cohesion: 0.04
Nodes (35): appr, PeerId, IsValid, JerseyChunkMsg, Dictionary, HashSet, name, PlayerAppearance (+27 more)

### Community 24 - "BuildAll"
Cohesion: 0.12
Nodes (8): Action, BuildAll, ZipEnabled, Plat, BuildTarget, MenuItem, Plat, Type

### Community 25 - "MatchProbe"
Cohesion: 0.14
Nodes (9): List, Vector3, MatchProbe, Overlay, ProbeTackle, Ai, Human, Slide (+1 more)

### Community 26 - ".Mat"
Cohesion: 0.30
Nodes (6): Camera, Material, Refs, Renderer, Rigidbody, Transform

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.09
Nodes (7): List, Material, ShootoutState, Transform, Vector3, Body, NetSetPieceMatch

### Community 28 - "PauseMenu"
Cohesion: 0.15
Nodes (10): Action, List, Entry, Kind, Bad, Normal, PauseMenu, Paused (+2 more)

### Community 29 - "Passing"
Cohesion: 0.12
Nodes (12): List, Vector3, Bar, AnyArmed, Option, Passing, PassKind, Air (+4 more)

### Community 30 - "NetStrikerMatch"
Cohesion: 0.16
Nodes (3): Crosser, Vector3, NetStrikerMatch

### Community 31 - "BodyLayoutDef"
Cohesion: 0.11
Nodes (23): Vector3, BodyLayout, BodyLayoutDef, ParentByBone, BoneSpec, ColliderKind, Box, CapsuleY (+15 more)

### Community 32 - "Emote"
Cohesion: 0.06
Nodes (34): Emote, Backflip, Bow, Charleston, Cheer, Clap, Crip, Dab (+26 more)

### Community 33 - "KeeperController"
Cohesion: 0.12
Nodes (12): Func, Quaternion, Vector2, Vector3, KeeperController, Body, Hands, HasBall (+4 more)

### Community 34 - "GameManager"
Cohesion: 0.11
Nodes (11): Transform, Vector3, GameManager, CrossMapEscapeOwned, SaveWatch, Armed, Epic, Touched (+3 more)

### Community 35 - "Striker"
Cohesion: 0.19
Nodes (6): Striker, KickDetector, Rigidbody, Rigidbody, Rigidbody, Rigidbody

### Community 36 - "AnatomySim"
Cohesion: 0.26
Nodes (7): Collider, Color, GameObject, Material, Transform, Vector3, AnatomySim

### Community 37 - "KeeperHands"
Cohesion: 0.14
Nodes (5): Collider, Vector3, KeeperHands, HeldFor, Holding

### Community 38 - "NetEndpoint"
Cohesion: 0.19
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
Cohesion: 0.20
Nodes (6): Vector3, AccuracyGame, Phase, Armed, Cooldown, Live

### Community 43 - "QuickChatFeed"
Cohesion: 0.11
Nodes (10): Dictionary, ChatCensor, List, Queue, Line, QuickChatFeed, AnyOpen, EscapeOwned (+2 more)

### Community 44 - "AimReticle"
Cohesion: 0.22
Nodes (8): Collider, Material, Renderer, Transform, Vector3, AimReticle, Active, TargetPoint

### Community 45 - "CrossMap"
Cohesion: 0.31
Nodes (8): Color, Rect, Vector2, Vector3, CrossMap, BottomZ, HalfW, TopZ

### Community 46 - "FreeKickGame"
Cohesion: 0.11
Nodes (14): Goalkeeper, Collider, Random, Vector3, FreeKickGame, Outcome, Blocked, Miss (+6 more)

### Community 47 - "SetPieceTaker"
Cohesion: 0.09
Nodes (18): Func, Quaternion, Vector3, SetPieceTaker, Active, Done, HasCharged, IsCharging (+10 more)

### Community 48 - "Keybinds"
Cohesion: 0.22
Nodes (5): action, Dictionary, label, Keybinds, Current

### Community 49 - ".DestroyNetworkedUI"
Cohesion: 0.19
Nodes (6): Action, MatchModeUI, Action, MultiplayerHubUI, Action, OtherModesUI

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

### Community 54 - ".Rect"
Cohesion: 0.14
Nodes (4): Action, Rect, Action, MenuUI

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
Nodes (21): Dictionary, Func, HashSet, List, Refs, Vector3, MatchGame, AwayScore (+13 more)

### Community 59 - "IStrikerInput"
Cohesion: 0.07
Nodes (25): Vector2, IStrikerInput, CloseControlHeld, EmoteId, Fresh, JumpHeld, JumpPressed, JumpReleased (+17 more)

### Community 60 - "FlexNet"
Cohesion: 0.16
Nodes (11): Func, List, Material, Mesh, MeshFilter, MeshRenderer, Transform, Vector3 (+3 more)

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
Cohesion: 0.14
Nodes (14): CapsuleCollider, Collider, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion (+6 more)

### Community 67 - "UITheme"
Cohesion: 0.10
Nodes (28): Action, Rect, GUIStyle, Color, GUIStyle, Rect, Texture2D, UITheme (+20 more)

### Community 68 - "TitleGlyph"
Cohesion: 0.33
Nodes (4): Color32, Texture2D, TitleGlyph, K

### Community 69 - "graphify knowledge graph"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - "PrematchUI"
Cohesion: 0.19
Nodes (5): Action, GUIStyle, ScrimPos, Vector3, PrematchUI

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

### Community 75 - "AudioManager"
Cohesion: 0.08
Nodes (14): Dictionary, IEnumerator, RuntimeInitializeOnLoadMethod, Vector3, AudioManager, Instance, Channel, Crowd (+6 more)

### Community 76 - "Multiplayer"
Cohesion: 0.09
Nodes (14): List, RuntimeInitializeOnLoadMethod, Multiplayer, IsActive, IsClient, IsHost, Session, SteamLinked (+6 more)

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
Cohesion: 0.13
Nodes (5): InputFrame, Bar, Vector3, Body, NetMatch

### Community 82 - "NetInputSource"
Cohesion: 0.07
Nodes (30): Vector2, NetInputSource, CloseControlHeld, EmoteId, Fresh, JumpHeld, JumpPressed, JumpReleased (+22 more)

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

### Community 87 - "CareerStats"
Cohesion: 0.14
Nodes (8): name, CareerStats, Data, FilePath, CareerStatsData, OnlineRanks, RankData, min

### Community 88 - "MsgType"
Cohesion: 0.09
Nodes (22): MsgType, AssignSlot, BallKick, CastJerseyVote, Hello, JerseyChunk, MatchEvent, MatchStats (+14 more)

### Community 89 - "INetTransport"
Cohesion: 0.06
Nodes (22): Action, Func, List, INetTransport, AdvertProvider, HostPeer, IsHost, IsRunning (+14 more)

### Community 90 - ".Pose"
Cohesion: 0.22
Nodes (3): Vector3, KickSwing, LocalFoot

### Community 91 - "NetRole"
Cohesion: 0.33
Nodes (5): NetRole, Crosser, Keeper, Shooter, Spectator

### Community 92 - "ReliableChannel"
Cohesion: 0.19
Nodes (7): Dictionary, List, Pending, ReliableChannel, CumAck, HasUnacked, Pending

### Community 93 - "PlayerProfile"
Cohesion: 0.03
Nodes (58): label, Texture2D, PlayerProfile, AgilityStat, AirFlipMul, Bias, BicycleSkill01, BodyHeightScale (+50 more)

### Community 94 - "PlayerPreview"
Cohesion: 0.10
Nodes (14): Color, GameObject, Light, Material, Quaternion, Rect, Renderer, Texture2D (+6 more)

### Community 95 - "Hud"
Cohesion: 0.10
Nodes (15): Color, GUIStyle, List, Rect, Vector2, Vector3, Hud, H (+7 more)

### Community 96 - "Bone"
Cohesion: 0.10
Nodes (18): Vector3, Bone, CalfL, CalfR, Count, FootL, FootR, ForearmL (+10 more)

### Community 97 - "Knockdown"
Cohesion: 0.24
Nodes (4): Knockdown, Beaten, Down, Strk

### Community 98 - ".Build"
Cohesion: 0.11
Nodes (28): IEnumerable, Quaternion, Vector3, PitchLayout, AttackGoalLineZ, FarGoalLineZ, HalfWidth, PitchCenterZ (+20 more)

### Community 99 - ".RequestFriendsList"
Cohesion: 0.20
Nodes (7): Action, List, SteamFriendInfo, SteamFriendsAPI, Available, List, FriendsPanelUI

### Community 100 - ".Build"
Cohesion: 0.39
Nodes (5): Collider, Material, Transform, Vector3, PitchBuilder

### Community 101 - ".SlotSubMenu"
Cohesion: 0.18
Nodes (7): SlotKind, Skin, StyleA, StyleB, StyleC, Color, SpeciesCosmetics

### Community 102 - "SurroundBuilder"
Cohesion: 0.23
Nodes (10): Collider, Color, Material, Texture2D, Transform, Vector3, SurroundBuilder, BowlHalfX (+2 more)

### Community 103 - ".Build"
Cohesion: 0.29
Nodes (10): Collider, Material, MeshFilter, MeshRenderer, PhysicsMaterial, Renderer, Transform, Vector3 (+2 more)

### Community 104 - "MonoBehaviour"
Cohesion: 0.08
Nodes (18): Action, Collider, Goal, Material, Transform, NetAccuracyMatch, List, Quaternion (+10 more)

### Community 105 - "Gait"
Cohesion: 0.31
Nodes (4): Vector3, Gait, Profile, Profile

### Community 106 - "MenuBackground"
Cohesion: 0.13
Nodes (12): Collider, Color, Light, List, Material, MeshFilter, MeshRenderer, PhysicsMaterial (+4 more)

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
Cohesion: 0.13
Nodes (12): Color, Vector3, StadiumStyle, Active, FirstPickable, SelectedIndex, Surroundings, Flags (+4 more)

### Community 113 - "LocalTransport"
Cohesion: 0.11
Nodes (14): LobbyInfo, Action, data, Dictionary, from, Func, List, Queue (+6 more)

### Community 114 - "com.unity.modules.screencapture"
Cohesion: 0.33
Nodes (6): com.unity.modules.screencapture, dependencies, depth, source, version, com.unity.modules.screencapture

### Community 115 - ".Configure"
Cohesion: 0.14
Nodes (4): Texture2D, Material, Refs, Transform

### Community 116 - ".Draw"
Cohesion: 0.10
Nodes (16): Color, GUIStyle, List, MatchStatsUI, Tab, Away, Home, Matrix4x4 (+8 more)

### Community 117 - "com.unity.modules.unitywebrequest"
Cohesion: 0.33
Nodes (6): com.unity.modules.unitywebrequest, dependencies, depth, source, version, com.unity.modules.unitywebrequest

### Community 118 - ".ResetTo"
Cohesion: 0.17
Nodes (4): KeeperGame, Vector3, ShotServer, JustFired

### Community 121 - ".Box"
Cohesion: 0.21
Nodes (13): CapsuleCollider, Collider, Color, GameObject, Material, MeshFilter, Renderer, Shader (+5 more)

### Community 123 - "AnimState"
Cohesion: 0.11
Nodes (16): AnimState, Dive, Down, Idle, Jump, Kick, Run, Sit (+8 more)

### Community 124 - ".Build"
Cohesion: 0.15
Nodes (15): Goal, GoalFrame, NetBackstop, Collider, Material, MeshFilter, MeshRenderer, PhysicsMaterial (+7 more)

### Community 125 - "grassprep.py"
Cohesion: 0.60
Nodes (4): load(), main(), member(), Build the turf detail layer in Assets/Resources/Turf from an ambientCG scan.…

### Community 126 - ".Build"
Cohesion: 0.14
Nodes (10): BoxCollider, CapsuleCollider, Collider, Material, Renderer, SphereCollider, Transform, Color (+2 more)

### Community 127 - "Phase"
Cohesion: 0.33
Nodes (6): Phase, CareerStats, Hub, SinglePlayer, Splash, Zoo

### Community 128 - ".Label"
Cohesion: 0.15
Nodes (9): Action, GUIStyle, RebindingOperation, Rect, Vector2, OptionsMenu, IsRebinding, QuickChat (+1 more)

### Community 133 - "SessionBrowserUI"
Cohesion: 0.24
Nodes (3): Action, List, SessionBrowserUI

### Community 136 - ".DrawLobby"
Cohesion: 0.13
Nodes (6): Action, GUIStyle, Vector3, HostSetupUI, Color, LobbyUI

### Community 137 - "Striker"
Cohesion: 0.06
Nodes (23): Collider, Func, Vector3, Striker, FacingForward, HasLookAim, IsBusy, IsDiving (+15 more)

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
Cohesion: 0.13
Nodes (14): Func, Transform, Vector3, GameCamera, BallCam, KeeperLookDownFraction, KeeperLookYaw, Pitch (+6 more)

### Community 143 - "GameInput"
Cohesion: 0.05
Nodes (36): Vector2, GameInput, BallCamPressed, CloseControlHeld, CrossMapPressed, CursorCaptured, EmoteHeld, EmoteId (+28 more)

### Community 144 - "Crosser"
Cohesion: 0.16
Nodes (9): Quaternion, Transform, Vector3, Crosser, JustServed, Origin, Ragdoll, ReadyToServe (+1 more)

### Community 145 - ".Init"
Cohesion: 0.33
Nodes (3): InputAction, InputActionAsset, PlayerInput

### Community 146 - "Achievements"
Cohesion: 0.14
Nodes (13): SteamAchievementsAPI, Available, Func, List, AchievementDef, AchievementKind, LeaderboardTop, StatThreshold (+5 more)

### Community 147 - "Touch"
Cohesion: 0.29
Nodes (7): Touch, Carry, Contact, Keeper, Pass, Shot, Tackle

### Community 148 - "ShotType"
Cohesion: 0.14
Nodes (12): SetPieceSpin, CurveLeft, CurveRight, Knuckle, None, TopSpin, ShotType, Bicycle (+4 more)

### Community 149 - "Celebration"
Cohesion: 0.19
Nodes (7): e, name, Celebration, CurrentEmote, Playing, Progress01, Emote

### Community 150 - "Category"
Cohesion: 0.20
Nodes (10): Category, Agility, Control, Heading, Instinct, Pace, Passing, Shooting (+2 more)

### Community 151 - "State"
Cohesion: 0.33
Nodes (6): State, Diving, Holding, Ready, Saving, Stumble

### Community 152 - "Role"
Cohesion: 0.33
Nodes (5): Role, Crosser, Goalkeeper, Sniper, Striker

### Community 153 - "CrosserControl"
Cohesion: 0.24
Nodes (4): Func, CrosserControl, Charge01, Holding

### Community 154 - "graphify"
Cohesion: 0.40
Nodes (5): graphify, unityMCP, C:/Users/evrik/AppData/Local/Microsoft/WinGet/Packages/astral-sh.uv_Microsoft.Winget.Source_8wekyb3d8bbwe/uvx.exe, graphify-mcp, mcp-for-unity

### Community 155 - "Phase"
Cohesion: 0.40
Nodes (5): Phase, Attack, Defend, Loose, Restart

### Community 156 - "Band"
Cohesion: 0.40
Nodes (5): Band, High, Jump, Low, Mid

### Community 157 - "NotificationToastUI"
Cohesion: 0.33
Nodes (4): List, NotificationToastUI, Toast, Toast

### Community 160 - "Phase"
Cohesion: 0.50
Nodes (4): Phase, Armed, Live, Settle

### Community 161 - "JerseyFaces"
Cohesion: 0.67
Nodes (3): JerseyFaces, Chest, Flank

### Community 162 - ".Set"
Cohesion: 0.38
Nodes (4): e, Vector3, KeeperPose, b

### Community 163 - "TackleResult"
Cohesion: 0.33
Nodes (6): TackleResult, Beaten, Foul, NoCarrier, Won, WrongSide

### Community 169 - "ShotBand"
Cohesion: 0.50
Nodes (4): ShotBand, Chip, Drive, Placed

### Community 170 - "Phase"
Cohesion: 0.50
Nodes (4): Phase, Connecting, Playlist, Searching

### Community 171 - "Tab"
Cohesion: 0.50
Nodes (4): Tab, Audio, Keybindings, Quickchat

## Knowledge Gaps
- **816 isolated node(s):** `mcp-for-unity`, `graphify-mcp`, `ZipEnabled`, `CursorCaptured`, `Move` (+811 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 1146 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **12 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `Trickshot` to `JerseyDesigns`, `SimConfig`, `SetPieceMap`, `SkillTree`, `Cosmetics`, `AdultQuiz`, `Species`, `CareerStatsUI`, `Crowd`, `MenuIcons`, `GameBootstrap`, `ActiveRagdoll`, `BuildAll`, `MatchProbe`, `PauseMenu`, `Passing`, `BodyLayoutDef`, `GameManager`, `Striker`, `AnatomySim`, `KeeperHands`, `AccuracyGame`, `QuickChatFeed`, `AimReticle`, `CrossMap`, `FreeKickGame`, `SetPieceTaker`, `Keybinds`, `.DestroyNetworkedUI`, `SkyDome`, `PropKit`, `AccuracyTarget`, `MatchGame`, `IStrikerInput`, `FlexNet`, `Sniper`, `DefensiveWall`, `TitleGlyph`, `PrematchUI`, `AudioManager`, `Multiplayer`, `DisplaySettings`, `Footballer`, `StatRadar`, `CareerStats`, `.Pose`, `PlayerPreview`, `Bone`, `Knockdown`, `.Build`, `.RequestFriendsList`, `.Build`, `.SlotSubMenu`, `.Build`, `MonoBehaviour`, `Gait`, `MenuBackground`, `.Place`, `BallController`, `Turf`, `StadiumStyle`, `.Draw`, `.ResetTo`, `.Box`, `.Build`, `.Build`, `.Label`, `AssetImportRules`, `SessionBrowserUI`, `.DrawLobby`, `Striker`, `.DriveTowardRotation`, `UIFont`, `GameInput`, `Crosser`, `Achievements`, `ShotType`, `Celebration`, `Role`, `CrosserControl`, `NotificationToastUI`, `CallLimiter`, `.Set`, `IPlayerController`?**
  _High betweenness centrality (0.156) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `ActiveRagdoll` to `SimConfig`, `Dribble`, `Vector3`, `Trickshot`, `Cosmetics`, `Striker`, `Species`, `Goalkeeper`, `Crosser`, `Celebration`, `.Mat`, `NetSetPieceMatch`, `Passing`, `NetStrikerMatch`, `BodyLayoutDef`, `KeeperController`, `GameManager`, `Striker`, `AnatomySim`, `KeeperHands`, `AccuracyGame`, `FreeKickGame`, `SetPieceTaker`, `MatchGame`, `Sniper`, `Footballer`, `NetMatch`, `NetInputSource`, `.Pose`, `PlayerPreview`, `Hud`, `Bone`, `Knockdown`, `MonoBehaviour`, `Gait`, `MenuBackground`, `BallController`, `.Configure`, `.ResetTo`, `.Update`, `.Box`, `.Build`?**
  _High betweenness centrality (0.140) - this node is a cross-community bridge._
- **Why does `NetSession` connect `NetSession` to `.JerseyVoteTex`, `Trickshot`, `.DrawLobby`, `Color32`, `AnimState`, `Multiplayer`, `.BroadcastBallKick`, `QuickChatFeed`, `NetSetPieceMatch`, `.RouteMessage`, `NetMatch`, `NetMessages.cs`, `.Configure`, `MsgType`, `INetTransport`, `NetRole`, `NetStrikerMatch`?**
  _High betweenness centrality (0.107) - this node is a cross-community bridge._
- **What connects `mcp-for-unity`, `graphify-mcp`, `ZipEnabled` to the rest of the system?**
  _816 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `CustomizeUI` be split into smaller, more focused modules?**
  _Cohesion score 0.09322033898305085 - nodes in this community are weakly interconnected._
- **Should `JerseyDesigns` be split into smaller, more focused modules?**
  _Cohesion score 0.11428571428571428 - nodes in this community are weakly interconnected._
- **Should `SimConfig` be split into smaller, more focused modules?**
  _Cohesion score 0.05919661733615222 - nodes in this community are weakly interconnected._