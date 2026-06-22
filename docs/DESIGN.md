# ContextBridge — Project Backlog Summary

**Status:** Backlog / Pre-concept  
**Date:** June 17, 2026  
**Category:** Developer Tooling / AI Infrastructure

---

## Problem Statement

AI coding assistants are proliferating faster than any single vendor can keep up with. Developers increasingly use two or three simultaneously — Claude Code for reasoning, a local model for cost/privacy, Cursor for a specific workflow. None of them share context. 

Within each tool, AI assistants are stateless between sessions by default. Each session starts from zero with no memory of decisions made, patterns established, or context accumulated across previous work. Current mitigations — CLAUDE.md files, manual context pasting — are static, manually curated, and scoped to individual repositories. There is no mechanism for knowledge to travel with the developer across multiple repos, multiple AI assistant instances, or between different coding assistants simultaneously.

Existing MCP memory server implementations (MihaiBuilds/memory-vault, fusae/Memory-Vault) solve the concept but require heavy dependencies (Docker, PostgreSQL, Python environments, Ollama as a separate server process) that create significant friction and limit the audience to developers and tinkerers willing to manage additional infrastructure.

**The core insight:** No single vendor has incentive to solve cross-vendor memory sharing. Context-Bridge is vendor-neutral MCP infrastructure that gives all of them a shared memory layer with semantic retrieval, not context blob injection.

---

## Proposed Solution

A Windows-first, self-contained MCP memory server that requires zero external dependencies to install and run. Ships as a standard Windows installer (.msi) or dotnet global tool. Bundled embedding model runs in-process via ONNX Runtime — no Ollama, no Python, no Docker, no separate server process required.

Runs as a Windows Service so all concurrent clients (multiple VS Code instances, Claude Desktop, Cowork) share a single memory store and a single running process with the model loaded once and warm.

---

## Key Differentiators

- **Zero external dependencies** — no Docker, no Postgres, no Python runtime, no Ollama server
- **In-process embeddings** — ONNX Runtime runs the embedding model directly inside the service process
- **Windows Service model** — one process, model loaded once, all concurrent sessions share real-time memory state
- **Simple distribution** — .msi installer or `dotnet tool install -g context-bridge`
- **Single-file storage** — SQLite in AppData, backup is just copying a file
- **Broader audience** — non-developers can install and use this; not just developers willing to manage infrastructure

---

## Design Philosophy

**Installation should be an event, not a project.**

The target experience is a standard installer wizard or a single command — next, next, finish, done. The tool works immediately with no additional setup. No prerequisites to install first, no services to configure, no models to download separately, no environment variables to set, no README to follow step by step.

This is a deliberate product decision, not a consequence of technology choices. Every dependency that leaks into the user's environment is a support problem, a friction point, and a reason someone doesn't finish installing. The existing tools in this space (Docker + PostgreSQL, Ollama + Node, Python virtual environments) treat setup complexity as acceptable because they're built by developers for developers who are comfortable managing infrastructure. This project treats setup complexity as a bug.

The Windows Service / Mac daemon / Linux systemd model is a direct expression of this philosophy. A proper system service starts automatically, runs in the background, survives reboots, and is managed through standard OS tooling. It is the opposite of "open a terminal and run this command every time you want to use it."

---

## Architecture Decisions

### Language / Runtime
**.NET Worker Service**

- First-party Windows Service integration via `UseWindowsService()`
- `Microsoft.ML.OnnxRuntime` is Microsoft-maintained, well-documented, ships native binaries for all target platforms
- `Microsoft.Extensions.AI` provides clean embedding provider abstraction
- `dotnet tool` and self-contained publish solve distribution cleanly
- Go considered and rejected — better fit for cloud-native/containerized CLI tools, weaker ONNX ecosystem, no advantage for Windows Service scenario

