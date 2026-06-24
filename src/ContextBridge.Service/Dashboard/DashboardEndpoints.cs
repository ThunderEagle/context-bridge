using ContextBridge.Core.Models;
using ContextBridge.Core.Repositories;
using Microsoft.Extensions.AI;

namespace ContextBridge.Service.Dashboard;

internal static class DashboardEndpoints
{
    internal static void MapDashboard(this WebApplication app)
    {
        app.MapGet("/dashboard", () =>
            Results.Content(DashboardHtml.Page, "text/html; charset=utf-8"));

        app.MapGet("/api/dashboard/handoffs", async (
            IHandoffRepository repository,
            CancellationToken ct) =>
        {
            var handoffs = await repository.ListAsync(null, ct);
            return Results.Ok(handoffs.Select(h => new
            {
                id = h.Id,
                content = h.Content,
                project = h.Project,
                createdAt = h.CreatedAt,
                expiresAt = h.ExpiresAt
            }));
        });

        app.MapGet("/api/dashboard/stats", async (
            IMemoryRepository repository,
            CancellationToken ct) =>
        {
            var status = await repository.GetStatusAsync(ct);
            return Results.Ok(new
            {
                model = "all-MiniLM-L6-v2-INT8",
                totalCount = status.TotalCount,
                activeCount = status.ActiveCount,
                deletedCount = status.DeletedCount
            });
        });

        app.MapGet("/api/dashboard/memories", async (
            IMemoryRepository repository,
            IEmbeddingGenerator<string, Embedding<float>> embedder,
            string? q,
            string? tag,
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            page = Math.Max(1, page);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var embedding = await EmbedAsync(embedder, q, ct);
                var results = await repository.SearchAsync(embedding, pageSize, ct);
                var items = results.Select(r => new
                {
                    id = r.Memory.Id,
                    content = r.Memory.Content,
                    tags = r.Memory.Tags,
                    createdAt = r.Memory.CreatedAt,
                    updatedAt = r.Memory.UpdatedAt,
                    distance = (float?)r.Distance
                }).ToList();

                return Results.Ok(new { totalCount = items.Count, page = 1, pageSize, items });
            }

            string[]? tagFilter = !string.IsNullOrWhiteSpace(tag) ? [tag] : null;
            var (memoryItems, totalCount) = await repository.ListAsync(page, pageSize, tagFilter, ct);
            return Results.Ok(new
            {
                totalCount,
                page,
                pageSize,
                items = memoryItems.Select(m => new
                {
                    id = m.Id,
                    content = m.Content,
                    tags = m.Tags,
                    createdAt = m.CreatedAt,
                    updatedAt = m.UpdatedAt,
                    distance = (float?)null
                })
            });
        });
    }

    private static async Task<float[]> EmbedAsync(
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        string text,
        CancellationToken ct)
    {
        var result = await embedder.GenerateAsync([text], cancellationToken: ct);
        return result[0].Vector.ToArray();
    }
}
