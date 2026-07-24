# Graph Report - Trickshot  (2026-07-24)

## Corpus Check
- 109 files · ~237,638 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2093 nodes · 5160 edges · 100 communities (94 shown, 6 thin omitted)
- Extraction: 87% EXTRACTED · 13% INFERRED · 0% AMBIGUOUS · INFERRED: 668 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `0c6a05b7`
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
- .Build
- LocalTransport
- .Empty
- NetCodec
- Dribble
- PlayerPreview
- DefensiveWall
- .PushRoster
- FlexNet
- Crosser
- Multiplayer
- .SafeEncode
- .ClientUpdate
- ReplaySystem
- .Box
- .Empty
- .Configure
- GameCamera
- com.unity.modules.physics
- com.unity.modules.imageconversion
- MenuBackground
- .Cylinder
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
- .StartRebind
- com.unity.modules.ai
- .ResetTo
- JerseyDesigns.Nations2.cs
- com.unity.modules.wind
- OptionsMenu
- com.unity.modules.androidjni
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- GameMode
- .Set
- JerseyDesigns.Nations4.cs
- JerseyDesigns.Nations9.cs
- ShotType.cs
- PauseMenu
- Crowd
- LobbyInfo
- MenuUI
- QuickChat
- MonoBehaviour
- Knockdown
- StadiumStyle
- Goal
- com.unity.modules.terrain

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 113 edges
2. `Trickshot` - 93 edges
3. `NetSession` - 80 edges
4. `CustomizeUI` - 75 edges
5. `BallController` - 71 edges
6. `NetSetPieceMatch` - 62 edges
7. `ScrimmageGame` - 59 edges
8. `Striker` - 59 edges
9. `JerseyDesigns` - 58 edges
10. `GameCamera` - 54 edges

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

## Communities (100 total, 6 thin omitted)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.10
Nodes (13): Action, bool, Color32, Dictionary, float, int, string, Texture2D (+5 more)

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.12
Nodes (21): Action, Color32, Dictionary, int, IReadOnlyList, List, string, Texture2D (+13 more)

### Community 2 - "Dribble System"
Cohesion: 0.06
Nodes (21): bool, float, Vector3, Dribble, bool, float, int, Vector3 (+13 more)

### Community 3 - "Net Messages & Wire Codec"
Cohesion: 0.07
Nodes (14): NetRole, bool, byte, Dictionary, HashSet, int, PlayerAppearance, Queue (+6 more)

### Community 4 - "Input & Keybinds"
Cohesion: 0.13
Nodes (10): bool, Collision, float, Rigidbody, Vector3, BallController, SetPieceSpin, Rigidbody (+2 more)

### Community 5 - "SkillTree"
Cohesion: 0.05
Nodes (23): Action, bool, byte, Dictionary, float, IPEndPoint, List, ulong (+15 more)

### Community 6 - "Goalkeeper AI & Control"
Cohesion: 0.11
Nodes (15): Dictionary, float, HashSet, IEnumerable, int, string, Category, Effect (+7 more)

### Community 7 - "Skill Icon Drawing"
Cohesion: 0.16
Nodes (7): Color32, Dictionary, float, int, string, Texture2D, SkillIcons

### Community 8 - "AccuracyGame"
Cohesion: 0.06
Nodes (12): JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns (+4 more)

### Community 9 - "Direct IP Transport"
Cohesion: 0.14
Nodes (12): Action, bool, int, string, Vector3, HostSetupUI, Color, float (+4 more)

### Community 10 - "Kick Detection / Ragdoll Wiring"
Cohesion: 0.10
Nodes (13): bool, Camera, float, int, List, Material, string, Transform (+5 more)

### Community 11 - "Net Set-Piece Match"
Cohesion: 0.33
Nodes (5): int, string, AdultQuiz, Q, Q

### Community 12 - "SkillIcons"
Cohesion: 0.23
Nodes (5): bool, float, int, string, KeeperGame

### Community 13 - "SetPieceTaker"
Cohesion: 0.17
Nodes (7): bool, float, Func, Vector3, SetPieceTaker, State, SetPieceSpin

