# Phase 2 Review Guide: Embedding Pipeline

## Overview

Phase 2 adds the **embedding pipeline** — the core capability that converts natural language text into numerical vectors that can be semantically compared. This is the foundation for "memory" to work: without embeddings, the system has no way to determine if a new query is similar to stored memories.

### What Problem Does This Solve?

Currently, if you store a memory "use PascalCase for C# class names" and later search for "csharp naming conventions", the system has no way to know these are related. Embeddings solve this by converting both text snippets into 384-dimensional vectors where semantically similar texts are spatially close. The search will find the matching memory even though the words are different.

### Key Insight

> This is not about searching for keywords. It's about semantic similarity. "Use PascalCase for class names" and "class names should follow PascalCase convention" produce nearly identical vectors even though the wording differs.

---

## Concepts You Need to Know

### 1. **Embeddings**

An **embedding** is a fixed-size array of numbers (floats) that represents the meaning of text.

- `all-MiniLM-L6-v2` produces **384-dimensional** vectors
- Two texts with similar meaning will have vectors that point in similar directions (high cosine similarity)
- Example:
  ```
  "Use PascalCase for class names"  → [-0.15, 0.42, -0.88, ..., 0.21]  (384 values)
  "Classes should use PascalCase"   → [-0.14, 0.41, -0.87, ..., 0.20]  (384 values, very similar!)
  "Python uses snake_case"          → [0.33, -0.12, 0.51, ..., -0.41]  (384 values, different direction)
  ```
- Measuring how "close" vectors are = cosine similarity (dot product of normalized vectors)

### 2. **ONNX (Open Neural Network Exchange)**

ONNX is a standardized format for machine learning models that lets you run them without the framework that trained them.

- The `all-MiniLM-L6-v2` model is trained in PyTorch by Sentence Transformers
- We're using its ONNX export (a compiled version) that runs via ONNX Runtime
- Benefits: No Python, no PyTorch dependency, runs natively on CPU, ~22 MB binary
- The model is quantized to INT8 (integers instead of floats) — negligible quality loss, 4× smaller

### 3. **Tokenization**

Before feeding text to the embedding model, you must convert it to numbers the model understands.

- `all-MiniLM-L6-v2` uses the BERT tokenizer (WordPiece algorithm)
- Tokenization breaks text into tokens: `"Hello world!"` → `["hello", "world", "!"]` → `[7592, 2088, 999]`
- BERT adds special tokens: `[CLS] Hello world [SEP]` (CLS = classification token, SEP = separator)
- Max length is 128 tokens; longer text is truncated
- Padding: shorter sequences are padded with a special padding token to reach 128 tokens

### 4. **Tensor / Tensor Operations**

A **tensor** is a multidimensional array (1D = vector, 2D = matrix, 3D = cube of numbers).

- Input tensors to BERT:
  - `input_ids` [batch_size × 128]: token IDs
  - `attention_mask` [batch_size × 128]: which positions to pay attention to (1 = real token, 0 = padding)
  - `token_type_ids` [batch_size × 128]: always 0 for single-sequence input
- Output: `last_hidden_state` [batch_size × 128 × 384]: 384-dimensional representation of each token
- Post-processing: mean pool the tokens, then L2 normalize → final 384-dim vector

### 5. **Batch Processing**

Instead of embedding one text at a time, batch them together:
- Request embeddings for ["text 1", "text 2", "text 3"] at once
- Process as a batch (more efficient) rather than three separate calls
- This is what `BundledOnnxEmbeddingGenerator.GenerateAsync` does

---

## Architecture: How It Fits In

```
Program.cs (DI setup)
    ↓
BundledOnnxEmbeddingGenerator (implements IEmbeddingGenerator<string, Embedding<float>>)
    ├→ Uses BertTokenizer (from Microsoft.ML.Tokenizers)
    └→ Uses InferenceSession (from Microsoft.ML.OnnxRuntime)

Worker.cs (future: MCP server) will call GenerateAsync(texts) to embed memories
```

