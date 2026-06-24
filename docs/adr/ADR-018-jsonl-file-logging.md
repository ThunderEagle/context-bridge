# ADR-018: Custom ILoggerProvider for JSONL File Logging

**Date:** 2026-06-24  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

The service had almost no logging — three `LogInformation` calls in `Worker.cs` and silence everywhere else. The MCP tools, repositories, and embedding pipeline emitted nothing, making it difficult to trace problems during development or diagnose unexpected behaviour in production.

The requirement is structured file-based debug logging that can be enabled and disabled without restarting the service. JSONL (one JSON object per line) was chosen as the output format because it is machine-readable, trivially greppable, and compatible with log analysis tools.

## Decision

We will implement a custom `ILoggerProvider` (`JsonlFileLoggerProvider`) backed by `System.Text.Json`, rather than adopting a third-party logging library such as Serilog or NLog.

Log level control uses .NET's standard `Logging:LogLevel` configuration, which already hot-reloads from the ProgramData `appsettings.json` via `reloadOnChange: true`. The provider is registered only in service mode; stdio mode is unaffected (stdout is the MCP protocol channel).

Output: `%PROGRAMDATA%\ContextBridge\logs\contextbridge-YYYYMMDD.jsonl`, daily rolling, 7-day retention.

Enable debug logging: set `Logging:LogLevel:Default` to `"Debug"` in the ProgramData appsettings — takes effect immediately, no restart required.

## Consequences

### Positive
- No new NuGet dependencies; consistent with the project's lean posture.
- Hot-reload of log levels is handled entirely by .NET's existing `LoggerFilterOptions` infrastructure — no additional wiring needed.
- The provider is ~150 lines, self-contained in `ContextBridge.Infrastructure/Logging/`, and straightforward to reason about.
- JSONL format with CLEF-inspired field names (`@t`, `@l`, `@mt`, `@m`, `@x`) is compatible with standard log tooling.

### Negative
- Daily file rotation and 7-day retention are hand-rolled. Edge cases (disk full, file locked by a concurrent process) are handled best-effort rather than with Serilog's battle-tested resilience.
- No structured sink configuration in `appsettings.json` — the provider is always registered when the service starts; the only knob is the log level.

### Neutral / Trade-offs
- If a second sink is ever needed (e.g., structured output to a remote collector), the migration path is to adopt Serilog: delete `JsonlFileLoggerProvider.cs`, add three NuGet packages, and swap the `builder.Logging.AddProvider(...)` call. All `ILogger<T>` call sites in repositories and MCP tools remain unchanged.

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| Serilog (AspNetCore + Sinks.File + Formatting.Compact) | Adds three NuGet packages and a parallel configuration section for a single-sink use case. The rolling/retention logic it replaces is trivially simple at this scale. Worth revisiting if a second sink or enrichment pipeline is needed. |
| NLog | Same dependency-cost argument as Serilog, with less upside for a single-file scenario. |
| `Microsoft.Extensions.Logging.Console` with JSON format | Writes to stdout/stderr, not a file. Redirecting to a file is not supported natively. |

## References
- `src/ContextBridge.Infrastructure/Logging/JsonlFileLoggerProvider.cs`
- `src/ContextBridge.Infrastructure/Logging/JsonlFileLogger.cs`
- [CLEF (Compact Log Event Format)](https://clef-json.org/)
