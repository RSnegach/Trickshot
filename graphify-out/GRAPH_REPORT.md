# Graph Report - .  (2026-07-22)

## Corpus Check
- 6 files · ~170,813 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2118 nodes · 4931 edges · 155 communities (99 shown, 56 thin omitted)
- Extraction: 88% EXTRACTED · 12% INFERRED · 0% AMBIGUOUS · INFERRED: 607 edges (avg confidence: 0.8)
- Token cost: 109,971 input · 0 output

## Community Hubs (Navigation)
- Jersey / Nation Kit Designs
- Dribble Ball Control
- Scrimmage Arena Builder
- Direct-IP UDP Transport
- Multiplayer Entry / Net Pump
- FlexNet UI Layout
- Active Ragdoll Rig
- Cross Map + HUD
- Kick Detection / Striker
- Prematch UI / Sim Config
- Peer Identity + Roles
- Transport Seam (INetTransport)
- Skill Tree Icons
- Ball Controller Physics
- Pitch Layout / Seating
- Net Set-Piece Match Flow
- Net Session Wiring
- Net Writer / Serialization Out
- Game Bootstrap / Mode Lifecycle
- Cosmetics / Accessories
- Set-Piece Taker
- Skill Tree Model
- Celebration / Emotes
- Customize UI
- Netcode Architecture Concepts
- Game Camera
- Actor / Mesh Builders
- Keeper Controller (Dive/Save)
- Net Striker Match
- Unity Subsystems Package
- Input Frame Sampling
- Free Kick Game Mode
- Accuracy Game Mode
- Crowd / Crowd Cheer
- Net Scrimmage Match
- Net Message Structs
- Net Codec / Reader
- Accuracy Target
- Game Manager
- Net Backstop / Replay
- Game Input / Rebinding
- Brush / Paint Drawing
- Unity HierarchyCore Package
- Player Preview Render
- Defensive Wall Builder
- Freeplay Game Mode
- Goalkeeper Dive States
- Unity Package Dep
- Unity Package Dep
- Unity Package Dep
- Striker Input Feed
- Customize Submenus
- Player Profile / Ratings
- Keeper Game Mode
- Unity Package Dep
- Unity Package Dep
- Unity Package Dep
- Roster Slot Assignment
- Replay Broadcast
- Crosser Launch
- Sniper Aim / Fire
- Keybinds
- Lobby UI
- Aim Reticle
- packages-lock Dependencies
- Pause Menu
- Shot Server
- Unity Package Dep
- Unity Package Dep
- Unity Package Dep
- Aim Mode Control
- Menu UI
- Options Menu / Rebinding
- Unity Manifest Packages
- Body Preset Drag UI
- Knockdown / Recover
- Joint Math (Ragdoll Drive)
- Framework Concepts (Docs)
- Goal Trigger
- Power Meter / Scoreboard HUD
- Keeper Pose
- Ragdoll Pose
- Kyrgyz Sun Emblem Asset
- Soviet Emblem Asset
- Animation State
- Jersey Slot Sync
- Multiplayer Hub UI
- Stadium Select UI
- Unity Package Dep
- Unity Package Dep
- Unity Package Dep
- Unity Package Dep
- Unity Package Dep
- Player Controller Interface
- Kick Detector Attach
- Jersey Nations 1
- Jersey Nations 10
- Jersey Nations 3
- Jersey Nations 5
- Jersey Nations 7
- Jersey Nations 8
- Jersey Nations 9
- Unity Accessibility Module
- Unity AdaptivePerformance Module
- Unity AI Module
- Unity AndroidJNI Module
- Unity Cloth Module
- Unity ParticleSystem Module
- Unity ScreenCapture Module
- Unity TerrainPhysics Module
- Unity Tilemap Module
- Unity Umbra Module
- Unity Analytics Module
- Unity WebRequestTexture Module
- Unity WebRequestWWW Module
- Unity VectorGraphics Module
- Unity Vehicles Module
- Unity Video Module
- Unity VR Module
- Unity Wind Module
- Unity Multiplayer Center
- Unity Animation Module
- Unity AssetBundle Module
- Unity Audio Module
- Unity ImageConversion Module
- Unity IMGUI Module
- Unity JsonSerialize Module
- Unity Physics Module
- Unity Physics2D Module
- Unity Terrain Module
- Unity UI Module
- Unity WebRequest Module
- Unity WebRequestAudio Module
- Unity XR Module
- Ball / Crosser Link
- Community 137
- Community 138
- Community 139
- Community 140
- Community 141
- Community 142
- Community 143
- Community 144
- Community 145
- Community 146
- Community 147
- Community 148
- Community 149
- Community 150
- Community 151
- Community 152
- Community 153
- Community 154

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 111 edges
2. `Trickshot` - 86 edges
3. `NetSession` - 71 edges
4. `BallController` - 70 edges
5. `CustomizeUI` - 69 edges
6. `NetSetPieceMatch` - 60 edges
7. `ScrimmageGame` - 59 edges
8. `Striker` - 59 edges
9. `JerseyDesigns` - 58 edges
10. `GameCamera` - 52 edges

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
- **graphify CLI commands** — claude_graphify_query, claude_graphify_path, claude_graphify_explain, claude_graphify_update [EXTRACTED 0.85]
- **Interchangeable transports behind INetTransport seam** — multiplayer_inettransport, multiplayer_directiptransport, multiplayer_localtransport, multiplayer_steamtransport [EXTRACTED 1.00]
- **Active-ragdoll bicycle-kick mechanic** — readme_activeragdoll, readme_ragdollpose, readme_kickdetector, readme_jointmath, readme_bicycle_kick [INFERRED 0.85]
- **Host-authoritative frame loop (poll, input, snapshot)** — multiplayer_multiplayer, multiplayer_netsession, multiplayer_netmessages, multiplayer_host_authoritative [INFERRED 0.85]

