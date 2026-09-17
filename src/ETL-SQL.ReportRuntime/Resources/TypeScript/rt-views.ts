/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Bookmarks, saved views, and offline state restoration.
 */
import { _uiStates, apiBase, feedback, getLastManifest, isOfflineHost, isWebMode, parameters, vscode } from './rt-state.js';
import { renderManifest } from './report-runtime.js';
import { getActivePageName, navigateToPage } from './rt-actions.js';
import { _postParametersInternal, savedViewsRequest } from './rt-transport.js';

interface ViewState {
    schemaVersion?: number;
    activePage?: string | null;
    parameters?: Record<string, unknown>;
    visible?: Record<string, boolean>;
    collapsed?: Record<string, boolean>;
    [key: string]: unknown;
}

interface ViewOptions {
    hash?: string;
}

interface Bookmark {
    name: string;
    title?: string;
    state?: ViewState;
    isDefault?: boolean;
    [key: string]: unknown;
}

interface SavedView {
    id: string;
    name: string;
    isDefault?: boolean;
    driftWarning?: string;
    [key: string]: unknown;
}

interface ViewManifest {
    bookmarks?: Bookmark[];
    appliedState?: ViewState;
    stateWarnings?: string[];
    error?: string;
    [key: string]: unknown;
}

interface ViewHash {
    bookmark: string | null;
    view: string | null;
}

