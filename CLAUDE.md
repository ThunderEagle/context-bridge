# ContextBridge — AI Assistant Context

## Project Overview

ContextBridge is a Windows-first, zero-dependency MCP memory server. It gives AI coding assistants (Claude Code, Claude Desktop, and others) a shared persistent memory layer backed by semantic vector search. A single Windows Service process holds the embedding model warm and serves all concurrent clients via Streamable HTTP MCP transport.

Full design rationale and architectural decisions: [`docs/DESIGN.md`](docs/DESIGN.md)

NuGet package ID: `ThunderEagle.ContextBridge`  
Code namespaces: `ContextBridge.*`

---

## Solution Structure

```
src/
  ContextBridge.Core/           # Domain interfaces, models — no infrastructure deps
  ContextBridge.Infrastructure/ # SQLite, ONNX Runtime, MCP tool implementations
  ContextBridge.Service/        # Single executable: Worker Service host + CLI entry point
  ContextBridge.Cli/            # Class library: CLI command handler implementations
tests/
  ContextBridge.Tests/          # xUnit integration tests
docs/
  DESIGN.md                     # Full design document
  adr/                          # Architectural Decision Records
models/                         # Bundled ONNX model files (binary, committed intentionally)
```

**Single executable model:** `ContextBridge.Service` is the only executable. It bootstraps `System.CommandLine` at startup — if CLI args are present it routes to a command handler in `ContextBridge.Cli` and exits; otherwise it starts the Worker Service / Windows Service host. See ADR-002.

**Dependency direction:**

```
ContextBridge.Service (exe)
├── ContextBridge.Cli (lib)
├── ContextBridge.Infrastructure (lib)
└── ContextBridge.Core (lib)

ContextBridge.Cli (lib)
├── ContextBridge.Infrastructure (lib)
└── ContextBridge.Core (lib)
```

Core has no external package dependencies.

---

## Technology Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 Worker Service |
| Windows Service | `Microsoft.Extensions.Hosting.WindowsServices` |
| Embeddings | `Microsoft.ML.OnnxRuntime` + all-MiniLM-L6-v2 INT8 ONNX |
| AI abstraction | `Microsoft.Extensions.AI` (`IEmbeddingGenerator<string, Embedding<float>>`) |
| Storage | `Microsoft.Data.Sqlite` + sqlite-vec extension |
| Data access | Dapper (raw SQL, Dapper mapping — EF Core avoided; sqlite-vec requires raw SQL) |
| MCP transport | Streamable HTTP via MCP C# SDK (`ModelContextProtocol`) |
| Secret storage | `Microsoft.AspNetCore.DataProtection` (cross-platform; DPAPI on Windows) |
| CLI parsing | System.CommandLine |

---

## Build & Test

```powershell
# Build entire solution
dotnet build

# Run all tests
dotnet test

# Run the service locally (not as a Windows Service)
dotnet run --project src/ContextBridge.Service

# Install as Windows Service (requires admin shell)
dotnet run --project src/ContextBridge.Cli -- service install
```

Tests use real temp-file SQLite databases (not in-memory) because the sqlite-vec extension requires a file-backed connection.

---

## C# Conventions

- File-scoped namespaces, primary constructors, `required` properties (C# 11+)
- One class per file; namespace mirrors folder path under `src/`
- Nullable reference types enabled globally via `Directory.Build.props`
- `TreatWarningsAsErrors` is on — zero warnings policy
- All I/O async; every method accepting I/O forwards `CancellationToken`
- `_camelCase` for private fields, `PascalCase` for everything public

---

## ADR Conventions

Architectural Decision Records live in `docs/adr/`. Format: MADR (Markdown Architectural Decision Records).

**Write an ADR before writing the code that implements the decision.**

File naming: `ADR-NNN-short-slug.md`. Commit with message prefix `docs(adr):`.

Decisions already captured as ADRs:
- ADR-001: .NET Worker Service as the host technology
- ADR-002: Single executable — CLI and Service in one binary
- ADR-003: DataProtection over raw DPAPI for secret storage (Phase 1)
- ADR-004: CLI-driven service registration (vs. MSI installer) for v1 (Phase 1)
- ADR-005: all-MiniLM-L6-v2 INT8 quantized model (Phase 2)
- ADR-006: Tokenizer library choice (Phase 2)
- ADR-007: Dapper over EF Core for data access (Phase 3)
- ADR-008: Schema migration via `schema_version` table + startup DDL (Phase 3)
- ADR-009: Pure vector search over hybrid (Phase 3)
- ADR-010: Streamable HTTP over stdio MCP transport (Phase 4)
- ADR-011: Tag assignment as client responsibility (Phase 4)
- ADR-012: `extract` command calls Claude API directly (Phase 5)

---

## What to Capture as Memories / ADRs

When working in this codebase, write an ADR (not a memory) for:
- Any non-trivial technology or library selection
- Any deviation from the patterns established in `docs/DESIGN.md`
- Any constraint discovered that affects other phases

Write a memory (via `memory_write`) for:
- Developer preferences or conventions established during a session
- Cross-project context that would be lost at session end
- Decisions that don't rise to ADR level but are worth preserving

---

## MCP Tools (v1)

| Tool | Purpose |
|---|---|
| `memory_write` | Store a single memory with embedding |
| `memory_batch_write` | Store multiple memories atomically |
| `memory_search` | Semantic similarity search |
| `memory_list` | Paginated list with optional tag filter |
| `memory_delete` | Soft-delete a memory |
| `memory_update` | Update content + re-embed; preserve version |
| `memory_status` | Service health, record counts, model info |

---

## Security Model

- Kestrel binds exclusively to `127.0.0.1` — never `0.0.0.0`
- Bearer token required on all endpoints except `GET /health`
- Token generated on first run via `RandomNumberGenerator.GetBytes(32)`, stored via DataProtection
- `context-bridge configure` distributes the token to known MCP client config files

---

## Phase Status

| Phase | Status |
|---|---|
| 0 — Project Foundation | Complete |
| 1 — Windows Service Host + Configuration | Complete |
| 2 — Embedding Pipeline | Complete |
| 3 — Storage Layer | Pending |
| 4 — MCP Tools | Pending |
| 5 — configure + extract Commands | Pending |
| 6 — Distribution & Release | Pending |
