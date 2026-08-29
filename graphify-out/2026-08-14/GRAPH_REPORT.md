# Graph Report - C:\Users\rsnegach\Desktop\Trickshot  (2026-08-14)

## Corpus Check
- 119 files · ~289,533 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2352 nodes · 5911 edges · 112 communities (107 shown, 5 thin omitted)
- Extraction: 88% EXTRACTED · 12% INFERRED · 0% AMBIGUOUS · INFERRED: 723 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `c9d222b6`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- AccuracyGame
- SessionBrowserUI
- Knockdown
- OptionsMenu
- .StartRebind
- ShotServer
- Bone
- .Configure
- .Empty
- SkillTree
- .Box
- PitchLayout
- JerseyDesigns.Nations9.cs
- .OnGUI
- .Box
- com.unity.modules.jsonserialize
- .AdvanceTurn
- PlayerPreview
- QuickChat
- com.unity.modules.imageconversion
- .ClientUpdate
- JerseyDesigns.Nations1.cs
- .Build
- PauseMenu
- NetSetPieceMatch
- .AdvanceTurn
- PitchBuilder
- .ResetTo
- Footballer
- HostSetupUI
- SkillTree
- Role.cs
- OptionsMenu
- Net Set-Piece Match
- JerseyDesigns.Nations8.cs
- .PhysMat
- .SetLocalInput
- Net Messages & Wire Codec
- MenuUI
- .Build
- Input & Keybinds
- .ClientUpdate
- Dribble System
- NetCodec
- IStrikerInput
- NetPump
- PeerId
- Crowd
- Ball Physics & Launch
- Goalkeeper AI & Control
- .NavButtons
- SessionBrowserUI
- .Set
- NetStrikerMatch
- .Empty
- INetTransport
- GameInput
- .Configure
- Goalkeeper
- Goal
- SetPieceTaker
- Celebration
- .Box
- Jersey / Nation Designs
- JerseyDesigns.Nations2.cs
- .StartRebind
- .Configure
- JerseyDesigns.Nations3.cs
- SteamTransport
- OptionsMenu
- .Poll
- Direct IP Transport
- CustomizeUI
- Crowd
- Skill Icon Drawing
- PrematchUI
- StadiumSelectUI
- QuickChat
- BoneSpec
- com.unity.modules.wind
- JerseyDesigns.Nations7.cs
- ShotType.cs
- .PhysMat
- com.unity.modules.adaptiveperformance
- .Set
- SkillIcons
- IStrikerInput
- LobbyUI
- Footballer
- StadiumStyle
- ShotServer
- Sniper
- IStrikerInput
- DefensiveWall
- FreeplayGame
- com.unity.modules.ai
- com.unity.modules.physics
- FlexNet
- LobbySlot
- GameInput
- com.unity.modules.imgui
- .PushRoster
- com.unity.modules.ui
- .SkillPresetButtons
- Trickshot (3D trick-shot football prototype)
- DirectIpTransport
- com.unity.modules.terrain
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- AimReticle
- com.unity.modules.androidjni
- com.unity.modules.ai

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 127 edges
2. `Trickshot` - 101 edges
3. `NetSession` - 86 edges
4. `CustomizeUI` - 78 edges
5. `BallController` - 76 edges
6. `NetSetPieceMatch` - 71 edges
7. `Striker` - 61 edges
8. `ScrimmageGame` - 59 edges
9. `JerseyDesigns` - 58 edges
10. `GameCamera` - 57 edges

## Surprising Connections (you probably didn't know these)
- `PlayerInputManager (local multiplayer seam)` --semantically_similar_to--> `Slot / role model (NetSession.MaxSlots=8)`  [INFERRED] [semantically similar]
  README.md → MULTIPLAYER.md
- `Trickshot (3D trick-shot football prototype)` --conceptually_related_to--> `Trickshot Multiplayer Framework`  [INFERRED]
  README.md → MULTIPLAYER.md
