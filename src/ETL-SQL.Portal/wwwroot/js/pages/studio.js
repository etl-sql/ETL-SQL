/**
 * Page module for studio.html.
 *
 * Moved out of an inline <script type="module"> block so it is a file the type gate,
 * the linters and the parse check can all see. Behaviour is unchanged.
 */

import { auth, authApi, studioApi } from '../api.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { getSessionIdentity, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { createStudioWorkbench } from '../../designer/studio.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';

renderPortalHeader();
installDialogAccessibility();
if (!auth.isLoggedIn()) { window.location.href = '/login.html'; }
initTheme();
applyPortalBranding();

const identity = getSessionIdentity(auth.getToken());
renderSessionIdentity(identity, document.getElementById('topbarUser'));

const logoutBtn = document.getElementById('logoutBtn');
if (logoutBtn) {
  logoutBtn.style.display = 'inline-block';
  logoutBtn.addEventListener('click', () => authApi.logout());
}

applyNavigationSafely();

const params = new URLSearchParams(window.location.search);
const container = document.getElementById('studioHost');

async function authFetch(url, opts = {}) {
  const res = await fetch(url, {
    ...opts,
    headers: { ...(opts.headers || {}), Authorization: `Bearer ${auth.getToken()}` }
  });
  if (res.status === 401) { auth.redirectToLogin(); return null; }
  return res;
}

async function readJson(url, opts = {}) {
  const res = await authFetch(url, opts);
  if (!res) throw new Error('The Portal session ended while opening the report.');
  if (!res.ok) {
    const problem = await res.json().catch(() => ({}));
    const error = new Error(problem.error || `Request failed (${res.status}).`);
    error.status = res.status;
    error.payload = problem;
    throw error;
  }
  if (res.status === 204) return null;
  return res.json();
}

let studioSession;
let catalogReports = [];
let catalogFolders = [];
try {
  studioSession = await studioApi.session();
  const capabilities = new Set(studioSession.capabilities || []);
  [catalogReports, catalogFolders] = await Promise.all([
    capabilities.has('ScriptRead') ? studioApi.reports() : Promise.resolve([]),
    capabilities.has('ScriptSave') ? studioApi.folders() : Promise.resolve([])
  ]);
} catch (error) {
  ETLSQLFeedback?.notify?.('Studio catalog failed to load: ' + error.message, { title: 'Studio Unavailable', tone: 'error' });
  studioSession = { mode: 'Viewer', capabilities: [], sourceControlEnabled: false };
}

const capabilities = new Set(studioSession.capabilities || []);

async function acquireLease(reportId) {
  return readJson('/api/designer/lease', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reportId, force: false })
  });
}

async function openCatalogDocument(report) {
  const script = await readJson(`/api/reports/${report.id}/script-content`);
  const filename = /\.rptsql$/i.test(report.name) ? report.name : `${report.name}.rptsql`;
  let lease = { acquired: false };
  let canSave = false;
  let readOnlyReason = capabilities.has('ScriptSave')
    ? null
    : 'Your deployment mode or permissions allow viewing this report, but not saving it.';

  if (capabilities.has('ScriptSave')) {
    try {
      lease = await acquireLease(report.id);
      canSave = Boolean(lease.acquired);
    } catch (error) {
      const owner = error.payload?.owner || 'Another author';
      const expiresAt = error.payload?.expiresAt ? new Date(error.payload.expiresAt) : null;
      const expiry = expiresAt && !Number.isNaN(expiresAt.valueOf()) ? ` until ${expiresAt.toLocaleTimeString()}` : '';
      readOnlyReason = error.status === 409
        ? `${owner} is editing this report${expiry}. Saving is unavailable.`
        : `The edit lease could not be acquired: ${error.message}`;
    }
  }

  return {
    id: `report-${report.id}`,
    reportId: report.id,
    folderId: report.folderId,
    folderPath: report.folderPath,
    version: script.version ?? report.version ?? null,
    sourceRevision: script.sourceRevision ?? null,
    sourceControlEnabled: script.sourceControlEnabled ?? studioSession.sourceControlEnabled,
    path: `${report.folderPath || 'reports'}/${filename}`,
    name: filename,
    content: script.scriptText || '',
    lease,
    canSave,
    readOnlyReason
  };
}

const studio = await createStudioWorkbench(container, {
  catalogReports,
  catalogFolders,
  capabilities: studioSession.capabilities,
  deploymentMode: studioSession.mode,
  sourceControlEnabled: studioSession.sourceControlEnabled,
  activeDocId: '__home__',
  authFetch,
  apiBase: '',
  onOpenDocument: openCatalogDocument,
  onCreateDocument: request => studioApi.createReport({
    folderId: Number(request.folderId),
    name: request.name,
    scriptText: request.scriptText,
    description: null
  }),
  onRenewDocument: doc => acquireLease(doc.reportId),
  onCloseDocument: async (doc, { keepalive = false } = {}) => {
    if (!doc?.reportId || !doc.lease?.acquired) return;
    const res = await authFetch(`/api/designer/lease/${doc.reportId}`, { method: 'DELETE', keepalive });
    if (!res && !keepalive) throw new Error('The Portal session ended before the edit lease was released.');
    if (res && !res.ok && !keepalive) throw new Error(`The edit lease could not be released (${res.status}).`);
    doc.lease.acquired = false;
  },
  onSave: async (content, filePath, doc) => {
    if (!doc?.reportId || doc.version === null || doc.version === undefined) {
      throw new Error('This document is not attached to a versioned Portal catalog report.');
    }
    if (!capabilities.has('ScriptSave') || !doc.lease?.acquired) {
      throw new Error('Saving requires ScriptSave capability and an active edit lease.');
    }
    const res = await authFetch('/api/designer/save', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'If-Match': `"${doc.version}"`
      },
      body: JSON.stringify({
        reportId: doc.reportId,
        scriptText: content,
        baseRevision: doc.sourceRevision || null
      })
    });
    if (!res) throw new Error('The Portal session ended before the save completed.');
    if (!res.ok) {
      const problem = await res.json().catch(() => ({}));
      throw new Error(problem.error || `Portal save failed (${res.status}).`);
    }
    const saved = await res.json();
    return {
      version: saved.version ?? doc.version,
      sourceRevision: saved.sourceRevision ?? doc.sourceRevision
    };
  }
});

const requestedReportId = Number.parseInt(params.get('reportId') || '', 10);
if (Number.isInteger(requestedReportId) && requestedReportId > 0) {
  const requestedReport = catalogReports.find(report => report.id === requestedReportId);
  if (requestedReport) {
    await studio.openCatalogReport(requestedReport);
  } else {
    ETLSQLFeedback?.notify?.('The requested report is unavailable or outside your catalog permissions.', { title: 'Report Not Opened', tone: 'warning' });
  }
}
window.__STUDIO__ = studio;
