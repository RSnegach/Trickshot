# Graph Report - Trickshot  (2026-08-26)

## Corpus Check
- 136 files · ~561,856 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 2766 nodes · 7205 edges · 141 communities (122 shown, 19 thin omitted)
- Extraction: 89% EXTRACTED · 11% INFERRED · 0% AMBIGUOUS · INFERRED: 820 edges (avg confidence: 0.8)
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
- .ResetTo
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
- KickDetector
- StadiumStyle
- com.unity.modules.androidjni
- Kyrgyz Sun Emblem (kyrgyz_sun.png)
- Soviet Emblem Sprite
- Bone
- .Set
- .Set
- MenuUI
- PlayerAppearance
- Goal
- AimReticle
- NetPump
- Role.cs
- .StartRebind
- .Begin
- .Build
- HostSetupUI
- SessionBrowserUI
- StatRadar
- SurroundBuilder
- IPlayerController
- BoneSpec
- AccuracyBoard
- JerseyDesigns.Nations4.cs
- .Set
- JerseyDesigns.Nations2.cs
- skyprep.py
- com.unity.nuget.newtonsoft-json
- Turf
- Vector3
- JerseyDesigns.Nations10.cs
- com.unity.modules.screencapture
- JerseyDesigns.Nations6.cs
- PlayerAppearance
- .OnCollisionEnter
- ShotType.cs
- .SetDribbleCarrier
- .ListLobbies
- MultiplayerHubUI
- com.unity.modules.terrain
- StadiumSelectUI
- .ListLobbies
- grassprep.py
- JerseyDesigns.Nations1.cs
- StadiumSelectUI
- Knockdown
- AssetImportRules
- JerseyDesigns.Nations2.cs
- JerseyDesigns.Nations3.cs
- JerseyDesigns.Nations4.cs
- ChatCensor
- JerseyDesigns.Nations5.cs
- postprep.py
- JerseyDesigns.Nations6.cs
- JerseyDesigns.Nations7.cs
- JerseyDesigns.Nations9.cs
- NetBackstop.cs
- .Heavy

## God Nodes (most connected - your core abstractions)
1. `ActiveRagdoll` - 152 edges
2. `Trickshot` - 115 edges
3. `BallController` - 95 edges
4. `NetSession` - 88 edges
5. `CustomizeUI` - 80 edges
6. `NetSetPieceMatch` - 75 edges
7. `Striker` - 65 edges
8. `GameBootstrap` - 62 edges
9. `ScrimmageGame` - 61 edges
10. `GameCamera` - 58 edges

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

## Communities (141 total, 19 thin omitted)

### Community 0 - "Ball Physics & Launch"
Cohesion: 0.06
Nodes (19): Action, bool, Color, Color32, Dictionary, float, Func, GUIStyle (+11 more)

### Community 1 - "Jersey / Nation Designs"
Cohesion: 0.12
Nodes (21): Action, Color32, Dictionary, int, IReadOnlyList, List, string, Texture2D (+13 more)

### Community 2 - "Dribble System"
Cohesion: 0.11
Nodes (9): bool, float, HashSet, int, List, ScrimRole, string, Vector3 (+1 more)

### Community 3 - "Net Messages & Wire Codec"
Cohesion: 0.19
Nodes (4): bool, float, Vector3, Dribble

### Community 4 - "Input & Keybinds"
Cohesion: 0.12
Nodes (14): bool, Camera, float, Func, int, List, Material, Mesh (+6 more)

### Community 5 - "SkillTree"
Cohesion: 0.10
Nodes (12): bool, byte, ConcurrentQueue, Dictionary, float, Func, int, IPEndPoint (+4 more)

### Community 6 - "Goalkeeper AI & Control"
Cohesion: 0.14
Nodes (9): Dictionary, HashSet, IEnumerable, List, Category, SkillTree, Category, Node (+1 more)

### Community 7 - "Skill Icon Drawing"
Cohesion: 0.16
Nodes (7): Color32, Dictionary, float, int, string, Texture2D, SkillIcons

### Community 8 - "AccuracyGame"
Cohesion: 0.08
Nodes (6): JerseyDesigns, JerseyDesigns, Role, ShotType, Trickshot.Net, Trickshot

### Community 9 - "Direct IP Transport"
Cohesion: 0.14
Nodes (5): GameMode, bool, GameObject, Light, GameBootstrap

