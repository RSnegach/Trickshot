# Graph Report - Trickshot  (2026-08-30)

## Corpus Check
- 143 files · ~666,835 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 3011 nodes · 7836 edges · 136 communities (127 shown, 9 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 857 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `2efb1e8a`
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
- BodyLayoutDef
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
- .Join
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
- SessionBrowserUI
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
- MenuUI
- SimConfig
- SimConfig
- LobbySlot
- FreeplayGame
- com.unity.modules.ai
- com.unity.modules.imgui
- com.unity.modules.ui
- CrosserBubble
- AccuracyGame
- AimReticle
- QuickChat
- .SkillPresetButtons
- .AdvanceTurn
- .Build
- Trickshot (3D trick-shot football prototype)
- com.unity.modules.adaptiveperformance
- Snapshot
- com.unity.modules.ai
- .Tick
- .Heavy
- MenuUI
- .PhysMat
- JerseyDesigns.Nations10.cs
- StadiumStyle
- com.unity.modules.androidjni
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- Bone
- .Set
- .Set
- MenuUI
- DefensiveWall
- Goal
- CrosserControl
- NetPump
- Role.cs
- .StartRebind
- .Begin
- FreeplayGame
- .AttachKickDetectors
- SessionBrowserUI
- .StartRebind
- SurroundBuilder
- IPlayerController
- BoneSpec
- AccuracyBoard
- JerseyDesigns.Nations4.cs
- .Set
- .Update
- skyprep.py
- com.unity.nuget.newtonsoft-json
- Turf
- .ListLobbies
- .Update
- com.unity.modules.screencapture
- PlayerAppearance
- .AttachKickDetectors
- .OnCollisionEnter
- StadiumSelectUI
- Species
- .ListLobbies
- .LaunchBandDive
- com.unity.modules.terrain
- Body
- .BuildFootballer
- grassprep.py
- MenuUI
- StadiumSelectUI
- Goal
- AssetImportRules
- .Set
- .OptionGrid
- postprep.py
- Transform
- NetBackstop.cs

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 169 edges
2. `Trickshot` - 120 edges
3. `BallController` - 103 edges
4. `ScrimmageGame` - 97 edges
5. `NetSession` - 95 edges
6. `CustomizeUI` - 80 edges
7. `NetSetPieceMatch` - 75 edges
8. `Striker` - 75 edges
9. `Footballer` - 62 edges
10. `GameBootstrap` - 62 edges

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

## Communities (136 total, 9 thin omitted)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.06
Nodes (18): Action, bool, Color, Color32, Dictionary, float, Func, GUIStyle (+10 more)

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.12
Nodes (21): Action, Color32, Dictionary, int, IReadOnlyList, List, string, Texture2D (+13 more)

### Community 2 - "Dribble System"
Cohesion: 0.10
Nodes (16): AiDifficulty, ScrimPos, bool, Color, float, int, string, Vector2 (+8 more)

### Community 3 - "Net Messages & Wire Codec"
Cohesion: 0.14
Nodes (7): Action, bool, float, Vector3, Dribble, TackleResult, TackleResult

### Community 4 - "Input & Keybinds"
Cohesion: 0.25
Nodes (5): bool, float, int, string, KeeperGame

### Community 5 - "SkillTree"
Cohesion: 0.10
Nodes (12): bool, byte, ConcurrentQueue, Dictionary, float, Func, int, IPEndPoint (+4 more)

### Community 6 - "Goalkeeper AI & Control"
Cohesion: 0.09
Nodes (17): Dictionary, float, HashSet, IEnumerable, int, List, string, Category (+9 more)

### Community 7 - "Skill Icon Drawing"
Cohesion: 0.16
Nodes (7): Color32, Dictionary, float, int, string, Texture2D, SkillIcons

### Community 8 - "AccuracyGame"
Cohesion: 0.05
Nodes (14): JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns, JerseyDesigns (+6 more)

### Community 9 - "Direct IP Transport"
Cohesion: 0.06
Nodes (34): AccessoryEntry, bool, float, int, Material, Matrix4x4, Mesh, Transform (+26 more)

### Community 10 - "LobbyUI"
Cohesion: 0.20
Nodes (8): Action, bool, float, Vector3, Celebration, Emote, EmotePose, Emote

### Community 11 - "Net Set-Piece Match"
Cohesion: 0.33
Nodes (5): int, string, AdultQuiz, Q, Q

### Community 13 - "SetPieceTaker"
Cohesion: 0.18
Nodes (10): bool, Color, float, int, Material, Transform, Vector3, Crowd (+2 more)

### Community 14 - "OptionsMenu"
Cohesion: 0.16
Nodes (8): bool, float, Func, Quaternion, Vector2, Vector3, KeeperController, State

### Community 15 - "PrematchUI"
Cohesion: 0.15
Nodes (10): Action, bool, Delivery, float, GUIStyle, int, ScrimPos, string (+2 more)

### Community 16 - "Bone"
Cohesion: 0.15
Nodes (7): bool, float, int, string, Vector3, AccuracyGame, Phase

### Community 17 - ".Box"
Cohesion: 0.19
Nodes (7): Color32, Dictionary, float, int, string, Texture2D, MenuIcons

### Community 18 - "CustomizeUI"
Cohesion: 0.25
Nodes (4): Color, MsgType, NetReader, BinaryReader

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 20 - ".OnGUI"
Cohesion: 0.16
Nodes (6): float, int, Matrix4x4, Rect, Vector2, MenuScale

### Community 21 - "GameInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "Celebration"
Cohesion: 0.12
Nodes (14): bool, Camera, float, Func, int, List, Material, Mesh (+6 more)

### Community 23 - "SkillTree"
Cohesion: 0.07
Nodes (17): float, int, Transform, uint, Vector3, AccuracyBoard, Action, bool (+9 more)

### Community 24 - "Footballer"
Cohesion: 0.12
Nodes (9): Action, bool, string, BuildAll, Plat, BuildTarget, MenuItem, Plat (+1 more)

### Community 25 - "SteamTransport"
Cohesion: 0.14
Nodes (10): bool, float, int, List, Vector3, GoalBound(), Note(), MatchProbe (+2 more)

### Community 26 - ".Join"
Cohesion: 0.13
Nodes (9): List, Action, bool, float, int, List, string, ulong (+1 more)

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.11
Nodes (10): bool, float, int, List, string, uint, Vector3, Body (+2 more)

### Community 28 - "com.unity.modules.jsonserialize"
Cohesion: 0.13
Nodes (11): Action, bool, float, int, List, string, Entry, Kind (+3 more)

### Community 29 - "INetTransport"
Cohesion: 0.14
Nodes (10): bool, float, List, Vector3, Bar, Option, Passing, PassKind (+2 more)

### Community 30 - ".ClientUpdate"
Cohesion: 0.12
Nodes (10): bool, float, int, string, uint, Vector3, Body, NetStrikerMatch (+2 more)

### Community 31 - "Dribble"
Cohesion: 0.23
Nodes (11): bool, byte, Color, float, int, string, Texture2D, PlayerAppearance (+3 more)

### Community 32 - "Footballer"
Cohesion: 0.19
Nodes (10): float, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion, Transform (+2 more)

### Community 33 - ".Configure"
Cohesion: 0.08
Nodes (14): bool, float, Func, int, Quaternion, Vector3, Band, Goalkeeper (+6 more)

### Community 34 - "PitchBuilder"
Cohesion: 0.26
Nodes (7): Color, float, Random, Rect, Vector2, Vector3, SetPieceMap

### Community 35 - ".Empty"
Cohesion: 0.24
Nodes (7): Action, bool, float, Transform, Vector3, Sniper, LineRenderer

### Community 36 - "NetCodec"
Cohesion: 0.08
Nodes (17): bool, Camera, float, GameObject, int, Light, Material, Quaternion (+9 more)

### Community 37 - ".Configure"
Cohesion: 0.14
Nodes (15): bool, float, int, Rigidbody, BallController, BodyTouch, SetPieceSpin, Camera (+7 more)

### Community 38 - "PlayerPreview"
Cohesion: 0.18
Nodes (6): int, IPAddress, IPEndPoint, List, string, NetEndpoint

### Community 39 - "DefensiveWall"
Cohesion: 0.12
Nodes (16): dependencies, depth, source, url, version, depth, source, version (+8 more)

### Community 40 - ".PushRoster"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.physics, com.unity.modules.physics

### Community 41 - "FlexNet"
Cohesion: 0.18
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 42 - "SessionBrowserUI"
Cohesion: 0.17
Nodes (10): GUIStyle, Color, GUIStyle, int, Rect, Texture2D, UITheme, GUISkin (+2 more)

### Community 43 - "Footballer"
Cohesion: 0.11
Nodes (11): Camera, Color, float, int, Light, List, Material, PhysicsMaterial (+3 more)

### Community 44 - "Crowd"
Cohesion: 0.15
Nodes (10): bool, float, int, List, Quaternion, Rigidbody, Transform, Vector3 (+2 more)

### Community 45 - "SessionBrowserUI"
Cohesion: 0.33
Nodes (6): Color, float, Rect, Vector2, Vector3, CrossMap

### Community 46 - ".Empty"
Cohesion: 0.14
Nodes (8): Action, bool, int, MenuUI, Phase, Action, GUIStyle, Rect

### Community 48 - "Goalkeeper"
Cohesion: 0.19
Nodes (6): float, Material, Transform, Vector3, AimReticle, Refs

### Community 49 - "ShotServer"
Cohesion: 0.14
Nodes (6): bool, RuntimeInitializeOnLoadMethod, Multiplayer, NetPumpRunner, NetPump, NetPumpRunner

### Community 50 - ".SetLocalInput"
Cohesion: 0.08
Nodes (13): bool, float, Quaternion, Transform, Vector3, Crosser, bool, float (+5 more)

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.15
Nodes (13): com.unity.modules.hierarchycore, dependencies, depth, source, version, dependencies, depth, source (+5 more)

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.15
Nodes (6): PlayerAppearance, Vector3, NetCodec, NetWriter, BinaryWriter, MemoryStream

### Community 53 - ".Build"
Cohesion: 0.20
Nodes (11): Bounds, Dictionary, GameObject, HashSet, Material, string, Transform, Vector3 (+3 more)

### Community 54 - "GameInput"
Cohesion: 0.23
Nodes (3): Collision, Vector3, SetPieceSpin

### Community 55 - "QuickChat"
Cohesion: 0.21
Nodes (7): bool, float, int, string, Transform, Vector3, TimeTrialGame

### Community 56 - "ShotServer"
Cohesion: 0.06
Nodes (31): com.coplaydev.unity-mcp, com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+23 more)