### Community 14 - "OptionsMenu"
Cohesion: 0.29
Nodes (4): float, int, Vector3, ShotServer

### Community 15 - "PrematchUI"
Cohesion: 0.11
Nodes (12): bool, Collider, ConfigurableJoint, float, int, IReadOnlyList, List, Quaternion (+4 more)

### Community 16 - "Bone"
Cohesion: 0.10
Nodes (15): Color, float, Rect, Vector2, Vector3, CrossMap, bool, Color (+7 more)

### Community 17 - "Striker"
Cohesion: 0.14
Nodes (9): Action, byte, RebindingOperation, Vector2, GameInput, InputAction, InputActionAsset, InputActionMap (+1 more)

### Community 18 - "CustomizeUI"
Cohesion: 0.10
Nodes (12): Action, bool, Delivery, float, int, string, Vector3, PrematchUI (+4 more)

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 20 - "GameCamera"
Cohesion: 0.15
Nodes (9): bool, Camera, float, Func, Transform, Vector3, GameCamera, Mode (+1 more)

### Community 21 - "GameInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "Celebration"
Cohesion: 0.06
Nodes (34): AccessoryEntry, bool, float, int, Material, Mesh, Transform, uint (+26 more)

### Community 23 - "SkillTree"
Cohesion: 0.08
Nodes (20): float, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion, Transform (+12 more)

### Community 24 - "KeeperGame"
Cohesion: 0.13
Nodes (12): bool, Camera, float, int, Material, Refs, string, Transform (+4 more)

### Community 25 - "SteamTransport"
Cohesion: 0.09
Nodes (15): float, Quaternion, Vector3, Goalkeeper, State, IPlayerController, bool, float (+7 more)

### Community 26 - "NetStrikerMatch"
Cohesion: 0.14
Nodes (11): Action, bool, Collider, Color, float, int, Material, Transform (+3 more)

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.14
Nodes (10): bool, float, int, List, Quaternion, Rigidbody, Transform, Vector3 (+2 more)

### Community 29 - "INetTransport"
Cohesion: 0.12
Nodes (7): bool, float, int, string, Transform, Vector3, GameManager

### Community 30 - "BallController"
Cohesion: 0.17
Nodes (8): Action, bool, float, Vector3, Celebration, Emote, EmotePose, Emote

### Community 31 - "Footballer"
Cohesion: 0.30
Nodes (4): Camera, Material, Refs, Transform

### Community 32 - "Footballer"
Cohesion: 0.12
Nodes (11): bool, Camera, float, GameObject, Light, Material, Quaternion, Rect (+3 more)

### Community 33 - ".Build"
Cohesion: 0.08
Nodes (30): ScrimRole, GameObject, PhysicsMaterial, Transform, Vector3, float, int, Material (+22 more)

### Community 34 - "LocalTransport"
Cohesion: 0.17
Nodes (5): GameMode, bool, GameObject, RuntimeInitializeOnLoadMethod, GameBootstrap

### Community 35 - ".Empty"
Cohesion: 0.15
Nodes (9): Camera, float, int, Light, List, PhysicsMaterial, Quaternion, Vector3 (+1 more)

### Community 36 - "NetCodec"
Cohesion: 0.23
Nodes (5): Color, Vector2, MsgType, NetReader, BinaryReader

### Community 37 - "Dribble"
Cohesion: 0.24
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

### Community 42 - "Crosser"
Cohesion: 0.27
Nodes (5): float, Material, Transform, Vector3, AimReticle

### Community 43 - "Multiplayer"
Cohesion: 0.21
Nodes (5): Vector2, IStrikerInput, float, Func, CrosserControl

### Community 44 - ".SafeEncode"
Cohesion: 0.16
Nodes (8): bool, float, int, string, uint, Vector3, Body, NetStrikerMatch

### Community 45 - ".ClientUpdate"
Cohesion: 0.16
Nodes (5): Texture2D, Camera, Material, Transform, Texture2D

### Community 46 - "ReplaySystem"
Cohesion: 0.16
Nodes (3): IEnumerator, Rect, Vector2

### Community 47 - ".Box"
Cohesion: 0.07
Nodes (15): Action, Collision, float, KickDetector, Rigidbody, Rigidbody, Rigidbody, bool (+7 more)

