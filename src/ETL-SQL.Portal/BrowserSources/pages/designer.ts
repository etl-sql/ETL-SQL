/** Portal host for the shared report designer. */
import { apiFetch, auth, authApi, studioApi } from '../api.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { getSessionIdentity, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { createDesigner } from '../../designer/designer.js';
import { renderPortalHeader } from '../portal-header.js';
import { PortalAuthoringError, readDesignerParse, readDesignerReport, readDesignerSnapshot, readScriptContent, readStudioFolders, readStudioSession } from '../authoring-responses.js';
import { readAuthoringJson, showAuthoringError } from '../portal-authoring.js';
import { asError } from '../http-error.js';
import type { DesignerSnapshotPackage, DesignerState } from '../../designer/designer-context.js';

renderPortalHeader();
if (!auth.isLoggedIn()) window.location.href = '/login.html';
initTheme();
void applyPortalBranding();
renderSessionIdentity(getSessionIdentity(auth.getToken()), document.getElementById('topbarUser'));
const logoutBtn = document.getElementById('logoutBtn');
if (logoutBtn) {
    logoutBtn.style.display = 'inline-block';
    logoutBtn.addEventListener('click', () => { void authApi.logout(); });
}
void applyNavigationSafely();

async function openDesigner(): Promise<void> {
    const container = document.getElementById('designerHost');
    if (!container) throw new Error('Designer host is missing.');
    try {
        const params = new URLSearchParams(window.location.search);
        const reportId = params.get('id') ? Number.parseInt(params.get('id')!, 10) : null;
        const folderId = params.get('folderId') ? Number.parseInt(params.get('folderId')!, 10) : null;
        const initialMode = params.get('mode') === 'code' ? 'code' : 'design';
        const studioSession = readStudioSession(await studioApi.session());
        const required = reportId ? ['ScriptRead', 'ScriptPreview', 'ScriptSave']
            : ['ScriptPreview', 'ScriptSave', 'ReportPublish'];
        if (!required.every(capability => studioSession.capabilities.includes(capability))) {
            window.location.replace('/index.html');
            return;
        }
        const studioFolders = readStudioFolders(await studioApi.folders());
        let initialState: DesignerState | null = null;
        let initialSnapshot: DesignerSnapshotPackage | null = null;
        let initialScript = '';
        let initialReportName = params.get('name')?.trim() || 'New Report';
        let initialFolderId = folderId;
        let initialReportVersion: number | null = null;
        let initialSourceRevision: string | null = null;
        let initialSourceControlEnabled = false;
        if (reportId) {
            const report = readDesignerReport(await readAuthoringJson(await apiFetch(`/api/reports/${reportId}`)));
            initialReportName = report.name;
            initialFolderId = report.folderId;
            const script = readScriptContent(await readAuthoringJson(await apiFetch(`/api/reports/${reportId}/script-content`)));
            initialScript = script.scriptText;
            initialReportVersion = script.version;
            initialSourceRevision = script.sourceRevision ?? null;
            initialSourceControlEnabled = script.sourceControlEnabled;
            if (initialScript) {
                initialState = readDesignerParse(await readAuthoringJson(await apiFetch('/api/designer/parse', {
                    method: 'POST', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ script: initialScript })
                })));
            }
            // An unrun or identity-sensitive report has no shared snapshot. Its script is still editable.
            try {
                const snapshot = await readAuthoringJson(await apiFetch(`/api/designer/snapshot/${reportId}`));
                if (snapshot !== null) initialSnapshot = readDesignerSnapshot(snapshot);
            } catch (error) {
                if (error instanceof PortalAuthoringError && error.status === 401) throw error;
                if (!(error instanceof PortalAuthoringError && error.status === 404))
                    ETLSQLFeedback?.notify?.('Snapshot preview unavailable: ' + asError(error).message,
                        { title: 'Designer Preview', tone: 'warning' });
            }
        }
        const designer = createDesigner(container, {
            designState: initialState, initialScript, snapshotPackage: initialSnapshot ?? undefined, reportId,
            reportVersion: initialReportVersion, sourceRevision: initialSourceRevision,
            sourceControlEnabled: initialSourceControlEnabled && studioSession.capabilities.includes('SourceCommit'),
            reportName: initialReportName, folderId: initialFolderId, folders: studioFolders,
            initialMode, apiBase: '', host: 'portal', authFetch: apiFetch,
            onSave: () => { window.location.href = '/studio.html'; },
            onCancel: () => { window.location.href = '/studio.html'; }
        });
        window.addEventListener('pagehide', event => { if (!event.persisted) designer.dispose(); });
    } catch (error) {
        showAuthoringError(container, error, () => window.location.reload());
    }
}

await openDesigner();