### Community 57 - "Sniper"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.androidjni

### Community 58 - "IStrikerInput"
Cohesion: 0.07
Nodes (13): bool, Dictionary, float, GUIStyle, HashSet, int, string, Vector3 (+5 more)

### Community 59 - "MenuUI"
Cohesion: 0.12
Nodes (10): bool, float, int, Random, string, Vector3, FreeKickGame, Outcome (+2 more)

### Community 60 - "SimConfig"
Cohesion: 0.18
Nodes (8): bool, Delivery, float, int, string, Transform, Vector3, FreeplayGame

### Community 61 - "SimConfig"
Cohesion: 0.07
Nodes (11): Vector3, Dictionary, string, ChatCensor, float, int, List, Queue (+3 more)

### Community 62 - "LobbySlot"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.imgui, com.unity.modules.imgui

### Community 63 - "FreeplayGame"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.animation, com.unity.modules.animation

### Community 64 - "com.unity.modules.ai"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.audio

### Community 65 - "com.unity.modules.imgui"
Cohesion: 0.17
Nodes (11): dependencies, depth, source, version, dependencies, depth, source, version (+3 more)

### Community 66 - "com.unity.modules.ui"
Cohesion: 0.11
Nodes (8): Vector2, IStrikerInput, Texture2D, bool, float, Func, CrosserControl, Texture2D

