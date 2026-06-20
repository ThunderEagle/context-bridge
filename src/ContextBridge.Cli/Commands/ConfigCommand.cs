using System.CommandLine;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextBridge.Cli.WindowsService;
using Microsoft.Extensions.Configuration;

namespace ContextBridge.Cli.Commands;

internal static class ConfigCommand
{
    private static readonly string OverrideConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ContextBridge",
        "appsettings.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static Command Build(IConfiguration configuration)
    {
        var cmd = new Command("config", "Read or write ContextBridge configuration");
        cmd.Add(BuildSet());
        cmd.Add(BuildGet(configuration));
        return cmd;
    }

    private static Command BuildSet()
    {
        var setCmd = new Command("set", "Set a configuration value (requires admin)");
        var keyArg = new Argument<string>("key") { Description = "Configuration key (e.g. port)" };
        var valueArg = new Argument<string>("value") { Description = "Value to set" };
        setCmd.Add(keyArg);
        setCmd.Add(valueArg);
        setCmd.SetAction(parseResult =>
        {
            var key = parseResult.GetValue(keyArg)!;
            var value = parseResult.GetValue(valueArg)!;

            if (!string.Equals(key, "port", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Unknown configuration key '{key}'. Supported keys: port");
                return;
            }

            if (!int.TryParse(value, out var port) || port is < 1 or > 65535)
            {
                Console.Error.WriteLine("Port must be an integer between 1 and 65535.");
                return;
            }

            if (!CliHelpers.RequireAdmin()) { return; }

            SetPort(port);
            PromptRestartIfRunning();
        });
        return setCmd;
    }

    private static Command BuildGet(IConfiguration configuration)
    {
        var getCmd = new Command("get", "Get a configuration value");
        var keyArg = new Argument<string>("key") { Description = "Configuration key (e.g. port)" };
        getCmd.Add(keyArg);
        getCmd.SetAction(parseResult =>
        {
            var key = parseResult.GetValue(keyArg)!;

            if (!string.Equals(key, "port", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Unknown configuration key '{key}'. Supported keys: port");
                return;
            }

            var port = configuration.GetValue<int>("ServiceConfig:Port", 5290);
            var source = File.Exists(OverrideConfigPath) ? OverrideConfigPath : "default";
            Console.WriteLine($"port = {port} ({source})");
        });
        return getCmd;
    }

    private static void SetPort(int port)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OverrideConfigPath)!);

        JsonObject existing = new();
        if (File.Exists(OverrideConfigPath))
        {
            try
            {
                existing = JsonNode.Parse(File.ReadAllText(OverrideConfigPath))?.AsObject() ?? new JsonObject();
            }
            catch (JsonException) { /* Overwrite malformed file */ }
        }

        var serviceConfig = existing["ServiceConfig"]?.AsObject()?.DeepClone() as JsonObject ?? new JsonObject();
        serviceConfig["Port"] = port;
        existing["ServiceConfig"] = serviceConfig;

        File.WriteAllText(OverrideConfigPath, existing.ToJsonString(WriteOptions));
        Console.WriteLine($"Port set to {port}.");
    }

    private static void PromptRestartIfRunning()
    {
        try
        {
            using var sc = new ServiceController(NativeServiceManager.ServiceName);
            if (sc.Status != ServiceControllerStatus.Running) { return; }

            Console.Write("The service is running. Restart now to apply the change? [Y/n] ");
            var response = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(response) || response.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                Console.WriteLine("Service restarted.");
            }
            else
            {
                Console.WriteLine("Service not restarted. Change takes effect on the next manual restart.");
            }
        }
        catch (InvalidOperationException)
        {
            // Service not installed — no restart needed
        }
    }
}
