# Graph Report - Trickshot  (2026-08-19)

## Corpus Check
- 122 files · ~302,526 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2421 nodes · 6117 edges · 123 communities (108 shown, 15 thin omitted)
- Extraction: 88% EXTRACTED · 12% INFERRED · 0% AMBIGUOUS · INFERRED: 758 edges (avg confidence: 0.8)
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
- Crowd
- .ResetTo
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
- CrosserBubble
- .ResetTo
- AimReticle
- QuickChat
- .SkillPresetButtons
- .AdvanceTurn
- .PhysMat
- Trickshot (3D trick-shot football prototype)
- com.unity.modules.adaptiveperformance
- Snapshot
- com.unity.modules.ai
- HostSetupUI
- PitchLayout
- JerseyDesigns.Nations8.cs
- .PhysMat
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
- .ToScreen
- StadiumStyle
- BoneSpec
- .SetMode
- JerseyDesigns.Nations3.cs
- ChatCensor
- StatRadar
- .DriveTowardRotation
- PitchLayout
- .ResetTo
- AimReticle
- .Set
- Bone
- JerseyDesigns.Nations9.cs
- KickDetector
- .NavButtons
- com.unity.modules.terrain
- JerseyDesigns.Nations10.cs
- JerseyDesigns.Nations3.cs
- JerseyDesigns.Nations7.cs
- JerseyDesigns.Nations9.cs

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 137 edges
2. `Trickshot` - 104 edges
3. `NetSession` - 87 edges
4. `BallController` - 82 edges
5. `CustomizeUI` - 78 edges
6. `NetSetPieceMatch` - 74 edges
7. `ScrimmageGame` - 59 edges
8. `Striker` - 59 edges
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

## Communities (123 total, 15 thin omitted)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.07
Nodes (17): Action, bool, Color32, Dictionary, float, Func, IEnumerator, int (+9 more)

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.12
Nodes (21): Action, Color32, Dictionary, int, IReadOnlyList, List, string, Texture2D (+13 more)

### Community 2 - "Dribble System"
Cohesion: 0.05
Nodes (20): bool, float, Vector3, Dribble, bool, float, int, List (+12 more)

### Community 3 - "Net Messages & Wire Codec"
Cohesion: 0.13
Nodes (8): bool, Camera, GameObject, Material, Refs, RuntimeInitializeOnLoadMethod, Transform, GameBootstrap

### Community 4 - "Input & Keybinds"
Cohesion: 0.12
Nodes (8): bool, float, int, string, Vector3, AccuracyGame, Phase, Phase

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
Cohesion: 0.10
Nodes (5): JerseyDesigns, JerseyDesigns, Role, Trickshot.Net, Trickshot

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
Cohesion: 0.29
Nodes (4): float, int, Vector3, ShotServer

### Community 13 - "SetPieceTaker"
Cohesion: 0.34
Nodes (5): Material, Transform, uint, Vector3, SurroundBuilder

### Community 14 - "OptionsMenu"
Cohesion: 0.16
Nodes (6): float, int, Transform, uint, Vector3, AccuracyBoard

### Community 15 - "PrematchUI"
Cohesion: 0.24
Nodes (7): Action, bool, float, Transform, Vector3, Sniper, LineRenderer

### Community 16 - "Bone"
Cohesion: 0.08
Nodes (22): Color, float, Rect, Vector2, Vector3, CrossMap, bool, Color (+14 more)

### Community 17 - ".Box"
Cohesion: 0.20
Nodes (8): Action, List, int, string, ulong, LobbyInfo, Action, List

### Community 18 - "CustomizeUI"
Cohesion: 0.29
Nodes (6): Color, float, Rect, Vector2, Vector3, SetPieceMap

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 20 - ".OnGUI"
Cohesion: 0.28
Nodes (6): bool, float, Vector3, Gait, Profile, Profile

### Community 21 - "GameInput"
Cohesion: 0.29
Nodes (7): dependencies, depth, source, version, dependencies, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "Celebration"
Cohesion: 0.06
Nodes (34): AccessoryEntry, bool, float, int, Material, Matrix4x4, Mesh, Transform (+26 more)

### Community 23 - "SkillTree"
Cohesion: 0.13
Nodes (10): bool, float, int, Random, string, Vector3, FreeKickGame, Outcome (+2 more)

### Community 24 - "Footballer"
Cohesion: 0.12
Nodes (9): Action, bool, string, BuildAll, Plat, BuildTarget, MenuItem, Plat (+1 more)

### Community 25 - "SteamTransport"
Cohesion: 0.09
Nodes (10): InputFrame, AnimState, bool, float, int, string, uint, Vector3 (+2 more)