- `Trickshot (3D trick-shot football prototype)` --references--> `Unity 6000.4.1f1 editor version`  [EXTRACTED]
  README.md → ProjectSettings/ProjectVersion.txt
- `ActiveRagdoll.cs` --shares_data_with--> `ScrimmageGame`  [INFERRED]
  README.md → MULTIPLAYER.md
- `BallController` --references--> `ShotType`  [EXTRACTED]
  Assets/Scripts/Play/BallController.cs → Assets/Scripts/Play/ShotType.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Hair Atlas License Terms Set** — assets_resources_hair_hairatlas_license_royalty_free_use, assets_resources_hair_hairatlas_license_no_attribution_required, assets_resources_hair_hairatlas_license_no_resale_restriction, assets_resources_hair_hairatlas_license_bundled_license_requirement [EXTRACTED 1.00]
- **Four strand-card tiles compose the hair atlas** — assets_resources_hair_hairatlas_wavy_scattered_strands, assets_resources_hair_hairatlas_flowing_wavy_strands, assets_resources_hair_hairatlas_dense_wavy_strands, assets_resources_hair_hairatlas_straight_sleek_strands, assets_resources_hair_hairatlas_atlas [EXTRACTED 1.00]
- **graphify CLI commands** — claude_graphify_query, claude_graphify_path, claude_graphify_explain, claude_graphify_update [EXTRACTED 0.85]
- **Interchangeable transports behind INetTransport seam** — multiplayer_inettransport, multiplayer_directiptransport, multiplayer_localtransport, multiplayer_steamtransport [EXTRACTED 1.00]
- **Active-ragdoll bicycle-kick mechanic** — readme_activeragdoll, readme_ragdollpose, readme_kickdetector, readme_jointmath, readme_bicycle_kick [INFERRED 0.85]
- **Host-authoritative frame loop (poll, input, snapshot)** — multiplayer_multiplayer, multiplayer_netsession, multiplayer_netmessages, multiplayer_host_authoritative [INFERRED 0.85]

## Communities (112 total, 5 thin omitted)

### Community 8 - "AccuracyGame"
Cohesion: 0.06
Nodes (12): Trickshot, Trickshot.Net, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns (+4 more)

### Community 100 - "SessionBrowserUI"
Cohesion: 0.07
Nodes (19): GameInput, InputActionAsset, InputActionMap, PlayerInput, Vector2, byte, GameCamera, Mode (+11 more)

### Community 89 - "Knockdown"
Cohesion: 0.33
Nodes (4): MonoBehaviour, MultiplayerHubUI, Action, NetBackstop

### Community 83 - "OptionsMenu"
Cohesion: 0.18
Nodes (4): InputAction, Keybinds, string, Dictionary

### Community 49 - "ShotServer"
Cohesion: 0.14
Nodes (7): QuickChatFeed, int, float, Line, string, List, Queue

### Community 16 - "Bone"
Cohesion: 0.09
Nodes (16): CrossMap, float, Color, Rect, Vector3, Vector2, Delivery, Hud (+8 more)

### Community 33 - ".Configure"
Cohesion: 0.11
Nodes (15): InputFrame, JerseyChunkMsg, byte, uint, MatchConfig, ushort, bool, float (+7 more)

### Community 35 - ".Empty"
Cohesion: 0.15
Nodes (8): IStrikerInput, Vector2, NetInputSource, bool, Vector2, CrosserControl, Func, float

### Community 5 - "SkillTree"
Cohesion: 0.10
Nodes (12): DirectIpTransport, byte, float, Func, UdpClient, Thread, bool, ConcurrentQueue (+4 more)

### Community 47 - ".Box"
Cohesion: 0.14
Nodes (4): NetChannel, SteamTransport, Func, List

### Community 79 - "PitchLayout"
Cohesion: 0.10
Nodes (14): Action, List, ulong, LobbyInfo, string, int, LocalTransport, Dictionary (+6 more)