**Dependency flow:**
```
.NET IEmbeddingGenerator abstraction
    ↑
    └─ BundledOnnxEmbeddingGenerator (our implementation)
        ├─ BertTokenizer (tokenizes text → token IDs)
        └─ ONNX Runtime (runs the model)
```

By implementing `IEmbeddingGenerator<string, Embedding<float>>`, we integrate with:
- Microsoft.Extensions.AI's standard embedding abstraction
- Future support for other embedding providers (Ollama, OpenAI) without changing client code
- Built-in support for caching and observability middleware (for future phases)

---

## File-by-File Walkthrough

### 1. **`docs/adr/ADR-005-all-minilm-l6-v2-int8-model.md`**

**What:** Architectural Decision Record — why we chose this specific model.

**Key decisions:**
- ✅ Use all-MiniLM-L6-v2 (proven sentence embedding model, small, fast)
- ✅ INT8 quantized (~22 MB) instead of FP32 (~88 MB) — 4× smaller with >99% quality retained
- ✅ Download at install time (not bundled in git) — manifests is committed, binary is not
- ✅ Install to `%PROGRAMDATA%\ContextBridge\models\` (survives dotnet tool update cycles)

**Why this matters for your review:**
- Understand the trade-offs: smaller footprint vs. model quality
- Know that upgrading the model in future = breaking change (all stored vectors become incompatible)
- The manifest/SHA256 verification approach prevents silent corruption

### 2. **`docs/adr/ADR-006-microsoft-ml-tokenizers.md`**

**What:** Why we chose `Microsoft.ML.Tokenizers` for the BERT tokenizer.

**Key decisions:**
- ✅ Use Microsoft-maintained `Microsoft.ML.Tokenizers` (active development, aligned with ONNX Runtime team)
- ✅ Use `BertTokenizer` class — implements full WordPiece pipeline
- ❌ Reject custom implementation — too much surface area for subtle bugs
- ❌ Reject community libraries — unmaintained or misaligned with the ML ecosystem

**Why this matters for your review:**
- The tokenizer must exactly match how the model was trained (BERT's WordPiece algorithm)
- Thread-safe singleton registration (safe for concurrent service use)
- Microsoft maintenance = confidence we're using a trusted component

### 3. **`models/all-MiniLM-L6-v2/manifest.json`**

**What:** Authoritative spec for the embedding model (source URL, SHA256, metadata).

**Structure:**
```json
{
  "modelFile": "model_quint8_avx2.onnx",    // ONNX model binary (~22 MB)
  "vocabFile": "vocab.txt",                 // BERT vocabulary (30,522 tokens)
  "source": "https://huggingface.co/...",   // Where to download the model
  "vocabSource": "https://huggingface.co/...", // Where to download vocab
  "sha256": "abc123..."                     // Hash to verify integrity
}
```

**Why this matters for your review:**
- This is the *spec*, not the binary itself
- The model binary is `.gitignored`; only the manifest is committed
- When you run `context-bridge service install`, this manifest drives the download

### 4. **`scripts/update-model.ps1`**

**What:** PowerShell script to download a new model and update the manifest.

**Usage (future upgrades):**
```powershell
.\scripts/update-model.ps1 -ModelUrl "https://..." -ManifestPath "models/all-MiniLM-L6-v2/manifest.json"
```

**Why this matters for your review:**
- Future-proofing: upgrading the model follows this procedure
- The procedure is documented so it's repeatable and auditable

### 5. **`src/ContextBridge.Infrastructure/Embedding/BundledOnnxEmbeddingGenerator.cs`**

**What:** The core implementation — converts text → vectors.

**Key class: `BundledOnnxEmbeddingGenerator`**

Implements `IEmbeddingGenerator<string, Embedding<float>>`:
- Constructor: takes paths to the ONNX model file and vocab file
- `GenerateAsync(IEnumerable<string> values)`: main entry point
  - Input: list of text strings
  - Output: list of 384-dimensional `Embedding<float>` objects

**Internal methods:**

1. **`TokenizeBatch(List<string> texts)`**
   - Converts each text string to token IDs
   - Calls `BertTokenizer.EncodeToIds()` with:
     - `maxTokenCount: MaxSequenceLength` (128) — truncate if longer
     - `addSpecialTokens: true` — insert [CLS] and [SEP]
     - `considerPreTokenization: true` — handle punctuation
     - `considerNormalization: true` — lowercase before tokenizing
   - Returns: `List<int[]>` where each array is the token IDs

   **Example:**
   ```
   Input text: "Use PascalCase for class names"
   After tokenization: [101, 3384, 4239, 1041, 2005, 1500, 2313, 102, 0, 0, ...]
                       ^                                           ^  ^
                       CLS token                                 SEP padding
   ```

2. **`RunInference(List<int[]> encodings, int batchSize)`**
   - This is the heavy lifting — where ONNX Runtime actually runs the model
   
   **Step 1: Build tensors from token IDs**
   ```
   inputIdsData [batch × 128]:       token IDs (padded to 128)
   attentionMaskData [batch × 128]: 1 for real tokens, 0 for padding
   tokenTypeIdsData [batch × 128]:  always 0 (single-sequence input)
   ```
   
   **Step 2: Run the model**
   ```csharp
   using var outputs = _session.Run(inputs);
   ```
   - ONNX Runtime executes the neural network
   - Input: token IDs, attention mask, token type IDs
   - Output: `last_hidden_state` [batch × 128 × 384] — 384-dim representation for each token
   
   **Step 3: Post-process each batch sample**
   - Call `MeanPoolAndNormalize()` for each sequence in the batch
   - Returns: `float[][]` where each row is a 384-dim vector

3. **`MeanPoolAndNormalize(Tensor<float> hiddenState, long[] attentionMaskData, int batch)`**
   - Input: [batch × 128 × 384] tensor of hidden states from the model
   - Goal: reduce [128 × 384] tokens → single [384] vector
   
   **Mean pooling:** average the embeddings of non-padding tokens
   ```
   pooled[d] = sum(hiddenState[t][d] for t in non-padding tokens) / num_tokens
   ```
   
   **L2 normalization:** ensure the vector has magnitude 1 (standard for cosine similarity)
   ```
   normalized[d] = pooled[d] / sqrt(sum(pooled[d]^2))
   ```
   
   **Why normalize?** Cosine similarity of normalized vectors = dot product. Faster than computing the full cosine formula.

**Why this matters for your review:**
- This is where text becomes vectors
- The three ONNX input tensors must match exactly what the model expects (standard BERT inputs)
- Mean pooling + L2 norm is the standard sentence-transformer pooling strategy
- Batch processing is more efficient than processing one-at-a-time (important for future MCP tool calls)

### 6. **`src/ContextBridge.Infrastructure/Embedding/ModelDownloader.cs`**

**What:** Downloads and verifies the model file at install/setup time.

**Key method: `DownloadAsync(string manifestDir, string targetDir, ...)`**

**Flow:**
1. Read `manifest.json` from the repo (contains source URL and expected SHA256)
2. Check if model already exists at target and is valid (hash matches)
3. If not found or invalid:
   - Download the model from HuggingFace (~22 MB)
   - Verify SHA256 matches expected value
   - Throw if mismatch (prevents using corrupted files)
4. Download vocab file if not present
5. Copy manifest to target dir

**Key security detail:** `VerifyHash()` uses SHA256 to ensure the downloaded file matches what was expected. This prevents man-in-the-middle or corruption.

**Why this matters for your review:**
- This is called during `context-bridge service install`
- It's the install-time step that avoids bundling a 22 MB binary in git
- The manifest is the source of truth; updates are auditable (git diff on manifest.json)

### 7. **`src/ContextBridge.Service/Program.cs` (changes)**

**What:** Dependency injection setup — how the embedding generator gets wired in.

**Key changes:**
```csharp
// Register the embedding generator as a singleton
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new BundledOnnxEmbeddingGenerator(
        Path.Combine(modelDir, "model_quint8_avx2.onnx"),
        Path.Combine(modelDir, "vocab.txt")));