### Community 10 - "LobbyUI"
Cohesion: 0.19
Nodes (8): Action, bool, float, Vector3, Celebration, Emote, EmotePose, Emote

### Community 11 - "Net Set-Piece Match"
Cohesion: 0.33
Nodes (5): int, string, AdultQuiz, Q, Q

### Community 12 - "BodyLayoutDef"
Cohesion: 0.10
Nodes (22): bool, float, int, string, Vector3, BodyLayout, BodyLayoutDef, BoneSpec (+14 more)

### Community 13 - "SetPieceTaker"
Cohesion: 0.24
Nodes (11): IEnumerable, Color, float, int, Material, PhysicsMaterial, Transform, Vector3 (+3 more)

### Community 14 - "OptionsMenu"
Cohesion: 0.22
Nodes (8): Color, GameObject, Material, Transform, Vector3, JerseyFaces, Make, Shader

### Community 15 - "PrematchUI"
Cohesion: 0.06
Nodes (34): AccessoryEntry, bool, float, int, Material, Matrix4x4, Mesh, Transform (+26 more)

### Community 16 - "Bone"
Cohesion: 0.13
Nodes (11): Action, bool, float, int, List, string, Entry, Kind (+3 more)

### Community 17 - ".Box"
Cohesion: 0.09
Nodes (16): bool, Delivery, float, int, string, Transform, Vector3, FreeplayGame (+8 more)

### Community 18 - "CustomizeUI"
Cohesion: 0.35
Nodes (4): Camera, Material, Refs, Transform

### Community 19 - "DirectIpTransport"
Cohesion: 0.09
Nodes (25): Direct-IP UDP transport path (LAN / Tailscale), DirectIpTransport.cs (direct-IP UDP), Facepunch.Steamworks, Footballer, INetTransport.cs (transport seam), LocalTransport.cs (in-process loopback), Multiplayer.cs (global entry), NetEndpoint.cs (+17 more)

### Community 20 - ".OnGUI"
Cohesion: 0.24
Nodes (7): Color, float, Random, Rect, Vector2, Vector3, SetPieceMap

### Community 21 - "GameInput"
Cohesion: 0.33
Nodes (6): dependencies, depth, source, version, com.unity.modules.jsonserialize, com.unity.modules.jsonserialize

### Community 22 - "Celebration"
Cohesion: 0.09
Nodes (14): float, Transform, Vector3, AimReticle, bool, float, Quaternion, Transform (+6 more)

### Community 23 - "SkillTree"
Cohesion: 0.13
Nodes (10): bool, float, int, Random, string, Vector3, FreeKickGame, Outcome (+2 more)

### Community 24 - "Footballer"
Cohesion: 0.12
Nodes (9): Action, bool, string, BuildAll, Plat, BuildTarget, MenuItem, Plat (+1 more)

### Community 25 - "SteamTransport"
Cohesion: 0.17
Nodes (3): InputFrame, AnimState, Body

### Community 26 - ".Join"
Cohesion: 0.14
Nodes (8): bool, float, Func, Vector3, SetPieceTaker, State, SetPieceSpin, State

### Community 27 - "NetSetPieceMatch"
Cohesion: 0.11
Nodes (10): bool, float, int, List, string, uint, Vector3, Body (+2 more)

### Community 28 - "com.unity.modules.jsonserialize"
Cohesion: 0.17
Nodes (9): Action, bool, Delivery, float, GUIStyle, int, string, Vector3 (+1 more)

### Community 29 - "INetTransport"
Cohesion: 0.20
Nodes (7): bool, float, List, Vector3, Option, Passing, Option

### Community 30 - ".ClientUpdate"
Cohesion: 0.13
Nodes (8): bool, float, int, string, uint, Vector3, Body, NetStrikerMatch

### Community 31 - "Dribble"
Cohesion: 0.22
Nodes (4): SlotKind, Color, string, SpeciesCosmetics

### Community 32 - "Footballer"
Cohesion: 0.25
Nodes (6): bool, float, Vector3, Gait, Profile, Profile

### Community 33 - ".Configure"
Cohesion: 0.20
Nodes (10): float, GameObject, IReadOnlyList, List, Material, PhysicsMaterial, Quaternion, Transform (+2 more)

### Community 34 - "PitchBuilder"
Cohesion: 0.18
Nodes (7): byte, Dictionary, float, List, uint, Pending, ReliableChannel

