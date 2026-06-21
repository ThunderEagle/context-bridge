# ADR-008: Schema Migration via schema_version Table + Startup DDL

**Date:** 2026-06-21  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

ContextBridge stores data in a SQLite file that persists across service restarts and updates. Any schema change shipped in a new version must migrate existing databases cleanly — adding columns, creating tables, or restructuring data without requiring the user to delete their database and start over.

Two categories of schema change are expected:
- **Additive** — new tables, new nullable columns, new indexes (safe to apply at any point)
- **Restructuring** — column renames, type changes, data migrations (require careful sequencing)

The tool has no user-facing migration workflow (no CLI command to "apply migrations"). Migrations must run automatically on service startup, invisibly to the user.

## Decision

We will use a **`schema_version` table containing a single integer version number** combined with **explicit DDL statements applied at startup** by `SchemaInitializer`.

`SchemaInitializer` runs on every service startup via `Worker.ExecuteAsync`. It:

1. Creates the `schema_version` table if it does not exist
2. Reads the current version (0 if the table is empty)
3. Applies all migration blocks from `(current version, CurrentVersion]` in order
4. Updates `schema_version.version` to `CurrentVersion`

Each migration block is an idempotent set of DDL statements. `CREATE TABLE IF NOT EXISTS` and `CREATE VIRTUAL TABLE IF NOT EXISTS` are used for v1 initial schema. Future migrations use `ALTER TABLE ADD COLUMN IF NOT EXISTS` (SQLite 3.37+) or explicit version checks.

All migration steps within a version run in a single transaction. If a migration fails mid-way, the transaction rolls back and the service refuses to start, preserving database integrity.

```sql
-- Written once at v1 initial creation
CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);

CREATE TABLE IF NOT EXISTS memories (
    id          INTEGER PRIMARY KEY,
    content     TEXT    NOT NULL,
    created_at  TEXT    NOT NULL,
    updated_at  TEXT    NOT NULL,
    is_deleted  INTEGER NOT NULL DEFAULT 0
);

CREATE VIRTUAL TABLE IF NOT EXISTS memories_vec USING vec0(
    embedding float[384]
);

CREATE TABLE IF NOT EXISTS tags (
    id   INTEGER PRIMARY KEY,
    name TEXT    NOT NULL UNIQUE COLLATE NOCASE
);

CREATE TABLE IF NOT EXISTS memory_tags (
    memory_id INTEGER NOT NULL REFERENCES memories(id),
    tag_id    INTEGER NOT NULL REFERENCES tags(id),
    PRIMARY KEY (memory_id, tag_id)
);
```

## Consequences

### Positive
- Zero external dependencies for migrations — no FluentMigrator, no EF Core Migrations, no DbUp
- Startup migration is invisible to the user; no manual intervention required
- The migration history is auditable via the version number (and via git history of `SchemaInitializer.cs`)
- Idempotent DDL statements make the initializer safe to re-run if the process is interrupted mid-startup
- Runs inside a SQLite transaction — partial migrations never leave the database in an inconsistent state

### Negative
- Migration scripts are embedded in C# source rather than `.sql` files — less convenient for DBA-style review, but acceptable for a single-developer personal tool
- No automatic rollback path: if v2 schema is incompatible with v1 code and the user downgrades, they may need to delete the database (documented as a known limitation in v1)
- Schema history is a single integer, not a full migration log — no record of *when* each migration ran

### Neutral / Trade-offs
- All DDL is in one place (`SchemaInitializer.cs`) — a design choice that trades discoverability for simplicity at this scale

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| EF Core Migrations | Requires EF Core dependency (rejected in ADR-007); generates snapshot files that conflict with manual schema management |
| FluentMigrator | Well-regarded but adds a dependency, a migration runner hosted service, and an additional abstraction layer for a schema that will have <10 tables in v1 |
| DbUp | Same trade-off as FluentMigrator — external dependency and runner complexity not justified by the schema size |
| No migration (recreate on startup) | Destroys user data on every update — unacceptable for a memory tool |
| Plain SQL files in `resources/` | Cleaner separation but requires an embedded resource loader; no meaningful benefit over inline DDL at this scale |

## References
- ADR-007: Dapper data access (SchemaInitializer uses Dapper for DDL execution)
- [SQLite `ALTER TABLE`](https://www.sqlite.org/lang_altertable.html)
- [sqlite-vec virtual table DDL](https://alexgarcia.xyz/sqlite-vec/api-reference.html)
