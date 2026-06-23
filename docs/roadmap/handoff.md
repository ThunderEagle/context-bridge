# Handoff

## Purpose

Allow a user to capture the current state of a working session so it can be resumed in a future session — hours, days, or a few days later — without reloading conversation history or re-establishing context from scratch.

---

## What a Handoff Is (and Isn't)

A handoff is **ephemeral session state**, not a memory. Memories are durable facts — decisions, preferences, patterns — that remain true indefinitely. A handoff is a snapshot of "where I was and what I was thinking," scoped to a specific moment in active work. Once the session is resumed and the handoff consumed, it has served its purpose and should not persist.

Handoff content does not convert to memories automatically. If something from the resumed session rises to the level of a genuine memory, the model writes that through normal `memory_write` calls.

---

## Lifecycle

1. **Create** — the model generates a summary of current state and stores it as a handoff with a TTL
2. **Survive** — the handoff persists through the gap between sessions (hours to a few days)
3. **Surface** — on session start, the handoff is returned via `memory_search` alongside regular memories; the model incorporates it as session-opening context
4. **Acknowledge** — the model deletes the handoff record after processing it
5. **Expire** — TTL acts as a silent fallback if acknowledgement never happens (session crash, abandoned work)

---

## Expiration

- Default TTL is generous (7 days) to cover "a few days" resumption scenarios
- TTL should be configurable at creation time
- Explicit acknowledgement (delete after processing) is the primary cleanup path; TTL is the backstop
- Delete-on-retrieve is explicitly avoided: retrieval and successful incorporation are not the same moment, and silent loss of handoff context on session crash is an unacceptable failure mode

---

## Delivery Mechanisms

Two mechanisms, layered for broad client compatibility:

**Server instructions (90% path)** — standing behavior injected via the MCP `initialize` response. Any MCP client that respects server instructions will follow handoff create/resume behavior without client-specific configuration. This is the baseline.

**MCP `prompts` capability (protocol path)** — a named prompt template exposed via `prompts/list` / `prompts/get`. Clients that implement the MCP prompts capability can surface this as a slash command or menu item, giving users an explicit trigger without relying on the model to infer intent. Clients that don't implement prompts fall back to the instructions path transparently.

---

## Out of Scope (for this design)

- Multi-session or multi-device handoff sharing (delete-on-acknowledge means first resumption wins)
- Handoff diffing or versioning
- Automatic handoff creation on session end (requires client lifecycle hooks not available in MCP)
- Conversion of handoff content to permanent memories
