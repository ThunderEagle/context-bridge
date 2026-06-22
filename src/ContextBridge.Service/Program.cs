using System.Net;
using ContextBridge.Cli;
using ContextBridge.Core.Repositories;
using ContextBridge.Infrastructure.Embedding;
using ContextBridge.Infrastructure.Mcp;
using ContextBridge.Infrastructure.Security;
using ContextBridge.Infrastructure.Storage;
using ContextBridge.Service;
using ContextBridge.Service.Dashboard;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

var programDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "ContextBridge");

if (args.Length > 0)
{
    // CLI mode — minimal container
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile(Path.Combine(programDataPath, "appsettings.json"), optional: true)
        .Build();

    return await CliCommandBuilder.Build(configuration)
        .Parse(args)
        .InvokeAsync();
}

// Service mode — full WebApplication host
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(programDataPath, "appsettings.json"),
    optional: true,
    reloadOnChange: true);

// Models are installed to ProgramData by 'service install'; fall back to BaseDirectory for dev.
// Factory registration (not eager instance) so ONNX session creation happens after SERVICE_RUNNING
// is signaled to SCM — avoids the 30-second Windows Service startup timeout on cold JIT.
var modelDir = ResolveModelDir(programDataPath);
var modelPath = Path.Combine(modelDir, "model_quint8_avx2.onnx");
var vocabPath = Path.Combine(modelDir, "vocab.txt");
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    _ => new BundledOnnxEmbeddingGenerator(modelPath, vocabPath));

// Storage — SQLite + sqlite-vec
var dbPath = Path.Combine(programDataPath, "memories.db");
var vecExtensionPath = SqliteConnectionFactory.ResolveVecExtensionPath();
builder.Services.AddSingleton(new SqliteConnectionFactory(dbPath, vecExtensionPath));
builder.Services.AddSingleton<SchemaInitializer>();
builder.Services.AddSingleton<IMemoryRepository, MemoryRepository>();

// Background worker for warm-up and schema init
builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options => options.ServiceName = "ContextBridge");

// MCP server with Streamable HTTP transport
const string mcpInstructions =
    """
    You have access to a persistent memory service (context-bridge). Use it to preserve and retrieve context across sessions.

    memory_write — store a single memory immediately after a significant decision, architectural choice, or preference.
    memory_batch_write — store multiple related memories atomically; prefer this over sequential writes.
    memory_search — semantic search with natural language; call at session start to surface relevant prior context.
    memory_list — paginated list of all memories; useful for browsing or auditing.
    memory_update — update an existing memory when its content changes.
    memory_delete — remove a memory that is no longer accurate or relevant.
    memory_status — service health and record counts.

    Tag conventions (apply on every write):
    - project:<repo-name> — scope the memory to the current repository
    - type:decision — architectural or technology choice
    - type:preference — coding style, tooling, or workflow preference
    - type:pattern — recurring pattern or convention established in this codebase
    - type:reference — pointer to external resources, docs, or issue trackers

    Write memories incrementally during the session. Do not batch everything for session end.
    """;

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "context-bridge", Version = "1.0.0" };
        options.ServerInstructions = mcpInstructions;
    })
    .WithHttpTransport()
    .WithTools<MemoryMcpTools>();

var port = builder.Configuration.GetValue("ServiceConfig:Port", 5290);
var thumbprint = builder.Configuration.GetValue<string>("ServiceConfig:CertificateThumbprint");

// Kestrel — localhost only, never 0.0.0.0.
// HTTPS when a cert thumbprint is configured (installed via 'service install').
// Falls back to HTTP for local dev runs without a certificate.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.Loopback, port, listenOptions =>
    {
        if (!string.IsNullOrWhiteSpace(thumbprint))
        {
            listenOptions.UseHttps(https =>
            {
                https.ServerCertificateSelector = (_, _) => CertificateManager.FindByThumbprint(thumbprint);
            });
        }
    });
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapDashboard();
app.MapMcp("/mcp");

await app.RunAsync();
return 0;

static string ResolveModelDir(string programDataPath)
{
    var programDataModel = Path.Combine(programDataPath, "models", "all-MiniLM-L6-v2");
    if (Directory.Exists(programDataModel))
    {
        return programDataModel;
    }

    return Path.Combine(AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2");
}
