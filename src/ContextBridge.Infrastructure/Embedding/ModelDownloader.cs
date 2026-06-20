using System.Security.Cryptography;
using System.Text.Json;

namespace ContextBridge.Infrastructure.Embedding;

public static class ModelDownloader
{
    private const string ManifestName = "manifest.json";

    /// <summary>
    /// Downloads model files declared in manifest.json to <paramref name="targetDir"/>.
    /// Verifies the SHA256 of the ONNX file after download.
    /// Skips files that are already present and valid.
    /// </summary>
    public static async Task DownloadAsync(
        string manifestDir,
        string targetDir,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(Path.Combine(manifestDir, ManifestName), cancellationToken);

        Directory.CreateDirectory(targetDir);

        string modelTarget = Path.Combine(targetDir, manifest.ModelFile);
        string vocabTarget = Path.Combine(targetDir, manifest.VocabFile);

        if (IsValidModel(modelTarget, manifest.Sha256))
        {
            progress?.Invoke($"Model already present and verified at {targetDir}");
        }
        else
        {
            progress?.Invoke($"Downloading embedding model (~22 MB) from {manifest.Source}");
            await DownloadFileAsync(manifest.Source, modelTarget, cancellationToken);

            progress?.Invoke("Verifying SHA256...");
            VerifyHash(modelTarget, manifest.Sha256);
            progress?.Invoke("Verified.");
        }

        if (!File.Exists(vocabTarget))
        {
            progress?.Invoke("Downloading vocabulary file...");
            await DownloadFileAsync(manifest.VocabSource, vocabTarget, cancellationToken);
        }

        // Write a copy of the manifest into the target dir so the service can read it
        File.Copy(Path.Combine(manifestDir, ManifestName), Path.Combine(targetDir, ManifestName), overwrite: true);
    }

    private static bool IsValidModel(string path, string expectedSha256)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            VerifyHash(path, expectedSha256);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void VerifyHash(string path, string expectedSha256)
    {
        using var stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        string actual = Convert.ToHexString(hash);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SHA256 mismatch for {Path.GetFileName(path)}.{Environment.NewLine}" +
                $"  Expected: {expectedSha256}{Environment.NewLine}" +
                $"  Actual:   {actual}");
        }
    }

    private static async Task DownloadFileAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        string tempPath = targetPath + ".tmp";
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(tempPath);
            await stream.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }

        File.Move(tempPath, targetPath, overwrite: true);
    }

    private static async Task<ModelManifest> ReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"Model manifest not found at {manifestPath}. The application may not have been installed correctly.",
                manifestPath);
        }

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<ModelManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        return manifest ?? throw new InvalidOperationException($"Failed to parse manifest at {manifestPath}");
    }

    private sealed record ModelManifest(
        string ModelFile,
        string VocabFile,
        string Source,
        string VocabSource,
        string Sha256);
}
