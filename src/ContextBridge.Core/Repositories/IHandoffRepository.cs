using ContextBridge.Core.Models;

namespace ContextBridge.Core.Repositories;

public interface IHandoffRepository
{
    Task<long> WriteAsync(string content, string? project, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task<IReadOnlyList<HandoffRecord>> ListAsync(string? project = null, CancellationToken ct = default);
    Task<bool> AcknowledgeAsync(long id, CancellationToken ct = default);
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}