### Community 26 - "NetStrikerMatch"
Cohesion: 0.19
Nodes (5): Color, SlotKind, Color, string, SpeciesCosmetics

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.09
Nodes (12): bool, float, int, List, string, Transform, uint, Vector3 (+4 more)

### Community 28 - "com.unity.modules.jsonserialize"
Cohesion: 0.14
Nodes (8): Action, bool, Delivery, float, int, string, Vector3, PrematchUI

### Community 29 - "INetTransport"
Cohesion: 0.13
Nodes (10): Camera, Color, float, int, Light, List, Material, Quaternion (+2 more)

### Community 30 - ".ClientUpdate"
Cohesion: 0.11
Nodes (9): bool, float, int, string, Transform, uint, Vector3, Body (+1 more)

### Community 31 - "Dribble"
Cohesion: 0.09
Nodes (11): Rigidbody, Rigidbody, Rigidbody, bool, float, Func, Vector3, Striker (+3 more)

### Community 32 - "Footballer"
Cohesion: 0.15
Nodes (11): bool, byte, float, string, BodyPlan, HeaderAction, Species, SpeciesAxis (+3 more)

### Community 34 - "PitchBuilder"
Cohesion: 0.18
Nodes (7): byte, Dictionary, float, List, uint, Pending, ReliableChannel

### Community 35 - ".Empty"
Cohesion: 0.38
Nodes (3): float, Func, CrosserControl

### Community 36 - "NetCodec"
Cohesion: 0.09
Nodes (9): IPlayerController, bool, float, Func, Quaternion, Vector3, KeeperController, State (+1 more)

### Community 37 - ".Configure"
Cohesion: 0.14
Nodes (12): bool, float, int, Rigidbody, Vector3, BallController, BodyTouch, SetPieceSpin (+4 more)

### Community 38 - "PlayerPreview"
Cohesion: 0.16
Nodes (7): int, IPAddress, IPEndPoint, List, string, NetEndpoint, Action

### Community 39 - "DefensiveWall"
Cohesion: 0.20
Nodes (10): depth, source, version, dependencies, depth, source, version, com.unity.modules.uielements (+2 more)

### Community 40 - ".PushRoster"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.physics, com.unity.modules.physics

### Community 41 - "FlexNet"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 42 - "SessionBrowserUI"
Cohesion: 0.08
Nodes (15): bool, List, RuntimeInitializeOnLoadMethod, Multiplayer, NetPumpRunner, NetPump, Action, bool (+7 more)

### Community 44 - "Crowd"
Cohesion: 0.22
Nodes (9): Color, GameObject, Material, Texture2D, Transform, Vector3, JerseyFaces, Make (+1 more)

### Community 45 - ".ResetTo"
Cohesion: 0.12
Nodes (14): bool, Camera, float, Func, int, List, Material, Mesh (+6 more)

### Community 46 - ".Empty"
Cohesion: 0.19
Nodes (10): float, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion, Transform (+2 more)

### Community 47 - ".Box"
Cohesion: 0.19
Nodes (8): Action, bool, float, Vector3, Celebration, Emote, EmotePose, Emote

### Community 48 - "Goalkeeper"
Cohesion: 0.13
Nodes (10): bool, float, int, List, Quaternion, Rigidbody, Transform, Vector3 (+2 more)

### Community 49 - "ShotServer"
Cohesion: 0.38
Nodes (3): Dictionary, string, ChatCensor

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.33
Nodes (6): com.unity.modules.hierarchycore, dependencies, depth, source, version, com.unity.modules.hierarchycore

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.15
Nodes (7): PlayerAppearance, Vector3, NetCodec, NetWriter, Vector3, BinaryWriter, MemoryStream

### Community 53 - ".Build"
Cohesion: 0.22
Nodes (17): bool, Vector2, NetInputSource, bool, byte, float, string, uint (+9 more)

### Community 54 - "GameInput"
Cohesion: 0.19
Nodes (8): bool, Delivery, float, int, string, Transform, Vector3, FreeplayGame

### Community 55 - "QuickChat"
Cohesion: 0.13
Nodes (15): Refs, ScrimRole, float, PhysicsMaterial, Transform, Vector3, Refs, ScrimmageArena (+7 more)

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
Cohesion: 0.21
Nodes (7): bool, float, int, string, Transform, Vector3, TimeTrialGame

### Community 61 - ".Box"
Cohesion: 0.38
Nodes (4): Vector3, KeeperPose, b, e

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
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.ui, com.unity.modules.ui

