# ADR-013: Read-Only Web Dashboard — Auth Exemption and Implementation Approach

**Date:** 2026-06-22  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

A read-only web dashboard for monitoring memory state (counts, recent writes, semantic search) is useful during the pre-release testing phase and adds lasting value as a lightweight operational tool. The design doc deferred it to v2+, but the HTTP host is already running and the IMemoryRepository interface already exposes everything the dashboard needs. Building it now is low-friction.

Two design questions need explicit decisions before implementation:

**1. Authentication** — the existing bearer token middleware protects all endpoints except `/health`. A browser cannot easily send an `Authorization: Bearer <token>` header without custom JavaScript or browser extensions. The options are:
- Token-in-query-param (stored in sessionStorage/localStorage after first use)
- Exclude dashboard from bearer auth

**2. Implementation approach** — options range from a single inline HTML page to a separate frontend project.

## Decision

### Auth: Exempt `/dashboard` and `/api/dashboard/*` from bearer token enforcement

The bearer token is a defense-in-depth measure against blind port scanning on localhost. The actual security boundary is `127.0.0.1`-only Kestrel binding — only processes on the local machine can reach the server at all. The `/health` endpoint is already exempt using this same rationale.

The dashboard is read-only. It exposes no write surface (no delete, no create, no update). The worst-case unauthorized read from a local attacker is access to stored memory text — which that attacker could already read by opening the SQLite file directly in `%PROGRAMDATA%\ContextBridge\memories.db`.

Exempting the dashboard from bearer auth avoids:
- Storing the token in browser localStorage (a minor but real XSS risk if the page ever evolves)
- Requiring the user to manually paste the token into the browser

### Implementation: Self-contained HTML served from a C# string constant

A single `DashboardHtml.cs` file holds the entire HTML/CSS/JS page as a string constant. The page uses the browser-native `fetch()` API to call JSON endpoints on the same origin — no external CDN, no npm, no build step, no embedded resources, no separate frontend project.

`DashboardEndpoints.cs` provides:
- `GET /dashboard` — returns `text/html`; the BearerTokenMiddleware exempt list is extended to include this prefix
- `GET /api/dashboard/stats` — returns JSON counts + model info from `IMemoryRepository.GetStatusAsync`
- `GET /api/dashboard/memories?page&pageSize&tag&q` — unified list + search endpoint; `q` triggers `IMemoryRepository.SearchAsync` (requires `IEmbeddingGenerator`); omitting `q` calls `IMemoryRepository.ListAsync`

## Consequences

### Positive
- Dashboard is immediately accessible at `http://127.0.0.1:5290/dashboard` with no credential friction
- No build pipeline — the HTML lives in the C# codebase and is compiled into the binary
- Read-only surface with no write endpoints means the auth exemption carries negligible security cost on localhost

### Negative
- The dashboard becomes unauthenticated. If the port binding were ever changed to `0.0.0.0` (which would violate ADR-010), the dashboard would expose memory content to the network without auth
- Inline HTML in a C# string is harder to maintain than a proper frontend project as complexity grows

### Neutral / Trade-offs
- If the dashboard ever needs write capability (delete, tag editing), bearer auth should be reinstated using a sessionStorage token pattern — the inline HTML approach makes that straightforward

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| Token-in-query-param (e.g. `?token=xxx`) | Still requires manual copy-paste; stores token in browser history |
| Token entered via HTML form → stored in sessionStorage | Acceptable for a longer-lived dashboard; overkill for a monitoring tool used on the developer's own machine |
| Blazor / Razor Pages | Adds project complexity, build step, and NuGet dependencies that aren't justified for a dev-time monitoring page |
| Separate npm frontend project | Substantial build infrastructure for a tool with a handful of UI elements |

## References
- ADR-010: Streamable HTTP transport (established localhost-only binding)
- ADR-009: Pure vector search (SearchAsync signature used by the `/api/dashboard/memories?q=` endpoint)
