using System.CommandLine;
using ContextBridge.Cli.Commands;
using Microsoft.Extensions.Configuration;

namespace ContextBridge.Cli;

public static class CliCommandBuilder
{
    public static RootCommand Build(IConfiguration configuration)
    {
        var root = new RootCommand("ContextBridge MCP memory server — management CLI");
        root.Add(ServiceCommand.Build());
        root.Add(ModelCommand.Build());
        root.Add(ConfigCommand.Build(configuration));
        root.Add(ConfigureCommand.Build(configuration));
        return root;
    }
}