### Community 67 - "CrosserBubble"
Cohesion: 0.22
Nodes (10): PhysicsMaterial, float, int, Material, PhysicsMaterial, Transform, Vector3, Refs (+2 more)

### Community 68 - "AccuracyGame"
Cohesion: 0.06
Nodes (25): byte, Vector2, GameInput, bool, Camera, float, Func, Transform (+17 more)

### Community 69 - "AimReticle"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - "QuickChat"
Cohesion: 0.29
Nodes (4): Collider, float, Transform, CrosserBubble

### Community 71 - ".SkillPresetButtons"
Cohesion: 0.17
Nodes (12): com.unity.ext.nunit, com.unity.test-framework, dependencies, depth, source, version, dependencies, depth (+4 more)

### Community 72 - ".AdvanceTurn"
Cohesion: 0.14
Nodes (14): Action, List, Action, bool, ConcurrentQueue, float, IPAddress, IPEndPoint (+6 more)

### Community 73 - ".Build"
Cohesion: 0.17
Nodes (12): com.unity.modules.physics2d, dependencies, depth, hash, source, version, dependencies, depth (+4 more)

### Community 74 - "Trickshot (3D trick-shot football prototype)"
Cohesion: 0.22
Nodes (9): Trickshot Multiplayer Framework, Host-authoritative model, Set Pieces mode (free-kick shootout), Unity 6000.4.1f1 editor version, Bicycle kick trick, GameBootstrap, GameCamera.cs, KickDetector.cs (+1 more)

