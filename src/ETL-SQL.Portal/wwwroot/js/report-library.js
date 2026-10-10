/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/report-library.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { apiFetch, responseError } from './portal-http.js';
import { asError } from './http-error.js';
import { filterReports, readConsumerHome, readFolderTree, readReports, readSearchResults, renderCatalogList, renderFailure, renderFolderGrid, renderFolderToolbar, renderFolderTree, renderHome, renderSearchResults, sortReports } from './report-library-view.js';
async function readJson(url, fallback, signal) {
    const response = await apiFetch(url, { signal, cache: 'no-store' });
    if (response.status !== 200)
        throw await responseError(response, fallback);
    return response.json();
}
export function createReportLibrary(options) {
    const { root } = options;
    const lifetime = new AbortController();
    const box = (id) => {
        const found = root.getElementById(id);
        if (!found)
            throw new Error(`Missing report library element: ${id}`);
        return found;
    };
    const main = box('mainContent'), tree = box('folderTree');
    const favoriting = new Set();
    let view = null;
    let scope = new AbortController();
    let reports = [];
    let term = '', sort = 'name';
    let search = null;
    scope.abort();
    const live = (opened) => !lifetime.signal.aborted && opened === scope && !opened.signal.aborted;
    const busy = (label) => `<div class="loading-state"><span class="spinner"></span><span>${label}</span></div>`;
    function enter(next) {
        scope.abort();
        search?.abort();
        view = next;
        reports = [];
        options.entered(next.kind === 'favorites' ? 'favorites' : next.kind === 'recent' ? 'recent' : 'library');
        for (const item of tree.querySelectorAll('.folder-item')) {
            const active = next.kind === 'folder' && Number(item.dataset.id) === next.id;
            item.classList.toggle('active', active);
            item.setAttribute('aria-selected', String(active));
        }
        return scope = new AbortController();
    }
    /** Shows one view from one read; a failure offers Retry for that same view. */
    async function show(next, label, failure, load) {
        if (lifetime.signal.aborted)
            return;
        const opened = enter(next);
        main.innerHTML = busy(label);
        try {
            const draw = await load(opened.signal);
            if (live(opened))
                options.transition(() => { if (live(opened))
                    draw(); });
        }
        catch (problem) {
            if (live(opened))
                main.innerHTML = renderFailure(failure, asError(problem).message);
        }
    }
    function drawFolderGrid() {
        const shown = sortReports(filterReports(reports, term), sort);
        box('reportGridHost').innerHTML = renderFolderGrid(reports, shown);
        box('reportCount').textContent = `${shown.length} of ${reports.length} report${reports.length === 1 ? '' : 's'}`;
    }
    const showHome = () => show({ kind: 'home' }, 'Loading your report home…', 'Report home did not load', async (signal) => {
        const home = readConsumerHome(await readJson('/api/catalog/consumer-home?limit=8', 'The report home is unavailable.', signal));
        return () => { main.innerHTML = renderHome(home); };
    });
    function showFolder(id, name, push = true) {
        if (push && !lifetime.signal.aborted)
            options.folderOpened(id, name);
        return show({ kind: 'folder', id, name }, 'Loading reports…', 'Reports did not load', async (signal) => {
            const loaded = readReports(await readJson(`/api/folders/${id}/reports`, 'This folder is unavailable.', signal));
            return () => {
                reports = loaded;
                term = '';
                sort = 'name';
                main.innerHTML = renderFolderToolbar(name, loaded.length, sort);
                drawFolderGrid();
            };
        });
    }
    const catalogList = (kind) => {
        const title = kind === 'favorites' ? 'Favorites' : 'Recently Viewed';
        return show({ kind }, kind === 'favorites' ? 'Loading favorite reports…' : 'Loading recent reports…', kind === 'favorites' ? 'Favorites did not load' : 'Recent reports did not load', async (signal) => {
            const loaded = readReports(await readJson(kind === 'favorites' ? '/api/catalog/favorites?limit=50' : '/api/catalog/recent?limit=30', `${title} are unavailable.`, signal));
            return () => {
                reports = loaded;
                main.innerHTML = renderCatalogList(title, loaded, kind === 'favorites'
                    ? 'Mark reports as favorites and they will appear here.' : 'Open a report snapshot and it will appear here.');
            };
        });
    };
    function refresh() {
        if (!view)
            return;
        if (view.kind === 'home')
            void showHome();
        else if (view.kind === 'folder')
            void showFolder(view.id, view.name, false);
        else
            void catalogList(view.kind);
    }
    async function runSearch(query) {
        const results = root.getElementById('globalSearchResults'), sections = root.getElementById('consumerHomeSections');
        const opened = scope;
        if (!results || !sections || !live(opened))
            return;
        search?.abort();
        const wanted = query.trim();
        if (!wanted) {
            results.replaceChildren();
            sections.hidden = false;
            return;
        }
        const request = search = new AbortController();
        const current = () => live(opened) && search === request;
        sections.hidden = true;
        results.innerHTML = '<div class="loading-state loading-state-compact"><span class="spinner"></span><span>Searching the catalog…</span></div>';
        try {
            const found = readSearchResults(await readJson(`/api/catalog/search?q=${encodeURIComponent(wanted)}&limit=50`, 'Catalog search is unavailable.', request.signal));
            if (current())
                results.innerHTML = renderSearchResults(wanted, found);
        }
        catch (problem) {
            if (current())
                results.innerHTML = `<div class="empty-state empty-state-panel empty-state-error" role="alert">
                <h3>Search did not complete</h3><p></p></div>`;
            if (current())
                results.querySelector('p').textContent = asError(problem).message;
        }
    }
    /** Sets or clears a favorite once per report at a time, and redraws the views that list favorites. */
    async function toggleFavorite(button) {
        const id = Number(button.dataset.favoriteId), add = !button.classList.contains('is-active'), opened = scope;
        if (!Number.isSafeInteger(id) || id <= 0 || favoriting.has(id))
            return;
        favoriting.add(id);
        button.disabled = true;
        try {
            const response = await apiFetch(`/api/reports/${id}/favorite`, { method: add ? 'POST' : 'DELETE', signal: lifetime.signal });
            if (!response.ok)
                throw await responseError(response, 'Favorite update failed.');
            if (!live(opened))
                return;
            if (view?.kind === 'folder') {
                for (const report of reports)
                    if (report.id === id)
                        report.isFavorite = add;
                button.classList.toggle('is-active', add);
                button.textContent = add ? '★' : '☆';
                button.title = add ? 'Remove favorite' : 'Add favorite';
                button.setAttribute('aria-pressed', String(add));
            }
            else {
                const query = root.getElementById('globalSearchResults')?.childElementCount
                    ? root.getElementById('globalReportSearchInput')?.value ?? '' : '';
                // Home, favorites, and recent all list favorites, so they are read again rather than patched.
                if (query)
                    void runSearch(query);
                else
                    refresh();
            }
        }
        catch (problem) {
            if (!lifetime.signal.aborted)
                options.notify(asError(problem).message, { title: 'Favorite not changed', tone: 'error' });
        }
        finally {
            favoriting.delete(id);
            if (button.isConnected)
                button.disabled = false;
        }
    }
    main.addEventListener('click', event => {
        if (scope.signal.aborted || !(event.target instanceof Element))
            return;
        const star = event.target.closest('button[data-favorite-id]');
        if (star) {
            event.preventDefault();
            void toggleFavorite(star);
            return;
        }
        const action = event.target.closest('[data-library-action]')?.dataset.libraryAction;
        if (action === 'refresh') {
            refresh();
            return;
        }
        if (action === 'clear-search') {
            const input = root.getElementById('globalReportSearchInput');
            if (input instanceof HTMLInputElement) {
                input.value = '';
                input.focus();
            }
            void runSearch('');
            return;
        }
        const folder = event.target.closest('.consumer-folder-result');
        if (folder) {
            void showFolder(Number(folder.dataset.folderId), folder.dataset.folderName ?? '');
            return;
        }
        const card = event.target.closest('[data-report-id]');
        if (!card)
            return;
        event.preventDefault();
        const folderId = Number(card.dataset.folderId);
        // A card in a folder returns to that folder; a catalog card returns to the folder it lives in.
        const origin = view?.kind === 'folder' ? { id: view.id, name: view.name }
            : folderId > 0 ? { id: folderId, name: card.dataset.folderPath || 'Reports' } : null;
        options.openReport(Number(card.dataset.reportId), origin);
    }, { signal: lifetime.signal });
    main.addEventListener('submit', event => {
        if (scope.signal.aborted || !(event.target instanceof HTMLFormElement) || event.target.id !== 'globalReportSearch')
            return;
        event.preventDefault();
        const input = root.getElementById('globalReportSearchInput');
        void runSearch(input instanceof HTMLInputElement ? input.value : '');
    }, { signal: lifetime.signal });
    main.addEventListener('input', event => {
        if (scope.signal.aborted || view?.kind !== 'folder' || !(event.target instanceof HTMLInputElement) || event.target.id !== 'reportSearchInput')
            return;
        term = event.target.value;
        drawFolderGrid();
    }, { signal: lifetime.signal });
    main.addEventListener('change', event => {
        if (scope.signal.aborted || view?.kind !== 'folder' || !(event.target instanceof HTMLSelectElement) || event.target.id !== 'reportSortSelect')
            return;
        sort = event.target.value === 'recent' || event.target.value === 'stale' ? event.target.value : 'name';
        drawFolderGrid();
    }, { signal: lifetime.signal });
    // A report's live preview is built the first time its card is hovered or focused, not for every card up front.
    const preview = (event) => {
        const host = event.target instanceof Element ? event.target.closest('.report-card')?.querySelector('.report-preview') : null;
        if (scope.signal.aborted || !host || host.querySelector('iframe'))
            return;
        const frame = root.createElement('iframe');
        frame.title = `${host.closest('.report-card')?.querySelector('h3')?.textContent ?? 'Report'} preview`;
        frame.loading = 'lazy';
        frame.srcdoc = options.previewDocument(Number(host.dataset.previewId));
        host.replaceChildren(frame);
    };
    main.addEventListener('mouseover', preview, { signal: lifetime.signal });
    main.addEventListener('focusin', preview, { signal: lifetime.signal });
    const openFolderItem = (target) => {
        const item = target instanceof Element ? target.closest('.folder-item') : null;
        if (item)
            void showFolder(Number(item.dataset.id), item.dataset.folderName ?? '');
        return !!item;
    };
    tree.addEventListener('click', event => {
        if (event.target instanceof Element && event.target.closest('[data-library-action="reload-folders"]'))
            void loadFolders();
        else
            openFolderItem(event.target);
    }, { signal: lifetime.signal });
    tree.addEventListener('keydown', event => {
        if ((event.key === 'Enter' || event.key === ' ') && openFolderItem(event.target))
            event.preventDefault();
    }, { signal: lifetime.signal });
    async function loadFolders() {
        try {
            const folders = readFolderTree(await readJson('/api/folders', 'Folders are unavailable.', lifetime.signal));
            if (lifetime.signal.aborted)
                return;
            tree.innerHTML = folders.length ? renderFolderTree(folders) : '<div class="sidebar-note">No folders yet.</div>';
            if (view?.kind === 'folder')
                tree.querySelector(`.folder-item[data-id="${view.id}"]`)?.classList.add('active');
        }
        catch (problem) {
            if (lifetime.signal.aborted)
                return;
            tree.innerHTML = '<div class="sidebar-note sidebar-note-error" role="alert"><span></span> <button class="btn btn-outline btn-sm" type="button" data-library-action="reload-folders">Retry</button></div>';
            tree.querySelector('span').textContent = `Folders did not load: ${asError(problem).message}`;
        }
    }
    return {
        loadFolders, showHome, showFolder,
        showFavorites: () => catalogList('favorites'),
        showRecent: () => catalogList('recent'),
        /** The folder a report was opened from, if the library is showing one. */
        get folder() { return view?.kind === 'folder' ? { id: view.id, name: view.name } : null; },
        /** Another view owns the main area now; a library read still in flight must not repaint it. */
        leave() {
            scope.abort();
            search?.abort();
            view = null;
            for (const item of tree.querySelectorAll('.folder-item.active')) {
                item.classList.remove('active');
                item.setAttribute('aria-selected', 'false');
            }
        },
        dispose() { scope.abort(); search?.abort(); lifetime.abort(); }
    };
}
