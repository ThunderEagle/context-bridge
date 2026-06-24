namespace ContextBridge.Service.Dashboard;

internal static class DashboardHtml
{
    internal const string Page = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>ContextBridge</title>
          <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

            :root {
              --bg: #0f1117;
              --surface: #1a1d27;
              --border: #2a2d3a;
              --text: #e2e4ed;
              --muted: #7a7f9a;
              --accent: #6c8ef5;
              --accent-dim: #2a3260;
              --tag-bg: #1e2438;
              --tag-text: #8faaf5;
              --red: #f56c6c;
              --green: #6cf58a;
            }

            body {
              font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
              background: var(--bg);
              color: var(--text);
              font-size: 14px;
              line-height: 1.5;
              min-height: 100vh;
            }

            header {
              background: var(--surface);
              border-bottom: 1px solid var(--border);
              padding: 12px 24px;
              display: flex;
              align-items: center;
              gap: 12px;
            }

            header h1 {
              font-size: 16px;
              font-weight: 600;
              letter-spacing: 0.02em;
              color: var(--accent);
            }

            header .subtitle {
              font-size: 12px;
              color: var(--muted);
            }

            header .spacer { flex: 1; }

            header .refresh-control {
              display: flex;
              align-items: center;
              gap: 8px;
              font-size: 12px;
              color: var(--muted);
            }

            header .refresh-control input[type=checkbox] { cursor: pointer; }
            header .refresh-control label { cursor: pointer; }

            .container {
              max-width: 1100px;
              margin: 0 auto;
              padding: 24px 24px;
            }

            /* Stats */
            .stats-grid {
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
              gap: 12px;
              margin-bottom: 24px;
            }

            .stat-card {
              background: var(--surface);
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 16px;
            }

            .stat-card .label {
              font-size: 11px;
              text-transform: uppercase;
              letter-spacing: 0.06em;
              color: var(--muted);
              margin-bottom: 4px;
            }

            .stat-card .value {
              font-size: 28px;
              font-weight: 700;
              color: var(--text);
            }

            .stat-card .value.accent { color: var(--accent); }
            .stat-card .value.green { color: var(--green); }
            .stat-card .value.red { color: var(--red); }

            .stat-card .sub {
              font-size: 11px;
              color: var(--muted);
              margin-top: 2px;
            }

            /* Toolbar */
            .toolbar {
              display: flex;
              gap: 10px;
              margin-bottom: 16px;
              flex-wrap: wrap;
            }

            .toolbar input[type=text],
            .toolbar select {
              background: var(--surface);
              border: 1px solid var(--border);
              border-radius: 6px;
              color: var(--text);
              padding: 7px 12px;
              font-size: 13px;
              outline: none;
            }

            .toolbar input[type=text] { flex: 1; min-width: 200px; }
            .toolbar input[type=text]:focus,
            .toolbar select:focus {
              border-color: var(--accent);
            }

            .toolbar select option { background: var(--surface); }

            .btn {
              background: var(--accent-dim);
              border: 1px solid var(--accent);
              border-radius: 6px;
              color: var(--accent);
              padding: 7px 16px;
              font-size: 13px;
              cursor: pointer;
              white-space: nowrap;
            }

