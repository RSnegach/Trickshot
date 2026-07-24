# Graph Report - Trickshot  (2026-07-24)

## Corpus Check
- 107 files · ~229,891 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2065 nodes · 4407 edges · 343 communities (73 shown, 270 thin omitted)
- Extraction: 87% EXTRACTED · 13% INFERRED · 0% AMBIGUOUS · INFERRED: 562 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `db905d1c`
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
- GameBootstrap
- OptionsMenu
- PrematchUI
- Bone
- Striker
- CustomizeUI
- DirectIpTransport
- GameCamera
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
- Footballer
- Footballer
- NetScrimmageMatch
- LocalTransport
- GameCamera
- NetCodec
- Dribble
- PlayerPreview
- DefensiveWall
- .PushRoster
- FlexNet
- .Empty
- Crosser
- .SafeEncode
- .ClientUpdate
- ReplaySystem
- .Box
- .Empty
- .Configure
- GameCamera
- com.unity.modules.physics
- com.unity.modules.imageconversion
- SetPieceTaker
- Vector3
- QuickChat
- ShotServer
- Sniper
- IStrikerInput
- AccuracyTarget
- KeeperGame
- .Box
- LobbySlot
- FreeplayGame
- com.unity.modules.ai
- com.unity.modules.imgui
- com.unity.modules.ui
- Crowd
- PitchBuilder
- AimReticle
- DefensiveWall
- .SkillPresetButtons
- .AdvanceTurn
- AccuracyGame
- Trickshot (3D trick-shot football prototype)
- com.unity.modules.adaptiveperformance
- .MatTex
- com.unity.modules.ai
- .ResetTo
- PitchBuilder
- Multiplayer
- LocalTransport
- com.unity.modules.wind
- OptionsMenu
- com.unity.modules.androidjni
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- .Poll
- Sniper
- SteamTransport
- PauseMenu
- Role.cs
- SimConfig
- .Configure
- .DrawKeybindings
- StadiumStyle
- Crowd
- Goalkeeper
- .Build
- IStrikerInput
- NetEndpoint
- .BuildFootballer
- com.unity.modules.terrain
- Dictionary
- float
- IPEndPoint
- List
- ulong
- Action
- int
- List
- string
- ulong
- Action
- bool
- Dictionary
- List
- ulong
- bool
- List
- RuntimeInitializeOnLoadMethod
- int
- IPEndPoint
- List
- bool
- byte
- Color
- float
- string
- uint
- Vector2
- Vector3
- int
- string
- Transform
- uint
- Vector3
- Action
- bool
- Collider
- Color
- float
- int
- Material
- Transform
- Vector3
- float
- Material
- Transform
- Vector3
- bool
- Collision
- float
- Rigidbody
- Vector3
- Action
- bool
- float
- float
- Func
- Color
- float
- Rect
- Vector2
- Vector3
- bool
- Color
- float
- int
- Material
- Transform
- Vector3
- Action
- bool
- Color
- Color32
- Material
- PhysicsMaterial
- Quaternion
- Transform
- Vector3
- bool
- float
- Vector3
- bool
- Camera
- float
- Func
- int
- List
- Material
- Mesh
- Transform
- Vector3
- bool
- float
- int
- Vector3
- bool
- float
- int
- string
- Transform
- Vector3
- bool
- Camera
- float
- Func
- Transform
- Vector3
- Action
- bool
- Collider
- float
- Quaternion
- Vector3
- Action
- bool
- int
- string
- Vector3
- bool
- Color
- float
- GUIStyle
- IReadOnlyList
- List
- string
- Texture2D
- List
- List
- List
- List
- List
- List
- List
- List
- List
- List
- bool
- float
- Func
- Quaternion
- float
- Vector3
- Action
- bool
- string
- Action
- bool
- Texture2D
- Action
- int
- string
- Vector3
- bool
- float
- int
- List
- Quaternion
- Rigidbody
- Transform
- Vector3
- bool
- float
- GUIStyle
- HashSet
- string
- Color
- float
- Rect
- Vector2
- Vector3
- bool
- float
- Func
- Vector3
- float
- int
- Vector3
- Color32
- Dictionary
- float
- int
- Vector3
- Action
- Color
- Rect
- Vector2
- bool
- float
- Func
- Vector3
- bool
- float
- int
- string
- Transform
- Vector3
- int
- string
- Transform
- Vector3
- ConfigurableJoint
- Quaternion
- Rigidbody
- Vector3
- Vector3
- Material
- PhysicsMaterial
- ConfigurableJoint
- Quaternion
- float
- int
- Material
- Transform
- Vector3
- bool
- float
- IEnumerable
- int
- bool
- Camera
- GameObject
- Material
- Refs
- Rigidbody
- Color
- float
- int
- float
- int
- Material
- Transform
- Vector3
- bool
- float
- IEnumerable
- int
- Quaternion
- Vector3
- uint
- Vector3