### Community 116 - "JerseyDesigns.Nations9.cs"
Cohesion: 0.08
Nodes (18): PeerId, IEquatable, NetRole, NetSession, int, uint, StampedSnap, float (+10 more)

### Community 20 - ".OnGUI"
Cohesion: 0.12
Nodes (5): INetTransport, Action, List, Func, Vector3

### Community 17 - ".Box"
Cohesion: 0.20
Nodes (5): Multiplayer, bool, NetPumpRunner, NetPumpRunner, RuntimeInitializeOnLoadMethod

### Community 28 - "com.unity.modules.jsonserialize"
Cohesion: 0.14
Nodes (9): List, SessionBrowserUI, Action, List, int, ulong, float, string (+1 more)

### Community 72 - ".AdvanceTurn"
Cohesion: 0.20
Nodes (9): TailnetDiscovery, float, Reason, ConcurrentQueue, Action, List, IPAddress, IPEndPoint (+1 more)

### Community 38 - "PlayerPreview"
Cohesion: 0.18
Nodes (6): NetEndpoint, int, IPEndPoint, IPAddress, List, string

### Community 70 - "QuickChat"
Cohesion: 0.25
Nodes (4): MsgType, Color, NetReader, BinaryReader

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.16
Nodes (6): PlayerAppearance, Vector3, NetWriter, MemoryStream, BinaryWriter, NetCodec

### Community 30 - ".ClientUpdate"
Cohesion: 0.14
Nodes (11): AnimState, NetStrikerMatch, Body, bool, Vector3, float, int, uint (+3 more)

### Community 53 - ".Build"
Cohesion: 0.11
Nodes (7): Transform, Camera, Material, Rigidbody, Transform, Camera, Material

### Community 91 - "PauseMenu"
Cohesion: 0.17
Nodes (6): LobbyUI, Action, bool, string, float, Color

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.10
Nodes (10): NetSetPieceMatch, int, bool, float, Body, Vector3, uint, string (+2 more)

### Community 44 - ".AdvanceTurn"
Cohesion: 0.24
Nodes (3): ChatCensor, string, Dictionary

### Community 34 - "PitchBuilder"
Cohesion: 0.18
Nodes (7): ReliableChannel, float, uint, Pending, byte, Dictionary, List

### Community 68 - ".ResetTo"
Cohesion: 0.20
Nodes (7): LobbyAdvert, bool, string, int, LobbyProbe, byte, uint

### Community 43 - "Footballer"
Cohesion: 0.16
Nodes (6): AccuracyBoard, Transform, float, int, uint, Vector3

### Community 78 - "HostSetupUI"
Cohesion: 0.14
Nodes (8): AccuracyGame, int, Phase, float, bool, string, Vector3, Phase

### Community 23 - "SkillTree"
Cohesion: 0.12
Nodes (9): FreeKickGame, Phase, float, bool, int, string, Vector3, Outcome (+1 more)

### Community 95 - "Role.cs"
Cohesion: 0.13
Nodes (6): SetPieceTaker, State, Vector3, bool, float, Func

### Community 14 - "OptionsMenu"
Cohesion: 0.14
Nodes (11): AccuracyTarget, int, bool, Vector3, Action, float, BoxCollider, Transform (+3 more)

### Community 11 - "Net Set-Piece Match"
Cohesion: 0.33
Nodes (5): AdultQuiz, Q, string, int, Q

### Community 80 - "JerseyDesigns.Nations8.cs"
Cohesion: 0.23
Nodes (5): AimReticle, Transform, float, Vector3, Material

### Community 73 - ".PhysMat"
Cohesion: 0.19
Nodes (9): AnatomySim, Transform, float, int, Vector3, Collider, Color, Material (+1 more)

### Community 50 - ".SetLocalInput"
Cohesion: 0.13
Nodes (11): BallController, Rigidbody, SphereCollider, TrailRenderer, Vector3, float, bool, SetPieceSpin (+3 more)

