using ContextBridge.Infrastructure.Storage;
using Microsoft.Extensions.AI;

namespace ContextBridge.Service;

public class Worker(
    ILogger<Worker> logger,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    SchemaInitializer schemaInitializer) : BackgroundService
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
    }
}