**Why not Python:**
Python is where the ML/AI ecosystem lives and the path of least resistance for a quick prototype. But it has no credible answer to the distribution problem — the install story inevitably becomes Docker, or a Python runtime dependency, or a virtual environment the user has to manage. The existing Python-based tools in this space confirm this: MihaiBuilds/memory-vault requires Docker Desktop and PostgreSQL. That is the natural endpoint of Python for a tool targeting non-developer users on Windows. Python is the right choice if you are doing sophisticated ML operations (fine-tuning, custom preprocessing, HuggingFace integration). Running a pre-trained ONNX model does not qualify.

**Why not Node/TypeScript:**
The MCP TypeScript SDK is mature and well-documented, and sqlite-vec has good Node bindings. For a quick developer-facing CLI it is a reasonable choice. But the Windows Service story is weak, self-contained binary distribution via pkg is hacky compared to dotnet publish, and native binary handling for sqlite-vec and any other native dependencies is more complex to get right. fusae/Memory-Vault is the natural endpoint of this stack — functional, but requires Ollama running as a separate server process and has no install story beyond cloning the repo and running setup scripts.

**The common thread:** Both Python and Node are optimized for "runs on a developer's machine where the environment is already configured." Neither is optimized for "installs cleanly on any Windows machine with no prerequisites." That gap is the product opportunity.

### Storage
**SQLite via Microsoft.Data.Sqlite + sqlite-vec extension**

- Zero server, zero administration, single file
- Ships in-process with .NET — no additional runtime dependency
- sqlite-vec extension provides native vector similarity search at the database layer
- Sufficient for personal memory scale (thousands to low tens of thousands of records)
- Postgres rejected — heavyweight for the problem, requires Docker or local install, no meaningful advantage at this data scale

### Memory Tagging

Memories support one or more freeform tags. Tags are the unified model for both classification and scoping — no separate type/scope columns needed.

**Schema:** Normalized junction table, not a JSON array column. Tag filtering as a pre-step before vector search requires clean SQL — a `memory_tags` join is efficient; parsing JSON arrays in a WHERE clause is not.

```
memories          tags              memory_tags
----------        ----------        ---------------
id                id                memory_id (FK)
content           name              tag_id (FK)
embedding
created_at
```

**Conventions (not enforced, documented for LLM instruction):**
- `project:context-bridge` — project/repo scope
- `type:decision`, `type:preference`, `type:pattern` — memory classification
- Any freeform tag is valid — agents apply what makes sense

**Tag assignment — client-driven via instructions:**

Tag assignment is left to the agent, not the server. This is the right call architecturally: the agent has context the server never will — what repo is open, what task is being worked on, the full conversation that produced the memory. The server only sees the content string. It could infer `type:decision` from content analysis but cannot reliably infer `project:context-bridge` without being told.

The `InitializeResult.instructions` field carries the tagging convention to the agent:

> "When calling `memory_write`, apply tags using these conventions: `project:<repo-name>` for the current project, `type:decision|preference|pattern|reference` for classification. Apply multiple tags where relevant."

Server stores whatever tags the agent provides — no validation, no enforcement, no server-side inference in v1.

**Phasing:**
- **v1** — tags captured and stored; `memory_write` and `memory_batch_write` accept optional tags array; `memory_search` does not filter on them yet; tagging driven entirely by agent instructions
- **v2** — tag-aware search: filter or weight results by tag; if agent tag compliance proves inconsistent in practice, add a `tag_suggest` tool (server analyzes content, returns suggested tags, agent accepts or overrides) — but this is a round-trip cost on every write, so add it only with evidence of the problem

### Vector Search
**Pure vector search via sqlite-vec**

- `all-MiniLM-L6-v2` embedding model — use INT8 quantized ONNX export (~22MB, 384-dimension vectors); negligible quality loss for embedding tasks vs FP32 (~88MB). Source: [Optimum ONNX exports on HuggingFace](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/tree/main/onnx)
- Model loaded once at service start, stays warm in memory
- Pure vector similarity search via sqlite-vec — no hybrid keyword/vector merging
- sqlite-vec ships pre-built native binaries per platform (win-x64, osx-arm64, linux-x64), bundled with installer