## God Nodes (most connected - your core abstractions)
1. `Trickshot` - 91 edges
2. `ActiveRagdoll` - 91 edges
3. `NetSession` - 78 edges
4. `CustomizeUI` - 71 edges
5. `NetSetPieceMatch` - 62 edges
6. `ScrimmageGame` - 58 edges
7. `BallController` - 53 edges
8. `JerseyDesigns` - 53 edges
9. `NetStrikerMatch` - 51 edges
10. `GameBootstrap` - 49 edges

## Surprising Connections (you probably didn't know these)
- `PlayerInputManager (local multiplayer seam)` --semantically_similar_to--> `Slot / role model (NetSession.MaxSlots=8)`  [INFERRED] [semantically similar]
  README.md → MULTIPLAYER.md
- `Trickshot Multiplayer Framework` --conceptually_related_to--> `Trickshot (3D trick-shot football prototype)`  [INFERRED]
  MULTIPLAYER.md → README.md
- `Trickshot (3D trick-shot football prototype)` --references--> `Unity 6000.4.1f1 editor version`  [EXTRACTED]
  README.md → ProjectSettings/ProjectVersion.txt
- `ScrimmageGame` --shares_data_with--> `ActiveRagdoll.cs`  [INFERRED]
  MULTIPLAYER.md → README.md
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

## Communities (343 total, 270 thin omitted)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.12
Nodes (13): Action, bool, Color32, Dictionary, float, int, string, Texture2D (+5 more)

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.14
Nodes (3): Design, DesignTab, JerseyDesigns

### Community 2 - "Dribble System"
Cohesion: 0.05
Nodes (16): bool, float, HashSet, int, List, Refs, ScrimRole, string (+8 more)

### Community 3 - "Net Messages & Wire Codec"
Cohesion: 0.14
Nodes (9): bool, Collision, float, Rigidbody, Vector3, BallController, SetPieceSpin, SphereCollider (+1 more)

### Community 4 - "Input & Keybinds"
Cohesion: 0.17
Nodes (7): bool, float, Func, Quaternion, Vector3, KeeperController, State

### Community 5 - "SkillTree"
Cohesion: 0.08
Nodes (7): DirectIpTransport, NetEndpoint, Pending, ReliableChannel, ConcurrentQueue, Thread, UdpClient

### Community 6 - "Goalkeeper AI & Control"
Cohesion: 0.13
Nodes (14): Dictionary, float, HashSet, IEnumerable, int, string, Category, Effect (+6 more)

### Community 7 - "Skill Icon Drawing"
Cohesion: 0.16
Nodes (7): Color32, Dictionary, float, int, string, Texture2D, SkillIcons

### Community 8 - "AccuracyGame"
Cohesion: 0.06
Nodes (15): CrowdCheer, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns (+7 more)

### Community 9 - "Direct IP Transport"
Cohesion: 0.16
Nodes (5): GameMode, bool, GameObject, RuntimeInitializeOnLoadMethod, GameBootstrap

### Community 10 - "Kick Detection / Ragdoll Wiring"
Cohesion: 0.13
Nodes (10): bool, float, int, List, string, uint, Vector3, Body (+2 more)

### Community 11 - "Net Set-Piece Match"
Cohesion: 0.14
Nodes (8): Camera, float, int, Light, List, Quaternion, Vector3, MenuBackground

### Community 12 - "SkillIcons"
Cohesion: 0.14
Nodes (10): Refs, bool, float, int, Vector3, Delivery, ScrimRole, SimConfig (+2 more)

### Community 14 - "OptionsMenu"
Cohesion: 0.29
Nodes (3): int, string, QuickChat

### Community 15 - "PrematchUI"
Cohesion: 0.09
Nodes (12): bool, Collider, ConfigurableJoint, float, int, IReadOnlyList, List, Quaternion (+4 more)

### Community 16 - "Bone"
Cohesion: 0.12
Nodes (11): bool, Color, float, GUIStyle, Rect, Texture2D, Hud, P (+3 more)

