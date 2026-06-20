# ADR-003: DataProtection over Raw DPAPI for Secret Storage

**Date:** 2026-06-20  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

The service generates a bearer token on first run and must persist it securely so it can be read back by the `configure` and `token` CLI commands (which run in a separate process from the long-lived service). The token must survive reboots, re-installations, and service restarts without changing — MCP client config files embed the token and would break if it rotated.

Windows Data Protection API (DPAPI) via `System.Security.Cryptography.ProtectedData` is the obvious native answer on Windows. It encrypts data tied to the current Windows user account, requires no key management, and has zero setup. However, calling it directly binds the implementation to `System.Security.Cryptography.ProtectedData` and the Windows-only API.

The project targets Windows in v1 but the design explicitly defers to Linux (systemd) and macOS (launchd) in v3. Core service code is intended to be platform-agnostic.

## Decision

We will use **`Microsoft.AspNetCore.DataProtection`** instead of calling DPAPI directly.

The `IDataProtector` interface is registered via `services.AddDataProtection()` with a persisted key ring location (`%ProgramData%\ContextBridge\keys`). The `TokenStore` class in `ContextBridge.Infrastructure` depends only on `IDataProtector` — no platform-conditional code.

On Windows, DataProtection uses DPAPI internally to protect the key ring at rest. On Linux, it uses a key ring file. On macOS, it can be configured to use the Keychain. The platform-specific protection mechanism is resolved by the framework, not by application code.

## Consequences

### Positive
- `TokenStore` is platform-agnostic — no `#if WINDOWS` guards or runtime OS checks
- Key ring at `%ProgramData%\ContextBridge\keys` is accessible to the SYSTEM account (service) and to admin users (CLI commands), matching the required access pattern without extra ACL configuration
- `IDataProtector` is available from the standard DI container — no special wiring in test scenarios
- Framework handles key rotation and versioning automatically if needed in future

### Negative
- DataProtection is an ASP.NET Core package — it pulls in more than strictly necessary for a service that is not yet hosting HTTP endpoints in Phase 1. This dependency arrives before Kestrel is wired up.
- Key ring files are stored unencrypted on Linux unless additionally protected; acceptable for v1 given the Windows-first scope

### Neutral / Trade-offs
- Key ring location (`%ProgramData%\ContextBridge\keys`) must exist before the service can write keys on first run — the `TokenStore` is responsible for ensuring the directory exists at initialization time

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| `System.Security.Cryptography.ProtectedData` (raw DPAPI) | Windows-only; would require platform guards or a separate abstraction when v3 adds Linux/macOS support |
| Store token as plaintext in `%ProgramData%\ContextBridge\token.txt` | No meaningful protection against local process reads; the threat model requires at least basic defense-in-depth |
| Windows Credential Manager (`Windows.Security.Credentials`) | Windows-only WinRT API; even harder to abstract than DPAPI; no .NET standard cross-platform path |
| Azure Key Vault | External dependency; defeats the zero-external-dependencies design goal |

## References

- [Microsoft.AspNetCore.DataProtection — docs](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction)
- [DataProtection key storage providers](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers)
- ADR-001: .NET Worker Service technology selection
