# ADR-007: Dapper over EF Core for Data Access

**Date:** 2026-06-21  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

Phase 3 introduces a SQLite storage layer for memories. The data access layer must support:

1. Standard CRUD against the `memories`, `tags`, and `memory_tags` tables
2. Vector similarity queries against the `memories_vec` sqlite-vec virtual table (`MATCH` syntax, `distance` column)
3. Tag-based filtering via JOIN across the junction table

The sqlite-vec `vec0` virtual table uses non-standard SQL syntax (`WHERE embedding MATCH ?`, `k = N`) that sits entirely outside the query expression trees that ORMs generate. EF Core has no mapping concept for virtual tables or the `MATCH` operator.

## Decision

We will use **Dapper** for all data access. All SQL is written explicitly; no ORM generates queries.

EF Core is not used. Not even for the non-vec tables.

Keeping the ORM boundary at zero eliminates the hybrid pattern where some queries go through EF and others bypass it via `FromSqlRaw` or raw `SqliteConnection`. Having two data access paths in the same layer increases cognitive load and introduces friction whenever a developer adds a query that touches both a mapped entity and a vec0 table in the same transaction.

## Consequences

### Positive
- Full control over every SQL statement — critical for the sqlite-vec MATCH syntax and `last_insert_rowid()` dependency
- No migration scaffolding or model snapshot files — schema DDL is explicit in `SchemaInitializer`
- Dapper's `CommandDefinition` carries `CancellationToken` through every async call without any additional plumbing
- Zero ORM startup cost — no model validation, shadow property scanning, or change tracker initialization
- The sqlite-vec row insertion sequence (INSERT memories → get rowid → INSERT memories_vec) requires an explicit transaction with two INSERT statements; Dapper makes this natural

### Negative
- No change tracking — update operations require explicit `UPDATE` statements for every modified field
- Relationship loading is manual (separate queries + in-code join) rather than declarative includes
- SQL strings live in C# source rather than `.sql` files — slightly harder to test in isolation via a SQL client

### Neutral / Trade-offs
- Schema migrations are handled by `SchemaInitializer` (ADR-008), not by EF Core Migrations — a deliberate choice driven by the desire to avoid the EF Core dependency, not a consequence of using Dapper

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| EF Core with `FromSqlRaw` for vec0 queries | Creates a two-tier data access pattern (EF for entities, raw SQL for vector ops) in the same layer; sqlite-vec virtual tables cannot be mapped as EF entities; migrations would own schema DDL while SchemaInitializer also needs to own the vec0 DDL |
| EF Core only, skip vec0 at ORM level | EF Core still imposes the migration scaffold, startup model validation, and change tracker overhead; no benefit offsets those costs given that >30% of queries are raw SQL anyway |
| linq2db | Better raw SQL story than EF Core but still adds a dependency and mapping layer with no clear benefit over Dapper for this query volume |

## References
- ADR-008: Schema migration strategy
- ADR-009: Pure vector search (sqlite-vec MATCH syntax)
- [Dapper GitHub](https://github.com/DapperLib/Dapper)
- [sqlite-vec query syntax](https://alexgarcia.xyz/sqlite-vec/api-reference.html)
