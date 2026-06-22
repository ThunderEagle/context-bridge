using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextBridge.Infrastructure.Security;

namespace ContextBridge.Cli.Commands;

internal static class ConfigureCommand
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static Command Build(TokenStore tokenStore, int port)
    {
        var cmd = new Command("configure", "Configure MCP clients to connect to ContextBridge");
        cmd.SetAction(async (_, ct) =>
        {
            var token = await tokenStore.GetOrCreateTokenAsync(ct);
            var configured = 0;

            if (ConfigureClaudeCode(token, port)) { configured++; }
            if (ConfigureClaudeDesktop(token, port)) { configured++; }

            Console.WriteLine(configured == 0
                ? "No supported MCP clients detected. Run 'context-bridge token' for manual configuration."
                : $"\nConfigured {configured} client(s). Restart them to pick up the changes.");
        });
        return cmd;
    }

    private static bool ConfigureClaudeCode(string token, int port)
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "settings.json");

        if (!Directory.Exists(Path.GetDirectoryName(settingsPath)))
        {
            return false;
        }

        var settings = ReadJsonObject(settingsPath);

        // MCP server entry
        var mcpServers = settings["mcpServers"]?.AsObject().DeepClone() as JsonObject ?? new JsonObject();
        mcpServers["context-bridge"] = new JsonObject
        {
            ["type"] = "http",
            ["url"] = $"http://127.0.0.1:{port}/",
            ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {token}" }
        };
        settings["mcpServers"] = mcpServers;

        WriteJsonObject(settingsPath, settings);
        InjectClaudeMd();
        Console.WriteLine($"  Claude Code configured ({settingsPath})");
        return true;
    }

    private static bool ConfigureClaudeDesktop(string token, int port)
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude", "claude_desktop_config.json");

        if (!Directory.Exists(Path.GetDirectoryName(configPath)))
        {
            return false;
        }

        var config = ReadJsonObject(configPath);

        var mcpServers = config["mcpServers"]?.AsObject().DeepClone() as JsonObject ?? new JsonObject();
        mcpServers["context-bridge"] = new JsonObject
        {
            ["url"] = $"http://127.0.0.1:{port}/",
            ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {token}" }
        };
        config["mcpServers"] = mcpServers;

        WriteJsonObject(configPath, config);
        Console.WriteLine($"  Claude Desktop configured ({configPath})");
        return true;
    }

    private static JsonObject ReadJsonObject(string path)
    {
        if (!File.Exists(path)) { return new JsonObject(); }

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) { return new JsonObject(); }

        try
        {
            return JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static void WriteJsonObject(string path, JsonObject obj)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, obj.ToJsonString(WriteOptions));
    }

    private static void InjectClaudeMd()
    {
        var claudeMdPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "CLAUDE.md");

        const string sectionMarker = "## Context Bridge Memory";
        const string injection = """


## Context Bridge Memory

You have access to a persistent memory service via the context-bridge MCP server.

- Call `memory_search` at the start of each session to surface relevant prior context for the current project.
- Call `memory_write` immediately after significant decisions, preferences, or architectural choices — do not defer to session end.
- For multiple related memories, use `memory_batch_write` instead of sequential calls.
- Tag every write: `project:<repo-name>` for scope; `type:decision|preference|pattern|reference` for classification.
""";

        var existing = File.Exists(claudeMdPath) ? File.ReadAllText(claudeMdPath) : string.Empty;
        if (existing.Contains(sectionMarker, StringComparison.Ordinal)) { return; }

        File.AppendAllText(claudeMdPath, injection);
    }
}