### Community 48 - ".Empty"
Cohesion: 0.17
Nodes (8): bool, float, int, string, Transform, uint, Vector3, AccuracyGame

### Community 49 - ".Configure"
Cohesion: 0.19
Nodes (18): bool, Vector2, NetInputSource, bool, byte, float, string, uint (+10 more)

### Community 50 - "GameCamera"
Cohesion: 0.12
Nodes (14): bool, Camera, float, Func, int, List, Material, Mesh (+6 more)

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): com.unity.modules.hierarchycore, dependencies, depth, source, version, com.unity.modules.hierarchycore

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.16
Nodes (6): PlayerAppearance, Vector3, NetCodec, NetWriter, BinaryWriter, MemoryStream

### Community 53 - "MenuBackground"
Cohesion: 0.19
Nodes (9): Collider, Color, float, GameObject, int, Material, Transform, Vector3 (+1 more)

### Community 55 - "QuickChat"
Cohesion: 0.24
Nodes (7): Action, bool, float, Transform, Vector3, Sniper, LineRenderer

### Community 56 - "ShotServer"
Cohesion: 0.07
Nodes (29): com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.animation, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+21 more)

### Community 57 - "Sniper"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.androidjni

### Community 58 - "IStrikerInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, url, version, com.unity.inputsystem

### Community 60 - "INetTransport"
Cohesion: 0.17
Nodes (8): bool, Delivery, float, int, string, Transform, Vector3, FreeplayGame

### Community 61 - ".Box"
Cohesion: 0.24
Nodes (5): bool, float, Transform, Vector3, Crosser

### Community 62 - "LobbySlot"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.imgui, com.unity.modules.imgui

### Community 63 - "FreeplayGame"
Cohesion: 0.29
Nodes (6): dependencies, depth, source, version, dependencies, com.unity.modules.animation

### Community 64 - "com.unity.modules.ai"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.audio

### Community 65 - "com.unity.modules.imgui"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.particlesystem

### Community 66 - "com.unity.modules.ui"
Cohesion: 0.29
Nodes (7): dependencies, depth, source, version, dependencies, com.unity.modules.ui, com.unity.modules.ui

### Community 67 - "Crowd"
Cohesion: 0.23
Nodes (6): Action, float, int, List, string, SessionBrowserUI

### Community 68 - "ChatCensor"
Cohesion: 0.14
Nodes (7): float, int, List, Queue, string, Line, QuickChatFeed

### Community 69 - "AimReticle"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - "DefensiveWall"
Cohesion: 0.21
Nodes (7): bool, float, int, string, Transform, Vector3, TimeTrialGame

### Community 71 - ".SkillPresetButtons"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.umbra

### Community 72 - ".AdvanceTurn"
Cohesion: 0.22
Nodes (3): Color, Func, label

### Community 73 - "AccuracyGame"
Cohesion: 0.38
Nodes (3): Dictionary, string, ChatCensor

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
Cohesion: 0.15
Nodes (5): bool, List, RuntimeInitializeOnLoadMethod, Multiplayer, NetPump

### Community 80 - "JerseyDesigns.Nations2.cs"
Cohesion: 0.09
Nodes (28): bool, Color, float, int, Material, Transform, Vector3, Crowd (+20 more)

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
Cohesion: 0.25
Nodes (4): Action, bool, string, LobbyUI

### Community 88 - ".Set"
Cohesion: 0.38
Nodes (4): Vector3, RagdollPose, bone, euler

### Community 89 - "JerseyDesigns.Nations4.cs"
Cohesion: 0.11
Nodes (9): Color, Material, Material, Transform, Color, Material, Make, PhysicsMaterialCombine (+1 more)

### Community 92 - "JerseyDesigns.Nations9.cs"
Cohesion: 0.40
Nodes (6): Material, PhysicsMaterial, Transform, Vector3, Arena, Refs

### Community 93 - "ShotType.cs"
Cohesion: 0.22
Nodes (3): Dictionary, string, Keybinds

### Community 94 - "PauseMenu"
Cohesion: 0.25
Nodes (4): Action, bool, float, PauseMenu