// Parses an identifier-only state hash. Returns { bookmark, view } with at most one set.
export function parseStateHash(hash?: string | null): ViewHash {
    const result: ViewHash = { bookmark: null, view: null };
    if (!hash) return result;
    const bm = hash.match(/[#&]bookmark=([^&]+)/);
    if (bm) {
        try { result.bookmark = decodeURIComponent(bm[1]); } catch { /* malformed hash: ignore */ }
        return result;
    }
    const vw = hash.match(/[#&]view=([^&]+)/);
    if (vw) {
        try { result.view = decodeURIComponent(vw[1]); } catch { /* malformed hash: ignore */ }
    }
    return result;
}

export async function applyBookmark(bookmarkName?: string | null): Promise<boolean> {
    if (!bookmarkName || !getLastManifest()) return false;
    const bookmarks: Bookmark[] = ((getLastManifest() as ViewManifest).bookmarks || []);
    const bm = bookmarks.find((b: Bookmark) => b.name.toLowerCase() === bookmarkName.toLowerCase());
    if (!bm) {
        console.warn('Bookmark not found:', bookmarkName);
        if (feedback) feedback.notify('Bookmark not found: ' + bookmarkName, { title: 'Bookmark', tone: 'error' });
        return false;
    }

    const hash = '#bookmark=' + encodeURIComponent(bm.name);

    // Web mode: apply through the server-side atomic operation. The server resolves, validates,
    // reconciles, refreshes affected visuals through the cascading-parameter engine, and publishes
    // ONE manifest carrying the resolved `appliedState`. The client applies that state as one swap.
    if (isWebMode && !vscode && !isOfflineSnapshot()) {
        try {
            const res = await fetch(apiBase + '/bookmark', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ bookmarkName: bm.name })
            });
            if (!res.ok) {
                if (feedback) feedback.notify('Could not apply bookmark.', { title: 'Bookmark', tone: 'error' });
                return false;
            }
            const manifest = (await res.json()) as ViewManifest;
            if (manifest && manifest.error) {
                if (feedback) feedback.notify(manifest.error, { title: 'Bookmark not applied', tone: 'error' });
                return false;
            }
            renderManifest(manifest);
            if (manifest && manifest.appliedState) commitResolvedState(manifest.appliedState, { hash });
            else if (window.history && window.history.replaceState) window.history.replaceState(null, '', hash);
            if (manifest && manifest.stateWarnings && feedback) {
                manifest.stateWarnings.forEach((w: string) => feedback!.notify(w, { title: 'Saved view', tone: 'warning' }));
            }
            return true;
        } catch (e) {
            console.warn('Bookmark application request failed:', e && (e as { message?: unknown }).message);
            if (feedback) feedback.notify('Could not apply bookmark.', { title: 'Bookmark', tone: 'error' });
            return false;
        }
    }

    // VS Code preview / offline snapshot: apply the manifest-carried envelope through the shared
    // client-side atomic contract (no server available).
    return applyResolvedState(bm.state || {}, { hash });
}

// Applies the active page + presentation state from a resolved envelope and writes the
// identifier-only hash. Used after both the server-side and client-side application paths.
function commitResolvedState(state: ViewState, opts?: ViewOptions | null): void {
    opts = opts || {};
    if (state.activePage) navigateToPage(state.activePage);
    applyPresentationState(state);
    if (opts.hash && window.history && window.history.replaceState) {
        // A snapshot opened from disk has an opaque origin, where replaceState throws. The hash is
        // a convenience for sharing a link; failing to write it must not abort the application.
        try {
            window.history.replaceState(null, '', opts.hash);
        } catch (e) {
            console.debug('State hash not written:', e && (e as { message?: unknown }).message);
        }
    }
}

// Reads a named-object VISIBLE/COLLAPSED map from the shared envelope onto the DOM.
function applyPresentationState(state: ViewState): void {
    if (state.visible) {
        Object.entries(state.visible).forEach(([objName, on]) => {
            const el = document.getElementById(objName) || document.querySelector<HTMLElement>(`[data-name="${objName}"]`);
            if (el) {
                el.style.display = on ? '' : 'none';
                _uiStates[objName] = Object.assign({}, _uiStates[objName], { visible: !!on });
            }
        });
    }
    if (state.collapsed) {
        Object.entries(state.collapsed).forEach(([objName, on]) => {
            const el = document.getElementById(objName) || document.querySelector<HTMLElement>(`[data-name="${objName}"]`);
            if (!el) return;
            const container = el.closest('.collapsible-drawer')
                           || el.closest('.collapsible-inline')
                           || el.closest('.report-container') || el;
            const name = container.getAttribute('data-name');
            if (name) _uiStates[name] = Object.assign({}, _uiStates[name], { collapsed: !!on });
            if (on) container.classList.add('collapsed');
            else container.classList.remove('collapsed');
        });
    }
}

// The single atomic application contract shared by author bookmarks, saved views, buttons, and
// URL replay. Parameters are staged and committed as one request; the active page and
// presentation state are applied ONLY after that request succeeds. On failure nothing is
// applied (no partial bookmark), the identifier-only hash is not written, and a warning is shown.
async function applyResolvedState(state: ViewState, opts?: ViewOptions | null): Promise<boolean> {
    opts = opts || {};
    const batch: Record<string, string> = {};
    if (state.parameters) {
        Object.entries(state.parameters).forEach(([k, v]) => {
            const paramName = k.startsWith('@') ? k : '@' + k;
            // Typed values arrive as JS number/boolean/string/null; project to the string the API expects.
            batch[paramName] = (v === null || v === undefined) ? '' : String(v);
        });
    }

    const commit = () => commitResolvedState(state, opts);

    if (Object.keys(batch).length === 0) {
        commit();
        return true;
    }

    // Offline snapshot / VS Code preview: apply parameters locally, then commit page/state.
    if (typeof applyParametersOffline === 'function' && isOfflineSnapshot()) {
        const ok = await applyParametersOffline(batch);
        if (!ok) {
            console.warn('Offline parameter application failed; bookmark not applied.');
            if (feedback) feedback.notify('Could not apply bookmark offline.', { title: 'Bookmark', tone: 'error' });
            return false;
        }
        commit();
        return true;
    }

    const manifest = await _postParametersInternal(batch, false, getActivePageName());
    if (manifest) {
        renderManifest(manifest);
        commit();
        return true;
    }

    if (vscode) {
        // VS Code host applies parameters and re-renders asynchronously; commit page/state after.
        commit();
        return true;
    }

    // Web mode: the parameter request failed — do not partially apply the bookmark.
    console.warn('Parameter application failed; bookmark/view not applied.');
    if (feedback) feedback.notify('Could not apply the requested state.', { title: 'Bookmark', tone: 'error' });
    return false;
}

// Offline snapshots set window.__ETLSNAP__; overridden by the snapshot bootstrap when present.
// Re-read rather than returning the boot-time `isOfflineHost`, so a host that sets the flag after
// the runtime script has been parsed still gets offline behaviour from every later decision.
export function isOfflineSnapshot(): boolean {
    return isOfflineHost
        || !!(window.__ETLSNAP__ || (typeof window.__OFFLINE__ !== 'undefined' && window.__OFFLINE__));
}

/**
 * Applies a bookmark's parameter values inside an offline snapshot.
 *
 * A snapshot has no server to re-query, and its rows are frozen at capture time, so the figures
 * cannot move. What can and must replay is everything the author bookmarked that is not data: the
 * parameter values themselves (so the slicers show the state the bookmark describes), the active
 * page, and the VISIBLE/COLLAPSED presentation state — all of which the manifest already carries,
 * because ManifestBuilder resolves each bookmark's typed envelope at build time.
 *
 * The reader is told once, and only once per application, that the figures belong to the snapshot;
 * silently showing bookmarked slicer positions over unchanged numbers would be the more misleading
 * outcome.
 *
 * Returns true when the state was applied, so the caller can commit the page/presentation half.
 */
/**
 * Records parameter values into the snapshot manifest held in memory, without re-rendering.
 *
 * Offline there is no server to re-resolve against, so a parameter change moves the control and
 * leaves the figures where the capture froze them. Separated from the bookmark path because a
 * bookmark announces itself to the reader and an ordinary control change must not.
 */
export function recordParametersOffline(batch?: Record<string, unknown> | null): boolean {
    if (!getLastManifest()) return false;
    getLastManifest().parameters = getLastManifest().parameters || {};
    for (const [name, value] of Object.entries(batch || {})) {
        getLastManifest().parameters[name] = value;
        parameters[name] = value;
    }
    return true;
}

async function applyParametersOffline(batch: Record<string, unknown>): Promise<boolean> {
    if (!getLastManifest()) return false;
    const entries = Object.entries(batch || {});
    if (entries.length === 0) return true;

    if (!recordParametersOffline(batch)) return false;

    // Re-render from the snapshot in memory so the controls reflect the bookmarked values.
    renderManifest(getLastManifest());

    if (feedback) {
        feedback.notify(
            'Applied the bookmark’s filters. Figures come from the saved snapshot and do not change offline.',
            { title: 'Offline snapshot', tone: 'info' });
    }
    return true;
}

// Saved views are a Portal-only, per-user feature. The Portal hosts the runtime with
// __API_BASE__ = '/api/reports/{id}'; the ReportPlayer uses '/reports/{name}/api' and has no
// saved-view API at all. Addressing off the host base (rather than rebuilding '/api/reports/{id}'
// by hand) is what keeps the URL correct in both hosts. Returns null wherever saved views do not
// exist — VS Code preview, the Player, and offline snapshots.
export function savedViewsBase(): string | null {
    if (vscode || isOfflineSnapshot()) return null;
    const base = (window.__API_BASE__ || '').replace(/\/$/, '');
    return /^\/api\/reports\/\d+$/.test(base) ? base + '/saved-views' : null;
}

// Applies one Portal saved view by identifier. Portal mode only. Returns true on success.
// Unknown/unauthorized identifiers resolve to a graceful no-op (base report still opens) and
// never reveal whether another user's view exists.
export async function applySavedView(viewId: string): Promise<boolean> {
    const base = savedViewsBase();
    if (!base) return false;
    try {
        const res = await fetch(`${base}/${encodeURIComponent(viewId)}/apply`, {
            method: 'POST',
            headers: { 'Accept': 'application/json' }
        });
        if (!res.ok) return false;
        const manifest = (await res.json()) as ViewManifest;
        if (!manifest || manifest.error) {
            if (manifest && manifest.error && feedback)
                feedback.notify(manifest.error, { title: 'Saved view not applied', tone: 'error' });
            return false;
        }
        renderManifest(manifest);
        if (manifest.appliedState)
            commitResolvedState(manifest.appliedState, { hash: '#view=' + encodeURIComponent(viewId) });
        if (manifest.stateWarnings && feedback)
            manifest.stateWarnings.forEach((w: string) => feedback!.notify(w, { title: 'Saved view', tone: 'warning' }));
        return true;
    } catch (e) {
        console.warn('Saved view could not be applied:', e && (e as { message?: unknown }).message);
        return false;
    }
}

// Applies the current user's default saved view, if any. Returns true when one was applied.
export async function applyUserDefaultSavedView(): Promise<boolean> {
    const base = savedViewsBase();
    if (!base) return false;
    try {
        const res = await fetch(`${base}/default`, {
            headers: { 'Accept': 'application/json' }
        });
        if (res.status === 204 || !res.ok) return false;
        const view = (await res.json()) as SavedView;
        if (!view || view.id == null) return false;
        return await applySavedView(view.id);
    } catch (e) {
        console.warn('Default saved view could not be applied:', e && (e as { message?: unknown }).message);
        return false;
    }
}

// Captures the current report state into a resolved-state envelope for saving as a view.
// Only identifiers/values needed for replay are captured; the ScriptHash is stamped server-side.
export function captureResolvedState(): ViewState {
    const state: {
        schemaVersion: number;
        activePage: string | null;
        parameters: Record<string, unknown>;
        visible: Record<string, boolean>;
        collapsed: Record<string, boolean>;
    } = {
        schemaVersion: 1,
        activePage: getActivePageName(),
        parameters: {},
        visible: {},
        collapsed: {}
    };
    const params = (getLastManifest() && getLastManifest().parameters) || {};
    const metadata = (getLastManifest() && getLastManifest().parameterMetadata) || {};
    Object.entries(params).forEach(([k, v]) => {
        const name = k.startsWith('@') ? k : '@' + k;
        const metaKey = Object.keys(metadata).find((m: string) => m.toLowerCase() === name.toLowerCase());
        const type = metaKey && metadata[metaKey] ? String(metadata[metaKey].type || '').toUpperCase() : '';
        if (/^(BIT|BOOL|BOOLEAN)$/.test(type) && /^(TRUE|FALSE)$/i.test(String(v)))
            state.parameters[name] = String(v).toUpperCase() === 'TRUE';
        else if (/^(INT|INTEGER|BIGINT|SMALLINT|TINYINT|DECIMAL|NUMERIC|FLOAT|REAL|DOUBLE|MONEY|NUMBER)(\s*\(|$)/.test(type)
            && v !== '' && Number.isFinite(Number(v)))
            state.parameters[name] = Number(v);
        else
            state.parameters[name] = v == null ? null : String(v);
    });
    // Presentation state is tracked as actions/bookmarks are applied.
    Object.entries(_uiStates || {}).forEach(([name, s]) => {
        if (s && typeof s.visible === 'boolean') state.visible[name] = s.visible;
        if (s && typeof s.collapsed === 'boolean') state.collapsed[name] = s.collapsed;
    });
    return state;
}

function listSavedViews(): Promise<SavedView[]> {
    return savedViewsRequest('', { headers: { 'Accept': 'application/json' } })
        .then((r: unknown) => Array.isArray(r) ? (r as SavedView[]) : []);
}

function saveCurrentAsView(name: string, isDefault: boolean): Promise<unknown> {
    return savedViewsRequest('', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            name,
            stateJson: JSON.stringify(captureResolvedState()),
            isDefault: !!isDefault
        })
    });
}