## Communities (155 total, 56 thin omitted)

### Community 0 - "Jersey / Nation Kit Designs"
Cohesion: 0.06
Nodes (25): bool, float, Vector3, BallController, SetPieceSpin, Action, bool, Texture2D (+17 more)

### Community 1 - "Dribble Ball Control"
Cohesion: 0.12
Nodes (21): Action, Color32, Dictionary, int, IReadOnlyList, List, string, Texture2D (+13 more)

### Community 2 - "Scrimmage Arena Builder"
Cohesion: 0.07
Nodes (28): bool, Vector2, NetInputSource, bool, byte, Color, float, PlayerAppearance (+20 more)

### Community 3 - "Direct-IP UDP Transport"
Cohesion: 0.05
Nodes (33): bool, Camera, float, Func, int, List, Material, Mesh (+25 more)

### Community 4 - "Multiplayer Entry / Net Pump"
Cohesion: 0.10
Nodes (14): bool, Collider, ConfigurableJoint, float, IReadOnlyList, List, Material, Quaternion (+6 more)

### Community 5 - "FlexNet UI Layout"
Cohesion: 0.10
Nodes (15): Color, float, Rect, Vector2, Vector3, CrossMap, bool, Color (+7 more)

### Community 6 - "Active Ragdoll Rig"
Cohesion: 0.08
Nodes (20): float, Vector3, DefensiveWall, bool, float, int, string, Vector3 (+12 more)

### Community 7 - "Cross Map + HUD"
Cohesion: 0.09
Nodes (12): Rigidbody, Rigidbody, Rigidbody, bool, float, Func, Vector3, Striker (+4 more)

### Community 8 - "Kick Detection / Striker"
Cohesion: 0.10
Nodes (11): ActiveRagdoll, bool, float, int, List, string, uint, Vector3 (+3 more)

### Community 9 - "Prematch UI / Sim Config"
Cohesion: 0.16
Nodes (7): Color32, Dictionary, float, int, string, Texture2D, SkillIcons

### Community 10 - "Peer Identity + Roles"
Cohesion: 0.10
Nodes (13): Action, bool, Delivery, float, int, ScrimRole, string, Vector3 (+5 more)

### Community 11 - "Transport Seam (INetTransport)"
Cohesion: 0.08
Nodes (17): bool, float, int, JerseyRx, NetSession, StampedSnap, byte, Dictionary (+9 more)

### Community 12 - "Skill Tree Icons"
Cohesion: 0.12
Nodes (10): bool, byte, Dictionary, float, IPEndPoint, ulong, DirectIpTransport, ConcurrentQueue (+2 more)

