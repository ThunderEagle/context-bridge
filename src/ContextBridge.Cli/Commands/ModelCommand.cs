using System.CommandLine;
using ContextBridge.Infrastructure.Embedding;

namespace ContextBridge.Cli.Commands;

internal static class ModelCommand
{
    private static readonly string ModelDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ContextBridge", "models", "all-MiniLM-L6-v2");

    private static readonly string ManifestSourceDir = Path.Combine(
        AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2");

    public static Command Build()
    {
        var cmd = new Command("model", "Manage the bundled embedding model");
        cmd.Add(BuildDownload());
        return cmd;
    }

    private static Command BuildDownload()
    {
        var yes = new Option<bool>("--yes", "Skip confirmation prompt");
        var cmd = new Command("download", "Download the embedding model to the data directory");
        cmd.Add(yes);
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            string modelPath = Path.Combine(ModelDataDir, "model_quint8_avx2.onnx");
            if (File.Exists(modelPath))
            {
                Console.WriteLine($"Model is already present at {ModelDataDir}");
                Console.WriteLine("Run 'context-bridge model download --yes' to force re-download and re-verify.");
                if (!parseResult.GetValue(yes))
                {
                    return;
                }
            }

            Console.WriteLine($"Target directory: {ModelDataDir}");
            Console.WriteLine("This will download ~22 MB (all-MiniLM-L6-v2) from huggingface.co");
            Console.WriteLine();

            if (!parseResult.GetValue(yes))
            {
                Console.Write("Proceed? [Y/n]: ");
                string? answer = Console.ReadLine()?.Trim();
                if (answer is not null && !answer.Equals("y", StringComparison.OrdinalIgnoreCase) && answer.Length > 0)
                {
                    Console.WriteLine("Download cancelled.");
                    return;
                }
            }

            try
            {
                await ModelDownloader.DownloadAsync(
                    ManifestSourceDir,
                    ModelDataDir,
                    progress: Console.WriteLine,
                    cancellationToken);
                Console.WriteLine("Model ready.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Download failed: {ex.Message}");
            }
        });
        return cmd;
    }
}
