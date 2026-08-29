# Graph Report - Trickshot  (2026-08-18)

## Corpus Check
- 119 files · ~292,109 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2355 nodes · 5937 edges · 112 communities (104 shown, 8 thin omitted)
- Extraction: 88% EXTRACTED · 12% INFERRED · 0% AMBIGUOUS · INFERRED: 728 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `c9d222b6`
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
- LobbyUI
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
- Footballer
- SteamTransport
- NetStrikerMatch
- NetSetPieceMatch
- com.unity.modules.jsonserialize
- INetTransport
- .ClientUpdate
- Dribble
- Footballer
- .Configure
- PitchBuilder
- .Empty
- NetCodec
- .Configure
- PlayerPreview
- DefensiveWall
- .PushRoster
- FlexNet
- SessionBrowserUI
- Footballer
- .AdvanceTurn
- .ClientUpdate
- .Empty
- .Box
- Goalkeeper
- ShotServer
- .SetLocalInput
- com.unity.modules.physics
- com.unity.modules.imageconversion
- .Build
- GameInput
- QuickChat
- ShotServer
- Sniper
- IStrikerInput
- .Configure
- IStrikerInput
- .Box
- LobbySlot
- FreeplayGame
- com.unity.modules.ai
- com.unity.modules.imgui
- com.unity.modules.ui
- Crowd
- .ResetTo
- AimReticle
- QuickChat
- .SkillPresetButtons
- .AdvanceTurn
- .PhysMat
- Trickshot (3D trick-shot football prototype)
- com.unity.modules.adaptiveperformance
- PeerId
- com.unity.modules.ai
- HostSetupUI
- PitchLayout
- JerseyDesigns.Nations8.cs
- Multiplayer
- com.unity.modules.wind
- OptionsMenu
- com.unity.modules.androidjni
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- OptionsMenu
- .Set
- Crosser
- MenuUI
- PauseMenu
- Goal
- .Poll
- NetPump
- Role.cs
- .StartRebind
- .Begin
- Dribble
- HostSetupUI
- SessionBrowserUI
- StatRadar
- SimConfig
- StadiumStyle
- BoneSpec
- JerseyDesigns.Nations3.cs
- ChatCensor
- .StartRebind
- JerseyDesigns.Nations9.cs
- CrosserBubble
- .NavButtons
- com.unity.modules.terrain

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 128 edges
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

## Communities (112 total, 8 thin omitted)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.08
Nodes (14): bool, Color, Color32, Dictionary, float, IEnumerator, int, Rect (+6 more)

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.12
Nodes (21): Action, Color32, Dictionary, int, IReadOnlyList, List, string, Texture2D (+13 more)

### Community 2 - "Dribble System"
Cohesion: 0.06
Nodes (21): bool, float, Vector3, Dribble, bool, float, int, List (+13 more)

### Community 3 - "Net Messages & Wire Codec"
Cohesion: 0.14
Nodes (5): GameMode, bool, GameObject, RuntimeInitializeOnLoadMethod, GameBootstrap

### Community 4 - "Input & Keybinds"
Cohesion: 0.12
Nodes (10): Func, bool, float, int, string, KeeperGame, float, int (+2 more)

### Community 5 - "SkillTree"
Cohesion: 0.10
Nodes (12): bool, byte, ConcurrentQueue, Dictionary, float, Func, int, IPEndPoint (+4 more)

### Community 6 - "Goalkeeper AI & Control"
Cohesion: 0.10
Nodes (16): Dictionary, float, HashSet, IEnumerable, int, List, string, Category (+8 more)

### Community 7 - "Skill Icon Drawing"
Cohesion: 0.16
Nodes (7): Color32, Dictionary, float, int, string, Texture2D, SkillIcons

### Community 8 - "AccuracyGame"
Cohesion: 0.06
Nodes (14): JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns (+6 more)

### Community 9 - "Direct IP Transport"
Cohesion: 0.25
Nodes (4): Action, bool, float, PauseMenu

