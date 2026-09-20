// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-outline.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-outline.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Report outline, visual ordering, visibility, and locking.
 */
import { _escapeHtml, _feedback, _studioIcon, queryElements } from './studio-context.js';
export function createStudioOutline(hostContext) {
    const OUTLINE_LOCK_STORAGE = 'etlsql-studio-outline-locks';
    function reportTreeMarkup() {
        const pages = hostContext.state.designerInstance?.getState?.().pages || [];
        if (!pages.length)
            return '<div class="etlsql-studio-empty-compact">No report page yet.</div>';
        return pages.map(page => `<div class="etlsql-studio-tree-page"><strong>${_escapeHtml(page.name || 'Page')}</strong><span>${page.visuals?.length || 0} visuals</span></div>${(page.visuals || []).map(visual => `<button type="button" class="etlsql-studio-tree-visual" data-tree-visual="${_escapeHtml(visual.id)}"><span>${_escapeHtml(visual.type)}</span>${_escapeHtml(visual.name || visual.id)}</button>`).join('')}`).join('');
    }
    /** Locks are keyed by document path and by visual *name*: parse ids carry a position and move. */
    function outlineLockKey(doc = hostContext.getActiveDoc()) {
        return `${OUTLINE_LOCK_STORAGE}:${doc?.path || doc?.id || 'untitled'}`;
    }
    function lockedVisualNames(doc = hostContext.getActiveDoc()) {
        try {
            const raw = localStorage.getItem(outlineLockKey(doc));
            const parsed = raw ? JSON.parse(raw) : [];
            return new Set(Array.isArray(parsed) ? parsed.map(name => String(name).toLowerCase()) : []);
        }
        catch {
            return new Set();
        }
    }
    function isVisualLocked(visual, doc = hostContext.getActiveDoc()) {
        const name = String(visual?.name ?? visual ?? '').toLowerCase();
        return Boolean(name) && lockedVisualNames(doc).has(name);
    }
    function setVisualLocked(name, locked) {
        const locks = lockedVisualNames();
        const key = String(name || '').toLowerCase();
        if (!key)
            return;
        if (locked)
            locks.add(key);
        else
            locks.delete(key);
        try {
            localStorage.setItem(outlineLockKey(), JSON.stringify([...locks]));
        }
        catch {
            // A host with storage disabled keeps nothing past the reload. The panel and the canvas
            // both read this same set, so the guard still holds for the session; what is lost is
            // only its persistence, and the panel already tells the author it is machine-local.
        }
    }
    /** Reading order for a grid: top band first, then left to right inside the band. */
    function compareVisualPlacement(a, b) {
        return (a.gridRow || 1) - (b.gridRow || 1) || (a.gridCol || 1) - (b.gridCol || 1);
    }
    function isContainerVisual(visual) {
        return String(visual?.type || '').toUpperCase() === 'CONTAINER';
    }
    function isVisualHidden(visual) {
        return String(visual?.options?.VISIBLE ?? visual?.options?.visible ?? 'ON').toUpperCase() === 'OFF';
    }
    /**
     * Splits a page's top-level visuals into the row bands the grid actually draws.
     *
     * A band is a set of visuals whose row ranges overlap, which is what a reader sees as "one row"
     * even when the tiles in it have different heights. Grouping by `gridRow` alone would put a tall
     * tile in a band of its own and split the row it visibly shares.
     */
    function outlineRowBands(visuals) {
        const bands = [];
        for (const visual of [...visuals].sort(compareVisualPlacement)) {
            const start = visual.gridRow || 1;
            const end = start + (visual.gridRowSpan || 1) - 1;
            const band = bands.find(candidate => start <= candidate.end && end >= candidate.start);
            if (band) {
                band.start = Math.min(band.start, start);
                band.end = Math.max(band.end, end);
                band.visuals.push(visual);
            }
            else {
                bands.push({ start, end, visuals: [visual] });
            }
        }
        return bands;
    }
    /** The siblings a visual is reordered among: its container's children, or the page's roots. */
    function outlineSiblings(page, visual) {
        const visuals = page.visuals || [];
        const containerIds = new Set(visuals.filter(isContainerVisual).map((item) => item.id));
        const nested = Boolean(visual.containerId) && containerIds.has(visual.containerId);
        return visuals
            .filter((item) => (nested
            ? item.containerId === visual.containerId
            : !item.containerId || !containerIds.has(item.containerId)))
            .sort(compareVisualPlacement);
    }
    function outlineVisualMarkup(page, visual, depth, children = []) {
        const hidden = isVisualHidden(visual);
        const locked = isVisualLocked(visual);
        const selected = hostContext.state.selectedVisualId === visual.id;
        const siblings = outlineSiblings(page, visual);
        const index = siblings.findIndex((item) => item.id === visual.id);
        const name = visual.name || visual.id;
        const classes = ['etlsql-studio-outline-item'];
        if (selected)
            classes.push('is-selected');
        if (hidden)
            classes.push('is-hidden');
        if (locked)
            classes.push('is-locked');
        const action = (attr, icon, title, { disabled = false, pressed = null } = {}) => `<button type="button" class="etlsql-studio-outline-action" ${attr}="${_escapeHtml(visual.id)}"`
            + ` data-outline-name="${_escapeHtml(visual.name || '')}" title="${_escapeHtml(title)}"`
            + ` aria-label="${_escapeHtml(title)}"${disabled ? ' disabled' : ''}`
            + `${pressed === null ? '' : ` aria-pressed="${pressed}"`}>${icon}</button>`;
        return `<div class="${classes.join(' ')}" data-outline-item="${_escapeHtml(visual.id)}" role="treeitem" aria-selected="${selected}" style="--outline-depth:${depth}">
                <button type="button" class="etlsql-studio-outline-select" data-outline-select="${_escapeHtml(visual.id)}">
                    <span class="etlsql-studio-outline-kind">${_escapeHtml(isContainerVisual(visual) ? 'GROUP' : visual.type)}</span>
                    <span class="etlsql-studio-outline-name">${_escapeHtml(name)}</span>
                </button>
                <span class="etlsql-studio-outline-actions">
                    ${action('data-outline-up', _studioIcon('moveUp', 12), locked ? `${name} is locked` : index > 0 ? `Move ${name} earlier` : `${name} is already first here`, { disabled: locked || index <= 0 })}
                    ${action('data-outline-down', _studioIcon('moveDown', 12), locked ? `${name} is locked` : index >= 0 && index < siblings.length - 1 ? `Move ${name} later` : `${name} is already last here`, { disabled: locked || index < 0 || index >= siblings.length - 1 })}
                    ${action('data-outline-visible', _studioIcon(hidden ? 'hidden' : 'visible', 12), hidden ? `Show ${name}` : `Hide ${name}`, { pressed: hidden ? 'true' : 'false' })}
                    ${action('data-outline-lock', _studioIcon(locked ? 'locked' : 'unlocked', 12), locked ? `Unlock ${name} on this canvas` : `Lock ${name} on this canvas`, { pressed: locked ? 'true' : 'false' })}
                </span>
            </div>${children.join('')}`;
    }
    function outlineMarkup() {
        const design = hostContext.state.designerInstance?.getState?.();
        const pages = design?.pages || [];
        if (!pages.length) {
            return '<div class="etlsql-studio-empty-guidance"><strong>No report page yet</strong><span>Add a page or a visual and the outline will list what it holds.</span></div>';
        }
        const activePage = hostContext.state.designerInstance?.activePageIndex?.() ?? 0;
        return pages.map((page, pageIndex) => {
            const visuals = page.visuals || [];
            const containerIds = new Set(visuals.filter(isContainerVisual).map((container) => container.id));
            const roots = visuals.filter((visual) => !visual.containerId || !containerIds.has(visual.containerId));
            const bands = outlineRowBands(roots);
            const body = bands.length
                ? bands.map((band, bandIndex) => `<div class="etlsql-studio-outline-band"><span>Row ${bandIndex + 1}</span><small>${band.visuals.length} item${band.visuals.length === 1 ? '' : 's'}</small></div>${band.visuals.map(visual => outlineVisualMarkup(page, visual, 1, isContainerVisual(visual)
                    ? visuals.filter((child) => child.containerId === visual.id)
                        .sort(compareVisualPlacement)
                        .map((child) => outlineVisualMarkup(page, child, 2))
                    : [])).join('')}`).join('')
                : '<div class="etlsql-studio-empty-compact">This page has no visuals yet.</div>';
            return `<div class="etlsql-studio-outline-page${pageIndex === activePage ? ' is-active' : ''}">
                    <button type="button" class="etlsql-studio-outline-page-btn" data-outline-page="${pageIndex}">
                        <strong>${_escapeHtml(page.name || `Page ${pageIndex + 1}`)}</strong>
                        <span>${_escapeHtml(page.mode || 'Dashboard')} · ${visuals.length} visual${visuals.length === 1 ? '' : 's'}</span>
                    </button>
                </div>${body}`;
        }).join('');
    }
    function renderOutlineTree() {
        hostContext.sidebarTitle.textContent = 'Outline';
        hostContext.sidebarContent.innerHTML = `<section class="etlsql-studio-library-section">
                <div class="etlsql-studio-outline-tree" role="tree" aria-label="Document outline">${outlineMarkup()}</div>
                <p class="etlsql-studio-outline-note">Move and hide write to the script. Lock is a canvas guard held on this machine — it stops a drag, a resize, and a delete, and it is not saved into the report.</p>
            </section>`;
        queryElements(hostContext.sidebarContent, '[data-outline-page]').forEach(button => button.addEventListener('click', () => {
            hostContext.state.designerInstance?.selectPage?.(Number(button.dataset.outlinePage));
            renderOutlineTree();
        }));
        queryElements(hostContext.sidebarContent, '[data-outline-select]').forEach(button => button.addEventListener('click', () => {
            hostContext.state.designerInstance?.selectVisual?.(button.dataset.outlineSelect);
        }));
        queryElements(hostContext.sidebarContent, '[data-outline-up]').forEach(button => button.addEventListener('click', () => {
            void reorderVisualInOutline(button.dataset.outlineName || '', -1);
        }));
        queryElements(hostContext.sidebarContent, '[data-outline-down]').forEach(button => button.addEventListener('click', () => {
            void reorderVisualInOutline(button.dataset.outlineName || '', 1);
        }));
        queryElements(hostContext.sidebarContent, '[data-outline-visible]').forEach(button => button.addEventListener('click', () => {
            void toggleVisualVisibility(button.dataset.outlineName || '');
        }));
        queryElements(hostContext.sidebarContent, '[data-outline-lock]').forEach(button => button.addEventListener('click', () => {
            const visual = findVisualByName(button.dataset.outlineName || '');
            if (!visual)
                return;
            const locking = !isVisualLocked(visual);
            setVisualLocked(visual.name, locking);
            hostContext.state.designerInstance?.refreshSnapshot?.();
            renderOutlineTree();
            _feedback.notify(locking
                ? `${visual.name} will not move, resize, or delete from the canvas until it is unlocked. The lock lives on this machine and is not written into the script.`
                : `${visual.name} can be moved from the canvas again.`, { title: locking ? 'Locked on this canvas' : 'Unlocked', tone: 'info' });
        }));
    }
    function findVisualByName(visualName) {
        const wanted = String(visualName || '').toLowerCase();
        if (!wanted)
            return null;
        return (hostContext.state.designerInstance?.getState?.().pages || [])
            .flatMap((page) => page.visuals || [])
            .find((item) => String(item.name || '').toLowerCase() === wanted) || null;
    }
    /**
     * Moves a visual one place earlier or later in reading order by swapping its grid placement with
     * its neighbour's — spans included, so the two tiles trade cells and the grid stays exactly as
     * full as it was. The patcher regenerates STRUCTURE from those coordinates, so this one swap is
     * the entire edit; there is no separate ordering to keep in step with it.
     */
    function reorderVisualInOutline(visualName, direction) {
        return hostContext.canonicalDesignerMutation('Reorder visual', (designState) => {
            const visual = hostContext.findDesignerVisual(designState, visualName);
            if (!visual)
                throw new Error(`Visual ${visualName} was not found in the parsed document.`);
            if (isVisualLocked(visual))
                throw new Error(`${visual.name} is locked on this canvas. Unlock it to move it.`);
            const page = (designState.pages || []).find((item) => (item.visuals || []).includes(visual));
            if (!page)
                throw new Error(`Visual ${visual.name} is not placed on a page.`);
            const siblings = outlineSiblings(page, visual);
            const neighbour = siblings[siblings.findIndex((item) => item.id === visual.id) + direction];
            if (!neighbour)
                throw new Error(`${visual.name} is already ${direction < 0 ? 'first' : 'last'} here.`);
            const placement = (item) => ({
                gridCol: item.gridCol, gridRow: item.gridRow,
                gridColSpan: item.gridColSpan, gridRowSpan: item.gridRowSpan,
            });
            const moved = placement(visual);
            Object.assign(visual, placement(neighbour));
            Object.assign(neighbour, moved);
            return visual.name;
        }).then(result => {
            if (result && hostContext.state.activeActivity === 'outline')
                renderOutlineTree();
            return result;
        });
    }
    /**
     * Hides or shows a visual through VISIBLE, the property the report runtime already reads. A
     * hidden visual stays in the outline — it is the only place a tile the reader cannot see is
     * still reachable, which is most of why the panel lists it.
     */
    function toggleVisualVisibility(visualName) {
        const visual = findVisualByName(visualName);
        if (!visual)
            return Promise.resolve(null);
        return hostContext.surgicalPatchVisualOption(visual.id, 'VISIBLE', isVisualHidden(visual) ? 'ON' : 'OFF')
            .then(result => {
            if (result && hostContext.state.activeActivity === 'outline')
                renderOutlineTree();
            return result;
        });
    }
    return { reportTreeMarkup, isVisualLocked, renderOutlineTree };
}
