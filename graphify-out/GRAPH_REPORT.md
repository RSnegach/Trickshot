# Graph Report - Trickshot  (2026-07-24)

## Corpus Check
- 111 files · ~241,898 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2166 nodes · 5100 edges · 123 communities (107 shown, 16 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 579 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `68a34bb9`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Ball Physics & Launch
- Jersey / Nation Designs
- Dribble System
- Net Messages & Wire Codec
- Input & Keybinds
- SkillTree
- Goalkeeper AI & Control
- Skill Icon Drawing
- AccuracyGame
- Direct IP Transport
- Kick Detection / Ragdoll Wiring
- Net Set-Piece Match
- SkillIcons
- SetPieceTaker
- OptionsMenu
- PrematchUI
- Bone
- .Box
- CustomizeUI
- DirectIpTransport
- GameInput
- Celebration
- SkillTree
- KeeperGame
- SteamTransport
- NetStrikerMatch
- NetSetPieceMatch
- com.unity.modules.jsonserialize
- INetTransport
- BallController
- StadiumBuilder
- Footballer
- .Build
- PitchBuilder
- .Empty
- NetCodec
- CrossMap
- PlayerPreview
- DefensiveWall
- .PushRoster
- FlexNet
- Crowd
- Multiplayer
- .SafeEncode
- .ClientUpdate
- ReplaySystem
- .Box
- ReliableChannel
- .Configure
- GameCamera
- com.unity.modules.physics
- com.unity.modules.imageconversion
- MenuBackground
- Dribble
- QuickChat
- ShotServer
- Sniper
- IStrikerInput
- .SetLocalInput
- INetTransport
- .Box
- LobbySlot
- FreeplayGame
- com.unity.modules.ai
- com.unity.modules.imgui
- com.unity.modules.ui
- Crowd
- ChatCensor
- AimReticle
- DefensiveWall
- .SkillPresetButtons
- .AdvanceTurn
- AccuracyGame
- Trickshot (3D trick-shot football prototype)
- com.unity.modules.adaptiveperformance
- com.unity.modules.ai
- .ResetTo
- SessionBrowserUI
- IStrikerInput
- .Update
- com.unity.modules.wind
- OptionsMenu
- com.unity.modules.androidjni
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- GameMode
- .Set
- JerseyDesigns.Nations10.cs
- .Box
- SimConfig
- LobbyInfo
- .Poll
- .DrawKeybindings
- .HandlePassInput
- Crowd
- IPlayerController
- JerseyDesigns.Nations6.cs
- .ResetTo
- PitchLayout
- Knockdown
- StadiumStyle
- CrosserBubble
- .Set
- NetChannel
- .BroadcastSnapshot
- .FindBody
- MenuUI
- ShotType.cs
- .KickTo
- ChatCensor
- CrosserBubble
- .Configure
- Rect
- JerseyDesigns.Nations2.cs
- JerseyDesigns.Nations8.cs
- Role.cs
- GameObject
- Refs
- RuntimeInitializeOnLoadMethod
- com.unity.modules.terrain

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 101 edges
2. `Trickshot` - 94 edges
3. `NetSession` - 79 edges
4. `CustomizeUI` - 76 edges
5. `NetSetPieceMatch` - 62 edges
6. `JerseyDesigns` - 58 edges
7. `ScrimmageGame` - 58 edges
8. `GameCamera` - 54 edges
9. `BallController` - 54 edges
10. `NetStrikerMatch` - 51 edges

## Surprising Connections (you probably didn't know these)
- `PlayerInputManager (local multiplayer seam)` --semantically_similar_to--> `Slot / role model (NetSession.MaxSlots=8)`  [INFERRED] [semantically similar]
  README.md → MULTIPLAYER.md
- `Trickshot Multiplayer Framework` --conceptually_related_to--> `Trickshot (3D trick-shot football prototype)`  [INFERRED]
  MULTIPLAYER.md → README.md
- `Trickshot (3D trick-shot football prototype)` --references--> `Unity 6000.4.1f1 editor version`  [EXTRACTED]
  README.md → ProjectSettings/ProjectVersion.txt
- `ScrimmageGame` --shares_data_with--> `ActiveRagdoll.cs`  [INFERRED]
  MULTIPLAYER.md → README.md
- `AccuracyGame` --references--> `Crosser`  [EXTRACTED]
  Assets/Scripts/Play/AccuracyGame.cs → Assets/Scripts/Play/Crosser.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Hair Atlas License Terms Set** — assets_resources_hair_hairatlas_license_royalty_free_use, assets_resources_hair_hairatlas_license_no_attribution_required, assets_resources_hair_hairatlas_license_no_resale_restriction, assets_resources_hair_hairatlas_license_bundled_license_requirement [EXTRACTED 1.00]
- **Four strand-card tiles compose the hair atlas** — assets_resources_hair_hairatlas_wavy_scattered_strands, assets_resources_hair_hairatlas_flowing_wavy_strands, assets_resources_hair_hairatlas_dense_wavy_strands, assets_resources_hair_hairatlas_straight_sleek_strands, assets_resources_hair_hairatlas_atlas [EXTRACTED 1.00]
- **graphify CLI commands** — claude_graphify_query, claude_graphify_path, claude_graphify_explain, claude_graphify_update [EXTRACTED 0.85]
- **Interchangeable transports behind INetTransport seam** — multiplayer_inettransport, multiplayer_directiptransport, multiplayer_localtransport, multiplayer_steamtransport [EXTRACTED 1.00]
- **Active-ragdoll bicycle-kick mechanic** — readme_activeragdoll, readme_ragdollpose, readme_kickdetector, readme_jointmath, readme_bicycle_kick [INFERRED 0.85]
- **Host-authoritative frame loop (poll, input, snapshot)** — multiplayer_multiplayer, multiplayer_netsession, multiplayer_netmessages, multiplayer_host_authoritative [INFERRED 0.85]

## Communities (123 total, 16 thin omitted)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.12
Nodes (13): Action, bool, Color32, Dictionary, float, int, string, Texture2D (+5 more)

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.12
Nodes (21): Action, Color32, Dictionary, int, IReadOnlyList, List, string, Texture2D (+13 more)

### Community 2 - "Dribble System"
Cohesion: 0.09
Nodes (11): bool, float, GUIStyle, HashSet, int, List, Refs, ScrimRole (+3 more)

### Community 3 - "Net Messages & Wire Codec"
Cohesion: 0.08
Nodes (17): PeerId, NetRole, bool, byte, Dictionary, float, HashSet, int (+9 more)

### Community 4 - "Input & Keybinds"
Cohesion: 0.25
Nodes (5): float, Material, Transform, Vector3, AimReticle

### Community 5 - "SkillTree"
Cohesion: 0.13
Nodes (9): bool, byte, Dictionary, float, ulong, DirectIpTransport, ConcurrentQueue, Thread (+1 more)

### Community 6 - "Goalkeeper AI & Control"
Cohesion: 0.11
Nodes (15): Dictionary, float, HashSet, IEnumerable, int, string, Category, Effect (+7 more)

### Community 7 - "Skill Icon Drawing"
Cohesion: 0.16
Nodes (7): Color32, Dictionary, float, int, string, Texture2D, SkillIcons

### Community 8 - "AccuracyGame"
Cohesion: 0.06
Nodes (13): JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns (+5 more)

### Community 9 - "Direct IP Transport"
Cohesion: 0.20
Nodes (6): bool, float, int, List, Vector3, Footballer

### Community 10 - "Kick Detection / Ragdoll Wiring"
Cohesion: 0.14
Nodes (10): bool, float, int, List, string, uint, Vector3, Body (+2 more)

### Community 11 - "Net Set-Piece Match"
Cohesion: 0.33
Nodes (5): int, string, AdultQuiz, Q, Q

### Community 12 - "SkillIcons"
Cohesion: 0.23
Nodes (5): bool, float, int, string, KeeperGame

### Community 13 - "SetPieceTaker"
Cohesion: 0.19
Nodes (9): Collider, Color, float, GameObject, int, Material, Transform, Vector3 (+1 more)

### Community 15 - "PrematchUI"
Cohesion: 0.10
Nodes (15): bool, Collider, ConfigurableJoint, float, int, IReadOnlyList, List, Material (+7 more)

### Community 16 - "Bone"
Cohesion: 0.20
Nodes (9): bool, Color, float, GUIStyle, Rect, Texture2D, Hud, P (+1 more)

### Community 17 - ".Box"
Cohesion: 0.13
Nodes (11): bool, Color, float, int, Material, Transform, Vector3, Crowd (+3 more)

### Community 18 - "CustomizeUI"
Cohesion: 0.10
Nodes (13): Action, bool, Delivery, float, int, ScrimRole, string, Vector3 (+5 more)

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 21 - "GameInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "Celebration"
Cohesion: 0.06
Nodes (34): AccessoryEntry, bool, float, int, Material, Mesh, Transform, uint (+26 more)

### Community 23 - "SkillTree"
Cohesion: 0.06
Nodes (26): float, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion, Transform (+18 more)

### Community 24 - "KeeperGame"
Cohesion: 0.11
Nodes (12): bool, Camera, float, int, Material, Refs, string, Transform (+4 more)

### Community 25 - "SteamTransport"
Cohesion: 0.11
Nodes (8): IPlayerController, bool, float, Func, Quaternion, Vector3, KeeperController, State

### Community 26 - "NetStrikerMatch"
Cohesion: 0.18
Nodes (4): Dictionary, string, Keybinds, InputAction

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.34
Nodes (5): Material, Transform, uint, Vector3, SurroundBuilder

### Community 28 - "com.unity.modules.jsonserialize"
Cohesion: 0.13
Nodes (14): ActiveRagdoll, AimReticle, BallController, bool, float, GameInput, Goalkeeper, int (+6 more)

### Community 29 - "INetTransport"
Cohesion: 0.14
Nodes (12): Action, bool, int, string, Vector3, HostSetupUI, Color, float (+4 more)

### Community 30 - "BallController"
Cohesion: 0.16
Nodes (7): float, int, List, Queue, string, Line, QuickChatFeed

### Community 31 - "StadiumBuilder"
Cohesion: 0.32
Nodes (9): Color, float, int, Material, PhysicsMaterial, Transform, Vector3, StadiumBuilder (+1 more)

### Community 32 - "Footballer"
Cohesion: 0.12
Nodes (11): bool, Camera, float, GameObject, Light, Material, Quaternion, Rect (+3 more)

### Community 33 - ".Build"
Cohesion: 0.29
Nodes (3): int, string, QuickChat

### Community 34 - "PitchBuilder"
Cohesion: 0.32
Nodes (6): float, int, Material, Transform, Vector3, PitchBuilder

### Community 35 - ".Empty"
Cohesion: 0.19
Nodes (8): ActiveRagdoll, AimReticle, BallController, bool, float, Transform, Vector3, Crosser

### Community 36 - "NetCodec"
Cohesion: 0.29
Nodes (3): Color, NetReader, BinaryReader

### Community 37 - "CrossMap"
Cohesion: 0.33
Nodes (6): float, Vector3, CrossMap, Color, Rect, Vector2

### Community 38 - "PlayerPreview"
Cohesion: 0.29
Nodes (4): Action, bool, Collider, Goal

### Community 39 - "DefensiveWall"
Cohesion: 0.20
Nodes (10): depth, source, version, dependencies, depth, source, version, com.unity.modules.uielements (+2 more)

### Community 40 - ".PushRoster"
Cohesion: 0.29
Nodes (7): dependencies, depth, source, version, dependencies, com.unity.modules.physics, com.unity.modules.physics

### Community 41 - "FlexNet"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 42 - "Crowd"
Cohesion: 0.08
Nodes (16): bool, List, RuntimeInitializeOnLoadMethod, Multiplayer, int, IPEndPoint, List, NetEndpoint (+8 more)

### Community 43 - "Multiplayer"
Cohesion: 0.23
Nodes (4): bool, float, Vector3, Dribble

### Community 44 - ".SafeEncode"
Cohesion: 0.11
Nodes (9): AnimState, ReplaySystem, string, Vector3, NetStrikerMatch, Body, NetSession, QuickChatFeed (+1 more)

### Community 45 - ".ClientUpdate"
Cohesion: 0.24
Nodes (6): AimReticle, BallController, Camera, GameInput, Material, Transform

### Community 46 - "ReplaySystem"
Cohesion: 0.24
Nodes (8): PhysicsMaterial, float, PhysicsMaterial, Transform, Vector3, Refs, ScrimmageArena, PhysicsMaterialCombine

### Community 47 - ".Box"
Cohesion: 0.09
Nodes (11): Rigidbody, Rigidbody, bool, float, Func, Vector3, Striker, Trick (+3 more)

### Community 48 - "ReliableChannel"
Cohesion: 0.16
Nodes (7): byte, Dictionary, float, List, uint, Pending, ReliableChannel

### Community 49 - ".Configure"
Cohesion: 0.19
Nodes (18): bool, Vector2, NetInputSource, bool, byte, float, string, uint (+10 more)

### Community 50 - "GameCamera"
Cohesion: 0.20
Nodes (9): Color, Material, Color, GameObject, Material, Transform, Vector3, Make (+1 more)

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): com.unity.modules.hierarchycore, dependencies, depth, source, version, com.unity.modules.hierarchycore

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.16
Nodes (6): PlayerAppearance, Vector3, NetCodec, NetWriter, BinaryWriter, MemoryStream