// Upserts the caller's single default view. Distinct from save-as: the server replaces whatever
// default already exists rather than accumulating duplicates named "My Default View".
function saveDefaultView(): Promise<unknown> {
    return savedViewsRequest('/default', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ state: captureResolvedState() })
    });
}

function updateSavedView(viewId: string, patch: Record<string, unknown>): Promise<unknown> {
    return savedViewsRequest('/' + encodeURIComponent(viewId), {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(patch)
    });
}

async function deleteSavedView(viewId: string): Promise<boolean> {
    const base = savedViewsBase();
    if (!base) return false;
    try {
        const res = await fetch(base + '/' + encodeURIComponent(viewId), { method: 'DELETE' });
        return res.ok;
    } catch (e) {
        console.warn('Saved-view delete failed:', e && (e as { message?: unknown }).message);
        return false;
    }
}

// Drops the caller's personal default (so the report stops opening on it) and returns the canvas
// to what the author declared: the DEFAULT = ON bookmark when one exists, otherwise a clean reload.
async function resetToReportDefault(views: SavedView[]): Promise<boolean> {
    const personalDefault = (views || []).find((v: SavedView) => v.isDefault);
    if (personalDefault) await updateSavedView(personalDefault.id, { isDefault: false });
    const authorDefault = ((getLastManifest() && getLastManifest().bookmarks) || []).find((b: Bookmark) => b.isDefault);
    if (authorDefault) {
        await applyBookmark(authorDefault.name);
    } else if (!vscode) {
        window.location.hash = '';
        window.location.reload();
    }
    return true;
}