### Community 3 - "Net Messages & Wire Codec"
Cohesion: 0.28
Nodes (3): Transform, Camera, Refs

### Community 90 - "MenuUI"
Cohesion: 0.22
Nodes (4): KickDetector, Action, float, Collision

### Community 99 - ".Build"
Cohesion: 0.24
Nodes (8): PhysicsMaterial, PhysicsMaterialCombine, ScrimmageArena, Refs, float, Vector3, Transform, PhysicsMaterial

### Community 4 - "Input & Keybinds"
Cohesion: 0.12
Nodes (9): KeeperGame, float, bool, int, string, ShotServer, float, int (+1 more)

### Community 45 - ".ClientUpdate"
Cohesion: 0.09
Nodes (10): Collision, IPlayerController, Striker, Trick, Func, bool, Vector3, float (+2 more)

### Community 2 - "Dribble System"
Cohesion: 0.06
Nodes (21): Dribble, bool, float, Vector3, Footballer, int, bool, float (+13 more)

### Community 36 - "NetCodec"
Cohesion: 0.19
Nodes (8): Celebration, Emote, float, bool, EmotePose, Vector3, Action, Emote

### Community 112 - "IStrikerInput"
Cohesion: 0.24
Nodes (5): Crosser, Transform, float, Vector3, bool

### Community 94 - "NetPump"
Cohesion: 0.08
Nodes (19): CrosserBubble, Transform, float, Collider, ActiveRagdoll, Transform, Rigidbody, ConfigurableJoint (+11 more)

### Community 76 - "PeerId"
Cohesion: 0.12
Nodes (13): Crowd, float, Vector3, Color, int, Material, bool, Transform (+5 more)

### Community 81 - "Crowd"
Cohesion: 0.14
Nodes (10): Seat, PitchLayout, float, bool, Side, Seat, Vector3, Quaternion (+2 more)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.07
Nodes (14): CustomizeUI, Action, Stage, bool, int, Vector2, float, string (+6 more)

### Community 6 - "Goalkeeper AI & Control"
Cohesion: 0.10
Nodes (16): Category, Preset, SkillTree, Category, Effect, string, float, Node (+8 more)

### Community 121 - ".NavButtons"
Cohesion: 0.07
Nodes (20): IEnumerator, Vector2, Rect, PlayerPreview, Vector3, Camera, Light, GameObject (+12 more)

### Community 42 - "SessionBrowserUI"
Cohesion: 0.26
Nodes (3): MenuScale, float, Matrix4x4

### Community 88 - ".Set"
Cohesion: 0.19
Nodes (12): Func, PlayerProfile, float, string, int, bool, Texture2D, byte (+4 more)

### Community 26 - "NetStrikerMatch"
Cohesion: 0.22
Nodes (4): SlotKind, SpeciesCosmetics, string, Color

### Community 46 - ".Empty"
Cohesion: 0.20
Nodes (10): DefensiveWall, List, PhysicsMaterial, float, IReadOnlyList, GameObject, Transform, Vector3 (+2 more)

### Community 29 - "INetTransport"
Cohesion: 0.06
Nodes (24): FlexNet, Vector3, bool, int, Mesh, Transform, float, Link (+16 more)

### Community 54 - "GameInput"
Cohesion: 0.21
Nodes (7): FreeplayGame, Transform, bool, float, int, string, Vector3

### Community 59 - ".Configure"
Cohesion: 0.20
Nodes (7): GameManager, Transform, bool, int, string, float, Vector3

### Community 48 - "Goalkeeper"
Cohesion: 0.14
Nodes (10): ReplaySystem, Frame, Vector3, Quaternion, Transform, Rigidbody, bool, List (+2 more)

### Community 92 - "Goal"
Cohesion: 0.29
Nodes (4): Goal, Action, bool, Collider

### Community 13 - "SetPieceTaker"
Cohesion: 0.10
Nodes (14): Goalkeeper, Quaternion, float, State, Vector3, KeeperController, State, Quaternion (+6 more)