### Community 53 - "MenuBackground"
Cohesion: 0.06
Nodes (23): bool, Camera, float, Func, int, List, Material, Mesh (+15 more)

### Community 54 - "Dribble"
Cohesion: 0.18
Nodes (12): ActiveRagdoll, bool, float, Goalkeeper, int, Rigidbody, Striker, Body (+4 more)

### Community 55 - "QuickChat"
Cohesion: 0.16
Nodes (7): float, Quaternion, Vector3, Goalkeeper, State, Vector3, State

### Community 56 - "ShotServer"
Cohesion: 0.07
Nodes (29): com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.animation, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+21 more)

### Community 57 - "Sniper"
Cohesion: 0.29
Nodes (6): dependencies, depth, source, version, dependencies, com.unity.modules.androidjni

### Community 58 - "IStrikerInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, url, version, com.unity.inputsystem

### Community 59 - ".SetLocalInput"
Cohesion: 0.15
Nodes (10): bool, float, int, List, Quaternion, Rigidbody, Transform, Vector3 (+2 more)

### Community 60 - "INetTransport"
Cohesion: 0.15
Nodes (8): bool, float, int, string, Transform, uint, Vector3, AccuracyGame

### Community 61 - ".Box"
Cohesion: 0.18
Nodes (8): bool, Delivery, float, int, string, Transform, Vector3, FreeplayGame

