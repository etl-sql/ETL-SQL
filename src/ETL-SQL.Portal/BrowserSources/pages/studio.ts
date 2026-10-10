/** Portal host for the shared catalog Studio workbench. */
import { apiFetch, auth, authApi, studioApi } from '../api.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { getSessionIdentity, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { createStudioWorkbench } from '../../designer/studio.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';
import { readStudioFolders, readStudioReports, readStudioSession } from '../authoring-responses.js';
import { showAuthoringError } from '../portal-authoring.js';
import { createPortalStudioHost } from '../portal-studio-host.js';
import { asError } from '../http-error.js';

renderPortalHeader();
installDialogAccessibility();
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

async function openStudio(): Promise<void> {
    const container = document.getElementById('studioHost');
    if (!container) throw new Error('Studio host is missing.');
    try {
        const studioSession = readStudioSession(await studioApi.session());
        const capabilities = new Set(studioSession.capabilities);
        const [reports, folders] = await Promise.all([
            capabilities.has('ScriptRead') ? studioApi.reports() : Promise.resolve([]),
            capabilities.has('ScriptSave') ? studioApi.folders() : Promise.resolve([])
        ]);
        const catalogReports = readStudioReports(reports).map(report => ({ ...report }));
        const catalogFolders = readStudioFolders(folders).map(folder => ({ ...folder }));
        const studio = await createStudioWorkbench(container, {
            catalogReports, catalogFolders, capabilities: studioSession.capabilities,
            deploymentMode: studioSession.mode, sourceControlEnabled: studioSession.sourceControlEnabled,
            activeDocId: '__home__', authFetch: apiFetch, apiBase: '', catalogPipelines: true,
            ...createPortalStudioHost(studioSession, apiFetch, studioApi.createReport)
        });
        const requestedReportId = Number.parseInt(new URLSearchParams(window.location.search).get('reportId') || '', 10);
        if (Number.isInteger(requestedReportId) && requestedReportId > 0) {
            const requested = catalogReports.find(report => report.id === requestedReportId);
            if (requested) await studio.openCatalogReport(requested);
            else ETLSQLFeedback?.notify?.('The requested report is unavailable or outside your catalog permissions.',
                { title: 'Report Not Opened', tone: 'warning' });
        }
        window.__STUDIO__ = studio;
    } catch (error) {
        ETLSQLFeedback?.notify?.('Studio session failed to load: ' + asError(error).message,
            { title: 'Studio Unavailable', tone: 'error' });
        showAuthoringError(container, error, () => window.location.reload());
    }
}

await openStudio();
