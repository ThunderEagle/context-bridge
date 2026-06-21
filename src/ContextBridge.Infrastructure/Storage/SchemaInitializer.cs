using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ContextBridge.Infrastructure.Storage;

public sealed class SchemaInitializer(SqliteConnectionFactory factory)
{
    private const int CurrentVersion = 1;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = factory.Create();

        int version = await GetSchemaVersionAsync(connection, ct);

        if (version < CurrentVersion)
        {
            await ApplyMigrationsAsync(connection, version, ct);
        }
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection, CancellationToken ct)
    {
        var tableExists = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version'",
                cancellationToken: ct));

        if (tableExists == 0)
        {
            return 0;
        }

        var version = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition("SELECT version FROM schema_version LIMIT 1", cancellationToken: ct));

        return version ?? 0;
    }

    private static async Task ApplyMigrationsAsync(SqliteConnection connection, int fromVersion, CancellationToken ct)
    {
        await using var transaction = await connection.BeginTransactionAsync(ct);

        if (fromVersion < 1)
        {
            await ApplyV1Async(connection, transaction, ct);
        }

        if (fromVersion == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_version (version) VALUES (@version)",
                new { version = CurrentVersion },
                transaction,
                cancellationToken: ct));
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE schema_version SET version = @version",
                new { version = CurrentVersion },
                transaction,
                cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
    }

    private static async Task ApplyV1Async(SqliteConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS memories (
                id         INTEGER PRIMARY KEY,
                content    TEXT    NOT NULL,
                created_at TEXT    NOT NULL,
                updated_at TEXT    NOT NULL,
                is_deleted INTEGER NOT NULL DEFAULT 0
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
            """,
            transaction: transaction,
            cancellationToken: ct));
    }
}