### Community 75 - "com.unity.modules.adaptiveperformance"
Cohesion: 0.09
Nodes (13): bool, Dictionary, float, IEnumerator, int, RuntimeInitializeOnLoadMethod, string, Vector3 (+5 more)

### Community 76 - "Snapshot"
Cohesion: 0.10
Nodes (13): Bar, bool, Camera, float, int, Material, Refs, string (+5 more)

### Community 77 - "com.unity.modules.ai"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 78 - ".Tick"
Cohesion: 0.23
Nodes (4): byte, int, uint, LobbyProbe

### Community 79 - ".Heavy"
Cohesion: 0.18
Nodes (7): byte, Dictionary, float, List, uint, Pending, ReliableChannel

### Community 80 - "MenuUI"
Cohesion: 0.14
Nodes (10): AiTuning, bool, float, int, List, Vector2, Vector3, Footballer (+2 more)

### Community 81 - ".PhysMat"
Cohesion: 0.29
Nodes (4): float, int, Vector3, ShotServer

### Community 82 - "JerseyDesigns.Nations10.cs"
Cohesion: 0.24
Nodes (6): bool, float, Vector3, Gait, Profile, Profile

### Community 83 - "StadiumStyle"
Cohesion: 0.29
Nodes (6): ConfigurableJoint, Quaternion, Rigidbody, Vector3, JointMath, Space

### Community 84 - "com.unity.modules.androidjni"
Cohesion: 0.60
Nodes (5): Bundled License Inclusion Requirement, Hair Atlas Asset License, No Attribution Required, No-Resale Restriction, Royalty-Free Unlimited Use Grant

### Community 85 - "Kyrgyz Sun Emblem (kyrgyz_sun.png)"
Cohesion: 0.60
Nodes (5): Forty-Ray Golden Sun, Kyrgyz Sun Emblem (kyrgyz_sun.png), Kyrgyzstan Flag Emblem, Team / National Emblem Game Asset, Tunduk (Yurt Crown) Motif

### Community 86 - "Soviet Emblem Sprite"
Cohesion: 0.60
Nodes (5): Hammer and Sickle, Soviet Emblem Sprite, Five-Pointed Star, Team Emblem / Logo, Soviet Union Symbolism

### Community 87 - "Bone"
Cohesion: 0.14
Nodes (5): GameMode, bool, GameObject, Light, GameBootstrap

### Community 88 - ".Set"
Cohesion: 0.38
Nodes (4): Vector3, KeeperPose, b, e

### Community 89 - ".Set"
Cohesion: 0.05
Nodes (24): Action, bool, Func, int, List, string, ulong, INetTransport (+16 more)

### Community 90 - "MenuUI"
Cohesion: 0.18
Nodes (6): Action, bool, Color, float, string, LobbyUI

### Community 91 - "DefensiveWall"
Cohesion: 0.38
Nodes (4): Vector3, RagdollPose, bone, euler

### Community 92 - "Goal"
Cohesion: 0.21
Nodes (18): bool, Vector2, NetInputSource, bool, byte, float, string, uint (+10 more)

### Community 93 - "CrosserControl"
Cohesion: 0.19
Nodes (8): bool, float, Func, Quaternion, Vector3, SetPieceTaker, State, State

### Community 94 - "NetPump"
Cohesion: 0.06
Nodes (20): Rigidbody, Rigidbody, bool, Bounds, byte, Collider, ConfigurableJoint, Dictionary (+12 more)

### Community 95 - "Role.cs"
Cohesion: 0.09
Nodes (15): bool, Camera, Color, float, GUIStyle, int, List, Rect (+7 more)

### Community 96 - ".StartRebind"
Cohesion: 0.32
Nodes (4): Color, Rect, Vector2, StatRadar

### Community 97 - ".Begin"
Cohesion: 0.20
Nodes (4): bool, float, Vector3, Knockdown

### Community 98 - "FreeplayGame"
Cohesion: 0.17
Nodes (17): bool, float, int, Quaternion, Vector3, PitchLayout, Seat, Side (+9 more)

### Community 99 - ".AttachKickDetectors"
Cohesion: 0.25
Nodes (3): Collision, float, KickDetector

