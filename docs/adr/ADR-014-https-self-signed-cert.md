# ADR-014: HTTPS via Programmatically-Generated Self-Signed Certificate

**Date:** 2026-06-22  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

Claude Desktop's Connectors UI requires HTTPS for all remote MCP server URLs — it rejects `http://` entries outright. ContextBridge currently binds Kestrel to `http://127.0.0.1:{port}`, which works for Claude Code but blocks Connectors adoption.

Two approaches exist for adding HTTPS to a localhost-only Windows Service without requiring the end user to own a public domain or purchase a certificate:

1. **`dotnet dev-certs https --trust`** — a dotnet SDK CLI tool that generates a localhost development certificate and trusts it. Requires the .NET SDK installed on the end-user machine (not the Runtime). The cert lands in the current user's personal store, which the Windows Service (running as LocalSystem) cannot access without additional ACL changes.

2. **Programmatic self-signed certificate at install time** — generate the certificate inside application code using `System.Security.Cryptography.X509Certificates.CertificateRequest` (part of the .NET Runtime, no SDK required). Install to `LocalMachine\My` with `MachineKeySet | PersistKeySet` flags so the service account can load it. Trust it via `LocalMachine\Root`. Store the thumbprint in `%ProgramData%\ContextBridge\appsettings.json` for Kestrel to load on startup.

## Decision

Use **Option 2 — programmatic self-signed certificate generated at `service install` time**.

### Certificate properties

| Property | Value |
|---|---|
| Subject | `CN=ContextBridge` |
| Subject Alternative Names | `DNS:localhost`, `IP:127.0.0.1` |
| Key algorithm | RSA 2048 |
| Signature hash | SHA-256 |
| Validity | 10 years from install date |
| Key storage | `X509KeyStorageFlags.MachineKeySet \| PersistKeySet \| Exportable` |
| Stores | `LocalMachine\My` (for Kestrel to load) + `LocalMachine\Root` (trust anchor) |

### Kestrel configuration

Kestrel is configured to load the certificate by thumbprint from `LocalMachine\My` at startup. The thumbprint is written to `%ProgramData%\ContextBridge\appsettings.json` during install. This avoids exporting to a `.pfx` file and removes any file-path dependency.

```json
// %ProgramData%\ContextBridge\appsettings.json (written by service install)
{
  "ServiceConfig": {
    "Port": 5290,
    "CertificateThumbprint": "<hex-thumbprint>"
  }
}
```

Kestrel `ConfigureHttpsDefaults` loads the cert:

```csharp
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.Loopback, port, listenOptions =>
    {
        listenOptions.UseHttps(httpsOptions =>
        {
            httpsOptions.ServerCertificateSelector = (_, _) =>
                LoadCertByThumbprint(thumbprint);
        });
    });
});
```

### Transport change: HTTP → HTTPS only

The service switches entirely from HTTP to HTTPS on the same port (5290). Dual-listener (HTTP + HTTPS) is not used — a single HTTPS endpoint is simpler and the localhost bind is already the security perimeter. The `configure` command writes `https://127.0.0.1:{port}/mcp`.

### Install-time certificate lifecycle

- **Install**: Generate cert if none exists with a valid thumbprint in config. Write thumbprint to `appsettings.json`. Add to `LocalMachine\My` and `LocalMachine\Root`.
- **Reinstall**: If a cert with the stored thumbprint exists and has >30 days remaining validity, skip generation.
- **Uninstall**: Remove the cert from `LocalMachine\My` and `LocalMachine\Root` by thumbprint.

## Consequences

### Positive
- No .NET SDK required on end-user machines — all crypto APIs are in the Runtime
- Certificate is accessible to the Windows Service account (LocalMachine store, MachineKeySet flag)
- Claude Desktop Connectors can connect via `https://127.0.0.1:5290/mcp`
- Dashboard accessible at `https://127.0.0.1:5290/dashboard` without credential friction
- Certificate lifecycle (generate, trust, remove) is fully automated within the existing admin-required install/uninstall flow
- 10-year validity matches the expected single-machine lifetime of a personal tool

### Negative
- Adding a self-signed cert to `LocalMachine\Root` is a meaningful trust operation — the user must understand this is a localhost-only cert. The install output should state this explicitly.
- Certificate generation adds ~100ms to `service install` time (RSA key generation)
- If the user reinstalls Windows, the cert is lost and `service install` must be re-run (expected behavior)

### Neutral / Trade-offs
- The thumbprint in `appsettings.json` is not a secret — it is a certificate identifier, not a private key
- Private key material never leaves the machine; the cert is not exportable to `.pfx` (use `Exportable` flag only if future tooling needs it — omit for now)

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| `dotnet dev-certs https --trust` | Requires .NET SDK on end-user machine; cert in user store, inaccessible to service account |
| Dual listener (HTTP + HTTPS) | Unnecessary complexity; Claude Code accepts HTTPS equally well |
| Let's Encrypt / ACME | Requires a public domain and outbound ACME traffic; incompatible with the zero-external-dependencies goal |
| Keep HTTP, accept Connectors limitation | Closes off Connectors entirely; worth fixing now while the install flow is being worked |

## References

- ADR-010: Streamable HTTP (now HTTPS) transport
- ADR-004: CLI-driven service registration (install/uninstall flow that cert lifecycle hooks into)
- ADR-003 (Superseded): DataProtection for bearer token storage — removed; cert thumbprint replaces it as the only persisted security artifact
