import { isObject } from './http-error.js';
import { escapeHtml as esc, timeAgo } from './report-format.js';

/** Response readers, ordering, and markup for the report library. Pure, so they can be checked without a browser. */
export interface LibraryFolder { id: number; name: string; path: string; children: LibraryFolder[] }
export interface LibraryReport {
    id: number; folderId: number | null; name: string; description: string; path: string; isFavorite: boolean; isStale: boolean;
    scriptChanged: boolean; perViewer: boolean; certification: string; category: string; domain: string; snapshotBuiltAt: string | null;
    lastViewedAt: string | null; lastRefreshStatus: string; lastRefreshError: string; lastRefreshCompletedAt: string | null;
}
export interface ConsumerHome { favorites: LibraryReport[]; recent: LibraryReport[]; featured: LibraryReport[]; popular: LibraryReport[] }
export interface SearchResults { folders: Array<{ id: number; name: string; path: string }>; reports: LibraryReport[]; total: number }
export type SortMode = 'name' | 'recent' | 'stale';

const identifier = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const text = (value: unknown): string => typeof value === 'string' ? value : '';
const date = (value: unknown): string | null => typeof value === 'string' && Number.isFinite(Date.parse(value)) ? value : null;

/** The folder tree as the server sends it: roots, each carrying its own children. */
export function readFolderTree(value: unknown, depth = 0): LibraryFolder[] {
    const failure = new TypeError('The folder list is unavailable or incomplete.');
    if (!Array.isArray(value) || depth > 32) throw failure;
    return value.map((item: unknown) => {
        if (!isObject(item) || !identifier(item.id) || typeof item.name !== 'string' || !item.name) throw failure;
        return { id: item.id, name: item.name, path: text(item.path), children: readFolderTree(item.children ?? [], depth + 1) };
    });
}

function readReport(item: unknown, failure: Error): LibraryReport {
    if (!isObject(item) || !identifier(item.id) || typeof item.name !== 'string') throw failure;
    return {
        id: item.id, folderId: identifier(item.folderId) ? item.folderId : null, name: item.name, description: text(item.description),
        path: text(item.path), isFavorite: item.isFavorite === true, isStale: item.isStale === true, scriptChanged: item.scriptChanged === true,
        perViewer: item.perViewer === true, certification: text(item.certification), category: text(item.category), domain: text(item.domain),
        snapshotBuiltAt: date(item.snapshotBuiltAt), lastViewedAt: date(item.lastViewedAt), lastRefreshStatus: text(item.lastRefreshStatus),
        lastRefreshError: text(item.lastRefreshError), lastRefreshCompletedAt: date(item.lastRefreshCompletedAt)
    };
}

export function readReports(value: unknown, subject = 'report list'): LibraryReport[] {
    const failure = new TypeError(`The ${subject} is unavailable or incomplete.`);
    if (!Array.isArray(value)) throw failure;
    return value.map((item: unknown) => readReport(item, failure));
}

export function readConsumerHome(value: unknown): ConsumerHome {
    if (!isObject(value)) throw new TypeError('The report home is unavailable or incomplete.');
    const section = (items: unknown) => readReports(items ?? [], 'report home');
    return { favorites: section(value.favorites), recent: section(value.recent), featured: section(value.featured), popular: section(value.popular) };
}

export function readSearchResults(value: unknown): SearchResults {
    const failure = new TypeError('The search results are unavailable or incomplete.');
    if (!Array.isArray(value)) throw failure;
    const kind = (item: unknown) => isObject(item) ? text(item.type).toLowerCase() : '';
    return {
        total: value.length,
        reports: value.filter(item => kind(item) === 'report').map(item => readReport(item, failure)),
        folders: value.filter(item => kind(item) === 'folder').map((item: unknown) => {
            if (!isObject(item) || !identifier(item.id) || typeof item.name !== 'string') throw failure;
            return { id: item.id, name: item.name, path: text(item.path) };
        })
    };
}

export function filterReports(reports: LibraryReport[], term: string): LibraryReport[] {
    const wanted = term.trim().toLowerCase();
    return wanted ? reports.filter(report => report.name.toLowerCase().includes(wanted) || report.description.toLowerCase().includes(wanted)) : reports;
}

