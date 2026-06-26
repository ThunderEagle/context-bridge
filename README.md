<!-- mcp-name: io.github.ThunderEagle/context-bridge -->
# ContextBridge

A zero-dependency MCP memory server for Windows that gives AI coding assistants a shared, persistent memory layer with semantic search.

## What is ContextBridge?

AI coding assistants like Claude Code are stateless—each session starts fresh, with no persistent memory of your prior context, decisions, or discoveries. ContextBridge solves this by running as a background Windows Service that all your AI tools connect to. It stores memories persistently, searchable by meaning (not keywords), so context accumulates across sessions and across different tools simultaneously.

## Why ContextBridge?

- **Zero external dependencies** — No Docker, Postgres, Python, or Ollama. Everything runs in one Windows Service process.
- **In-process embeddings** — Embedding model (all-MiniLM-L6-v2, ~22 MB) runs via ONNX Runtime; no external API calls.
- **Single-file storage** — SQLite database at `%ProgramData%\ContextBridge\memories.db`. Backup is one file copy.
- **Shared across tools** — Claude Code (HTTP), Claude Desktop (stdio), Cline, VS Code Chat Agents all connect to the same memory store in real-time.
- **Memory survives service restarts** — Memories persist in SQLite; the embedding model loads once on startup and stays warm.

## Requirements

- **Windows 10 / Windows 11** (required for Windows Service integration)
- **Option A:** .NET 10 SDK (if installing via `dotnet tool`)
- **Option B:** Windows 10+ (no additional prerequisites if downloading a pre-built .exe)
- **Admin PowerShell** (required to install/uninstall the Windows Service)

## Quick Start

### Option A: Install via dotnet global tool

If you have the .NET 10 SDK installed:

```powershell
dotnet tool install -g ThunderEagle.ContextBridge
```

This places a `context-bridge` executable on your system PATH.

### Option B: Direct download

