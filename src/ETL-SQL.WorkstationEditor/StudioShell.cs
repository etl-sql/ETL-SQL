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
    const clientId = crypto.randomUUID();
    let heartbeatTimer = null;

    function hasDirtyDocuments() {
      return Boolean(window.__STUDIO__?.state?.documents?.some(document => document.isDirty));
    }

    async function sendHeartbeat() {
      await authFetch('/api/studio/heartbeat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ clientId, dirty: hasDirtyDocuments() })
      });
    }

    async function checkExternalChanges() {
      for (const document of window.__STUDIO__?.state?.documents || []) {
        if (!document.sourceRevision || document.externalChange) continue;
        const response = await authFetch('/api/files/revision?path=' + encodeURIComponent(document.path));
        if (!response.ok) continue;
        const current = await response.json();
        if (current.sourceRevision && current.sourceRevision !== document.sourceRevision) {
          document.externalChange = true;
          document.canSave = false;
          document.readOnlyReason = 'This file changed outside Studio. Close and reopen it before saving.';
          window.ETLSQLFeedback?.notify(document.readOnlyReason, {
            title: 'External Change Detected',
            tone: 'warning'
          });
        }
      }
    }

    async function exitStudio(state) {
      await sendHeartbeat();
      const response = await authFetch('/api/studio/shutdown', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ force: Boolean(state?.force) })
      });
      if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'The Studio host refused the shutdown request.');
      }

      const deadline = Date.now() + 5000;
      while (Date.now() < deadline) {
        await new Promise(resolve => setTimeout(resolve, 150));
        try {
          const health = await authFetch('/api/studio/lifecycle');
          if (!health.ok) return true;
        } catch {
          return true;
        }
      }
      return false;
    }

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
        let initialSourceRevision = null;
        try {
          const res = await authFetch('/api/files?path=' + encodeURIComponent(initialFile));
          if (res.ok) {
            const data = await res.json();
            initialContent = data.content || '';
            initialSourceRevision = data.sourceRevision || null;
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
          sourceRevision: initialSourceRevision,
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
        onExit: exitStudio,
        onSave: async (content, filePath) => {
          if (readOnly) return;
          const res = await authFetch('/api/files', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ path: filePath, content, baseRevision: window.__STUDIO__?.state?.documents?.find(document => document.path === filePath)?.sourceRevision || null })
          });
          if (!res.ok) {
            const error = await res.json().catch(() => ({}));
            throw new Error(error.error || 'The file could not be saved.');
          }
          const saved = await res.json();
          return { sourceRevision: saved.sourceRevision, canSave: true, readOnlyReason: null, externalChange: false };
        },
        onRenameDocument: readOnly ? null : async (document, name) => {
          const res = await authFetch('/api/files/rename', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ path: document.path, name })
          });
          if (!res.ok) {
            const error = await res.json().catch(() => ({}));
            throw new Error(error.error || 'The file could not be renamed.');
          }
          return res.json();
        }
      });
      await sendHeartbeat();
      heartbeatTimer = window.setInterval(() => {
        Promise.all([sendHeartbeat(), checkExternalChanges()]).catch(() => window.clearInterval(heartbeatTimer));
      }, 10000);
    }

    window.addEventListener('pagehide', () => {
      if (heartbeatTimer) window.clearInterval(heartbeatTimer);
      const body = new Blob([JSON.stringify({ clientId, dirty: hasDirtyDocuments() })], { type: 'application/json' });
      navigator.sendBeacon('/api/studio/disconnect?token=' + encodeURIComponent(token), body);
    });

    boot().catch(err => console.error('Failed to boot ETL-SQL Studio:', err));
  </script>
</body>
</html>
""";
    }
}
