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

      let docs = [];
      let activeDocId = '__home__';
      if (initialFile) {
        let initialContent = '';
        try {
          const res = await authFetch('/api/files?path=' + encodeURIComponent(initialFile));
          if (res.ok) {
            const data = await res.json();
            initialContent = data.content || '';
          }
        } catch (e) {
          console.error('Failed to load primary file:', e);
        }
        docs.push({
          id: 'doc-primary',
          path: initialFile,
          name: initialFile.split('/').pop().split('\\').pop(),
          content: initialContent || '',
          isDirty: false,
          projection: 'split'
        });
        activeDocId = 'doc-primary';
      }

      window.__STUDIO__ = await createStudioWorkbench(document.getElementById('studioHost'), {
        documents: docs,
        activeDocId: activeDocId,
        workspaceFiles: workspaceFiles,
        apiBase: '',
        authFetch,
        onSave: async (content, filePath) => {
          if (readOnly) return;
          const res = await authFetch('/api/files', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ path: filePath, content })
          });
          if (!res.ok) throw new Error(await res.text());
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
