## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

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