**Why pure vector, not hybrid:**
Hybrid search (vector + FTS5 keyword matching merged via RRF) exists to compensate for weak embedding models operating on long, sparse documents. Neither condition applies here. Memories are short, semantically dense snippets where a good embedding model performs well. More importantly, search queries are generated by Claude, not typed by a human — Claude will naturally produce semantically appropriate queries regardless of exact wording, making keyword fallback redundant. Hybrid search would add complexity and pipeline noise without solving a real problem in this use case.

**Why sqlite-vec over in-process linear scan:**
An in-process cosine similarity scan across all vectors is a linear operation — every query touches every record. sqlite-vec keeps search at the database layer, enables ANN (approximate nearest neighbor) indexing for future scale, and is the architecturally correct place for this operation rather than pulling data into application memory to compute what the database should be doing.

### MCP Transport & Service Model
**Streamable HTTP (MCP spec 2025-03-26)**

The Streamable HTTP transport replaces the original two-endpoint HTTP+SSE transport (deprecated). Single endpoint, client-initiated requests, server can respond inline or upgrade to an SSE stream for long-running operations. This is the current transport implemented by Claude Desktop and Claude Code.

**Dual transport: HTTP for Claude Code, stdio for Claude Desktop**

The service runs two transport modes from the same binary:

- **HTTP service (Windows Service, `context-bridge` with no args):** Streamable HTTP on `127.0.0.1:5290`. Single persistent process. Claude Code connects here. Multiple Claude Code sessions hit the same process, the same SQLite connection, and see immediately-consistent memory state.

- **stdio subprocess (`context-bridge stdio`):** Spawned by Claude Desktop as a child process, communicating via stdin/stdout. Minimal generic host — no Kestrel, no Windows Service registration. Shares `memories.db` with the HTTP service; SQLite WAL mode ensures safe concurrent multi-process access. Memories written from Desktop are immediately visible to Claude Code and vice versa.

The HTTP service remains the right answer for Claude Code: a single process with the ONNX model loaded once and warm, serving all concurrent sessions. The stdio path accepts a per-session ONNX load (~500ms) as the cost of Claude Desktop compatibility. For Desktop usage patterns (typically one session at a time), this is acceptable.

See ADR-010 (HTTP transport rationale) and ADR-016 (stdio transport for Claude Desktop).

### Embedding Provider Abstraction
**`IEmbeddingGenerator<string, Embedding<float>>` from Microsoft.Extensions.AI**

Use MEA's built-in interface directly — rolling a custom `IEmbeddingProvider` is redundant. MEA already provides the abstraction, ships built-in implementations for Ollama and OpenAI-compatible endpoints, and integrates with the standard DI pipeline and middleware (caching, OpenTelemetry). A custom interface would bypass all of that for no gain.

```csharp
// Register in DI — no custom interface needed
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new BundledOnnxEmbeddingGenerator(modelPath));
```

Implementations:
- `BundledOnnxEmbeddingGenerator` — custom, wraps ONNX Runtime; default, works out of the box (v1)
- `OllamaEmbeddingGenerator` — MEA built-in; for users already running Ollama (later phase)
- `OpenAIEmbeddingGenerator` — MEA built-in; covers OpenAI-compatible endpoints (later phase)

### Cross-Platform Abstraction
Windows Service in v1, abstracted at the host layer:

```csharp
if (OperatingSystem.IsWindows())
    builder.UseWindowsService();
else if (OperatingSystem.IsLinux())
    builder.UseSystemd();
// Mac: launchd plist shipped as install artifact
```

Core service code is platform-agnostic. ONNX Runtime and SQLite both ship cross-platform native binaries via NuGet RID system.

---

## Security

### Threat Model

This is a personal tool running on a single-user machine. The realistic threat is another local process reading or writing your memories — not a remote attacker. The design addresses that threat without overstating the security posture.

### Network Binding

The service binds exclusively to `127.0.0.1` (never `0.0.0.0`). This is non-negotiable — it ensures the service is unreachable from outside the machine regardless of firewall state.

