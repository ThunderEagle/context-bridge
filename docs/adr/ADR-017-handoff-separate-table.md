# ADR-017: Handoff Storage as a Separate Table

## Status

Accepted

## Context

The handoff feature allows a model to capture ephemeral session state ("where I was, what I was doing") so a future session can resume without reloading conversation history. The initial design (`docs/roadmap/handoff.md`) proposed storing handoffs in the existing `memories` table and surfacing them via `memory_search` alongside regular memories.

Two approaches were evaluated:

**Option A — Unified table (memories + expires_at column)**  
Add a nullable `expires_at` column to `memories`. Handoffs are stored in the same table and vector index (`memories_vec`), surfacing naturally in `memory_search` results. The model detects them by the presence of `expires_at` in the response.

**Option B — Separate `handoffs` table**  
Store handoffs in a dedicated table with no embedding. The model retrieves them via an explicit `handoff_list` tool call, filtered by project.

## Decision

**Option B — Separate `handoffs` table.**

## Rationale

A handoff is a project-scoped blob, not a semantic document. The lookup question is "does this project have an active handoff?" — a filter, not a similarity query. Semantic search adds cost (embedding generation on every write) with no benefit when the retrieval criterion is already exact.

The unified-table approach pollutes the memory system:
- `memory_list` and `memory_status` would need `AND expires_at IS NULL` guards to exclude handoffs
- `memory_delete` and `memory_update` would need `AND expires_at IS NULL` guards to prevent inadvertently touching handoffs
- Search results would require an `expires_at` field in the response for the model to distinguish records, and the server instructions would rely on the model inferring what to do with that signal — an implicit contract

The separate table approach is explicit:
- All existing memory tools (`memory_search`, `memory_list`, `memory_delete`, `memory_update`, `memory_status`) are unchanged
- Handoffs have their own tools with clear semantics: `handoff_write`, `handoff_list`, `handoff_acknowledge`
- No embedding is generated or stored — `IEmbeddingGenerator` is not a dependency of `HandoffMcpTools`
- Simpler schema: no `is_deleted` column on `handoffs` (hard delete is used; see below)

## Hard Delete on Acknowledge

Handoffs use hard deletes (`DELETE FROM handoffs WHERE id = @id`) rather than soft deletes. Rationale:

The model reads handoff content via `handoff_list` before calling `handoff_acknowledge`. After acknowledgement, the context has been incorporated — there is nothing to recover. Soft delete would accumulate acknowledged handoffs indefinitely because the TTL purge targets `expires_at < @now`, not `is_deleted = 1`, requiring a second cleanup pass with no operational benefit.

The TTL is the correct recovery mechanism for the failure case: if `handoff_acknowledge` is never called (session crash between list and acknowledge), the handoff survives until expiry and surfaces again in the next session's `handoff_list` call.

## Consequences

- `IHandoffRepository` is a new interface in `ContextBridge.Core` — clean DI with no coupling to memory internals
- Schema migration v2 creates the `handoffs` table and a partial index on `project`
- The `resume-session` MCP prompt supplements server instructions for clients that surface prompts as slash commands
- `memory_search` does not return handoffs — this is an intentional contract, not an omission