### Community 13 - "Ball Controller Physics"
Cohesion: 0.11
Nodes (8): bool, float, GUIStyle, HashSet, int, string, Vector3, ScrimmageGame

### Community 14 - "Pitch Layout / Seating"
Cohesion: 0.14
Nodes (12): Action, bool, int, string, Vector3, HostSetupUI, Color, float (+4 more)

### Community 15 - "Net Set-Piece Match Flow"
Cohesion: 0.14
Nodes (8): IPlayerController, bool, float, Func, Quaternion, Vector3, KeeperController, State

### Community 16 - "Net Session Wiring"
Cohesion: 0.11
Nodes (19): AccessoryEntry, Action, bool, float, IReadOnlyList, List, Material, Mesh (+11 more)

### Community 17 - "Net Writer / Serialization Out"
Cohesion: 0.14
Nodes (8): bool, float, int, string, uint, Vector3, Body, NetStrikerMatch

### Community 18 - "Game Bootstrap / Mode Lifecycle"
Cohesion: 0.13
Nodes (12): Action, bool, Color32, Dictionary, float, int, string, Texture2D (+4 more)

### Community 19 - "Cosmetics / Accessories"
Cohesion: 0.14
Nodes (14): Dictionary, float, HashSet, IEnumerable, int, string, Category, Effect (+6 more)

### Community 20 - "Set-Piece Taker"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 21 - "Skill Tree Model"
Cohesion: 0.11
Nodes (6): CrowdCheer, JerseyDesigns, JerseyDesigns, JerseyDesigns, Trickshot.Net, Trickshot

### Community 22 - "Celebration / Emotes"
Cohesion: 0.12
Nodes (9): Camera, Material, Transform, Camera, Material, Transform, Texture2D, LobbySlot (+1 more)

### Community 23 - "Customize UI"
Cohesion: 0.16
Nodes (6): bool, float, int, Vector3, Footballer, List

### Community 24 - "Netcode Architecture Concepts"
Cohesion: 0.13
Nodes (12): bool, Camera, float, int, Material, Refs, string, Transform (+4 more)

### Community 25 - "Game Camera"
Cohesion: 0.16
Nodes (7): bool, float, Func, Vector3, SetPieceTaker, State, SetPieceSpin

### Community 26 - "Actor / Mesh Builders"
Cohesion: 0.09
Nodes (23): com.unity.modules.subsystems, dependencies, depth, source, version, dependencies, depth, source (+15 more)

### Community 27 - "Keeper Controller (Dive/Save)"
Cohesion: 0.14
Nodes (9): bool, Camera, float, Func, Transform, Vector3, GameCamera, Mode (+1 more)

### Community 28 - "Net Striker Match"
Cohesion: 0.14
Nodes (4): INetTransport, NetChannel, PeerId, IEquatable

### Community 29 - "Unity Subsystems Package"
Cohesion: 0.16
Nodes (8): bool, float, int, string, Transform, uint, Vector3, AccuracyGame

### Community 30 - "Input Frame Sampling"
Cohesion: 0.21
Nodes (8): Action, bool, float, Vector3, Celebration, Emote, EmotePose, Emote

### Community 31 - "Free Kick Game Mode"
Cohesion: 0.35
Nodes (5): Material, Transform, uint, Vector3, SurroundBuilder

### Community 32 - "Accuracy Game Mode"
Cohesion: 0.32
Nodes (9): Color, float, int, Material, PhysicsMaterial, Transform, Vector3, StadiumBuilder (+1 more)

### Community 33 - "Crowd / Crowd Cheer"
Cohesion: 0.11
Nodes (13): Action, List, int, string, ulong, LobbyInfo, Action, bool (+5 more)

### Community 34 - "Net Scrimmage Match"
Cohesion: 0.16
Nodes (7): float, Quaternion, Vector3, Goalkeeper, State, Vector3, State

### Community 35 - "Net Message Structs"
Cohesion: 0.32
Nodes (6): float, int, Material, Transform, Vector3, PitchBuilder

### Community 36 - "Net Codec / Reader"
Cohesion: 0.23
Nodes (3): JerseyChunkMsg, NetRole, PeerId

### Community 37 - "Accuracy Target"
Cohesion: 0.14
Nodes (11): Action, bool, Collider, Color, float, int, Material, Transform (+3 more)