### Authentication

None. The security perimeter is exclusively the Kestrel bind address (`127.0.0.1`). See ADR-015 for the full rationale and future options (named pipe / Unix socket if MCP clients add support).

### Client Configuration

`context-bridge configure` detects known MCP client config file locations and writes the appropriate transport entry:

- **Claude Code** (`~/.claude/settings.json` via `claude mcp add`): HTTP transport at `http://127.0.0.1:5290/mcp`
- **Claude Desktop** (`%APPDATA%\Claude\claude_desktop_config.json`): stdio transport — spawns `context-bridge stdio` as a child process

---

## Service Installation & Management (v1)

v1 avoids installer complexity by deferring service registration to explicit CLI commands. Users control the setup flow with clear, discoverable operations.

### Distribution & Installation

Two options:
- **`dotnet tool install -g context-bridge`** — for developers with .NET SDK installed
- **GitHub Releases .exe download** — for users without SDK; one-time SmartScreen warning on first run

Both produce the same `context-bridge` executable on the system PATH.

### Service Registration via CLI

Service operations require **admin privileges** (user must run from admin PowerShell/Command Prompt):

```bash
context-bridge service install    # Register as Windows Service, start immediately
context-bridge service start      # Start if stopped
context-bridge service stop       # Stop the running service
context-bridge service uninstall  # Stop and remove service registration
context-bridge service status     # Health check + current state
```

On failure (e.g., run without admin), commands print clear instructions: `"This command requires administrator privileges. Open PowerShell as Administrator and try again."`

**First-run flow:**
1. Install via dotnet tool or download .exe
2. Open admin PowerShell
3. `context-bridge service install` — downloads embedding model (~22 MB) if not present, registers and starts service
4. User runs `context-bridge configure` (standard user privileges) — wires up Claude Code (HTTP) and Claude Desktop (stdio)

### Code Signing & Distribution

v1 ships **unsigned binaries** to avoid upfront complexity:
- Direct .exe download avoids NuGet account/signing requirements
- Windows SmartScreen shows "Unknown Publisher" on first run — users dismiss it (one-time friction)
- Code signing cert (~$300+/year) deferred until real userbase demand justifies the cost
- Future: if v1 gains traction and signing becomes a friction point, add code signing cert in v2+

---

## Cross-Client Support & Session Extraction

### Universal Mechanism: `InitializeResult.instructions`

During the MCP handshake the server returns an `instructions` string in its `initialize` response. Compliant clients inject this into the system prompt automatically — no user configuration required. This is the cross-client baseline that works everywhere.

OpenAI Codex explicitly documents this behaviour: *"Codex reads the MCP instructions field returned during initialization and uses it as server-wide guidance alongside the server's tools."* Claude Desktop, Claude Code, Cursor, and Antigravity are all confirmed compliant.

The instructions field should direct the LLM to write memories **incrementally** — after each significant decision, preference, or architectural choice — rather than batching for session end. This makes the extraction model robust regardless of whether the client has a session-end hook:

> "You have access to a persistent memory service via the context-bridge MCP server. After each significant decision, architectural choice, preference, or piece of context worth preserving across sessions, call `memory_write` immediately. When multiple related memories arise at once, use `memory_batch_write`. At natural stopping points call `memory_search` to surface relevant prior context."

### Progressive Enhancement via `context-bridge configure`

`configure` detects installed clients and layers in client-specific improvements on top of the universal baseline. Each enhancement is additive — clients without a detected config get `instructions`-only coverage and degrade gracefully.