### Community 62 - "LobbySlot"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.imgui, com.unity.modules.imgui

### Community 63 - "FreeplayGame"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.animation

### Community 64 - "com.unity.modules.ai"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.audio

### Community 65 - "com.unity.modules.imgui"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.particlesystem

### Community 66 - "com.unity.modules.ui"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.ui, com.unity.modules.ui

### Community 67 - "Crowd"
Cohesion: 0.21
Nodes (7): bool, float, int, string, Transform, Vector3, TimeTrialGame

### Community 68 - "ChatCensor"
Cohesion: 0.10
Nodes (5): Action, List, INetTransport, List, SteamTransport

### Community 69 - "AimReticle"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - "DefensiveWall"
Cohesion: 0.14
Nodes (11): Action, bool, Collider, Color, float, int, Material, Transform (+3 more)

### Community 71 - ".SkillPresetButtons"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.umbra

### Community 72 - ".AdvanceTurn"
Cohesion: 0.22
Nodes (3): Color, Func, label

### Community 73 - "AccuracyGame"
Cohesion: 0.24
Nodes (7): Action, bool, float, Transform, Vector3, Sniper, LineRenderer

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "com.unity.modules.adaptiveperformance"
Cohesion: 0.09
Nodes (13): bool, Dictionary, float, IEnumerator, int, RuntimeInitializeOnLoadMethod, string, Vector3 (+5 more)

