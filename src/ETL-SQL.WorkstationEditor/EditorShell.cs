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
  <link rel="stylesheet" href="/css/portal.css?v={{options.SessionToken}}">
  <link rel="stylesheet" href="/designer/designer.css?v={{options.SessionToken}}">
  <style>
    html, body {
      margin: 0;
      padding: 0;
      width: 100%;
      height: 100%;
      overflow: hidden;
      background: var(--portal-bg);
      color: var(--portal-text);
      font-family: var(--portal-font);
    }
    #workbench {
      width: 100%;
      height: 100%;
    }
  </style>
</head>
<body>
  <div id="workbench"></div>
  <script type="module">
    import { createScriptEditorWorkbench } from '/designer/designer.js?v={{options.SessionToken}}';
    const token = new URLSearchParams(location.search).get('token') || '';
    const initialFile = '{{initialFile}}';
    const readOnly = {{readonlyAttr}};
    const authFetch = (url, opts = {}) =>
      fetch(url, { ...opts, headers: { 'X-ETLSQL-EDITOR-TOKEN': token, ...(opts.headers || {}) } });

    async function boot() {
      let initialContent = '';
      if (initialFile) {
        try {
          const res = await authFetch('/api/files?path=' + encodeURIComponent(initialFile));
          if (res.ok) {
            const data = await res.json();
            initialContent = data.content || '';
          }
        } catch (e) {
          console.error('Failed to load initial file:', e);
        }
      }

      await createScriptEditorWorkbench(document.getElementById('workbench'), {
        title: initialFile || 'new-script.etlsql',
        showSidebar: true,
        runUrl: '/api/run',
        previewApiUrl: '/api/preview',
        authFetch,
        onExit: async () => {
          if (confirm('Stop Workstation Editor process and exit?')) {
            try {
              await authFetch('/api/shutdown', { method: 'POST' });
              document.body.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;height:100vh;font-family:Segoe UI,sans-serif;color:#9da7b1;background:#101317;font-size:18px;">Workstation Editor host stopped. You may close this browser tab.</div>';
            } catch (e) {
              alert('Shutdown failed: ' + e.message);
            }
          }
        },
        editor: {
          value: initialContent || '\n'.repeat(9),
          readOnly,
          analyzeUrl: '/api/analyze',
          completeUrl: '/api/complete',
          hoverUrl: '/api/hover',
          authFetch,
          documentUri: () => initialFile || 'untitled.etlsql'
        },
        onSave: async (content, filePath) => {
          if (readOnly) return;
          try {
            const res = await authFetch('/api/files', {
              method: 'PUT',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ path: filePath, content })
            });
            if (!res.ok) throw new Error(await res.text());
          } catch (e) {
            alert('Save failed: ' + e.message);
          }
        }
      });
    }

    boot().catch(console.error);
  </script>
</body>
</html>
""";
    }
}