| Phase | Client | MCP config path | Rules / instructions injection | Notes |
|-------|--------|----------------|-------------------------------|-------|
| v1 | Claude Code | `~/.claude/settings.json` | `~/.claude/CLAUDE.md` append | `Stop` hook for transcript-based extraction |
| v1 | Claude Desktop | `claude_desktop_config.json` | — | `instructions` only |
| v2 | Cline | `%APPDATA%\Code\User\globalStorage\saoudrizwan.claude-dev\settings\cline_mcp_settings.json` | — | Identical `mcpServers` schema to Claude Desktop; `instructions` only |
| v2 | Cursor | `~/.cursor/mcp.json` | `~/.cursor/rules/` global rules | |
| v2 | Devin Desktop (formerly Windsurf) | `~/.codeium/windsurf/mcp_config.json` | cascade rules | Acquired by Cognition, rebranded June 2026; config path appears unchanged but verify before implementing |
| v3 | Continue.dev | `~/.continue/config.json` | `systemMessage` field | |
| v3 | GitHub Copilot | VS Code user `settings.json` | `github.copilot.chat.codeGeneration.instructions` array | Verify VS Code native MCP support vs. Copilot-specific path before implementing |
| v3 | OpenCode | `~/.config/opencode/opencode.json` | — | Terminal-first agent; uses `mcp` key with `type: "remote"` (not `mcpServers` schema); `instructions` only |
| v3 | OpenAI Codex | `~/.codex/config.toml` | `AGENTS.md` in home dir | CLI and VS Code extension share config |
| v3 | Zed | `%APPDATA%\Zed\settings.json` (Win) / `~/.config/zed\settings.json` | — | Uses `context_servers` key (not `mcpServers`); `instructions` only |
| v4 | Google Antigravity | `~/.gemini/config/mcp_config.json` | custom rules (path TBD — verify before implementing) | Shared across IDE and Antigravity CLI |
| — | Unknown | — | — | `instructions` only (fallback for future clients) |

**v1 client selection rationale:** Launch with Claude Code and Claude Desktop only. Both are first-party Anthropic tools with well-understood config paths and a stable control plane. This cuts the v1 testing matrix in half and lets you ship the core value (memories shared across your own coding workflows) without managing config variations for third-party editors. Cline, Cursor, and other editors move to v2 once v1 is stable and the `configure` command pattern is proven. This phasing is also strategic: a third-party editor integration is more valuable *after* you can demonstrate working memory across your own primary tools.

### Claude Code: Stop Hook Enhancement

For Claude Code specifically, `configure` installs a `Stop` hook that fires when the agent session ends:

```json
{
  "hooks": {
    "Stop": [{ "hooks": [{ "type": "command", "command": "context-bridge extract" }] }]
  }
}
```

`context-bridge extract` reads the most recent session transcript from `~/.claude/projects/`, calls the configured LLM provider to extract discrete memories, and calls `memory_batch_write`. This is the most reliable extraction path — transcript-based, fires on every session end, does not depend on LLM compliance during the session.

---

## MCP Tools (Proposed v1)

| Tool | Description |
|------|-------------|
| `memory_write` | Store a memory with automatic embedding and classification |
| `memory_batch_write` | Store multiple memories atomically — preferred for end-of-session extraction to avoid partial state from sequential calls |
| `memory_search` | Semantic search with optional relational pre-filter |
| `memory_list` | List memories with pagination and filters |
| `memory_delete` | Soft-delete a memory |
| `memory_update` | Update a memory, preserve version history |
| `memory_status` | Service health, record counts, model info |

---

## Technology Stack