### Community 22 - "Celebration"
Cohesion: 0.06
Nodes (34): HairSim, RootMode, HairDef, int, float, Vector3, bool, Vector2 (+26 more)

### Community 61 - ".Box"
Cohesion: 0.20
Nodes (7): HostSetupUI, Action, string, int, bool, Vector3, Rect

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.12
Nodes (21): List, List, List, List, List, List, List, List (+13 more)

### Community 96 - ".StartRebind"
Cohesion: 0.32
Nodes (3): Knockdown, float, bool

### Community 37 - ".Configure"
Cohesion: 0.13
Nodes (5): GameMode, GameBootstrap, bool, RuntimeInitializeOnLoadMethod, GameObject

### Community 106 - "JerseyDesigns.Nations3.cs"
Cohesion: 0.27
Nodes (4): MenuUI, Action, bool, Texture2D

### Community 25 - "SteamTransport"
Cohesion: 0.12
Nodes (13): NetScrimmageMatch, Body, bool, int, float, Vector3, Transform, Refs (+5 more)

### Community 87 - "OptionsMenu"
Cohesion: 0.19
Nodes (8): OptionsMenu, Tab, string, RebindingOperation, int, Vector2, Action, Tab

### Community 93 - ".Poll"
Cohesion: 0.29
Nodes (3): QuickChat, string, int

### Community 9 - "Direct IP Transport"
Cohesion: 0.25
Nodes (4): PauseMenu, Action, bool, float

### Community 18 - "CustomizeUI"
Cohesion: 0.07
Nodes (20): PrematchUI, Action, float, bool, Delivery, Vector3, int, ScrimRole (+12 more)

### Community 67 - "Crowd"
Cohesion: 0.32
Nodes (6): SetPieceMap, Color, float, Rect, Vector3, Vector2

### Community 7 - "Skill Icon Drawing"
Cohesion: 0.16
Nodes (7): SkillIcons, int, float, Dictionary, Color32, string, Texture2D

### Community 15 - "PrematchUI"
Cohesion: 0.24
Nodes (7): Sniper, Transform, LineRenderer, float, bool, Action, Vector3

### Community 55 - "QuickChat"
Cohesion: 0.22
Nodes (7): TimeTrialGame, Transform, bool, int, float, string, Vector3

### Community 104 - "BoneSpec"
Cohesion: 0.14
Nodes (14): Material, ColliderKind, HitboxClass, DecorTint, DecorSpec, string, Vector3, bool (+6 more)

### Community 82 - "com.unity.modules.wind"
Cohesion: 0.29
Nodes (6): JointMath, ConfigurableJoint, Quaternion, Space, Rigidbody, Vector3

### Community 108 - "JerseyDesigns.Nations7.cs"
Cohesion: 0.38
Nodes (4): KeeperPose, Vector3, b, e

### Community 110 - "ShotType.cs"
Cohesion: 0.38
Nodes (4): RagdollPose, Vector3, euler, bone

### Community 97 - ".PhysMat"
Cohesion: 0.35
Nodes (6): Arena, Refs, Transform, Material, Vector3, PhysicsMaterial

### Community 75 - "com.unity.modules.adaptiveperformance"
Cohesion: 0.09
Nodes (13): AudioManager, Channel, string, float, RuntimeInitializeOnLoadMethod, AudioSource, int, Dictionary (+5 more)

### Community 102 - ".Set"
Cohesion: 0.29
Nodes (6): Make, Shader, Color, Material, Texture2D, JerseyFaces

### Community 12 - "SkillIcons"
Cohesion: 0.34
Nodes (5): SurroundBuilder, uint, Vector3, Transform, Material

### Community 60 - "IStrikerInput"
Cohesion: 0.32
Nodes (6): PitchBuilder, float, int, Transform, Material, Vector3

### Community 10 - "LobbyUI"
Cohesion: 0.32
Nodes (9): StadiumBuilder, Color, float, int, Side, Transform, Material, PhysicsMaterial (+1 more)

