using System.CommandLine;
using ContextBridge.Cli.Commands;
using ContextBridge.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace ContextBridge.Cli;

public static class CliCommandBuilder
{
    public static RootCommand Build(TokenStore tokenStore, IConfiguration configuration)
    {
        var port = configuration.GetValue<int>("ServiceConfig:Port", 5290);

        var root = new RootCommand("ContextBridge MCP memory server — management CLI");
        root.Add(ServiceCommand.Build());
        root.Add(ConfigCommand.Build(configuration));
        root.Add(ConfigureCommand.Build(tokenStore, port));
        root.Add(TokenCommand.Build(tokenStore));
        return root;
    }
}
