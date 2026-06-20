# ADR-006: Microsoft.ML.Tokenizers for BERT Tokenization

**Date:** 2026-06-20  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

`all-MiniLM-L6-v2` uses the standard BERT tokenizer (WordPiece algorithm, lowercase, 30,522-token vocabulary). The `BundledOnnxEmbeddingGenerator` must tokenize text before passing it to ONNX Runtime — converting raw strings to `input_ids`, `attention_mask`, and `token_type_ids` tensors.

Required capabilities:
- WordPiece tokenization matching the BERT training vocabulary (`vocab.txt`)
- Lowercase normalization before tokenization (the model was trained on lowercased text)
- Special token insertion (`[CLS]` at position 0, `[SEP]` at the end)
- Truncation to 128 tokens
- Manual padding to a fixed length for batch inference

The tokenizer is a performance-sensitive component — it runs synchronously on every embedding request. It must be thread-safe for concurrent service use.

## Decision

We will use **`Microsoft.ML.Tokenizers`** (NuGet package `Microsoft.ML.Tokenizers`) and its `BertTokenizer` class.

The tokenizer is constructed from the bundled `vocab.txt` file at service startup and kept as a singleton. Truncation and padding are handled in `BundledOnnxEmbeddingGenerator` using the encoded ID arrays returned by the tokenizer.

## Consequences

### Positive
- Microsoft-maintained package, actively developed alongside `Microsoft.ML.OnnxRuntime` — same ecosystem
- `BertTokenizer` implements the full WordPiece pipeline including CLS/SEP token insertion and lowercase normalization
- Thread-safe by design — safe as a singleton in the DI container
- No native binary dependency (pure managed code) — no RID-specific NuGet assets to manage

### Negative
- Package versioning is independent of `Microsoft.ML.OnnxRuntime` — must track both

### Neutral / Trade-offs
- The `BertTokenizer` API requires manual padding after encoding; padding is not built into `EncodeToIds` for batch use — this is handled in `BundledOnnxEmbeddingGenerator`

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| `BERTTokenizers` NuGet | Unmaintained (last updated 2021), no active development, limited WordPiece support |
| `FastBertTokenizer` | Community port; less aligned with the Microsoft ML ecosystem; no CLS/SEP handling built in |
| Custom WordPiece implementation | Significant implementation surface, risk of subtle divergence from the training tokenizer — not worth it when a first-party implementation exists |
| `SharpToken` / tiktoken port | GPT tokenizer (BPE), not WordPiece — incompatible with BERT vocabulary |

## References
- [Microsoft.ML.Tokenizers NuGet](https://www.nuget.org/packages/Microsoft.ML.Tokenizers)
- [Microsoft.ML.Tokenizers source (dotnet/machinelearning)](https://github.com/dotnet/machinelearning/tree/main/src/Microsoft.ML.Tokenizers)
- ADR-005: all-MiniLM-L6-v2 INT8 ONNX model
