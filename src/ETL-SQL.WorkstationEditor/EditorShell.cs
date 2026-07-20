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
  <link rel="stylesheet" href="/designer/designer.css?v={{options.SessionToken}}">
  <style>
    /* Design tokens mirror src/ETL-SQL.Portal/wwwroot/css/portal.css so the local
       editor and the hosted Portal share one visual language. Keep them in sync. */
    :root {
      --portal-bg: #f3f5f8;
      --portal-bg-soft: #eef2f7;
      --portal-surface: #ffffff;
      --portal-surface-subtle: #f8fafc;
      --portal-surface-raised: #ffffff;
      --portal-text: #172033;
      --portal-text-soft: #46556c;
      --portal-muted: #5a6778;
      --portal-text-muted: #5a6778;
      --portal-border: #d9e0ea;
      --portal-border-soft: #e8edf4;
      --portal-accent: #2563eb;
      --portal-accent-hover: #1d4ed8;
      --portal-accent-soft: #e8f0ff;
      --portal-danger: #b83535;
      --portal-warning: #a05a00;
      --portal-success: #117853;
      --portal-focus-ring: #1d4ed8;
      --portal-focus: rgba(37, 99, 235, .36);
      --portal-shadow-sm: 0 1px 2px rgba(15, 23, 42, .06);
      --portal-shadow-md: 0 14px 30px rgba(15, 23, 42, .12);
      --portal-radius: 8px;
      --portal-radius-sm: 5px;
      --portal-font: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
      --portal-font-mono: Cascadia Code, Consolas, monospace;
    }
    body.theme-dark {
      color-scheme: dark;
      --portal-bg: #0b0f19;
      --portal-bg-soft: #111827;
      --portal-surface: #1f2937;
      --portal-surface-subtle: #111827;
      --portal-surface-raised: #1f2937;
      --portal-text: #f9fafb;
      --portal-text-soft: #d1d5db;
      --portal-muted: #9ca3af;
      --portal-text-muted: #9ca3af;
      --portal-border: #374151;
      --portal-border-soft: #1f2937;
      --portal-accent: #3b82f6;
      --portal-accent-hover: #60a5fa;
      --portal-accent-soft: rgba(59, 130, 246, 0.15);
      --portal-danger: #f87171;
      --portal-warning: #fbbf24;
      --portal-success: #34d399;
      --portal-focus-ring: #60a5fa;
      --portal-focus: rgba(96, 165, 250, .36);
      --portal-shadow-sm: 0 1px 2px rgba(0, 0, 0, .3);
      --portal-shadow-md: 0 14px 30px rgba(0, 0, 0, .5);
    }
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
