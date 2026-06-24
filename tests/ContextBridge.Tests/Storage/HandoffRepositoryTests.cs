using ContextBridge.Core.Repositories;
using ContextBridge.Infrastructure.Storage;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextBridge.Tests.Storage;

public sealed class HandoffRepositoryTests : IAsyncLifetime
{
    private string _dbPath = string.Empty;
    private SqliteConnectionFactory _factory = null!;
    private IHandoffRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"context-bridge-handoff-test-{Guid.NewGuid():N}.db");

        // Pass null vec extension — handoffs have no vec dependency.
        _factory = new SqliteConnectionFactory(_dbPath, vecExtensionPath: null);
        _repository = new HandoffRepository(_factory, NullLogger<HandoffRepository>.Instance);

        await using var connection = _factory.Create();
        await connection.ExecuteAsync(
            """
            CREATE TABLE handoffs (
                id         INTEGER PRIMARY KEY,
                content    TEXT    NOT NULL,
                project    TEXT    NULL,
                created_at TEXT    NOT NULL,
                expires_at TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_handoffs_project
                ON handoffs(project) WHERE project IS NOT NULL;
            """);
    }

    public Task DisposeAsync()
    {
        if (_dbPath.Length == 0 || !File.Exists(_dbPath))
        {
            return Task.CompletedTask;
        }

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

    [Fact]
    public async Task WriteAsync_StoresHandoffAndReturnsId()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        long id = await _repository.WriteAsync("Working on the handoff feature", "context-bridge", expiresAt);

        Assert.True(id > 0);
    }

    [Fact]
    public async Task ListAsync_ReturnsActiveHandoffs()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        await _repository.WriteAsync("Session state content", "context-bridge", expiresAt);

        var results = await _repository.ListAsync();

        var handoff = Assert.Single(results);
        Assert.Equal("Session state content", handoff.Content);
        Assert.Equal("context-bridge", handoff.Project);
        Assert.True(handoff.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ListAsync_FiltersByProject()
    {
        var expires = DateTimeOffset.UtcNow.AddDays(7);
        await _repository.WriteAsync("Project A handoff", "project-a", expires);
        await _repository.WriteAsync("Project B handoff", "project-b", expires);

        var resultsA = await _repository.ListAsync("project-a");
        var resultsB = await _repository.ListAsync("project-b");
        var resultsOther = await _repository.ListAsync("project-c");

        Assert.Single(resultsA);
        Assert.Equal("Project A handoff", resultsA[0].Content);
        Assert.Single(resultsB);
        Assert.Equal("Project B handoff", resultsB[0].Content);
        Assert.Empty(resultsOther);
    }

    [Fact]
    public async Task ListAsync_ExcludesExpiredHandoffs()
    {
        var pastExpiry = DateTimeOffset.UtcNow.AddDays(-1);
        await _repository.WriteAsync("Expired handoff", null, pastExpiry);

        var results = await _repository.ListAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ListAsync_ExcludesUnprojectHandoffWhenProjectFilterApplied()
    {
        var expires = DateTimeOffset.UtcNow.AddDays(7);
        await _repository.WriteAsync("Unscoped handoff", null, expires);

        var results = await _repository.ListAsync("any-project");

        Assert.Empty(results);
    }

    [Fact]
    public async Task AcknowledgeAsync_RemovesHandoff()
    {
        var expires = DateTimeOffset.UtcNow.AddDays(7);
        long id = await _repository.WriteAsync("To be acknowledged", null, expires);

        bool first = await _repository.AcknowledgeAsync(id);
        bool second = await _repository.AcknowledgeAsync(id);

        Assert.True(first);
        Assert.False(second);

        var results = await _repository.ListAsync();
        Assert.Empty(results);
    }

    [Fact]
    public async Task AcknowledgeAsync_ReturnsFalse_WhenIdNotFound()
    {
        bool acknowledged = await _repository.AcknowledgeAsync(999_999);

        Assert.False(acknowledged);
    }

    [Fact]
    public async Task PurgeExpiredAsync_DeletesOnlyExpiredHandoffs()
    {
        var future = DateTimeOffset.UtcNow.AddDays(7);
        var past = DateTimeOffset.UtcNow.AddDays(-1);

        long activeId = await _repository.WriteAsync("Active handoff", null, future);
        await _repository.WriteAsync("Expired handoff", null, past);

        int purged = await _repository.PurgeExpiredAsync();

        Assert.Equal(1, purged);

        var remaining = await _repository.ListAsync();
        Assert.Single(remaining);
        Assert.Equal(activeId, remaining[0].Id);
    }
}