### Community 35 - ".Empty"
Cohesion: 0.24
Nodes (7): Action, bool, float, Transform, Vector3, Sniper, LineRenderer

### Community 36 - "NetCodec"
Cohesion: 0.08
Nodes (17): bool, Camera, float, GameObject, int, Light, Material, Quaternion (+9 more)

### Community 37 - ".Configure"
Cohesion: 0.16
Nodes (6): float, int, Matrix4x4, Rect, Vector2, MenuScale

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
Cohesion: 0.16
Nodes (12): Action, GUIStyle, Rect, Color, GUIStyle, int, Rect, Texture2D (+4 more)

### Community 43 - "Footballer"
Cohesion: 0.14
Nodes (11): Action, bool, Collider, Color, float, int, Material, Transform (+3 more)

### Community 44 - "Crowd"
Cohesion: 0.11
Nodes (11): Material, Camera, Color, float, int, Light, List, Material (+3 more)

### Community 45 - "SessionBrowserUI"
Cohesion: 0.13
Nodes (9): List, Action, bool, float, int, List, string, ulong (+1 more)

### Community 46 - ".Empty"
Cohesion: 0.16
Nodes (8): bool, float, Func, Quaternion, Vector2, Vector3, KeeperController, State

### Community 47 - ".Box"
Cohesion: 0.05
Nodes (20): Action, Func, int, List, string, ulong, INetTransport, LobbyInfo (+12 more)

### Community 48 - "Goalkeeper"
Cohesion: 0.14
Nodes (8): bool, float, int, Quaternion, Rigidbody, Vector3, Frame, ReplaySystem

### Community 49 - "ShotServer"
Cohesion: 0.20
Nodes (9): Collider, Color, float, GameObject, int, Material, Transform, Vector3 (+1 more)

### Community 50 - ".SetLocalInput"
Cohesion: 0.33
Nodes (6): Color, float, Rect, Vector2, Vector3, CrossMap

### Community 51 - "com.unity.modules.physics"
Cohesion: 0.15
Nodes (13): com.unity.modules.hierarchycore, dependencies, depth, source, version, dependencies, depth, source (+5 more)

### Community 52 - "com.unity.modules.imageconversion"
Cohesion: 0.13
Nodes (8): Color, PlayerAppearance, Vector2, Vector3, NetWriter, ShootoutState, BinaryWriter, MemoryStream

### Community 53 - ".Build"
Cohesion: 0.15
Nodes (16): bool, Vector2, NetInputSource, bool, byte, float, string, uint (+8 more)

### Community 54 - "GameInput"
Cohesion: 0.29
Nodes (3): int, string, QuickChat

### Community 55 - "QuickChat"
Cohesion: 0.11
Nodes (8): Refs, bool, float, Func, Vector3, Striker, Trick, Trick

### Community 56 - "ShotServer"
Cohesion: 0.06
Nodes (31): com.coplaydev.unity-mcp, com.unity.inputsystem, com.unity.modules.androidjni, com.unity.modules.audio, com.unity.modules.particlesystem, com.unity.modules.umbra, com.unity.modules.vectorgraphics, com.unity.multiplayer.center (+23 more)

### Community 57 - "Sniper"
Cohesion: 0.40
Nodes (5): dependencies, depth, source, version, com.unity.modules.androidjni

### Community 58 - "IStrikerInput"
Cohesion: 0.16
Nodes (9): bool, Color, float, int, Material, Transform, Vector3, Crowd (+1 more)

### Community 59 - "MenuUI"
Cohesion: 0.21
Nodes (4): bool, float, Vector3, KeeperHands

### Community 60 - "SimConfig"
Cohesion: 0.16
Nodes (6): float, int, Transform, uint, Vector3, AccuracyBoard

### Community 61 - "SimConfig"
Cohesion: 0.15
Nodes (7): float, int, List, Queue, string, Line, QuickChatFeed

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
Cohesion: 0.19
Nodes (7): bool, float, string, DisplaySettings, RuntimeInitializeOnLoadMethod, FullScreenMode, Resolution

### Community 67 - "CrosserBubble"
Cohesion: 0.08
Nodes (18): PeerId, JoinRefusal, NetRole, bool, byte, Dictionary, float, HashSet (+10 more)

