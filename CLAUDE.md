## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## Project notes

Workflow
- Compile check without the editor: the open editor holds `Temp/UnityLockfile`, so batchmode is out. `dotnet <sdk>/Roslyn/bincore/csc.dll -target:library -langversion:9.0 -recurse:Assets/Scripts/*.cs` referencing every `Editor/Data/Managed/UnityEngine/UnityEngine*.dll`, `Editor/Data/NetStandard/ref/2.1.0/netstandard.dll` and `Library/ScriptAssemblies/Unity.InputSystem.dll` builds the whole runtime assembly (Unity 6000.4.1f1 under `C:/Program Files/Unity/Hub/Editor`). Steam code is behind `TRICKSHOT_STEAM`, so no Steamworks reference is needed.
- Scripts are CRLF (`core.autocrlf=true`); keep them CRLF when writing files programmatically.
- "Add to ideas" means a bullet under `## Ideas so far` in `DESIGN_NOTES.md`, ending with its "just an idea, not being built yet" line. Record it; don't build it.

Networking
- `BodyState` snapshot records are fixed-stride with no per-record length, so any per-body wire field forces a `NetCodec.ProtocolVersion` bump (v7 added `erect`). Fields appended AFTER the body loop (e.g. `guided`) are read with `r.More` and need no bump. `InputFrame`'s trailing `bits2` byte is the extension point for new held buttons (bit 4 = thirdLeg).
- A held input bit must be written onto the body every tick (not on edges): the host re-feeds a quiet client's last frame forever, so only a level write lets a release land.

Ragdoll / cosmetics
- `Bone` is a fixed 13-slot enum shared by every body plan; ~47 `Bone.Count` loops (balance, mass, poses, gait, emotes, replay) assume every slot is body mass. Optional parts (hair, the adult appendage) are Verlet cosmetics parented to a bone, not bones.
- `ActiveRagdoll.IsGrounded` is a pelvis sphere-cast: it reads grounded before touchdown and flickers for a few frames while a landed body settles. Gate air-only input on jump/landing edges (see Striker's `_wheelArmed` / `_airHold`), never on the raw flag.
- The adult hitbox is a compound `CapsuleCollider` under the pelvis rigidbody, adopted via `ActiveRagdoll.RegisterExtraCollider`; self-collision ignores are re-applied on every enable (`IgnoreOwnCollisionsWith`) because PhysX refuses `IgnoreCollision` on a disabled collider. `BoneOf` returns null for it by design; the ball resolves it through `AnatomySim.IsHitbox`.
- `HairSim` uploads mesh vertices in `LateUpdate` behind `_meshDirty`; `Build` must upload vertices BEFORE assigning triangles or Unity rejects the triangles and every card is invisible (static styles never dirty the mesh at all).
- Replays (`ReplaySystem`) record position, rotation AND local scale; `ReplaySystem.TrackBody` adds a body's bones plus its `AnatomySim` pieces and pauses that sim as a driver. Re-run `Setup` after any tracked body is rebuilt.

Striker-mode crosser (MP)
- The crosser body is a child `Body` object of the `Crosser` and is rebuilt per seat holder by `NetStrikerMatch.RebuildCrosserBody` (a human's roster look, or the orange AI); `Crosser.SetRagdoll` re-points the driver. Single-player's `BuildStrikerMode` still puts the ragdoll on the Crosser object itself, so never destroy `Crosser.Ragdoll.gameObject` without checking it is not the Crosser's own.
- The host's crosser dropdown reads the roster (humans always, AI seats while a human crosses; an AI pick is `NetSession.AssignCrosserAi`, a seat swap). The open list is modal and opens downward: IMGUI gives an overlapping click to the first-drawn control, which is how the AI sliders once ate every pick.

## Session Clearing Protocol

Trigger phrases (from the user, in any phrasing): "about to clear", "clearing", "I'm going to clear", "clear soon", or any other indication the user is about to clear/reset the Claude Code conversation.

When triggered, perform a knowledge preservation pass automatically, without asking for confirmation, before the session is cleared. Do not merely acknowledge the clear — do the pass.

**A. Preserve durable project knowledge.** Review the current session for knowledge genuinely useful across *future* Trickshot sessions: architecture and subsystem ownership, important class/file relationships, non-obvious execution paths, project-specific conventions, design assumptions, fragile dependencies or execution-order requirements, physics/networking constraints, recurring gotchas, repository-specific commands/workflows, decisions future agents should consistently follow. Merge only this into this CLAUDE.md.

Rules for the merge:
- Preserve all existing content; do not overwrite unrelated sections.
- Do not duplicate information already present.
- Keep additions concise, high-signal, short factual bullets.
- Never turn this file into a session diary or log of what happened.
- Never save temporary debugging detail or speculation/unverified assumptions.
- Never save facts trivially rediscoverable from source (file lists, obvious structure).
- Correct existing content only when the session definitively proved it wrong.
- Keep the file lean — prune superseded bullets rather than letting it grow indefinitely.

**B. Preserve current task state.** If the active task is unfinished, mid-debug, or otherwise carries short-term context that would be expensive to reconstruct after clearing, write/update `docs/claude-handoff.md` with only: current task; desired end state / acceptance criteria; what's already implemented or changed; key files/classes/methods involved; current build/test/runtime state; known bugs or open questions; approaches already tried that failed, and why; decisions already made; exact recommended next steps. Keep it tight enough that a fresh session can resume cold.

If the task is fully complete with nothing transient to preserve, delete an obsolete `docs/claude-handoff.md` instead of leaving stale instructions behind.

**C. Verify before finishing.** Re-read the changes. Confirm: speculation wasn't saved as fact; durable knowledge landed in CLAUDE.md and only transient task state landed in the handoff doc; existing content survived; nothing verbose or redundant was added; no stale handoff remains for a completed task.

**Report back, briefly:** what durable knowledge was preserved, what task state was preserved, which files were touched, and whether it's safe to clear.

**Scope guard:** this pass may only edit this CLAUDE.md and `docs/claude-handoff.md`. Never touch gameplay code, assets, scenes, prefabs, or project configuration during a preservation pass unless separately asked.
