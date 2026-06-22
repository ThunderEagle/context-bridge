using System.ComponentModel;
using System.CommandLine;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextBridge.Cli.WindowsService;
using ContextBridge.Infrastructure.Embedding;
using ContextBridge.Infrastructure.Security;

namespace ContextBridge.Cli.Commands;

internal static class ServiceCommand
{
    private static readonly string ModelDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ContextBridge", "models", "all-MiniLM-L6-v2");

    private static readonly string ManifestSourceDir = Path.Combine(
        AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2");

    private static readonly string OverrideConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ContextBridge", "appsettings.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static Command Build()
    {
        var cmd = new Command("service", "Manage the ContextBridge Windows Service");
        cmd.Add(BuildInstall());
        cmd.Add(BuildStart());
        cmd.Add(BuildStop());
        cmd.Add(BuildUninstall());
        cmd.Add(BuildStatus());
        return cmd;
    }

    private static Command BuildInstall()
    {
        var yes = new Option<bool>("--yes") { Description = "Skip confirmation prompts and accept all defaults" };
        var cmd = new Command("install", "Register and start the ContextBridge Windows Service (requires admin)");
        cmd.Add(yes);
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!CliHelpers.RequireAdmin())
            {
                return;
            }

            if (!await EnsureModelAsync(parseResult.GetValue(yes), cancellationToken))
            {
                return;
            }

            EnsureCertificate();

            try
            {
                NativeServiceManager.Install();
                Console.WriteLine($"Service '{NativeServiceManager.ServiceName}' installed successfully.");
                StartServiceCore();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                Console.Error.WriteLine($"Install failed: {ex.Message}");
            }
        });
        return cmd;
    }

    private static Command BuildStart()
    {
        var cmd = new Command("start", "Start the ContextBridge Windows Service");
        cmd.SetAction(_ => StartServiceCore());
        return cmd;
    }

    private static Command BuildStop()
    {
        var cmd = new Command("stop", "Stop the ContextBridge Windows Service");
        cmd.SetAction(_ =>
        {
            try
            {
                using var sc = new ServiceController(NativeServiceManager.ServiceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    Console.WriteLine("Service is already stopped.");
                    return;
                }
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                Console.WriteLine("Service stopped.");
            }
            catch (InvalidOperationException)
            {
                Console.Error.WriteLine($"Service '{NativeServiceManager.ServiceName}' is not installed.");
            }
        });
        return cmd;
    }

    private static Command BuildUninstall()
    {
        var cmd = new Command("uninstall", "Stop and remove the ContextBridge Windows Service (requires admin)");
        cmd.SetAction(_ =>
        {
            if (!CliHelpers.RequireAdmin()) { return; }
            try
            {
                var thumbprint = ReadThumbprintFromConfig();
                StopServiceIfRunning();
                NativeServiceManager.Uninstall();
                Console.WriteLine($"Service '{NativeServiceManager.ServiceName}' uninstalled.");

                if (!string.IsNullOrWhiteSpace(thumbprint))
                {
                    CertificateManager.RemoveByThumbprint(thumbprint);
                    Console.WriteLine("HTTPS certificate removed from LocalMachine stores.");
                }
            }
            catch (Win32Exception ex)
            {
                Console.Error.WriteLine($"Uninstall failed: {ex.Message}");
            }
        });
        return cmd;
    }

    private static Command BuildStatus()
    {
        var cmd = new Command("status", "Show the current state of the ContextBridge Windows Service");
        cmd.SetAction(_ =>
        {
            try
            {
                using var sc = new ServiceController(NativeServiceManager.ServiceName);
                Console.WriteLine($"Service: {sc.DisplayName}");
                Console.WriteLine($"Status:  {sc.Status}");
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine($"Service '{NativeServiceManager.ServiceName}' is not installed.");
            }
        });
        return cmd;
    }

    private static void EnsureCertificate()
    {
        var thumbprint = ReadThumbprintFromConfig();
        if (CertificateManager.IsValid(thumbprint))
        {
            Console.WriteLine("HTTPS certificate is valid, skipping generation.");
            return;
        }

        Console.WriteLine("Generating HTTPS certificate for localhost...");
        var cert = CertificateManager.GenerateAndInstall();
        WriteThumbprintToConfig(cert.Thumbprint);
        Console.WriteLine($"Certificate installed (thumbprint: {cert.Thumbprint[..8]}...).");
        Console.WriteLine("Trusted in LocalMachine\\Root — valid for localhost connections only.");
    }

    private static string? ReadThumbprintFromConfig()
    {
        if (!File.Exists(OverrideConfigPath)) { return null; }
        try
        {
            var obj = JsonNode.Parse(File.ReadAllText(OverrideConfigPath))?.AsObject();
            return obj?["ServiceConfig"]?["CertificateThumbprint"]?.GetValue<string>();
        }
        catch (JsonException) { return null; }
    }

    private static void WriteThumbprintToConfig(string thumbprint)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OverrideConfigPath)!);

        JsonObject existing = new();
        if (File.Exists(OverrideConfigPath))
        {
            try
            {
                existing = JsonNode.Parse(File.ReadAllText(OverrideConfigPath))?.AsObject() ?? new JsonObject();
            }
            catch (JsonException) { }
        }

        var serviceConfig = existing["ServiceConfig"]?.AsObject().DeepClone() as JsonObject ?? new JsonObject();
        serviceConfig["CertificateThumbprint"] = thumbprint;
        existing["ServiceConfig"] = serviceConfig;
        File.WriteAllText(OverrideConfigPath, existing.ToJsonString(WriteOptions));
    }

    private static void StartServiceCore()
    {
        try
        {
            using var sc = new ServiceController(NativeServiceManager.ServiceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                Console.WriteLine("Service is already running.");
                return;
            }
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            Console.WriteLine("Service started.");
        }
        catch (InvalidOperationException ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            Console.Error.WriteLine($"Failed to start service '{NativeServiceManager.ServiceName}': {detail}");
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            Console.Error.WriteLine($"Service '{NativeServiceManager.ServiceName}' did not reach Running state within 30s. Check Event Viewer for startup errors.");
        }
    }

    internal static void StopServiceIfRunning()
    {
        try
        {
            using var sc = new ServiceController(NativeServiceManager.ServiceName);
            if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            {
                return;
            }

            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
        catch (InvalidOperationException)
        {
            // Not installed — nothing to stop
        }
    }

    /// <summary>
    /// Ensures the embedding model is present in the data directory.
    /// Prompts the user before downloading unless <paramref name="skipPrompt"/> is true.
    /// Returns false if the user declines or download fails.
    /// </summary>
    private static async Task<bool> EnsureModelAsync(bool skipPrompt, CancellationToken cancellationToken)
    {
        string modelPath = Path.Combine(ModelDataDir, "model_quint8_avx2.onnx");
        if (File.Exists(modelPath))
        {
            return true;
        }

        Console.WriteLine();
        Console.WriteLine("  The ContextBridge embedding model is not yet installed.");
        Console.WriteLine("  This step will download ~22 MB (all-MiniLM-L6-v2) from huggingface.co");
        Console.WriteLine($"  and store it in: {ModelDataDir}");
        Console.WriteLine();

        if (!skipPrompt)
        {
            Console.Write("  Proceed with download? [Y/n]: ");
            string? answer = Console.ReadLine()?.Trim();
            if (answer is not null && !answer.Equals("y", StringComparison.OrdinalIgnoreCase) && answer.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Download skipped. Service not installed.");
                Console.WriteLine("  Run 'context-bridge service install' again when ready, or");
                Console.WriteLine("  run 'context-bridge model download' to pre-stage the model.");
                return false;
            }
        }

        Console.WriteLine();
        try
        {
            await ModelDownloader.DownloadAsync(
                ManifestSourceDir,
                ModelDataDir,
                progress: msg => Console.WriteLine($"  {msg}"),
                cancellationToken);
            Console.WriteLine();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"  Model download failed: {ex.Message}");
            Console.Error.WriteLine("  Check your internet connection and try again.");
            return false;
        }
    }
}
