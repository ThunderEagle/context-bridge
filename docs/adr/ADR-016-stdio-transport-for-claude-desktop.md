# ADR-016: stdio Transport for Claude Desktop Support

**Date:** 2026-06-22  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

ADR-010 chose Streamable HTTP as the MCP transport for the Windows Service. The rationale: stdio spawns one server process per client, which would give each client an independent SQLite connection and break the shared memory model across concurrent sessions.

Claude Desktop requires stdio transport — it spawns the MCP server as a child process and communicates via stdin/stdout. HTTP entries in `claude_desktop_config.json` are rejected at startup. This blocked Claude Desktop support entirely.

ADR-014 (HTTPS cert) attempted to address this via the Connectors UI path (which accepts HTTPS URLs) but was reverted due to self-signed cert incompatibility with the Node.js MCP client.

## Decision

Add a **`stdio` dispatch path** in `Program.cs`. When invoked as `context-bridge stdio`, the binary starts a minimal generic host (no Kestrel, no Windows Service integration) with MCP stdio transport and the same DI stack as the HTTP service:

```
context-bridge          → Windows Service mode (HTTP, port 5290)
context-bridge stdio    → stdio mode (Claude Desktop child process)
context-bridge <cmd>    → CLI mode (service management, configure, etc.)
```

Claude Desktop config:
```json
{
  "mcpServers": {
    "context-bridge": {
      "command": "<absolute-path-to-context-bridge.exe>",
      "args": ["stdio"]
    }
  }
}
```

The `configure` command detects `%APPDATA%\Claude\claude_desktop_config.json` and writes this entry automatically.

## Why This Doesn't Break the Shared Memory Model

ADR-010's concern was correct for the HTTP service: multiple stdio processes would give each its own SQLite connection with no live consistency. The stdio path here avoids that problem differently:

- The **Windows Service** (HTTP) handles all Claude Code traffic — single process, single SQLite connection, immediately consistent
- The **stdio process** (Claude Desktop) shares the same `memories.db` file; SQLite WAL mode (already configured) safely handles concurrent multi-process access
- Memories written by the stdio process are committed to the database and immediately visible to the HTTP service (and thus Claude Code)
- The shared memory model is preserved **at the SQLite layer**, not the process layer

The downside compared to a pure HTTP model: the stdio process loads its own ONNX model instance (~100MB RAM, ~500ms startup) rather than reusing the service's warm instance. For Claude Desktop usage patterns (one session at a time), this is acceptable.

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| stdio proxy that forwards to HTTP service | Adds a process and a network hop; eliminates the size/startup concern but adds protocol translation complexity |
| Keep HTTP, accept Desktop limitation | Closes off Claude Desktop entirely |
| Named pipe / Unix socket | MCP clients don't support non-URL transports yet |
| HTTPS self-signed cert (Connectors path) | Reverted — Node.js MCP client rejects self-signed certs (ADR-014) |

## References

- ADR-010: Streamable HTTP transport (unchanged for the HTTP service)
- ADR-015: Localhost-only security model
- ADR-004: CLI-driven service registration