### Community 32 - "Footballer"
Cohesion: 0.15
Nodes (11): BodyPlan, SpeciesAxis, string, float, SpeciesBias, HeaderAction, SpeciesSlot, SpeciesDef (+3 more)

### Community 103 - "StadiumStyle"
Cohesion: 0.32
Nodes (7): Surroundings, StadiumStyle, string, int, float, bool, Color

### Community 56 - "ShotServer"
Cohesion: 0.07
Nodes (29): dependencies, com.unity.inputsystem, com.unity.inputsystem, com.unity.multiplayer.center, com.unity.multiplayer.center, com.unity.modules.androidjni, com.unity.modules.androidjni, com.unity.modules.animation (+21 more)

### Community 57 - "Sniper"
Cohesion: 0.29
Nodes (6): dependencies, com.unity.modules.androidjni, version, depth, source, dependencies

### Community 58 - "IStrikerInput"
Cohesion: 0.33
Nodes (6): com.unity.inputsystem, version, depth, source, dependencies, url

### Community 39 - "DefensiveWall"
Cohesion: 0.20
Nodes (10): com.unity.modules.uielements, com.unity.modules.uielements, com.unity.multiplayer.center, version, depth, source, dependencies, version (+2 more)

### Community 63 - "FreeplayGame"
Cohesion: 0.40
Nodes (5): com.unity.modules.animation, version, depth, source, dependencies

### Community 64 - "com.unity.modules.ai"
Cohesion: 0.40
Nodes (5): com.unity.modules.audio, version, depth, source, dependencies

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): com.unity.modules.hierarchycore, version, depth, source, dependencies, com.unity.modules.hierarchycore

### Community 41 - "FlexNet"
Cohesion: 0.18
Nodes (11): com.unity.modules.imageconversion, version, depth, source, dependencies, com.unity.modules.vectorgraphics, version, depth (+3 more)

### Community 62 - "LobbySlot"
Cohesion: 0.33
Nodes (6): com.unity.modules.imgui, version, depth, source, dependencies, com.unity.modules.imgui

### Community 21 - "GameInput"
Cohesion: 0.29
Nodes (7): com.unity.modules.jsonserialize, version, depth, source, dependencies, dependencies, com.unity.modules.jsonserialize

### Community 65 - "com.unity.modules.imgui"
Cohesion: 0.40
Nodes (5): com.unity.modules.particlesystem, version, depth, source, dependencies

### Community 40 - ".PushRoster"
Cohesion: 0.33
Nodes (6): com.unity.modules.physics, version, depth, source, dependencies, com.unity.modules.physics

### Community 66 - "com.unity.modules.ui"
Cohesion: 0.33
Nodes (6): com.unity.modules.ui, version, depth, source, dependencies, com.unity.modules.ui

### Community 71 - ".SkillPresetButtons"
Cohesion: 0.40
Nodes (5): com.unity.modules.umbra, version, depth, source, dependencies

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot (3D trick-shot football prototype), GameBootstrap, KickDetector.cs, GameCamera.cs, Bicycle kick trick, Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout) (+1 more)

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): ActiveRagdoll.cs, RagdollPose.cs, JointMath.cs, ConfigurableJoint (Unity), PlayerInput (Unity Input System), PlayerInputManager (local multiplayer seam), INetTransport.cs (transport seam), NetMessages.cs (wire types + NetCodec) (+17 more)

### Community 85 - "Kyrgyz Sun Emblem (kyrgyz_sun.png)"
Cohesion: 0.60
Nodes (5): Kyrgyz Sun Emblem (kyrgyz_sun.png), Kyrgyzstan Flag Emblem, Tunduk (Yurt Crown) Motif, Forty-Ray Golden Sun, Team / National Emblem Game Asset

### Community 86 - "Soviet Emblem Sprite"
Cohesion: 0.60
Nodes (5): Soviet Emblem Sprite, Hammer and Sickle, Five-Pointed Star, Soviet Union Symbolism, Team Emblem / Logo

