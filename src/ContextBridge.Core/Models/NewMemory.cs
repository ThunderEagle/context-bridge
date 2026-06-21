namespace ContextBridge.Core.Models;

public sealed record NewMemory(string Content, float[] Embedding, IEnumerable<string>? Tags = null);