### Community 96 - "Crowd"
Cohesion: 0.38
Nodes (4): Vector3, KeeperPose, b, e

### Community 97 - "LobbyInfo"
Cohesion: 0.06
Nodes (19): Action, int, List, string, ulong, INetTransport, LobbyInfo, NetChannel (+11 more)

### Community 98 - "MenuUI"
Cohesion: 0.27
Nodes (4): Action, bool, Texture2D, MenuUI

### Community 99 - "QuickChat"
Cohesion: 0.29
Nodes (3): int, string, QuickChat

### Community 102 - "MonoBehaviour"
Cohesion: 0.33
Nodes (4): Action, MultiplayerHubUI, NetBackstop, MonoBehaviour

### Community 106 - "Knockdown"
Cohesion: 0.32
Nodes (3): bool, float, Knockdown

### Community 107 - "StadiumStyle"
Cohesion: 0.32
Nodes (7): bool, Color, float, int, string, StadiumStyle, Surroundings

### Community 108 - "Goal"
Cohesion: 0.29
Nodes (4): Action, bool, Collider, Goal

## Knowledge Gaps
- **129 isolated node(s):** `SetPieceSpin`, `Emote`, `Stage`, `BodySub`, `Phase` (+124 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **6 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `Jersey / Nation Designs`, `Dribble System`, `Goalkeeper AI & Control`, `Direct IP Transport`, `Net Set-Piece Match`, `SkillIcons`, `SetPieceTaker`, `OptionsMenu`, `Bone`, `Striker`, `CustomizeUI`, `GameCamera`, `Celebration`, `SkillTree`, `SteamTransport`, `NetStrikerMatch`, `NetSetPieceMatch`, `INetTransport`, `BallController`, `Footballer`, `.Build`, `.Empty`, `Dribble`, `Crosser`, `Multiplayer`, `.Box`, `.Empty`, `GameCamera`, `MenuBackground`, `.Cylinder`, `QuickChat`, `.SetLocalInput`, `INetTransport`, `.Box`, `Crowd`, `DefensiveWall`, `AccuracyGame`, `com.unity.modules.adaptiveperformance`, `.StartRebind`, `JerseyDesigns.Nations2.cs`, `com.unity.modules.wind`, `OptionsMenu`, `.Set`, `ShotType.cs`, `PauseMenu`, `Crowd`, `MenuUI`, `QuickChat`, `MonoBehaviour`, `Knockdown`, `StadiumStyle`, `Goal`?**
  _High betweenness centrality (0.199) - this node is a cross-community bridge._
- **Why does `NetSession` connect `Net Messages & Wire Codec` to `LobbyInfo`, `NetCodec`, `ChatCensor`, `PlayerPreview`, `Kick Detection / Ragdoll Wiring`, `.SafeEncode`, `.ClientUpdate`, `.ResetTo`, `.Configure`, `GameMode`, `KeeperGame`, `com.unity.modules.jsonserialize`?**
  _High betweenness centrality (0.119) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `PrematchUI` to `Dribble System`, `Input & Keybinds`, `AccuracyGame`, `Kick Detection / Ragdoll Wiring`, `SkillIcons`, `SetPieceTaker`, `SkillTree`, `KeeperGame`, `SteamTransport`, `INetTransport`, `BallController`, `Footballer`, `Footballer`, `.Build`, `.Empty`, `Multiplayer`, `.SafeEncode`, `.ClientUpdate`, `.Box`, `.Empty`, `INetTransport`, `.Box`, `DefensiveWall`, `JerseyDesigns.Nations4.cs`, `MonoBehaviour`, `Knockdown`?**
  _High betweenness centrality (0.117) - this node is a cross-community bridge._
- **What connects `SetPieceSpin`, `Emote`, `Stage` to the rest of the system?**
  _129 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Ball Physics & Launch` be split into smaller, more focused modules?**
  _Cohesion score 0.1021021021021021 - nodes in this community are weakly interconnected._
- **Should `Jersey / Nation Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.11901263590949163 - nodes in this community are weakly interconnected._
- **Should `Dribble System` be split into smaller, more focused modules?**
  _Cohesion score 0.05540499849442939 - nodes in this community are weakly interconnected._