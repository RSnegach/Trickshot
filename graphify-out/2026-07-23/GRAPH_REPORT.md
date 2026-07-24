# Graph Report - Trickshot  (2026-07-23)

## Corpus Check
- 104 files · ~222,654 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1989 nodes · 3966 edges · 376 communities (59 shown, 317 thin omitted)
- Extraction: 88% EXTRACTED · 12% INFERRED · 0% AMBIGUOUS · INFERRED: 464 edges (avg confidence: 0.8)
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
- DirectIpTransport.cs (direct-IP UDP)
- NetStrikerMatch
- NetSetPieceMatch
- com.unity.modules.jsonserialize
- INetTransport
- com.unity.modules.imgui
- .ToArray
- Footballer
- NetScrimmageMatch
- LocalTransport
- .Build
- NetCodec
- Dribble
- PlayerPreview
- DefensiveWall
- .PushRoster
- FlexNet
- Multiplayer
- .Configure
- .SafeEncode
- .ApplySlotRequest
- Goalkeeper
- com.unity.modules.assetbundle
- GameManager
- .Configure
- TimeTrialGame
- com.unity.modules.physics
- com.unity.modules.imageconversion
- IStrikerInput
- Goalkeeper
- ShotServer
- ShotServer
- Sniper
- IStrikerInput
- LocalTransport
- .Configure
- LobbySlot
- FreeplayGame
- com.unity.modules.ai
- com.unity.modules.imgui
- com.unity.modules.ui
- .MatTex
- MonoBehaviour
- AimReticle
- .Configure
- .SkillPresetButtons
- .Set
- Trickshot (3D trick-shot football prototype)
- com.unity.modules.adaptiveperformance
- com.unity.modules.ai
- AccuracyTarget
- com.unity.modules.wind
- com.unity.modules.androidjni
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- .Poll
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Role.cs
- com.unity.modules.wind
- com.unity.modules.terrain
- Vector2
- Action
- bool
- byte
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
- bool
- byte
- Dictionary
- float
- HashSet
- int
- string
- Texture2D
- uint
- byte
- Dictionary
- float
- List
- uint
- List
- bool
- float
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
- Vector3
- Collision
- float
- Rigidbody
- Vector3
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
- Dictionary
- float
- Func
- int
- Rect
- string
- Texture2D
- Vector2
- float
- GameObject
- IReadOnlyList
- List
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
- string
- Texture2D
- Action
- bool
- float
- Transform
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
- RuntimeInitializeOnLoadMethod
- Transform
- HashSet
- IEnumerable
- int
- string
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
1. `ActiveRagdoll` - 90 edges
2. `Trickshot` - 88 edges
3. `NetSetPieceMatch` - 60 edges
4. `ScrimmageGame` - 58 edges
5. `CustomizeUI` - 57 edges
6. `NetSession` - 56 edges
7. `JerseyDesigns` - 53 edges
8. `NetStrikerMatch` - 49 edges
9. `GameBootstrap` - 49 edges
10. `BallController` - 48 edges

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

## Communities (376 total, 317 thin omitted)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.12
Nodes (5): BodySub, CustomizeUI, Stage, IEnumerator, Stage

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.14
Nodes (3): Design, DesignTab, JerseyDesigns

### Community 2 - "Dribble System"
Cohesion: 0.05
Nodes (17): bool, float, GUIStyle, HashSet, int, List, Refs, ScrimRole (+9 more)

### Community 3 - "Net Messages & Wire Codec"
Cohesion: 0.06
Nodes (25): Camera, Color, float, int, Light, List, Material, Quaternion (+17 more)

### Community 4 - "Input & Keybinds"
Cohesion: 0.12
Nodes (7): bool, float, int, string, KeeperGame, CrowdCheer, ShotServer

### Community 5 - "SkillTree"
Cohesion: 0.08
Nodes (7): DirectIpTransport, NetEndpoint, Pending, ReliableChannel, ConcurrentQueue, Thread, UdpClient

### Community 6 - "Goalkeeper AI & Control"
Cohesion: 0.18
Nodes (8): Category, Effect, Node, Preset, SkillTree, Category, Effect, Node

### Community 7 - "Skill Icon Drawing"
Cohesion: 0.17
Nodes (3): Transform, GameCamera, Mode

### Community 8 - "AccuracyGame"
Cohesion: 0.06
Nodes (14): JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns (+6 more)

### Community 9 - "Direct IP Transport"
Cohesion: 0.14
Nodes (8): bool, float, int, string, uint, Vector3, Body, NetStrikerMatch