| Component | Technology |
|-----------|------------|
| Runtime | .NET 10, Worker Service |
| Windows Service | Microsoft.Extensions.Hosting.WindowsServices |
| Embeddings | Microsoft.ML.OnnxRuntime + all-MiniLM-L6-v2.onnx |
| AI Abstraction | Microsoft.Extensions.AI |
| Storage | SQLite via Microsoft.Data.Sqlite |
| Vector Search | sqlite-vec extension |
| MCP Transport | Streamable HTTP — MCP spec 2025-03-26 (MCP C# SDK) |
| Distribution (v1) | dotnet global tool OR GitHub .exe download |
| Service Management | CLI commands (context-bridge service install\|start\|stop\|uninstall\|status) |

---

## Phasing

### v1 — Core (Claude agents only, Windows)
- Windows Service host
- In-process ONNX embedding with bundled model
- SQLite + sqlite-vec pure vector search
- Core MCP tools (write, search, list, delete, status)
- `configure` command for Claude Code and Claude Desktop
- Service management via CLI: `context-bridge service install|start|stop|uninstall|status`
- Two distribution options:
  - `dotnet tool install -g context-bridge` (requires .NET SDK)
  - Direct .exe download from GitHub Releases (one-time SmartScreen warning)
- Claude Code `Stop` hook for transcript-based extraction
- Claude Desktop `instructions`-driven incremental writes

### v2 — Third-Party Editors & Core Enhancements
- Third-party editor support (`configure` for Cline, Cursor, Windsurf)
- Web dashboard for memory management / audit
- Configurable embedding provider (Ollama, OpenAI-compatible)
- Memory spaces / project scoping as first-class concept

### v3 — Expanded Client Support
- Additional editor clients (Continue.dev, OpenCode, Zed, GitHub Copilot, OpenAI Codex)
- Cross-platform distribution (Mac, Linux)

### Future (if demand warrants)
- .msi installer for zero-friction install + service registration (requires code signing evaluation)
- Advanced client integrations (Google Antigravity, others)
- Optional encrypted cloud sync

---

## Use Cases Driving Requirements

- **Personal cross-repo development context** — preferences, conventions, architectural decisions that apply across all projects regardless of which repo is open
- **Multi-instance AI assistants** — multiple coding assistant windows sharing real-time memory via single service
- **Multiple AI tools simultaneously** — same memory store available across different coding assistants
- **Job search project** — company vetting history, application status, role pattern matching across sessions
- **Cowork job triage** — avoid re-evaluating previously seen postings, accumulate company intelligence over time

---

## Prior Art Evaluated

| Project | Stack | Why Not |
|---------|-------|---------|
| MihaiBuilds/memory-vault | Python, PostgreSQL, Docker | Docker Desktop required, heavyweight for personal use, 6 commits, pre-v1 |
| fusae/Memory-Vault | TypeScript, SQLite, Ollama | Requires Ollama server running separately, no Windows Service model, small community |

Both confirmed the concept is valid. Neither solves the distribution/friction problem for a broad audience.

---

## Open Questions

- **Tag-aware search weighting (v2)** — when a memory_search includes tag context, how are results weighted? Options: hard pre-filter (only tagged memories), soft boost (tagged memories ranked higher), or an LLM call inside the MCP that determines relevance weighting dynamically before executing vector search. The LLM-inside-MCP approach is interesting — it could interpret the agent's current task context and decide which tags matter — but adds latency and cost per search call. Worth prototyping in v2 once tagging data exists.
- **SQLite schema migration strategy** — needs a decision before v1 ships. First-run creates the DB; any v1.x schema change must handle existing installs cleanly. Options: EF Core migrations, FluentMigrator, or a simple `schema_version` table with manual DDL. Skipping this means ad-hoc upgrade code the first time a column is added.
- **Service command UAC elevation** — `context-bridge service install|uninstall` require admin. Verify whether the application can detect non-admin context and print a helpful error, or if additional UAC elevation request mechanisms are needed.
- **Memory consolidation / deduplication strategy** — as memory accumulates, define strategy for merging similar entries (if any).
- **Web dashboard scope** — currently planned for v2+; confirm whether it's a "nice to have" or core to the roadmap.

---

## References

- [MihaiBuilds/memory-vault](https://github.com/MihaiBuilds/memory-vault)
- [fusae/Memory-Vault](https://github.com/fusae/Memory-Vault)
- [Microsoft.ML.OnnxRuntime](https://onnxruntime.ai/docs/get-started/with-csharp.html)
- [Microsoft.Extensions.AI](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai-preview/)
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [all-MiniLM-L6-v2 ONNX](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
- [sqlite-vec](https://github.com/asg017/sqlite-vec)
- [WiX 4](https://wixtoolset.org/)