### Community 68 - ".ResetTo"
Cohesion: 0.29
Nodes (3): byte, uint, LobbyProbe

### Community 69 - "AimReticle"
Cohesion: 0.24
Nodes (10): community structure, god nodes, graphify-out/graph.json, graphify-out/GRAPH_REPORT.md, graphify knowledge graph, graphify explain command, graphify path command, graphify query command (+2 more)

### Community 70 - "QuickChat"
Cohesion: 0.33
Nodes (3): NetCodec, NetReader, BinaryReader

### Community 71 - ".SkillPresetButtons"
Cohesion: 0.17
Nodes (12): com.unity.ext.nunit, com.unity.test-framework, dependencies, depth, source, version, dependencies, depth (+4 more)

### Community 72 - ".AdvanceTurn"
Cohesion: 0.13
Nodes (16): Action, List, Action, bool, ConcurrentQueue, float, int, IPAddress (+8 more)

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
Cohesion: 0.17
Nodes (8): bool, float, int, string, uint, Vector3, Body, NetScrimmageMatch

### Community 77 - "com.unity.modules.ai"
Cohesion: 0.38
Nodes (7): Hair Strand Texture Atlas, Dense Wavy Strand Card (Tile 3), Flowing Wavy Strand Card (Tile 2), Four-Column Horizontal Tile Layout, White-on-Black Strand Alpha/Luminance Mask, Straight Sleek Strand Card (Tile 4), Wavy Scattered Strand Card (Tile 1)

### Community 79 - ".Heavy"
Cohesion: 0.14
Nodes (6): bool, RuntimeInitializeOnLoadMethod, Multiplayer, NetPumpRunner, NetPump, NetPumpRunner

### Community 80 - "MenuUI"
Cohesion: 0.17
Nodes (6): bool, float, int, List, Vector3, Footballer

### Community 81 - ".PhysMat"
Cohesion: 0.18
Nodes (4): Dictionary, string, Keybinds, InputAction

### Community 82 - "KickDetector"
Cohesion: 0.14
Nodes (9): bool, float, int, string, KeeperGame, float, int, Vector3 (+1 more)

### Community 83 - "StadiumStyle"
Cohesion: 0.24
Nodes (8): bool, Color, float, int, string, Vector3, StadiumStyle, Surroundings

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
Cohesion: 0.18
Nodes (8): bool, float, int, Quaternion, Vector3, PitchLayout, Seat, Side

### Community 88 - ".Set"
Cohesion: 0.18
Nodes (6): Action, bool, Color, float, string, LobbyUI

### Community 89 - ".Set"
Cohesion: 0.32
Nodes (4): Color, Rect, Vector2, StatRadar

### Community 90 - "MenuUI"
Cohesion: 0.21
Nodes (7): bool, float, int, string, Transform, Vector3, TimeTrialGame

### Community 91 - "PlayerAppearance"
Cohesion: 0.14
Nodes (11): bool, byte, float, string, BodyPlan, HeaderAction, Species, SpeciesAxis (+3 more)

### Community 92 - "Goal"
Cohesion: 0.29
Nodes (4): Action, bool, Collider, Goal

### Community 93 - "AimReticle"
Cohesion: 0.29
Nodes (7): float, int, string, Effect, Node, Preset, Effect

### Community 94 - "NetPump"
Cohesion: 0.07
Nodes (20): Rigidbody, Rigidbody, Rigidbody, bool, byte, Collider, ConfigurableJoint, Dictionary (+12 more)

### Community 95 - "Role.cs"
Cohesion: 0.09
Nodes (13): bool, Camera, Color, float, GUIStyle, int, List, Rect (+5 more)

### Community 96 - ".StartRebind"
Cohesion: 0.22
Nodes (7): int, RebindingOperation, string, Vector2, OptionsMenu, Tab, Tab

### Community 97 - ".Begin"
Cohesion: 0.06
Nodes (22): byte, Vector2, GameInput, Vector2, IStrikerInput, Texture2D, Camera, Material (+14 more)

### Community 98 - ".Build"
Cohesion: 0.30
Nodes (8): float, int, Material, PhysicsMaterial, Transform, Vector3, Refs, ScrimmageArena

### Community 99 - "HostSetupUI"
Cohesion: 0.13
Nodes (9): bool, float, int, string, Transform, Vector3, GameManager, float (+1 more)