### Community 10 - "Kick Detection / Ragdoll Wiring"
Cohesion: 0.15
Nodes (9): bool, float, int, List, string, uint, Body, NetSetPieceMatch (+1 more)

### Community 11 - "Net Set-Piece Match"
Cohesion: 0.19
Nodes (3): SetPieceTaker, State, State

### Community 14 - "OptionsMenu"
Cohesion: 0.09
Nodes (6): JerseyChunkMsg, NetRole, JerseyRx, NetSession, StampedSnap, StampedSnap

### Community 15 - "PrematchUI"
Cohesion: 0.09
Nodes (14): Rigidbody, Rigidbody, Rigidbody, bool, Collider, ConfigurableJoint, float, int (+6 more)

### Community 16 - "Bone"
Cohesion: 0.08
Nodes (18): bool, Color, float, GUIStyle, Rect, Texture2D, Hud, P (+10 more)

### Community 17 - "Striker"
Cohesion: 0.06
Nodes (20): Action, bool, Texture2D, MenuUI, Action, RebindingOperation, string, OptionsMenu (+12 more)

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 21 - "GameInput"
Cohesion: 0.29
Nodes (7): dependencies, depth, source, version, dependencies, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "Celebration"
Cohesion: 0.07
Nodes (32): AccessoryEntry, bool, float, int, Material, Mesh, Transform, uint (+24 more)

### Community 24 - "KeeperGame"
Cohesion: 0.20
Nodes (6): bool, float, int, string, Vector3, GameManager

### Community 25 - "DirectIpTransport.cs (direct-IP UDP)"
Cohesion: 0.10
Nodes (11): bool, Camera, float, int, Material, string, Transform, uint (+3 more)

### Community 26 - "NetStrikerMatch"
Cohesion: 0.10
Nodes (8): b, bone, IPlayerController, Striker, Trick, KeeperPose, e, Trick

### Community 29 - "INetTransport"
Cohesion: 0.05
Nodes (23): PhysicsMaterial, Refs, Color, GameObject, Material, PhysicsMaterial, Transform, Vector3 (+15 more)

### Community 31 - ".ToArray"
Cohesion: 0.13
Nodes (7): bool, float, Func, Quaternion, Vector3, KeeperController, State

### Community 32 - "Footballer"
Cohesion: 0.12
Nodes (11): bool, Camera, float, GameObject, Light, Material, Quaternion, Rect (+3 more)

### Community 33 - "NetScrimmageMatch"
Cohesion: 0.10
Nodes (11): bool, float, int, string, Vector3, FreeKickGame, Outcome, Phase (+3 more)

### Community 36 - "NetCodec"
Cohesion: 0.10
Nodes (9): BinaryReader, BodyState, InputFrame, MatchConfig, MsgType, NetReader, ShootoutState, Snapshot (+1 more)

### Community 37 - "Dribble"
Cohesion: 0.16
Nodes (11): bool, byte, Color, float, int, string, Texture2D, PlayerAppearance (+3 more)

### Community 38 - "PlayerPreview"
Cohesion: 0.17
Nodes (5): INetTransport, LobbyInfo, NetChannel, PeerId, IEquatable

### Community 39 - "DefensiveWall"
Cohesion: 0.20
Nodes (10): depth, source, version, dependencies, depth, source, version, com.unity.modules.uielements (+2 more)

### Community 40 - ".PushRoster"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.physics, com.unity.modules.physics

### Community 41 - "FlexNet"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 47 - "com.unity.modules.assetbundle"
Cohesion: 0.21
Nodes (5): bool, float, Transform, Vector3, Crosser

### Community 48 - "GameManager"
Cohesion: 0.20
Nodes (3): IStrikerInput, NetInputSource, CrosserControl

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): com.unity.modules.hierarchycore, dependencies, depth, source, version, com.unity.modules.hierarchycore

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.17
Nodes (3): BinaryWriter, NetWriter, MemoryStream

### Community 53 - "IStrikerInput"
Cohesion: 0.47
Nodes (3): Camera, Material, Transform

### Community 54 - "Goalkeeper"
Cohesion: 0.22
Nodes (4): FlexNet, Link, Link, MeshRenderer

### Community 56 - "ShotServer"
Cohesion: 0.07
Nodes (29): com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.animation, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+21 more)

### Community 57 - "Sniper"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.androidjni

### Community 58 - "IStrikerInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, url, version, com.unity.inputsystem

### Community 61 - ".Configure"
Cohesion: 0.23
Nodes (3): Camera, Material, Transform

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

### Community 68 - "MonoBehaviour"
Cohesion: 0.21
Nodes (3): Frame, ReplaySystem, Mode

