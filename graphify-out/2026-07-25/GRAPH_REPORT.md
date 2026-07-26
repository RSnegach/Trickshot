# Graph Report - Trickshot  (2026-07-25)

## Corpus Check
- 114 files · ~252,683 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2222 nodes · 5364 edges · 131 communities (101 shown, 30 thin omitted)
- Extraction: 88% EXTRACTED · 12% INFERRED · 0% AMBIGUOUS · INFERRED: 650 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `4f90c24f`
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
- .OnGUI
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
- SessionBrowserUI
- Multiplayer
- OptionsMenu
- .ClientUpdate
- PauseMenu
- .Box
- NetEndpoint
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
- QuickChat
- .SkillPresetButtons
- .AdvanceTurn
- AccuracyGame
- Trickshot (3D trick-shot football prototype)
- com.unity.modules.adaptiveperformance
- JerseyDesigns.Nations10.cs
- com.unity.modules.ai
- .ResetTo
- .HandlePacket
- IStrikerInput
- MultiplayerHubUI
- com.unity.modules.wind
- OptionsMenu
- com.unity.modules.androidjni
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- GameMode
- .Set
- StadiumSelectUI
- JerseyDesigns.Nations10.cs
- PauseMenu
- .Poll
- MenuUI
- Role.cs
- Crowd
- QuickChat
- IStrikerInput
- CrosserControl
- MonoBehaviour
- .StartRebind
- Knockdown
- StadiumStyle
- Frame
- .Set
- JerseyDesigns.Nations9.cs
- CrosserBubble
- JerseyDesigns.Nations1.cs
- JerseyDesigns.Nations2.cs
- JerseyDesigns.Nations6.cs
- JerseyDesigns.Nations8.cs
- JerseyDesigns.Nations5.cs
- JerseyDesigns.Nations6.cs
- JerseyDesigns.Nations7.cs
- JerseyDesigns.Nations8.cs
- JerseyDesigns.Nations9.cs
- Material
- Action
- Color32
- Dictionary
- float
- com.unity.modules.terrain
- int
- string
- Texture2D
- Camera
- GameObject
- Refs
- Rigidbody
- RuntimeInitializeOnLoadMethod

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 110 edges
2. `Trickshot` - 97 edges
3. `NetSession` - 84 edges
4. `CustomizeUI` - 78 edges
5. `NetSetPieceMatch` - 71 edges
6. `BallController` - 61 edges
7. `JerseyDesigns` - 58 edges
8. `ScrimmageGame` - 58 edges
9. `Striker` - 56 edges
10. `GameBootstrap` - 54 edges

## Surprising Connections (you probably didn't know these)
- `PlayerInputManager (local multiplayer seam)` --semantically_similar_to--> `Slot / role model (NetSession.MaxSlots=8)`  [INFERRED] [semantically similar]
  README.md → MULTIPLAYER.md
- `Trickshot Multiplayer Framework` --conceptually_related_to--> `Trickshot (3D trick-shot football prototype)`  [INFERRED]
  MULTIPLAYER.md → README.md
- `Trickshot (3D trick-shot football prototype)` --references--> `Unity 6000.4.1f1 editor version`  [EXTRACTED]
  README.md → ProjectSettings/ProjectVersion.txt
- `ScrimmageGame` --shares_data_with--> `ActiveRagdoll.cs`  [INFERRED]
  MULTIPLAYER.md → README.md
- `GameInput` --implements--> `IStrikerInput`  [EXTRACTED]
  Assets/Scripts/Input/GameInput.cs → Assets/Scripts/Input/IStrikerInput.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Hair Atlas License Terms Set** — assets_resources_hair_hairatlas_license_royalty_free_use, assets_resources_hair_hairatlas_license_no_attribution_required, assets_resources_hair_hairatlas_license_no_resale_restriction, assets_resources_hair_hairatlas_license_bundled_license_requirement [EXTRACTED 1.00]