// ── Views menu ──────────────────────────────────────────────────────────────────────────────

function styleMenuItem(el: HTMLButtonElement): HTMLButtonElement {
    el.type = 'button';
    el.setAttribute('role', 'menuitem');
    el.tabIndex = -1;
    el.style.border = 'none';
    el.style.background = 'none';
    el.style.cursor = 'pointer';
    el.style.fontSize = '0.875rem';
    el.style.color = 'var(--text, #333)';
    el.addEventListener('mouseenter', () => { el.style.background = 'var(--hover-bg, #f0f0f0)'; });
    el.addEventListener('mouseleave', () => { el.style.background = 'none'; });
    el.addEventListener('focus', () => { el.style.background = 'var(--hover-bg, #f0f0f0)'; });
    el.addEventListener('blur', () => { el.style.background = 'none'; });
    return el;
}

function menuHeading(text: string): HTMLDivElement {
    const h = document.createElement('div');
    // Presentational: the accessible name of the group comes from the group's aria-label, so the
    // heading text must not be announced a second time as a menu item.
    h.setAttribute('role', 'presentation');
    h.textContent = text;
    h.style.padding = '6px 16px 2px';
    h.style.fontSize = '0.7rem';
    h.style.textTransform = 'uppercase';
    h.style.letterSpacing = '0.04em';
    h.style.opacity = '0.65';
    return h;
}

