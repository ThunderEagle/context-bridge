using System.Security.Principal;

namespace ContextBridge.Cli;

public static class CliHelpers
{
    public static bool RequireAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            return true;
        }

        Console.Error.WriteLine("This command requires administrator privileges.");
        Console.Error.WriteLine("Open PowerShell as Administrator and try again.");
        return false;
    }
}