### Community 17 - "Striker"
Cohesion: 0.17
Nodes (7): byte, Vector2, GameInput, InputAction, InputActionAsset, InputActionMap, PlayerInput

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 21 - "GameInput"
Cohesion: 0.29
Nodes (7): dependencies, depth, source, version, dependencies, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "Celebration"
Cohesion: 0.06
Nodes (34): AccessoryEntry, bool, float, int, Material, Mesh, Transform, uint (+26 more)

### Community 23 - "SkillTree"
Cohesion: 0.12
Nodes (7): float, int, List, Queue, string, Line, QuickChatFeed

### Community 24 - "KeeperGame"
Cohesion: 0.13
Nodes (11): bool, Camera, float, int, string, Transform, uint, Vector3 (+3 more)

### Community 26 - "NetStrikerMatch"
Cohesion: 0.37
Nodes (3): Camera, Refs, Transform

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.13
Nodes (6): Goal, MultiplayerHubUI, NetBackstop, StadiumSelectUI, Refs, MonoBehaviour

### Community 28 - "com.unity.modules.jsonserialize"
Cohesion: 0.21
Nodes (6): bool, float, int, string, Vector3, GameManager

### Community 29 - "INetTransport"
Cohesion: 0.23
Nodes (5): PitchLayout, Seat, Side, StadiumBuilder, Side

### Community 31 - "Footballer"
Cohesion: 0.25
Nodes (8): Color, GameObject, Material, Transform, Vector3, Make, ColliderKind, Shader

### Community 32 - "Footballer"
Cohesion: 0.13
Nodes (11): bool, Camera, float, GameObject, Light, Material, Quaternion, Rect (+3 more)

### Community 33 - "NetScrimmageMatch"
Cohesion: 0.07
Nodes (14): bool, float, int, string, Vector3, FreeKickGame, Outcome, Phase (+6 more)

### Community 34 - "LocalTransport"
Cohesion: 0.18
Nodes (4): Rigidbody, Rigidbody, Rigidbody, Rigidbody

### Community 35 - "GameCamera"
Cohesion: 0.25
Nodes (3): Camera, Material, Transform

### Community 37 - "Dribble"
Cohesion: 0.21
Nodes (10): bool, byte, Color, float, int, string, Texture2D, PlayerAppearance (+2 more)

### Community 39 - "DefensiveWall"
Cohesion: 0.20
Nodes (10): depth, source, version, dependencies, depth, source, version, com.unity.modules.uielements (+2 more)

### Community 40 - ".PushRoster"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.physics, com.unity.modules.physics

### Community 41 - "FlexNet"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 42 - ".Empty"
Cohesion: 0.08
Nodes (17): NetRole, bool, byte, Dictionary, float, HashSet, int, PlayerAppearance (+9 more)

### Community 43 - "Crosser"
Cohesion: 0.12
Nodes (3): INetTransport, LobbyInfo, NetChannel

### Community 44 - ".SafeEncode"
Cohesion: 0.15
Nodes (8): bool, float, int, string, uint, Vector3, Body, NetStrikerMatch

### Community 46 - "ReplaySystem"
Cohesion: 0.18
Nodes (3): IEnumerator, Rect, Vector2

### Community 47 - ".Box"
Cohesion: 0.14
Nodes (5): bone, IStrikerInput, Striker, Trick, Trick

### Community 49 - ".Configure"
Cohesion: 0.27
Nodes (15): bool, byte, float, string, uint, Vector2, BodyState, InputFrame (+7 more)

### Community 50 - "GameCamera"
Cohesion: 0.22
Nodes (4): FlexNet, Link, Link, MeshRenderer

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): com.unity.modules.hierarchycore, dependencies, depth, source, version, com.unity.modules.hierarchycore

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.15
Nodes (6): Color, PlayerAppearance, NetCodec, NetWriter, BinaryWriter, MemoryStream

### Community 54 - "Vector3"
Cohesion: 0.23
Nodes (4): PhysicsMaterial, PhysicsMaterial, Arena, PhysicsMaterialCombine

### Community 56 - "ShotServer"
Cohesion: 0.07
Nodes (29): com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.animation, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+21 more)

### Community 57 - "Sniper"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.androidjni

### Community 58 - "IStrikerInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, url, version, com.unity.inputsystem

### Community 59 - "AccuracyTarget"
Cohesion: 0.12
Nodes (7): Color, Material, Material, Transform, Bone, RagdollPose, euler

### Community 60 - "KeeperGame"
Cohesion: 0.15
Nodes (6): bool, float, int, string, KeeperGame, ShotServer

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
Cohesion: 0.29
Nodes (6): dependencies, depth, source, version, dependencies, com.unity.modules.particlesystem