### Community 77 - "com.unity.modules.ai"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 78 - ".ResetTo"
Cohesion: 0.18
Nodes (4): Action, bool, string, LobbyUI

### Community 79 - "SessionBrowserUI"
Cohesion: 0.18
Nodes (3): IEnumerator, Rect, Vector2

### Community 80 - "IStrikerInput"
Cohesion: 0.21
Nodes (5): Vector2, IStrikerInput, float, Func, CrosserControl

### Community 82 - "com.unity.modules.wind"
Cohesion: 0.33
Nodes (5): ConfigurableJoint, Quaternion, Rigidbody, JointMath, Space

### Community 83 - "OptionsMenu"
Cohesion: 0.19
Nodes (8): Action, int, RebindingOperation, string, Vector2, OptionsMenu, Tab, Tab

### Community 84 - "com.unity.modules.androidjni"
Cohesion: 0.60
Nodes (5): Bundled License Inclusion Requirement, Hair Atlas Asset License, No Attribution Required, No-Resale Restriction, Royalty-Free Unlimited Use Grant

### Community 85 - "Kyrgyz Sun Emblem (kyrgyz_sun.png)"
Cohesion: 0.60
Nodes (5): Forty-Ray Golden Sun, Kyrgyz Sun Emblem (kyrgyz_sun.png), Kyrgyzstan Flag Emblem, Team / National Emblem Game Asset, Tunduk (Yurt Crown) Motif

