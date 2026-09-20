/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Visual selection, creation, deletion, and bookmark editing.
 */

import { datasetValue, feedback, queryElement, queryElements } from './designer-context.js';

import { DATA_PREP_RECIPES } from './data-prep-recipes.js';
import type { DesignerBookmark, DesignerDom, DesignerOptions, DesignerPage, DesignerState, DesignerVisual } from './designer-context.js';
import { esc } from './designer-util.js';

export interface DesignerVisualActionsContext {
    readonly bookmarksSection: DesignerDom;
    readonly canvasGrid: DesignerDom;
    readonly curPage: () => DesignerPage;
    readonly curVis: () => DesignerVisual[];
    readonly dataPrepModal: DesignerDom;
    readonly findVis: (id: string) => DesignerVisual | null;
    readonly maxRow: (vs: DesignerVisual[]) => number;
    readonly opts: DesignerOptions;
    pageIdx: number;
    readonly pushUndoState: () => void;
    readonly renderAlignmentToolbar: () => void;
    readonly renderAll: () => void;
    readonly renderCanvas: () => void;
    readonly renderDatasets: () => void;
    readonly renderProps: () => void;
    readonly renderTree: () => void;
    readonly selectVisualInEditor: (visualName: string) => void;
    selVisualId: string | null;
    selVisualIds: Set<string>;
    readonly state: DesignerState;
    readonly syncScriptFromGridDebounced: () => void;
    readonly uid: () => string;
}