### Community 38 - "Game Manager"
Cohesion: 0.12
Nodes (10): bool, Camera, float, GameObject, Light, Material, Rect, Texture2D (+2 more)

### Community 39 - "Net Backstop / Replay"
Cohesion: 0.18
Nodes (8): bool, Delivery, float, int, string, Transform, Vector3, FreeplayGame

### Community 40 - "Game Input / Rebinding"
Cohesion: 0.14
Nodes (9): Action, byte, RebindingOperation, Vector2, GameInput, InputAction, InputActionAsset, InputActionMap (+1 more)

### Community 41 - "Brush / Paint Drawing"
Cohesion: 0.26
Nodes (7): Color, GameObject, Material, Transform, Vector3, Make, Shader

### Community 43 - "Unity HierarchyCore Package"
Cohesion: 0.18
Nodes (10): bool, Color, float, int, Material, Transform, Vector3, Crowd (+2 more)

### Community 44 - "Player Preview Render"
Cohesion: 0.11
Nodes (18): com.unity.modules.hierarchycore, dependencies, depth, source, version, dependencies, depth, source (+10 more)

### Community 45 - "Defensive Wall Builder"
Cohesion: 0.14
Nodes (12): NetBackstop, bool, float, int, List, Quaternion, Rigidbody, Transform (+4 more)

### Community 46 - "Freeplay Game Mode"
Cohesion: 0.12
Nodes (17): dependencies, depth, source, version, dependencies, depth, source, version (+9 more)

### Community 47 - "Goalkeeper Dive States"
Cohesion: 0.12
Nodes (17): dependencies, depth, source, version, dependencies, depth, source, version (+9 more)

### Community 48 - "Unity Package Dep"
Cohesion: 0.12
Nodes (17): dependencies, depth, source, version, dependencies, depth, source, version (+9 more)

### Community 49 - "Unity Package Dep"
Cohesion: 0.21
Nodes (3): Color, Func, label

### Community 50 - "Unity Package Dep"
Cohesion: 0.19
Nodes (3): Rect, Vector2, IEnumerator

### Community 51 - "Striker Input Feed"
Cohesion: 0.20
Nodes (7): bool, float, int, string, Transform, Vector3, GameManager

### Community 52 - "Customize Submenus"
Cohesion: 0.23
Nodes (5): bool, float, int, string, KeeperGame

### Community 53 - "Player Profile / Ratings"
Cohesion: 0.21
Nodes (7): bool, float, int, string, Transform, Vector3, TimeTrialGame

### Community 54 - "Keeper Game Mode"
Cohesion: 0.12
Nodes (16): dependencies, depth, source, url, version, depth, source, version (+8 more)

### Community 55 - "Unity Package Dep"
Cohesion: 0.12
Nodes (16): dependencies, depth, source, version, dependencies, depth, source, version (+8 more)

### Community 56 - "Unity Package Dep"
Cohesion: 0.12
Nodes (16): dependencies, depth, source, version, dependencies, depth, source, version (+8 more)

### Community 57 - "Unity Package Dep"
Cohesion: 0.15
Nodes (5): bool, List, RuntimeInitializeOnLoadMethod, Multiplayer, NetPump

### Community 58 - "Roster Slot Assignment"
Cohesion: 0.16
Nodes (4): Action, bool, string, LobbyUI

### Community 60 - "Crosser Launch"
Cohesion: 0.26
Nodes (4): bool, float, Vector3, Dribble

### Community 61 - "Sniper Aim / Fire"
Cohesion: 0.24
Nodes (7): Action, bool, float, Transform, Vector3, Sniper, LineRenderer

### Community 62 - "Keybinds"
Cohesion: 0.26
Nodes (8): PhysicsMaterial, float, PhysicsMaterial, Transform, Vector3, Refs, ScrimmageArena, PhysicsMaterialCombine

### Community 63 - "Lobby UI"
Cohesion: 0.18
Nodes (10): bool, byte, Color, float, int, string, Texture2D, PlayerAppearance (+2 more)

### Community 64 - "Aim Reticle"
Cohesion: 0.23
Nodes (6): Action, float, int, List, string, SessionBrowserUI

