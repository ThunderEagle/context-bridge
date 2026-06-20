# ADR-001: .NET 10 Worker Service as the Host Technology

**Date:** 2026-06-20  
**Status:** Accepted  
**Deciders:** Scott Williams

---

## Context

ContextBridge needs to run as a persistent background process on Windows (and eventually macOS and Linux) that:

1. Loads and holds an ONNX embedding model in memory across sessions
2. Serves multiple concurrent MCP clients (Claude Code, Claude Desktop, etc.) over HTTP
3. Installs cleanly without requiring external runtimes on the user's machine
4. Has a credible cross-platform story for v3+

The choice of language and runtime determines the packaging story, the ONNX integration path, the Windows Service integration quality, and the long-term maintainability of the codebase.

Three candidates were evaluated: .NET, Python, and Node/TypeScript.

## Decision

We will use **.NET 10 Worker Service** as the application host.

The service host uses `Microsoft.Extensions.Hosting` with `UseWindowsService()` on Windows and `UseSystemd()` on Linux. The embedding model runs in-process via `Microsoft.ML.OnnxRuntime`. HTTP transport is served via Kestrel.

Distribution is via `dotnet tool install -g ThunderEagle.ContextBridge` (requires .NET SDK) or a self-contained win-x64 `.exe` published with `dotnet publish -r win-x64 --self-contained` (no SDK required on target machine).

## Consequences

### Positive
- `UseWindowsService()` is first-party, production-grade Windows Service integration — no third-party wrappers
- `Microsoft.ML.OnnxRuntime` is Microsoft-maintained; ships native win-x64/osx-arm64/linux-x64 binaries via NuGet RID system, no separate install
- `dotnet publish --self-contained` bundles the runtime into the executable — the .exe download path requires nothing on the target machine
- `Microsoft.Extensions.AI` provides a clean `IEmbeddingGenerator<string, Embedding<float>>` abstraction with built-in Ollama and OpenAI-compatible implementations for v2+ provider configurability
- `Microsoft.AspNetCore.DataProtection` is the correct cross-platform abstraction over DPAPI/Keychain/key ring
- The developer (Scott Williams) has deep C# / .NET expertise — no ramp-up cost on the language itself

### Negative
- .NET self-contained binaries are larger than a statically compiled Go binary (~60–100 MB vs. ~10 MB)
- The `dotnet tool` path requires the .NET SDK on the target machine; this is acceptable for the developer audience of v1 but limits the non-developer user story
- C# / .NET is less common in open-source developer tooling than Node or Go, which may reduce external contribution velocity

### Neutral / Trade-offs
- The `TargetFramework` will need to be bumped as .NET versions are released; `global.json` with `rollForward: "latestMinor"` manages this

## Alternatives Considered

| Option | Reason Rejected |
|---|---|
| **Python** | No credible zero-dependency distribution story. Inevitably requires Docker, a venv, or a Python runtime on the target machine. Existing Python tools in this space (MihaiBuilds/memory-vault) confirm this — they require Docker Desktop + PostgreSQL. Python is the correct choice when doing fine-tuning or HuggingFace-native ML work; running a pre-trained ONNX model does not qualify. |
| **Node / TypeScript** | Weak Windows Service integration story (no first-party equivalent of `UseWindowsService`). Self-contained binary distribution via `pkg` is less reliable than `dotnet publish`. Native binary handling for sqlite-vec and ONNX Runtime is more fragile. `fusae/Memory-Vault` is the natural endpoint of this stack: functional, but requires Ollama as a separate server process. |
| **Go** | Better than Node for distribution (static binary), but the ONNX ecosystem for Go is significantly weaker than for .NET or Python. No equivalent of `Microsoft.ML.OnnxRuntime`'s first-party support. Also weaker Windows Service story than .NET. Go is a better fit for cloud-native/containerized CLI tools than for this Windows-first service scenario. |

## References

- [Microsoft.ML.OnnxRuntime — Getting Started with C#](https://onnxruntime.ai/docs/get-started/with-csharp.html)
- [Microsoft.Extensions.Hosting.WindowsServices](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service)
- [Microsoft.Extensions.AI announcement](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai-preview/)
- [docs/DESIGN.md — Language / Runtime section](../DESIGN.md#language--runtime)
- [MihaiBuilds/memory-vault](https://github.com/MihaiBuilds/memory-vault) — Python + Docker example of the friction problem
- [fusae/Memory-Vault](https://github.com/fusae/Memory-Vault) — Node + Ollama example
