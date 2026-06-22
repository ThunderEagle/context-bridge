# ContextBridge v1 Testing Guide

Pre-release validation guide. Complete all phases before Phase 6 (distribution/release). Phases 1–7 are scripted; Phase 8 is multi-day organic use.

---

## Pre-flight

```powershell
dotnet build          # must be zero warnings — TreatWarningsAsErrors is on
dotnet test           # all tests pass
ls "$env:PROGRAMDATA\ContextBridge\models\"   # ONNX model file present
```

---

## One-Time Setup: Publish + PATH

The binary is named `context-bridge.exe` (set via `<AssemblyName>` in the csproj). Publish to a stable directory and add it to user PATH so all commands below work from any shell.

```powershell
$installDir = "$env:LOCALAPPDATA\Programs\ContextBridge"
dotnet publish src/ContextBridge.Service -r win-x64 -c Release --self-contained -o $installDir

# Add to user PATH permanently (no admin required)
$current = [Environment]::GetEnvironmentVariable("Path", "User")
if ($current -notlike "*$installDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$current;$installDir", "User")
}

# Reload PATH in current session
$env:PATH += ";$installDir"

# Verify
context-bridge --help
```

Re-run the publish step after any code changes. The `service install` registration points to this directory.

---

## Phase 1 — Service Lifecycle

**Goal:** install, start, stop, status round-trip works correctly.

**Foreground mode** (no Windows Service registration, useful during development):

```powershell
dotnet run --project src/ContextBridge.Service
# Model warms up (~2s), then Kestrel binds to 127.0.0.1:5290
Invoke-RestMethod http://127.0.0.1:5290/health   # expect: {status:"ok"}
```

**Windows Service install** (requires admin shell for SCM registration):

```powershell
context-bridge service install
context-bridge service start
context-bridge service status

Invoke-RestMethod http://127.0.0.1:5290/health     # {status:"ok"}
Invoke-RestMethod http://127.0.0.1:5290/mcp        # expect 401 Unauthorized
```

**Stop/start durability:**

```powershell
context-bridge service stop
context-bridge service start
# After model warm-up (~5s):
Invoke-RestMethod http://127.0.0.1:5290/health     # must return ok
```

**Checks:**
- [ ] Health endpoint responds after service start
- [ ] `/mcp` returns 401 without an Authorization header
- [ ] Service recovers cleanly after stop/start
- [ ] Windows Event Log shows no errors: `Get-EventLog -LogName Application -Source ContextBridge -Newest 10`

---

## Phase 2 — Configure Command

**Goal:** both Claude Code and Claude Desktop receive the correct MCP configuration.

```powershell
context-bridge configure
```

**Verify Claude Code** (`~/.claude/settings.json`):
- Contains `mcpServers.context-bridge` with `type: http`
- URL is `http://127.0.0.1:5290/` (or configured port)
- Authorization header is present with `Bearer <token>`

**Verify Claude Desktop** (`%APPDATA%\Claude\claude_desktop_config.json`):
- Same MCP entry structure

**Verify CLAUDE.md injection** (`~/.claude/CLAUDE.md`):
- ContextBridge instructions block is present with tool descriptions and tag conventions

**Verify in-client:**
- Open Claude Code → ask Claude to call `memory_status` → it should return service health and counts
- Open Claude Desktop → same check

**Checks:**
- [ ] Claude Code settings updated
- [ ] Claude Desktop config updated
- [ ] CLAUDE.md injection present
- [ ] `memory_status` callable from Claude Code
- [ ] `memory_status` callable from Claude Desktop

---

## Phase 3 — MCP Tool Coverage

**Goal:** all 7 MCP tools function correctly via Claude Code. Run these as natural-language requests to Claude.

Run in order — each step builds state used by the next.

| Step | Request to Claude | Expected Result |
|------|-------------------|-----------------|
| 1 | `memory_write` with content `"test memory alpha"` and tags `["project:context-bridge", "type:test"]` | Returns an `id` — note it |
| 2 | `memory_search` for `"alpha test memory"` | Step 1's `id` appears in top 3 results |
| 3 | `memory_list` with default pagination, then with `pageSize: 5` | Step 1's `id` present; pagination returns correct subset |
| 4 | `memory_update` on step 1's `id` with content `"updated content beta"` | Returns `{updated: true}`; searching `"beta"` now returns it |
| 5 | `memory_delete` on step 1's `id` | Returns `{deleted: true}`; list and search no longer return it |
| 6 | `memory_batch_write` with 3 entries | Response contains 3 IDs; `memory_list` shows all 3 |
| 7 | `memory_status` | Active count matches live records; deleted count ≥ 1 |

Repeat steps 1, 2, 6, 7 via **Claude Desktop** to confirm both clients work.

**Checks:**
- [ ] All 7 tools return expected results via Claude Code
- [ ] Steps 1+2 via Claude Desktop also succeed

---

## Phase 4 — Concurrent Client Testing

