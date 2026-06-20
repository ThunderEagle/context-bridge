using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace ContextBridge.Infrastructure.Embedding;

public sealed class BundledOnnxEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int MaxSequenceLength = 128;
    private const int Dimensions = 384;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;

    public EmbeddingGeneratorMetadata Metadata { get; } =
        new("BundledOnnx", providerUri: null, defaultModelId: "all-MiniLM-L6-v2", defaultModelDimensions: Dimensions);

    public BundledOnnxEmbeddingGenerator(string modelPath, string vocabPath)
    {
        _session = new InferenceSession(modelPath);
        _tokenizer = BertTokenizer.Create(vocabPath, new BertOptions { LowerCaseBeforeTokenization = true });
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var texts = values.ToList();
        int batchSize = texts.Count;

        var encodings = TokenizeBatch(texts);
        float[][] vectors = await Task.Run(() => RunInference(encodings, batchSize), cancellationToken);

        return new GeneratedEmbeddings<Embedding<float>>(
            vectors.Select(v => new Embedding<float>(v)));
    }

    private List<int[]> TokenizeBatch(List<string> texts)
    {
        var encodings = new List<int[]>(texts.Count);
        foreach (var text in texts)
        {
            IReadOnlyList<int> ids = _tokenizer.EncodeToIds(
                text,
                maxTokenCount: MaxSequenceLength,
                addSpecialTokens: true,
                normalizedText: out _,
                charsConsumed: out _,
                considerPreTokenization: true,
                considerNormalization: true);
            encodings.Add([.. ids]);
        }
        return encodings;
    }

    private float[][] RunInference(List<int[]> encodings, int batchSize)
    {
        // Build [batch × seq] tensors for all three BERT inputs
        int totalLen = batchSize * MaxSequenceLength;
        var inputIdsData = new long[totalLen];
        var attentionMaskData = new long[totalLen];
        var tokenTypeIdsData = new long[totalLen]; // stays zero

        for (int b = 0; b < batchSize; b++)
        {
            int[] ids = encodings[b];
            int offset = b * MaxSequenceLength;
            for (int s = 0; s < MaxSequenceLength; s++)
            {
                if (s < ids.Length)
                {
                    inputIdsData[offset + s] = ids[s];
                    attentionMaskData[offset + s] = 1L;
                }
                else
                {
                    inputIdsData[offset + s] = _tokenizer.PaddingTokenId;
                    attentionMaskData[offset + s] = 0L;
                }
            }
        }

        int[] shape = [batchSize, MaxSequenceLength];
        var inputIds = new DenseTensor<long>(inputIdsData, shape);
        var attentionMask = new DenseTensor<long>(attentionMaskData, shape);
        var tokenTypeIds = new DenseTensor<long>(tokenTypeIdsData, shape);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };

        using var outputs = _session.Run(inputs);

        // last_hidden_state: [batch, seq, 384]
        var hiddenState = outputs.First(o => o.Name == "last_hidden_state").AsTensor<float>();

        var result = new float[batchSize][];
        for (int b = 0; b < batchSize; b++)
        {
            result[b] = MeanPoolAndNormalize(hiddenState, attentionMaskData, b);
        }
        return result;
    }

    private static float[] MeanPoolAndNormalize(Tensor<float> hiddenState, long[] attentionMaskData, int batch)
    {
        var pooled = new float[Dimensions];
        int maskSum = 0;
        int offset = batch * MaxSequenceLength;

        for (int s = 0; s < MaxSequenceLength; s++)
        {
            if (attentionMaskData[offset + s] == 0L)
            {
                continue;
            }
            maskSum++;
            for (int d = 0; d < Dimensions; d++)
            {
                pooled[d] += hiddenState[batch, s, d];
            }
        }

        if (maskSum > 0)
        {
            float count = maskSum;
            for (int d = 0; d < Dimensions; d++)
            {
                pooled[d] /= count;
            }
        }

        // L2 normalize
        float normSq = 0f;
        for (int d = 0; d < Dimensions; d++)
        {
            normSq += pooled[d] * pooled[d];
        }

        float norm = MathF.Sqrt(normSq);
        if (norm > 0f)
        {
            for (int d = 0; d < Dimensions; d++)
            {
                pooled[d] /= norm;
            }
        }

        return pooled;
    }

    public object? GetService(Type serviceType, object? key) => null;

    public void Dispose() => _session.Dispose();
}
