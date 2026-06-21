using System.ComponentModel;

namespace ContextBridge.Infrastructure.Mcp;

public sealed record MemoryBatchEntry(
    [property: Description("The memory content to store.")] string Content,
    [property: Description("Optional classification tags (e.g. project:my-repo, type:decision).")] string[]? Tags = null);