/**
 * Builds the header "Views" menu. Returns null when there is nothing to show — no author
 * bookmarks and no Portal saved-view API (VS Code preview, ReportPlayer, offline snapshot).
 *
 * Keyboard model (WAI-ARIA menu button): Enter/Space/ArrowDown open and focus the first item,
 * ArrowUp opens on the last, Arrow keys roam, Home/End jump, Escape closes and restores focus to
 * the button, and Tab closes without swallowing the tab stop.
 */
export function buildViewsPicker(manifest?: ViewManifest | null): HTMLDivElement | null {
    const bookmarks: Bookmark[] = (manifest && manifest.bookmarks) || [];
    const supportsSavedViews = !!savedViewsBase();
    if (bookmarks.length === 0 && !supportsSavedViews) return null;

    const container = document.createElement('div');
    container.className = 'bookmark-picker';
    container.style.position = 'relative';
    container.style.display = 'inline-block';

    const menuId = 'etlsql-views-menu';
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'header-btn';
    btn.id = menuId + '-button';
    btn.title = supportsSavedViews ? 'Bookmarks and saved views' : 'Author bookmarks';
    btn.textContent = 'Views';
    btn.setAttribute('aria-haspopup', 'menu');
    btn.setAttribute('aria-expanded', 'false');
    btn.setAttribute('aria-controls', menuId);

    const menu = document.createElement('div');
    menu.id = menuId;
    menu.className = 'bookmark-menu';
    menu.setAttribute('role', 'menu');
    menu.setAttribute('aria-labelledby', btn.id);
    menu.hidden = true;
    menu.style.display = 'none';
    menu.style.position = 'absolute';
    menu.style.right = '0';
    menu.style.top = '100%';
    menu.style.zIndex = '1000';
    menu.style.background = 'var(--card-bg, #fff)';
    menu.style.border = '1px solid var(--border, #ddd)';
    menu.style.borderRadius = '6px';
    menu.style.boxShadow = '0 4px 12px rgba(0,0,0,0.15)';
    menu.style.minWidth = '260px';
    menu.style.padding = '4px 0';

    const items = (): HTMLElement[] => Array.from(menu.querySelectorAll<HTMLElement>('[role="menuitem"]:not([disabled])'));

    function close(restoreFocus: boolean): void {
        menu.hidden = true;
        menu.style.display = 'none';
        btn.setAttribute('aria-expanded', 'false');
        if (restoreFocus) btn.focus();
    }

    async function open(focusLast: boolean): Promise<void> {
        await renderMenu();
        menu.hidden = false;
        menu.style.display = 'block';
        btn.setAttribute('aria-expanded', 'true');
        const all = items();
        if (all.length) /** @type {HTMLElement} */ ((all[focusLast ? all.length - 1 : 0]) as HTMLElement).focus();
    }

    function isOpen(): boolean { return !menu.hidden; }

    // ── Menu content ────────────────────────────────────────────────────────────────────────
    let savedViews: SavedView[] = [];

    function addBookmarkSection(): void {
        if (bookmarks.length === 0) return;
        const group = document.createElement('div');
        group.setAttribute('role', 'group');
        group.setAttribute('aria-label', 'Report bookmarks');
        group.appendChild(menuHeading('Report bookmarks'));
        bookmarks.forEach((bm: Bookmark) => {
            const item = styleMenuItem(document.createElement('button'));
            item.className = 'bookmark-menu-item';
            item.style.display = 'block';
            item.style.width = '100%';
            item.style.textAlign = 'left';
            item.style.padding = '8px 16px';
            item.textContent = bm.title || bm.name;
            if (bm.isDefault) {
                item.style.fontWeight = 'bold';
                item.setAttribute('aria-label', (bm.title || bm.name) + ' (report default)');
            }
            item.addEventListener('click', () => { close(true); applyBookmark(bm.name); });
            group.appendChild(item);
        });
        menu.appendChild(group);
    }

    function savedViewRow(view: SavedView): HTMLDivElement {
        const row = document.createElement('div');
        row.setAttribute('role', 'group');
        row.setAttribute('aria-label', view.name);
        row.style.display = 'flex';
        row.style.alignItems = 'center';
        row.style.gap = '2px';
        row.style.padding = '0 8px 0 0';

        const apply = styleMenuItem(document.createElement('button'));
        apply.className = 'saved-view-menu-item';
        apply.style.flex = '1';
        apply.style.textAlign = 'left';
        apply.style.padding = '8px 8px 8px 16px';
        apply.textContent = view.isDefault ? '★ ' + view.name : view.name;
        apply.setAttribute('aria-label', view.isDefault ? view.name + ' (your default)' : view.name);
        if (view.driftWarning) apply.title = view.driftWarning;
        apply.addEventListener('click', () => { close(true); applySavedView(view.id); });
        row.appendChild(apply);

        const iconBtn = (glyph: string, label: string, handler: () => void | Promise<void>): HTMLButtonElement => {
            const b = styleMenuItem(document.createElement('button'));
            b.textContent = glyph;
            b.setAttribute('aria-label', label);
            b.title = label;
            b.style.padding = '6px 6px';
            b.addEventListener('click', handler);
            return b;
        };

        row.appendChild(iconBtn('⟳', 'Update ' + view.name + ' to the current state', async () => {
            const ok = await updateSavedView(view.id, { stateJson: JSON.stringify(captureResolvedState()) });
            feedback!.notify(ok ? `Updated '${view.name}'.` : `Could not update '${view.name}'.`,
                { title: 'Saved views', tone: ok ? 'success' : 'error', auditAction: 'report.saved-view.update' });
            if (ok) await renderMenu();
        }));

        if (!view.isDefault) {
            row.appendChild(iconBtn('☆', 'Make ' + view.name + ' my default view', async () => {
                const ok = await updateSavedView(view.id, { isDefault: true });
                feedback!.notify(ok ? `'${view.name}' is now your default view.` : 'Could not set the default view.',
                    { title: 'Saved views', tone: ok ? 'success' : 'error', auditAction: 'report.saved-view.update' });
                if (ok) await renderMenu();
            }));
        }

        row.appendChild(iconBtn('✕', 'Delete ' + view.name, async () => {
            close(true);
            const confirmed = await feedback!.confirm(`Delete the saved view '${view.name}'?`, {
                title: 'Delete saved view', confirmLabel: 'Delete', danger: true
            });
            if (!confirmed) return;
            const ok = await deleteSavedView(view.id);
            feedback!.notify(ok ? `Deleted '${view.name}'.` : `Could not delete '${view.name}'.`,
                { title: 'Saved views', tone: ok ? 'success' : 'error', auditAction: 'report.saved-view.delete' });
            if (ok) await open(false);
        }));

        return row;
    }

    function addSavedViewSection(): void {
        if (!supportsSavedViews) return;
        const group = document.createElement('div');
        group.setAttribute('role', 'group');
        group.setAttribute('aria-label', 'My saved views');
        group.appendChild(menuHeading('My saved views'));
        if (savedViews.length === 0) {
            const empty = document.createElement('div');
            empty.setAttribute('role', 'presentation');
            empty.textContent = 'No saved views yet.';
            empty.style.padding = '6px 16px';
            empty.style.fontSize = '0.8rem';
            empty.style.opacity = '0.7';
            group.appendChild(empty);
        } else {
            savedViews.forEach((v: SavedView) => group.appendChild(savedViewRow(v)));
        }
        menu.appendChild(group);
    }

    function addActionsSection(): void {
        if (!supportsSavedViews) return;
        const group = document.createElement('div');
        group.setAttribute('role', 'group');
        group.setAttribute('aria-label', 'Saved view actions');
        group.style.borderTop = '1px solid var(--border, #ddd)';
        group.style.marginTop = '4px';
        group.style.paddingTop = '4px';

        const action = (label: string, handler: () => void | Promise<void>): void => {
            const b = styleMenuItem(document.createElement('button'));
            b.style.display = 'block';
            b.style.width = '100%';
            b.style.textAlign = 'left';
            b.style.padding = '8px 16px';
            b.textContent = label;
            b.addEventListener('click', handler);
            group.appendChild(b);
        };

        action('Save current view as…', async () => {
            close(true);
            const name = await feedback!.prompt('Name this view so you can return to it later.', {
                title: 'Save current view', label: 'View name', confirmLabel: 'Save',
                required: true, requiredMessage: 'Enter a name for the view.'
            });
            if (!name) return;
            const created = await saveCurrentAsView(name, false);
            feedback!.notify(created ? `Saved '${name}'.` : `Could not save '${name}'.`,
                { title: 'Saved views', tone: created ? 'success' : 'error', auditAction: 'report.saved-view.create' });
        });

        action('Save as my default view', async () => {
            close(true);
            const saved = await saveDefaultView();
            feedback!.notify(saved ? 'Saved as your default view.' : 'Could not save your default view.',
                { title: 'Saved views', tone: saved ? 'success' : 'error', auditAction: 'report.saved-view.update' });
        });

        action('Reset to report default', async () => {
            close(true);
            await resetToReportDefault(savedViews);
        });

        menu.appendChild(group);
    }

    async function renderMenu(): Promise<void> {
        if (supportsSavedViews) savedViews = await listSavedViews();
        menu.textContent = '';
        addBookmarkSection();
        addSavedViewSection();
        addActionsSection();
    }

    btn.addEventListener('click', () => { if (isOpen()) close(false); else open(false); });
    btn.addEventListener('keydown', (e: KeyboardEvent) => {
        if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(false); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); open(true); }
    });

    menu.addEventListener('keydown', (e: KeyboardEvent) => {
        const all = items();
        const index = all.indexOf(document.activeElement as HTMLElement);
        if (e.key === 'Escape') { e.preventDefault(); close(true); }
        else if (e.key === 'Tab') { close(false); }
        else if (e.key === 'ArrowDown') { e.preventDefault(); /** @type {HTMLElement} */ ((all[(index + 1) % all.length]) as HTMLElement)?.focus(); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); /** @type {HTMLElement} */ ((all[(index - 1 + all.length) % all.length]) as HTMLElement)?.focus(); }
        else if (e.key === 'Home') { e.preventDefault(); /** @type {HTMLElement} */ ((all[0]) as HTMLElement)?.focus(); }
        else if (e.key === 'End') { e.preventDefault(); /** @type {HTMLElement} */ ((all[all.length - 1]) as HTMLElement)?.focus(); }
    });

    // The header is rebuilt on every manifest render, so this listener must go inert once its
    // picker has been detached rather than accumulating one live handler per render.
    document.addEventListener('click', (e: MouseEvent) => {
        if (!container.isConnected) return;
        if (isOpen() && !container.contains(/** @type {Node} */ ((e.target) as Node))) close(false);
    });

    container.appendChild(btn);
    container.appendChild(menu);
    return container;
}
