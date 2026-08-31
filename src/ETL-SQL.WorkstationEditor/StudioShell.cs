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
    const requestTimeoutMs = 5000;
    const shutdownConfirmationTimeoutMs = 10000;
    let heartbeatTimer = null;
    let shutdownStarted = false;

    const delay = milliseconds => new Promise(resolve => window.setTimeout(resolve, milliseconds));

    async function authFetchWithTimeout(url, opts = {}, timeoutMs = requestTimeoutMs) {
      const controller = new AbortController();
      const timeout = window.setTimeout(() => controller.abort(), timeoutMs);
      try {
        return await authFetch(url, { ...opts, signal: controller.signal });
      } catch (error) {
        if (controller.signal.aborted) {
          const timeoutError = new Error(`Studio lifecycle request timed out after ${timeoutMs} ms.`);
          timeoutError.name = 'TimeoutError';
          throw timeoutError;
        }
        throw error;
      } finally {
        window.clearTimeout(timeout);
      }
    }

    function stopHeartbeat() {
      if (heartbeatTimer) window.clearInterval(heartbeatTimer);
      heartbeatTimer = null;
    }

    function startHeartbeat() {
      if (heartbeatTimer || shutdownStarted) return;
      heartbeatTimer = window.setInterval(() => {
        Promise.all([sendHeartbeat(), checkExternalChanges()]).catch(() => stopHeartbeat());
      }, 10000);
    }

    function hasDirtyDocuments() {
      return Boolean(window.__STUDIO__?.state?.documents?.some(document => document.isDirty));
    }

    async function sendHeartbeat() {
      if (shutdownStarted) return;
      await authFetchWithTimeout('/api/studio/heartbeat', {
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
      shutdownStarted = true;
      stopHeartbeat();

      let requestError = null;
      let response = null;
      try {
        response = await authFetchWithTimeout('/api/studio/shutdown', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ force: Boolean(state?.force) })
        });
      } catch (error) {
        requestError = error;
      }
      if (response && !response.ok) {
        shutdownStarted = false;
        startHeartbeat();
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || 'The Studio host refused the shutdown request.');
      }

      const deadline = Date.now() + shutdownConfirmationTimeoutMs;
      let consecutiveDisconnects = 0;
      while (Date.now() < deadline) {
        await delay(150);
        try {
          await authFetchWithTimeout('/api/studio/lifecycle', {}, 750);
          consecutiveDisconnects = 0;
        } catch (error) {
          if (error?.name === 'TimeoutError') {
            consecutiveDisconnects = 0;
            continue;
          }
          consecutiveDisconnects++;
          if (consecutiveDisconnects >= 2) {
            window.setTimeout(() => {
              document.title = 'ETL-SQL Studio — Stopped';
              document.body.innerHTML = `<main role="status" style="display:grid;place-items:center;min-height:100%;padding:24px;box-sizing:border-box;text-align:center">
                <div><h1 style="margin:0 0 8px;font-size:1.25rem">Studio stopped</h1>
                <p style="margin:0;color:var(--portal-text-soft,#8b949e)">The project host exited cleanly. You can close this tab.</p></div>
              </main>`;
            }, 0);
            return true;
          }
        }
      }

      shutdownStarted = false;
      startHeartbeat();
      if (requestError) throw requestError;
      return false;
    }

    async function mutateWorkspace(route, body) {
      const response = await authFetch(route, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      });
      if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || `The workspace operation failed (${response.status}).`);
      }
      const result = response.status === 204 ? null : await response.json();
      const workspaceResponse = await authFetch('/api/workspace');
      if (!workspaceResponse.ok) throw new Error('The workspace changed, but Explorer could not be refreshed.');
      const workspace = await workspaceResponse.json();
      return { ...workspace, result };
    }

    async function boot() {
      let workspaceFiles = [];
      let workspaceFolders = [];
      try {
        const wsRes = await authFetch('/api/workspace');
        if (wsRes.ok) {
          const wsData = await wsRes.json();
          workspaceFiles = wsData.files || [];
          workspaceFolders = wsData.folders || [];
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
        workspaceFolders: workspaceFolders,
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
        },
        onCreateWorkspaceFolder: readOnly ? null : path => mutateWorkspace('/api/workspace/folders', { path }),
        onRenameWorkspaceEntry: readOnly ? null : (entry, name) => mutateWorkspace('/api/workspace/rename', {
          path: entry.path,
          name,
          isDirectory: entry.isDirectory
        }),
        onDeleteWorkspaceEntry: readOnly ? null : entry => mutateWorkspace('/api/workspace/delete', {
          path: entry.path,
          isDirectory: entry.isDirectory
        }),
        onMoveWorkspaceFile: readOnly ? null : (path, destinationFolder) => mutateWorkspace('/api/workspace/move', {
          path,
          destinationFolder
        }),
        onLoadGitStatus: async () => {
          const response = await authFetch('/api/git/status');
          if (!response.ok) throw new Error('Git status could not be loaded.');
          return response.json();
        },
        onLoadGitHistory: async document => {
          const response = await authFetch('/api/git/history?path=' + encodeURIComponent(document.path));
          if (!response.ok) throw new Error('Git history could not be loaded for this script.');
          return response.json();
        },
        onLoadGitDiff: async (document, revision, content) => {
          const response = await authFetch('/api/git/diff', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ path: document.path, revision, content })
          });
          if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.error || 'Git could not build this comparison.');
          }
          return response.json();
        }
      });
      await sendHeartbeat();
      startHeartbeat();
    }

    window.addEventListener('pagehide', () => {
      stopHeartbeat();
      if (shutdownStarted) return;
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
