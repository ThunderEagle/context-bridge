using ContextBridge.Infrastructure.Embedding;
using Microsoft.Extensions.AI;

namespace ContextBridge.Tests.Embedding;

[Trait("Category", "LocalOnly")]
public sealed class BundledOnnxEmbeddingGeneratorTests : IDisposable
{
    // Walk up from the test assembly to find the repo root models/ directory
    private static readonly string ModelDir = FindModelDir();
    private static readonly string ModelPath = Path.Combine(ModelDir, "model_quint8_avx2.onnx");
    private static readonly string VocabPath = Path.Combine(ModelDir, "vocab.txt");

    private readonly BundledOnnxEmbeddingGenerator _generator = new(ModelPath, VocabPath);

    [Fact]
    public async Task GenerateAsync_SingleInput_Returns384DimVector()
    {
        GeneratedEmbeddings<Embedding<float>> result = await _generator.GenerateAsync(["hello world"]);

        Assert.Single(result);
        Assert.Equal(384, result[0].Vector.Length);
    }

    [Fact]
    public async Task GenerateAsync_SemanticallySimilarSentences_HighCosineSimilarity()
    {
        var result = await _generator.GenerateAsync([
            "The cat sat on the mat",
            "A cat is sitting on a mat"
        ]);

        float similarity = CosineSimilarity(result[0].Vector.Span, result[1].Vector.Span);
        Assert.True(similarity > 0.85f, $"Expected similarity > 0.85 for similar sentences, got {similarity:F4}");
    }

    [Fact]
    public async Task GenerateAsync_DissimilarSentences_LowerSimilarityThanSimilarPair()
    {
        var result = await _generator.GenerateAsync([
            "The cat sat on the mat",
            "A cat is sitting on a mat",
            "Stocks fell sharply on Wall Street"
        ]);

        float similarPairScore = CosineSimilarity(result[0].Vector.Span, result[1].Vector.Span);
        float dissimilarPairScore = CosineSimilarity(result[0].Vector.Span, result[2].Vector.Span);

        Assert.True(
            similarPairScore > dissimilarPairScore,
            $"Similar pair ({similarPairScore:F4}) should score higher than dissimilar pair ({dissimilarPairScore:F4})");
    }

    [Fact]
    public async Task GenerateAsync_BatchInput_ReturnsOneEmbeddingPerText()
    {
        var texts = new[] { "first", "second", "third", "fourth" };
        var result = await _generator.GenerateAsync(texts);

        Assert.Equal(texts.Length, result.Count);
        Assert.All(result, e => Assert.Equal(384, e.Vector.Length));
    }

    public void Dispose() => _generator.Dispose();

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }
        // Vectors are already L2-normalized by BundledOnnxEmbeddingGenerator, so cosine = dot product
        return dot;
    }

    private static string FindModelDir()
    {
        // 1. ProgramData — set by 'service install' or 'model download'
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ContextBridge", "models", "all-MiniLM-L6-v2");
        if (File.Exists(Path.Combine(programData, "model_quint8_avx2.onnx")))
        {
            return programData;
        }

        // 2. Walk up from the test assembly to find models/ in the repo (gitignored local copy)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "models", "all-MiniLM-L6-v2");
            if (File.Exists(Path.Combine(candidate, "model_quint8_avx2.onnx")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Embedding model not found. Run 'context-bridge model download' or 'context-bridge service install' first.");
    }
}
