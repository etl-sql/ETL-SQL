using System.Net;

namespace ETL_SQL.WorkstationEditor;

internal static class StudioShell
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
  <title>ETL-SQL Studio (Local Workbench)</title>
  <link rel="stylesheet" href="/css/portal.css?v={{options.SessionToken}}">
  <link rel="stylesheet" href="/designer/designer.css?v={{options.SessionToken}}">
  <style>
    html, body {
      margin: 0;
      padding: 0;
      width: 100%;
      height: 100%;
      overflow: hidden;
      background: var(--portal-bg, #0d1117);
      color: var(--portal-text, #f0f6fc);
      font-family: var(--portal-font, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif);
    }
    #studioHost {
      width: 100%;
      height: 100%;
    }
  </style>
</head>
<body>
  <div id="studioHost"></div>
  <script src="/runtime/feedback.js?v={{options.SessionToken}}"></script>
  <script type="module">
    import { createStudioWorkbench } from '/designer/studio.js?v={{options.SessionToken}}';
    const token = new URLSearchParams(location.search).get('token') || '';
    const initialFile = '{{initialFile}}';
    const readOnly = {{readonlyAttr}};
    const authFetch = (url, opts = {}) =>
      fetch(url, { ...opts, headers: { 'X-ETLSQL-EDITOR-TOKEN': token, ...(opts.headers || {}) } });

    async function boot() {
      let workspaceFiles = [];
      try {
        const wsRes = await authFetch('/api/workspace');
        if (wsRes.ok) {
          const wsData = await wsRes.json();
          workspaceFiles = wsData.files || [];
        }
      } catch (e) {
        console.error('Failed to fetch workspace files:', e);
      }

      let primaryFile = initialFile || (workspaceFiles.length > 0 ? workspaceFiles[0].path : 'untitled.rptsql');
      let initialContent = '';
      if (primaryFile && primaryFile !== 'untitled.rptsql') {
        try {
          const res = await authFetch('/api/files?path=' + encodeURIComponent(primaryFile));
          if (res.ok) {
            const data = await res.json();
            initialContent = data.content || '';
          }
        } catch (e) {
          console.error('Failed to load primary file:', e);
        }
      }

      const docs = [
        {
          id: 'doc-primary',
          path: primaryFile,
          name: (primaryFile ? primaryFile.split('/').pop().split('\\').pop() : 'untitled.rptsql'),
          content: initialContent || '',
          isDirty: false,
          projection: 'split'
        }
      ];

      for (const f of workspaceFiles.slice(0, 10)) {
        if (f.path === primaryFile) continue;
        let content = '';
        try {
          const res = await authFetch('/api/files?path=' + encodeURIComponent(f.path));
          if (res.ok) {
            const data = await res.json();
            content = data.content || '';
          }
        } catch (e) {}
        docs.push({
          id: 'doc-' + Math.random().toString(36).slice(2, 7),
          path: f.path,
          name: f.path.split('/').pop().split('\\').pop(),
          content: content,
          isDirty: false,
          projection: 'split'
        });
      }

      window.__STUDIO__ = await createStudioWorkbench(document.getElementById('studioHost'), {
        documents: docs,
        apiBase: '',
        authFetch,
        onSave: async (content, filePath) => {
          if (readOnly) return;
          try {
            const res = await authFetch('/api/files', {
              method: 'PUT',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ path: filePath, content })
            });
            if (!res.ok) throw new Error(await res.text());
            ETLSQLFeedback?.notify?.('Saved ' + filePath, { title: 'File Saved', tone: 'success' });
          } catch (e) {
            ETLSQLFeedback?.notify?.('Save failed: ' + e.message, { title: 'Save failed', tone: 'error' });
          }
        }
      });
    }

    boot().catch(err => console.error('Failed to boot ETL-SQL Studio:', err));
  </script>
</body>
</html>
""";
    }
}