### Community 100 - "SessionBrowserUI"
Cohesion: 0.32
Nodes (6): float, int, Material, Transform, Vector3, PitchBuilder

### Community 101 - ".StartRebind"
Cohesion: 0.25
Nodes (8): Color, GameObject, Material, Transform, Vector3, JerseyFaces, Make, Shader

### Community 102 - "SurroundBuilder"
Cohesion: 0.24
Nodes (8): Color, float, Material, string, Transform, uint, Vector3, SurroundBuilder

### Community 103 - "IPlayerController"
Cohesion: 0.12
Nodes (10): Action, bool, GUIStyle, int, string, Vector3, HostSetupUI, Func (+2 more)

### Community 104 - "BoneSpec"
Cohesion: 0.08
Nodes (10): IPlayerController, Rigidbody, bool, Collider, float, Func, Vector3, Striker (+2 more)

### Community 105 - "AccuracyBoard"
Cohesion: 0.07
Nodes (28): bool, float, int, string, Vector3, BodyLayout, BodyLayoutDef, BoneSpec (+20 more)

### Community 106 - "JerseyDesigns.Nations4.cs"
Cohesion: 0.20
Nodes (10): bool, Camera, Color, Dictionary, float, Light, Material, string (+2 more)

### Community 107 - ".Set"
Cohesion: 0.22
Nodes (7): GameObject, Material, Mesh, Transform, Vector2, Vector3, Landform

### Community 108 - ".Update"
Cohesion: 0.40
Nodes (3): bool, UIFont, Font

### Community 109 - "skyprep.py"
Cohesion: 0.26
Nodes (12): dir_from_pixel(), find_sun(), lum(), main(), Turn the Poly Haven .hdr pureskies into game-ready equirectangular skybox textur, Euler for a directional light whose forward is -d (i.e. shining from d)., Exposure-normalise, roll the highlights off, then gamma-encode to bytes., Decode a Radiance RGBE file to a float32 HxWx3 array of linear radiance. (+4 more)

### Community 110 - "com.unity.nuget.newtonsoft-json"
Cohesion: 0.29
Nodes (7): com.unity.nuget.newtonsoft-json, dependencies, depth, source, url, version, com.unity.nuget.newtonsoft-json

### Community 111 - "Turf"
Cohesion: 0.17
Nodes (8): bool, Color, Dictionary, float, int, Material, Texture2D, Turf

### Community 112 - ".ListLobbies"
Cohesion: 0.24
Nodes (8): bool, Color, float, int, string, Vector3, StadiumStyle, Surroundings

### Community 113 - ".Update"
Cohesion: 0.07
Nodes (16): JoinRefusal, NetRole, bool, byte, Dictionary, float, HashSet, int (+8 more)

### Community 114 - "com.unity.modules.screencapture"
Cohesion: 0.33
Nodes (6): com.unity.modules.screencapture, dependencies, depth, source, version, com.unity.modules.screencapture

### Community 116 - ".AttachKickDetectors"
Cohesion: 0.19
Nodes (7): bool, float, string, DisplaySettings, RuntimeInitializeOnLoadMethod, FullScreenMode, Resolution

### Community 117 - ".OnCollisionEnter"
Cohesion: 0.33
Nodes (6): com.unity.modules.unitywebrequest, dependencies, depth, source, version, com.unity.modules.unitywebrequest

### Community 118 - "StadiumSelectUI"
Cohesion: 0.23
Nodes (3): Dictionary, string, Keybinds

### Community 120 - ".ListLobbies"
Cohesion: 0.33
Nodes (4): float, int, Queue, CallLimiter

### Community 121 - ".LaunchBandDive"
Cohesion: 0.20
Nodes (9): Collider, Color, float, GameObject, int, Material, Transform, Vector3 (+1 more)

### Community 123 - "Body"
Cohesion: 0.22
Nodes (7): int, RebindingOperation, string, Vector2, OptionsMenu, Tab, Tab

### Community 124 - ".BuildFootballer"
Cohesion: 0.32
Nodes (4): Color32, int, Texture2D, TitleGlyph

### Community 125 - "grassprep.py"
Cohesion: 0.60
Nodes (4): load(), main(), member(), Build the turf detail layer in Assets/Resources/Turf from an ambientCG scan.  So

### Community 126 - "MenuUI"
Cohesion: 0.15
Nodes (8): GoalFrame, Action, MultiplayerHubUI, NetAccuracyMatch, NetBackstop, Action, StadiumSelectUI, MonoBehaviour

