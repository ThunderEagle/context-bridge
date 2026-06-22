using System.ComponentModel;
using System.Text.Json;
using ContextBridge.Core.Models;
using ContextBridge.Core.Repositories;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace ContextBridge.Infrastructure.Mcp;

[McpServerToolType]
public sealed class MemoryMcpTools(
    IMemoryRepository repository,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Store a memory with automatic embedding. Returns the assigned memory ID.</summary>
    [McpServerTool(Name = "memory_write")]
    public async Task<string> MemoryWriteAsync(
        [Description("The content to remember. Keep it to a single coherent fact, decision, or preference.")] string content,
        [Description("Optional classification tags. Use project:<repo-name> for scope and type:decision|preference|pattern|reference for classification.")] string[]? tags = null,
        CancellationToken cancellationToken = default)
    {
        var embedding = await EmbedAsync(content, cancellationToken);
        var id = await repository.WriteAsync(content, embedding, tags, cancellationToken);
        return JsonSerializer.Serialize(new { id }, JsonOptions);
    }

    /// <summary>Store multiple memories atomically. Preferred over sequential memory_write calls to avoid partial state.</summary>
    [McpServerTool(Name = "memory_batch_write")]
    public async Task<string> MemoryBatchWriteAsync(
        [Description("The memories to store.")] MemoryBatchEntry[] memories,
        CancellationToken cancellationToken = default)
    {
        var contents = memories.Select(m => m.Content).ToList();
        var embeddings = await embedder.GenerateAsync(contents, cancellationToken: cancellationToken);

        var entries = memories.Zip(embeddings)
            .Select(pair => new NewMemory(pair.First.Content, pair.Second.Vector.ToArray(), pair.First.Tags))
            .ToList();

        var ids = await repository.BatchWriteAsync(entries, cancellationToken);
        return JsonSerializer.Serialize(new { ids }, JsonOptions);
    }

    /// <summary>Semantic similarity search across all memories. Returns the most relevant results for the query.</summary>
    [McpServerTool(Name = "memory_search")]
    public async Task<string> MemorySearchAsync(
        [Description("Natural language description of what you are looking for.")] string query,
        [Description("Maximum number of results to return. Defaults to 10.")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var embedding = await EmbedAsync(query, cancellationToken);
        var results = await repository.SearchAsync(embedding, limit, cancellationToken);
        return JsonSerializer.Serialize(results.Select(r => new
        {
            id = r.Memory.Id,
            content = r.Memory.Content,
            tags = r.Memory.Tags,
            distance = r.Distance,
            created_at = r.Memory.CreatedAt,
            updated_at = r.Memory.UpdatedAt
        }), JsonOptions);
    }

    /// <summary>List memories with pagination. Optionally filter by tags.</summary>
    [McpServerTool(Name = "memory_list")]
    public async Task<string> MemoryListAsync(
        [Description("1-based page number.")] int page = 1,
        [Description("Number of results per page.")] int pageSize = 20,
        [Description("Optional tag filter. Only memories matching ALL supplied tags are returned.")] string[]? tags = null,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await repository.ListAsync(page, pageSize, tags, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            page,
            pageSize,
            totalCount,
            items = items.Select(ToDto)
        }, JsonOptions);
    }

    /// <summary>Soft-delete a memory by ID. The record is marked deleted and excluded from search and list results.</summary>
    [McpServerTool(Name = "memory_delete")]
    public async Task<string> MemoryDeleteAsync(
        [Description("The ID of the memory to delete.")] long id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await repository.DeleteAsync(id, cancellationToken);
        return JsonSerializer.Serialize(new { deleted }, JsonOptions);
    }

    /// <summary>Update the content of an existing memory. The embedding is recomputed automatically.</summary>
    [McpServerTool(Name = "memory_update")]
    public async Task<string> MemoryUpdateAsync(
        [Description("The ID of the memory to update.")] long id,
        [Description("The new content to store.")] string content,
        CancellationToken cancellationToken = default)
    {
        var embedding = await EmbedAsync(content, cancellationToken);
        var updated = await repository.UpdateAsync(id, content, embedding, cancellationToken);
        return JsonSerializer.Serialize(new { updated }, JsonOptions);
    }

    /// <summary>Return service health, memory record counts, and embedding model information.</summary>
    [McpServerTool(Name = "memory_status")]
    public async Task<string> MemoryStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await repository.GetStatusAsync(cancellationToken);
        return JsonSerializer.Serialize(new
        {
            status = "ok",
            model = "all-MiniLM-L6-v2-INT8",
            embedding_dims = 384,
            total_count = status.TotalCount,
            active_count = status.ActiveCount,
            deleted_count = status.DeletedCount
        }, JsonOptions);
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var result = await embedder.GenerateAsync([text], cancellationToken: ct);
        return result[0].Vector.ToArray();
    }

    private static object ToDto(MemoryRecord r) => new
    {
        id = r.Id,
        content = r.Content,
        tags = r.Tags,
        created_at = r.CreatedAt,
        updated_at = r.UpdatedAt
    };
}
