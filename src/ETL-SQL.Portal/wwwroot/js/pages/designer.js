/**
 * Page module for designer.html.
 *
 * Moved out of an inline <script type="module"> block so it is a file the type gate,
 * the linters and the parse check can all see. Behaviour is unchanged.
 */

import { auth, authApi, studioApi } from '../api.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { getSessionIdentity, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { createDesigner } from '../../designer/designer.js';
import { renderPortalHeader } from '../portal-header.js';

renderPortalHeader();
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

const params     = new URLSearchParams(window.location.search);
const reportId   = params.get('id')       ? parseInt(params.get('id'), 10)       : null;
const folderId   = params.get('folderId') ? parseInt(params.get('folderId'), 10) : null;
const initialMode = params.get('mode') === 'code' ? 'code' : 'design';
const container = document.getElementById('designerHost');

// ── Load initial state ─────────────────────────────────────────────────────
async function authFetch(url, opts = {}) {
  const res = await fetch(url, {
    ...opts,
    headers: { ...(opts.headers || {}), Authorization: `Bearer ${auth.getToken()}` }
  });
  if (res.status === 401) { auth.redirectToLogin(); return null; }
  return res;
}

async function apiJson(url, opts = {}) {
  if (opts.body && typeof opts.body === 'object') {
    opts = { ...opts, body: JSON.stringify(opts.body),
             headers: { 'Content-Type': 'application/json', ...(opts.headers || {}) } };
  }
  const res = await authFetch(url, opts);
  if (!res) return null;
  if (!res.ok) { const e = await res.json().catch(() => ({})); throw new Error(e.error || res.statusText); }
  if (res.status === 204) return null;
  return res.json();
}

let initialState      = null;
let initialReportName = params.get('name')?.trim() || 'New Report';
let initialFolderId   = folderId;
let initialReportVersion = null;
let initialSourceRevision = null;
let initialSourceControlEnabled = false;
let initialSnapshot = null;
let studioSession = null;
let studioFolders = [];

try {
  studioSession = await studioApi.session();
  const required = reportId
    ? ['ScriptRead', 'ScriptPreview', 'ScriptSave']
    : ['ScriptPreview', 'ScriptSave', 'ReportPublish'];
  if (!required.every(capability => studioSession.capabilities.includes(capability))) {
    window.location.replace('/index.html');
    throw new Error('Studio capability unavailable.');
  }
  studioFolders = await studioApi.folders();
} catch (error) {
  if (!studioSession) window.location.replace('/index.html');
  throw error;
}

if (reportId) {
  try {
    const report = await apiJson(`/api/reports/${reportId}`);
    if (report) {
      initialReportName = report.name;
      initialFolderId   = report.folderId;
    }
    const sc = await apiJson(`/api/reports/${reportId}/script-content`);
    initialReportVersion = sc?.version ?? report?.version ?? null;
    initialSourceRevision = sc?.sourceRevision ?? null;
    initialSourceControlEnabled = sc?.sourceControlEnabled ?? false;
    if (sc?.scriptText) {
      const parsed = await apiJson('/api/designer/parse', { method: 'POST', body: { script: sc.scriptText } });
      if (parsed?.designState?.pages?.length) initialState = parsed.designState;
    }
  } catch (err) {
    console.warn('Designer: failed to load report', err);
  }

  // Lay visuals out against the last compiled snapshot rather than empty placeholders. Absence is
  // the normal case — a report that has never run has no snapshot, and an identity-sensitive report
  // deliberately never persists a shared one — so a failure here must not block opening the designer.
  try {
    initialSnapshot = await apiJson(`/api/designer/snapshot/${reportId}`);
  } catch {
    initialSnapshot = null;
  }
}

createDesigner(container, {
  designState:  initialState,
  snapshotPackage: initialSnapshot,
  reportId,
  reportVersion: initialReportVersion,
  sourceRevision: initialSourceRevision,
  sourceControlEnabled: initialSourceControlEnabled && studioSession.capabilities.includes('SourceCommit'),
  reportName:   initialReportName,
  folderId:     initialFolderId,
  folders:      studioFolders,
  initialMode,
  apiBase:      '',
  host:         'portal',
  authFetch,
  onSave:  () => { window.location.href = '/studio.html'; },
  onCancel:() => { window.location.href = '/studio.html'; },
});
