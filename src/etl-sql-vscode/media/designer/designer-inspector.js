// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer-inspector.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/designer-inspector.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Selected visual and report property editors.
 */
import { VTYPES, controlTarget, datasetValue, inspectorGroupKey, queryElement, queryElements } from './designer-context.js';
import { filterSourceOn, filterSourceOnHover, filterSourceOnSeveral, listFilterCondition, hoverContextColumn, parametersRead, readClickAction, readEmitTargets, readRowDetail, readTooltip, selectionKey, splitNames, writeClickAction, writeRowDetail, writeTooltip } from './designer-interactions.js';
import { esc } from './designer-util.js';
import { renderFormattingSectionHtml, renderVisualFormatInspectorHtml, toHexColor } from './visual-format-inspector.js';
import { CHART_AGGREGATES, VISUAL_ROLES, aggregateExpression } from './visual-preview.js';
export function createDesignerInspector(context) {
    const CASCADE_INVALID = [
        { value: 'CLEAR', label: 'Clear the selection' },
        { value: 'FIRST', label: 'Select the first remaining option' },
        { value: 'ERROR', label: 'Refuse the change' },
    ];
    const INTERACTION_EFFECTS = [
        { value: 'HIGHLIGHT', label: 'Linked — highlight matching data', note: 'A selection elsewhere keeps every row here and dims the rest.' },
        { value: 'FILTER', label: 'Linked — filter to matching rows', note: 'A selection elsewhere re-queries this visual and hides the rest.' },
        { value: 'NONE', label: 'Never linked', note: 'This visual never sends or reacts to a selection.' },
    ];
    const CLICK_KINDS = [
        { value: 'NONE', label: 'Nothing' },
        { value: 'DRILL_DOWN', label: 'Show details in another visual' },
        { value: 'DRILL_IN', label: 'Drill into the next level' },
        { value: 'NAVIGATE_PAGE', label: 'Go to a page' },
        { value: 'SET_PARAMETER', label: 'Set a parameter' },
        { value: 'CUSTOM', label: 'Custom action' },
    ];
    const TOOLTIP_KINDS = [
        { value: 'NONE', label: 'The default tooltip' },
        { value: 'TEXT', label: 'Text' },
        { value: 'FIELDS', label: 'Fields from the hovered row' },
        { value: 'VISUALS', label: 'Other visuals, in a popover' },
        { value: 'CONTAINER', label: 'A container, in a popover' },
        { value: 'CUSTOM', label: 'As written in the script' },
    ];
    /** Suggestions for a field's FORMAT, which follows the DATA_LABELS convention. */
    const TOOLTIP_FORMATS = [
        { value: 'C0', label: 'Currency, whole' },
        { value: 'C2', label: 'Currency, cents' },
        { value: 'N0', label: 'Number, whole' },
        { value: 'N2', label: 'Number, 2 decimals' },
        { value: 'P0', label: 'Percent, whole' },
        { value: 'P1', label: 'Percent, 1 decimal' },
        { value: 'yyyy-MM-dd', label: 'Date' },
    ];
    /** Visuals with no rows of their own: they neither send nor receive a selection. */
    const NON_DATA_TYPES = new Set(['CONTAINER', 'BUTTON', 'TEXT', 'IMAGE']);
    /** Where a click action lives once the visual's left click selects. */
    const LINKED_CLICK_NOTE = ' This visual is linked, so a left click selects; this action is on its right-click menu.';
    /** Visuals the dialect refuses ACTIONS on. */
    const DISPLAY_ONLY_TYPES = new Set(['TEXT', 'CARD', 'IMAGE', 'CONTAINER']);
    const ROLES = ['X', 'Y', 'VALUE', 'CATEGORY', 'SERIES', 'LABEL', 'TOOLTIP'];
    /**
     * A click action the author has started but not finished, by visual id. An incomplete action
     * writes nothing to the script, so without this the picker would forget the author's choice
     * on the next render.
     */
    const clickDrafts = new Map();
    /**
     * A row detail with a match the author has added but not filled in, by visual id. It is used
     * only while it still writes the clause the script holds, so an edit made in the script wins.
     */
    const rowDetailDrafts = new Map();
    /** A tooltip being set up that does not write a clause yet (no field or visual chosen), by visual id. */
    const tooltipDrafts = new Map();
    /**
     * Columns this visual can key a selection on: what it maps, what its dataset declares, and what
     * the host can see in its own data sample.
     *
     * The list is a suggestion, never a limit. A visual sourced from a `#temp` table the designer
     * never sampled has no known columns at all, and a picker that offered nothing would make the
     * setting unreachable for exactly the scripts most likely to need it.
     */
    function interactionKeyCandidates(v, colNames) {
        let hostColumns = [];
        try {
            hostColumns = context.opts.getDatasetColumns?.() || [];
        }
        catch {
            // A host that cannot answer right now is not a reason to lose the columns we do know.
        }
        return [...new Set([
                ...Object.values(v.mappings || {}).filter(Boolean).map(String),
                ...(colNames || []),
                ...hostColumns.map(String),
            ])].filter(Boolean);
    }
    /** Suggestions for a free-text column field. */
    function columnDatalist(id, values) {
        return `<datalist id="${esc(id)}">${values
            .map(value => `<option value="${esc(value)}"></option>`).join('')}</datalist>`;
    }
    /**
     * Options for a picker that must never silently drop a value the author wrote by hand.
     *
     * A `MATCHING` naming a column the designer cannot see is not necessarily wrong — the source may
     * be an inline query the canvas never sampled — so an unknown current value is offered as its
     * own option rather than resetting the control to "auto" and writing that back on the next edit.
     */
    function preservingOptions(values, current, placeholder) {
        const known = values.filter(Boolean).map(String);
        const has = known.some(value => value.toLowerCase() === String(current || '').toLowerCase());
        const all = current && !has ? [current, ...known] : known;
        return `<option value=""${current ? '' : ' selected'}>${esc(placeholder)}</option>`
            + all.map(value => `<option value="${esc(value)}"${String(value).toLowerCase() === String(current || '').toLowerCase() ? ' selected' : ''}>${esc(value)}</option>`).join('');
    }
    function readCascade(clause) {
        if (!clause || !String(clause).trim())
            return null;
        const text = String(clause);
        const mode = /\bMODE\s*=\s*(LOCAL|LIVE)\b/i.exec(text)?.[1]?.toUpperCase();
        if (!mode)
            return { supported: false, text };
        const parents = [];
        const parentsClause = /\bPARENTS\s*\(([^)]*)\)/i.exec(text)?.[1] || '';
        for (const entry of parentsClause.split(',')) {
            const pair = /^\s*(@[A-Za-z_][A-Za-z0-9_]*)\s*=\s*([A-Za-z_][A-Za-z0-9_]*)\s*$/.exec(entry);
            if (pair)
                parents.push({ parameter: pair[1], column: pair[2] });
        }
        // A PARENTS clause that is present but unreadable must not be silently emptied.
        if (parentsClause.trim() && !parents.length)
            return { supported: false, text };
        return {
            supported: true,
            text,
            mode,
            parents,
            invalid: /\bINVALID\s*=\s*(CLEAR|FIRST|ERROR)\b/i.exec(text)?.[1]?.toUpperCase() || 'CLEAR',
            nullPolicy: /\bNULL\s*=\s*(ALL|MATCH)\b/i.exec(text)?.[1]?.toUpperCase() || 'ALL',
            allValue: /\bALL_VALUE\s*=\s*'((?:[^']|'')*)'/i.exec(text)?.[1]?.replace(/''/g, "'") ?? '*',
            multiSelect: /\bMULTISELECT\s*=\s*(ANY|ALL)\b/i.exec(text)?.[1]?.toUpperCase() || 'ANY',
        };
    }
    /** Writes the clause back in the serializer's own shape, so a round-trip changes no bytes. */
    function writeCascade(cascade) {
        const parts = [`MODE = ${cascade.mode}`];
        if (cascade.mode === 'LOCAL' && cascade.parents.length) {
            parts.push('PARENTS (' + cascade.parents
                .map(parent => `${parent.parameter} = ${parent.column}`).join(', ') + ')');
        }
        parts.push(`INVALID = ${cascade.invalid}`);
        parts.push(`NULL = ${cascade.nullPolicy}`);
        parts.push(`ALL_VALUE = '${String(cascade.allValue).replace(/'/g, "''")}'`);
        parts.push(`MULTISELECT = ${cascade.multiSelect}`);
        return 'CASCADE ( ' + parts.join(', ') + ' )';
    }
    function restoreInspectorGroups() {
        for (const details of queryElements(context.propsPanel, '.etlsql-format-group')) {
            const heading = inspectorGroupKey(details);
            if (!heading)
                continue;
            // A group the markup opens by default stays open and is recorded, so closing it sticks.
            if (details.open)
                context.openInspectorGroups.add(heading);
            else if (context.openInspectorGroups.has(heading))
                details.open = true;
        }
    }
    /**
     * Records which groups are open right now, from the DOM. The `toggle` listener does this too,
     * but `toggle` is dispatched as a later task, so a rebuild that follows the click in the same
     * turn (open a group, then pick from it) ran before the event and closed the group again.
     */
    function rememberInspectorGroups() {
        for (const details of queryElements(context.propsPanel, '.etlsql-format-group')) {
            const heading = inspectorGroupKey(details);
            if (!heading)
                continue;
            if (details.open)
                context.openInspectorGroups.add(heading);
            else
                context.openInspectorGroups.delete(heading);
        }
    }
    function renderProps() {
        rememberInspectorGroups();
        renderPropsBody();
        restoreInspectorGroups();
    }
    function renderPropsBody() {
        context.propsPanel.innerHTML = '';
        const v = context.selVisualId ? context.findVis(context.selVisualId) : null;
        const on = (sel, fn) => queryElement(context.propsPanel, sel)?.addEventListener('change', fn);
        if (!v) {
            // Reading the inspector must not author anything. Defaulting the theme into the design
            // state here wrote `SET REPORT THEME = 'light';` into every script that had no theme, on
            // nothing more than a render - and the Portal refuses SetReportMetadata in an interactive
            // run, so a report the author never themed became unrunnable in Studio.
            const style = context.state.reportStyle || {};
            const currentTheme = style.theme || 'light';
            const themes = ['light', 'dark', 'midnight', 'dracula', 'nord', 'custom'];
            context.propsPanel.innerHTML = `
                <section class="etlsql-format-inspector" aria-label="Report properties">
                    <div class="etlsql-format-profile">
                        <div><span>Dashboard</span><strong>Report & Dashboard Style</strong></div>
                    </div>
                    <div class="etlsql-format-search-wrap">
                        <input type="search" id="pp-search-filter" class="form-control etlsql-format-search" placeholder="Filter settings... (e.g. title, theme, accent)" autocomplete="off" spellcheck="false">
                        <button type="button" class="etlsql-format-search-clear" id="pp-search-clear" title="Clear filter" style="display:none;">×</button>
                    </div>
                    <details class="etlsql-format-group" open>
                        <summary>Style & Theme</summary>
                        <div class="etlsql-format-group-body">
                            <label class="etlsql-dsgn-label">Report Title
                                <input type="text" id="pp-report-title" class="form-control" value="${esc(context.reportName)}" placeholder="Dashboard Title">
                            </label>
                            <label class="etlsql-dsgn-label">Report Theme
                                <select id="pp-report-theme" class="form-control">
                                    ${themes.map(t => `<option value="${t}"${currentTheme === t ? ' selected' : ''}>${t.charAt(0).toUpperCase() + t.slice(1)}</option>`).join('')}
                                </select>
                            </label>
                            ${currentTheme === 'custom' || style.accent ? `
                            <div class="etlsql-dsgn-color-grid" style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px;">
                                <label class="etlsql-dsgn-label">Accent Color
                                    <input type="color" id="pp-color-accent" class="form-control" value="${style.accent || '#2563eb'}">
                                </label>
                                <label class="etlsql-dsgn-label">Background
                                    <input type="color" id="pp-color-bg" class="form-control" value="${style.background || '#ffffff'}">
                                </label>
                                <label class="etlsql-dsgn-label">Card Surface
                                    <input type="color" id="pp-color-surface" class="form-control" value="${style.surface || '#ffffff'}">
                                </label>
                                <label class="etlsql-dsgn-label">Text Color
                                    <input type="color" id="pp-color-text" class="form-control" value="${style.text || '#1e293b'}">
                                </label>
                            </div>` : ''}
                        </div>
                    </details>
                </section>
                <p class="etlsql-dsgn-props-empty" style="margin-top:16px;">Click any visual card on the grid canvas to edit its properties, mappings, and events.</p>
            `;
            on('#pp-report-title', e => {
                context.reportName = controlTarget(e).value;
                const titleEl = queryElement(context.topbar, '#dsgn-title-input');
                if (titleEl) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */
                    (titleEl).value = context.reportName;
                context.syncScriptFromGridDebounced();
            });
            on('#pp-report-theme', e => {
                context.pushUndoState();
                if (!context.state.reportStyle)
                    context.state.reportStyle = {};
                context.state.reportStyle.theme = controlTarget(e).value;
                const themesList = ['light', 'dark', 'midnight', 'dracula', 'nord', 'custom'];
                themesList.forEach(t => document.body.classList.remove('theme-' + t));
                document.body.classList.add('theme-' + controlTarget(e).value);
                const selectEl = queryElement(context.topbar, '#dsgn-theme-select');
                if (selectEl) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */
                    (selectEl).value = controlTarget(e).value;
                renderProps();
                context.syncScriptFromGridDebounced();
            });
            const bindColor = (id, prop) => {
                on(id, e => {
                    context.pushUndoState();
                    if (!context.state.reportStyle)
                        context.state.reportStyle = {};
                    context.state.reportStyle[prop] = controlTarget(e).value;
                    context.syncScriptFromGridDebounced();
                });
            };
            bindColor('#pp-color-accent', 'accent');
            bindColor('#pp-color-bg', 'background');
            bindColor('#pp-color-surface', 'surface');
            bindColor('#pp-color-text', 'text');
            context.bindInspectorSearch(context.propsPanel);
            return;
        }
        if (v.type === 'CONTAINER') {
            const containerType = v.options?.CONTAINER_TYPE || 'BOX';
            const ctypes = ['BOX', 'SCROLL', 'DRAWER', 'SIDEBAR', 'TABS', 'ACCORDION', 'MODAL', 'POPOVER'];
            context.propsPanel.innerHTML = `
                <section class="etlsql-format-inspector" aria-label="Container properties">
                    <div class="etlsql-format-profile">
                        <div><span>Container</span><strong>${esc(v.name || 'CONTAINER')} · ${esc(containerType)}</strong></div>
                    </div>
                    <div class="etlsql-format-search-wrap">
                        <input type="search" id="pp-search-filter" class="form-control etlsql-format-search" placeholder="Filter settings... (e.g. title, type, border)" autocomplete="off" spellcheck="false">
                        <button type="button" class="etlsql-format-search-clear" id="pp-search-clear" title="Clear filter" style="display:none;">×</button>
                    </div>
                    <details class="etlsql-format-group" open>
                        <summary>Properties</summary>
                        <div class="etlsql-format-group-body">
                            <label class="etlsql-dsgn-label">Container Type
                                <select id="pp-container-type" class="form-control">
                                    ${ctypes.map(t => `<option${containerType === t ? ' selected' : ''}>${t}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Title<input id="pp-title" class="form-control" value="${esc(v.title || '')}"></label>
                        </div>
                    </details>
                    ${renderFormattingSectionHtml(v)}
                    <details class="etlsql-format-group" open>
                        <summary>Grid Position & Layout</summary>
                        <div class="etlsql-format-group-body">
                            <div class="etlsql-dsgn-grid4">
                                <label>Col<input type="number" id="pp-col"   class="form-control" min="1" max="12" value="${v.gridCol || 1}"></label>
                                <label>Row<input type="number" id="pp-row"   class="form-control" min="1"          value="${v.gridRow || 1}"></label>
                                <label>W  <input type="number" id="pp-cspan" class="form-control" min="1" max="12" value="${v.gridColSpan || 12}"></label>
                                <label>H  <input type="number" id="pp-rspan" class="form-control" min="1"          value="${v.gridRowSpan || 4}"></label>
                            </div>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Container Name (ID)<input id="pp-name" class="form-control" value="${esc(v.name)}"></label>
                            <button class="btn btn-sm etlsql-dsgn-del-btn" id="pp-delete">Remove Container</button>
                        </div>
                    </details>
                </section>
            `;
            on('#pp-name', e => { v.name = controlTarget(e).value; context.renderCanvas(); context.renderTree(); });
            on('#pp-container-type', e => { if (!v.options)
                v.options = {}; v.options.CONTAINER_TYPE = controlTarget(e).value; });
            on('#pp-title', e => { v.title = controlTarget(e).value; context.renderCanvas(); });
            on('#pp-col', e => { v.gridCol = +controlTarget(e).value || 1; context.renderCanvas(); });
            on('#pp-row', e => { v.gridRow = +controlTarget(e).value || 1; context.renderCanvas(); });
            on('#pp-cspan', e => { v.gridColSpan = +controlTarget(e).value || 12; context.renderCanvas(); });
            on('#pp-rspan', e => { v.gridRowSpan = +controlTarget(e).value || 4; context.renderCanvas(); });
            context.bindFormattingSection(context.propsPanel, v, context.renderCanvas, context.syncScriptFromGridDebounced);
            context.bindInspectorSearch(context.propsPanel);
            queryElement(context.propsPanel, '#pp-delete')?.addEventListener('click', () => context.deleteVisual(v.id));
            return;
        }
        if (v.type === 'BUTTON') {
            const buttonType = v.options?.BUTTON_TYPE || 'REFRESH';
            const btypes = ['REFRESH', 'BACK', 'HELP', 'SUBMIT', 'RESET', 'NAVIGATE', 'ACTION'];
            context.propsPanel.innerHTML = `
                <section class="etlsql-format-inspector" aria-label="Button properties">
                    <div class="etlsql-format-profile">
                        <div><span>Button</span><strong>${esc(v.name || 'BUTTON')} · ${esc(buttonType)}</strong></div>
                    </div>
                    <div class="etlsql-format-search-wrap">
                        <input type="search" id="pp-search-filter" class="form-control etlsql-format-search" placeholder="Filter settings... (e.g. title, button type, border)" autocomplete="off" spellcheck="false">
                        <button type="button" class="etlsql-format-search-clear" id="pp-search-clear" title="Clear filter" style="display:none;">×</button>
                    </div>
                    <details class="etlsql-format-group" open>
                        <summary>Properties</summary>
                        <div class="etlsql-format-group-body">
                            <label class="etlsql-dsgn-label">Button Type
                                <select id="pp-button-type" class="form-control">
                                    ${btypes.map(t => `<option${buttonType === t ? ' selected' : ''}>${t}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Title<input id="pp-title" class="form-control" value="${esc(v.title || '')}"></label>
                        </div>
                    </details>
                    ${renderFormattingSectionHtml(v)}
                    <details class="etlsql-format-group" open>
                        <summary>Grid Position & Layout</summary>
                        <div class="etlsql-format-group-body">
                            <div class="etlsql-dsgn-grid4">
                                <label>Col<input type="number" id="pp-col"   class="form-control" min="1" max="12" value="${v.gridCol || 1}"></label>
                                <label>Row<input type="number" id="pp-row"   class="form-control" min="1"          value="${v.gridRow || 1}"></label>
                                <label>W  <input type="number" id="pp-cspan" class="form-control" min="1" max="12" value="${v.gridColSpan || 12}"></label>
                                <label>H  <input type="number" id="pp-rspan" class="form-control" min="1"          value="${v.gridRowSpan || 4}"></label>
                            </div>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Button Name (ID)<input id="pp-name" class="form-control" value="${esc(v.name)}"></label>
                            <button class="btn btn-sm etlsql-dsgn-del-btn" id="pp-delete">Remove Button</button>
                        </div>
                    </details>
                </section>
            `;
            on('#pp-name', e => { v.name = controlTarget(e).value; context.renderCanvas(); context.renderTree(); });
            on('#pp-button-type', e => { if (!v.options)
                v.options = {}; v.options.BUTTON_TYPE = controlTarget(e).value; });
            on('#pp-title', e => { v.title = controlTarget(e).value; context.renderCanvas(); });
            on('#pp-col', e => { v.gridCol = +controlTarget(e).value || 1; context.renderCanvas(); });
            on('#pp-row', e => { v.gridRow = +controlTarget(e).value || 1; context.renderCanvas(); });
            on('#pp-cspan', e => { v.gridColSpan = +controlTarget(e).value || 12; context.renderCanvas(); });
            on('#pp-rspan', e => { v.gridRowSpan = +controlTarget(e).value || 4; context.renderCanvas(); });
            context.bindFormattingSection(context.propsPanel, v, context.renderCanvas, context.syncScriptFromGridDebounced);
            context.bindInspectorSearch(context.propsPanel);
            queryElement(context.propsPanel, '#pp-delete')?.addEventListener('click', () => context.deleteVisual(v.id));
            return;
        }
        function parseRoleAggregate(expr) {
            const s = String(expr || '').trim();
            const match = /^(COUNT|SUM|AVG|MIN|MAX)\s*\(\s*(DISTINCT\s+)?([A-Za-z0-9_.]+)\s*\)$/i.exec(s);
            if (match) {
                return {
                    aggregate: match[2] ? 'COUNT_DISTINCT' : match[1].toUpperCase(),
                    column: match[3],
                };
            }
            return { aggregate: 'NONE', column: s };
        }
        const mappings = v.mappings || {};
        const dsOpts = context.state.datasets
            .map(d => `<option value="${esc(d.name)}"${v.dataset === d.name ? ' selected' : ''}>#${esc(d.name)}</option>`)
            .join('');
        const REQUIRED_ROLES = {
            SANKEY: ['Source', 'Target', 'Value'],
            NETWORK: ['From', 'To'],
            DONUT: ['Category', 'Value'], PIE: ['Category', 'Value'], FUNNEL: ['Category', 'Value'], SUNBURST: ['Category', 'Value'],
            BAR: ['Category', 'Value'], HBAR: ['Category', 'Value'], LINE: ['Category', 'Value'], COMBO: ['Category', 'Value'],
            WATERFALL: ['Category', 'Value'], CANDLESTICK: ['Category', 'Value'],
            GAUGE: ['Value'], HEATMAP: ['Category', 'Value'], BOXPLOT: ['Category', 'Value'],
            SCATTER: ['X', 'Y'], BUBBLE: ['X', 'Y'], SLICER: ['Category'], MULTISELECT: ['Category']
        };
        const reqList = REQUIRED_ROLES[v.type] || [];
        const parentVis = v.containerId ? context.findVis(v.containerId) : null;
        const parentType = parentVis?.options?.CONTAINER_TYPE;
        const isTabbedParent = parentType === 'TABS' || parentType === 'ACCORDION';
        // Visuals live on the page, not on the root state. Reaching for state.visuals threw a
        // TypeError inside renderProps, and because renderProps runs *before* onVisualSelect in
        // selectVisual, the throw took the whole selection with it: the host never learned a visual
        // had been selected, so the properties panel simply never opened.
        const cOpts = context.curVis()
            .filter(c => c.type === 'CONTAINER' && c.id !== v.id)
            .map(c => `<option value="${esc(c.id)}"${v.containerId === c.id ? ' selected' : ''}>${esc(c.name || 'Container')} (${esc(c.options?.CONTAINER_TYPE || 'BOX')})</option>`)
            .join('');
        // The parse reports declarations as `parameters`, with the `@` already on the name. Reading
        // `state.variables` — a key nothing ever sets — left this picker permanently empty, so the
        // one control that binds a slicer to a parameter looked like a report with no parameters.
        const declaredParameters = context.state.parameters || [];
        const varOpts = declaredParameters
            .map(vr => `<option value="${esc(vr.name)}"${v.options?.['action:TARGET_VAR'] === vr.name ? ' selected' : ''}>${esc(vr.name)} (${esc(vr.dataType || 'VARCHAR')})</option>`)
            .join('');
        const ds = context.state.datasets.find(d => d.name === v.dataset);
        const schema = ds?.schema;
        const colNames = schema?.map(c => c.name) || ds?.columns || [];
        const colOptions = colNames.length
            ? (ds?.schema?.length
                ? colNames.map(n => { const c = schema?.find(s => s.name === n); return `<option value="${esc(n)}">${esc(n)} (${esc(c?.type || 'TEXT')})</option>`; }).join('')
                : colNames.map(n => `<option value="${esc(n)}">${esc(n)}</option>`).join(''))
            : '';
        const datalistId = `dsgn-cols-${v.id}`;
        const datalistHtml = colOptions.length ? `<datalist id="${datalistId}">${colOptions}</datalist>` : '';
        // Cross-visual interaction and cascade state, read from the visual as authored.
        const onSelect = String(v.options?.['interaction:ON_SELECT'] || '').trim().toUpperCase();
        const matchingColumn = String(v.options?.['interaction:MATCHING'] || '').trim();
        const isSlicerLike = v.type === 'SLICER' || v.type === 'MULTISELECT';
        const cascade = readCascade(v.options?.cascade);
        // How the runtime links visuals: a visual responds to a selection elsewhere only when it
        // declares ON_SELECT, and sends one when it declares ON_SELECT or is a table or slicer
        // (ChartInteractionResolver.SelectionModeFor). The old default label said an unlinked
        // visual highlighted; it ignores selections.
        const sendsByDefault = v.type === 'TABLE' || v.type === 'SLICER';
        const linked = onSelect !== '' && onSelect !== 'NONE';
        const sendsSelection = linked || (onSelect === '' && sendsByDefault);
        const defaultLinkLabel = sendsByDefault ? 'Default \u2014 clicks send a selection' : 'Not linked';
        const interactionEffect = INTERACTION_EFFECTS.find(effect => effect.value === onSelect);
        const interactionNote = onSelect === 'NONE'
            ? 'Clicks here select nothing, and selections elsewhere leave this visual alone.'
            : !linked
                ? (sendsByDefault
                    ? 'Clicking a row sends a selection to linked visuals. Selections elsewhere leave this one alone.'
                    : 'Clicks here select nothing, and selections elsewhere leave this visual alone.')
                : `Clicking here sends a selection to linked visuals. ${interactionEffect ? interactionEffect.note : ''}`
                    + (matchingColumn
                        ? ` Rows are matched on ${matchingColumn}.`
                        : ' Rows are matched on this visual\u2019s category field.');
        // Who a selection reaches (EMIT_FILTER). No boxes ticked means every linked visual.
        const pageVisuals = context.curVis().filter(other => other.id !== v.id && !NON_DATA_TYPES.has(String(other.type || '').toUpperCase()));
        const pageVisualNames = new Set(pageVisuals.map(other => String(other.name).toLowerCase()));
        const linkedNames = new Set(pageVisuals
            .filter(other => /^(HIGHLIGHT|FILTER)$/i.test(String(other.options?.['interaction:ON_SELECT'] || '')))
            .map(other => String(other.name).toLowerCase()));
        const emitTargets = readEmitTargets(v.options?.emit_filter);
        const emitTargetSet = new Set(emitTargets.map(name => name.toLowerCase()));
        // An authored target this page does not show is kept, ticked, so an edit never drops it.
        const emitCandidates = [
            ...pageVisuals.map(other => String(other.name)),
            ...emitTargets.filter(name => !pageVisualNames.has(name.toLowerCase())),
        ];
        const unlinkedTargets = emitTargets.filter(name => pageVisualNames.has(name.toLowerCase()) && !linkedNames.has(name.toLowerCase()));
        const emitNote = (emitTargets.length
            ? `A selection here reaches only ${emitTargets.join(', ')}.`
            : linkedNames.size
                ? 'Nothing ticked: a selection here reaches every linked visual.'
                : 'No visual on this page is linked yet, so a selection here reaches nothing. Link one by setting its cross-filtering.')
            + (unlinkedTargets.length
                ? ` ${unlinkedTargets.join(', ')} ${unlinkedTargets.length === 1 ? 'is' : 'are'} not linked, so ${unlinkedTargets.length === 1 ? 'it ignores' : 'they ignore'} the selection until ${unlinkedTargets.length === 1 ? 'its' : 'their'} cross-filtering is set.`
                : '');
        // What a linked visual's query has to read. A selection does not filter rows by itself: it
        // sets a parameter named after the column it was made on while each receiver's query runs
        // (VisualBuilder.FetchDataAsync), so a receiver that never reads it cannot narrow.
        const sendsFrom = (other) => {
            const mode = String(other.options?.['interaction:ON_SELECT'] || '').toUpperCase();
            return mode ? mode !== 'NONE' : other.type === 'TABLE' || other.type === 'SLICER';
        };
        const incoming = new Map();
        if (linked) {
            for (const other of pageVisuals) {
                if (!sendsFrom(other))
                    continue;
                const reach = readEmitTargets(other.options?.emit_filter);
                if (reach.length && !reach.some(name => name.toLowerCase() === String(v.name).toLowerCase()))
                    continue;
                const key = selectionKey(other.options, other.mappings);
                if (key)
                    incoming.set(key, [...(incoming.get(key) || []), String(other.name)]);
            }
        }
        const reads = parametersRead(v.options?.inline_source);
        const unreadKeys = [...incoming.keys()].filter(key => !reads.has(`@${key}`.toLowerCase()));
        // Keys this visual filters on with Studio's earlier single-value condition.
        const singlePickKeys = [...incoming.keys()].filter(key => filterSourceOnSeveral(v.options?.inline_source, key));
        const acceptsClickActions = !DISPLAY_ONLY_TYPES.has(String(v.type || '').toUpperCase());
        const writtenClick = readClickAction(v.options?.['action:ON_CLICK']);
        const clickAction = writtenClick.kind === 'NONE' ? (clickDrafts.get(v.id) ?? writtenClick) : writtenClick;
        const otherVisualNames = context.curVis()
            .filter(other => other.id !== v.id && !NON_DATA_TYPES.has(String(other.type || '').toUpperCase()))
            .map(other => String(other.name));
        const pageNames = (context.state.pages || []).map(page => String(page.name));
        const declaredNames = new Set(declaredParameters.map(item => String(item.name).toLowerCase()));
        const undeclaredKeys = clickAction.kind === 'DRILL_DOWN'
            ? clickAction.keys.filter(key => !declaredNames.has(`@${key}`.toLowerCase()))
            : [];
        // The drill-down's target has to read what the click sets; offer to write that for it.
        const drillTarget = clickAction.kind === 'DRILL_DOWN' && clickAction.target
            ? context.curVis().find(other => String(other.name).toLowerCase() === clickAction.target.toLowerCase()) ?? null
            : null;
        const drillUnread = drillTarget && clickAction.kind === 'DRILL_DOWN'
            ? clickAction.keys.filter(key => !parametersRead(drillTarget.options?.inline_source).has(`@${key}`.toLowerCase())
                && filterSourceOn(drillTarget.options?.inline_source, key))
            : [];
        const clickNote = (() => {
            switch (clickAction.kind) {
                case 'DRILL_DOWN': {
                    const params = clickAction.keys.map(key => `@${key}`).join(' and ');
                    let note = params
                        ? `A click sets ${params} to the clicked row's value and re-runs the report${clickAction.target ? `, so ${clickAction.target} must read ${params} in its query` : ''}.`
                        : 'Name the columns whose clicked values the detail visual should filter on.';
                    if (undeclaredKeys.length)
                        note += ` Declare ${undeclaredKeys.map(key => `@${key}`).join(', ')} first; nothing reads an undeclared parameter.`;
                    if (sendsSelection)
                        note += LINKED_CLICK_NOTE;
                    return note;
                }
                case 'DRILL_IN':
                    return 'A click replaces this visual\u2019s rows with the next level down, keeping the clicked value. Each level must be a column of its source.'
                        + (sendsSelection ? LINKED_CLICK_NOTE : '');
                case 'NAVIGATE_PAGE':
                    return sendsSelection ? LINKED_CLICK_NOTE.trim() : '';
                case 'SET_PARAMETER':
                    return `A click sets ${clickAction.parameter || 'the parameter'} to the clicked row's ${clickAction.column || 'value'}, and every visual that reads it re-runs.`
                        + (sendsSelection ? LINKED_CLICK_NOTE : '');
                case 'CUSTOM':
                    return 'Written exactly as typed. Use the script to combine several actions.';
                default:
                    return '';
            }
        })();
        // What hovering a data point shows (TOOLTIP). Visuals with no rows have nothing to hover.
        const acceptsTooltip = !NON_DATA_TYPES.has(String(v.type || '').toUpperCase());
        const writtenTooltip = readTooltip(v.options?.tooltip);
        const tooltipDraft = tooltipDrafts.get(v.id);
        const tooltip = tooltipDraft && (writtenTooltip.kind === 'NONE' || writeTooltip(tooltipDraft) === v.options?.tooltip)
            ? tooltipDraft
            : writtenTooltip;
        const hoverColumn = hoverContextColumn(v.mappings);
        const containerNames = context.curVis()
            .filter(other => String(other.type || '').toUpperCase() === 'CONTAINER')
            .map(other => String(other.name));
        const popoverVisuals = tooltip.kind === 'VISUALS'
            ? tooltip.visuals
                .map(name => context.curVis().find(other => String(other.name).toLowerCase() === name.toLowerCase()))
                .filter((other) => Boolean(other))
            : [];
        // Popover visuals that do not read @hover_value yet, and can be made to.
        const hoverUnread = hoverColumn && /^[A-Za-z_][A-Za-z0-9_]*$/.test(hoverColumn)
            ? popoverVisuals.filter(other => !parametersRead(other.options?.inline_source).has('@hover_value')
                && Boolean(filterSourceOnHover(other.options?.inline_source, hoverColumn)))
            : [];
        const tooltipNote = (() => {
            switch (tooltip.kind) {
                case 'TEXT': return 'Shown as written whenever a data point is hovered.';
                case 'FIELDS': return 'Read from the hovered row, so it needs no extra query. Field names are columns of this visual’s source.';
                case 'VISUALS':
                case 'CONTAINER':
                    return hoverColumn
                        ? `Opens a popover on hover or click. Its visuals receive the hovered ${hoverColumn} as @hover_value, and each one’s query decides what that filters.`
                        : 'A popover needs this visual to map X, LABEL, NAME, REGION, or Y: that column’s hovered value is what the popover’s visuals receive as @hover_value. The report will not build until one is mapped.';
                case 'CUSTOM': return 'This tooltip is left exactly as written. Edit it in the script, or choose another kind to replace it.';
                default: return '';
            }
        })();
        // A table's drill-through (ROW_DETAIL).
        const rowDetailDraft = rowDetailDrafts.get(v.id);
        const rowDetail = v.type !== 'TABLE'
            ? null
            : rowDetailDraft && v.options?.row_detail === writeRowDetail(rowDetailDraft)
                ? rowDetailDraft
                : readRowDetail(v.options?.row_detail);
        const rowDetailNote = rowDetail?.supported
            ? `Each row gets an expand button. It shows ${rowDetail.target}\u2019s rows`
                + (rowDetail.bindings.filter(binding => binding.childColumn && binding.parentColumn).length
                    ? ` where ${rowDetail.bindings.filter(binding => binding.childColumn && binding.parentColumn).map(binding => `${binding.childColumn} equals this row\u2019s ${binding.parentColumn}`).join(' and ')}`
                    : '')
                + `. ${rowDetail.target} usually sets VISIBLE = OFF so it appears only under rows.`
            : '';
        const cascadeNote = !cascade || !cascade.supported
            ? ''
            : cascade.mode === 'LIVE'
                ? 'Its options come from re-running this control\u2019s own query, so its parents are whichever parameters that query names.'
                : cascade.parents.length
                    ? `Its options are filtered by ${cascade.parents.map(parent => parent.parameter).join(' and ')} before the reader sees them.`
                    : 'LOCAL filtering does nothing until a parent binding names the parameter and the column to filter on.';
        const isCustomChart = v.type === 'CUSTOM';
        const defaultCustomChart = `CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
        x_scale = BAND (CHANNEL = X),
        y_scale = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
    ),
    LAYERS (
        bars = RECT (
            Z_INDEX = 1,
            ENCODINGS (
                X = category (TYPE = ORDINAL, SCALE = x_scale),
                Y = value (TYPE = QUANTITATIVE, SCALE = y_scale)
            )
        )
    )
)`;
        const boxPlotMeanRecipe = `CHART (
    COORDINATE (TYPE = CARTESIAN),
    ENCODINGS (X = category (TYPE = NOMINAL)),
    LAYERS (
        boxes = RECT (
            ENCODINGS (
                LOW = low (TYPE = QUANTITATIVE),
                Q1 = q1 (TYPE = QUANTITATIVE),
                MEDIAN = median (TYPE = QUANTITATIVE),
                Q3 = q3 (TYPE = QUANTITATIVE),
                HIGH = high (TYPE = QUANTITATIVE)
            )
        ),
        mean = TICK (
            Z_INDEX = 1,
            THICKNESS = 0.3,
            ENCODINGS (Y = mean (TYPE = QUANTITATIVE))
        )
    )
)`;
        const candlestickVolumeRecipe = `CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
        categories = BAND (CHANNEL = X, ORDER = SOURCE),
        price = LINEAR (CHANNEL = Y, INCLUDE_ZERO = OFF),
        volume_scale = LINEAR (CHANNEL = Y2, INCLUDE_ZERO = ON)
    ),
    LAYERS (
        volume = RECT (
            Z_INDEX = 0,
            BAND_SIZE = 0.35,
            ENCODINGS (
                X = category (TYPE = ORDINAL, SCALE = categories),
                Y2 = volume (TYPE = QUANTITATIVE, SCALE = volume_scale, AXIS = SECONDARY)
            )
        ),
        candles = RECT (
            Z_INDEX = 1,
            ENCODINGS (
                X = category (TYPE = ORDINAL, SCALE = categories),
                OPEN = open (TYPE = QUANTITATIVE, SCALE = price),
                CLOSE = close (TYPE = QUANTITATIVE, SCALE = price),
                LOW = low (TYPE = QUANTITATIVE, SCALE = price),
                HIGH = high (TYPE = QUANTITATIVE, SCALE = price)
            )
        )
    )
)`;
        const layeredMapRecipe = `CHART (
    COORDINATE (TYPE = GEOGRAPHIC, PROJECTION = EQUIRECTANGULAR, MAP_NAME = 'WORLD', FEATURE_KEY = 'name'),
    LAYERS (
        regions = RECT (
            ENCODINGS (
                REGION = region (TYPE = NOMINAL),
                COLOR = value (TYPE = QUANTITATIVE)
            )
        ),
        routes = LINE (
            Z_INDEX = 1,
            ENCODINGS (
                LONGITUDE = longitude (TYPE = QUANTITATIVE),
                LATITUDE = latitude (TYPE = QUANTITATIVE),
                ROUTE = route (TYPE = NOMINAL)
            )
        ),
        points = POINT (
            Z_INDEX = 2,
            ENCODINGS (
                LONGITUDE = longitude (TYPE = QUANTITATIVE),
                LATITUDE = latitude (TYPE = QUANTITATIVE),
                TEXT = label (TYPE = NOMINAL)
            )
        )
    )
)`;
        const chartCode = v.options?.advanced_chart || defaultCustomChart;
        const isHtmlVisual = v.type === 'HTML';
        const htmlMode = v.options?.html_mode || 'SINGLE';
        const htmlTemplate = v.options?.html_template || '<article class="custom-card">\n  <h3>{{Title}}</h3>\n  <p>{{Description}}</p>\n</article>';
        const htmlStyle = v.options?.html_style || '';
        const htmlFallback = v.options?.html_fallback || '';
        const formatting = v.formatting || {};
        const palette = formatting.palette || [];
        const palettePreview = palette.length ? palette : ['#2563eb', '#16a34a', '#f59e0b', '#dc2626'];
        const formatValue = v.options?.FORMAT || '';
        const filledRolesCount = ROLES.filter(r => Boolean(mappings[r])).length;
        context.propsPanel.innerHTML = `
            <section class="etlsql-format-inspector" aria-label="Visual formatting">
                <div class="etlsql-format-profile">
                    <div><span>Format profile</span><strong>${esc(v.type)} · ${esc(formatValue || 'Auto')}</strong></div>
                    <div class="etlsql-format-profile-palette" aria-label="${palette.length ? 'Authored palette' : 'Default palette'}">
                        ${palettePreview.slice(0, 5).map(color => `<i style="--format-color:${esc(toHexColor(color, '#64748b'))}"></i>`).join('')}
                    </div>
                </div>
                <div class="etlsql-format-search-wrap">
                    <input type="search" id="pp-search-filter" class="form-control etlsql-format-search" placeholder="Filter settings... (e.g. title, dataset, axis, color)" autocomplete="off" spellcheck="false">
                    <button type="button" class="etlsql-format-search-clear" id="pp-search-clear" title="Clear filter" style="display:none;">×</button>
                </div>

                <details class="etlsql-format-group" open>
                    <summary>Data & Mappings <span>${filledRolesCount}/${ROLES.length}</span></summary>
                    <div class="etlsql-format-group-body">
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Visual Type
                                <select id="pp-type" class="form-control">
                                    ${VTYPES.map(([t]) => `<option${v.type === t ? ' selected' : ''}>${t}</option>`).join('')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Dataset
                                <select id="pp-ds" class="form-control">
                                    <option value="">— none —</option>${dsOpts}
                                </select>
                            </label>
                        </div>
                        ${isCustomChart ? `
                        <div class="etlsql-dsgn-props-section etlsql-dsgn-chart-editor-section">
                            <div class="etlsql-dsgn-chart-quick-controls" style="display:grid;grid-template-columns:1fr 1fr;gap:6px;margin:8px 0 6px;">
                                <label class="etlsql-dsgn-label">Coordinate
                                    <select id="pp-chart-coord" class="form-control">
                                        <option value="CARTESIAN"${chartCode.includes('CARTESIAN') && !chartCode.includes('TRANSPOSED') ? ' selected' : ''}>CARTESIAN</option>
                                        <option value="TRANSPOSED_CARTESIAN"${chartCode.includes('TRANSPOSED_CARTESIAN') ? ' selected' : ''}>TRANSPOSED</option>
                                        <option value="POLAR"${chartCode.includes('POLAR') ? ' selected' : ''}>POLAR</option>
                                        <option value="GEOGRAPHIC"${chartCode.includes('GEOGRAPHIC') ? ' selected' : ''}>GEOGRAPHIC</option>
                                    </select>
                                </label>
                                <label class="etlsql-dsgn-label">Primary Mark
                                    <select id="pp-chart-primary-mark" class="form-control">
                                        <option value="RECT"${chartCode.includes('RECT') ? ' selected' : ''}>RECT (Bar)</option>
                                        <option value="LINE"${chartCode.includes('LINE') ? ' selected' : ''}>LINE (Line)</option>
                                        <option value="AREA"${chartCode.includes('AREA') ? ' selected' : ''}>AREA (Area)</option>
                                        <option value="POINT"${chartCode.includes('POINT') ? ' selected' : ''}>POINT (Scatter)</option>
                                        <option value="RULE"${chartCode.includes('RULE') ? ' selected' : ''}>RULE (Span)</option>
                                        <option value="ARC"${chartCode.includes('ARC') ? ' selected' : ''}>ARC (Radial)</option>
                                        <option value="TEXT"${chartCode.includes('TEXT') ? ' selected' : ''}>TEXT (Label)</option>
                                        <option value="TICK"${chartCode.includes('TICK') ? ' selected' : ''}>TICK (Target)</option>
                                    </select>
                                </label>
                                <label class="etlsql-dsgn-label" style="grid-column:1 / -1;">Composition recipe
                                    <select id="pp-chart-recipe" class="form-control">
                                        <option value="">Keep current chart</option>
                                        <option value="boxplot-mean"${/\bQ1\s*=/.test(chartCode) ? ' selected' : ''}>Box plot + mean tick</option>
                                        <option value="candlestick-volume"${/\bOPEN\s*=/.test(chartCode) ? ' selected' : ''}>Candlestick + volume</option>
                                        <option value="layered-map"${/TYPE\s*=\s*GEOGRAPHIC/.test(chartCode) ? ' selected' : ''}>Layered map</option>
                                    </select>
                                </label>
                            </div>
                            <label class="etlsql-dsgn-label">CHART Clauses (Layers, Scales, Encodings, Conditions)
                                <textarea id="pp-chart-code" class="form-control etlsql-code-editor" rows="12" spellcheck="false" style="font-family:monospace;font-size:11px;line-height:1.4;tab-size:2;white-space:pre;resize:vertical;">${esc(chartCode)}</textarea>
                            </label>
                        </div>` : (isHtmlVisual ? `
                        <div class="etlsql-dsgn-props-section etlsql-dsgn-html-editor-section">
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Mode
                                <select id="pp-html-mode" class="form-control">
                                    <option value="SINGLE"${htmlMode === 'SINGLE' ? ' selected' : ''}>SINGLE (First row or static)</option>
                                    <option value="REPEATER"${htmlMode === 'REPEATER' ? ' selected' : ''}>REPEATER (Repeat per row)</option>
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">HTML Template
                                <span style="font-size:10px;color:var(--portal-muted,#7a8798);display:block;margin-bottom:2px;">
                                    Substitutions: <code>{{Field}}</code>, <code>{{@Param}}</code>, <code>{{#IF ...}}</code>, <code>{{SPARKLINE(...)}}</code>, <code>{{PROGRESS_BAR(...)}}</code>
                                </span>
                                <textarea id="pp-html-template" class="form-control etlsql-code-editor" rows="8" spellcheck="false" style="font-family:monospace;font-size:11px;line-height:1.4;tab-size:2;white-space:pre;resize:vertical;">${esc(htmlTemplate)}</textarea>
                            </label>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Scoped CSS (STYLE)
                                <textarea id="pp-html-style" class="form-control etlsql-code-editor" rows="4" spellcheck="false" placeholder=".custom-card { padding: 8px; }" style="font-family:monospace;font-size:11px;line-height:1.4;tab-size:2;white-space:pre;resize:vertical;">${esc(htmlStyle)}</textarea>
                            </label>
                            <label class="etlsql-dsgn-label" style="margin-top:6px;">Fallback Summary (Terminal/Print)
                                <input type="text" id="pp-html-fallback" class="form-control" placeholder="e.g., Status: {{Title}} - {{Description}}" value="${esc(htmlFallback)}">
                            </label>
                        </div>` : `
                        <div style="margin-top:8px;">
                            ${ROLES.map(r => {
            const isReq = reqList.includes(r);
            const isFilled = Boolean(mappings[r]);
            const badge = isReq
                ? (isFilled ? '<span class="etlsql-dsgn-role-badge req-ok">✓ Required</span>' : '<span class="etlsql-dsgn-role-badge req-missing">* Required</span>')
                : '';
            const roleSpecs = VISUAL_ROLES[v.type || ''] || [];
            const roleSpec = roleSpecs.find(s => s.key.toUpperCase() === r.toUpperCase());
            const isMeasureRole = Boolean(roleSpec?.measure || ['Y', 'VALUE', 'MEASURE', 'ACTUAL', 'TARGET'].includes(r.toUpperCase()));
            const { aggregate: currentAgg } = parseRoleAggregate(mappings[r] || '');
            return `
                                    <div class="etlsql-dsgn-map-row">
                                        <div class="etlsql-dsgn-map-label">
                                            <span class="etlsql-dsgn-role-name" title="${r}">${r}</span>
                                            ${badge}
                                        </div>
                                        <div style="display:flex;gap:4px;width:100%;align-items:center;">
                                            <input type="text" data-role="${r}" class="form-control${isReq && !isFilled ? ' is-required-missing' : ''}" value="${esc(mappings[r] || '')}" placeholder="column or expression" ${colOptions.length ? `list="${datalistId}"` : ''} style="flex:1;">
                                            ${isMeasureRole ? `
                                            <select data-role-agg="${r}" class="form-control etlsql-dsgn-agg-select" style="width:115px;font-size:11px;padding:2px 4px;" title="Aggregation function">
                                                ${CHART_AGGREGATES.map(a => `<option value="${a.id}" ${currentAgg === a.id ? 'selected' : ''}>${esc(a.id === 'NONE' ? 'No aggregate' : a.label)}</option>`).join('')}
                                            </select>` : ''}
                                        </div>
                                    </div>`;
        }).join('')}
                            ${datalistHtml}
                        </div>`)}
                    </div>
                </details>

                ${renderVisualFormatInspectorHtml(v, colNames)}
                ${renderFormattingSectionHtml(v)}

                <details class="etlsql-format-group">
                    <summary>Actions & Interactions</summary>
                    <div class="etlsql-format-group-body">
                        <label class="etlsql-dsgn-label">Target Parameter (@var)
                            <select id="pp-action-target-var" class="form-control">
                                <option value="">— Select Target @Variable —</option>
                                ${varOpts}
                            </select>
                        </label>
                        <label class="etlsql-dsgn-label">On Change
                            <input type="text" id="pp-action-on-change" class="form-control" placeholder="e.g., SET_PARAMETER(@var, value)" value="${esc(v.options?.['action:ON_CHANGE'] || '')}">
                        </label>
                        <label class="etlsql-dsgn-label">Cross-filtering
                            <select id="pp-interaction-on-select" class="form-control">
                                <option value=""${onSelect ? '' : ' selected'}>${esc(defaultLinkLabel)}</option>
                                ${INTERACTION_EFFECTS.map(effect => `<option value="${effect.value}"${onSelect === effect.value ? ' selected' : ''}>${esc(effect.label)}</option>`).join('')}
                                ${onSelect && !INTERACTION_EFFECTS.some(effect => effect.value === onSelect)
            ? `<option value="${esc(onSelect)}" selected>${esc(onSelect)} (authored)</option>` : ''}
                            </select>
                        </label>
                        ${onSelect === 'NONE' ? '' : `<label class="etlsql-dsgn-label">Match selections on
                            <input type="text" id="pp-interaction-matching" class="form-control" spellcheck="false"
                                list="dsgn-match-cols-${esc(v.id)}" value="${esc(matchingColumn)}"
                                placeholder="Auto — this visual\u2019s category field">
                        </label>`}
                        <p class="etlsql-dsgn-interaction-note">${esc(interactionNote)}</p>
                        ${unreadKeys.map(key => `
                        <div class="etlsql-dsgn-unread-key" data-unread-key="${esc(key)}">
                            <p class="etlsql-dsgn-interaction-note">A selection on ${esc((incoming.get(key) || []).join(', '))} arrives as @${esc(key)}, and this visual’s query does not read it, so the selection cannot narrow it.
                            ${filterSourceOn(v.options?.inline_source, key) ? '' : ` Add <code>WHERE ${esc(listFilterCondition(key))}</code> to its query and declare <code>@${esc(key)} LIST = 'All'</code>.`}</p>
                            ${filterSourceOn(v.options?.inline_source, key) ? `<button type="button" class="btn btn-sm" data-filter-on="${esc(key)}">Filter this visual on @${esc(key)}</button>` : ''}
                        </div>`).join('')}
                        ${singlePickKeys.map(key => `
                        <div class="etlsql-dsgn-unread-key" data-single-pick="${esc(key)}">
                            <p class="etlsql-dsgn-interaction-note">This visual matches one selected ${esc(key)} at a time, so a Ctrl+click on several points narrows it to nothing.</p>
                            <button type="button" class="btn btn-sm" data-filter-several="${esc(key)}">Match several ${esc(key)} values</button>
                        </div>`).join('')}
                        ${sendsSelection ? `
                        <fieldset class="etlsql-dsgn-emit-targets" data-emit-targets>
                            <legend class="etlsql-dsgn-label">A selection here reaches</legend>
                            ${emitCandidates.length ? emitCandidates.map(name => {
            const isLinked = linkedNames.has(name.toLowerCase());
            const known = pageVisualNames.has(name.toLowerCase());
            return `<label class="etlsql-dsgn-check">
                                    <input type="checkbox" data-emit-target="${esc(name)}"${emitTargetSet.has(name.toLowerCase()) ? ' checked' : ''}>
                                    ${esc(name)}${!known ? ' <span class="etlsql-dsgn-hint">(not on this page)</span>' : isLinked ? '' : ' <span class="etlsql-dsgn-hint">(not linked)</span>'}
                                </label>`;
        }).join('') : '<p class="etlsql-dsgn-interaction-note">No other visuals on this page yet.</p>'}
                            <p class="etlsql-dsgn-interaction-note" data-emit-note>${esc(emitNote)}</p>
                        </fieldset>` : ''}
                        ${columnDatalist(`dsgn-match-cols-${v.id}`, interactionKeyCandidates(v, colNames))}

                        ${acceptsClickActions ? `
                        <label class="etlsql-dsgn-label">When a data point is clicked
                            <select id="pp-click-kind" class="form-control">
                                ${CLICK_KINDS.map(kind => `<option value="${kind.value}"${clickAction.kind === kind.value ? ' selected' : ''}>${esc(kind.label)}</option>`).join('')}
                            </select>
                        </label>
                        ${clickAction.kind === 'DRILL_DOWN' ? `
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Show details in
                                <select id="pp-click-target" class="form-control">
                                    ${preservingOptions(otherVisualNames, clickAction.target, '— visual —')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Key columns
                                <input type="text" id="pp-click-keys" class="form-control" spellcheck="false"
                                    list="dsgn-match-cols-${esc(v.id)}" value="${esc(clickAction.keys.join(', '))}" placeholder="Region, Year">
                            </label>
                        </div>
                        ${drillUnread.map(key => `<button type="button" class="btn btn-sm" data-drill-read="${esc(key)}">Make ${esc(clickAction.target)} read @${esc(key)}</button>`).join('')}` : ''}
                        ${clickAction.kind === 'DRILL_IN' ? `
                        <label class="etlsql-dsgn-label">Levels, top first
                            <input type="text" id="pp-click-levels" class="form-control" spellcheck="false"
                                list="dsgn-match-cols-${esc(v.id)}" value="${esc(clickAction.levels.join(', '))}" placeholder="Year, Quarter, Month">
                        </label>` : ''}
                        ${clickAction.kind === 'NAVIGATE_PAGE' ? `
                        <label class="etlsql-dsgn-label">Go to page
                            <select id="pp-click-page" class="form-control">
                                ${preservingOptions(pageNames, clickAction.page, '— page —')}
                            </select>
                        </label>` : ''}
                        ${clickAction.kind === 'SET_PARAMETER' ? `
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">Parameter
                                <select id="pp-click-parameter" class="form-control">
                                    ${preservingOptions(declaredParameters.map(item => item.name), clickAction.parameter, '— parameter —')}
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">Set to the clicked
                                <input type="text" id="pp-click-column" class="form-control" spellcheck="false"
                                    list="dsgn-match-cols-${esc(v.id)}" value="${esc(clickAction.column)}" placeholder="column">
                            </label>
                        </div>` : ''}
                        ${clickAction.kind === 'CUSTOM' ? `
                        <label class="etlsql-dsgn-label">Action
                            <input type="text" id="pp-action-on-click" class="form-control" spellcheck="false" placeholder="e.g., CLEAR_FILTERS" value="${esc(clickAction.text)}">
                        </label>` : ''}
                        ${clickNote ? `<p class="etlsql-dsgn-interaction-note" data-click-note>${esc(clickNote)}</p>` : ''}` : ''}
                    </div>
                </details>

                ${acceptsTooltip ? `<details class="etlsql-format-group">
                    <summary>Tooltip</summary>
                    <div class="etlsql-format-group-body" data-tooltip-editor>
                        <label class="etlsql-dsgn-label">Hovering a data point shows
                            <select id="pp-tooltip-kind" class="form-control">
                                ${TOOLTIP_KINDS.filter(kind => kind.value !== 'CUSTOM' || tooltip.kind === 'CUSTOM')
            .map(kind => `<option value="${kind.value}"${tooltip.kind === kind.value ? ' selected' : ''}>${esc(kind.label)}</option>`).join('')}
                            </select>
                        </label>
                        ${tooltip.kind === 'TEXT' ? `
                        <label class="etlsql-dsgn-label">Text
                            <input type="text" id="pp-tooltip-text" class="form-control" value="${esc(tooltip.text)}" placeholder="Revenue for the month">
                        </label>` : ''}
                        ${tooltip.kind === 'FIELDS' || tooltip.kind === 'VISUALS' ? `
                        <label class="etlsql-dsgn-label">Heading
                            <input type="text" id="pp-tooltip-heading" class="form-control" value="${esc(tooltip.heading)}" placeholder="Optional, markdown">
                        </label>` : ''}
                        ${tooltip.kind === 'FIELDS' ? `
                        <div class="etlsql-dsgn-cascade-parents" data-tooltip-fields>
                            ${tooltip.fields.map((field, index) => `
                                <div class="etlsql-dsgn-cascade-parent">
                                    <input type="text" class="form-control" data-tooltip-field="${index}" spellcheck="false"
                                        list="dsgn-match-cols-${esc(v.id)}" value="${esc(field.name)}" placeholder="column" aria-label="Field ${index + 1}">
                                    <input type="text" class="form-control" data-tooltip-format="${index}" spellcheck="false"
                                        list="dsgn-tooltip-formats" value="${esc(field.format)}" placeholder="format" aria-label="Field ${index + 1} format">
                                    <button type="button" class="etlsql-dsgn-cascade-drop" data-tooltip-remove="${index}" aria-label="Remove field">×</button>
                                </div>`).join('')}
                            <button type="button" class="btn btn-sm" id="pp-tooltip-add-field">+ Field</button>
                        </div>
                        <datalist id="dsgn-tooltip-formats">${TOOLTIP_FORMATS.map(format => `<option value="${format.value}">${esc(format.label)}</option>`).join('')}</datalist>` : ''}
                        ${tooltip.kind === 'VISUALS' ? `
                        <fieldset class="etlsql-dsgn-emit-targets" data-tooltip-visuals>
                            <legend class="etlsql-dsgn-label">Visuals in the popover</legend>
                            ${[...otherVisualNames, ...tooltip.visuals.filter(name => !otherVisualNames.some(other => other.toLowerCase() === name.toLowerCase()))]
            .map(name => `<label class="etlsql-dsgn-check">
                                    <input type="checkbox" data-tooltip-visual="${esc(name)}"${tooltip.visuals.some(item => item.toLowerCase() === name.toLowerCase()) ? ' checked' : ''}>
                                    ${esc(name)}
                                </label>`).join('') || '<p class="etlsql-dsgn-interaction-note">No other visuals on this page yet.</p>'}
                        </fieldset>
                        ${hoverUnread.map(other => `<button type="button" class="btn btn-sm" data-hover-filter="${esc(other.name)}">Show ${esc(other.name)} for the hovered ${esc(hoverColumn || '')}</button>`).join('')}` : ''}
                        ${tooltip.kind === 'CONTAINER' ? `
                        <label class="etlsql-dsgn-label">Container
                            <select id="pp-tooltip-container" class="form-control">
                                ${preservingOptions(containerNames, tooltip.name, containerNames.length ? '— container —' : 'No containers on this page')}
                            </select>
                        </label>` : ''}
                        ${tooltip.kind === 'CUSTOM' ? `<pre class="etlsql-dsgn-readonly-clause">${esc(tooltip.text)}</pre>` : ''}
                        ${tooltipNote ? `<p class="etlsql-dsgn-interaction-note" data-tooltip-note>${esc(tooltipNote)}</p>` : ''}
                    </div>
                </details>` : ''}

                ${v.type === 'TABLE' ? `<details class="etlsql-format-group">
                    <summary>Row detail</summary>
                    <div class="etlsql-format-group-body">
                        ${rowDetail && !rowDetail.supported ? `
                        <p class="etlsql-dsgn-interaction-note">This table has a ROW_DETAIL clause Studio cannot read, so it is left exactly as authored. Edit it in the script.</p>
                        <pre class="etlsql-dsgn-readonly-clause">${esc(rowDetail.text)}</pre>` : `
                        <label class="etlsql-dsgn-label">Expanding a row shows
                            <select id="pp-row-detail-target" class="form-control">
                                ${preservingOptions(otherVisualNames, rowDetail?.target, 'Nothing — rows do not expand')}
                            </select>
                        </label>
                        ${rowDetail ? `
                        <div class="etlsql-dsgn-cascade-parents" data-row-detail-bindings>
                            ${rowDetail.bindings.length ? rowDetail.bindings.map((binding, index) => `
                                <div class="etlsql-dsgn-cascade-parent">
                                    <input type="text" class="form-control" data-row-detail-child="${index}" spellcheck="false"
                                        value="${esc(binding.childColumn)}" placeholder="${esc(rowDetail.target)} column" aria-label="${esc(rowDetail.target)} column">
                                    <span class="etlsql-dsgn-hint">=</span>
                                    <input type="text" class="form-control" data-row-detail-parent="${index}" spellcheck="false"
                                        list="dsgn-match-cols-${esc(v.id)}" value="${esc(binding.parentColumn)}" placeholder="this row's column" aria-label="This row's column">
                                    <button type="button" class="etlsql-dsgn-cascade-drop" data-row-detail-remove="${index}" aria-label="Remove match">×</button>
                                </div>`).join('')
            : `<p class="etlsql-dsgn-interaction-note">No match yet, so every row shows all of ${esc(rowDetail.target)}.</p>`}
                            <button type="button" class="btn btn-sm" id="pp-row-detail-add">+ Match a column</button>
                        </div>
                        <label class="etlsql-dsgn-label">Show at most
                            <input type="number" id="pp-row-detail-limit" class="form-control" min="1" value="${rowDetail.limit ?? ''}" placeholder="All rows">
                        </label>
                        <p class="etlsql-dsgn-interaction-note">${esc(rowDetailNote)}</p>` : ''}`}
                    </div>
                </details>` : ''}

                ${isSlicerLike ? `<details class="etlsql-format-group">
                    <summary>Cascading options</summary>
                    <div class="etlsql-format-group-body">
                        ${cascade && !cascade.supported ? `
                        <p class="etlsql-dsgn-interaction-note">This control has a CASCADE clause Studio cannot read, so it is left exactly as authored. Edit it in the script.</p>
                        <pre class="etlsql-dsgn-readonly-clause">${esc(cascade.text)}</pre>` : `
                        <label class="etlsql-dsgn-label">Option set depends on
                            <select id="pp-cascade-mode" class="form-control">
                                <option value=""${cascade ? '' : ' selected'}>Nothing — always the same options</option>
                                <option value="LOCAL"${cascade?.mode === 'LOCAL' ? ' selected' : ''}>Other controls, filtered here (LOCAL)</option>
                                <option value="LIVE"${cascade?.mode === 'LIVE' ? ' selected' : ''}>Other controls, re-queried (LIVE)</option>
                            </select>
                        </label>
                        ${cascade?.mode === 'LOCAL' ? `
                        <div class="etlsql-dsgn-cascade-parents" data-cascade-parents>
                            ${cascade.parents.length ? cascade.parents.map((parent, index) => `
                                <div class="etlsql-dsgn-cascade-parent">
                                    <select class="form-control" data-cascade-parameter="${index}">
                                        ${preservingOptions(declaredParameters.map(item => item.name), parent.parameter, '— parameter —')}
                                    </select>
                                    <input type="text" class="form-control" data-cascade-column="${index}" spellcheck="false"
                                        list="dsgn-match-cols-${esc(v.id)}" value="${esc(parent.column)}" placeholder="column">
                                    <button type="button" class="etlsql-dsgn-cascade-drop" data-cascade-remove="${index}" aria-label="Remove parent binding">×</button>
                                </div>`).join('')
            : '<p class="etlsql-dsgn-interaction-note">No parents yet. LOCAL filtering needs at least one.</p>'}
                            <button type="button" class="btn btn-sm" id="pp-cascade-add-parent"${declaredParameters.length ? '' : ' disabled'}>+ Parent control</button>
                            ${declaredParameters.length ? '' : '<p class="etlsql-dsgn-interaction-note">Declare a parameter first; a parent binding names one.</p>'}
                        </div>` : ''}
                        ${cascade ? `
                        <label class="etlsql-dsgn-label">When a parent change invalidates this selection
                            <select id="pp-cascade-invalid" class="form-control">
                                ${CASCADE_INVALID.map(option => `<option value="${option.value}"${cascade.invalid === option.value ? ' selected' : ''}>${esc(option.label)}</option>`).join('')}
                            </select>
                        </label>
                        <div class="etlsql-dsgn-typography-grid">
                            <label class="etlsql-dsgn-label">A blank parent means
                                <select id="pp-cascade-null" class="form-control">
                                    <option value="ALL"${cascade.nullPolicy === 'ALL' ? ' selected' : ''}>No filter</option>
                                    <option value="MATCH"${cascade.nullPolicy === 'MATCH' ? ' selected' : ''}>Match blanks only</option>
                                </select>
                            </label>
                            <label class="etlsql-dsgn-label">All value
                                <input type="text" id="pp-cascade-all-value" class="form-control" value="${esc(cascade.allValue)}">
                            </label>
                        </div>
                        <label class="etlsql-dsgn-label">With several parent values selected
                            <select id="pp-cascade-multiselect" class="form-control">
                                <option value="ANY"${cascade.multiSelect === 'ANY' ? ' selected' : ''}>Keep rows matching any of them</option>
                                <option value="ALL"${cascade.multiSelect === 'ALL' ? ' selected' : ''}>Keep rows matching all of them</option>
                            </select>
                        </label>
                        <p class="etlsql-dsgn-interaction-note">${esc(cascadeNote)}</p>` : ''}`}
                    </div>
                </details>` : ''}

                <details class="etlsql-format-group" open>
                    <summary>Grid Position & Layout</summary>
                    <div class="etlsql-format-group-body">
                        <div class="etlsql-dsgn-grid4">
                            <label>Col<input type="number" id="pp-col"   class="form-control" min="1" max="12" value="${v.gridCol || 1}"></label>
                            <label>Row<input type="number" id="pp-row"   class="form-control" min="1"          value="${v.gridRow || 1}"></label>
                            <label>W  <input type="number" id="pp-cspan" class="form-control" min="1" max="12" value="${v.gridColSpan || 12}"></label>
                            <label>H  <input type="number" id="pp-rspan" class="form-control" min="1"          value="${v.gridRowSpan || 4}"></label>
                        </div>
                        <label class="etlsql-dsgn-label">Container Group
                            <select id="pp-container-id" class="form-control">
                                <option value="">— none —</option>${cOpts}
                            </select>
                        </label>
                        ${isTabbedParent ? `
                        <label class="etlsql-dsgn-label">Tab / Section
                            <input type="text" id="pp-container-section" class="form-control" placeholder="e.g., Tab 1" value="${esc(v.options?.CONTAINER_SECTION || '')}">
                        </label>` : ''}
                        <div class="etlsql-dsgn-typography-grid" style="margin-top:6px;">
                            <label class="etlsql-dsgn-label">Explicit Width<input id="pp-width" class="form-control" placeholder="auto, 300px, 100%" value="${esc(v.options?.WIDTH || v.width || '')}"></label>
                            <label class="etlsql-dsgn-label">Explicit Height<input id="pp-height" class="form-control" placeholder="auto, 200px, 100%" value="${esc(v.options?.HEIGHT || v.height || '')}"></label>
                        </div>
                        <label class="etlsql-dsgn-label" style="margin-top:6px;">Visual Name (ID)<input id="pp-name" class="form-control" value="${esc(v.name)}"></label>
                        <button class="btn btn-sm etlsql-dsgn-del-btn" id="pp-delete">Remove Visual</button>
                    </div>
                </details>
            </section>
        `;
        on('#pp-name', e => { v.name = controlTarget(e).value; context.renderCanvas(); context.renderTree(); });
        on('#pp-type', e => {
            v.type = controlTarget(e).value;
            if (v.type === 'CUSTOM' && !v.options?.advanced_chart) {
                if (!v.options)
                    v.options = {};
                v.options.advanced_chart = defaultCustomChart;
            }
            else if (v.type === 'HTML' && !v.options?.html_template) {
                if (!v.options)
                    v.options = {};
                v.options.html_mode = 'SINGLE';
                v.options.html_template = `<article class="custom-card">\n  <h3>{{Title}}</h3>\n  <p>{{Description}}</p>\n</article>`;
                v.options.html_style = `.custom-card {\n  padding: 12px;\n  border: 1px solid var(--portal-border, #e2e8f0);\n  border-radius: 6px;\n}`;
                v.options.html_fallback = 'Custom HTML: {{Title}} - {{Description}}';
            }
            context.renderCanvas();
            context.renderTree();
            renderProps();
        });
        on('#pp-container-id', e => { v.containerId = controlTarget(e).value || null; context.renderTree(); context.renderCanvas(); renderProps(); });
        if (isTabbedParent) {
            on('#pp-container-section', e => { if (!v.options)
                v.options = {}; if (controlTarget(e).value.trim())
                v.options.CONTAINER_SECTION = controlTarget(e).value.trim();
            else
                delete v.options.CONTAINER_SECTION; context.syncScriptFromGridDebounced(); });
        }
        on('#pp-title', e => {
            v.title = controlTarget(e).value;
            if (v.formatting?.title)
                v.formatting.title.text = controlTarget(e).value;
            context.renderCanvas();
        });
        on('#pp-ds', e => { v.dataset = controlTarget(e).value || null; });
        on('#pp-width', e => { if (!v.options)
            v.options = {}; if (controlTarget(e).value.trim())
            v.options.WIDTH = controlTarget(e).value.trim();
        else
            delete v.options.WIDTH; });
        on('#pp-height', e => { if (!v.options)
            v.options = {}; if (controlTarget(e).value.trim())
            v.options.HEIGHT = controlTarget(e).value.trim();
        else
            delete v.options.HEIGHT; });
        on('#pp-action-target-var', e => {
            const selectedVar = controlTarget(e).value;
            if (!selectedVar)
                return;
            if (!v.options)
                v.options = {};
            const col = mappings['Category'] || mappings['Value'] || 'value';
            const actionStr = `SET_PARAMETER(${selectedVar}, ${col})`;
            v.options['action:ON_CHANGE'] = actionStr;
            const input = queryElement(context.propsPanel, '#pp-action-on-change');
            if (input) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */
                (input).value = actionStr;
            context.syncScriptFromGridDebounced();
        });
        // These three wrote to the in-memory visual and never to the script: an author could set an
        // action or an interaction, watch the control keep the value, save, and find nothing there.
        on('#pp-action-on-change', e => { if (!v.options)
            v.options = {}; const val = controlTarget(e).value.trim(); if (val)
            v.options['action:ON_CHANGE'] = val;
        else
            delete v.options['action:ON_CHANGE']; context.syncScriptFromGridDebounced(); });
        // ── Click action ──────────────────────────────────────────────────────
        const commitClick = (next) => {
            if (!v.options)
                v.options = {};
            const text = writeClickAction(next);
            if (text) {
                v.options['action:ON_CLICK'] = text;
                clickDrafts.delete(v.id);
            }
            else {
                delete v.options['action:ON_CLICK'];
                if (next.kind === 'NONE')
                    clickDrafts.delete(v.id);
                else
                    clickDrafts.set(v.id, next);
            }
            renderProps();
            context.syncScriptFromGridDebounced();
        };
        on('#pp-click-kind', e => {
            const kind = controlTarget(e).value;
            const current = writeClickAction(clickAction) || '';
            const blank = {
                NONE: { kind: 'NONE' },
                DRILL_DOWN: { kind: 'DRILL_DOWN', target: '', keys: [] },
                DRILL_IN: { kind: 'DRILL_IN', levels: [] },
                NAVIGATE_PAGE: { kind: 'NAVIGATE_PAGE', page: '' },
                SET_PARAMETER: { kind: 'SET_PARAMETER', parameter: '', column: '' },
                // Switching to custom starts from what the guided editor wrote, so the author can
                // extend it rather than retype it.
                CUSTOM: { kind: 'CUSTOM', text: current },
            };
            commitClick(blank[kind]);
        });
        on('#pp-click-target', e => {
            if (clickAction.kind === 'DRILL_DOWN')
                commitClick({ ...clickAction, target: controlTarget(e).value });
        });
        on('#pp-click-keys', e => {
            if (clickAction.kind === 'DRILL_DOWN')
                commitClick({ ...clickAction, keys: splitNames(controlTarget(e).value) });
        });
        on('#pp-click-levels', e => {
            if (clickAction.kind === 'DRILL_IN')
                commitClick({ ...clickAction, levels: splitNames(controlTarget(e).value) });
        });
        on('#pp-click-page', e => {
            if (clickAction.kind === 'NAVIGATE_PAGE')
                commitClick({ ...clickAction, page: controlTarget(e).value });
        });
        on('#pp-click-parameter', e => {
            if (clickAction.kind === 'SET_PARAMETER')
                commitClick({ ...clickAction, parameter: controlTarget(e).value });
        });
        on('#pp-click-column', e => {
            if (clickAction.kind === 'SET_PARAMETER')
                commitClick({ ...clickAction, column: controlTarget(e).value.trim() });
        });
        on('#pp-action-on-click', e => commitClick({ kind: 'CUSTOM', text: controlTarget(e).value }));
        // ── Make a query read the parameter a selection or a drill-down sets ──
        // Writes the documented pattern into the visual's own source and declares the parameter as a
        // LIST with 'All' as its resting value: the unfiltered report still shows every row, and a
        // Ctrl+click on several points matches each of them.
        const declareListParameter = (column) => {
            const name = `@${column}`;
            const parameters = context.state.parameters ?? (context.state.parameters = []);
            const existing = parameters.find(parameter => String(parameter.name).toLowerCase() === name.toLowerCase());
            if (!existing) {
                parameters.push({
                    name, dataType: 'LIST', initialValue: "'All'",
                    isInput: false, isOutput: false, isRequired: false, isSensitive: false, isBlockScoped: false,
                });
            }
            else if (String(existing.dataType).toUpperCase() !== 'LIST' && String(existing.initialValue).trim() === "'All'") {
                // Studio's earlier single-value declaration; a hand-written one is the author's.
                existing.dataType = 'LIST';
            }
        };
        const readParameterIn = (target, column) => {
            const next = filterSourceOn(target.options?.inline_source, column);
            if (!next)
                return;
            if (!target.options)
                target.options = {};
            target.options.inline_source = next;
            declareListParameter(column);
            renderProps();
            context.syncScriptFromGridDebounced();
        };
        queryElements(context.propsPanel, '[data-filter-several]').forEach(button => {
            button.addEventListener('click', () => {
                const column = datasetValue(button, 'filterSeveral');
                const next = filterSourceOnSeveral(v.options?.inline_source, column);
                if (!next || !v.options)
                    return;
                v.options.inline_source = next;
                declareListParameter(column);
                renderProps();
                context.syncScriptFromGridDebounced();
            });
        });
        queryElements(context.propsPanel, '[data-filter-on]').forEach(button => {
            button.addEventListener('click', () => readParameterIn(v, datasetValue(button, 'filterOn')));
        });
        queryElements(context.propsPanel, '[data-drill-read]').forEach(button => {
            button.addEventListener('click', () => {
                if (drillTarget)
                    readParameterIn(drillTarget, datasetValue(button, 'drillRead'));
            });
        });
        // ── Who a selection reaches ───────────────────────────────────────────
        queryElements(context.propsPanel, '[data-emit-target]').forEach(box => {
            box.addEventListener('change', () => {
                if (!v.options)
                    v.options = {};
                const ticked = Array.from(queryElements(context.propsPanel, '[data-emit-target]'))
                    .filter(item => item.checked)
                    .map(item => datasetValue(item, 'emitTarget'))
                    .filter(Boolean);
                if (ticked.length)
                    v.options.emit_filter = ticked.join(', ');
                else
                    delete v.options.emit_filter;
                renderProps();
                context.syncScriptFromGridDebounced();
            });
        });
        // ── Tooltip ───────────────────────────────────────────────────────────
        const commitTooltip = (next) => {
            if (!v.options)
                v.options = {};
            const text = writeTooltip(next);
            if (text)
                v.options.tooltip = text;
            else
                delete v.options.tooltip;
            const unfinished = next.kind === 'FIELDS' && next.fields.some(field => !field.name.trim());
            if (next.kind !== 'NONE' && (!text || unfinished))
                tooltipDrafts.set(v.id, next);
            else
                tooltipDrafts.delete(v.id);
            renderProps();
            context.syncScriptFromGridDebounced();
        };
        const editTooltip = (kind, change) => {
            if (tooltip.kind !== kind)
                return;
            const next = structuredClone(tooltip);
            change(next);
            commitTooltip(next);
        };
        on('#pp-tooltip-kind', e => {
            const kind = controlTarget(e).value;
            // A field list starts from what the visual already plots, so it shows something at once.
            const plotted = ['X', 'Y', 'VALUE', 'CATEGORY']
                .map(role => String(v.mappings?.[role] || '').trim())
                .filter(column => /^[A-Za-z_][A-Za-z0-9_]*$/.test(column));
            const blank = {
                NONE: { kind: 'NONE' },
                TEXT: { kind: 'TEXT', text: '' },
                FIELDS: { kind: 'FIELDS', heading: '', fields: [...new Set(plotted)].map(name => ({ name, format: '' })) },
                VISUALS: { kind: 'VISUALS', heading: '', visuals: [] },
                CONTAINER: { kind: 'CONTAINER', name: containerNames[0] || '' },
                CUSTOM: tooltip,
            };
            commitTooltip(blank[kind]);
        });
        on('#pp-tooltip-text', e => editTooltip('TEXT', next => { next.text = controlTarget(e).value; }));
        on('#pp-tooltip-heading', e => {
            const heading = controlTarget(e).value;
            if (tooltip.kind === 'FIELDS')
                editTooltip('FIELDS', next => { next.heading = heading; });
            else
                editTooltip('VISUALS', next => { next.heading = heading; });
        });
        on('#pp-tooltip-container', e => editTooltip('CONTAINER', next => { next.name = controlTarget(e).value; }));
        queryElement(context.propsPanel, '#pp-tooltip-add-field')?.addEventListener('click', () => editTooltip('FIELDS', next => { next.fields.push({ name: '', format: '' }); }));
        queryElements(context.propsPanel, '[data-tooltip-remove]').forEach(button => {
            button.addEventListener('click', () => editTooltip('FIELDS', next => {
                next.fields.splice(Number(datasetValue(button, 'tooltipRemove')), 1);
            }));
        });
        queryElements(context.propsPanel, '[data-tooltip-field], [data-tooltip-format]').forEach(input => {
            input.addEventListener('change', () => editTooltip('FIELDS', next => {
                const field = datasetValue(input, 'tooltipField');
                const entry = next.fields[Number(field ?? datasetValue(input, 'tooltipFormat'))];
                if (!entry)
                    return;
                if (field != null)
                    entry.name = input.value.trim();
                else
                    entry.format = input.value.trim();
            }));
        });
        queryElements(context.propsPanel, '[data-tooltip-visual]').forEach(box => {
            box.addEventListener('change', () => editTooltip('VISUALS', next => {
                next.visuals = Array.from(queryElements(context.propsPanel, '[data-tooltip-visual]'))
                    .filter(item => item.checked)
                    .map(item => datasetValue(item, 'tooltipVisual'))
                    .filter(Boolean);
            }));
        });
        // Writes `WHERE <hovered column> = @hover_value` into a popover visual's own source.
        queryElements(context.propsPanel, '[data-hover-filter]').forEach(button => {
            button.addEventListener('click', () => {
                const target = popoverVisuals.find(other => other.name === datasetValue(button, 'hoverFilter'));
                const next = target && hoverColumn ? filterSourceOnHover(target.options?.inline_source, hoverColumn) : null;
                if (!target || !next)
                    return;
                if (!target.options)
                    target.options = {};
                target.options.inline_source = next;
                renderProps();
                context.syncScriptFromGridDebounced();
            });
        });
        // ── Row detail ────────────────────────────────────────────────────────
        const commitRowDetail = (next) => {
            if (!v.options)
                v.options = {};
            if (next)
                v.options.row_detail = writeRowDetail(next);
            else
                delete v.options.row_detail;
            if (next && next.bindings.some(binding => !binding.childColumn || !binding.parentColumn))
                rowDetailDrafts.set(v.id, next);
            else
                rowDetailDrafts.delete(v.id);
            renderProps();
            context.syncScriptFromGridDebounced();
        };
        const editRowDetail = (change) => {
            if (!rowDetail?.supported)
                return;
            const next = { ...rowDetail, bindings: rowDetail.bindings.map(binding => ({ ...binding })) };
            change(next);
            commitRowDetail(next);
        };
        on('#pp-row-detail-target', e => {
            const target = controlTarget(e).value;
            if (!target) {
                commitRowDetail(null);
                return;
            }
            commitRowDetail(rowDetail?.supported
                ? { ...rowDetail, target }
                : { supported: true, target, bindings: [], limit: null });
        });
        on('#pp-row-detail-limit', e => editRowDetail(next => {
            const limit = Number.parseInt(controlTarget(e).value, 10);
            next.limit = Number.isFinite(limit) && limit > 0 ? limit : null;
        }));
        queryElement(context.propsPanel, '#pp-row-detail-add')?.addEventListener('click', () => editRowDetail(next => { next.bindings.push({ childColumn: '', parentColumn: '' }); }));
        queryElements(context.propsPanel, '[data-row-detail-remove]').forEach(button => {
            button.addEventListener('click', () => editRowDetail(next => {
                next.bindings.splice(Number(datasetValue(button, 'rowDetailRemove')), 1);
            }));
        });
        queryElements(context.propsPanel, '[data-row-detail-child], [data-row-detail-parent]').forEach(input => {
            input.addEventListener('change', () => editRowDetail(next => {
                const child = datasetValue(input, 'rowDetailChild');
                const index = Number(child || datasetValue(input, 'rowDetailParent'));
                const binding = next.bindings[index];
                if (!binding)
                    return;
                const value = input.value.trim().replace(/^@/, '');
                if (child)
                    binding.childColumn = value;
                else
                    binding.parentColumn = value;
            }));
        });
        on('#pp-interaction-on-select', e => {
            if (!v.options)
                v.options = {};
            const val = controlTarget(e).value.trim().toUpperCase();
            if (val)
                v.options['interaction:ON_SELECT'] = val;
            else
                delete v.options['interaction:ON_SELECT'];
            // NONE means this visual never reacts, so a match column would describe nothing.
            if (val === 'NONE')
                delete v.options['interaction:MATCHING'];
            renderProps();
            context.syncScriptFromGridDebounced();
        });
        on('#pp-interaction-matching', e => {
            if (!v.options)
                v.options = {};
            const val = controlTarget(e).value.trim();
            if (val)
                v.options['interaction:MATCHING'] = val;
            else
                delete v.options['interaction:MATCHING'];
            renderProps();
            context.syncScriptFromGridDebounced();
        });
        // ── Cascade ───────────────────────────────────────────────────────────
        // Every edit rewrites the whole clause from the fields, in the serializer's own shape, so a
        // parse of what Studio wrote produces the text Studio would write again.
        const commitCascade = (next) => {
            if (!v.options)
                v.options = {};
            if (next)
                v.options.cascade = writeCascade(next);
            else
                delete v.options.cascade;
            renderProps();
            context.syncScriptFromGridDebounced();
        };
        const editCascade = (change) => {
            if (!cascade?.supported)
                return;
            const next = { ...cascade, parents: cascade.parents.map((parent) => ({ ...parent })) };
            change(next);
            commitCascade(next);
        };
        on('#pp-cascade-mode', e => {
            const mode = controlTarget(e).value;
            if (!mode) {
                commitCascade(null);
                return;
            }
            const cascadeMode = mode;
            const base = cascade?.supported ? cascade : null;
            commitCascade({
                supported: true,
                text: '',
                mode: cascadeMode,
                // LIVE infers its parents from the parameters its own query names, and the parser
                // rejects PARENTS there, so switching to LIVE drops them rather than writing a
                // clause that will not parse.
                parents: cascadeMode === 'LOCAL' ? (base?.parents ?? []) : [],
                invalid: base?.invalid ?? 'CLEAR',
                nullPolicy: base?.nullPolicy ?? 'ALL',
                allValue: base?.allValue ?? '*',
                multiSelect: base?.multiSelect ?? 'ANY',
            });
        });
        on('#pp-cascade-invalid', e => editCascade(next => { next.invalid = controlTarget(e).value; }));
        on('#pp-cascade-null', e => editCascade(next => { next.nullPolicy = controlTarget(e).value; }));
        on('#pp-cascade-all-value', e => editCascade(next => { next.allValue = controlTarget(e).value; }));
        on('#pp-cascade-multiselect', e => editCascade(next => { next.multiSelect = controlTarget(e).value; }));
        queryElement(context.propsPanel, '#pp-cascade-add-parent')?.addEventListener('click', () => editCascade(next => {
            next.parents.push({
                parameter: declaredParameters[0]?.name || '@parameter',
                column: interactionKeyCandidates(v, colNames)[0] || '',
            });
        }));
        queryElements(context.propsPanel, '[data-cascade-parameter]').forEach(select => select.addEventListener('change', () => editCascade(next => { next.parents[Number(datasetValue(select, 'cascadeParameter'))].parameter = select.value; })));
        queryElements(context.propsPanel, '[data-cascade-column]').forEach(select => select.addEventListener('change', () => editCascade(next => { next.parents[Number(datasetValue(select, 'cascadeColumn'))].column = select.value; })));
        queryElements(context.propsPanel, '[data-cascade-remove]').forEach(button => button.addEventListener('click', () => editCascade(next => { next.parents.splice(Number(/** @type {HTMLElement} */ (button).dataset.cascadeRemove), 1); })));
        on('#pp-col', e => { v.gridCol = +controlTarget(e).value || 1; context.renderCanvas(); });
        on('#pp-row', e => { v.gridRow = +controlTarget(e).value || 1; context.renderCanvas(); });
        on('#pp-cspan', e => { v.gridColSpan = +controlTarget(e).value || 12; context.renderCanvas(); });
        on('#pp-rspan', e => { v.gridRowSpan = +controlTarget(e).value || 4; context.renderCanvas(); });
        if (isCustomChart) {
            const chartInput = queryElement(context.propsPanel, '#pp-chart-code');
            if (chartInput) {
                chartInput.addEventListener('input', ev => {
                    if (!v.options)
                        v.options = {};
                    v.options.advanced_chart = controlTarget(ev).value;
                    context.renderCanvas();
                    context.syncScriptFromGridDebounced();
                });
            }
            const coordInput = queryElement(context.propsPanel, '#pp-chart-coord');
            if (coordInput) {
                coordInput.addEventListener('change', ev => {
                    if (!v.options)
                        v.options = {};
                    let cur = v.options.advanced_chart || chartCode;
                    if (/COORDINATE\s*\(\s*TYPE\s*=\s*[A-Z_]+\s*\)/i.test(cur)) {
                        const coordinate = controlTarget(ev).value === 'GEOGRAPHIC'
                            ? "COORDINATE (TYPE = GEOGRAPHIC, PROJECTION = EQUIRECTANGULAR, MAP_NAME = 'WORLD', FEATURE_KEY = 'name')"
                            : `COORDINATE (TYPE = ${controlTarget(ev).value})`;
                        cur = cur.replace(/COORDINATE\s*\(\s*TYPE\s*=\s*[A-Z_]+\s*\)/i, coordinate);
                    }
                    v.options.advanced_chart = cur;
                    if (chartInput) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */
                        (chartInput).value = cur;
                    context.renderCanvas();
                    context.syncScriptFromGridDebounced();
                });
            }
            const markInput = queryElement(context.propsPanel, '#pp-chart-primary-mark');
            if (markInput) {
                markInput.addEventListener('change', ev => {
                    if (!v.options)
                        v.options = {};
                    let cur = v.options.advanced_chart || chartCode;
                    const markPattern = /\b(RECT|LINE|AREA|POINT|RULE|ARC|TEXT|TICK)\b/i;
                    if (markPattern.test(cur)) {
                        cur = cur.replace(markPattern, controlTarget(ev).value);
                    }
                    v.options.advanced_chart = cur;
                    if (chartInput) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */
                        (chartInput).value = cur;
                    context.renderCanvas();
                    context.syncScriptFromGridDebounced();
                });
            }
            const recipeInput = queryElement(context.propsPanel, '#pp-chart-recipe');
            if (recipeInput) {
                recipeInput.addEventListener('change', ev => {
                    const recipes = {
                        'boxplot-mean': boxPlotMeanRecipe,
                        'candlestick-volume': candlestickVolumeRecipe,
                        'layered-map': layeredMapRecipe
                    };
                    const replacement = recipes[controlTarget(ev).value];
                    if (!replacement)
                        return;
                    if (!v.options)
                        v.options = {};
                    v.options.advanced_chart = replacement;
                    if (chartInput) /** @type {HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement} */
                        (chartInput).value = replacement;
                    context.renderCanvas();
                    context.syncScriptFromGridDebounced();
                });
            }
        }
        else if (isHtmlVisual) {
            on('#pp-html-mode', e => {
                if (!v.options)
                    v.options = {};
                v.options.html_mode = controlTarget(e).value;
                context.renderCanvas();
                context.syncScriptFromGridDebounced();
            });
            on('#pp-html-template', e => {
                if (!v.options)
                    v.options = {};
                v.options.html_template = controlTarget(e).value;
                context.renderCanvas();
                context.syncScriptFromGridDebounced();
            });
            on('#pp-html-style', e => {
                if (!v.options)
                    v.options = {};
                if (controlTarget(e).value.trim())
                    v.options.html_style = controlTarget(e).value.trim();
                else
                    delete v.options.html_style;
                context.renderCanvas();
                context.syncScriptFromGridDebounced();
            });
            on('#pp-html-fallback', e => {
                if (!v.options)
                    v.options = {};
                if (controlTarget(e).value.trim())
                    v.options.html_fallback = controlTarget(e).value.trim();
                else
                    delete v.options.html_fallback;
                context.syncScriptFromGridDebounced();
            });
        }
        else {
            for (const role of ROLES) {
                const input = queryElement(context.propsPanel, `[data-role="${role}"]`);
                const aggSelect = queryElement(context.propsPanel, `[data-role-agg="${role}"]`);
                if (aggSelect) {
                    aggSelect.addEventListener('change', () => {
                        const agg = aggSelect.value;
                        const currentVal = v.mappings?.[role] || input?.value || '';
                        const parsed = parseRoleAggregate(currentVal);
                        const col = parsed.column;
                        if (!v.mappings)
                            v.mappings = {};
                        if (!col) {
                            renderProps();
                            return;
                        }
                        if (agg === 'NONE') {
                            v.mappings[role] = col;
                        }
                        else {
                            v.mappings[role] = aggregateExpression(agg, col);
                        }
                        if (input)
                            input.value = v.mappings[role];
                        context.renderCanvas();
                        renderProps();
                        context.syncScriptFromGridDebounced();
                    });
                }
                if (!input)
                    continue;
                input.addEventListener('change', ev => {
                    if (!v.mappings)
                        v.mappings = {};
                    const val = controlTarget(ev).value.trim();
                    if (val)
                        v.mappings[role] = val;
                    else
                        delete v.mappings[role];
                    context.renderCanvas();
                    renderProps();
                    context.syncScriptFromGridDebounced();
                });
                input.addEventListener('dragover', e => {
                    e.preventDefault();
                    input.classList.add('drag-over');
                });
                input.addEventListener('dragleave', () => input.classList.remove('drag-over'));
                input.addEventListener('drop', ((e) => {
                    e.preventDefault();
                    input.classList.remove('drag-over');
                    const col = e.dataTransfer?.getData('text/plain');
                    if (col) {
                        const currentAgg = aggSelect?.value || 'NONE';
                        input.value = currentAgg !== 'NONE' ? aggregateExpression(currentAgg, col) : col;
                        input.dispatchEvent(new Event('change'));
                    }
                }));
            }
        }
        context.bindFormattingSection(context.propsPanel, v, context.renderCanvas, context.syncScriptFromGridDebounced);
        context.bindVisualFormatInspector(context.propsPanel, v, colNames, renderProps);
        context.bindInspectorSearch(context.propsPanel);
        queryElement(context.propsPanel, '#pp-delete')?.addEventListener('click', () => context.deleteVisual(v.id));
    }
    return { renderProps };
}
