# ADR-010: Streamable HTTP over stdio MCP Transport

**Date:** 2026-06-21  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

The MCP specification defines two transport mechanisms for server-client communication:

**stdio** — the server process is spawned by the client as a child process. Communication happens via stdin/stdout pipes. Each client spawns its own server process.

**Streamable HTTP** — a single persistent server process exposes an HTTP endpoint. Clients connect as HTTP clients. The server can respond inline or upgrade to an SSE stream for long-running operations. This is the transport defined in the MCP spec 2025-03-26 and replaces the earlier deprecated HTTP+SSE two-endpoint design.

## Decision

We will use **Streamable HTTP** as the exclusive transport for the ContextBridge MCP server. stdio support is not implemented.

The shared memory model is the core value proposition of this service. That model requires a single process with a single SQLite connection so all concurrent clients see immediately-consistent memory state. stdio defeats this directly: the client spawns one server process per session, giving each its own SQLite connection and its own view of the database. Real-time consistency between Claude Code and Claude Desktop — two sessions open simultaneously — is impossible with stdio.

Streamable HTTP gives us:
- One process, one SQLite connection, one view of memory state across all concurrent clients
- Kestrel binds to `127.0.0.1` only — network-isolated by design, no firewall dependency
- A path to a web dashboard in a future phase without architectural change
- The transport that Claude Code, Claude Desktop, and all compliant MCP clients already implement

The Streamable HTTP transport is implemented using `ModelContextProtocol.AspNetCore` 1.4.0, which provides `MapMcp()` for ASP.NET Core endpoint registration and handles the SSE upgrade protocol transparently.

## Consequences

### Positive
- Concurrent clients (Claude Code + Claude Desktop open simultaneously) share real-time memory state
- MCP server boots once on service start; all clients connect to the running instance
- `MapMcp()` handles the full Streamable HTTP protocol, SSE upgrade, and session management
- HTTP endpoint enables future web dashboard without adding a second transport

### Negative
- Requires a running service process — there is no zero-setup stdio fallback
- Service must be running before MCP clients can connect; a stopped service appears as a connection failure to clients

### Neutral / Trade-offs
- Bearer token is required on all non-health requests; token ends up in plaintext in MCP client config files (see security model in DESIGN.md — this is an accepted trade-off with no clean alternative given current MCP client constraints)

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| stdio | Each client spawns its own process and gets its own SQLite connection — shared memory state across concurrent clients is impossible |
| stdio + shared SQLite (WAL mode, multiple writers) | SQLite WAL mode supports multiple writers but does not solve the process-per-client problem; each process still has an independent in-memory state and query cache |
| Named pipe / Unix socket | OS-level access control, no plain-text token needed — but MCP clients configure servers via HTTP URLs and do not support named pipe or Unix socket addresses today |

## References
- [MCP Streamable HTTP transport spec](https://modelcontextprotocol.io/specification/2025-03-26/basic/transports)
- [ModelContextProtocol.AspNetCore NuGet](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)
- ADR-001: .NET Worker Service as the host (now extended to WebApplication for HTTP hosting)
