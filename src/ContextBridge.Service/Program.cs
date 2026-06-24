using System.Net;
using ContextBridge.Cli;
using ContextBridge.Core.Repositories;
using ContextBridge.Infrastructure.Embedding;
using ContextBridge.Infrastructure.Mcp;
using ContextBridge.Infrastructure.Storage;
using ContextBridge.Service;
using ContextBridge.Service.Dashboard;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;

var programDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "ContextBridge");

// Shared MCP server instructions for both HTTP (service) and stdio (Claude Desktop) modes.
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

    Handoff — session state bridging (ephemeral, not a memory):
    - handoff_write — capture current session state before ending a work session. Include: what you were working on, key decisions made, next steps, and any blockers. Pass project: <repo-name> to scope the handoff.
    - handoff_list — call at the start of every session to check for a prior handoff. Filter by project when the working directory is known.
    - handoff_acknowledge — call immediately after processing a handoff from handoff_list. This removes it permanently; do not call if no handoff was found.

    Handoffs are not memories. Do not convert handoff content to memories automatically; write memories only for facts that independently warrant permanent storage.
    """;

// stdio mode — spawned by Claude Desktop; minimal host, no HTTP stack.
if (args.Length > 0 && args[0].Equals("stdio", StringComparison.OrdinalIgnoreCase))
{
    var stdioBuilder = Host.CreateApplicationBuilder(args);

    // stdout is the MCP protocol channel — route all host logging to stderr so it
    // doesn't corrupt the JSON-RPC framing that Claude Desktop reads from stdout.
    stdioBuilder.Logging.ClearProviders();
    stdioBuilder.Logging.AddConsole(options =>
        options.LogToStandardErrorThreshold = LogLevel.Trace);
    stdioBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
    stdioBuilder.Configuration.AddJsonFile(
        Path.Combine(programDataPath, "appsettings.json"), optional: true);

    var stdioModelDir = ResolveModelDir(programDataPath);
    stdioBuilder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
        _ => new BundledOnnxEmbeddingGenerator(
            Path.Combine(stdioModelDir, "model_quint8_avx2.onnx"),
            Path.Combine(stdioModelDir, "vocab.txt")));

    var stdioVecPath = SqliteConnectionFactory.ResolveVecExtensionPath();
    stdioBuilder.Services.AddSingleton(
        new SqliteConnectionFactory(Path.Combine(programDataPath, "memories.db"), stdioVecPath));
    stdioBuilder.Services.AddSingleton<SchemaInitializer>();
    stdioBuilder.Services.AddSingleton<IMemoryRepository, MemoryRepository>();
    stdioBuilder.Services.AddSingleton<IHandoffRepository, HandoffRepository>();

    stdioBuilder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "context-bridge", Version = "1.0.0" };
            options.ServerInstructions = mcpInstructions;
        })
        .WithStdioServerTransport()
        .WithTools<MemoryMcpTools>()
        .WithTools<HandoffMcpTools>()
        .WithPrompts<HandoffMcpPrompts>();

    var stdioHost = stdioBuilder.Build();
    await stdioHost.Services.GetRequiredService<SchemaInitializer>()
        .InitializeAsync(CancellationToken.None);
    await stdioHost.RunAsync();
    return 0;
}

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
builder.Services.AddSingleton<IHandoffRepository, HandoffRepository>();

// Background worker for warm-up and schema init
builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options => options.ServiceName = "ContextBridge");

// MCP server with Streamable HTTP transport
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "context-bridge", Version = "1.0.0" };
        options.ServerInstructions = mcpInstructions;
    })
    .WithHttpTransport()
    .WithTools<MemoryMcpTools>()
    .WithTools<HandoffMcpTools>()
    .WithPrompts<HandoffMcpPrompts>();

var port = builder.Configuration.GetValue("ServiceConfig:Port", 5290);

// Kestrel — localhost only, never 0.0.0.0.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.Loopback, port);
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
