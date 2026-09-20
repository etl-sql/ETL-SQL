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
import { VTYPES, controlTarget, datasetValue, queryElement, queryElements } from './designer-context.js';
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
        { value: 'HIGHLIGHT', label: 'Highlight matching data', note: 'Keeps every row and dims the rest.' },
        { value: 'FILTER', label: 'Filter to matching rows', note: 'Re-queries this visual and hides the rest.' },
        { value: 'NONE', label: 'Ignore selections elsewhere', note: 'This visual never reacts to another one.' },
    ];
    const ROLES = ['X', 'Y', 'VALUE', 'CATEGORY', 'SERIES', 'LABEL', 'TOOLTIP'];
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
            const heading = String(queryElement(details, 'summary')?.textContent || '').trim();
            if (!heading)
                continue;
            // A group the markup opens by default stays open and is recorded, so closing it sticks.
            if (details.open)
                context.openInspectorGroups.add(heading);
            else if (context.openInspectorGroups.has(heading))
                details.open = true;
        }
    }
    function renderProps() {
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
        const interactionEffect = INTERACTION_EFFECTS.find(effect => effect.value === onSelect);
        const interactionNote = onSelect === 'NONE'
            ? 'Selecting data in another visual leaves this one alone.'
            : `${interactionEffect ? interactionEffect.note : 'Selecting data in another visual dims the rows that do not match.'}`
                + (matchingColumn
                    ? ` Rows are matched on ${matchingColumn}.`
                    : ' Rows are matched on this visual\u2019s category field.');
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
                        <label class="etlsql-dsgn-label">On Click
                            <input type="text" id="pp-action-on-click" class="form-control" placeholder="e.g., DRILL_DOWN(Target = Tbl, Key = region)" value="${esc(v.options?.['action:ON_CLICK'] || '')}">
                        </label>
                        <label class="etlsql-dsgn-label">When another visual is selected
                            <select id="pp-interaction-on-select" class="form-control">
                                <option value=""${onSelect ? '' : ' selected'}>Default — highlight matching data</option>
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
                        ${columnDatalist(`dsgn-match-cols-${v.id}`, interactionKeyCandidates(v, colNames))}
                    </div>
                </details>

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
        on('#pp-action-on-click', e => { if (!v.options)
            v.options = {}; const val = controlTarget(e).value.trim(); if (val)
            v.options['action:ON_CLICK'] = val;
        else
            delete v.options['action:ON_CLICK']; context.syncScriptFromGridDebounced(); });
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