export function sortReports(reports: LibraryReport[], mode: SortMode): LibraryReport[] {
    const built = (report: LibraryReport) => report.snapshotBuiltAt ? Date.parse(report.snapshotBuiltAt) : 0;
    return [...reports].sort((a, b) => mode === 'recent' ? built(b) - built(a)
        : mode === 'stale' ? Number(b.isStale) - Number(a.isStale) || built(b) - built(a)
        : a.name.localeCompare(b.name));
}

export function statusBadge(report: LibraryReport): string {
    if (report.lastRefreshStatus === 'Failed')
        return `<span class="badge badge-error" title="${esc(report.lastRefreshError || 'Last refresh failed')}">Failed</span>`;
    if (report.lastRefreshStatus === 'Cancelled') return '<span class="badge badge-warning">Cancelled</span>';
    if (report.lastRefreshStatus === 'Running') return '<span class="badge badge-running">Refreshing</span>';
    if (report.perViewer) return '<span class="badge" title="Runs with your permissions each time you open it">Live for you</span>';
    return report.isStale ? '<span class="badge badge-stale">Stale</span>' : '';
}

export function activityLine(report: LibraryReport, now = Date.now()): string {
    const viewed = report.lastViewedAt ? Date.parse(report.lastViewedAt) : 0, built = report.snapshotBuiltAt ? Date.parse(report.snapshotBuiltAt) : 0;
    if (report.lastRefreshStatus === 'Failed')
        return report.lastRefreshCompletedAt ? `Run failed ${timeAgo(report.lastRefreshCompletedAt, now)}` : 'Last run failed';
    if (report.lastRefreshStatus === 'Running') return 'Running now';
    if (report.lastRefreshStatus === 'Cancelled')
        return report.lastRefreshCompletedAt ? `Run cancelled ${timeAgo(report.lastRefreshCompletedAt, now)}` : 'Last run cancelled';
    if (report.perViewer) return 'Runs with your permissions';
    if (viewed >= built && viewed > 0) return `Viewed ${timeAgo(report.lastViewedAt, now)}`;
    return built > 0 ? `Updated ${timeAgo(report.snapshotBuiltAt, now)}` : 'Ready for first run';
}

const folderPath = (report: LibraryReport) => report.path ? report.path.replace(/\/[^/]*$/, '') : '';
const favorite = (report: LibraryReport) => `<button class="favorite-btn ${report.isFavorite ? 'is-active' : ''}" data-favorite-id="${report.id}"
    aria-pressed="${report.isFavorite}" title="${report.isFavorite ? 'Remove favorite' : 'Add favorite'}" type="button">${report.isFavorite ? '★' : '☆'}</button>`;
const link = (report: LibraryReport) => `<a href="#report-${report.id}" class="report-card-link" data-id="${report.id}">${esc(report.name)}</a>`;
/** Every card names its report and the folder Back should return to. */
const origin = (report: LibraryReport) => `data-report-id="${report.id}" data-id="${report.id}" data-folder-id="${report.folderId ?? ''}" data-folder-path="${esc(folderPath(report))}"`;

export function renderConsumerCard(report: LibraryReport): string {
    const glyph = (report.category || report.domain || report.name || 'R').trim().charAt(0).toUpperCase() || 'R';
    return `<article class="consumer-report-card" ${origin(report)}>
      <div class="consumer-report-icon" aria-hidden="true">${esc(glyph)}</div>
      <div class="consumer-report-copy"><div class="report-card-title-row"><h4>${link(report)}</h4>${favorite(report)}</div>
        <p>${esc(report.description || report.path || 'No description provided.')}</p>
        <div class="consumer-report-footer">${statusBadge(report)}<span>${esc(activityLine(report))}</span></div></div></article>`;
}

