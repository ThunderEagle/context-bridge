using ContextBridge.Core.Models;

namespace ContextBridge.Core.Repositories;

public interface IMemoryRepository
{
    Task<long> WriteAsync(string content, float[] embedding, IEnumerable<string>? tags = null, CancellationToken ct = default);
    Task<IReadOnlyList<long>> BatchWriteAsync(IEnumerable<NewMemory> entries, CancellationToken ct = default);
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(float[] queryEmbedding, int limit = 10, CancellationToken ct = default);
    Task<(IReadOnlyList<MemoryRecord> Items, int TotalCount)> ListAsync(int page, int pageSize, IEnumerable<string>? tags = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
    Task<bool> UpdateAsync(long id, string content, float[] embedding, CancellationToken ct = default);
    Task<StorageStatus> GetStatusAsync(CancellationToken ct = default);
}