### Community 86 - "Soviet Emblem Sprite"
Cohesion: 0.60
Nodes (5): Hammer and Sickle, Soviet Emblem Sprite, Five-Pointed Star, Team Emblem / Logo, Soviet Union Symbolism

### Community 87 - "GameMode"
Cohesion: 0.07
Nodes (30): bool, Camera, float, Transform, Vector3, GameCamera, Mode, ActiveRagdoll (+22 more)

### Community 88 - ".Set"
Cohesion: 0.38
Nodes (4): Vector3, RagdollPose, bone, euler

### Community 90 - ".Box"
Cohesion: 0.35
Nodes (6): Material, PhysicsMaterial, Transform, Vector3, Arena, Refs

### Community 91 - "SimConfig"
Cohesion: 0.17
Nodes (9): bool, float, int, Vector3, Delivery, ScrimRole, SimConfig, Delivery (+1 more)

### Community 92 - "LobbyInfo"
Cohesion: 0.18
Nodes (8): Action, List, int, string, ulong, LobbyInfo, Action, List

### Community 93 - ".Poll"
Cohesion: 0.24
Nodes (10): bool, byte, Color, float, int, string, Texture2D, PlayerAppearance (+2 more)

### Community 94 - ".DrawKeybindings"
Cohesion: 0.19
Nodes (6): byte, Vector2, GameInput, InputActionAsset, InputActionMap, PlayerInput

### Community 95 - ".HandlePassInput"
Cohesion: 0.25
Nodes (4): Action, bool, float, PauseMenu

### Community 96 - "Crowd"
Cohesion: 0.38
Nodes (4): Vector3, KeeperPose, b, e

### Community 97 - "IPlayerController"
Cohesion: 0.08
Nodes (15): bool, Collision, float, Rigidbody, Vector3, BallController, SetPieceSpin, Action (+7 more)

### Community 99 - "JerseyDesigns.Nations6.cs"
Cohesion: 0.18
Nodes (5): bool, Dictionary, Queue, ulong, LocalTransport

### Community 100 - ".ResetTo"
Cohesion: 0.31
Nodes (4): float, int, Vector3, ShotServer

### Community 101 - "PitchLayout"
Cohesion: 0.18
Nodes (8): bool, float, int, Quaternion, Vector3, PitchLayout, Seat, Side

### Community 102 - "Knockdown"
Cohesion: 0.32
Nodes (3): bool, float, Knockdown

