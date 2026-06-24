using System.ComponentModel;
using System.Text.Json;
using ContextBridge.Core.Repositories;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ContextBridge.Infrastructure.Mcp;

[McpServerToolType]
public sealed class HandoffMcpTools(IHandoffRepository repository, ILogger<HandoffMcpTools> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Store ephemeral session state to resume in a future session.</summary>
    [McpServerTool(Name = "handoff_write")]
    public async Task<string> HandoffWriteAsync(
        [Description("Summary of current session state: what you were working on, decisions made, next steps. Ephemeral session context — not a permanent memory.")] string content,
        [Description("Optional project identifier to scope this handoff (e.g. 'context-bridge'). Used to retrieve it in the next session.")] string? project = null,
        [Description("Time-to-live in days before the handoff expires. Defaults to 7.")] int ttlDays = 7,
        CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("handoff_write project={Project} content_length={ContentLength} ttl_days={TtlDays}", project, content.Length, ttlDays);
        }

        var expiresAt = DateTimeOffset.UtcNow.AddDays(ttlDays);
        var id = await repository.WriteAsync(content, project, expiresAt, cancellationToken);
        return JsonSerializer.Serialize(new { id, expires_at = expiresAt }, JsonOptions);
    }

    /// <summary>List active handoffs from previous sessions, optionally filtered by project.</summary>
    [McpServerTool(Name = "handoff_list")]
    public async Task<string> HandoffListAsync(
        [Description("Optional project identifier to filter handoffs. If omitted, all active handoffs are returned.")] string? project = null,
        CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("handoff_list project={Project}", project);
        }

        var handoffs = await repository.ListAsync(project, cancellationToken);
        return JsonSerializer.Serialize(handoffs.Select(h => new
        {
            id = h.Id,
            content = h.Content,
            project = h.Project,
            created_at = h.CreatedAt,
            expires_at = h.ExpiresAt
        }), JsonOptions);
    }

    /// <summary>Acknowledge a handoff after processing it. Removes the record permanently.</summary>
    [McpServerTool(Name = "handoff_acknowledge")]
    public async Task<string> HandoffAcknowledgeAsync(
        [Description("The ID of the handoff to acknowledge and remove.")] long id,
        CancellationToken cancellationToken = default)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("handoff_acknowledge id={Id}", id);
        }

        var acknowledged = await repository.AcknowledgeAsync(id, cancellationToken);
        return JsonSerializer.Serialize(new { acknowledged }, JsonOptions);
    }
}