### Community 128 - "Goal"
Cohesion: 0.29
Nodes (4): Action, bool, Collider, Goal

### Community 131 - ".Set"
Cohesion: 0.29
Nodes (3): int, string, QuickChat

### Community 132 - ".OptionGrid"
Cohesion: 0.33
Nodes (6): Material, PhysicsMaterial, Transform, Vector3, Arena, Refs

### Community 140 - "NetBackstop.cs"
Cohesion: 0.40
Nodes (4): Ideas so far, Open questions (not answered yet), Problem, Trickshot: Replayability Brainstorm

## Knowledge Gaps
- **178 isolated node(s):** `Reason`, `Phase`, `SetPieceSpin`, `Emote`, `Stage` (+173 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **9 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `Goal`, `AssetImportRules`, `Jersey / Nation Designs`, `.Set`, `Input & Keybinds`, `.OptionGrid`, `Dribble System`, `Goalkeeper AI & Control`, `Direct IP Transport`, `LobbyUI`, `Net Set-Piece Match`, `BodyLayoutDef`, `SetPieceTaker`, `Bone`, `.Box`, `.OnGUI`, `Celebration`, `SkillTree`, `Footballer`, `SteamTransport`, `com.unity.modules.jsonserialize`, `INetTransport`, `.ClientUpdate`, `Dribble`, `Footballer`, `.Configure`, `PitchBuilder`, `.Empty`, `NetCodec`, `Footballer`, `Crowd`, `SessionBrowserUI`, `.Empty`, `Goalkeeper`, `.SetLocalInput`, `.Build`, `QuickChat`, `IStrikerInput`, `MenuUI`, `SimConfig`, `SimConfig`, `com.unity.modules.ui`, `CrosserBubble`, `AccuracyGame`, `QuickChat`, `com.unity.modules.adaptiveperformance`, `MenuUI`, `.PhysMat`, `JerseyDesigns.Nations10.cs`, `StadiumStyle`, `.Set`, `DefensiveWall`, `CrosserControl`, `.StartRebind`, `.Begin`, `FreeplayGame`, `.AttachKickDetectors`, `SessionBrowserUI`, `.StartRebind`, `BoneSpec`, `AccuracyBoard`, `JerseyDesigns.Nations4.cs`, `.Set`, `.Update`, `Turf`, `.ListLobbies`, `PlayerAppearance`, `.AttachKickDetectors`, `StadiumSelectUI`, `.ListLobbies`, `.LaunchBandDive`, `Body`, `.BuildFootballer`, `MenuUI`?**
  _High betweenness centrality (0.224) - this node is a cross-community bridge._
- **Why does `NetSession` connect `.Update` to `com.unity.modules.ui`, `AccuracyGame`, `Goalkeeper AI & Control`, `Snapshot`, `.Box`, `ShotServer`, `CustomizeUI`, `Species`, `.Set`, `MenuUI`, `NetSetPieceMatch`, `Goal`, `SimConfig`, `.ClientUpdate`?**
  _High betweenness centrality (0.119) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `NetPump` to `Dribble System`, `Net Messages & Wire Codec`, `Input & Keybinds`, `AccuracyGame`, `LobbyUI`, `BodyLayoutDef`, `OptionsMenu`, `Bone`, `NetSetPieceMatch`, `INetTransport`, `.ClientUpdate`, `.Configure`, `NetCodec`, `.Configure`, `Footballer`, `Goalkeeper`, `.SetLocalInput`, `GameInput`, `QuickChat`, `IStrikerInput`, `MenuUI`, `SimConfig`, `com.unity.modules.ui`, `AccuracyGame`, `QuickChat`, `Snapshot`, `MenuUI`, `JerseyDesigns.Nations10.cs`, `CrosserControl`, `Role.cs`, `.Begin`, `.AttachKickDetectors`, `BoneSpec`, `AccuracyBoard`, `.LaunchBandDive`, `MenuUI`?**
  _High betweenness centrality (0.102) - this node is a cross-community bridge._
- **What connects `Reason`, `Phase`, `SetPieceSpin` to the rest of the system?**
  _178 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Ball Physics & Launch` be split into smaller, more focused modules?**
  _Cohesion score 0.05860805860805861 - nodes in this community are weakly interconnected._
- **Should `Jersey / Nation Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.11901263590949163 - nodes in this community are weakly interconnected._
- **Should `Dribble System` be split into smaller, more focused modules?**
  _Cohesion score 0.09885057471264368 - nodes in this community are weakly interconnected._