### Community 65 - "packages-lock Dependencies"
Cohesion: 0.18
Nodes (7): byte, Dictionary, float, List, uint, Pending, ReliableChannel

### Community 66 - "Pause Menu"
Cohesion: 0.23
Nodes (3): Dictionary, string, Keybinds

### Community 67 - "Shot Server"
Cohesion: 0.21
Nodes (5): Vector2, IStrikerInput, float, Func, CrosserControl

### Community 68 - "Unity Package Dep"
Cohesion: 0.21
Nodes (5): bool, float, Transform, Vector3, Crosser

### Community 69 - "Unity Package Dep"
Cohesion: 0.17
Nodes (9): bool, float, int, Vector3, Delivery, ScrimRole, SimConfig, Delivery (+1 more)

### Community 70 - "Unity Package Dep"
Cohesion: 0.29
Nodes (11): com.unity.modules.ai, dependencies, com.unity.modules.ai, com.unity.modules.androidjni, com.unity.modules.animation, com.unity.modules.tilemap, com.unity.modules.ui, com.unity.modules.umbra (+3 more)

### Community 71 - "Aim Mode Control"
Cohesion: 0.17
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 72 - "Menu UI"
Cohesion: 0.25
Nodes (4): Action, bool, float, PauseMenu

### Community 73 - "Options Menu / Rebinding"
Cohesion: 0.29
Nodes (4): float, int, Vector3, ShotServer

### Community 74 - "Unity Manifest Packages"
Cohesion: 0.18
Nodes (8): bool, float, int, Quaternion, Vector3, PitchLayout, Seat, Side

### Community 75 - "Body Preset Drag UI"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 76 - "Knockdown / Recover"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 77 - "Joint Math (Ragdoll Drive)"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 78 - "Framework Concepts (Docs)"
Cohesion: 0.27
Nodes (5): float, Material, Transform, Vector3, AimReticle

### Community 80 - "Goal Trigger"
Cohesion: 0.24
Nodes (6): Action, RebindingOperation, string, OptionsMenu, Tab, Tab

### Community 81 - "Power Meter / Scoreboard HUD"
Cohesion: 0.33
Nodes (6): Material, PhysicsMaterial, Transform, Vector3, Arena, Refs

### Community 82 - "Keeper Pose"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 83 - "Ragdoll Pose"
Cohesion: 0.28
Nodes (4): int, IPEndPoint, List, NetEndpoint

### Community 86 - "Animation State"
Cohesion: 0.22
Nodes (4): Action, Collision, float, KickDetector

### Community 87 - "Jersey Slot Sync"
Cohesion: 0.33
Nodes (5): ConfigurableJoint, Quaternion, Rigidbody, JointMath, Space

### Community 88 - "Multiplayer Hub UI"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 90 - "Unity Package Dep"
Cohesion: 0.32
Nodes (3): bool, float, Knockdown

### Community 91 - "Unity Package Dep"
Cohesion: 0.29
Nodes (7): bool, Color, float, int, string, StadiumStyle, Surroundings

### Community 92 - "Unity Package Dep"
Cohesion: 0.29
Nodes (4): Action, bool, Collider, Goal

### Community 93 - "Unity Package Dep"
Cohesion: 0.38
Nodes (4): Vector3, KeeperPose, b, e

### Community 94 - "Unity Package Dep"
Cohesion: 0.38
Nodes (4): Vector3, RagdollPose, bone, euler

### Community 95 - "Player Controller Interface"
Cohesion: 0.60
Nodes (5): Forty-Ray Golden Sun, Kyrgyz Sun Emblem (kyrgyz_sun.png), Kyrgyzstan Flag Emblem, Team / National Emblem Game Asset, Tunduk (Yurt Crown) Motif

### Community 96 - "Kick Detector Attach"
Cohesion: 0.60
Nodes (5): Hammer and Sickle, Soviet Emblem Sprite, Five-Pointed Star, Team Emblem / Logo, Soviet Union Symbolism

### Community 98 - "Jersey Nations 10"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.adaptiveperformance

### Community 99 - "Jersey Nations 3"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.ai

### Community 100 - "Jersey Nations 5"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.particlesystem

### Community 101 - "Jersey Nations 7"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.umbra

### Community 102 - "Jersey Nations 8"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.wind

