using System.Security.AccessControl;
using System.Security.Principal;
using ContextBridge.Core.Repositories;
using ContextBridge.Infrastructure.Storage;
using Microsoft.Extensions.AI;

namespace ContextBridge.Service;

public class Worker(
    ILogger<Worker> logger,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    SchemaInitializer schemaInitializer,
    IHandoffRepository handoffRepository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warm-up: run one inference to load the ONNX model into memory before clients connect
        var probe = await embeddingGenerator.GenerateAsync(["warm-up"], cancellationToken: stoppingToken);
        logger.LogInformation(
            "Embedding model loaded (all-MiniLM-L6-v2, {Dims} dims)",
            probe[0].Vector.Length);

        // Initialize / migrate the SQLite schema on every startup
        await schemaInitializer.InitializeAsync(stoppingToken);
        logger.LogInformation("Storage schema initialized");

        // Remove expired handoffs that were never acknowledged
        var purged = await handoffRepository.PurgeExpiredAsync(stoppingToken);
        if (purged > 0)
        {
            logger.LogInformation("Purged {Count} expired handoff(s)", purged);
        }

        // The service runs as LocalSystem. Ensure the data directory grants the Users group
        // Modify access so the stdio subprocess (running as the current user) can write memories.db.
        EnsureDataDirPermissions(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ContextBridge"));
    }

    private static void EnsureDataDirPermissions(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var dirInfo = new DirectoryInfo(dataDir);
        var security = dirInfo.GetAccessControl(AccessControlSections.Access);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var rule = new FileSystemAccessRule(
            users,
            FileSystemRights.Modify | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);
        security.AddAccessRule(rule);
        dirInfo.SetAccessControl(security);
    }
}