### Community 10 - "LobbyUI"
Cohesion: 0.30
Nodes (9): Color, float, int, Material, PhysicsMaterial, Transform, Vector3, StadiumBuilder (+1 more)

### Community 11 - "Net Set-Piece Match"
Cohesion: 0.33
Nodes (5): int, string, AdultQuiz, Q, Q

### Community 12 - "SkillIcons"
Cohesion: 0.25
Nodes (8): Color, GameObject, Material, Transform, Vector3, JerseyFaces, Make, Shader

### Community 13 - "SetPieceTaker"
Cohesion: 0.34
Nodes (5): Material, Transform, uint, Vector3, SurroundBuilder

### Community 14 - "OptionsMenu"
Cohesion: 0.08
Nodes (17): float, int, Transform, uint, Vector3, AccuracyBoard, Action, bool (+9 more)

### Community 15 - "PrematchUI"
Cohesion: 0.24
Nodes (7): Action, bool, float, Transform, Vector3, Sniper, LineRenderer

### Community 16 - "Bone"
Cohesion: 0.09
Nodes (16): Color, float, Rect, Vector2, Vector3, CrossMap, Delivery, bool (+8 more)

### Community 17 - ".Box"
Cohesion: 0.14
Nodes (9): List, Action, bool, float, int, List, string, ulong (+1 more)

### Community 18 - "CustomizeUI"
Cohesion: 0.14
Nodes (8): Action, bool, Delivery, float, int, string, Vector3, PrematchUI

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 20 - ".OnGUI"
Cohesion: 0.16
Nodes (11): bool, Color, float, int, Material, Transform, Vector3, Crowd (+3 more)

### Community 21 - "GameInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "Celebration"
Cohesion: 0.06
Nodes (34): AccessoryEntry, bool, float, int, Material, Matrix4x4, Mesh, Transform (+26 more)

### Community 23 - "SkillTree"
Cohesion: 0.14
Nodes (10): bool, float, int, Random, string, Vector3, FreeKickGame, Outcome (+2 more)

### Community 24 - "Footballer"
Cohesion: 0.32
Nodes (6): float, int, Material, Transform, Vector3, PitchBuilder

### Community 25 - "SteamTransport"
Cohesion: 0.12
Nodes (12): bool, Camera, float, int, Material, Refs, string, Transform (+4 more)

### Community 26 - "NetStrikerMatch"
Cohesion: 0.20
Nodes (4): SlotKind, Color, string, SpeciesCosmetics

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.10
Nodes (10): bool, float, int, List, string, uint, Vector3, Body (+2 more)

### Community 28 - "com.unity.modules.jsonserialize"
Cohesion: 0.12
Nodes (9): Camera, Material, Transform, Camera, Material, Transform, Camera, Material (+1 more)

### Community 29 - "INetTransport"
Cohesion: 0.12
Nodes (14): bool, Camera, float, Func, int, List, Material, Mesh (+6 more)

### Community 30 - ".ClientUpdate"
Cohesion: 0.21
Nodes (8): bool, float, int, string, uint, Vector3, Body, NetStrikerMatch

### Community 31 - "Dribble"
Cohesion: 0.08
Nodes (14): float, KickDetector, Rigidbody, Rigidbody, Rigidbody, bool, float, Func (+6 more)

### Community 32 - "Footballer"
Cohesion: 0.15
Nodes (11): bool, byte, float, string, BodyPlan, HeaderAction, Species, SpeciesAxis (+3 more)

### Community 34 - "PitchBuilder"
Cohesion: 0.11
Nodes (13): byte, Dictionary, float, List, uint, Pending, ReliableChannel, ConfigurableJoint (+5 more)

### Community 35 - ".Empty"
Cohesion: 0.19
Nodes (9): PhysicsMaterial, Material, PhysicsMaterial, Transform, Vector3, Arena, Refs, PhysicsMaterial (+1 more)

