using System.ComponentModel;
using System.CommandLine;
using System.ServiceProcess;
using ContextBridge.Cli.WindowsService;

namespace ContextBridge.Cli.Commands;

internal static class ServiceCommand
{
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
        var cmd = new Command("install", "Register and start the ContextBridge Windows Service (requires admin)");
        cmd.SetAction(_ =>
        {
            if (!CliHelpers.RequireAdmin()) { return; }
            try
            {
                NativeServiceManager.Install();
                Console.WriteLine($"Service '{NativeServiceManager.ServiceName}' installed successfully.");
                StartServiceCore();
            }
            catch (Win32Exception ex)
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
                StopServiceIfRunning();
                NativeServiceManager.Uninstall();
                Console.WriteLine($"Service '{NativeServiceManager.ServiceName}' uninstalled.");
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
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine($"Service '{NativeServiceManager.ServiceName}' is not installed.");
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
}