- **Four strand-card tiles compose the hair atlas** — assets_resources_hair_hairatlas_wavy_scattered_strands, assets_resources_hair_hairatlas_flowing_wavy_strands, assets_resources_hair_hairatlas_dense_wavy_strands, assets_resources_hair_hairatlas_straight_sleek_strands, assets_resources_hair_hairatlas_atlas [EXTRACTED 1.00]
- **graphify CLI commands** — claude_graphify_query, claude_graphify_path, claude_graphify_explain, claude_graphify_update [EXTRACTED 0.85]
- **Interchangeable transports behind INetTransport seam** — multiplayer_inettransport, multiplayer_directiptransport, multiplayer_localtransport, multiplayer_steamtransport [EXTRACTED 1.00]
- **Active-ragdoll bicycle-kick mechanic** — readme_activeragdoll, readme_ragdollpose, readme_kickdetector, readme_jointmath, readme_bicycle_kick [INFERRED 0.85]
- **Host-authoritative frame loop (poll, input, snapshot)** — multiplayer_multiplayer, multiplayer_netsession, multiplayer_netmessages, multiplayer_host_authoritative [INFERRED 0.85]

## Communities (131 total, 30 thin omitted)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.06
Nodes (22): Action, bool, Color, Rect, Vector2, BodySub, CustomizeUI, Stage (+14 more)

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.12
Nodes (21): Action, Color32, Dictionary, int, IReadOnlyList, List, string, Texture2D (+13 more)

### Community 2 - "Dribble System"
Cohesion: 0.12
Nodes (7): bool, float, HashSet, int, string, Vector3, ScrimmageGame

### Community 4 - "Input & Keybinds"
Cohesion: 0.06
Nodes (19): Action, int, List, string, ulong, INetTransport, LobbyInfo, NetChannel (+11 more)

### Community 5 - "SkillTree"
Cohesion: 0.05
Nodes (24): Action, bool, byte, Dictionary, float, int, IPEndPoint, List (+16 more)

### Community 6 - "Goalkeeper AI & Control"
Cohesion: 0.07
Nodes (27): bool, byte, Color, float, int, string, Texture2D, PlayerAppearance (+19 more)

### Community 7 - "Skill Icon Drawing"
Cohesion: 0.16
Nodes (7): Color32, Dictionary, float, int, string, Texture2D, SkillIcons

### Community 8 - "AccuracyGame"
Cohesion: 0.13
Nodes (4): JerseyDesigns, Role, Trickshot.Net, Trickshot

### Community 9 - "Direct IP Transport"
Cohesion: 0.21
Nodes (8): bool, float, int, string, uint, Vector3, Body, NetStrikerMatch

### Community 10 - "Kick Detection / Ragdoll Wiring"
Cohesion: 0.10
Nodes (11): bool, float, int, List, Rigidbody, string, uint, Vector3 (+3 more)

### Community 11 - "Net Set-Piece Match"
Cohesion: 0.33
Nodes (5): int, string, AdultQuiz, Q, Q

### Community 12 - "SkillIcons"
Cohesion: 0.25
Nodes (5): Action, bool, Texture2D, GameMode, MenuUI

### Community 13 - "SetPieceTaker"
Cohesion: 0.19
Nodes (9): Collider, Color, float, GameObject, int, Material, Transform, Vector3 (+1 more)

### Community 14 - "OptionsMenu"
Cohesion: 0.14
Nodes (11): Action, bool, Collider, Color, float, int, Material, Transform (+3 more)

### Community 15 - "PrematchUI"
Cohesion: 0.10
Nodes (14): bool, Collider, ConfigurableJoint, float, int, IReadOnlyList, List, Material (+6 more)

### Community 16 - "Bone"
Cohesion: 0.06
Nodes (26): Dictionary, string, ChatCensor, Color, float, Rect, Vector2, Vector3 (+18 more)

### Community 17 - ".Box"
Cohesion: 0.20
Nodes (10): float, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion, Transform (+2 more)

### Community 18 - "CustomizeUI"
Cohesion: 0.07
Nodes (20): Action, bool, Delivery, float, int, ScrimRole, string, Vector3 (+12 more)

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 20 - ".OnGUI"
Cohesion: 0.15
Nodes (8): NetPump, Action, bool, float, int, List, string, SessionBrowserUI

### Community 21 - "GameInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "Celebration"
Cohesion: 0.06
Nodes (34): AccessoryEntry, bool, float, int, Material, Matrix4x4, Mesh, Transform (+26 more)

### Community 23 - "SkillTree"
Cohesion: 0.11
Nodes (9): bool, float, int, string, Vector3, FreeKickGame, Outcome, Phase (+1 more)

### Community 24 - "KeeperGame"
Cohesion: 0.13
Nodes (8): IPlayerController, bool, float, Func, Quaternion, Vector3, KeeperController, State

