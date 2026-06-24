using System.Globalization;
using ContextBridge.Core.Models;
using ContextBridge.Core.Repositories;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ContextBridge.Infrastructure.Storage;

public sealed class HandoffRepository(SqliteConnectionFactory factory) : IHandoffRepository
{
    public async Task<long> WriteAsync(
        string content,
        string? project,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        await using var connection = factory.Create();

        string now = UtcNow();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO handoffs (content, project, created_at, expires_at) VALUES (@content, @project, @now, @expiresAt)",
            new { content, project, now, expiresAt = expiresAt.ToString("O") },
            cancellationToken: ct));

        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition("SELECT last_insert_rowid()", cancellationToken: ct));
    }

    public async Task<IReadOnlyList<HandoffRecord>> ListAsync(
        string? project = null,
        CancellationToken ct = default)
    {
        await using var connection = factory.Create();

        string sql;
        object param;

        if (project is not null)
        {
            sql = """
                SELECT id AS Id, content AS Content, project AS Project, created_at AS CreatedAt, expires_at AS ExpiresAt
                FROM handoffs
                WHERE expires_at > @now AND project = @project
                ORDER BY created_at DESC
                """;
            param = new { now = UtcNow(), project };
        }
        else
        {
            sql = """
                SELECT id AS Id, content AS Content, project AS Project, created_at AS CreatedAt, expires_at AS ExpiresAt
                FROM handoffs
                WHERE expires_at > @now
                ORDER BY created_at DESC
                """;
            param = new { now = UtcNow() };
        }

        var rows = await connection.QueryAsync<HandoffRow>(new CommandDefinition(sql, param, cancellationToken: ct));
        return rows.Select(ToRecord).ToList();
    }

    public async Task<bool> AcknowledgeAsync(long id, CancellationToken ct = default)
    {
        await using var connection = factory.Create();

        int rows = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM handoffs WHERE id = @id",
            new { id },
            cancellationToken: ct));

        return rows > 0;
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        await using var connection = factory.Create();

        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM handoffs WHERE expires_at < @now",
            new { now = UtcNow() },
            cancellationToken: ct));
    }

    private static HandoffRecord ToRecord(HandoffRow row) =>
        new(row.Id, row.Content, row.Project,
            DateTimeOffset.Parse(row.CreatedAt, null, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(row.ExpiresAt, null, DateTimeStyles.RoundtripKind));

    private static string UtcNow() => DateTimeOffset.UtcNow.ToString("O");

    private sealed record HandoffRow(long Id, string Content, string? Project, string CreatedAt, string ExpiresAt);
}