### Community 100 - "SessionBrowserUI"
Cohesion: 0.32
Nodes (6): float, int, Material, Transform, Vector3, PitchBuilder

### Community 101 - "StatRadar"
Cohesion: 0.29
Nodes (6): ConfigurableJoint, Quaternion, Rigidbody, Vector3, JointMath, Space

### Community 102 - "SurroundBuilder"
Cohesion: 0.34
Nodes (5): Material, Transform, uint, Vector3, SurroundBuilder

### Community 103 - "IPlayerController"
Cohesion: 0.21
Nodes (7): Action, bool, GUIStyle, int, string, Vector3, HostSetupUI

### Community 104 - "BoneSpec"
Cohesion: 0.33
Nodes (3): Action, bool, MenuUI

### Community 105 - "AccuracyBoard"
Cohesion: 0.08
Nodes (15): bool, Collision, float, int, Rigidbody, Vector3, BallController, BodyTouch (+7 more)

### Community 106 - "JerseyDesigns.Nations4.cs"
Cohesion: 0.20
Nodes (10): bool, Camera, Color, Dictionary, float, Light, Material, string (+2 more)

### Community 108 - "JerseyDesigns.Nations2.cs"
Cohesion: 0.19
Nodes (9): PhysicsMaterial, Material, PhysicsMaterial, Transform, Vector3, Arena, Refs, PhysicsMaterial (+1 more)

### Community 109 - "skyprep.py"
Cohesion: 0.26
Nodes (12): dir_from_pixel(), find_sun(), lum(), main(), Turn the Poly Haven .hdr pureskies into game-ready equirectangular skybox textur, Euler for a directional light whose forward is -d (i.e. shining from d)., Exposure-normalise, roll the highlights off, then gamma-encode to bytes., Decode a Radiance RGBE file to a float32 HxWx3 array of linear radiance. (+4 more)

### Community 110 - "com.unity.nuget.newtonsoft-json"
Cohesion: 0.29
Nodes (7): com.unity.nuget.newtonsoft-json, dependencies, depth, source, url, version, com.unity.nuget.newtonsoft-json

### Community 111 - "Turf"
Cohesion: 0.17
Nodes (8): bool, Color, Dictionary, float, int, Material, Texture2D, Turf

### Community 112 - "Vector3"
Cohesion: 0.40
Nodes (3): bool, UIFont, Font

### Community 113 - "JerseyDesigns.Nations10.cs"
Cohesion: 0.15
Nodes (9): bool, Camera, float, Func, Transform, Vector3, GameCamera, Mode (+1 more)

### Community 114 - "com.unity.modules.screencapture"
Cohesion: 0.33
Nodes (6): com.unity.modules.screencapture, dependencies, depth, source, version, com.unity.modules.screencapture

### Community 115 - "JerseyDesigns.Nations6.cs"
Cohesion: 0.12
Nodes (8): bool, float, int, string, Vector3, AccuracyGame, Phase, Phase

### Community 116 - "PlayerAppearance"
Cohesion: 0.17
Nodes (10): bool, byte, Color, float, int, string, Texture2D, PlayerAppearance (+2 more)

### Community 117 - ".OnCollisionEnter"
Cohesion: 0.33
Nodes (6): com.unity.modules.unitywebrequest, dependencies, depth, source, version, com.unity.modules.unitywebrequest

### Community 119 - ".SetDribbleCarrier"
Cohesion: 0.15
Nodes (8): bool, float, Func, Quaternion, Vector3, Goalkeeper, State, Vector3

### Community 120 - ".ListLobbies"
Cohesion: 0.29
Nodes (4): float, int, Queue, CallLimiter

### Community 125 - "grassprep.py"
Cohesion: 0.60
Nodes (4): load(), main(), member(), Build the turf detail layer in Assets/Resources/Turf from an ambientCG scan.  So

### Community 127 - "StadiumSelectUI"
Cohesion: 0.12
Nodes (10): Collider, float, Transform, CrosserBubble, GoalFrame, Camera, Material, Transform (+2 more)

### Community 128 - "Knockdown"
Cohesion: 0.22
Nodes (3): bool, float, Knockdown

### Community 133 - "ChatCensor"
Cohesion: 0.24
Nodes (3): Dictionary, string, ChatCensor

