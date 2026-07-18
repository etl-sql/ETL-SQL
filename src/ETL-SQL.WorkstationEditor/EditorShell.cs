using System.Net;

namespace ETL_SQL.WorkstationEditor;

internal static class EditorShell
{
    public static string Html(WorkstationEditorOptions options, WorkstationWorkspace workspace)
    {
        var initialFile = WebUtility.HtmlEncode(workspace.InitialRelativeFile(options.InitialFile) ?? string.Empty);
        var readonlyAttr = workspace.ReadOnly ? "true" : "false";

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>ETL-SQL Local Script Editor</title>
  <link rel="stylesheet" href="/designer/designer.css">
  <style>
    :root {
      color-scheme: dark;
      --portal-bg: #101317;
      --portal-bg-soft: #161b22;
      --portal-surface: #0f141b;
      --portal-surface-subtle: #161b22;
      --portal-text: #e6edf3;
      --portal-text-soft: #c9d1d9;
      --portal-text-muted: #9da7b1;
      --portal-muted: #9da7b1;
      --portal-border: #30363d;
      --portal-border-soft: #252c35;
      --portal-accent: #58a6ff;
      --portal-accent-soft: rgba(88, 166, 255, 0.16);
      --portal-danger: #ff7b72;
      --portal-warning: #d29922;
      --portal-success: #3fb950;
      --portal-font: Segoe UI, Arial, sans-serif;
      --portal-font-mono: Cascadia Code, Consolas, monospace;
    }
    body { margin: 0; font-family: Segoe UI, Arial, sans-serif; background: #101317; color: #e6edf3; }
    .app { display: grid; grid-template-columns: 260px 1fr; height: 100vh; }
    aside { border-right: 1px solid #30363d; background: #161b22; padding: 12px; overflow: auto; }
    main { display: grid; grid-template-rows: auto minmax(0, 2fr) minmax(180px, 1fr) auto; min-width: 0; }
    header { display: flex; gap: 8px; align-items: center; padding: 10px 12px; border-bottom: 1px solid #30363d; }
    button { border: 1px solid #3f4752; background: #21262d; color: #e6edf3; padding: 6px 10px; border-radius: 6px; cursor: pointer; }
    button:disabled { opacity: .45; cursor: default; }
    .file { display: block; width: 100%; text-align: left; margin: 2px 0; overflow-wrap: anywhere; }
    .file.active { border-color: #58a6ff; color: #fff; }
    #editor { min-height: 0; background: #0f141b; }
    #editor .cm-editor { background: #0f141b; color: #e6edf3; }
    #editor .cm-scroller, #editor .cm-content { background: #0f141b; color: #e6edf3; caret-color: #e6edf3; }
    #editor .cm-line { color: #e6edf3; }
    #editor .cm-line span { color: #e6edf3 !important; }
    #editor .cm-matchingBracket, #editor .cm-nonmatchingBracket { color: #f0f6fc !important; background: rgba(88, 166, 255, 0.22); }
    #editor .cm-gutters { background: #161b22; color: #8b949e; border-right-color: #30363d; }
    #editor .cm-activeLine, #editor .cm-activeLineGutter { background: rgba(88, 166, 255, 0.12); }
    #editor .cm-selectionBackground, #editor .cm-content ::selection { background: rgba(88, 166, 255, 0.35) !important; }
    #editor .cm-cursor { border-left-color: #e6edf3; }
    #editor .etlsql-editor-container.has-diagnostics .cm-editor { height: calc(100% - 92px); }
    #editor .etlsql-editor-diagnostics { height: 92px; }
    #editor .etlsql-editor-diagnostics-list { height: 64px; }
    #editor .etlsql-editor-diagnostics { background: #161b22; border-top-color: #30363d; color: #e6edf3; }
    #editor .etlsql-editor-diagnostic { color: #e6edf3; border-bottom-color: #252c35; }
    .cm-tooltip, .cm-tooltip-autocomplete, .cm-completionInfo {
      background: #161b22 !important;
      color: #e6edf3 !important;
      border-color: #30363d !important;
      box-shadow: 0 16px 36px rgba(0, 0, 0, .42) !important;
    }
    .cm-tooltip-autocomplete ul li { color: #e6edf3 !important; }
    .cm-tooltip-autocomplete ul li[aria-selected] { background: #2f6feb !important; color: #fff !important; }
    .cm-completionLabel, .cm-completionDetail, .cm-completionInfo * { color: inherit !important; }
    .cm-completionIcon { opacity: .85; }
    #results { min-height: 0; background: #101317; overflow: hidden; font-size: 13px; }
    #results .etlsql-script-results { background: #101317; border-top-color: #30363d; color: #e6edf3; }
    #results .etlsql-script-results-tabs { background: #161b22; border-bottom-color: #30363d; }
    #results .etlsql-script-results-tabs button,
    #results .etlsql-script-results-tools button { color: #c9d1d9; background: transparent; border-color: transparent; }
    #results .etlsql-script-results-tabs button.active,
    #results .etlsql-script-results-tools button { background: #21262d; border-color: #3f4752; color: #e6edf3; }
    #results .etlsql-script-results-tools input { background: #0f141b; border-color: #3f4752; color: #e6edf3; }
    #results .etlsql-script-results th { color: #c9d1d9; background: #161b22; border-color: #252c35; }
    #results .etlsql-script-results td { border-color: #252c35; }
    #results .etlsql-script-results-count,
    #results .etlsql-script-results-empty,
    #results .etlsql-script-results-status,
    #results .etlsql-script-message span { color: #9da7b1; }
    #results .etlsql-script-message { border-bottom-color: #252c35; }
    #results .message { color: #9da7b1; margin-bottom: 8px; }
    #results .error { color: #ff7b72; white-space: pre-wrap; }
    #status { min-height: 24px; padding: 8px 12px; border-top: 1px solid #30363d; color: #9da7b1; font-size: 13px; }
    .brand { font-weight: 650; margin-bottom: 10px; }
    .path { color: #9da7b1; font-size: 12px; overflow-wrap: anywhere; margin-bottom: 10px; }
  </style>
</head>
<body>
  <div class="app">
    <aside>
      <div class="brand">ETL-SQL Editor</div>
      <div class="path" id="workspaceRoot"></div>
      <div id="files"></div>
    </aside>
    <main>
      <header>
        <button id="newFile" type="button">New</button>
        <button id="save" type="button">Save</button>
        <button id="analyze" type="button">Analyze</button>
        <button id="suggest" type="button">Suggest</button>
        <button id="format" type="button">Format</button>
        <button id="run" type="button">Run</button>
        <span id="activeFile"></span>
      </header>
      <div id="editor"></div>
      <div id="results"><div class="message">Run a statement or selection to see results here.</div></div>
      <div id="status"></div>
    </main>
  </div>
  <script type="module">
    import { createScriptEditor, createScriptResultsPanel } from '/designer/designer.js';
    const token = new URLSearchParams(location.search).get('token') || '';
    const initialFile = '{{initialFile}}';
    const readOnly = {{readonlyAttr}};
    const headers = { 'X-ETLSQL-EDITOR-TOKEN': token };
    let editor;
    let resultsPanel;
    let currentPath = '';
    const newScriptText = '\n'.repeat(9);

    const status = text => document.getElementById('status').textContent = text;
    const escapeHtml = value => String(value ?? '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
    const api = async (url, opts = {}) => {
      const res = await fetch(url, { ...opts, headers: { ...headers, ...(opts.headers || {}) } });
      if (!res.ok) throw new Error(await res.text());
      return res.json();
    };
    const authFetch = (url, opts = {}) =>
      fetch(url, { ...opts, headers: { ...headers, ...(opts.headers || {}) } });

    function renderRunResult(result) {
      if (!result.success) {
        const diagnostics = result.diagnostics || [];
        resultsPanel.replay([
          { type: 'clear', resetHistory: true },
          { type: 'status', status: 'failed' },
          { type: 'message', level: 'error', text: result.message || diagnostics[0]?.message || 'Run failed.' },
          ...diagnostics.map(d => ({ type: 'message', level: 'error', text: d.message || String(d) })),
          { type: 'done', exitCode: 1 }
        ]);
        return;
      }

      const rows = result.rows || [];
      const columns = result.columns || [];
      resultsPanel.replay([
        { type: 'clear', resetHistory: true },
        { type: 'status', status: 'complete' },
        { type: 'message', level: 'info', text: result.message || 'Run completed.' },
        { type: 'results', columns, rows },
        { type: 'performance', metrics: { executionMs: result.elapsedMs || 0, rowsProcessed: rows.length, memoryMb: 0, statements: [{ type: 'Run', totalMs: result.elapsedMs || 0 }] } },
        { type: 'done', exitCode: 0 }
      ]);
    }

    async function openFile(path) {
      const file = await api('/api/files?path=' + encodeURIComponent(path));
      currentPath = path;
      document.getElementById('activeFile').textContent = path;
      document.querySelectorAll('.file').forEach(b => b.classList.toggle('active', b.dataset.path === path));
      editor.setValue(file.content || '');
      status('Opened ' + path);
    }

    async function refreshFiles(activePath = currentPath) {
      const workspace = await api('/api/workspace');
      const files = document.getElementById('files');
      document.getElementById('workspaceRoot').textContent = workspace.root;
      files.textContent = '';
      for (const file of workspace.files) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'file';
        button.dataset.path = file.path;
        button.textContent = file.path;
        button.addEventListener('click', () => openFile(file.path));
        files.appendChild(button);
      }
      document.querySelectorAll('.file').forEach(b => b.classList.toggle('active', b.dataset.path === activePath));
      return workspace;
    }

    function newScript() {
      currentPath = '';
      document.getElementById('activeFile').textContent = 'Untitled';
      document.querySelectorAll('.file').forEach(b => b.classList.remove('active'));
      editor.setValue(newScriptText);
      resultsPanel.clear();
      status('New script');
    }

    async function boot() {
      resultsPanel = createScriptResultsPanel(document.getElementById('results'));
      editor = await createScriptEditor(document.getElementById('editor'), {
        value: newScriptText,
        readOnly,
        analyzeUrl: '/api/analyze',
        completeUrl: '/api/complete',
        hoverUrl: '/api/hover',
        authFetch,
        documentUri: () => currentPath || 'untitled.etlsql',
        onDiagnostics: diagnostics => status((diagnostics || []).length + ' diagnostic(s)')
      });
      const workspace = await refreshFiles();
      document.getElementById('save').disabled = readOnly;
      const first = initialFile || workspace.initialFile || workspace.files[0]?.path;
      if (first) await openFile(first);
      else newScript();
    }

    document.getElementById('newFile').addEventListener('click', newScript);

    document.getElementById('save').addEventListener('click', async () => {
      if (!currentPath) {
        const requestedPath = prompt('Save new script as', 'new-script.etlsql');
        if (!requestedPath) return;
        currentPath = requestedPath;
        document.getElementById('activeFile').textContent = currentPath;
      }
      await api('/api/files', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: currentPath, content: editor.getValue() })
      });
      await refreshFiles(currentPath);
      status('Saved ' + currentPath);
    });

    document.getElementById('analyze').addEventListener('click', async () => {
      await editor.analyze?.();
      const result = await api('/api/analyze', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ script: editor.getValue(), documentUri: currentPath || 'untitled.etlsql' })
      });
      status(result.diagnostics.length + ' diagnostic(s)');
    });

    document.getElementById('suggest').addEventListener('click', () => {
      if (!editor.triggerCompletion?.()) status('Suggestions are not available in this browser session.');
    });

    document.getElementById('format').addEventListener('click', async () => {
      status('Formatting...');
      const result = await api('/api/format', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ script: editor.getValue(), documentUri: currentPath || 'untitled.etlsql' })
      });
      editor.setValue(result.script || '');
      status(result.diagnostics?.length ? 'Format completed with warning: ' + result.diagnostics[0].message : 'Formatted');
    });

    document.getElementById('run').addEventListener('click', async () => {
      const script = editor.getValue();
      status('Running...');
      const result = await api('/api/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ script, documentUri: currentPath || 'untitled.etlsql', rowLimit: 100 })
      });
      renderRunResult(result);
      status(result.success ? 'Run completed' : 'Run failed');
    });

    boot().catch(err => status(err.message));
  </script>
</body>
</html>
""";
    }
}