### Community 36 - "NetCodec"
Cohesion: 0.09
Nodes (8): IPlayerController, bool, float, Quaternion, Vector3, KeeperController, State, Vector3

### Community 37 - ".Configure"
Cohesion: 0.35
Nodes (3): Camera, Refs, Transform

### Community 38 - "PlayerPreview"
Cohesion: 0.18
Nodes (6): int, IPAddress, IPEndPoint, List, string, NetEndpoint

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
Cohesion: 0.15
Nodes (6): bool, RuntimeInitializeOnLoadMethod, Multiplayer, NetPumpRunner, NetPump, NetPumpRunner

### Community 43 - "Footballer"
Cohesion: 0.22
Nodes (17): bool, Vector2, NetInputSource, bool, byte, float, string, uint (+9 more)

### Community 44 - ".AdvanceTurn"
Cohesion: 0.14
Nodes (14): ScrimRole, float, PhysicsMaterial, Transform, Vector3, Refs, ScrimmageArena, bool (+6 more)

### Community 45 - ".ClientUpdate"
Cohesion: 0.24
Nodes (6): byte, Vector2, GameInput, InputActionAsset, InputActionMap, PlayerInput

### Community 46 - ".Empty"
Cohesion: 0.19
Nodes (10): float, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion, Transform (+2 more)

### Community 47 - ".Box"
Cohesion: 0.19
Nodes (8): Action, bool, float, Vector3, Celebration, Emote, EmotePose, Emote

### Community 48 - "Goalkeeper"
Cohesion: 0.15
Nodes (10): bool, float, int, List, Quaternion, Rigidbody, Transform, Vector3 (+2 more)

### Community 49 - "ShotServer"
Cohesion: 0.15
Nodes (7): float, int, List, Queue, string, Line, QuickChatFeed

### Community 50 - ".SetLocalInput"
Cohesion: 0.10
Nodes (13): bool, Collision, float, Rigidbody, Vector3, BallController, SetPieceSpin, Collision (+5 more)

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): com.unity.modules.hierarchycore, dependencies, depth, source, version, com.unity.modules.hierarchycore

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.16
Nodes (6): PlayerAppearance, Vector3, NetCodec, NetWriter, BinaryWriter, MemoryStream

### Community 53 - ".Build"
Cohesion: 0.14
Nodes (5): Vector2, IStrikerInput, float, Func, CrosserControl

### Community 54 - "GameInput"
Cohesion: 0.22
Nodes (7): bool, float, int, string, Transform, Vector3, FreeplayGame

### Community 55 - "QuickChat"
Cohesion: 0.22
Nodes (7): bool, float, int, string, Transform, Vector3, TimeTrialGame

### Community 56 - "ShotServer"
Cohesion: 0.07
Nodes (29): com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.animation, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+21 more)

### Community 57 - "Sniper"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.androidjni

### Community 58 - "IStrikerInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, url, version, com.unity.inputsystem

### Community 59 - ".Configure"
Cohesion: 0.08
Nodes (17): float, Material, Transform, Vector3, AimReticle, bool, float, Transform (+9 more)

### Community 61 - ".Box"
Cohesion: 0.38
Nodes (4): Vector3, KeeperPose, b, e

### Community 62 - "LobbySlot"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.imgui, com.unity.modules.imgui

### Community 63 - "FreeplayGame"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.animation

### Community 64 - "com.unity.modules.ai"
Cohesion: 0.29
Nodes (6): dependencies, depth, source, version, dependencies, com.unity.modules.audio

### Community 65 - "com.unity.modules.imgui"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.particlesystem

### Community 66 - "com.unity.modules.ui"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.ui, com.unity.modules.ui

### Community 67 - "Crowd"
Cohesion: 0.29
Nodes (6): Color, float, Rect, Vector2, Vector3, SetPieceMap