## Knowledge Gaps
- **166 isolated node(s):** `Reason`, `Phase`, `SetPieceSpin`, `Emote`, `Stage` (+161 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **19 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trickshot` connect `AccuracyGame` to `Jersey / Nation Designs`, `Net Messages & Wire Codec`, `Input & Keybinds`, `Goalkeeper AI & Control`, `LobbyUI`, `Net Set-Piece Match`, `BodyLayoutDef`, `SetPieceTaker`, `OptionsMenu`, `PrematchUI`, `Bone`, `.Box`, `.OnGUI`, `Celebration`, `SkillTree`, `Footballer`, `.Join`, `INetTransport`, `Dribble`, `Footballer`, `.Configure`, `.Empty`, `NetCodec`, `.Configure`, `Footballer`, `Crowd`, `.Empty`, `Goalkeeper`, `ShotServer`, `.SetLocalInput`, `GameInput`, `IStrikerInput`, `MenuUI`, `SimConfig`, `com.unity.modules.ui`, `com.unity.modules.adaptiveperformance`, `.Tick`, `MenuUI`, `.PhysMat`, `KickDetector`, `StadiumStyle`, `Bone`, `.Set`, `MenuUI`, `PlayerAppearance`, `Goal`, `.StartRebind`, `.Begin`, `.Build`, `HostSetupUI`, `SessionBrowserUI`, `StatRadar`, `BoneSpec`, `JerseyDesigns.Nations4.cs`, `JerseyDesigns.Nations2.cs`, `Turf`, `Vector3`, `JerseyDesigns.Nations10.cs`, `JerseyDesigns.Nations6.cs`, `PlayerAppearance`, `.SetDribbleCarrier`, `.ListLobbies`, `MultiplayerHubUI`, `StadiumSelectUI`, `.ListLobbies`, `JerseyDesigns.Nations1.cs`, `StadiumSelectUI`, `Knockdown`, `AssetImportRules`, `JerseyDesigns.Nations2.cs`, `JerseyDesigns.Nations3.cs`, `JerseyDesigns.Nations4.cs`, `ChatCensor`, `JerseyDesigns.Nations5.cs`, `JerseyDesigns.Nations6.cs`, `JerseyDesigns.Nations7.cs`, `JerseyDesigns.Nations9.cs`, `NetBackstop.cs`?**
  _High betweenness centrality (0.193) - this node is a cross-community bridge._
- **Why does `NetSession` connect `CrosserBubble` to `.Begin`, `ChatCensor`, `QuickChat`, `Snapshot`, `.Box`, `.Heavy`, `com.unity.modules.imageconversion`, `.Build`, `ShotType.cs`, `.Set`, `SteamTransport`, `NetSetPieceMatch`, `SimConfig`, `.ClientUpdate`?**
  _High betweenness centrality (0.128) - this node is a cross-community bridge._
- **Why does `ActiveRagdoll` connect `NetPump` to `Knockdown`, `Dribble System`, `Net Messages & Wire Codec`, `AccuracyGame`, `LobbyUI`, `BodyLayoutDef`, `OptionsMenu`, `.Box`, `CustomizeUI`, `Celebration`, `SkillTree`, `.Join`, `NetSetPieceMatch`, `INetTransport`, `.ClientUpdate`, `Footballer`, `NetCodec`, `Crowd`, `.Empty`, `ShotServer`, `QuickChat`, `MenuUI`, `Snapshot`, `.Tick`, `MenuUI`, `KickDetector`, `MenuUI`, `PlayerAppearance`, `Role.cs`, `.Begin`, `HostSetupUI`, `AccuracyBoard`, `JerseyDesigns.Nations10.cs`, `JerseyDesigns.Nations6.cs`, `PlayerAppearance`, `.SetDribbleCarrier`, `StadiumSelectUI`?**
  _High betweenness centrality (0.106) - this node is a cross-community bridge._
- **What connects `Reason`, `Phase`, `SetPieceSpin` to the rest of the system?**
  _166 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Ball Physics & Launch` be split into smaller, more focused modules?**
  _Cohesion score 0.05760905760905761 - nodes in this community are weakly interconnected._
- **Should `Jersey / Nation Designs` be split into smaller, more focused modules?**
  _Cohesion score 0.11901263590949163 - nodes in this community are weakly interconnected._
- **Should `Dribble System` be split into smaller, more focused modules?**
  _Cohesion score 0.10931174089068826 - nodes in this community are weakly interconnected._