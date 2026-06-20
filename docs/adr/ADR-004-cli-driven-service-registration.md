# ADR-004: CLI-Driven Service Registration Over MSI Installer for v1

**Date:** 2026-06-20  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

The service must be registered as a Windows Service before it can run in the background and survive reboots. There are two credible approaches for v1:

1. **MSI installer** — a proper installer wizard handles service registration, directory setup, and PATH configuration. Standard Windows user experience. Requires WiX 4 toolchain, code signing for SmartScreen, and significant packaging infrastructure before any user can run the product.

2. **CLI-driven registration** — the binary ships as a self-contained EXE or dotnet global tool; the user runs `context-bridge service install` from an admin shell to register it as a Windows Service. No installer toolchain required.

The key constraint is timeline: installer infrastructure (WiX, code signing cert at ~$300+/year, GitHub Actions pipeline for signing) is a significant upfront investment that produces no end-user value until the core product works. The DESIGN.md explicitly defers MSI and code signing to v2+ pending user demand.

## Decision

We will ship **CLI-driven service registration** for v1.

The `service` subcommand group exposes:

```
context-bridge service install    — register as Windows Service, start immediately
context-bridge service start      — start if stopped
context-bridge service stop       — stop the running service
context-bridge service uninstall  — stop and remove service registration
context-bridge service status     — show current service state + health check
```

Commands that require admin privileges (`install`, `uninstall`) detect non-elevated context and print a clear actionable error rather than failing cryptically. The SC API (`System.ServiceProcess.ServiceController` + P/Invoke `CreateService`) is used for registration; `ServiceController` is used for start/stop/status.

A `config` subcommand group manages runtime configuration:

```
context-bridge config set port 5291   — write port to %ProgramData%\ContextBridge\appsettings.json
context-bridge config get port        — read and display current configured port
```

The `get`/`set` verb pattern is chosen over dedicated subcommands (e.g., `set-port`) for extensibility — additional settings in v2+ follow the same pattern without expanding the command surface.

Service configuration is stored in `%ProgramData%\ContextBridge\appsettings.json` (not `%APPDATA%`). The service runs as SYSTEM, which has no access to the installing user's `%APPDATA%` roaming profile. `%ProgramData%` is accessible to SYSTEM and to any admin-elevated process, covering both the running service and the CLI commands that manage it.

`config set` requires admin for the same reason as `service install` — it writes to `%ProgramData%`. When the service is already running, `config set` detects this and prompts the user (Y/n) to restart the service immediately; if declined, a warning is printed that the change will not take effect until the next restart.

Distribution in v1:
- `dotnet tool install -g ThunderEagle.ContextBridge` for developers with .NET SDK
- Direct `.exe` download from GitHub Releases for everyone else (one-time SmartScreen "Unknown Publisher" warning)

## Consequences

### Positive
- Zero packaging infrastructure required to ship v1 — no WiX project, no code signing cert, no signing pipeline
- CLI commands are testable as library code (`ContextBridge.Cli`) without an installer
- Developers (the v1 audience) are comfortable running commands in an admin shell
- The first-run flow is explicit and auditable: the user sees exactly what is being registered

### Negative
- Requires the user to open an admin PowerShell — one extra step vs. a wizard
- SmartScreen "Unknown Publisher" warning on direct .exe download; first-time friction for non-developer users
- No automatic service re-registration on updates — user must `uninstall` then `install` if the binary path changes (e.g., dotnet tool update moves the binary)

### Neutral / Trade-offs
- `dotnet tool update -g ThunderEagle.ContextBridge` does not automatically re-register the service; this is documented behavior the user must be aware of. A future `service upgrade` command could automate this.

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **WiX 4 MSI installer** | Requires full packaging toolchain and code signing before first user can run the product; deferred to v2+ pending demand |
| **NSIS / Inno Setup** | Lower quality than WiX; still requires code signing; no meaningful advantage over CLI-driven for a developer-first tool |
| **PowerShell install script** | Adds a script that must be separately downloaded and trusted; does not simplify the admin requirement; CLI approach is cleaner |
| **ClickOnce** | No service registration support; primarily for UI applications |
| **Store port config in `%APPDATA%`** | Service runs as SYSTEM; SYSTEM cannot read the installing user's roaming profile. Config must be in `%ProgramData%` to be readable by both the service and the CLI. |
| **Bake port into service binary path args** | `sc config binPath=` with embedded args works but makes `config get` awkward and couples the port to the service registration record rather than a readable config file. |

## References

- [System.ServiceProcess.ServiceController — docs](https://learn.microsoft.com/en-us/dotnet/api/system.serviceprocess.servicecontroller)
- [WiX 4 — deferred to v2+](https://wixtoolset.org/)
- ADR-002: Single executable — CLI and Service in one binary
