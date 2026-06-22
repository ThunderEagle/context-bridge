# ADR-003: DataProtection over Raw DPAPI for Secret Storage

**Date:** 2026-06-20  
**Status:** Superseded by decision made 2026-06-22  
**Deciders:** Scott Williams

---

## Status Note

This ADR is superseded. The bearer token and the entire auth layer were removed (see decision below). `Microsoft.AspNetCore.DataProtection`, `TokenStore`, and `BearerTokenMiddleware` were deleted. The security perimeter is now the Kestrel bind address (`127.0.0.1`) alone.

**Reason for reversal:** On a single-user localhost service, a bearer token stored in a DataProtection-encrypted file on the same machine provides no meaningful protection against a local attacker who already has code execution. The token was purely defensive boilerplate. Removing it eliminates complexity, removes the token distribution ceremony from `configure`, and enables Claude Desktop Connectors compatibility without requiring OAuth.

---

## Original Decision (Archived)

The service generated a bearer token on first run and persisted it via `Microsoft.AspNetCore.DataProtection` to `%ProgramData%\ContextBridge\token.dat`. The `TokenStore` class in `ContextBridge.Infrastructure` depended only on `IDataProtector` — no platform-conditional code. On Windows, DataProtection used DPAPI internally to protect the key ring at rest.

## Alternatives Considered (original)

| Option | Reason Rejected |
|---|---|
| `System.Security.Cryptography.ProtectedData` (raw DPAPI) | Windows-only |
| Plaintext in `%ProgramData%\ContextBridge\token.txt` | No meaningful protection |
| Windows Credential Manager | Windows-only WinRT API |
| Azure Key Vault | External dependency |

## References

- ADR-001: .NET Worker Service technology selection
