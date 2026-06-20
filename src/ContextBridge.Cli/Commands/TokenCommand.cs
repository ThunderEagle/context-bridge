using System.CommandLine;
using ContextBridge.Infrastructure.Security;

namespace ContextBridge.Cli.Commands;

internal static class TokenCommand
{
    public static Command Build(TokenStore tokenStore)
    {
        var cmd = new Command("token", "Print the bearer token for manual MCP client configuration");
        cmd.SetAction(async (_, ct) =>
        {
            var token = await tokenStore.GetOrCreateTokenAsync(ct);
            Console.WriteLine(token);
        });
        return cmd;
    }
}