### Community 106 - "Unity AI Module"
Cohesion: 0.50
Nodes (4): com.unity.modules.unitywebrequestwww, com.unity.modules.unitywebrequestassetbundle, com.unity.modules.unitywebrequesttexture, com.unity.modules.unitywebrequestwww

## Knowledge Gaps
- **221 isolated node(s):** `Emote`, `Stage`, `BodySub`, `Phase`, `Outcome` (+216 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **56 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `Skill Tree Model` to `Jersey / Nation Kit Designs`, `Dribble Ball Control`, `Direct-IP UDP Transport`, `FlexNet UI Layout`, `Active Ragdoll Rig`, `Cross Map + HUD`, `Peer Identity + Roles`, `Ball Controller Physics`, `Pitch Layout / Seating`, `Net Set-Piece Match Flow`, `Net Session Wiring`, `Game Bootstrap / Mode Lifecycle`, `Cosmetics / Accessories`, `Customize UI`, `Game Camera`, `Keeper Controller (Dive/Save)`, `Unity Subsystems Package`, `Input Frame Sampling`, `Net Scrimmage Match`, `Net Message Structs`, `Accuracy Target`, `Game Manager`, `Net Backstop / Replay`, `Game Input / Rebinding`, `Brush / Paint Drawing`, `Unity HierarchyCore Package`, `Defensive Wall Builder`, `Striker Input Feed`, `Customize Submenus`, `Player Profile / Ratings`, `Crosser Launch`, `Sniper Aim / Fire`, `Keybinds`, `Lobby UI`, `Pause Menu`, `Shot Server`, `Unity Package Dep`, `Unity Package Dep`, `Menu UI`, `Options Menu / Rebinding`, `Unity Manifest Packages`, `Framework Concepts (Docs)`, `Goal Trigger`, `Power Meter / Scoreboard HUD`, `Animation State`, `Jersey Slot Sync`, `Unity Package Dep`, `Unity Package Dep`, `Unity Package Dep`, `Unity Package Dep`, `Unity Package Dep`, `Jersey Nations 1`, `Unity Cloth Module`, `Unity ParticleSystem Module`, `Unity ScreenCapture Module`, `Unity TerrainPhysics Module`, `Unity Tilemap Module`, `Unity Umbra Module`, `Unity Analytics Module`, `Unity WebRequestTexture Module`?**
  _High betweenness centrality (0.173) - this node is a cross-community bridge._
- **Why does `CustomizeUI` connect `Game Bootstrap / Mode Lifecycle` to `Dribble Ball Control`, `Game Manager`, `Jersey Nations 9`, `Defensive Wall Builder`, `Unity Package Dep`, `Unity Package Dep`, `Cosmetics / Accessories`, `Stadium Select UI`, `Lobby UI`?**
  _High betweenness centrality (0.105) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `Multiplayer Entry / Net Pump` to `Jersey / Nation Kit Designs`, `Direct-IP UDP Transport`, `Active Ragdoll Rig`, `Cross Map + HUD`, `Kick Detection / Striker`, `Ball Controller Physics`, `Net Set-Piece Match Flow`, `Net Writer / Serialization Out`, `Skill Tree Model`, `Celebration / Emotes`, `Customize UI`, `Netcode Architecture Concepts`, `Game Camera`, `Unity Subsystems Package`, `Input Frame Sampling`, `Net Scrimmage Match`, `Game Manager`, `Net Backstop / Replay`, `Defensive Wall Builder`, `Striker Input Feed`, `Customize Submenus`, `Player Profile / Ratings`, `Crosser Launch`, `Shot Server`, `Unity Package Dep`, `Animation State`, `Unity Package Dep`, `Unity AdaptivePerformance Module`?**
  _High betweenness centrality (0.094) - this node is a cross-community bridge._
- **What connects `Emote`, `Stage`, `BodySub` to the rest of the system?**
  _221 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Jersey / Nation Kit Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.06468858593958834 - nodes in this community are weakly interconnected._
- **Should `Dribble Ball Control` be split into smaller, more focused modules?**
  _Cohesion score 0.11901263590949163 - nodes in this community are weakly interconnected._
- **Should `Scrimmage Arena Builder` be split into smaller, more focused modules?**
  _Cohesion score 0.07404426559356136 - nodes in this community are weakly interconnected._