**Goal:** shared memory state is immediately visible across simultaneous connections.

1. Open Claude Code **and** Claude Desktop at the same time (both connected to the running service)
2. From Claude Code: `memory_write` with content `"written from claude code"`
3. From Claude Desktop: `memory_search` for `"claude code"` → the memory from step 2 appears
4. From Claude Desktop: `memory_write` with content `"written from claude desktop"`
5. From Claude Code: `memory_search` for `"claude desktop"` → the memory from step 4 appears
6. Call `memory_status` from both clients → counts agree

**Checks:**
- [ ] Memory written from Claude Code is immediately searchable from Claude Desktop
- [ ] Memory written from Claude Desktop is immediately searchable from Claude Code
- [ ] Status counts are consistent across both clients

---

## Phase 5 — Data Durability

**Goal:** SQLite data and vector embeddings persist across service restarts.

1. Write 5 memories via MCP, note their IDs and some distinctive content from each
2. Stop and restart the service:

```powershell
context-bridge service stop
context-bridge service start
```

3. After warm-up, verify via Claude or direct HTTP:
   - `memory_list` returns all 5 IDs
   - `memory_search` for content from one of the memories returns it ranked at the top
   - `memory_status` active count matches

**Checks:**
- [ ] All written memories survive a service restart
- [ ] Semantic search still works after restart (embeddings persisted in vec table)
- [ ] Status counts are correct after restart

---

## Phase 6 — Dashboard

**Goal:** the read-only web dashboard accurately reflects the database state.

Navigate to `http://127.0.0.1:5290/dashboard` in a browser. No login required.

| Check | What to Verify |
|-------|----------------|
| Stats card | Active / deleted / total counts match `memory_status` MCP output |
| Memory list | Correct records shown with IDs, tags, and created dates |
| Search bar | Enter a natural language query → results match `memory_search` MCP output |
| Tag filter | Select a tag → list narrows to matching memories; clicking a tag chip in a memory card also filters |
| Pagination | Previous/Next work; page indicator is correct |
| Auto-refresh | Enable the 30s toggle; write a new memory via MCP → it appears without manual refresh |

**Checks:**
- [ ] Dashboard loads without errors
- [ ] Stats match MCP `memory_status`
- [ ] Search returns semantically relevant results
- [ ] Tag filtering works (dropdown + chip clicks)
- [ ] Pagination works
- [ ] Auto-refresh shows new writes

---

## Phase 7 — Edge Cases

| Scenario | How to Test | Expected |
|----------|-------------|----------|
| Unicode + emoji in content | `memory_write` with `"日本語テスト 🧠 edge case"` | Stored, searchable, rendered in dashboard |
| Content > 5000 chars | `memory_write` with a large block of text | Stored without error; dashboard shows truncated preview |
| Empty search query | `memory_search` with `query: ""` | Graceful error or empty results — no crash |
| Nonsense search | `memory_search` with `query: "xyzzy frobnicator"` | 0 results, no error |
| Batch write — 10 entries | `memory_batch_write` with 10 items | All 10 IDs returned; status active count increases by 10 |
| Delete non-existent ID | `memory_delete` with `id: 99999` | `{deleted: false}` — no error or exception |
| Update deleted memory | `memory_update` on a previously deleted ID | `{updated: false}` or descriptive error |
| Service restart mid-session | Stop service while a client is connected, then restart | Client reconnects cleanly on next call |

**Checks:**
- [ ] All edge cases handled without crashes or unhandled exceptions
- [ ] Event Log clean after edge case testing

---

## Phase 8 — Extended Use (Multi-day)

No scripted steps. Use Claude Code normally for real development work. At the end of each session:

1. **Recall quality:** ask Claude to `memory_search` for something from that session — judge whether the right memories surface
2. **Tag hygiene:** browse the dashboard tag filter — are tags sensible and consistently applied?
3. **Count growth:** check stats card for steady accumulation without unexpected deletions
4. **Event log:** `Get-EventLog -LogName Application -Source ContextBridge -Newest 20` — should be clean
5. **Process stability:** `Get-Process context-bridge | Select-Object WorkingSet64` — memory usage should be stable across days (no leak)

Capture any UX issues, recall failures, or unexpected behavior as GitHub Issues before starting Phase 6.

---

## Quick Reference

```powershell
# Health check
Invoke-RestMethod http://127.0.0.1:5290/health

# Authenticated call (get token first)
$token = context-bridge token show
Invoke-RestMethod http://127.0.0.1:5290/mcp -Headers @{ Authorization = "Bearer $token" }

# Service control
context-bridge service status
context-bridge service stop
context-bridge service start

# Event log
Get-EventLog -LogName Application -Source ContextBridge -Newest 20

# Process memory
Get-Process context-bridge | Select-Object WorkingSet64, CPU

# SQLite quick count (without service)
# Open: $env:PROGRAMDATA\ContextBridge\memories.db in any SQLite browser
```