export function createDesignerVisualActions(context: DesignerVisualActionsContext) {
    const SOURCE_OPTIONAL_TYPES = new Set([
        'TEXT', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER', 'SEARCH', 'SLICER',
        'MULTISELECT', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX', 'IMAGE', 'HTML',
    ]);

    function selectVisual(id: string | null, selectionOpts: { toggle?: boolean; multi?: boolean; skipEditorSync?: boolean; skipCanvas?: boolean } = {}): void {
        if (selectionOpts.toggle || selectionOpts.multi) {
            if (id) {
                if (context.selVisualIds.has(id)) context.selVisualIds.delete(id);
                else context.selVisualIds.add(id);
            }
        } else {
            context.selVisualIds.clear();
            if (id) context.selVisualIds.add(id);
        }

        context.selVisualId = context.selVisualIds.size === 1 ? Array.from(context.selVisualIds)[0] : null;

        for (const card of queryElements(context.canvasGrid, '.etlsql-dsgn-visual-card')) {
            card.classList.toggle('selected', context.selVisualIds.has(datasetValue(card, 'vid')));
        }

        context.renderTree();
        context.renderProps();
        context.renderAlignmentToolbar();

        context.opts.onVisualSelect?.(context.selVisualId);

        if (context.selVisualId && !selectionOpts.skipEditorSync) {
            const v = context.findVis(context.selVisualId);
            if (v && v.name) {
                context.selectVisualInEditor(v.name);
            }
        }
    }

    function deleteVisual(id: string): void {
        context.pushUndoState();
        for (const page of context.state.pages) {
            const i = (page.visuals || []).findIndex(v => v.id === id);
            if (i >= 0) { page.visuals.splice(i, 1); break; }
        }
        if (context.selVisualId === id) context.selVisualId = null;
        context.selVisualIds.delete(id);
        context.renderCanvas();
        context.renderTree();
        context.renderProps();
        context.syncScriptFromGridDebounced();
    }

    function deleteSelectedVisuals() {
        if (context.selVisualIds.size === 0) return;
        context.pushUndoState();
        for (const page of context.state.pages) {
            page.visuals = (page.visuals || []).filter(v => !context.selVisualIds.has(v.id));
        }
        context.selVisualIds.clear();
        context.selVisualId = null;
        context.renderAll();
    }

    /**
     * What a newly added visual should read from.
     *
     * The host knows best - Studio binds the sample the author just took - so it is asked first.
     * Standalone, the first declared dataset is the report's own answer, and failing that the source
     * an existing visual already uses, because a second visual on a page almost always plots the
     * same rows as the first.
     */
    function defaultVisualBinding(): { dataset: string | null; options: Record<string, string> } | null {
        if (typeof context.opts.defaultVisualBinding === 'function') {
            const hosted = context.opts.defaultVisualBinding();
            if (hosted && (hosted.dataset || hosted.options?.inline_source)) return hosted;
        }
        const dataset = (context.state.datasets || []).find(item => item?.name);
        if (dataset) return { dataset: dataset.name, options: {} };
        for (const existing of context.curVis()) {
            if (existing.dataset) return { dataset: existing.dataset, options: {} };
            if (existing.options?.inline_source)
                return { dataset: null, options: { inline_source: existing.options.inline_source } };
        }
        return null;
    }

    function addVisualAt(type: string, col = 1, row: number | null = null, colSpan = 12, rowSpan = 4): string | null {
        if (context.opts.canAddVisual && !context.opts.canAddVisual()) {
            context.opts.onAddVisualBlocked?.();
            return null;
        }

        // A visual added with no source used to look like it worked and then vanish. The card
        // rendered, but `CREATE VISUAL x AS BAR (...)` without a SOURCE clause does not parse, and
        // the patcher refuses a patch that does not parse - so the script never changed and the
        // visual was gone on the next reload, with nothing said. Bind the source before the card
        // exists, and refuse the add when there is nothing to bind.
        const upperType = type.toUpperCase();
        const needsSource = upperType !== 'CONTAINER' && upperType !== 'BUTTON'
            && !SOURCE_OPTIONAL_TYPES.has(upperType);
        const binding = needsSource ? defaultVisualBinding() : null;
        if (needsSource && !binding) {
            context.opts.onAddVisualBlocked?.();
            return null;
        }

        context.pushUndoState();
        if (!context.state.pages || !context.state.pages.length) {
            context.state.pages = [{ id: 'p1', name: 'Page 1', mode: 'Dashboard', visuals: [] }];
            context.pageIdx = 0;
        }
        let page = context.curPage();
        if (!page) {
            page = context.state.pages[0];
            context.pageIdx = 0;
        }
        if (!page.visuals) page.visuals = [];
        const newId = context.uid();
        const visual: DesignerVisual = {
            id: newId,
            name: type.toLowerCase() + '_' + newId.slice(2),
            type: type.toUpperCase(),
            gridCol: col || 1,
            gridRow: row !== null ? row : context.maxRow(page.visuals) + 1,
            gridColSpan: colSpan || (type === 'KPI' ? 3 : type === 'TABLE' ? 12 : 6),
            gridRowSpan: rowSpan || (type === 'KPI' ? 2 : type === 'TABLE' ? 5 : 4),
            title: '',
            dataset: binding?.dataset ?? null,
            mappings: {},
            options: { ...(binding?.options ?? {}) },
        };

        const uType = upperType;
        if (uType === 'BAR') {
            Object.assign(visual.options, { TITLE: 'Bar Chart' });
        } else if (uType === 'LINE') {
            Object.assign(visual.options, { TITLE: 'Trend Line' });
        } else if (uType === 'KPI') {
            Object.assign(visual.options, { TITLE: 'Key Metric' });
            visual.gridColSpan = 3;
            visual.gridRowSpan = 2;
        } else if (uType === 'DONUT' || uType === 'PIE') {
            Object.assign(visual.options, { TITLE: 'Proportions' });
        } else if (uType === 'TABLE') {
            Object.assign(visual.options, { TITLE: 'Data Grid Table', PAGE_SIZE: '10' });
            visual.gridColSpan = 12;
            visual.gridRowSpan = 5;
        } else if (uType === 'SLICER') {
            Object.assign(visual.options, { TITLE: 'Filter Slicer' });
            visual.gridColSpan = 3;
            visual.gridRowSpan = 3;
        } else if (uType === 'CONTAINER') {
            visual.options.CONTAINER_TYPE = 'BOX';
            visual.gridColSpan = 12;
            visual.gridRowSpan = 6;
        } else if (uType === 'BUTTON') {
            visual.options.BUTTON_TYPE = 'REFRESH';
            visual.gridColSpan = 2;
            visual.gridRowSpan = 1;
        } else if (uType === 'CUSTOM') {
            visual.options.advanced_chart = `CHART (
        COORDINATE (TYPE = CARTESIAN),
        LAYERS (
            main = RECT (
                ENCODINGS (
                    X = category (TYPE = NOMINAL),
                    Y = value (TYPE = QUANTITATIVE)
                )
            )
        )
    )`;
        } else if (uType === 'HTML') {
            visual.options.html_mode = 'SINGLE';
            visual.options.html_template = `<article class="custom-card">
  <h3>{{Title}}</h3>
  <p>{{Description}}</p>
</article>`;
            visual.options.html_style = `.custom-card {
  padding: 12px;
  border: 1px solid var(--portal-border, #e2e8f0);
  border-radius: 6px;
}`;
            visual.options.html_fallback = 'Custom HTML Visual: {{Title}} - {{Description}}';
        }

        page.visuals.push(visual);
        context.selVisualId = newId;
        context.renderCanvas();
        context.renderTree();
        context.renderProps();
        context.syncScriptFromGridDebounced();
        return newId;
    }

    function addVisual(type: string): void {
        const uType = (type || 'BAR').toUpperCase();
        addVisualAt(uType, 1, null, uType === 'KPI' ? 3 : uType === 'TABLE' ? 12 : 6, uType === 'KPI' ? 2 : uType === 'TABLE' ? 5 : 4);
    }

    function addPage() {
        const n = context.state.pages.length + 1;
        context.state.pages.push({ id: `p${n}_${Date.now()}`, name: `Page ${n}`, mode: 'Dashboard', visuals: [] });
        context.pageIdx = context.state.pages.length - 1;
        context.selVisualId = null;
        context.renderAll();
    }

    async function addDataset(): Promise<void> {
        const name = await feedback.prompt('Name the dataset used by this report.', { title: 'Add dataset', label: 'Dataset name', required: true, pattern: /^[A-Za-z_][A-Za-z0-9_]*$/, patternMessage: 'Start with a letter or underscore and use only letters, numbers, and underscores.', confirmLabel: 'Add dataset', auditAction: 'designer.dataset.add' });
        if (!name?.trim()) return;
        context.state.datasets.push({ id: 'ds_' + context.uid(), name: name.trim(), query: 'SELECT 1 AS Placeholder' });
        context.renderDatasets();
        context.renderProps();
    }

    function openDataPrepModal(): void {
        const recipeSelect = queryElement<HTMLSelectElement>(context.dataPrepModal, '#dsgn-dp-recipe');
        const descEl = queryElement(context.dataPrepModal, '#dsgn-dp-desc');
        const sourceInput = queryElement<HTMLInputElement>(context.dataPrepModal, '#dsgn-dp-source');
        const targetInput = queryElement<HTMLInputElement>(context.dataPrepModal, '#dsgn-dp-target');
        const sqlPreview = queryElement<HTMLTextAreaElement>(context.dataPrepModal, '#dsgn-dp-sql');

        const defaultSource = (context.state.datasets && context.state.datasets.length > 0)
            ? context.state.datasets[0].name.replace(/^[#&]/, '')
            : 'source_data';
        /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (sourceInput).value = defaultSource;

        function updatePreview() {
            const recipeId = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (recipeSelect).value;
            const recipe = DATA_PREP_RECIPES.find(r => r.id === recipeId) || DATA_PREP_RECIPES[0];
            descEl.textContent = recipe.description;
            const src = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (sourceInput).value.trim() || 'source_data';
            if (!/** @type {HTMLElement} */ (targetInput).dataset.userEdited) {
                /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (targetInput).value = `${src}_${recipe.targetSuffix}`;
            }
            const tgt = /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (targetInput).value.trim() || `${src}_${recipe.targetSuffix}`;
            /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */ (sqlPreview).value = recipe.template(tgt, src);
        }

        /** @type {HTMLElement} */ (targetInput).dataset.userEdited = '';
        /** @type {HTMLElement} */ (targetInput).oninput = () => { /** @type {HTMLElement} */ (targetInput).dataset.userEdited = 'true'; updatePreview(); };
        /** @type {HTMLElement} */ (sourceInput).oninput = () => { updatePreview(); };
        /** @type {HTMLElement} */ (recipeSelect).onchange = () => { /** @type {HTMLElement} */ (targetInput).dataset.userEdited = ''; updatePreview(); };

        updatePreview();
        context.dataPrepModal.style.display = 'flex';
    }

    // ── Author bookmarks ─────────────────────────────────────────────────────
    // Bookmarks are shared, source-controlled report state — the author's counterpart to a reader's
    // private saved view. The designer edits them as a list; the server patches only the matching
    // CREATE BOOKMARK statement, so everything else in the script stays where the author put it.

    function bookmarkList(): DesignerBookmark[] {
        // Undefined means "never loaded"; the patcher reads that as "leave existing bookmarks alone".
        // Only materialize the array once the author actually edits one.
        return Array.isArray(context.state.bookmarks) ? context.state.bookmarks : [];
    }

    function renderBookmarks(): void {
        const list = queryElement(context.bookmarksSection, '#dsgn-bookmark-list');
        if (!list) return;
        list.innerHTML = '';
        const bookmarks = bookmarkList();
        if (!bookmarks.length) {
            list.innerHTML = '<div class="etlsql-dsgn-sidebar-empty"><strong>No bookmarks yet</strong>'
                + '<span>Capture a page and its parameters as a named view readers can jump to.</span></div>';
            return;
        }
        for (const bm of bookmarks) {
            const row = document.createElement('div');
            row.className = 'etlsql-dsgn-ds-block';
            const label = bm.title || bm.name;
            const page = bm.page ? ` → ${esc(bm.page)}` : '';
            row.innerHTML = `
                <div class="etlsql-dsgn-ds-item">
                    <span title="${esc(bm.name)}">${bm.isDefault ? '★ ' : ''}${esc(label)}${page}</span>
                    <span>
                        <button data-bmedit="${esc(bm.id)}" type="button" title="Edit ${esc(bm.name)}"
                                aria-label="Edit bookmark ${esc(bm.name)}">✎</button>
                        <button data-bmdefault="${esc(bm.id)}" type="button"
                                title="${bm.isDefault ? 'Clear report default' : 'Make report default'}"
                                aria-label="${bm.isDefault ? 'Clear' : 'Set'} ${esc(bm.name)} as the report default">${bm.isDefault ? '★' : '☆'}</button>
                        <button data-bmid="${esc(bm.id)}" type="button" title="Remove ${esc(bm.name)}"
                                aria-label="Remove bookmark ${esc(bm.name)}">✕</button>
                    </span>
                </div>
            `;
            list.appendChild(row);
        }
    }

    async function addBookmark(): Promise<void> {
        const name = await feedback.prompt('Name the bookmark readers will see.', {
            title: 'Add bookmark', label: 'Bookmark name', required: true,
            pattern: /^[A-Za-z_][A-Za-z0-9_]*$/,
            patternMessage: 'Start with a letter or underscore and use only letters, numbers, and underscores.',
            confirmLabel: 'Add bookmark', auditAction: 'designer.bookmark.add'
        });
        if (!name?.trim()) return;
        if (!Array.isArray(context.state.bookmarks)) context.state.bookmarks = [];
        context.state.bookmarks.push({
            id: 'bm_' + context.uid(),
            name: name.trim(),
            // Capture the page the author is on: a bookmark that lands nowhere is not useful, and the
            // author can still clear it when editing.
            page: context.state.pages[context.pageIdx]?.name || null,
            isDefault: false,
            parameters: [],
            state: []
        });
        renderBookmarks();
        context.syncScriptFromGridDebounced();
    }

    async function editBookmarkTitle(id: string): Promise<void> {
        const bm = bookmarkList().find(b => b.id === id);
        if (!bm) return;
        const title = await feedback.prompt('Shown in the reader’s bookmark menu.', {
            title: `Edit ${bm.name}`, label: 'Display title', value: bm.title || '',
            confirmLabel: 'Save', auditAction: 'designer.bookmark.update'
        });
        if (title === null) return;
        bm.title = title.trim() || null;
        renderBookmarks();
        context.syncScriptFromGridDebounced();
    }

    function toggleBookmarkDefault(id: string): void {
        const bookmarks = bookmarkList();
        const target = bookmarks.find(b => b.id === id);
        if (!target) return;
        const next = !target.isDefault;
        // At most one author default: the parser rejects a second one, so the designer must not be
        // able to author a script that will not parse.
        for (const bm of bookmarks) bm.isDefault = false;
        target.isDefault = next;
        renderBookmarks();
        context.syncScriptFromGridDebounced();
    }

    function removeBookmark(id: string): void {
        if (!Array.isArray(context.state.bookmarks)) return;
        context.state.bookmarks = context.state.bookmarks.filter(b => b.id !== id);
        renderBookmarks();
        context.syncScriptFromGridDebounced();
    }

    return { selectVisual, deleteVisual, deleteSelectedVisuals, addVisualAt, addVisual, addPage, addDataset, openDataPrepModal, renderBookmarks, addBookmark, editBookmarkTitle, toggleBookmarkDefault, removeBookmark };
}
