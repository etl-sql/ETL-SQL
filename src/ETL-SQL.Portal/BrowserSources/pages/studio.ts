/** Portal host for the shared catalog Studio workbench. */
import { apiFetch, auth, authApi, studioApi } from '../api.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { getSessionIdentity, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { createStudioWorkbench } from '../../designer/studio.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';
import { readStudioCatalogPage, readStudioFolders, readStudioPrivateDrafts, readStudioReport, readStudioSession } from '../authoring-responses.js';
import { showAuthoringError } from '../portal-authoring.js';
import { createPortalStudioHost } from '../portal-studio-host.js';
import { asError } from '../http-error.js';

renderPortalHeader();
installDialogAccessibility();
if (!auth.isLoggedIn()) window.location.href = '/login.html';
initTheme();
void applyPortalBranding();
const identityHost = document.getElementById('topbarUser');
if (identityHost) identityHost.textContent = 'Verifying session…';
const logoutBtn = document.getElementById('logoutBtn');
if (logoutBtn) {
    logoutBtn.style.display = 'inline-block';
    logoutBtn.addEventListener('click', () => { void authApi.logout(); });
}
void applyNavigationSafely();

async function openStudio(): Promise<void> {
    const container = document.getElementById('studioHost');
    if (!container) throw new Error('Studio host is missing.');
    const controller = new AbortController();
    const deadline = setTimeout(() => controller.abort(new Error('Studio startup timed out after 15 seconds. Check the connection, then retry.')), 15_000);
    try {
        const studioSession = readStudioSession(await studioApi.session(controller.signal));
        renderSessionIdentity(getSessionIdentity(auth.getToken()), identityHost);
        const capabilities = new Set(studioSession.capabilities);
        const [reports, folders, privateDrafts] = await Promise.all([
            capabilities.has('ScriptRead') ? studioApi.catalog('', 0, controller.signal) : Promise.resolve({ items: [], nextAfterId: null }),
            capabilities.has('ScriptSave') ? studioApi.folders(controller.signal) : Promise.resolve([]),
            studioSession.privateDrafts && capabilities.has('ScriptRead') ? studioApi.privateDrafts(controller.signal) : Promise.resolve([])
        ]);
        clearTimeout(deadline);
        const catalogPage = readStudioCatalogPage(reports);
        const catalogReports = catalogPage.items.map(report => ({ ...report }));
        const catalogFolders = readStudioFolders(folders).map(folder => ({ ...folder }));
        const studio = await createStudioWorkbench(container, {
            catalogReports, catalogFolders, privateDraftSummaries: readStudioPrivateDrafts(privateDrafts),
            catalogDiscovery: { nextAfterId: catalogPage.nextAfterId,
                search: async (query, afterId) => {
                    const page = readStudioCatalogPage(await studioApi.catalog(query, afterId));
                    return { items: page.items.map(report => ({ ...report })), nextAfterId: page.nextAfterId };
                } },
            capabilities: studioSession.capabilities,
            maxScriptCharacters: studioSession.maxScriptCharacters,
            deploymentMode: studioSession.mode, sourceControlEnabled: studioSession.sourceControlEnabled,
            activeDocId: '__home__', authFetch: apiFetch, apiBase: '', catalogPipelines: true,
            ...createPortalStudioHost(studioSession, apiFetch, studioApi.createReport)
        });
        const requestedReportId = Number.parseInt(new URLSearchParams(window.location.search).get('reportId') || '', 10);
        if (Number.isInteger(requestedReportId) && requestedReportId > 0) {
            try { await studio.openCatalogReport(readStudioReport(await studioApi.reportSummary(requestedReportId))); }
            catch (error) { ETLSQLFeedback?.notify?.(asError(error).message, { title: 'Report Not Opened', tone: 'warning' }); }
        }
        window.__STUDIO__ = studio;
    } catch (error) {
        const reason: unknown = controller.signal.aborted ? controller.signal.reason : error;
        ETLSQLFeedback?.notify?.('Studio session failed to load: ' + asError(reason).message,
            { title: 'Studio Unavailable', tone: 'error' });
        showAuthoringError(container, reason, () => window.location.reload());
    } finally {
        clearTimeout(deadline);
    }
}

await openStudio();
