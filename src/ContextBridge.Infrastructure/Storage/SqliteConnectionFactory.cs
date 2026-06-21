using Microsoft.Data.Sqlite;

namespace ContextBridge.Infrastructure.Storage;

public sealed class SqliteConnectionFactory
{
    private readonly string _databasePath;
    private readonly string? _vecExtensionPath;

    public SqliteConnectionFactory(string databasePath, string? vecExtensionPath)
    {
        _databasePath = databasePath;
        _vecExtensionPath = vecExtensionPath;
    }

    public SqliteConnection Create()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString());

        connection.Open();

        if (_vecExtensionPath is not null)
        {
            connection.EnableExtensions(true);
            connection.LoadExtension(_vecExtensionPath);
        }

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    /// <summary>
    /// Resolves the platform-appropriate vec0 extension path from known installation locations.
    /// Returns null if the extension binary is not found — callers should surface a meaningful error.
    /// </summary>
    public static string? ResolveVecExtensionPath()
    {
        var fileName = VecExtensionFileName();

        // 1. ProgramData installation path (set by 'service install')
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ContextBridge", "native", fileName);
        if (File.Exists(programData))
        {
            return programData;
        }

        // 2. Walk up from the application base directory (dev/test local copy)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "native", PlatformRid(), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        return null;
    }

    private static string VecExtensionFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "vec0.dll";
        }
        if (OperatingSystem.IsMacOS())
        {
            return "vec0.dylib";
        }
        return "vec0.so";
    }

    private static string PlatformRid()
    {
        if (OperatingSystem.IsWindows())
        {
            return "win-x64";
        }
        if (OperatingSystem.IsMacOS())
        {
            return "osx-arm64";
        }
        return "linux-x64";
    }
}