function consumerSection(title: string, reports: LibraryReport[], none: string): string {
    const id = `consumer-${title.replace(/\s+/g, '-').toLowerCase()}`;
    return `<section class="consumer-section" aria-labelledby="${id}">
      <div class="consumer-section-header"><h3 id="${id}">${esc(title)}</h3><span>${reports.length} report${reports.length === 1 ? '' : 's'}</span></div>
      ${reports.length ? `<div class="consumer-card-grid">${reports.map(renderConsumerCard).join('')}</div>` : `<p class="consumer-section-empty">${esc(none)}</p>`}</section>`;
}

export function renderHome(home: ConsumerHome): string {
    return `<section class="consumer-home" aria-labelledby="consumerHomeTitle">
      <div class="consumer-home-hero">
        <div><span class="library-kicker">Report catalog</span><h2 id="consumerHomeTitle">Find the report you need</h2>
          <p>Search every folder, description, tag, owner, domain, steward, certification, and lineage term.</p></div>
        <form class="global-report-search" id="globalReportSearch" role="search">
          <label for="globalReportSearchInput">Search the report catalog</label>
          <div><input id="globalReportSearchInput" type="search" placeholder="Revenue, customer, steward, table…" autocomplete="off">
            <button class="btn btn-primary" type="submit">Search</button></div></form>
      </div>
      <div id="consumerHomeSections">
        ${consumerSection('Favorites', home.favorites, 'Favorite reports appear here for quick return.')}
        ${consumerSection('Recently viewed', home.recent, 'Reports you open appear here.')}
        ${consumerSection('Featured', home.featured, 'Certified and stewarded reports appear here.')}
        ${consumerSection('Popular', home.popular, 'Frequently used reports appear here.')}
      </div>
      <div id="globalSearchResults" aria-live="polite"></div></section>`;
}

export function renderSearchResults(query: string, results: SearchResults): string {
    return `<section class="consumer-search-results"><div class="consumer-section-header"><div><span class="library-kicker">Search results</span>
        <h3>${results.total} match${results.total === 1 ? '' : 'es'} for “${esc(query)}”</h3></div>
        <button class="btn btn-outline" id="clearGlobalSearchBtn" type="button" data-library-action="clear-search">Clear</button></div>
      ${results.folders.length ? `<div class="consumer-folder-results">${results.folders.map(folder => `<button type="button" class="consumer-folder-result"
        data-folder-id="${folder.id}" data-folder-name="${esc(folder.name)}"><span class="folder-icon folder-icon-parent" aria-hidden="true"></span>
        <span><strong>${esc(folder.name)}</strong><small>${esc(folder.path)}</small></span></button>`).join('')}</div>` : ''}
      ${results.reports.length ? `<div class="consumer-card-grid">${results.reports.map(renderConsumerCard).join('')}</div>` : ''}
      ${results.total === 0 ? `<div class="empty-state empty-state-panel"><div class="empty-state-icon empty-state-icon-search" aria-hidden="true"></div>
        <h3>No catalog matches</h3><p>Try a broader name, tag, owner, domain, steward, or source table.</p></div>` : ''}</section>`;
}

export function renderFolderToolbar(folderName: string, total: number, sort: SortMode): string {
    const option = (value: SortMode, label: string) => `<option value="${value}"${sort === value ? ' selected' : ''}>${label}</option>`;
    return `<div class="library-toolbar">
      <div class="library-title"><span class="library-kicker">Folder</span><h2>${esc(folderName)}</h2></div>
      <span class="badge badge-ok" id="reportCount"></span><div class="library-toolbar-spacer"></div>
      ${total ? `<label class="library-search"><span class="search-icon" aria-hidden="true"></span>
        <input id="reportSearchInput" type="search" placeholder="Search reports" aria-label="Search reports in this folder"></label>
        <select id="reportSortSelect" class="library-sort" aria-label="Sort reports">${option('name', 'Name')}${option('recent', 'Last run')}${option('stale', 'Needs refresh')}</select>` : ''}
      <button class="btn btn-outline" id="refreshReportsBtn" type="button" data-library-action="refresh">Refresh</button></div>
      <div id="reportGridHost"></div>`;
}