### Community 67 - "CrosserBubble"
Cohesion: 0.13
Nodes (9): Collider, float, Transform, CrosserBubble, Action, MultiplayerHubUI, NetAccuracyMatch, NetBackstop (+1 more)

### Community 68 - ".ResetTo"
Cohesion: 0.29
Nodes (3): byte, uint, LobbyProbe

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
Cohesion: 0.16
Nodes (14): Action, bool, ConcurrentQueue, float, int, IPAddress, IPEndPoint, List (+6 more)

### Community 73 - ".PhysMat"
Cohesion: 0.20
Nodes (9): Collider, Color, float, GameObject, int, Material, Transform, Vector3 (+1 more)

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "com.unity.modules.adaptiveperformance"
Cohesion: 0.09
Nodes (13): bool, Dictionary, float, IEnumerator, int, RuntimeInitializeOnLoadMethod, string, Vector3 (+5 more)

### Community 77 - "com.unity.modules.ai"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 79 - "PitchLayout"
Cohesion: 0.32
Nodes (6): float, int, Material, Transform, Vector3, PitchBuilder

### Community 81 - ".PhysMat"
Cohesion: 0.12
Nodes (11): Action, bool, Collider, Color, float, int, Material, Transform (+3 more)

### Community 82 - "com.unity.modules.wind"
Cohesion: 0.18
Nodes (10): bool, Color, float, int, Material, Transform, Vector3, Crowd (+2 more)

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
Cohesion: 0.13
Nodes (12): Material, bool, byte, Color, float, int, string, Texture2D (+4 more)

### Community 90 - "MenuUI"
Cohesion: 0.13
Nodes (9): bool, float, int, string, Transform, Vector3, GameManager, float (+1 more)

### Community 91 - "PauseMenu"
Cohesion: 0.18
Nodes (5): bool, Color, float, string, LobbyUI

### Community 92 - "Goal"
Cohesion: 0.29
Nodes (4): Action, bool, Collider, Goal

### Community 93 - ".Poll"
Cohesion: 0.29
Nodes (3): int, string, QuickChat

### Community 94 - "NetPump"
Cohesion: 0.10
Nodes (14): bool, byte, Collider, ConfigurableJoint, Dictionary, float, IReadOnlyList, List (+6 more)

### Community 95 - "Role.cs"
Cohesion: 0.15
Nodes (8): bool, float, Func, Vector3, SetPieceTaker, State, SetPieceSpin, State

### Community 96 - ".StartRebind"
Cohesion: 0.32
Nodes (3): bool, float, Knockdown

### Community 97 - ".Begin"
Cohesion: 0.19
Nodes (5): float, Matrix4x4, MenuScale, Action, StadiumSelectUI

### Community 98 - "Dribble"
Cohesion: 0.06
Nodes (16): Action, Func, List, INetTransport, NetChannel, PeerId, bool, Dictionary (+8 more)

### Community 99 - "HostSetupUI"
Cohesion: 0.23
Nodes (6): Action, bool, int, string, Vector3, HostSetupUI

### Community 100 - "SessionBrowserUI"
Cohesion: 0.06
Nodes (24): byte, Vector2, GameInput, bool, Camera, float, Func, Transform (+16 more)

### Community 101 - "StatRadar"
Cohesion: 0.29
Nodes (6): ConfigurableJoint, Quaternion, Rigidbody, Vector3, JointMath, Space

### Community 102 - ".ToScreen"
Cohesion: 0.19
Nodes (9): PhysicsMaterial, Material, PhysicsMaterial, Transform, Vector3, Arena, Refs, PhysicsMaterial (+1 more)

### Community 103 - "StadiumStyle"
Cohesion: 0.32
Nodes (7): bool, Color, float, int, string, StadiumStyle, Surroundings

### Community 104 - "BoneSpec"
Cohesion: 0.19
Nodes (13): bool, float, int, string, Vector3, BodyLayout, BodyLayoutDef, BoneSpec (+5 more)

### Community 105 - ".SetMode"
Cohesion: 0.16
Nodes (5): float, Quaternion, Vector3, Goalkeeper, State

### Community 106 - "JerseyDesigns.Nations3.cs"
Cohesion: 0.27
Nodes (4): Action, bool, Texture2D, MenuUI

### Community 108 - "StatRadar"
Cohesion: 0.32
Nodes (4): Color, Rect, Vector2, StatRadar

### Community 109 - ".DriveTowardRotation"
Cohesion: 0.17
Nodes (5): Vector2, IStrikerInput, Texture2D, Material, Material

### Community 110 - "PitchLayout"
Cohesion: 0.18
Nodes (8): bool, float, int, Quaternion, Vector3, PitchLayout, Seat, Side