### Community 69 - "AimReticle"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - ".Configure"
Cohesion: 0.17
Nodes (6): Material, Transform, Bone, RagdollPose, ColliderKind, euler

### Community 71 - ".SkillPresetButtons"
Cohesion: 0.29
Nodes (6): dependencies, depth, source, version, dependencies, com.unity.modules.umbra

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "com.unity.modules.adaptiveperformance"
Cohesion: 0.11
Nodes (10): bool, Dictionary, float, RuntimeInitializeOnLoadMethod, string, AudioManager, Channel, AudioClip (+2 more)

### Community 77 - "com.unity.modules.ai"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 84 - "com.unity.modules.androidjni"
Cohesion: 0.60
Nodes (5): Bundled License Inclusion Requirement, Hair Atlas Asset License, No Attribution Required, No-Resale Restriction, Royalty-Free Unlimited Use Grant

### Community 85 - "Kyrgyz Sun Emblem (kyrgyz_sun.png)"
Cohesion: 0.60
Nodes (5): Forty-Ray Golden Sun, Kyrgyz Sun Emblem (kyrgyz_sun.png), Kyrgyzstan Flag Emblem, Team / National Emblem Game Asset, Tunduk (Yurt Crown) Motif

### Community 86 - "Soviet Emblem Sprite"
Cohesion: 0.60
Nodes (5): Hammer and Sickle, Soviet Emblem Sprite, Five-Pointed Star, Team Emblem / Logo, Soviet Union Symbolism

### Community 88 - "Kyrgyz Sun Emblem (kyrgyz_sun.png)"
Cohesion: 0.11
Nodes (8): Mesh, GeneratedMeshOwner, Goal, MultiplayerHubUI, NetBackstop, StadiumSelectUI, Refs, MonoBehaviour

## Knowledge Gaps
- **138 isolated node(s):** `LobbyInfo`, `StampedSnap`, `JerseyRx`, `Pending`, `SetPieceSpin` (+133 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **317 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `Jersey / Nation Designs`, `Dribble System`, `Net Messages & Wire Codec`, `Input & Keybinds`, `Goalkeeper AI & Control`, `Skill Icon Drawing`, `Net Set-Piece Match`, `Bone`, `Striker`, `CustomizeUI`, `GameCamera`, `Celebration`, `KeeperGame`, `NetStrikerMatch`, `NetSetPieceMatch`, `com.unity.modules.jsonserialize`, `INetTransport`, `com.unity.modules.imgui`, `.ToArray`, `Footballer`, `NetScrimmageMatch`, `Dribble`, `Goalkeeper`, `GameManager`, `.Configure`, `TimeTrialGame`, `Goalkeeper`, `MonoBehaviour`, `.Configure`, `.Set`, `com.unity.modules.adaptiveperformance`, `AccuracyTarget`, `com.unity.modules.wind`, `Kyrgyz Sun Emblem (kyrgyz_sun.png)`, `com.unity.modules.wind`?**
  _High betweenness centrality (0.148) - this node is a cross-community bridge._
- **Why does `NetSession` connect `OptionsMenu` to `LocalTransport`, `.MatTex`, `NetCodec`, `PlayerPreview`, `Direct IP Transport`, `Kick Detection / Ragdoll Wiring`, `ShotServer`, `DirectIpTransport.cs (direct-IP UDP)`?**
  _High betweenness centrality (0.107) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `PrematchUI` to `Footballer`, `NetScrimmageMatch`, `Dribble System`, `Net Messages & Wire Codec`, `Input & Keybinds`, `.Configure`, `Skill Icon Drawing`, `Kyrgyz Sun Emblem (kyrgyz_sun.png)`, `Direct IP Transport`, `Kick Detection / Ragdoll Wiring`, `GameBootstrap`, `com.unity.modules.assetbundle`, `GameManager`, `KeeperGame`, `DirectIpTransport.cs (direct-IP UDP)`, `NetSetPieceMatch`, `.Configure`, `.ToArray`?**
  _High betweenness centrality (0.084) - this node is a cross-community bridge._
- **What connects `LobbyInfo`, `StampedSnap`, `JerseyRx` to the rest of the system?**
  _138 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Ball Physics & Launch` be split into smaller, more focused modules?**
  _Cohesion score 0.11954022988505747 - nodes in this community are weakly interconnected._
- **Should `Jersey / Nation Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.14086538461538461 - nodes in this community are weakly interconnected._
- **Should `Dribble System` be split into smaller, more focused modules?**
  _Cohesion score 0.050686641697877656 - nodes in this community are weakly interconnected._