### Community 25 - "SteamTransport"
Cohesion: 0.13
Nodes (13): bool, Camera, float, int, Material, Refs, Rigidbody, string (+5 more)

### Community 26 - "NetStrikerMatch"
Cohesion: 0.15
Nodes (11): bool, Color, float, int, Material, Transform, Vector3, Crowd (+3 more)

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.24
Nodes (8): Color, GameObject, Material, Transform, Vector3, Make, ColliderKind, Shader

### Community 28 - "com.unity.modules.jsonserialize"
Cohesion: 0.32
Nodes (9): Color, float, int, Material, PhysicsMaterial, Transform, Vector3, StadiumBuilder (+1 more)

### Community 29 - "INetTransport"
Cohesion: 0.32
Nodes (6): Color, float, Rect, Vector2, Vector3, SetPieceMap

### Community 30 - "BallController"
Cohesion: 0.18
Nodes (7): float, Quaternion, Vector3, Goalkeeper, State, Vector3, State

### Community 31 - "StadiumBuilder"
Cohesion: 0.12
Nodes (10): Camera, Color, float, int, Light, List, Material, Quaternion (+2 more)

### Community 32 - "Footballer"
Cohesion: 0.12
Nodes (11): bool, Camera, float, GameObject, Light, Material, Quaternion, Rect (+3 more)

### Community 33 - ".Build"
Cohesion: 0.15
Nodes (6): byte, Vector2, GameInput, InputActionAsset, InputActionMap, PlayerInput

### Community 34 - "PitchBuilder"
Cohesion: 0.34
Nodes (5): Material, Transform, uint, Vector3, SurroundBuilder

### Community 35 - ".Empty"
Cohesion: 0.09
Nodes (15): bool, Collision, float, Rigidbody, Vector3, BallController, SetPieceSpin, Action (+7 more)

### Community 36 - "NetCodec"
Cohesion: 0.25
Nodes (4): Color, MsgType, NetReader, BinaryReader

### Community 37 - "CrossMap"
Cohesion: 0.32
Nodes (6): float, int, Material, Transform, Vector3, PitchBuilder

### Community 38 - "PlayerPreview"
Cohesion: 0.13
Nodes (9): bool, float, int, string, KeeperGame, float, int, Vector3 (+1 more)

### Community 39 - "DefensiveWall"
Cohesion: 0.20
Nodes (10): depth, source, version, dependencies, depth, source, version, com.unity.modules.uielements (+2 more)

### Community 40 - ".PushRoster"
Cohesion: 0.29
Nodes (7): dependencies, depth, source, version, dependencies, com.unity.modules.physics, com.unity.modules.physics

### Community 41 - "FlexNet"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 42 - "SessionBrowserUI"
Cohesion: 0.36
Nodes (6): float, PhysicsMaterial, Transform, Vector3, Refs, ScrimmageArena

### Community 43 - "Multiplayer"
Cohesion: 0.10
Nodes (14): Action, MultiplayerHubUI, NetBackstop, bool, float, int, List, Quaternion (+6 more)

### Community 44 - "OptionsMenu"
Cohesion: 0.18
Nodes (6): bool, float, int, List, Vector3, Footballer

### Community 45 - ".ClientUpdate"
Cohesion: 0.14
Nodes (8): bool, Camera, float, Func, Transform, Vector3, GameCamera, Mode

### Community 46 - "PauseMenu"
Cohesion: 0.17
Nodes (8): Action, bool, float, Vector3, Celebration, Emote, EmotePose, Emote

### Community 47 - ".Box"
Cohesion: 0.12
Nodes (9): Rigidbody, bool, float, Vector3, Striker, Trick, Rigidbody, Bone (+1 more)

### Community 48 - "NetEndpoint"
Cohesion: 0.19
Nodes (9): PhysicsMaterial, Material, PhysicsMaterial, Transform, Vector3, Arena, Refs, PhysicsMaterial (+1 more)

### Community 49 - ".Configure"
Cohesion: 0.22
Nodes (17): bool, Vector2, NetInputSource, bool, byte, float, string, uint (+9 more)

### Community 50 - "GameCamera"
Cohesion: 0.20
Nodes (5): bool, float, Transform, Vector3, Crosser

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): com.unity.modules.hierarchycore, dependencies, depth, source, version, com.unity.modules.hierarchycore

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.16
Nodes (6): PlayerAppearance, Vector3, NetCodec, NetWriter, BinaryWriter, MemoryStream

