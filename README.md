# ContextBridge

A Windows-first, zero-dependency MCP memory server for AI coding assistants.

## Overview

Context-Bridge solves the problem of AI assistant statefulness across multiple tools and sessions. It provides a shared memory layer that:

- **Runs as a Windows Service** — starts automatically, model loaded once, shared across all concurrent coding assistant sessions
- **Requires zero external dependencies** — no Docker, Postgres, Python, or Ollama; everything runs in-process
- **Installs cleanly** — `dotnet tool install -g context-bridge` or a standard .exe installer
- **Uses semantic search** — ONNX-powered embeddings with SQLite vector storage

## Key Architecture Decisions

See [DESIGN.md](DESIGN.md) for the full design document. Key highlights:

- **Language:** .NET 10 Worker Service (Windows Service integration, clean distribution story)
- **Embeddings:** all-MiniLM-L6-v2 via Microsoft.ML.OnnxRuntime (in-process, no Ollama)
- **Storage:** SQLite + sqlite-vec (single file, vector search at DB layer)
- **Transport:** Streamable HTTP MCP (shared service across multiple concurrent clients)
- **Clients (v1):** Claude Code + Claude Desktop (with progressive enhancement for others in v2+)

## Phasing

**v1** — Core MVP with Windows Service, in-process embeddings, SQLite vector storage, Claude Code + Claude Desktop support.

**v2** — Third-party editors (Cline, Cursor, Windsurf), web dashboard, configurable embedding providers.

**v3** — Expanded client support, cross-platform distribution.

## Status

Currently in **active development** — design finalized, implementation starting.
