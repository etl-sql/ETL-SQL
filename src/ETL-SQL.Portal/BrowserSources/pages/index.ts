import { adminApi, auth, authApi, catalogApi, dataQualityApi, governanceApi, studioApi } from '../api.js';
import { applyPortalBranding, initTheme } from '../branding.js';
import { isObject } from '../http-error.js';
import { lineageRowsToCsv, renderLineageRow } from '../lineage-ui.js';
import { createLineageCatalog } from '../lineage-catalog.js';
import { createDataQualityQueue } from '../data-quality-queue.js';
import { createGovernancePortal } from '../governance-portal.js';
import { renderDag } from '../../designer/designer.js';
import { getSessionIdentity, hasRole, renderSessionIdentity } from '../session-identity.js';
import { applyNavigationSafely } from '../portal-nav.js';
import { renderPortalHeader } from '../portal-header.js';
import { installDialogAccessibility } from '../dialog-a11y.js';
import { createReaderSubscriptions } from '../reader-subscriptions.js';
import { buildReportFrameDocument, createReportViewer } from '../report-viewer.js';
import { createReportLibrary } from '../report-library.js';
import { formatBuiltAt, timeAgo } from '../report-format.js';
import { defaultGovernanceMode, governanceAccess, isDashboardMode, NO_GOVERNANCE_ACCESS, resolveGovernanceRoute, revealedGovernanceNav } from '../governance-access.js';
import type { GovernanceMode } from '../governance-access.js';

/**
 * Page owner for index.html, the catalog page: the report library, the report viewer, the reader's
 * subscriptions, and the governance views share one main area, and exactly one of them owns it at
 * a time. Taking it over ends whatever the previous owner still had in flight.
 */
type Owner = 'library' | 'viewer' | 'subscriptions' | 'governance';
interface HistoryState { view: 'folder' | 'report'; id?: number; page?: string | null; folderId?: number | null; folderName?: string }

const GOVERNANCE_NAV: Array<[string, GovernanceMode]> = [['Overview', 'overview'], ['Workqueue', 'workqueue'], ['Exceptions', 'exceptions'],
    ['Glossary', 'glossary'], ['Quality', 'quality'], ['Quarantine', 'quarantine'], ['Lineage', 'lineage'], ['Settings', 'settings']];

