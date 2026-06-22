# ADR-015: Localhost-Bind-Only as the v1 Security Model

**Date:** 2026-06-22  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

Two previous approaches to service security were evaluated and rejected:

1. **ADR-003 — Bearer token via DataProtection:** A random token required on all inbound requests. Removed because on a single-user localhost service, a token stored in a DataProtection-encrypted file on the same machine provides no meaningful protection against a local attacker who already has code execution. The token was purely defensive boilerplate, and its distribution ceremony (the `configure` command writing it to client config files) added friction and complexity.

2. **ADR-014 — HTTPS self-signed certificate:** A programmatic RSA-2048 cert generated at install time and installed to `LocalMachine\My` + `LocalMachine\Root`. Reverted because Claude Code's MCP client (Node.js `fetch`) rejects self-signed certificates with `UNABLE_TO_VERIFY_LEAF_SIGNATURE`. Disabling TLS verification globally (`NODE_TLS_REJECT_UNAUTHORIZED=0`) would be a worse posture than plain HTTP.

Both attempts added complexity without improving the realistic security outcome for a personal developer tool on a machine the user controls.

## Decision

The v1 security perimeter is **exclusively the Kestrel bind address: `127.0.0.1` (loopback only, never `0.0.0.0`)**.

- No authentication (no bearer token, no API key)
- No TLS (plain HTTP)
- Service is unreachable from any other machine regardless of firewall state

## Consequences

### Positive
- Zero credential distribution: `configure` writes no secrets to client config files
- Zero cert lifecycle management
- All MCP clients (Claude Code, Claude Desktop via stdio, future clients) connect identically with no per-client credential setup
- No token-related startup sequence; service starts and is immediately usable

### Accepted Risk
A local attacker with code execution on the machine can:
- Connect to the service at `127.0.0.1:5290/mcp` and call any MCP tool
- Read the SQLite database directly at `%ProgramData%\ContextBridge\memories.db`

Both vectors require local code execution — at that point the attacker has broad access to the machine regardless. This is accepted for a personal developer tool.

## Future Options

If security requirements evolve (e.g., multi-user machines, organizational deployment):

- **Named pipe (Windows) / Unix socket (cross-platform):** The OS enforces user-level ACL at the transport layer. No credential needed. Kestrel supports both. Blocked only by MCP client support for non-HTTP transport URLs — if that arrives, this is the architecturally correct path.
- **Bearer token (restored):** Could be reinstated if the distribution complexity is acceptable and a real threat model justifies it.

## References

- ADR-003: DataProtection bearer token (Superseded)
- ADR-014: HTTPS self-signed cert (Reverted)