### Community 53 - "MenuBackground"
Cohesion: 0.12
Nodes (14): bool, Camera, float, Func, int, List, Material, Mesh (+6 more)

### Community 54 - "Dribble"
Cohesion: 0.18
Nodes (8): bool, float, int, Quaternion, Vector3, PitchLayout, Seat, Side

### Community 55 - "QuickChat"
Cohesion: 0.21
Nodes (4): bool, float, Vector3, Dribble

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
Nodes (7): bool, float, int, string, Transform, Vector3, GameManager

### Community 60 - "INetTransport"
Cohesion: 0.13
Nodes (8): bool, float, int, string, Vector3, AccuracyGame, Phase, Phase

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
Cohesion: 0.16
Nodes (6): float, int, Transform, uint, Vector3, AccuracyBoard

### Community 69 - "AimReticle"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 71 - ".SkillPresetButtons"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.umbra

### Community 73 - "AccuracyGame"
Cohesion: 0.24
Nodes (7): Action, bool, float, Transform, Vector3, Sniper, LineRenderer

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "com.unity.modules.adaptiveperformance"
Cohesion: 0.09
Nodes (13): bool, Dictionary, float, IEnumerator, int, RuntimeInitializeOnLoadMethod, string, Vector3 (+5 more)

### Community 76 - "JerseyDesigns.Nations10.cs"
Cohesion: 0.07
Nodes (16): NetRole, bool, byte, Dictionary, float, HashSet, int, PlayerAppearance (+8 more)

### Community 77 - "com.unity.modules.ai"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 78 - ".ResetTo"
Cohesion: 0.25
Nodes (4): Action, bool, string, LobbyUI

### Community 79 - ".HandlePacket"
Cohesion: 0.18
Nodes (6): bool, List, RuntimeInitializeOnLoadMethod, Multiplayer, NetPumpRunner, NetPumpRunner

### Community 80 - "IStrikerInput"
Cohesion: 0.21
Nodes (5): Vector2, IStrikerInput, float, Func, CrosserControl

### Community 81 - "MultiplayerHubUI"
Cohesion: 0.19
Nodes (8): Action, int, RebindingOperation, string, Vector2, OptionsMenu, Tab, Tab

### Community 82 - "com.unity.modules.wind"
Cohesion: 0.33
Nodes (5): ConfigurableJoint, Quaternion, Rigidbody, JointMath, Space

### Community 83 - "OptionsMenu"
Cohesion: 0.18
Nodes (4): Dictionary, string, Keybinds, InputAction

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
Cohesion: 0.10
Nodes (23): ActiveRagdoll, AimReticle, bool, GameBootstrap, BallController, Camera, Crosser, Crowd (+15 more)

### Community 88 - ".Set"
Cohesion: 0.38
Nodes (4): Vector3, RagdollPose, bone, euler

### Community 90 - "JerseyDesigns.Nations10.cs"
Cohesion: 0.20
Nodes (7): Action, bool, int, Rect, string, Vector3, HostSetupUI

### Community 91 - "PauseMenu"
Cohesion: 0.25
Nodes (4): Action, bool, float, PauseMenu

### Community 94 - "MenuUI"
Cohesion: 0.29
Nodes (4): Action, bool, Collider, Goal

### Community 95 - "Role.cs"
Cohesion: 0.18
Nodes (6): bool, float, Func, Vector3, SetPieceTaker, State

### Community 96 - "Crowd"
Cohesion: 0.38
Nodes (4): Vector3, KeeperPose, b, e

### Community 97 - "QuickChat"
Cohesion: 0.29
Nodes (3): int, string, QuickChat

### Community 98 - "IStrikerInput"
Cohesion: 0.13
Nodes (8): Texture2D, Camera, Material, Transform, Camera, Material, Transform, Texture2D

### Community 100 - "MonoBehaviour"
Cohesion: 0.15
Nodes (7): float, Matrix4x4, Rect, Vector2, MenuScale, Action, StadiumSelectUI

### Community 102 - "Knockdown"
Cohesion: 0.29
Nodes (4): List, Refs, ScrimRole, Func

### Community 103 - "StadiumStyle"
Cohesion: 0.32
Nodes (7): bool, Color, float, int, string, StadiumStyle, Surroundings

