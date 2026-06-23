# Server Instructions Override

## Purpose

Allow power users to replace the built-in MCP `ServerInstructions` string with a local file, enabling prompt tuning (custom tags, additional guidance, project-specific conventions) without a rebuild or republish cycle.

---

## Design

**Option B — optional override file, path configured in `appsettings.json`.**

```json
"ContextBridge": {
  "ServerInstructionsPath": "" // empty or absent = use built-in default
}
```

At startup, the MCP server reads `ServerInstructionsPath`. If the path is non-empty and the file exists, its content replaces the built-in string. If the path is empty, absent, or the file is missing, the hardcoded default is used without error.

The override file is plain text (`.md` or `.txt`). It lives outside the install directory — typically the same data directory used for the SQLite database — so it survives upgrades.

---

## CLI Command

```
context-bridge instructions init [--path <file>]
```

- Writes the current built-in instructions to the target file as a starting point
- Sets `ServerInstructionsPath` in `appsettings.json` to the resolved path
- If `--path` is omitted, defaults to `<data-dir>/server-instructions.md`
- If the file already exists, prompts before overwriting (or `--force` to skip)
- Prints the path on success so the user knows where to open it

```
context-bridge instructions path
```

- Prints the currently configured path (or "built-in default" if none is set)

---

## Failure Modes

| Condition | Behavior |
|---|---|
| `ServerInstructionsPath` empty / absent | Silent fallback to built-in |
| Path configured but file missing | Log a warning at startup; fall back to built-in |
| File exists but is empty | Log a warning at startup; fall back to built-in |
| File unreadable (permissions) | Log an error at startup; fall back to built-in |

All failure cases degrade gracefully — the server starts normally. No condition should prevent the service from starting.

---

## Out of Scope

- Validation or linting of instruction content
- Hot-reload without service restart
- Multiple instruction profiles or per-client overrides
- Any UI for editing the file (users open it in their own editor)
