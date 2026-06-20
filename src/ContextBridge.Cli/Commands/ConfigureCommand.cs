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
        var mcpServers = settings["mcpServers"]?.AsObject()?.DeepClone() as JsonObject ?? new JsonObject();
        mcpServers["context-bridge"] = new JsonObject
        {
            ["type"] = "http",
            ["url"] = $"http://127.0.0.1:{port}/",
            ["headers"] = new JsonObject { ["Authorization"] = $"Bearer {token}" }
        };
        settings["mcpServers"] = mcpServers;

        // Stop hook — idempotent: remove any existing context-bridge entry, then append fresh
        var hooks = settings["hooks"]?.AsObject()?.DeepClone() as JsonObject ?? new JsonObject();
        var existingStop = hooks["Stop"]?.AsArray();
        var newStop = new JsonArray();

        if (existingStop != null)
        {
            foreach (var item in existingStop)
            {
                if (item == null) { continue; }
                var hooksArr = item["hooks"]?.AsArray();
                var hasOurCommand = hooksArr?.Any(h =>
                    h?["command"]?.GetValue<string>() == "context-bridge extract") ?? false;
                if (!hasOurCommand)
                {
                    newStop.Add(item.DeepClone());
                }
            }
        }

        newStop.Add(new JsonObject
        {
            ["hooks"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = "context-bridge extract"
                }
            }
        });

        hooks["Stop"] = newStop;
        settings["hooks"] = hooks;

        WriteJsonObject(settingsPath, settings);
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

        var mcpServers = config["mcpServers"]?.AsObject()?.DeepClone() as JsonObject ?? new JsonObject();
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
}
