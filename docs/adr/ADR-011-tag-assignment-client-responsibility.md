# ADR-011: Tag Assignment as Client Responsibility

**Date:** 2026-06-21  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

Memories support one or more freeform tags. Tags serve as the unified model for both classification (`type:decision`) and scoping (`project:context-bridge`). The question is who assigns tags: the server (infers from content), the client (agent applies based on task context), or a hybrid.

Two options were evaluated:

**Server-side inference** — the server analyzes memory content using heuristics or an LLM call to assign tags automatically. The agent passes content; tags come back.

**Client-driven assignment** — the agent assigns tags when calling `memory_write` or `memory_batch_write`. The server stores whatever the client provides with no validation or enforcement. Tagging conventions are communicated to the agent via `InitializeResult.instructions`.

## Decision

Tags are **assigned by the client agent**, not inferred by the server.

The agent has context the server never will: which repository is open, what task is being worked on, the full conversation history that produced the memory. The server sees only a content string. Content-based inference of `project:context-bridge` is impossible without being told the current project. Inference of `type:decision` from linguistic patterns is unreliable and would produce inconsistent results across content styles.

The `InitializeResult.instructions` field (returned in the MCP `initialize` response) carries tagging conventions to the agent:

> "Apply tags when writing memories: use `project:<repo-name>` for the current project, `type:decision|preference|pattern|reference` for classification. Apply multiple tags where relevant."

All compliant MCP clients (Claude Code, Claude Desktop, Cursor, Codex) inject this string into the system prompt automatically. No user configuration is required.

The server stores whatever tags the agent provides — no validation, no normalization beyond trimming whitespace, no enforcement.

## Consequences

### Positive
- No LLM call on the write path (content analysis for tag suggestion would add ~200–500ms per write)
- Tags reflect agent intent rather than server inference — higher accuracy for scope tags like `project:context-bridge`
- Server remains stateless with respect to tag semantics — adding new tag conventions requires no server changes
- `InitializeResult.instructions` is a standard MCP mechanism supported by all target clients

### Negative
- Tag compliance depends on the agent following instructions; non-compliant or instruction-ignoring agents produce untagged memories
- Tag inconsistency across agents (e.g., `project:context-bridge` vs `project:ContextBridge`) is possible — no normalization enforced
- Search by tag (`memory_list` tag filter) is only useful if tagging is consistent

### Neutral / Trade-offs
- v1 tag filter is in `memory_list` (pagination) only; `memory_search` does not pre-filter by tag. Tag-aware search is a v2 concern, deferred until tag compliance data exists from real usage.
- If agent tag compliance proves inconsistent in practice, a `tag_suggest` tool (server returns suggested tags, agent accepts or overrides) can be added in v2 without changing the `memory_write` interface

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| Server-side heuristic inference | Cannot infer scope tags (project, repo) from content alone; unreliable for type classification |
| Server-side LLM inference (call Claude inside the MCP tool) | Adds latency + cost to every write; requires an API key in the server process; adds a dependency that breaks the zero-dependency install story |
| Hybrid (server suggests, client confirms) | Extra round-trip on every write; overhead not justified until tag compliance proves to be a problem |

## References
- [MCP InitializeResult specification](https://modelcontextprotocol.io/specification/2025-03-26/basic/lifecycle#initialization)
- ADR-009: Pure vector search (v1 tag filter scope limited to `memory_list`)
- DESIGN.md §Memory Tagging for tag convention documentation
