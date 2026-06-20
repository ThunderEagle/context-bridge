# ADR-005: all-MiniLM-L6-v2 INT8 Quantized ONNX Model

**Date:** 2026-06-20  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

Phase 2 requires an embedding model that converts text into dense float vectors for semantic similarity search. The model must satisfy the zero-dependency constraint — it must run entirely in-process via ONNX Runtime without any external server, network call at inference time, or Python/Ollama dependency. It must ship bundled with the executable.

Practical constraints:
- Inference must be fast enough for synchronous use (< 100ms per query on commodity hardware)
- Model binary must be small enough to bundle without making the installer unacceptably large
- 384-dim vectors required to match the sqlite-vec configuration planned for Phase 3
- Model must be available as a pre-exported ONNX file (not requiring a Python export step)

## Decision

We will use the **INT8-quantized ONNX export of `all-MiniLM-L6-v2`** from the Hugging Face Optimum exports at `sentence-transformers/all-MiniLM-L6-v2`.

- **Model file:** `onnx/model_quint8_avx2.onnx` (~22 MB, UINT8 static quantization via Optimum, AVX2-optimized)
- **Dimensions:** 384
- **Vocabulary:** standard BERT `vocab.txt` (30,522 tokens, WordPiece)
- **Max sequence length:** 128 tokens (sentence-transformer default; sufficient for memory snippets)
- **Pooling:** mean pooling over non-padding tokens, then L2 normalization

The model files are committed to the repository under `models/all-MiniLM-L6-v2/`. This is intentional — it ensures reproducible builds and zero download dependency at install time. A `manifest.json` alongside the files records the source URL, SHA256, and metadata.

### Model upgrade procedure

1. Run `scripts/update-model.ps1` with the new source URL
2. Update `models/all-MiniLM-L6-v2/manifest.json` with the new `sha256`
3. Run `dotnet test` — the semantic similarity thresholds in the embedding tests serve as a regression gate
4. Submit a PR with the binary diff; the manifest change makes the upgrade auditable in code review

**Model upgrades are breaking changes.** A new model produces a different embedding space — all vectors stored in SQLite from the previous model become incompatible. Any future model upgrade must be paired with a storage migration strategy (Phase 3+). Making it a PR enforces this discipline.

## Consequences

### Positive
- ~22 MB binary — acceptable for a bundled tool; the FP32 equivalent is ~88 MB
- Negligible quality loss at this scale: INT8 dynamic quantization retains >99% of retrieval quality for sentence-length inputs
- Model is already exported and maintained by the Hugging Face Optimum team — no custom export step required
- Runs entirely on CPU via ONNX Runtime; no GPU dependency
- Inference latency ~5–20ms per batch on a modern CPU — well within tolerance

### Negative
- ~22 MB added to the repository (committed intentionally; documented in CLAUDE.md)
- All vectors stored in SQLite are tied to this model version — upgrading the model requires a storage migration

### Neutral / Trade-offs
- INT8 quantization is dynamic (weights quantized, activations computed at runtime) — simpler than static quantization, no calibration data required, only minor accuracy trade-off

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| FP32 `model.onnx` (~88 MB) | 4× larger with no meaningful quality gain for embedding tasks on short text |
| `model_qint8_avx512.onnx` | Requires AVX512 — less broadly available than AVX2; developer machines without AVX512 would fail |
| `model_qint8_avx512_vnni.onnx` | Even narrower hardware requirement than AVX512 |
| `model_qint8_arm64.onnx` | ARM64 only — primary target is Windows x64 |
| `all-MiniLM-L12-v2` INT8 (~44 MB, 384-dim) | 2× larger, small quality improvement not worth the size trade-off for v1 |
| `bge-small-en-v1.5` (384-dim, ~67 MB) | Higher quality ceiling but larger FP32 only; no Optimum INT8 export readily available |
| Download model at first run | Violates zero-dependency install principle; adds failure mode (network, disk, path) at startup |
| Ollama-hosted model | Requires Ollama running as a separate process — exactly the dependency this project avoids |

## References
- [all-MiniLM-L6-v2 on HuggingFace](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
- [Optimum ONNX exports](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/tree/main/onnx)
- [ONNX Runtime C# docs](https://onnxruntime.ai/docs/get-started/with-csharp.html)
- ADR-006: Tokenizer library choice
- ADR-007: Dapper over EF Core (Phase 3 storage)
