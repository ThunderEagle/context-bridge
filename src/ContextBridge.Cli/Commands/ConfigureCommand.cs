using System.CommandLine;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace ContextBridge.Cli.Commands;

internal static class ConfigureCommand
{
    public static Command Build(IConfiguration configuration)
    {
        var port = configuration.GetValue<int>("ServiceConfig:Port", 5290);

        var cmd = new Command("configure", "Configure MCP clients to connect to ContextBridge");
        cmd.SetAction((_, _) =>
        {
            var configured = 0;

            if (ConfigureClaudeCode(port)) { configured++; }
            if (ConfigureClaudeDesktop()) { configured++; }
            if (ConfigureCline(port)) { configured++; }

            Console.WriteLine(configured == 0
                ? $"No supported MCP clients detected. Manually add context-bridge to your MCP client config."
                : $"\nConfigured {configured} client(s). Restart them to pick up the changes.");

            return Task.FromResult(0);
        });
        return cmd;
    }

    private static bool ConfigureClaudeDesktop()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Claude", "claude_desktop_config.json");

        if (!File.Exists(configPath)) { return false; }

        try
        {
            var json = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ?? new JsonObject();
            var mcpServers = json["mcpServers"]?.AsObject() ?? new JsonObject();

            var exePath = Environment.ProcessPath ?? "context-bridge";
            mcpServers["context-bridge"] = new JsonObject
            {
                ["command"] = exePath,
                ["args"] = new JsonArray("stdio")
            };
            json["mcpServers"] = mcpServers;

            File.WriteAllText(configPath, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("  Claude Desktop configured (stdio transport).");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Failed to configure Claude Desktop: {ex.Message}");
            return false;
        }
    }

    private static bool ConfigureCline(int port)
    {
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Code", "User", "globalStorage", "saoudrizwan.claude-dev", "settings");

        if (!Directory.Exists(settingsDir)) { return false; }

        var configPath = Path.Combine(settingsDir, "cline_mcp_settings.json");

        try
        {
            var json = File.Exists(configPath)
                ? JsonNode.Parse(File.ReadAllText(configPath))?.AsObject() ?? new JsonObject()
                : new JsonObject();
            var mcpServers = json["mcpServers"]?.AsObject() ?? new JsonObject();

            mcpServers["context-bridge"] = new JsonObject
            {
                ["url"] = $"http://127.0.0.1:{port}/mcp",
                ["disabled"] = false,
                ["autoApprove"] = new JsonArray()
            };
            json["mcpServers"] = mcpServers;

            File.WriteAllText(configPath, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"  Cline configured (HTTP transport, port {port}).");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Failed to configure Cline: {ex.Message}");
            return false;
        }
    }

    private static bool ConfigureClaudeCode(int port)
    {
        // Claude Code reads MCP servers from ~/.claude.json, not settings.json.
        // The supported path is 'claude mcp add', which writes the correct format.
        var claudeBinary = FindClaudeBinary();
        if (claudeBinary is null) { return false; }

        try
        {
            var result = Process.Start(new ProcessStartInfo
            {
                FileName = claudeBinary,
                Arguments = $"mcp add --transport http context-bridge http://127.0.0.1:{port}/mcp --scope user",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            result!.WaitForExit(10_000);
            var output = result.StandardOutput.ReadToEnd().Trim();
            var error = result.StandardError.ReadToEnd().Trim();

            if (result.ExitCode != 0)
            {
                Console.Error.WriteLine($"  claude mcp add failed: {error}");
                return false;
            }

            Console.WriteLine($"  Claude Code configured ({output})");
            InjectClaudeMd();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Failed to run claude mcp add: {ex.Message}");
            return false;
        }
    }

    private static string? FindClaudeBinary()
    {
        // Check PATH first
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, "claude.exe");
            if (File.Exists(candidate)) { return candidate; }
        }

        // Fall back to known VSCode extension install locations
        var extensionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".vscode", "extensions");

        if (!Directory.Exists(extensionsDir)) { return null; }

        var extension = Directory.GetDirectories(extensionsDir, "anthropic.claude-code-*")
            .OrderByDescending(d => d)
            .FirstOrDefault();

        if (extension is null) { return null; }

        var binary = Path.Combine(extension, "resources", "native-binary", "claude.exe");
        return File.Exists(binary) ? binary : null;
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