### Community 111 - ".ResetTo"
Cohesion: 0.25
Nodes (5): bool, float, int, string, KeeperGame

### Community 112 - "AimReticle"
Cohesion: 0.14
Nodes (10): float, Material, Transform, Vector3, AimReticle, bool, float, Transform (+2 more)

### Community 114 - ".Set"
Cohesion: 0.38
Nodes (4): Vector3, RagdollPose, bone, euler

### Community 116 - "JerseyDesigns.Nations9.cs"
Cohesion: 0.07
Nodes (16): JoinRefusal, NetRole, bool, byte, Dictionary, float, HashSet, int (+8 more)

### Community 119 - "KickDetector"
Cohesion: 0.25
Nodes (3): Collision, float, KickDetector

### Community 121 - ".NavButtons"
Cohesion: 0.07
Nodes (17): bool, Camera, float, GameObject, Light, Material, Quaternion, Rect (+9 more)

## Knowledge Gaps
- **130 isolated node(s):** `Reason`, `Phase`, `SetPieceSpin`, `Emote`, `Stage` (+125 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **15 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `JerseyDesigns.Nations7.cs`, `Jersey / Nation Designs`, `Dribble System`, `JerseyDesigns.Nations9.cs`, `Input & Keybinds`, `Goalkeeper AI & Control`, `Direct IP Transport`, `LobbyUI`, `Net Set-Piece Match`, `SkillIcons`, `OptionsMenu`, `PrematchUI`, `Bone`, `CustomizeUI`, `.OnGUI`, `Celebration`, `SkillTree`, `Footballer`, `NetStrikerMatch`, `com.unity.modules.jsonserialize`, `INetTransport`, `Dribble`, `Footballer`, `.Empty`, `NetCodec`, `Crowd`, `.ResetTo`, `.Empty`, `.Box`, `Goalkeeper`, `ShotServer`, `.SetLocalInput`, `GameInput`, `QuickChat`, `.Configure`, `.Box`, `CrosserBubble`, `.PhysMat`, `com.unity.modules.adaptiveperformance`, `Snapshot`, `HostSetupUI`, `PitchLayout`, `JerseyDesigns.Nations8.cs`, `.PhysMat`, `com.unity.modules.wind`, `OptionsMenu`, `OptionsMenu`, `.Set`, `Crosser`, `MenuUI`, `Goal`, `.Poll`, `Role.cs`, `.StartRebind`, `.Begin`, `SessionBrowserUI`, `StatRadar`, `.ToScreen`, `StadiumStyle`, `BoneSpec`, `JerseyDesigns.Nations3.cs`, `ChatCensor`, `StatRadar`, `.DriveTowardRotation`, `PitchLayout`, `.ResetTo`, `.Set`, `KickDetector`, `.NavButtons`, `JerseyDesigns.Nations10.cs`, `JerseyDesigns.Nations3.cs`?**
  _High betweenness centrality (0.160) - this node is a cross-community bridge._
- **Why does `NetSession` connect `JerseyDesigns.Nations9.cs` to `Dribble`, `SessionBrowserUI`, `QuickChat`, `NetSetPieceMatch`, `SessionBrowserUI`, `.DriveTowardRotation`, `Bone`, `com.unity.modules.imageconversion`, `.Build`, `SteamTransport`, `PauseMenu`, `.ClientUpdate`?**
  _High betweenness centrality (0.143) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `NetPump` to `Dribble System`, `Net Messages & Wire Codec`, `Input & Keybinds`, `AccuracyGame`, `SkillTree`, `SteamTransport`, `NetSetPieceMatch`, `INetTransport`, `.ClientUpdate`, `Dribble`, `Footballer`, `NetCodec`, `.Configure`, `.Box`, `GameInput`, `.Configure`, `CrosserBubble`, `.PhysMat`, `.Set`, `MenuUI`, `Role.cs`, `.StartRebind`, `SessionBrowserUI`, `BoneSpec`, `.SetMode`, `.DriveTowardRotation`, `.ResetTo`, `AimReticle`, `Bone`, `KickDetector`, `.NavButtons`?**
  _High betweenness centrality (0.128) - this node is a cross-community bridge._
- **What connects `Reason`, `Phase`, `SetPieceSpin` to the rest of the system?**
  _130 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Ball Physics & Launch` be split into smaller, more focused modules?**
  _Cohesion score 0.06927551560021153 - nodes in this community are weakly interconnected._
- **Should `Jersey / Nation Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.11901263590949163 - nodes in this community are weakly interconnected._
- **Should `Dribble System` be split into smaller, more focused modules?**
  _Cohesion score 0.053482221569203646 - nodes in this community are weakly interconnected._