Download a pre-built executable from [GitHub Releases](https://github.com/ThunderEagle/context-bridge/releases) (no SDK required; one-time SmartScreen warning). Add the `.exe` to your PATH or invoke it directly.

### First-run setup

Run these two commands in **admin PowerShell** (right-click → "Run as Administrator"):

```powershell
# 1. Download the embedding model, register as Windows Service, start it
context-bridge service install

# 2. Configure your AI tools to connect
context-bridge configure
```

**What happens:**
1. `service install` downloads the embedding model (~22 MB), registers the Windows Service, sets it to auto-start on boot, and starts it immediately.
2. `configure` auto-detects installed clients (Claude Code, Claude Desktop, Cline, VS Code Chat) and wires them up to connect to the service.

The service now runs in the background. Your AI tools will see the memory store the next time you restart them.

**Verify the service is running:**

```powershell
Get-Service ContextBridge
```

Expected output: `Status = Running`

## Supported Clients

| Client | Transport | Version | Notes |
|---|---|---|---|
| **Claude Code** | HTTP | Latest | Auto-configured by `context-bridge configure` |
| **Claude Desktop** | stdio | Latest | Auto-configured via `claude_desktop_config.json` |
| **Cline** (VS Code) | HTTP | Latest | Auto-configured by `context-bridge configure` |
| **VS Code Chat Agents** | HTTP | 1.99+ | Auto-configured by `context-bridge configure` |

All clients share the same SQLite database via concurrent connections; memories written by one tool are immediately visible to others.

## MCP Tools

ContextBridge exposes seven MCP tools for your AI assistants to use:

| Tool | Purpose |
|---|---|
| `memory_write` | Store a single memory with automatic semantic embedding and optional tags |
| `memory_batch_write` | Store multiple related memories atomically (efficient end-of-session extraction) |
| `memory_search` | Semantic search — natural language query, returns nearest-neighbor results |
| `memory_list` | Paginated list of all memories with optional tag filters |
| `memory_update` | Update a memory's content (re-embedded automatically) |
| `memory_delete` | Delete a memory by ID |
| `memory_status` | Service health check, record count, model info |

**Tag conventions** (optional; assigned by the AI tool):
- `project:<repo-name>` — scope memories to a project
- `type:decision` — architectural or technology choices
- `type:preference` — coding style, tooling, workflow preferences
- `type:pattern` — recurring patterns or conventions
- `type:reference` — pointers to external resources or documentation

## Handoff — Resuming Sessions

Memories are permanent facts. A **handoff** is something different: ephemeral session state that lets you resume where you left off in a future session, without reloading conversation history.

### How it works

**At the end of a session**, ask your AI assistant to save its state:

> "Save a handoff for project context-bridge with what we were working on."

The model calls `handoff_write` with a summary of current work — decisions made, next steps, open questions — scoped to the project.

**At the start of the next session**, the model calls `handoff_list` automatically (via server instructions) and incorporates any prior handoff as its opening context. It then calls `handoff_acknowledge` to remove the handoff once processed.

### Handoff tools

| Tool | Purpose |
|---|---|
| `handoff_write` | Capture session state — what you're working on, decisions made, next steps |
| `handoff_list` | Retrieve active handoffs, optionally filtered by project |
| `handoff_acknowledge` | Remove a handoff after processing it (permanent deletion) |

**Key parameters for `handoff_write`:**
- `content` — the session summary (free-form text)
- `project` — project identifier, e.g. `context-bridge` (optional but recommended)
- `ttl_days` — how many days to keep the handoff before auto-expiry (default: 7)

### Handoffs vs. memories

| | Memories | Handoffs |
|---|---|---|
| **Purpose** | Durable facts, decisions, preferences | Ephemeral "where I was" snapshots |
| **Lifespan** | Permanent (until explicitly deleted) | TTL-bounded (default 7 days) |
| **Search** | Semantic search via `memory_search` | Exact lookup via `handoff_list` |
| **Cleanup** | `memory_delete` | `handoff_acknowledge` (or TTL expiry) |

Do not convert handoff content to memories automatically. Memories are for facts that will remain true indefinitely. If something from a resumed session rises to that level, write it via `memory_write` separately.

### Explicit resumption

If your MCP client supports the prompts capability (e.g. Claude Code), you can trigger a session resumption explicitly:

```
/mcp__context-bridge__resume-session context-bridge
```

This invokes the `resume-session` named prompt, which tells the model to look up any handoff for the specified project and incorporate it.

### Expiry and reliability

Handoffs expire after `ttl_days` and are purged on service startup. If a session crashes before `handoff_acknowledge` is called, the handoff survives until its TTL — it will surface again in the next session's `handoff_list` call.

## Importing Existing Context

If you've been using Claude Code's built-in file-based memory (`~/.claude/projects/<name>/memory/`), you can migrate that context into ContextBridge without any special tooling. Just ask:

> "Check your memory files and add any relevant entries to context-bridge using `memory_batch_write`."

Claude Code reads its own memory index, iterates the entries, and calls `memory_batch_write` to store them in ContextBridge — where they become semantically searchable and visible to all connected clients immediately.

You can scope the request: *"import only entries tagged `project:my-repo`"* or *"add everything in your memory files."*

Once imported, you can remove the original file-based entries to avoid maintaining two stores. ContextBridge becomes the single source of truth, shared across Claude Code, Claude Desktop, and any other connected client.

## CLI Reference

All functionality is controlled via the `context-bridge` command. Run from any command-line (admin PowerShell required for service install/uninstall/config set).

### Service Management

```powershell
context-bridge service install      # Download model, register service, start it
context-bridge service start        # Start the service (no-op if already running)
context-bridge service stop         # Stop the service gracefully
context-bridge service status       # Show current status
context-bridge service uninstall    # Stop and unregister (preserves memories.db)
```

### Model Management

```powershell
context-bridge model download       # Download embedding model (~22 MB) from Hugging Face
context-bridge model download --yes # Skip confirmation, force re-download
```

### Configuration

```powershell
context-bridge config get port      # Show current HTTP port (default: 5290)
context-bridge config set port 8000 # Change HTTP port (requires service restart)
```

Configuration is stored in `%ProgramData%\ContextBridge\appsettings.json`. Changes take effect on next service start.

### Client Configuration

```powershell
context-bridge configure            # Auto-configure all installed clients
```

This command:
- Detects installed AI clients (Claude Code, Claude Desktop, Cline, VS Code)
- Writes MCP server configuration for each client
- For Claude Code: injects usage guidelines into `~/.claude/CLAUDE.md`
- Prints which clients were configured

**Example output:**
```
Claude Code configured (HTTP transport)
Claude Desktop configured (stdio transport)
Cline configured (HTTP transport)
VS Code Chat Agents configured (HTTP transport)

Configured 4 client(s). Restart them to pick up changes.
```

Run this again after:
- Installing a new AI client
- Changing the service port (`config set port`)
- Updating ContextBridge itself

## Security

**Security model:** Kestrel binds exclusively to `127.0.0.1` (localhost only). No authentication, no TLS.

**Design rationale:** The localhost bind is the security perimeter. A process with enough privilege to intercept traffic on `127.0.0.1` already has broad machine access regardless of additional authentication.

**Sensitive data:** Treat `%ProgramData%\ContextBridge\memories.db` as sensitive — it contains plaintext memory content. Any credentials or API keys stored in memories should be considered readable by local processes. Keep backups secure accordingly.

## Build from Source

For developers who want to modify, test, or run ContextBridge locally.

### Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** — provides `dotnet` CLI and runtime
- **Windows 10 / Windows 11**
- **Admin PowerShell** — required to install as a Windows Service
- **Git**

### Clone and Build

```powershell
git clone https://github.com/ThunderEagle/context-bridge.git
cd context-bridge
dotnet build
```

Build time: 30–60 seconds on first run; subsequent builds are faster. Warnings are treated as errors per project policy.

### Run Tests

```powershell
dotnet test
```

This executes all xUnit integration tests (~2–5 minutes):
- Vector search accuracy validation
- Memory persistence across restarts
- All MCP tool implementations
- Schema migrations
- SQLite + sqlite-vec integration

Tests use real temp-file SQLite databases (not in-memory) because sqlite-vec requires file-backed connections.

### Run Locally (Console Mode)

To test the service without installing as a Windows Service:

```powershell
dotnet run --project src/ContextBridge.Service
```

This starts the HTTP MCP server in the foreground:
- **Binding:** `http://127.0.0.1:5290` (localhost only)
- **Storage:** `%LOCALAPPDATA%\ContextBridge\memories.db` (user temp directory)
- **Logging:** Console output shows startup, request traces, and errors

The service runs until you press `Ctrl+C`. Expected startup time: 5–10 seconds (ONNX model loads and JIT-compiles on first run; subsequent starts are faster).

### Install as a Windows Service (from source)

Publish the project, then install from the published binary:

```powershell
dotnet publish -c Release -o ./publish
./publish/ContextBridge.Service.exe service install
```

**Important:** Run in admin PowerShell. This registers ContextBridge as a Windows Service with auto-start enabled, identical to the released executable.

Publishing to a separate directory avoids file-locking issues if you rebuild the project while the service is running.

Uninstall:
```powershell
./publish/ContextBridge.Service.exe service uninstall
```

## Roadmap

**v1** (current) — Windows Service, in-process embeddings (all-MiniLM-L6-v2), SQLite vector storage, Claude Code + Claude Desktop support.

**v2** — Third-party editor support (Cursor, Windsurf), web dashboard, configurable embedding providers.

**v3** — Cross-platform support (macOS, Linux), expanded client ecosystem.

## Technology Stack

For contributors and those interested in architectural details.

| Concern | Technology | Why |
|---|---|---|
| **Runtime** | .NET 10 Worker Service (`Microsoft.NET.Sdk.Web`) | Windows Service integration, clean distribution, no external runtime |
| **Embeddings** | ONNX Runtime + all-MiniLM-L6-v2 INT8 | Fast, bundled, 384-dim vectors, ~22 MB model, in-process |
| **Vector Search** | sqlite-vec extension | Native vector operations in SQLite, single-file storage, no external DB |
| **Data Access** | Dapper + raw SQL | Sqlite-vec requires raw SQL; Dapper handles object mapping |
| **AI Abstraction** | `Microsoft.Extensions.AI` | Standard AI library for .NET |
| **CLI** | System.CommandLine | Built-in CLI parsing, minimal dependencies |
| **MCP Transport** | Streamable HTTP (ModelContextProtocol SDK) | Shared service, concurrent clients, clean async/await model |

**No external dependencies:** Core domain logic depends only on C# standard library. Infrastructure and CLI add Microsoft packages and MCP SDK.

Full design rationale: see [`docs/DESIGN.md`](docs/DESIGN.md) and [`docs/adr/`](docs/adr/) (Architectural Decision Records).
