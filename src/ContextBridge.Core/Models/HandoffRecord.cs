namespace ContextBridge.Core.Models;

public sealed record HandoffRecord(
    long Id,
    string Content,
    string? Project,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