            .btn:hover { background: var(--accent); color: #fff; }

            .btn.secondary {
              background: transparent;
              border-color: var(--border);
              color: var(--muted);
            }

            .btn.secondary:hover {
              border-color: var(--muted);
              color: var(--text);
            }

            /* Mode pill */
            .mode-pill {
              display: inline-block;
              font-size: 11px;
              padding: 2px 8px;
              border-radius: 12px;
              margin-bottom: 12px;
              background: var(--accent-dim);
              color: var(--accent);
              border: 1px solid var(--accent);
            }

            .mode-pill.search { background: #1e3028; color: var(--green); border-color: var(--green); }

            /* Memory list */
            .memory-list { display: flex; flex-direction: column; gap: 8px; }

            .memory-card {
              background: var(--surface);
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 14px 16px;
            }

            .memory-card:hover { border-color: #3a3d50; }

            .memory-meta {
              display: flex;
              align-items: center;
              gap: 12px;
              margin-bottom: 6px;
              flex-wrap: wrap;
            }

            .memory-id {
              font-family: monospace;
              font-size: 11px;
              color: var(--muted);
            }

            .memory-date {
              font-size: 11px;
              color: var(--muted);
            }

            .memory-distance {
              font-size: 11px;
              color: var(--green);
              margin-left: auto;
            }

            .memory-content {
              font-size: 13px;
              color: var(--text);
              line-height: 1.6;
              word-break: break-word;
              margin-bottom: 8px;
            }

            .memory-content.truncated {
              display: -webkit-box;
              -webkit-line-clamp: 3;
              -webkit-box-orient: vertical;
              overflow: hidden;
            }

            .tags { display: flex; flex-wrap: wrap; gap: 5px; }

            .tag {
              background: var(--tag-bg);
              color: var(--tag-text);
              border: 1px solid #2a3260;
              border-radius: 4px;
              font-size: 11px;
              padding: 2px 8px;
              cursor: pointer;
            }

            .tag:hover { border-color: var(--accent); color: var(--accent); }

            /* Pagination */
            .pagination {
              display: flex;
              align-items: center;
              gap: 12px;
              margin-top: 20px;
              justify-content: center;
            }

            .page-info { font-size: 13px; color: var(--muted); }

            /* Empty / error states */
            .empty-state {
              text-align: center;
              padding: 48px 24px;
              color: var(--muted);
              font-size: 13px;
            }

            .error-banner {
              background: #2a1515;
              border: 1px solid #5a2020;
              border-radius: 8px;
              padding: 12px 16px;
              color: var(--red);
              font-size: 13px;
              margin-bottom: 16px;
            }

            .spinner {
              display: inline-block;
              width: 16px;
              height: 16px;
              border: 2px solid var(--border);
              border-top-color: var(--accent);
              border-radius: 50%;
              animation: spin 0.7s linear infinite;
              vertical-align: middle;
              margin-right: 6px;
            }

            @keyframes spin { to { transform: rotate(360deg); } }

            .loading-row {
              text-align: center;
              padding: 32px;
              color: var(--muted);
              font-size: 13px;
            }

            .section-title {
              font-size: 11px;
              text-transform: uppercase;
              letter-spacing: 0.06em;
              color: var(--muted);
              margin: 28px 0 12px;
              padding-bottom: 8px;
              border-bottom: 1px solid var(--border);
            }

            .project-badge {
              display: inline-block;
              background: var(--accent-dim);
              color: var(--accent);
              border: 1px solid var(--accent);
              border-radius: 4px;
              font-size: 11px;
              padding: 2px 8px;
              font-family: monospace;
            }

            .expiry-label {
              font-size: 11px;
              color: var(--muted);
              margin-left: auto;
            }

            .expiry-label.near { color: var(--red); }
          </style>
        </head>
        <body>
          <header>
            <h1>ContextBridge</h1>
            <span class="subtitle" id="model-label">loading…</span>
            <span class="spacer"></span>
            <div class="refresh-control">
              <input type="checkbox" id="auto-refresh" />
              <label for="auto-refresh">Auto-refresh (30s)</label>
            </div>
          </header>

          <div class="container">
            <div class="stats-grid">
              <div class="stat-card">
                <div class="label">Active</div>
                <div class="value green" id="stat-active">—</div>
                <div class="sub">memories</div>
              </div>
              <div class="stat-card">
                <div class="label">Deleted</div>
                <div class="value red" id="stat-deleted">—</div>
                <div class="sub">soft-deleted</div>
              </div>
              <div class="stat-card">
                <div class="label">Total</div>
                <div class="value accent" id="stat-total">—</div>
                <div class="sub">all time</div>
              </div>
              <div class="stat-card">
                <div class="label">Handoffs</div>
                <div class="value accent" id="stat-handoffs">—</div>
                <div class="sub">active sessions</div>
              </div>
            </div>

            <div class="toolbar">
              <input type="text" id="search-input" placeholder="Semantic search…" />
              <select id="tag-filter">
                <option value="">All tags</option>
              </select>
              <button class="btn" id="search-btn">Search</button>
              <button class="btn secondary" id="clear-btn">Clear</button>
            </div>

            <div id="error-area"></div>
            <div id="mode-indicator"></div>
            <div id="memory-list" class="memory-list"></div>
            <div class="pagination" id="pagination" style="display:none">
              <button class="btn secondary" id="prev-btn">← Prev</button>
              <span class="page-info" id="page-info"></span>
              <button class="btn secondary" id="next-btn">Next →</button>
            </div>

            <h2 class="section-title" id="handoff-section-title" style="display:none">Handoffs</h2>
            <div id="handoff-list" class="memory-list"></div>
          </div>

          <script>
            const BASE = window.location.origin;
            let state = { page: 1, pageSize: 20, tag: '', query: '', mode: 'list', totalCount: 0 };
            let autoRefreshTimer = null;

            // ── Stats ─────────────────────────────────────────────────────────
            async function loadStats() {
              try {
                const r = await fetch(`${BASE}/api/dashboard/stats`);
                if (!r.ok) return;
                const d = await r.json();
                document.getElementById('stat-active').textContent = d.activeCount.toLocaleString();
                document.getElementById('stat-deleted').textContent = d.deletedCount.toLocaleString();
                document.getElementById('stat-total').textContent = d.totalCount.toLocaleString();
                document.getElementById('model-label').textContent = d.model;
              } catch (_) { /* stats are best-effort */ }
            }

            // ── Memories ──────────────────────────────────────────────────────
            async function loadMemories() {
              showLoading();
              clearError();

              try {
                const params = new URLSearchParams({
                  page: state.page,
                  pageSize: state.pageSize,
                });
                if (state.tag)   params.set('tag', state.tag);
                if (state.query) params.set('q', state.query);

                const r = await fetch(`${BASE}/api/dashboard/memories?${params}`);
                if (!r.ok) { showError(`Server returned ${r.status}`); return; }

                const d = await r.json();
                state.totalCount = d.totalCount;
                renderMemories(d.items, d.totalCount);
                renderPagination(d.totalCount);
                populateTagsFromItems(d.items);
              } catch (e) {
                showError(`Failed to load memories: ${e.message}`);
              }
            }

            function renderMemories(items, total) {
              const list = document.getElementById('memory-list');
              const modeEl = document.getElementById('mode-indicator');

              if (state.query) {
                modeEl.innerHTML = `<div class="mode-pill search">Semantic search — "${escHtml(state.query)}" — ${total} result${total !== 1 ? 's' : ''}</div>`;
              } else if (state.tag) {
                modeEl.innerHTML = `<div class="mode-pill">Filtered by tag: ${escHtml(state.tag)}</div>`;
              } else {
                modeEl.innerHTML = '';
              }

              if (!items || items.length === 0) {
                list.innerHTML = `<div class="empty-state">No memories found.</div>`;
                return;
              }

              list.innerHTML = items.map(m => {
                const preview = m.content.length > 300
                  ? escHtml(m.content.slice(0, 300)) + '…'
                  : escHtml(m.content);
                const date = new Date(m.createdAt).toLocaleString();
                const tags = (m.tags || []).map(t =>
                  `<span class="tag" data-tag="${escHtml(t)}">${escHtml(t)}</span>`).join('');
                const dist = m.distance != null
                  ? `<span class="memory-distance">dist ${m.distance.toFixed(3)}</span>` : '';
                return `
                  <div class="memory-card">
                    <div class="memory-meta">
                      <span class="memory-id">#${m.id}</span>
                      <span class="memory-date">${date}</span>
                      ${dist}
                    </div>
                    <div class="memory-content">${preview}</div>
                    <div class="tags">${tags}</div>
                  </div>`;
              }).join('');

              // Tag click filters
              list.querySelectorAll('.tag').forEach(el => {
                el.addEventListener('click', () => {
                  const t = el.dataset.tag;
                  state.tag = (state.tag === t) ? '' : t;
                  state.query = '';
                  state.page = 1;
                  state.mode = 'list';
                  document.getElementById('search-input').value = '';
                  syncTagSelect();
                  refresh();
                });
              });
            }

            function renderPagination(total) {
              const totalPages = Math.max(1, Math.ceil(total / state.pageSize));
              const pag = document.getElementById('pagination');
              if (state.mode === 'search' || totalPages <= 1) {
                pag.style.display = 'none';
                return;
              }
              pag.style.display = 'flex';
              document.getElementById('page-info').textContent =
                `Page ${state.page} of ${totalPages} (${total.toLocaleString()} total)`;
              document.getElementById('prev-btn').disabled = state.page <= 1;
              document.getElementById('next-btn').disabled = state.page >= totalPages;
            }

            // ── Tag dropdown ──────────────────────────────────────────────────
            const knownTags = new Set();
            function populateTagsFromItems(items) {
              const sel = document.getElementById('tag-filter');
              let changed = false;
              (items || []).forEach(m => (m.tags || []).forEach(t => {
                if (!knownTags.has(t)) { knownTags.add(t); changed = true; }
              }));
              if (!changed) return;
              const cur = sel.value;
              sel.innerHTML = '<option value="">All tags</option>';
              [...knownTags].sort().forEach(t => {
                const o = document.createElement('option');
                o.value = t; o.textContent = t;
                sel.appendChild(o);
              });
              sel.value = cur;
            }

            function syncTagSelect() {
              document.getElementById('tag-filter').value = state.tag;
            }

            // ── Helpers ───────────────────────────────────────────────────────
            function showLoading() {
              document.getElementById('memory-list').innerHTML =
                `<div class="loading-row"><span class="spinner"></span>Loading…</div>`;
            }

            function showError(msg) {
              document.getElementById('error-area').innerHTML =
                `<div class="error-banner">${escHtml(msg)}</div>`;
            }

            function clearError() {
              document.getElementById('error-area').innerHTML = '';
            }

            function escHtml(s) {
              return String(s)
                .replace(/&/g, '&amp;').replace(/</g, '&lt;')
                .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
            }

            // ── Handoffs ──────────────────────────────────────────────────────
            async function loadHandoffs() {
              try {
                const r = await fetch(`${BASE}/api/dashboard/handoffs`);
                if (!r.ok) return;
                const items = await r.json();
                document.getElementById('stat-handoffs').textContent = items.length.toLocaleString();
                renderHandoffs(items);
              } catch (_) { /* best-effort */ }
            }

            function renderHandoffs(items) {
              const list = document.getElementById('handoff-list');
              const title = document.getElementById('handoff-section-title');
              if (!items || items.length === 0) {
                title.style.display = 'none';
                list.innerHTML = '';
                return;
              }
              title.style.display = '';
              list.innerHTML = items.map(h => {
                const project = h.project
                  ? `<span class="project-badge">${escHtml(h.project)}</span>` : '';
                const created = new Date(h.createdAt).toLocaleString();
                const expiresIn = expiresInLabel(h.expiresAt);
                const near = isNearExpiry(h.expiresAt);
                const preview = h.content.length > 300
                  ? escHtml(h.content.slice(0, 300)) + '…'
                  : escHtml(h.content);
                return `
                  <div class="memory-card">
                    <div class="memory-meta">
                      <span class="memory-id">#${h.id}</span>
                      ${project}
                      <span class="memory-date">${created}</span>
                      <span class="expiry-label${near ? ' near' : ''}">${expiresIn}</span>
                    </div>
                    <div class="memory-content">${preview}</div>
                  </div>`;
              }).join('');
            }

            function expiresInLabel(expiresAt) {
              const diff = new Date(expiresAt) - Date.now();
              if (diff <= 0) return 'expired';
              const days = Math.floor(diff / 86_400_000);
              if (days >= 1) return `expires in ${days}d`;
              const hours = Math.floor(diff / 3_600_000);
              if (hours >= 1) return `expires in ${hours}h`;
              return 'expires soon';
            }

            function isNearExpiry(expiresAt) {
              return (new Date(expiresAt) - Date.now()) < 86_400_000;
            }

            async function refresh() {
              await Promise.all([loadStats(), loadMemories(), loadHandoffs()]);
            }

            // ── Event wiring ──────────────────────────────────────────────────
            document.getElementById('search-btn').addEventListener('click', () => {
              const q = document.getElementById('search-input').value.trim();
              state.query = q;
              state.page = 1;
              state.mode = q ? 'search' : 'list';
              state.tag = q ? '' : state.tag;
              if (q) syncTagSelect();
              refresh();
            });

            document.getElementById('search-input').addEventListener('keydown', e => {
              if (e.key === 'Enter') document.getElementById('search-btn').click();
            });

            document.getElementById('clear-btn').addEventListener('click', () => {
              state.query = ''; state.tag = ''; state.page = 1; state.mode = 'list';
              document.getElementById('search-input').value = '';
              document.getElementById('tag-filter').value = '';
              refresh();
            });

            document.getElementById('tag-filter').addEventListener('change', e => {
              state.tag = e.target.value;
              state.query = '';
              state.page = 1;
              state.mode = 'list';
              document.getElementById('search-input').value = '';
              refresh();
            });

            document.getElementById('prev-btn').addEventListener('click', () => {
              if (state.page > 1) { state.page--; refresh(); }
            });

            document.getElementById('next-btn').addEventListener('click', () => {
              const totalPages = Math.ceil(state.totalCount / state.pageSize);
              if (state.page < totalPages) { state.page++; refresh(); }
            });

            document.getElementById('auto-refresh').addEventListener('change', e => {
              if (e.target.checked) {
                autoRefreshTimer = setInterval(refresh, 30_000);
              } else {
                clearInterval(autoRefreshTimer);
                autoRefreshTimer = null;
              }
            });

            // ── Init ──────────────────────────────────────────────────────────
            refresh();
          </script>
        </body>
        </html>
        """;
}
