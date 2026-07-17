# Trickshot

A 3D trick-shot football prototype in Unity 6. A crosser floats a ball into the
box, and a striker (a controllable **active ragdoll**) has to physically time a
**bicycle kick** past a keeper. First playable: prove that aiming a cross at a
player who must coordinate a bicycle kick is fun.

Built for **Unity 6000.4.1f1** (Unity 6.2), built-in render pipeline, new Input
System, keyboard + mouse, single player.

## Run it

1. Open the project in Unity Hub (point it at this folder). First open imports
   the Input System package; if prompted to enable the new input backends, click
   **Yes** (the project is already set to *Both*, so no restart is needed).
2. Open `Assets/Scenes/Main.unity` (or any scene) and press **Play**.

The whole game builds itself at runtime from `GameBootstrap`
(`[RuntimeInitializeOnLoadMethod]`), so there is nothing to wire in the scene and
it works from an empty scene too.

## Controls

| Action | Input |
| --- | --- |
| Move / look | **WASD** + **Mouse** (Minecraft third person: mouse turns you, A/D strafe) |
| Jump | **Space** |
| Diving header | **W + Space** (dive forward, land belly-down) |
| Raise left / right leg | **Left Mouse** / **Right Mouse** (hold) |
| Recline backward (airborne) | **E** (hold) — bicycle-kick setup, lands on the back |
| Ball cam | **V** (toggle) |
| Reset | **R** |

Crosses are served automatically to a fixed spot every few seconds; you only
control the striker. To score a trick: jump into the cross, hold **E** to recline
onto your back, and hold a leg (**LMB/RMB**) into the ball while tipped back — or
run in with **W + Space** for a diving header. Goals trigger a slow-motion sports
replay.

The camera follows the striker and turns with you: run into the box, turn away
from goal to line up, and trigger the bike. Toggle **ball cam** (V) to bias the
aim toward the incoming ball and widen the view as it approaches, so you can read
the cross's depth and height while still keeping your bearings. On a goal or a
clean trick the view cuts to a slow-motion broadcast replay, then resets.

## How it works

- **Active ragdoll** (`ActiveRagdoll.cs`): two skeletons. A target skeleton
  (invisible transforms) holds the intended pose; the physics skeleton is rigid
  parts joined by `ConfigurableJoint`s whose slerp drives chase the target. While
  grounded the pelvis is hard-locked upright (pitch/roll frozen) so the striker
  cannot fall over; jumping or reclining releases the lock so the body can leave
  the ground and tip. Joint target rotations use the standard
  `SetTargetRotationLocal` conversion (`JointMath.cs`).
- **Poses** (`RagdollPose.cs`): stand / load / bicycle, as per-bone local rotation
  offsets that blend, plus additive per-bone overrides for the run cycle, leg
  raises, and recline.
- **Trick detection** (`KickDetector.cs`): a leg-ball contact only counts as a
  bicycle kick if it lands while reclining **and** the torso is tipped well back;
  otherwise it is just a physical knock.
- **Auto serve** (`Crosser.cs` + `BallController.cs`): a projectile solve puts the
  ball on a fixed spot in the box in a set time of flight, served on a timer.
- **Camera** (`GameCamera.cs`): mouse-orbit follow (cursor locked centre) with a
  ball-lock toggle for play, diagonal broadcast for replays, owns the slow-motion
  `Time.timeScale`.

## Scope / not yet

First-playable only: greybox arena, one trick (bicycle), a static-ish keeper, no
networking, no split-screen, no modeled humans. Scorpion / diving header / volley,
role-specific cameras, and local multiplayer via `PlayerInputManager` are the next
steps (a `PlayerInput` is already attached as the seam for that).

## Note

The project lives on an OneDrive-redirected Desktop. `Library/` is gitignored and
should not sync; if Unity ever complains about locked files, pause OneDrive sync
while the editor is open.
