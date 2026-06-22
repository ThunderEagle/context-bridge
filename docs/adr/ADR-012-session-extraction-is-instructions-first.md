# ADR-012: Session Extraction Is Instructions-First, Not Command-Driven

## Status

Accepted

## Context

Phase 5 originally planned a `context-bridge extract` command. The command would fire via a Claude Code `Stop` hook at session end, parse the session transcript from `~/.claude/projects/`, call the Anthropic Messages API to identify extractable facts, and write them to ContextBridge via `memory_batch_write`.

During planning, three blocking flaws were identified:

1. **Client coupling.** Claude Code stores transcripts as JSONL under a proprietary path. Parsing this format inside the ContextBridge binary creates a hard dependency on a single client's internal storage format. Any future client change silently breaks extraction. This violates MCP's client-agnostic design principle.

2. **API key unavailability.** Calling the Anthropic Messages API requires an API key. Claude Pro and Max subscribers access Claude through a subscription — no API key is issued. An extraction strategy that requires one is not universally deployable.

3. **Wrong abstraction layer.** The problem — agents not writing memories consistently — is a guidance problem, not a tooling gap. Adding a transcript-scraping fallback compensates for poor instructions rather than improving them. The correct fix is at the instruction layer, not at the CLI layer.

## Decision

No `extract` command will be built. Session memory extraction is the agent's responsibility, directed by two mechanisms:

1. **MCP `instructions` field** — the server returns guidance on every `initialize` response (already implemented in `Program.cs`). Compliant clients inject this into the system prompt automatically; no user configuration is required.

2. **`~/.claude/CLAUDE.md` injection** — `context-bridge configure` appends a `## Context Bridge Memory` section to the user's Claude Code global instructions file. This reinforces the MCP instructions for Claude Code users and persists across sessions regardless of MCP server connectivity.

Both mechanisms are agent-driven: the LLM reads the guidance and calls the memory tools itself. No API key is required. No client-specific binary format is parsed.

## Consequences

- Extraction quality depends on LLM instruction-following. Sessions where the agent ignores the instructions produce no memories. This is the correct tradeoff: improving compliance through better instructions is a tractable problem; maintaining a transcript parser tied to a proprietary format is not.
- Pro and Max subscribers are fully supported.
- No coupling to any client's internal storage format exists in the ContextBridge binary.
- The `configure` command's previously installed `Stop` hook (`context-bridge extract`) is removed. It referenced a command that does not exist and would have fired a failing shell invocation on every session end.