### Community 103 - "StadiumStyle"
Cohesion: 0.32
Nodes (7): bool, Color, float, int, string, StadiumStyle, Surroundings

### Community 104 - "CrosserBubble"
Cohesion: 0.29
Nodes (5): ActiveRagdoll, float, Transform, CrosserBubble, Collider

### Community 105 - ".Set"
Cohesion: 0.20
Nodes (8): Action, bool, float, Vector3, Celebration, Emote, EmotePose, Emote

### Community 109 - "MenuUI"
Cohesion: 0.25
Nodes (5): Action, bool, Texture2D, GameMode, MenuUI

### Community 112 - "ChatCensor"
Cohesion: 0.14
Nodes (4): Vector3, Dictionary, string, ChatCensor

### Community 113 - "CrosserBubble"
Cohesion: 0.18
Nodes (6): Action, MultiplayerHubUI, NetBackstop, Action, StadiumSelectUI, MonoBehaviour

### Community 114 - ".Configure"
Cohesion: 0.23
Nodes (5): Texture2D, Camera, Material, Transform, Texture2D

## Knowledge Gaps
- **129 isolated node(s):** `Mode`, `Delivery`, `ScrimRole`, `SetPieceSpin`, `Emote` (+124 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **16 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `Jersey / Nation Designs`, `Input & Keybinds`, `Goalkeeper AI & Control`, `Net Set-Piece Match`, `SkillIcons`, `SetPieceTaker`, `Bone`, `.Box`, `CustomizeUI`, `Celebration`, `SkillTree`, `SteamTransport`, `NetStrikerMatch`, `com.unity.modules.jsonserialize`, `INetTransport`, `Footballer`, `.Build`, `PitchBuilder`, `.Empty`, `CrossMap`, `PlayerPreview`, `Multiplayer`, `.SafeEncode`, `ReplaySystem`, `.Box`, `GameCamera`, `MenuBackground`, `QuickChat`, `.SetLocalInput`, `INetTransport`, `.Box`, `Crowd`, `DefensiveWall`, `AccuracyGame`, `com.unity.modules.adaptiveperformance`, `IStrikerInput`, `com.unity.modules.wind`, `OptionsMenu`, `GameMode`, `.Set`, `.Box`, `SimConfig`, `.Poll`, `.DrawKeybindings`, `.HandlePassInput`, `Crowd`, `IPlayerController`, `.ResetTo`, `PitchLayout`, `Knockdown`, `StadiumStyle`, `CrosserBubble`, `.Set`, `MenuUI`, `ChatCensor`, `CrosserBubble`?**
  _High betweenness centrality (0.195) - this node is a cross-community bridge._
- **Why does `NetSession` connect `Net Messages & Wire Codec` to `JerseyDesigns.Nations3.cs`, `ChatCensor`, `NetCodec`, `AccuracyGame`, `Crowd`, `.BroadcastSnapshot`, `Kick Detection / Ragdoll Wiring`, `.ResetTo`, `ChatCensor`, `.Configure`, `.Configure`, `.Update`, `KeeperGame`, `BallController`?**
  _High betweenness centrality (0.151) - this node is a cross-community bridge._
- **Why does `CustomizeUI` connect `Ball Physics & Launch` to `Footballer`, `Jersey / Nation Designs`, `Goalkeeper AI & Control`, `AccuracyGame`, `.AdvanceTurn`, `SessionBrowserUI`, `CrosserBubble`, `.OnGUI`, `.Poll`?**
  _High betweenness centrality (0.092) - this node is a cross-community bridge._
- **What connects `Mode`, `Delivery`, `ScrimRole` to the rest of the system?**
  _129 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Ball Physics & Launch` be split into smaller, more focused modules?**
  _Cohesion score 0.11904761904761904 - nodes in this community are weakly interconnected._
- **Should `Jersey / Nation Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.11901263590949163 - nodes in this community are weakly interconnected._
- **Should `Dribble System` be split into smaller, more focused modules?**
  _Cohesion score 0.09090909090909091 - nodes in this community are weakly interconnected._