### Community 68 - ".ResetTo"
Cohesion: 0.20
Nodes (7): bool, byte, int, string, uint, LobbyAdvert, LobbyProbe

### Community 69 - "AimReticle"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - "QuickChat"
Cohesion: 0.25
Nodes (4): Color, MsgType, NetReader, BinaryReader

### Community 71 - ".SkillPresetButtons"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.umbra

### Community 72 - ".AdvanceTurn"
Cohesion: 0.24
Nodes (9): Action, ConcurrentQueue, float, IPAddress, IPEndPoint, List, Reason, TailnetDiscovery (+1 more)

### Community 73 - ".PhysMat"
Cohesion: 0.19
Nodes (9): Collider, Color, float, GameObject, int, Material, Transform, Vector3 (+1 more)

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "com.unity.modules.adaptiveperformance"
Cohesion: 0.09
Nodes (13): bool, Dictionary, float, IEnumerator, int, RuntimeInitializeOnLoadMethod, string, Vector3 (+5 more)

### Community 76 - "PeerId"
Cohesion: 0.38
Nodes (4): Vector3, RagdollPose, bone, euler

### Community 77 - "com.unity.modules.ai"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 78 - "HostSetupUI"
Cohesion: 0.14
Nodes (8): bool, float, int, string, Vector3, AccuracyGame, Phase, Phase

### Community 79 - "PitchLayout"
Cohesion: 0.05
Nodes (24): Action, List, Action, Func, int, List, string, ulong (+16 more)

### Community 80 - "JerseyDesigns.Nations8.cs"
Cohesion: 0.14
Nodes (6): float, Quaternion, Vector3, Goalkeeper, State, Mode

### Community 81 - "Multiplayer"
Cohesion: 0.18
Nodes (8): bool, float, int, Quaternion, Vector3, PitchLayout, Seat, Side

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

### Community 87 - "OptionsMenu"
Cohesion: 0.19
Nodes (8): Action, int, RebindingOperation, string, Vector2, OptionsMenu, Tab, Tab

### Community 88 - ".Set"
Cohesion: 0.19
Nodes (12): Func, bool, byte, Color, float, int, string, Texture2D (+4 more)

### Community 90 - "MenuUI"
Cohesion: 0.12
Nodes (10): Camera, Color, float, int, Light, List, Material, Quaternion (+2 more)

### Community 91 - "PauseMenu"
Cohesion: 0.17
Nodes (6): Action, bool, Color, float, string, LobbyUI

### Community 92 - "Goal"
Cohesion: 0.29
Nodes (4): Action, bool, Collider, Goal

### Community 93 - ".Poll"
Cohesion: 0.29
Nodes (3): int, string, QuickChat

### Community 94 - "NetPump"
Cohesion: 0.09
Nodes (15): bool, byte, Collider, ConfigurableJoint, Dictionary, float, IReadOnlyList, List (+7 more)

### Community 95 - "Role.cs"
Cohesion: 0.17
Nodes (7): bool, float, Func, Vector3, SetPieceTaker, State, State

### Community 96 - ".StartRebind"
Cohesion: 0.32
Nodes (3): bool, float, Knockdown

### Community 97 - ".Begin"
Cohesion: 0.26
Nodes (3): float, Matrix4x4, MenuScale

### Community 99 - "HostSetupUI"
Cohesion: 0.22
Nodes (6): Action, bool, int, string, Vector3, HostSetupUI

### Community 100 - "SessionBrowserUI"
Cohesion: 0.16
Nodes (8): bool, Camera, float, Func, Transform, Vector3, GameCamera, Mode

### Community 101 - "StatRadar"
Cohesion: 0.32
Nodes (4): Color, Rect, Vector2, StatRadar

### Community 103 - "StadiumStyle"
Cohesion: 0.32
Nodes (7): bool, Color, float, int, string, StadiumStyle, Surroundings

### Community 104 - "BoneSpec"
Cohesion: 0.18
Nodes (13): bool, float, int, string, Vector3, BodyLayout, BodyLayoutDef, BoneSpec (+5 more)

