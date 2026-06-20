# ADR-002: Single Executable — CLI and Service in One Binary

**Date:** 2026-06-20  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

The project requires two distinct runtime behaviors from the same tool:

1. **Service mode** — long-running background process; loads the ONNX model, serves MCP requests over HTTP, runs as a Windows Service
2. **CLI mode** — short-lived commands that manage the service (`service install|start|stop|uninstall|status`) and configure MCP clients (`configure`, `extract`, `token`)

The initial scaffold created two executable projects: `ContextBridge.Service` (Worker Service) and `ContextBridge.Cli` (Console App). This raised the question of whether they should remain separate binaries or be unified into one.

The critical constraint is that CLI commands need access to the same DataProtection configuration as the service — specifically to read the bearer token for distribution to MCP client configs. With two separate binaries, each would need to be independently configured with the same DataProtection key ring path, creating a coordination surface that can drift.

## Decision

We will ship a **single executable**: `ContextBridge.Service` is the entry point for both modes.

`Program.cs` in `ContextBridge.Service` checks arguments at startup:
- Arguments present and matching a CLI command → route to the CLI handler (via `System.CommandLine`), execute, exit
- No arguments, or running as a Windows Service → build and run the `IHost` (Worker Service mode)

`ContextBridge.Cli` is retained as a **class library** containing all CLI command handler implementations. It is referenced by `ContextBridge.Service` and has no entry point of its own. This preserves separation of concerns (CLI logic is isolated and independently testable) without producing a second binary.

The project reference graph becomes:

```
ContextBridge.Service (exe)
├── ContextBridge.Cli (lib)       ← CLI command handlers
├── ContextBridge.Infrastructure (lib)
└── ContextBridge.Core (lib)

ContextBridge.Cli (lib)
├── ContextBridge.Infrastructure (lib)
└── ContextBridge.Core (lib)
```

## Consequences

### Positive
- Single binary to install, distribute, and version — `dotnet tool install -g ThunderEagle.ContextBridge` produces exactly one `context-bridge` executable
- CLI commands share the Service's DI container configuration — DataProtection, `appsettings.json`, port — with no coordination overhead
- `UseWindowsService()` already handles detection of service vs. interactive context; `System.CommandLine` handles detection of CLI vs. service mode at the argument level
- CLI handlers are independently testable as a library without spinning up the full host

### Negative
- `Program.cs` must handle two bootstrap paths (CLI routing vs. host builder); this adds a small amount of startup logic that must be kept tidy

### Neutral / Trade-offs
- The Windows Service Manager always starts the binary with no arguments, so it always enters service mode — no special handling required for that case

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Two separate executables** | CLI commands need DataProtection access to read the bearer token. Two binaries require independently configured key ring paths that can drift. Also complicates the `dotnet tool` packaging story (one tool ID, two executables is awkward). |
| **CLI commands call the service over HTTP** | The `service install` command must run before the service is running, so it cannot depend on an HTTP connection to a service that doesn't exist yet. `configure` must also run at any time regardless of service state. |

## References

- [.NET Worker Service as Windows Service + CLI — Microsoft docs pattern](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service)
- [System.CommandLine](https://github.com/dotnet/command-line-api)
- ADR-001: .NET Worker Service technology selection
