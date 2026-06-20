using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ContextBridge.Cli.WindowsService;

internal static class NativeServiceManager
{
    public const string ServiceName = "ContextBridge";
    public const string ServiceDisplayName = "ContextBridge MCP Memory Server";

    private const uint ScManagerAllAccess = 0xF003F;
    private const uint ServiceAllAccess = 0xF01FF;
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceAutoStart = 0x00000002;
    private const uint ServiceErrorNormal = 0x00000001;

    public static void Install()
    {
        var binaryPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the executable path.");

        var scHandle = OpenSCManager(null, null, ScManagerAllAccess);
        if (scHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open Service Control Manager.");
        }

        try
        {
            var svcHandle = CreateService(
                scHandle,
                ServiceName,
                ServiceDisplayName,
                ServiceAllAccess,
                ServiceWin32OwnProcess,
                ServiceAutoStart,
                ServiceErrorNormal,
                binaryPath,
                null, IntPtr.Zero, null, null, null);

            if (svcHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create Windows Service.");
            }

            CloseServiceHandle(svcHandle);
        }
        finally
        {
            CloseServiceHandle(scHandle);
        }
    }

    public static void Uninstall()
    {
        var scHandle = OpenSCManager(null, null, ScManagerAllAccess);
        if (scHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open Service Control Manager.");
        }

        try
        {
            var svcHandle = OpenService(scHandle, ServiceName, ServiceAllAccess);
            if (svcHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Service '{ServiceName}' not found.");
            }

            try
            {
                if (!DeleteService(svcHandle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to delete service.");
                }
            }
            finally
            {
                CloseServiceHandle(svcHandle);
            }
        }
        finally
        {
            CloseServiceHandle(scHandle);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateService(
        IntPtr scManager, string serviceName, string displayName,
        uint desiredAccess, uint serviceType, uint startType, uint errorControl,
        string binaryPath, string? loadOrderGroup, IntPtr tagId,
        string? dependencies, string? serviceStartName, string? password);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr serviceHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr scObject);
}
