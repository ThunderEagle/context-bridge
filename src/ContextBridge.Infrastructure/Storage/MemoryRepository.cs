using System.Data;
using System.Data.Common;
using System.Globalization;
using ContextBridge.Core.Models;
using ContextBridge.Core.Repositories;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ContextBridge.Infrastructure.Storage;

public sealed class MemoryRepository(SqliteConnectionFactory factory, ILogger<MemoryRepository> logger) : IMemoryRepository
{
    public async Task<long> WriteAsync(
        string content,
        float[] embedding,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        await using var connection = factory.Create();
        await using var transaction = await connection.BeginTransactionAsync(ct);

        long id = await InsertMemoryAsync(connection, transaction, content, ct);
        await InsertEmbeddingAsync(connection, transaction, id, embedding, ct);

        if (tags is not null)
        {
            await UpsertTagsAsync(connection, transaction, id, tags, ct);
        }

        await transaction.CommitAsync(ct);
        if (logger.IsEnabled(LogLevel.Debug)) { logger.LogDebug("memory written id={Id}", id); }
        return id;
    }

    public async Task<IReadOnlyList<long>> BatchWriteAsync(
        IEnumerable<NewMemory> entries,
        CancellationToken ct = default)
    {
        await using var connection = factory.Create();
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var ids = new List<long>();
        foreach (var entry in entries)
        {
            long id = await InsertMemoryAsync(connection, transaction, entry.Content, ct);
            await InsertEmbeddingAsync(connection, transaction, id, entry.Embedding, ct);

            if (entry.Tags is not null)
            {
                await UpsertTagsAsync(connection, transaction, id, entry.Tags, ct);
            }

            ids.Add(id);
        }

        await transaction.CommitAsync(ct);
        if (logger.IsEnabled(LogLevel.Debug)) { logger.LogDebug("batch write committed count={Count}", ids.Count); }
        return ids;
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        float[] queryEmbedding,
        int limit = 10,
        CancellationToken ct = default)
    {
        await using var connection = factory.Create();

        // Oversample to account for soft-deleted rows filtered out after the KNN pass
        int oversample = limit * 3;
        var candidates = (await connection.QueryAsync<VecCandidate>(
            new CommandDefinition(
                """
                SELECT rowid AS Rowid, distance AS Distance
                FROM memories_vec
                WHERE embedding MATCH @embedding
                ORDER BY distance
                LIMIT @oversample
                """,
                new { embedding = SerializeEmbedding(queryEmbedding), oversample },
                cancellationToken: ct))).ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        var rowids = candidates.Select(c => c.Rowid).ToList();
        var rows = (await connection.QueryAsync<MemoryRow>(
            new CommandDefinition(
                """
                SELECT id AS Id, content AS Content, created_at AS CreatedAt, updated_at AS UpdatedAt
                FROM memories
                WHERE id IN @rowids AND is_deleted = 0
                """,
                new { rowids },
                cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return [];
        }

        var memoryIds = rows.Select(r => r.Id).ToList();
        var tagRows = await LoadTagsForMemoriesAsync(connection, memoryIds, ct);
        var tagsByMemoryId = GroupTagsByMemoryId(tagRows);

        var distanceByRowid = candidates.ToDictionary(c => c.Rowid, c => c.Distance);

        var results = rows
            .OrderBy(r => distanceByRowid[r.Id])
            .Take(limit)
            .Select(r => new MemorySearchResult(
                ToRecord(r, tagsByMemoryId.GetValueOrDefault(r.Id, [])),
                (float)distanceByRowid[r.Id]))
            .ToList();

        if (logger.IsEnabled(LogLevel.Debug)) { logger.LogDebug("search returned {ResultCount} of {CandidateCount} candidates limit={Limit}", results.Count, candidates.Count, limit); }
        return results;
    }

    public async Task<(IReadOnlyList<MemoryRecord> Items, int TotalCount)> ListAsync(
        int page,
        int pageSize,
        IEnumerable<string>? tags = null,
        CancellationToken ct = default)
    {
        await using var connection = factory.Create();

        var tagList = tags?.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        bool hasTagFilter = tagList is { Count: > 0 };
        int offset = (page - 1) * pageSize;

        // Build query conditionally to avoid string interpolation fragility
        string countSql;
        string querySql;
        object countParam;
        object param;

        if (hasTagFilter)
        {
            countSql = """
                SELECT COUNT(DISTINCT m.id)
                FROM memories m
                JOIN memory_tags mt ON mt.memory_id = m.id
                JOIN tags t ON t.id = mt.tag_id
                WHERE m.is_deleted = 0
                AND t.name IN @tagNames
                """;

            querySql = """
                SELECT DISTINCT m.id AS Id, m.content AS Content, m.created_at AS CreatedAt, m.updated_at AS UpdatedAt
                FROM memories m
                JOIN memory_tags mt ON mt.memory_id = m.id
                JOIN tags t ON t.id = mt.tag_id
                WHERE m.is_deleted = 0
                AND t.name IN @tagNames
                ORDER BY m.created_at DESC
                LIMIT @pageSize OFFSET @offset
                """;

            countParam = new { tagNames = tagList };
            param = new { tagNames = tagList, pageSize, offset };
        }
        else
        {
            countSql = """
                SELECT COUNT(m.id)
                FROM memories m
                WHERE m.is_deleted = 0
                """;

            querySql = """
                SELECT m.id AS Id, m.content AS Content, m.created_at AS CreatedAt, m.updated_at AS UpdatedAt
                FROM memories m
                WHERE m.is_deleted = 0
                ORDER BY m.created_at DESC
                LIMIT @pageSize OFFSET @offset
                """;

            countParam = new { };
            param = new { pageSize, offset };
        }

        int totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, countParam, cancellationToken: ct));

        var rows = (await connection.QueryAsync<MemoryRow>(
            new CommandDefinition(querySql, param, cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return ([], totalCount);
        }

        var memoryIds = rows.Select(r => r.Id).ToList();
        var tagRows = await LoadTagsForMemoriesAsync(connection, memoryIds, ct);
        var tagsByMemoryId = GroupTagsByMemoryId(tagRows);

        var items = rows.Select(r => ToRecord(r, tagsByMemoryId.GetValueOrDefault(r.Id, []))).ToList();
        return (items, totalCount);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var connection = factory.Create();

        int rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE memories SET is_deleted = 1, updated_at = @updatedAt WHERE id = @id AND is_deleted = 0",
            new { id, updatedAt = UtcNow() },
            cancellationToken: ct));

        if (logger.IsEnabled(LogLevel.Debug)) { logger.LogDebug("memory delete id={Id} found={Found}", id, rows > 0); }
        return rows > 0;
    }