### Community 69 - "AimReticle"
Cohesion: 0.24
Nodes (10): graphify knowledge graph, graphify query command, graphify path command, graphify explain command, graphify update command, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify-out/wiki/index.md (+2 more)

### Community 84 - "com.unity.modules.androidjni"
Cohesion: 0.60
Nodes (5): Hair Atlas Asset License, Royalty-Free Unlimited Use Grant, No Attribution Required, No-Resale Restriction, Bundled License Inclusion Requirement

### Community 77 - "com.unity.modules.ai"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Wavy Scattered Strand Card (Tile 1), Flowing Wavy Strand Card (Tile 2), Dense Wavy Strand Card (Tile 3), Straight Sleek Strand Card (Tile 4), White-on-Black Strand Alpha/Luminance Mask, Four-Column Horizontal Tile Layout

## Knowledge Gaps
- **130 isolated node(s):** `Reason`, `Phase`, `SetPieceSpin`, `Emote`, `Stage` (+125 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **5 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `Jersey / Nation Designs`, `Dribble System`, `Input & Keybinds`, `Goalkeeper AI & Control`, `Direct IP Transport`, `Net Set-Piece Match`, `SetPieceTaker`, `OptionsMenu`, `PrematchUI`, `Bone`, `CustomizeUI`, `Celebration`, `SkillTree`, `NetStrikerMatch`, `INetTransport`, `Footballer`, `.Empty`, `NetCodec`, `SessionBrowserUI`, `Footballer`, `.AdvanceTurn`, `.ClientUpdate`, `.Empty`, `Goalkeeper`, `GameInput`, `QuickChat`, `.Configure`, `IStrikerInput`, `Crowd`, `.PhysMat`, `com.unity.modules.adaptiveperformance`, `PeerId`, `HostSetupUI`, `JerseyDesigns.Nations8.cs`, `Crowd`, `com.unity.modules.wind`, `OptionsMenu`, `OptionsMenu`, `.Set`, `Knockdown`, `MenuUI`, `Goal`, `.Poll`, `NetPump`, `Role.cs`, `.StartRebind`, `.PhysMat`, `.Build`, `.Set`, `StadiumStyle`, `BoneSpec`, `JerseyDesigns.Nations3.cs`, `JerseyDesigns.Nations7.cs`, `ShotType.cs`, `IStrikerInput`, `StadiumSelectUI`, `JerseyDesigns.Nations2.cs`, `.NavButtons`?**
  _High betweenness centrality (0.191) - this node is a cross-community bridge._
- **Why does `NetSession` connect `JerseyDesigns.Nations9.cs` to `.Configure`, `QuickChat`, `PauseMenu`, `.AdvanceTurn`, `.Box`, `ShotServer`, `.OnGUI`, `.Build`, `SteamTransport`, `NetSetPieceMatch`, `.ClientUpdate`?**
  _High betweenness centrality (0.128) - this node is a cross-community bridge._
- **Why does `CustomizeUI` connect `Ball Physics & Launch` to `.NavButtons`, `Jersey / Nation Designs`, `Goalkeeper AI & Control`, `AccuracyGame`, `SessionBrowserUI`, `.Set`, `Knockdown`, `NetStrikerMatch`?**
  _High betweenness centrality (0.121) - this node is a cross-community bridge._
- **What connects `Reason`, `Phase`, `SetPieceSpin` to the rest of the system?**
  _130 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `AccuracyGame` be split into smaller, more focused modules?**
  _Cohesion score 0.05807200929152149 - nodes in this community are weakly interconnected._
- **Should `SessionBrowserUI` be split into smaller, more focused modules?**
  _Cohesion score 0.0693815987933635 - nodes in this community are weakly interconnected._
- **Should `ShotServer` be split into smaller, more focused modules?**
  _Cohesion score 0.14 - nodes in this community are weakly interconnected._