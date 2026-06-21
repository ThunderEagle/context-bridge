using ContextBridge.Core.Models;
using ContextBridge.Core.Repositories;
using ContextBridge.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace ContextBridge.Tests.Storage;

public sealed class MemoryRepositoryTests : IAsyncLifetime
{
    private string _dbPath = string.Empty;
    private string? _vecExtensionPath;
    private SqliteConnectionFactory _factory = null!;
    private IMemoryRepository _repository = null!;
    private string? _skipReason;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"context-bridge-test-{Guid.NewGuid():N}.db");
        _vecExtensionPath = SqliteConnectionFactory.ResolveVecExtensionPath();

        if (_vecExtensionPath is null)
        {
            _skipReason = "sqlite-vec extension (vec0.dll) not found. " +
                          "Run 'context-bridge service install' or place vec0.dll in native/win-x64/ to run storage tests.";
            return;
        }

        _factory = new SqliteConnectionFactory(_dbPath, _vecExtensionPath);
        _repository = new MemoryRepository(_factory);

        var initializer = new SchemaInitializer(_factory);
        await initializer.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        if (_dbPath.Length == 0 || !File.Exists(_dbPath))
        {
            return Task.CompletedTask;
        }

        // Release all pooled connections so the temp file can be deleted
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    [SkippableFact]
    public async Task WriteAsync_StoresMemoryAndReturnsId()
    {
        RequireVec();

        long id = await _repository.WriteAsync("Prefer file-scoped namespaces in C#", RandomUnitVector(1));

        Assert.True(id > 0);
    }

    [SkippableFact]
    public async Task WriteAsync_WithTags_TagsAreRetrievableViaList()
    {
        RequireVec();

        await _repository.WriteAsync("Use primary constructors", RandomUnitVector(2), ["type:preference", "project:context-bridge"]);

        var (items, _) = await _repository.ListAsync(1, 10);
        var memory = Assert.Single(items, m => m.Content == "Use primary constructors");
        Assert.Contains("type:preference", memory.Tags);
        Assert.Contains("project:context-bridge", memory.Tags);
    }

    [SkippableFact]
    public async Task SearchAsync_ReturnsSimilarMemories()
    {
        RequireVec();

        // Vectors 1 and 2 are close; vector 3 is orthogonal (irrelevant).
        float[] v1 = RandomUnitVector(10);
        float[] v2 = SlightlyPerturbed(v1, 0.01f);
        float[] vUnrelated = RandomUnitVector(99);

        await _repository.WriteAsync("Similar memory A", v1);
        await _repository.WriteAsync("Similar memory B", v2);
        await _repository.WriteAsync("Unrelated memory", vUnrelated);

        var results = await _repository.SearchAsync(v1, limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Memory.Content == "Similar memory A");
        Assert.Contains(results, r => r.Memory.Content == "Similar memory B");
    }

    [SkippableFact]
    public async Task SearchAsync_ExcludesSoftDeletedMemories()
    {
        RequireVec();

        float[] v = RandomUnitVector(20);
        long id = await _repository.WriteAsync("To be deleted", v);

        bool deleted = await _repository.DeleteAsync(id);
        Assert.True(deleted);

        var results = await _repository.SearchAsync(v, limit: 10);
        Assert.DoesNotContain(results, r => r.Memory.Id == id);
    }

    [SkippableFact]
    public async Task BatchWriteAsync_InsertsAllEntriesAtomically()
    {
        RequireVec();

        var entries = new[]
        {
            new NewMemory("Batch entry 1", RandomUnitVector(30)),
            new NewMemory("Batch entry 2", RandomUnitVector(31)),
            new NewMemory("Batch entry 3", RandomUnitVector(32), ["type:pattern"]),
        };

        var ids = await _repository.BatchWriteAsync(entries);

        Assert.Equal(3, ids.Count);
        Assert.All(ids, id => Assert.True(id > 0));

        var (_, total) = await _repository.ListAsync(1, 10);
        Assert.Equal(3, total);
    }

    [SkippableFact]
    public async Task ListAsync_PaginatesCorrectly()
    {
        RequireVec();

        for (int i = 0; i < 5; i++)
        {
            await _repository.WriteAsync($"Memory {i}", RandomUnitVector(i + 40));
        }

        var (page1, total) = await _repository.ListAsync(1, 3);
        var (page2, _) = await _repository.ListAsync(2, 3);

        Assert.Equal(5, total);
        Assert.Equal(3, page1.Count);
        Assert.Equal(2, page2.Count);
    }

    [SkippableFact]
    public async Task ListAsync_FiltersByTag()
    {
        RequireVec();

        await _repository.WriteAsync("Tagged memory", RandomUnitVector(50), ["project:alpha"]);
        await _repository.WriteAsync("Untagged memory", RandomUnitVector(51));

        var (tagged, count) = await _repository.ListAsync(1, 10, ["project:alpha"]);

        Assert.Equal(1, count);
        var single = Assert.Single(tagged);
        Assert.Equal("Tagged memory", single.Content);
    }

    [SkippableFact]
    public async Task DeleteAsync_SoftDeletesExistingMemory()
    {
        RequireVec();

        long id = await _repository.WriteAsync("To be soft-deleted", RandomUnitVector(60));

        bool first = await _repository.DeleteAsync(id);
        bool second = await _repository.DeleteAsync(id);

        Assert.True(first);
        Assert.False(second);
    }

    [SkippableFact]
    public async Task UpdateAsync_ReplacesContentAndEmbedding()
    {
        RequireVec();

        long id = await _repository.WriteAsync("Original content", RandomUnitVector(70));
        float[] newEmbedding = RandomUnitVector(71);

        bool updated = await _repository.UpdateAsync(id, "Updated content", newEmbedding);

        Assert.True(updated);

        var (items, _) = await _repository.ListAsync(1, 10);
        var memory = Assert.Single(items);
        Assert.Equal("Updated content", memory.Content);
        Assert.True(memory.UpdatedAt > memory.CreatedAt);
    }

    [SkippableFact]
    public async Task UpdateAsync_ReturnsFalse_WhenMemoryDoesNotExist()
    {
        RequireVec();

        bool updated = await _repository.UpdateAsync(999_999, "ghost", RandomUnitVector(80));

        Assert.False(updated);
    }

    [SkippableFact]
    public async Task GetStatusAsync_ReflectsInsertAndDeleteCounts()
    {
        RequireVec();

        long id1 = await _repository.WriteAsync("Memory 1", RandomUnitVector(90));
        await _repository.WriteAsync("Memory 2", RandomUnitVector(91));
        await _repository.DeleteAsync(id1);

        var status = await _repository.GetStatusAsync();

        Assert.Equal(2, status.TotalCount);
        Assert.Equal(1, status.ActiveCount);
        Assert.Equal(1, status.DeletedCount);
    }

    // --- Helpers ---

    private void RequireVec() => Skip.If(_skipReason is not null, _skipReason ?? string.Empty);

    /// <summary>Creates a deterministic random unit vector of 384 dimensions.</summary>
    private static float[] RandomUnitVector(int seed)
    {
        var rng = new Random(seed);
        var v = new float[384];
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = (float)(rng.NextDouble() * 2 - 1);
        }

        float norm = 0f;
        for (int i = 0; i < v.Length; i++)
        {
            norm += v[i] * v[i];
        }
        norm = MathF.Sqrt(norm);
        for (int i = 0; i < v.Length; i++)
        {
            v[i] /= norm;
        }

        return v;
    }

    /// <summary>
    /// Returns a unit vector close to <paramref name="v"/> by adding small noise and re-normalizing.
    /// Use to produce a "similar" vector for testing nearest-neighbour recall.
    /// </summary>
    private static float[] SlightlyPerturbed(float[] v, float noise)
    {
        var rng = new Random(42);
        var result = new float[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            result[i] = v[i] + (float)(rng.NextDouble() * 2 - 1) * noise;
        }

        float norm = 0f;
        for (int i = 0; i < result.Length; i++)
        {
            norm += result[i] * result[i];
        }
        norm = MathF.Sqrt(norm);
        for (int i = 0; i < result.Length; i++)
        {
            result[i] /= norm;
        }

        return result;
    }
}
