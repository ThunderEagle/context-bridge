namespace ContextBridge.Core.Models;

public sealed record StorageStatus(long TotalCount, long ActiveCount, long DeletedCount);