### Community 66 - "com.unity.modules.ui"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.ui, com.unity.modules.ui

### Community 69 - "AimReticle"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 71 - ".SkillPresetButtons"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.umbra

### Community 72 - ".AdvanceTurn"
Cohesion: 0.22
Nodes (3): Color, Func, label

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
Cohesion: 0.07
Nodes (6): Multiplayer, NetPump, HostSetupUI, LobbyUI, SessionBrowserUI, SetPieceMap

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

### Community 87 - ".Poll"
Cohesion: 0.28
Nodes (5): bool, float, Transform, Vector3, Crosser

### Community 90 - "PauseMenu"
Cohesion: 0.25
Nodes (4): Action, bool, float, PauseMenu

### Community 92 - "SimConfig"
Cohesion: 0.32
Nodes (3): Dictionary, string, ChatCensor

### Community 93 - ".Configure"
Cohesion: 0.15
Nodes (6): Texture2D, Material, Camera, Material, Transform, Texture2D

### Community 95 - "StadiumStyle"
Cohesion: 0.27
Nodes (4): Action, bool, Texture2D, MenuUI

### Community 96 - "Crowd"
Cohesion: 0.33
Nodes (3): b, KeeperPose, e

### Community 97 - "Goalkeeper"
Cohesion: 0.29
Nodes (3): Goalkeeper, State, State

## Knowledge Gaps
- **134 isolated node(s):** `LobbyInfo`, `Pending`, `SetPieceSpin`, `Emote`, `Stage` (+129 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **270 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `Jersey / Nation Designs`, `Dribble System`, `Input & Keybinds`, `Goalkeeper AI & Control`, `Net Set-Piece Match`, `SkillIcons`, `GameBootstrap`, `OptionsMenu`, `Bone`, `Striker`, `CustomizeUI`, `GameCamera`, `Celebration`, `NetSetPieceMatch`, `com.unity.modules.jsonserialize`, `INetTransport`, `Footballer`, `Footballer`, `NetScrimmageMatch`, `Dribble`, `PlayerPreview`, `.Box`, `GameCamera`, `SetPieceTaker`, `Vector3`, `QuickChat`, `AccuracyTarget`, `KeeperGame`, `.Box`, `Crowd`, `DefensiveWall`, `AccuracyGame`, `com.unity.modules.adaptiveperformance`, `.MatTex`, `.ResetTo`, `Multiplayer`, `com.unity.modules.wind`, `OptionsMenu`, `PauseMenu`, `SimConfig`, `.DrawKeybindings`, `StadiumStyle`, `Crowd`, `Goalkeeper`, `IStrikerInput`, `NetEndpoint`?**
  _High betweenness centrality (0.152) - this node is a cross-community bridge._
- **Why does `NetSession` connect `.Empty` to `GameCamera`, `NetCodec`, `PitchBuilder`, `Kick Detection / Ragdoll Wiring`, `Crosser`, `.SafeEncode`, `.ResetTo`, `PitchBuilder`, `.Configure`, `com.unity.modules.imageconversion`, `SkillTree`, `KeeperGame`, `Role.cs`, `SimConfig`, `.Configure`?**
  _High betweenness centrality (0.092) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `PrematchUI` to `Dribble System`, `Net Messages & Wire Codec`, `Input & Keybinds`, `Kick Detection / Ragdoll Wiring`, `Net Set-Piece Match`, `GameCamera`, `KeeperGame`, `NetStrikerMatch`, `NetSetPieceMatch`, `com.unity.modules.jsonserialize`, `BallController`, `Footballer`, `Footballer`, `NetScrimmageMatch`, `LocalTransport`, `.SafeEncode`, `.Box`, `AccuracyTarget`, `KeeperGame`, `.Poll`, `Sniper`, `.Configure`, `.BuildFootballer`?**
  _High betweenness centrality (0.083) - this node is a cross-community bridge._
- **What connects `LobbyInfo`, `Pending`, `SetPieceSpin` to the rest of the system?**
  _134 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Ball Physics & Launch` be split into smaller, more focused modules?**
  _Cohesion score 0.11822660098522167 - nodes in this community are weakly interconnected._
- **Should `Jersey / Nation Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.14086538461538461 - nodes in this community are weakly interconnected._
- **Should `Dribble System` be split into smaller, more focused modules?**
  _Cohesion score 0.051590483827853514 - nodes in this community are weakly interconnected._