async function mountCatalogPage(): Promise<void> {
    renderPortalHeader();
    installDialogAccessibility();
    // Nothing below runs, or calls the API, for someone who is not signed in.
    if (!auth.isLoggedIn()) { window.location.href = '/login.html'; return; }
    void applyPortalBranding();
    initTheme();

    const feedback = window.ETLSQLFeedback;
    if (!feedback) throw new Error('The catalog page could not load its notification support.');
    const lifetime = new AbortController();
    const box = (id: string): HTMLElement => {
        const found = document.getElementById(id);
        if (!found) throw new TypeError(`Catalog page element is missing: ${id}`);
        return found;
    };
    const main = box('mainContent');
    const notify = (message: string, options: Parameters<EtlSqlFeedback['notify']>[1]) => { feedback.notify(message, options); };

    // Governance views are revealed by role (see governance-access). An unreadable identity keeps
    // the closed position: nothing role-gated is offered, and lineage remains.
    let isAdmin = false, canDesign = false, access = NO_GOVERNANCE_ACCESS;
    try {
        const identity = getSessionIdentity(auth.getToken());
        renderSessionIdentity(identity, document.getElementById('topbarUser'));
        isAdmin = hasRole(identity, 'Admin');
        access = governanceAccess(identity);
    } catch {
        // The closed position stands.
    }
    for (const id of revealedGovernanceNav(access)) box(id).style.display = '';

    // ── Who owns the main area ────────────────────────────────────────────────
    let returnTo: { id: number; name: string } | null = null;
    let lineage: ReturnType<typeof createLineageCatalog> | null = null;
    let quarantine: ReturnType<typeof createDataQualityQueue> | null = null;
    let governance: ReturnType<typeof createGovernancePortal> | null = null;
    function closeGovernance(keep?: 'lineage' | 'quarantine' | 'governance') {
        if (keep !== 'lineage') { lineage?.dispose(); lineage = null; }
        if (keep !== 'quarantine') { quarantine?.dispose(); quarantine = null; }
        if (keep !== 'governance') { governance?.dispose(); governance = null; }
    }
    function takeOver(owner: Owner) {
        if (owner !== 'library') library.leave();
        if (owner !== 'viewer') viewer.leave();
        if (owner !== 'subscriptions') subscriptions.leave();
        if (owner !== 'governance') closeGovernance();
    }

    function markSidebar(view: 'library' | 'favorites' | 'recent' | 'subscriptions' | 'governance', mode: GovernanceMode = 'lineage') {
        const inGovernance = view === 'governance';
        box('sidebar').style.display = '';
        box('reportsSidebarSection').style.display = inGovernance ? 'none' : '';
        box('governanceSidebarSection').style.display = inGovernance ? '' : 'none';
        const active = inGovernance
            ? `govNav${(mode === 'impact' ? 'lineage' : mode).replace(/^./, letter => letter.toUpperCase())}`
            : { library: 'navReportHome', favorites: 'navFavorites', recent: 'navRecent', subscriptions: 'navSubscriptions' }[view];
        for (const item of document.querySelectorAll(`#${inGovernance ? 'governanceSidebarSection' : 'reportsSidebarSection'} .sidebar-nav-item`))
            item.classList.toggle('active', item.id === active);
        document.getElementById('navReports')?.classList.toggle('active', !inGovernance);
        document.getElementById('navGovernance')?.classList.toggle('active', inGovernance);
    }

    function setHash(hash: string) {
        if (window.location.hash !== `#${hash}`) history.replaceState(history.state, '', `#${hash}`);
    }

    function transition(update: () => void) {
        if (!document.startViewTransition) { update(); return; }
        const started = document.startViewTransition(update);
        // Navigating faster than the animation skips the earlier transition and rejects these two
        // with AbortError, which is expected. updateCallbackDone is left alone: a throw there is real.
        started.ready.catch(() => { });
        started.finished.catch(() => { });
    }

    // ── Owners ────────────────────────────────────────────────────────────────
    const library = createReportLibrary({
        root: document, notify, transition,
        entered: view => { takeOver('library'); markSidebar(view); },
        folderOpened: (id, name) => history.pushState({ view: 'folder', folderId: id, folderName: name } satisfies HistoryState, '', window.location.pathname),
        openReport: (id, folder) => { returnTo = folder; void viewer.open(id); },
        previewDocument: id => buildReportFrameDocument(document, id, true)
    });
    const subscriptions = createReaderSubscriptions({
        root: document, notify,
        confirm: (message, options) => feedback.confirm(message, options),
        entered: () => { takeOver('subscriptions'); markSidebar('subscriptions'); }
    });
    const reportState = (id: number, page: string | null): HistoryState =>
        ({ view: 'report', id, page, folderId: returnTo?.id ?? null, folderName: returnTo?.name ?? '' });
    const viewer = createReportViewer({
        root: document, notify, renderDag,
        canDesign: () => canDesign,
        subscribe: id => { void subscriptions.subscribe(id); },
        // A report opened from a link has no folder behind it; Back then returns to the library home.
        back: () => { if (returnTo) void library.showFolder(returnTo.id, returnTo.name); else void library.showHome(); },
        entered: (id, push, page) => {
            takeOver('viewer');
            if (push) history.pushState(reportState(id, page), '', window.location.pathname);
        },
        // userTriggered: the reader chose a tab, so Back returns to the previous one. Otherwise it is
        // an initial load or a restore, which replaces the current entry.
        pageChanged: (id, page, userTriggered) => {
            if (userTriggered) history.pushState(reportState(id, page), '', window.location.href);
            else history.replaceState(reportState(id, page), '', window.location.href);
        }
    });

    // ── Governance ────────────────────────────────────────────────────────────
    // Every governance surface is durable and server-backed: it reads and writes /api/governance/*.
    const defaultMode = () => defaultGovernanceMode(access);
    let impactLinkUsed = false;

    /**
     * A deep link into impact analysis, used by the Orchestrator triage board. Its parameters ride in
     * the query string because the hash route is lowercased, which would corrupt a case-sensitive
     * name. They are applied once and stripped, or every later mode change would snap back to them.
     */
    function applyImpactLink() {
        const params = new URLSearchParams(window.location.search), name = params.get('impactName');
        if (impactLinkUsed || !lineage || !name) return;
        impactLinkUsed = true;
        lineage.showImpact({ kind: params.get('impactKind') || 'table', name, column: params.get('impactColumn') || '',
            direction: params.get('impactDirection') || 'downstream', depth: Number(params.get('impactDepth')) || undefined });
        const url = new URL(window.location.href);
        for (const key of ['impactKind', 'impactName', 'impactColumn', 'impactDirection', 'impactDepth']) url.searchParams.delete(key);
        history.replaceState(history.state, '', `${url.pathname}${url.search}${url.hash}`);
    }

    function showGovernance(requested: string) {
        // A view the reader's role cannot use falls back to lineage, which everyone can.
        const { mode, redirected } = resolveGovernanceRoute(requested, access);
        if (redirected) setHash('governance/lineage');
        library.leave(); viewer.leave(); subscriptions.leave();
        if (isDashboardMode(mode)) {
            closeGovernance('governance');
            governance ??= createGovernancePortal({ host: main, governanceApi, dataQualityApi,
                prepare: tab => markSidebar('governance', tab as GovernanceMode) });
            governance.setTab(mode);
        } else if (mode === 'quarantine') {
            closeGovernance('quarantine');
            quarantine ??= createDataQualityQueue({ host: main, dataQualityApi, prepare: () => markSidebar('governance', 'quarantine') });
            quarantine.show();
        } else {
            closeGovernance('lineage');
            lineage ??= createLineageCatalog({
                host: main, adminApi, catalogApi, renderDag, renderLineageRow, lineageRowsToCsv, allowAudit: isAdmin, timeAgo, formatBuiltAt,
                openReport: (id: number) => { returnTo = null; void viewer.open(id); },
                onModeChange: (next: string) => {
                    const route = next === 'history' ? 'lineage' : next;
                    setHash(`governance/${route}`);
                    markSidebar('governance', route as GovernanceMode);
                },
                prepare: (current: string) => markSidebar('governance', (current === 'history' ? 'lineage' : current) as GovernanceMode)
            });
            lineage.setMode(mode === 'lineage' ? 'history' : mode);
            applyImpactLink();
        }
    }

    // ── Routing ───────────────────────────────────────────────────────────────
    function openHashView() {
        const [view = '', sub = ''] = window.location.hash.replace(/^#/, '').toLowerCase().split('/');
        if (view === 'subscriptions') void subscriptions.show();
        else if (view === 'recent') void library.showRecent();
        else if (view === 'favorites') void library.showFavorites();
        else if (view === 'governance' || view === 'lineage')
            showGovernance(sub);
        else if (/^report-\d+$/.test(view)) { returnTo = null; void viewer.open(Number(view.slice('report-'.length))); }
        else void library.showHome();
    }

    const nav = (id: string, hash: string | null, show: () => void) => document.getElementById(id)?.addEventListener('click', event => {
        event.preventDefault();
        if (hash === null) history.replaceState(history.state, '', window.location.pathname); else setHash(hash);
        show();
    }, { signal: lifetime.signal });
    nav('navReportHome', null, () => { void library.showHome(); });
    nav('navSubscriptions', 'subscriptions', () => { void subscriptions.show(); });
    nav('navRecent', 'recent', () => { void library.showRecent(); });
    nav('navFavorites', 'favorites', () => { void library.showFavorites(); });
    nav('navGovernance', `governance/${defaultMode()}`, () => showGovernance(defaultMode()));
    for (const [name, mode] of GOVERNANCE_NAV) nav(`govNav${name}`, `governance/${mode}`, () => showGovernance(mode));
    document.getElementById('logoutBtn')?.addEventListener('click', () => { void authApi.logout(); }, { signal: lifetime.signal });

    // Back and forward restore the folder or report the entry was made for.
    window.addEventListener('popstate', event => {
        const state = event.state as HistoryState | null;
        if (state?.view === 'folder' && state.folderId) {
            viewer.activePage = null;
            void library.showFolder(state.folderId, state.folderName ?? '', false);
        } else if (state?.view === 'report' && state.id) {
            returnTo = state.folderId ? { id: state.folderId, name: state.folderName ?? '' } : null;
            viewer.activePage = state.page ?? null;
            void viewer.open(state.id, false);
        }
    }, { signal: lifetime.signal });
    window.addEventListener('hashchange', openHashView, { signal: lifetime.signal });

    // A dialog closes on a click outside it and on Escape, topmost first.
    const overlays = () => Array.from(document.querySelectorAll<HTMLElement>('.modal-overlay'));
    document.addEventListener('click', event => {
        if (event.target instanceof HTMLElement && event.target.classList.contains('modal-overlay')) event.target.style.display = 'none';
    }, { signal: lifetime.signal });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        const open = overlays().reverse().find(overlay => overlay.style.display !== 'none');
        if (open) open.style.display = 'none';
    }, { signal: lifetime.signal });

    window.addEventListener('pagehide', () => {
        lifetime.abort();
        closeGovernance();
        for (const owner of [library, viewer, subscriptions]) owner.dispose();
    }, { once: true });
    // A page restored from the back/forward cache has disposed owners; load it again instead.
    window.addEventListener('pageshow', event => { if (event.persisted) window.location.reload(); });

    // The top-level entries — Admin, Orchestrator, Docs, Studio — come from one server answer. Whether
    // *this page* offers authoring actions is asked separately; a failed answer means it does not.
    await applyNavigationSafely();
    try {
        const session: unknown = await studioApi.session();
        const capabilities = isObject(session) && Array.isArray(session.capabilities) ? session.capabilities : [];
        canDesign = capabilities.includes('ScriptRead') && capabilities.includes('ScriptSave');
    } catch { canDesign = false; }
    if (lifetime.signal.aborted) return;
    await library.loadFolders();
    if (!lifetime.signal.aborted) openHashView();
}

void mountCatalogPage();
