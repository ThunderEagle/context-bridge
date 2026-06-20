using ContextBridge.Cli;
using ContextBridge.Infrastructure.Embedding;
using ContextBridge.Infrastructure.Security;
using ContextBridge.Service;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var programDataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "ContextBridge");

var keysDir = new DirectoryInfo(Path.Combine(programDataPath, "keys"));

if (args.Length > 0)
{
    // CLI mode — build a minimal container with only DataProtection + TokenStore
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile(Path.Combine(programDataPath, "appsettings.json"), optional: true)
        .Build();

    var services = new ServiceCollection();
    services.AddDataProtection()
        .PersistKeysToFileSystem(keysDir)
        .ProtectKeysWithDpapi(protectToLocalMachine: true)
        .SetApplicationName("ContextBridge");
    services.AddSingleton<TokenStore>();

    await using var sp = services.BuildServiceProvider();
    var tokenStore = sp.GetRequiredService<TokenStore>();

    return await CliCommandBuilder.Build(tokenStore, configuration)
        .Parse(args)
        .InvokeAsync();
}

// Service mode
var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(programDataPath, "appsettings.json"),
    optional: true,
    reloadOnChange: true);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(keysDir)
    .ProtectKeysWithDpapi(protectToLocalMachine: true)
    .SetApplicationName("ContextBridge");

builder.Services.AddSingleton<TokenStore>();

var modelDir = Path.Combine(AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2");
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new BundledOnnxEmbeddingGenerator(
        Path.Combine(modelDir, "model_quint8_avx2.onnx"),
        Path.Combine(modelDir, "vocab.txt")));

builder.Services.AddHostedService<Worker>();
builder.Services.AddWindowsService(options => options.ServiceName = "ContextBridge");

var host = builder.Build();
await host.RunAsync();
return 0;