/** The cards for one folder after search and sort; the toolbar above them is left alone so typing keeps its place. */
export function renderFolderGrid(all: LibraryReport[], shown: LibraryReport[]): string {
    if (!all.length) return `<div class="empty-state empty-state-panel"><div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
        <h2>No reports here yet</h2><p>This folder is ready. Publish a report into it from Admin or the ETL-SQL publish workflow.</p></div>`;
    if (!shown.length) return `<div class="empty-state empty-state-panel"><div class="empty-state-icon empty-state-icon-search" aria-hidden="true"></div>
        <h2>No matching reports</h2><p>Clear the search box or try a broader report name or description.</p></div>`;
    return `<div class="report-grid">${shown.map(report => `<div class="report-card" ${origin(report)}>
        <div class="report-preview" data-preview-id="${report.id}"><div class="report-preview-placeholder"><div class="mock-header"></div>
          <div class="mock-grid"><div class="mock-widget">📊</div><div class="mock-widget">📈</div></div><div class="mock-footer"></div></div></div>
        <div class="report-card-body"><div class="report-card-title-row"><span class="report-glyph" aria-hidden="true"></span><h3>${link(report)}</h3>${favorite(report)}</div>
          <p>${esc(report.description || 'No description provided.')}</p>
          <div class="report-status-row">${statusBadge(report)}${report.scriptChanged ? '<span class="badge badge-warning">Script changed</span>' : ''}
            <span class="report-activity">${esc(activityLine(report))}</span></div></div></div>`).join('')}</div>`;
}

export function renderCatalogList(title: string, reports: LibraryReport[], none: string): string {
    return `<div class="library-toolbar"><div class="library-title"><span class="library-kicker">Catalog</span><h2>${esc(title)}</h2></div>
        <span class="badge badge-refresh">${reports.length} report${reports.length === 1 ? '' : 's'}</span><div class="library-toolbar-spacer"></div>
        <button class="btn btn-outline" id="refreshCatalogBtn" type="button" data-library-action="refresh">Refresh</button></div>
      ${reports.length ? `<div class="catalog-report-list">${reports.map(report => `<div class="catalog-report-row" ${origin(report)}>
        <span class="report-glyph" aria-hidden="true"></span>
        <div class="catalog-report-main"><div class="report-card-title-row"><h3>${link(report)}</h3>${favorite(report)}</div>
          <p>${esc(report.description || report.path || 'No description provided.')}</p>
          <div class="report-status-row">${statusBadge(report)}${report.certification ? `<span class="badge badge-ok">${esc(report.certification)}</span>` : ''}
            ${report.scriptChanged ? '<span class="badge badge-warning">Script changed</span>' : ''}</div></div>
        <div class="catalog-report-meta"><span>${esc(activityLine(report))}</span></div></div>`).join('')}</div>`
        : `<div class="empty-state empty-state-panel"><div class="empty-state-icon empty-state-icon-report" aria-hidden="true"></div>
          <h2>No ${esc(title.toLowerCase())} reports</h2><p>${esc(none)}</p></div>`}`;
}

export function renderFailure(title: string, reason: string): string {
    return `<div class="empty-state empty-state-panel empty-state-error" role="alert">
      <div class="empty-state-icon empty-state-icon-alert" aria-hidden="true"></div><h2>${esc(title)}</h2><p>${esc(reason)}</p>
      <button class="btn btn-outline" type="button" data-library-action="refresh">Retry</button></div>`;
}

/** The sidebar tree. Children are nested inside their parent's group, so a subfolder is reachable. */
export function renderFolderTree(folders: LibraryFolder[], depth = 0): string {
    return folders.map(folder => `<div class="folder-item" role="treeitem" tabindex="0" aria-selected="false"${folder.children.length ? ' aria-expanded="true"' : ''}
        data-id="${folder.id}" data-folder-name="${esc(folder.name)}" style="margin-left:${depth * 10}px">
        <span class="folder-icon ${folder.children.length ? 'folder-icon-parent' : 'folder-icon-leaf'}" aria-hidden="true"></span>
        <span class="folder-name">${esc(folder.name)}</span></div>
      ${folder.children.length ? `<div class="folder-children" role="group">${renderFolderTree(folder.children, depth + 1)}</div>` : ''}`).join('');
}
