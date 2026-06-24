using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ContextBridge.Infrastructure.Mcp;

[McpServerPromptType]
public sealed class HandoffMcpPrompts
{
    [McpServerPrompt(Name = "resume-session")]
    [Description("Retrieve any handoff from the previous session for this project.")]
    public static GetPromptResult ResumeSession(
        [Description("The project identifier to look up (e.g. 'context-bridge').")] string? project = null) =>
        new()
        {
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = project is not null
                            ? $"Call handoff_list with project \"{project}\". If any handoffs are returned, incorporate the most recent one as your session-opening context, then call handoff_acknowledge with its ID."
                            : "Call handoff_list. If any handoffs are returned, incorporate the most recent one as your session-opening context, then call handoff_acknowledge with its ID."
                    }
                }
            ]
        };
}