    public async Task<bool> UpdateAsync(
        long id,
        string content,
        float[] embedding,
        CancellationToken ct = default)
    {
        await using var connection = factory.Create();
        await using var transaction = await connection.BeginTransactionAsync(ct);

        int rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE memories SET content = @content, updated_at = @updatedAt WHERE id = @id AND is_deleted = 0",
            new { id, content, updatedAt = UtcNow() },
            transaction,
            cancellationToken: ct));

        if (rows == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        // Replace the stored embedding
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM memories_vec WHERE rowid = @id",
            new { id }, transaction, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO memories_vec (rowid, embedding) VALUES (@id, @embedding)",
            new { id, embedding = SerializeEmbedding(embedding) },
            transaction,
            cancellationToken: ct));

        await transaction.CommitAsync(ct);
        if (logger.IsEnabled(LogLevel.Debug)) { logger.LogDebug("memory update id={Id} found=true", id); }
        return true;
    }

    public async Task<StorageStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await using var connection = factory.Create();

        var row = await connection.QuerySingleAsync<StatusRow>(
            new CommandDefinition(
                """
                SELECT
                    COUNT(*) AS TotalCount,
                    COUNT(CASE WHEN is_deleted = 0 THEN 1 END) AS ActiveCount,
                    COUNT(CASE WHEN is_deleted = 1 THEN 1 END) AS DeletedCount
                FROM memories
                """,
                cancellationToken: ct));

        return new StorageStatus(row.TotalCount, row.ActiveCount, row.DeletedCount);
    }

    // --- Private helpers ---

    private static async Task<long> InsertMemoryAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string content,
        CancellationToken ct)
    {
        string now = UtcNow();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO memories (content, created_at, updated_at) VALUES (@content, @now, @now)",
            new { content, now },
            transaction,
            cancellationToken: ct));

        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition("SELECT last_insert_rowid()", transaction: transaction, cancellationToken: ct));
    }

    private static Task InsertEmbeddingAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        long id,
        float[] embedding,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO memories_vec (rowid, embedding) VALUES (@id, @embedding)",
            new { id, embedding = SerializeEmbedding(embedding) },
            transaction,
            cancellationToken: ct));

    private static async Task UpsertTagsAsync(
        SqliteConnection connection,
        IDbTransaction transaction,
        long memoryId,
        IEnumerable<string> tags,
        CancellationToken ct)
    {
        foreach (var tag in tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT OR IGNORE INTO tags (name) VALUES (@tag)",
                new { tag }, transaction, cancellationToken: ct));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT OR IGNORE INTO memory_tags (memory_id, tag_id)
                SELECT @memoryId, id FROM tags WHERE name = @tag
                """,
                new { memoryId, tag }, transaction, cancellationToken: ct));
        }
    }

    private static async Task<IEnumerable<TagRow>> LoadTagsForMemoriesAsync(
        SqliteConnection connection,
        IReadOnlyList<long> memoryIds,
        CancellationToken ct) =>
        await connection.QueryAsync<TagRow>(
            new CommandDefinition(
                """
                SELECT mt.memory_id AS MemoryId, t.name AS TagName
                FROM memory_tags mt
                INNER JOIN tags t ON t.id = mt.tag_id
                WHERE mt.memory_id IN @memoryIds
                """,
                new { memoryIds },
                cancellationToken: ct));

    private static Dictionary<long, List<string>> GroupTagsByMemoryId(IEnumerable<TagRow> tagRows)
    {
        var result = new Dictionary<long, List<string>>();
        foreach (var row in tagRows)
        {
            if (!result.TryGetValue(row.MemoryId, out var list))
            {
                list = [];
                result[row.MemoryId] = list;
            }
            list.Add(row.TagName);
        }
        return result;
    }

    private static MemoryRecord ToRecord(MemoryRow row, IReadOnlyList<string> tags) =>
        new(row.Id, row.Content, tags,
            DateTimeOffset.Parse(row.CreatedAt, null, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(row.UpdatedAt, null, DateTimeStyles.RoundtripKind));

    private static byte[] SerializeEmbedding(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static string UtcNow() => DateTimeOffset.UtcNow.ToString("O");

    // --- Dapper mapping types ---

    private sealed record MemoryRow(long Id, string Content, string CreatedAt, string UpdatedAt);
    private sealed record VecCandidate(long Rowid, double Distance);
    private sealed record TagRow(long MemoryId, string TagName);
    private sealed record StatusRow(long TotalCount, long ActiveCount, long DeletedCount);
}