### Community 106 - "JerseyDesigns.Nations3.cs"
Cohesion: 0.27
Nodes (4): Action, bool, Texture2D, MenuUI

### Community 107 - "ChatCensor"
Cohesion: 0.38
Nodes (3): Dictionary, string, ChatCensor

### Community 116 - "JerseyDesigns.Nations9.cs"
Cohesion: 0.07
Nodes (16): NetRole, bool, byte, Dictionary, float, HashSet, int, PlayerAppearance (+8 more)

### Community 117 - "CrosserBubble"
Cohesion: 0.17
Nodes (7): Collider, float, Transform, CrosserBubble, NetAccuracyMatch, NetBackstop, MonoBehaviour

### Community 121 - ".NavButtons"
Cohesion: 0.06
Nodes (20): Action, Rect, Vector2, bool, Camera, float, GameObject, Light (+12 more)

## Knowledge Gaps
- **130 isolated node(s):** `Reason`, `Phase`, `SetPieceSpin`, `Emote`, `Stage` (+125 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **8 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `Jersey / Nation Designs`, `Dribble System`, `Input & Keybinds`, `Goalkeeper AI & Control`, `Direct IP Transport`, `LobbyUI`, `Net Set-Piece Match`, `SkillIcons`, `OptionsMenu`, `PrematchUI`, `Bone`, `CustomizeUI`, `.OnGUI`, `Celebration`, `SkillTree`, `Footballer`, `NetStrikerMatch`, `INetTransport`, `Dribble`, `Footballer`, `PitchBuilder`, `.Empty`, `NetCodec`, `.AdvanceTurn`, `.ClientUpdate`, `.Empty`, `.Box`, `Goalkeeper`, `.Build`, `GameInput`, `QuickChat`, `.Configure`, `.Box`, `Crowd`, `.PhysMat`, `com.unity.modules.adaptiveperformance`, `PeerId`, `HostSetupUI`, `JerseyDesigns.Nations8.cs`, `Multiplayer`, `OptionsMenu`, `OptionsMenu`, `.Set`, `MenuUI`, `Goal`, `.Poll`, `Role.cs`, `.StartRebind`, `.Begin`, `Dribble`, `SessionBrowserUI`, `StatRadar`, `StadiumStyle`, `BoneSpec`, `JerseyDesigns.Nations3.cs`, `ChatCensor`, `.StartRebind`, `CrosserBubble`, `.NavButtons`?**
  _High betweenness centrality (0.173) - this node is a cross-community bridge._
- **Why does `NetSession` connect `JerseyDesigns.Nations9.cs` to `.Configure`, `QuickChat`, `NetSetPieceMatch`, `SessionBrowserUI`, `Footballer`, `PitchLayout`, `ShotServer`, `com.unity.modules.wind`, `.ClientUpdate`, `Crosser`, `PauseMenu`, `com.unity.modules.jsonserialize`, `SteamTransport`?**
  _High betweenness centrality (0.131) - this node is a cross-community bridge._
- **Why does `CustomizeUI` connect `Ball Physics & Launch` to `.Begin`, `Jersey / Nation Designs`, `Goalkeeper AI & Control`, `AccuracyGame`, `CrosserBubble`, `.Set`, `.NavButtons`, `NetStrikerMatch`?**
  _High betweenness centrality (0.128) - this node is a cross-community bridge._
- **What connects `Reason`, `Phase`, `SetPieceSpin` to the rest of the system?**
  _130 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Ball Physics & Launch` be split into smaller, more focused modules?**
  _Cohesion score 0.07890122735242548 - nodes in this community are weakly interconnected._
- **Should `Jersey / Nation Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.11901263590949163 - nodes in this community are weakly interconnected._
- **Should `Dribble System` be split into smaller, more focused modules?**
  _Cohesion score 0.05737234652897304 - nodes in this community are weakly interconnected._