### Community 105 - ".Set"
Cohesion: 0.21
Nodes (5): float, Material, Transform, Vector3, AimReticle

### Community 106 - "JerseyDesigns.Nations9.cs"
Cohesion: 0.28
Nodes (3): bool, float, Knockdown

### Community 107 - "CrosserBubble"
Cohesion: 0.29
Nodes (4): Collider, float, Transform, CrosserBubble

### Community 109 - "JerseyDesigns.Nations2.cs"
Cohesion: 0.33
Nodes (4): Camera, Material, Transform, NetAccuracyMatch

## Knowledge Gaps
- **130 isolated node(s):** `Stage`, `BodySub`, `Phase`, `SetPieceSpin`, `Emote` (+125 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **30 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `Ball Physics & Launch`, `Jersey / Nation Designs`, `Dribble System`, `Goalkeeper AI & Control`, `Net Set-Piece Match`, `SkillIcons`, `SetPieceTaker`, `OptionsMenu`, `Bone`, `.Box`, `CustomizeUI`, `Celebration`, `SkillTree`, `KeeperGame`, `NetStrikerMatch`, `NetSetPieceMatch`, `INetTransport`, `BallController`, `StadiumBuilder`, `Footballer`, `.Build`, `.Empty`, `CrossMap`, `PlayerPreview`, `SessionBrowserUI`, `Multiplayer`, `.ClientUpdate`, `PauseMenu`, `.Box`, `NetEndpoint`, `GameCamera`, `MenuBackground`, `Dribble`, `QuickChat`, `.SetLocalInput`, `INetTransport`, `.Box`, `Crowd`, `ChatCensor`, `AccuracyGame`, `com.unity.modules.adaptiveperformance`, `IStrikerInput`, `MultiplayerHubUI`, `com.unity.modules.wind`, `OptionsMenu`, `.Set`, `PauseMenu`, `.Poll`, `MenuUI`, `Role.cs`, `Crowd`, `QuickChat`, `MonoBehaviour`, `.StartRebind`, `StadiumStyle`, `.Set`, `JerseyDesigns.Nations9.cs`, `CrosserBubble`, `JerseyDesigns.Nations2.cs`, `JerseyDesigns.Nations6.cs`, `JerseyDesigns.Nations8.cs`, `JerseyDesigns.Nations5.cs`, `JerseyDesigns.Nations6.cs`, `JerseyDesigns.Nations7.cs`, `JerseyDesigns.Nations8.cs`, `JerseyDesigns.Nations9.cs`?**
  _High betweenness centrality (0.226) - this node is a cross-community bridge._
- **Why does `NetSession` connect `JerseyDesigns.Nations10.cs` to `IStrikerInput`, `Net Messages & Wire Codec`, `Input & Keybinds`, `NetCodec`, `Direct IP Transport`, `Kick Detection / Ragdoll Wiring`, `JerseyDesigns.Nations1.cs`, `.ResetTo`, `.HandlePacket`, `Bone`, `.Configure`, `SteamTransport`, `.BeginTurn`?**
  _High betweenness centrality (0.145) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `PrematchUI` to `Dribble System`, `AccuracyGame`, `Direct IP Transport`, `Kick Detection / Ragdoll Wiring`, `SkillTree`, `KeeperGame`, `SteamTransport`, `NetSetPieceMatch`, `BallController`, `StadiumBuilder`, `Footballer`, `.Empty`, `PlayerPreview`, `Multiplayer`, `OptionsMenu`, `PauseMenu`, `.Box`, `GameCamera`, `QuickChat`, `.SetLocalInput`, `INetTransport`, `.Box`, `Crowd`, `IStrikerInput`, `StadiumSelectUI`, `Role.cs`, `IStrikerInput`, `Knockdown`, `JerseyDesigns.Nations9.cs`, `CrosserBubble`?**
  _High betweenness centrality (0.092) - this node is a cross-community bridge._
- **What connects `Stage`, `BodySub`, `Phase` to the rest of the system?**
  _130 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Ball Physics & Launch` be split into smaller, more focused modules?**
  _Cohesion score 0.05917721518987342 - nodes in this community are weakly interconnected._
- **Should `Jersey / Nation Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.11901263590949163 - nodes in this community are weakly interconnected._
- **Should `Dribble System` be split into smaller, more focused modules?**
  _Cohesion score 0.11942959001782531 - nodes in this community are weakly interconnected._