```

**Model resolution logic:**
```csharp
static string ResolveModelDir(string programDataPath)
{
    // Production: check %PROGRAMDATA%\ContextBridge\models\all-MiniLM-L6-v2
    var programDataModel = Path.Combine(programDataPath, "models", "all-MiniLM-L6-v2");
    if (Directory.Exists(programDataModel))
        return programDataModel;

    // Development: fall back to src/ContextBridge.Service/models/all-MiniLM-L6-v2
    return Path.Combine(AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2");
}
```

**Why this matters for your review:**
- Singleton registration: the model is loaded once at service startup and stays in memory
- Falls back to dev directory for local testing (no need to install to %PROGRAMDATA%)
- `AppContext.BaseDirectory` vs `%PROGRAMDATA%` distinction handles both dotnet tool and direct exe scenarios

### 8. **`tests/ContextBridge.Tests/Embedding/BundledOnnxEmbeddingGeneratorTests.cs`**

**What:** Integration tests that verify the embedding pipeline works correctly.

**Test strategy:**
- Load real model and vocab files
- Embed sample texts
- Verify output shape and properties
- Check that semantically similar texts have high cosine similarity

**Why this matters for your review:**
- Regression gate for model upgrades (semantic similarity thresholds catch quality degradation)
- Validates end-to-end: tokenization → inference → pooling → normalization
- Tests the actual embedding generator, not mocks

---

## How It Works: Data Flow Example

Here's a concrete walkthrough of embedding a single text:

```
Input: "Use PascalCase for class names"
↓
[1] TokenizeBatch()
    - BertTokenizer.EncodeToIds() processes the text
    - Lowercases: "use pascalcase for class names"
    - Splits by WordPiece: ["use", "pascal", "##case", "for", "class", "names"]
    - Maps to token IDs: [3384, 4239, 1041, 2005, 1500, 2313]
    - Adds [CLS] at start, [SEP] at end: [101, 3384, 4239, 1041, 2005, 1500, 2313, 102]
    - Pads to 128: [101, 3384, 4239, 1041, 2005, 1500, 2313, 102, 0, 0, ..., 0]
    ↓ (returns int[])
[2] RunInference()
    - Builds three [1 × 128] tensors:
      - inputIds:      [101, 3384, 4239, ... 0, 0, 0]
      - attentionMask: [1,   1,    1,    ... 0, 0, 0]  (marks non-padding)
      - tokenTypeIds:  [0,   0,    0,    ... 0, 0, 0]
    - Passes to ONNX Runtime
    - Model produces: lastHiddenState [1 × 128 × 384] (each token has 384-dim representation)
    ↓
[3] MeanPoolAndNormalize()
    - Iterates through the 128 tokens
    - For positions marked in attentionMask (the 8 real tokens), sum their 384-dim vectors
    - Average by dividing by 8: sum / 8
    - L2 normalize the 384-dim result to magnitude 1
    ↓
Output: float[384] = [-0.15, 0.42, -0.88, ..., 0.21]
```

The vector is now ready to be stored in SQLite alongside the text, and future searches can compute cosine similarity against this vector.

---

## Batch Example

Embedding three texts at once is more efficient:

```
Input: ["Use PascalCase", "Apply const correctly", "Write clean code"]
↓
[1] TokenizeBatch()
    Returns: List<int[]> with 3 arrays
    - ids[0] = [101, 3384, 4239, 102, 0, 0, ...]
    - ids[1] = [101, 3462, 3469, 102, 0, 0, ...]
    - ids[2] = [101, 3957, 3476, 102, 0, 0, ...]
    ↓
[2] RunInference()
    Builds three [3 × 128] tensors (all three sequences batched)
    Passes to ONNX Runtime (processes all 3 at once)
    Output: [3 × 128 × 384] tensor (3 sequences, 128 tokens each, 384-dim per token)
    ↓
[3] MeanPoolAndNormalize()
    Loops 3 times (once per sequence in batch)
    Returns: float[][] with 3 vectors
    ↓
Output: [
    [-0.15, 0.42, -0.88, ..., 0.21],  // vector for "Use PascalCase"
    [0.33, -0.12, 0.51, ..., -0.41],  // vector for "Apply const correctly"
    [-0.08, 0.55, -0.67, ..., 0.19]   // vector for "Write clean code"
]
```

---

## Key Implementation Details

### Constants
```csharp
private const int MaxSequenceLength = 128;  // BERT max input length
private const int Dimensions = 384;         // Embedding dimension for all-MiniLM-L6-v2
```

### Thread Safety
- `InferenceSession` (ONNX Runtime) is **not reusable across concurrent requests** — each session must be created per request OR protected by locking
- Current implementation: **singleton session**, all inference calls are serialized via `Task.Run()` on a thread pool thread
- This is acceptable for now (v1); if concurrency becomes a bottleneck, consider session pooling in a future phase

### Tensor Layout
- ONNX uses **row-major order**: `[batch, seq, dim]` means element at (b, s, d) is at offset `b * seq_len * 384 + s * 384 + d`
- The code builds flat arrays and passes them as `DenseTensor<long>(data, shape)` — ONNX unpacks them correctly

---

## Testing Phase 2

To verify the embedding pipeline works:

```powershell
# Run embedding-specific tests
dotnet test --filter "Embedding"

# Run all tests (includes embedding regression tests)
dotnet test

# Manual verification: run the service locally
dotnet run --project src/ContextBridge.Service
```

The test suite embeds known text and validates:
1. Output shape is correct (array length = 384)
2. Similar texts have high cosine similarity
3. Dissimilar texts have low cosine similarity

---

## Summary: What You're Reviewing

| Component | Purpose | Key Insight |
|-----------|---------|------------|
| ADR-005, ADR-006 | Decisions on model & tokenizer | Tradeoffs: size vs quality, first-party support, maintenance |
| manifest.json | Model spec (not binary) | Enables reproducible downloads, auditable updates |
| BundledOnnxEmbeddingGenerator | Text → vectors | Core pipeline: tokenize → infer → pool → normalize |
| ModelDownloader | Install-time model acquisition | SHA256 verification, idempotent (already downloaded = skip) |
| Program.cs | DI wiring | Singleton, falls back to dev dir for testing |
| Tests | Regression gate | Validates semantic similarity thresholds |

---

## Merge Readiness Checklist

Before merging Phase 2:
- [ ] Understand why the model is INT8 quantized (size vs quality tradeoff)
- [ ] Understand tokenization (text → token IDs → tensors)
- [ ] Understand ONNX Runtime (compiled model, C# bindings)
- [ ] Understand mean pooling + L2 normalization (why final vectors are unit-length)
- [ ] Understand batch processing (why it's more efficient)
- [ ] Run tests locally: `dotnet test --filter "Embedding"`
- [ ] Verify model downloads: run `dotnet run` locally, check logs
- [ ] Read ADR-005 and ADR-006 — understand the "why" behind each decision

---

## Questions to Ask Yourself

As you review:

1. **Tokenization:** Why does the tokenizer produce token IDs, not strings? (Because ONNX expects integer array inputs, not text.)

2. **Mean pooling:** Why average all token vectors instead of using the [CLS] token alone? (Sentence transformers' pooling strategy provides better semantics for the sentence representation.)

3. **L2 normalization:** Why normalize vectors to unit length? (Cosine similarity of normalized vectors = dot product, which is faster to compute.)

4. **Batch processing:** Why pass a list of texts instead of one at a time? (ONNX Runtime and neural networks optimize for batch operations; batching reduces overhead.)

5. **Download vs. bundle:** Why download the model at install-time instead of bundling it in git? (22 MB in every clone/fork forever; dotnet tool update still needs a solution; HuggingFace is the source of truth.)

6. **Singleton session:** Could the ONNX session be shared across concurrent requests? (It serializes all inference via `Task.Run()` — acceptable for v1, may need pooling if it becomes a bottleneck.)

---

## Next Phase (Phase 3)

Phase 2 produces vectors. Phase 3 will:
- Store vectors in SQLite via sqlite-vec
- Implement vector similarity search
- Wire up the MCP `memory_write` and `memory_search` tools

The embedding pipeline is the foundation that Phase 3 builds on.
