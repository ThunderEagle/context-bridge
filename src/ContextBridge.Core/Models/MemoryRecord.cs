namespace ContextBridge.Core.Models;

public sealed record MemoryRecord(
    long Id,
    string Content,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
