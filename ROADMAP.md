# Roadmap

## Thoughts

Unfiltered captures — 1-2 sentences each. Use `/idea <text>` to add.

- ~~Import from native memory~~ — Resolved via documentation. Claude Code can read its own `MEMORY.md` files and call `memory_batch_write` without any server-side feature; covered in README under "Importing Existing Context."
- Tag-aware search weighting strategy: unclear whether hard pre-filter (only tagged memories), soft boost (tagged ranked higher), or LLM-inside-MCP (interprets task context to decide which tags matter per query) is the right model. The LLM path adds latency/cost per search; prototype once enough tagging data exists.
- Memory consolidation / deduplication — as memory accumulates, no strategy defined yet for merging near-duplicate entries. May not be necessary if agents write cleanly; needs data before designing.
- `tag_suggest` tool (conditional) — server analyzes content and returns suggested tags for agent confirmation. Adds a round-trip on every write; add only if evidence shows agent tag compliance is insufficient in practice.
- Named pipe / Unix socket transport — would tighten the security perimeter beyond localhost-bind-only. Contingent on MCP client ecosystem adding support; revisit when clients adopt it.

---

## Features

Named backlog items with clear scope, not yet designed. Use `/idea --feature <name> — <description>` to add.

| Name | Description | Target | Added |
|------|-------------|--------|-------|
| Tag-aware search | Add tag filter/boost to `memory_search` — v1 stores tags but search ignores them. Decide weighting model (see Thoughts) before implementing. | future | 2026-06-23 |
| Web dashboard | Read-only local web UI for browsing, auditing, and deleting memories. ADR-013 captures the read-only rationale. | future | 2026-06-23 |
| Memory spaces | Project scoping as a first-class concept beyond tag conventions — explicit namespacing or isolation at the storage layer. | future | 2026-06-23 |
| Cross-platform: macOS | macOS distribution via launchd plist as install artifact. `builder.UseWindowsService()` guard is already in place; add `builder.UseSystemd()` branch and launchd plist generation. | future | 2026-06-23 |
| Cross-platform: Linux | Linux distribution via systemd. Same host-layer abstraction as macOS; systemd unit file generation via CLI. | future | 2026-06-23 |
| .msi installer | Windows installer with code signing for zero-friction install + service registration. Code signing cert ~$300+/year — defer until v1 userbase justifies cost. | future | 2026-06-23 |
| Encrypted cloud sync | Optional sync of `memories.db` to a user-supplied cloud store (S3, OneDrive, etc.) for cross-machine availability. | future | 2026-06-23 |

---

## Not Planned

**Configurable embedding provider** — Allow Ollama and OpenAI-compatible endpoints as alternatives to the bundled ONNX model. Rationale: core value is zero-dependency, self-contained; external providers violate this philosophy, add network dependencies (startup fragility, service availability, latency), and require secret management. The bundled all-MiniLM-L6-v2 model is sufficient for general coding contexts. Revisit only if a concrete use case emerges (e.g., specialized domain embeddings) that warrants the complexity trade-off.

**Client `configure` auto-detection for all editors beyond Claude Code.** Rationale: MCP is modular—users can wire ContextBridge into any MCP-compatible client themselves. Maintaining auto-config parity for every editor's config schema is low-value churn. Revisit if explicit demand emerges.

| Name | Description | Added |
|------|-------------|-------|
| configure: Cursor | Add Cursor to `configure` auto-detection. Config path: `~/.cursor/mcp.json`; rules injection: `~/.cursor/rules/` global rules directory. | 2026-06-23 |
| configure: Devin Desktop | Add Devin Desktop (formerly Windsurf, acquired by Cognition June 2026) to `configure`. Config path appears to be `~/.codeium/windsurf/mcp_config.json` but must be verified before implementing — rebrand may have changed it. | 2026-06-23 |
| configure: Continue.dev | Add Continue.dev to `configure`. Config path: `~/.continue/config.json`; instructions via `systemMessage` field. | 2026-06-23 |
| configure: OpenCode | Add OpenCode to `configure`. Config path: `~/.config/opencode/opencode.json`; uses `mcp` key with `type: "remote"` (not `mcpServers` schema); instructions only. | 2026-06-23 |
| configure: Zed | Add Zed to `configure`. Config path: `%APPDATA%\Zed\settings.json` (Windows) / `~/.config/zed/settings.json`; uses `context_servers` key (not `mcpServers`); instructions only. | 2026-06-23 |
| configure: GitHub Copilot | Add GitHub Copilot to `configure`. Verify whether VS Code native MCP path (`mcp.servers`) covers Copilot or if `github.copilot.chat.codeGeneration.instructions` array is the right injection point before implementing. | 2026-06-23 |
| configure: OpenAI Codex | Add OpenAI Codex to `configure`. Config path: `~/.codex/config.toml`; instructions via `AGENTS.md` in home dir; shared between CLI and VS Code extension. | 2026-06-23 |
| configure: Google Antigravity | Add Google Antigravity to `configure`. Config path: `~/.gemini/config/mcp_config.json`; rules injection path TBD — verify before implementing. | 2026-06-23 |

---

## Designed Features

Features with a full design doc and a target version. Use `/idea --design <name> — <description>` to add.

| Name | Design Doc | Target Version |
|------|-----------|----------------|
| Handoff | [docs/roadmap/handoff.md](docs/roadmap/handoff.md) | — |
| Server Instructions Override | [docs/roadmap/server-instructions-override.md](docs/roadmap/server-instructions-override.md) | — |
