// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring-report.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-report.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Report parameters, table details, totals, and page furniture.
 */
import { asButton, asHtml, asInput, asSelect, STUDIO_PARAMETER_TYPES, STUDIO_TOTAL_AGGREGATES } from './studio-authoring-context.js';
import { escapeHtml, noteMarkup as guidedNoteMarkup, mutationExplanationMarkup, sqlPreviewMarkup } from './studio-authoring-ui.js';
export function createStudioAuthoringReport(hostContext) {
    // --- Steps 2-8 --------------------------------------------------------------------------------
    /** A parameter keeps the author's casing; only dataset names are lowercased. */
    function parameterName(seed) {
        const cleaned = String(seed || 'parameter').replace(/^@/, '').replace(/[^A-Za-z0-9_]/g, '_').replace(/^_+/, '');
        return /^[A-Za-z]/.test(cleaned) ? cleaned : `p_${cleaned || 'arameter'}`;
    }
    /**
     * The parameter manager: list, add, edit, delete.
     *
     * A parameter is the most-used concept after data and the only one an author revisits — a default
     * changes, a prompt gets a better name, a draft parameter turns out to be unnecessary. An add-only
     * dialog left every one of those as a trip to the script.
     *
     * Declarations inside a block are listed but not editable. The patcher deliberately never touches
     * them: a DECLARE inside procedural code is not part of the report's parameter list, and offering
     * Edit on one would silently do nothing.
     */
    async function runParameterStep() {
        const draftFor = (parameter) => ({
            original: parameter?.name ?? null,
            name: parameterName(parameter?.name ?? 'region'),
            type: parameter?.dataType ?? 'VARCHAR',
            initial: parameter?.initialValue ?? "'All'",
            prompt: parameter?.isInput ?? true,
            required: parameter?.isRequired ?? false,
            sensitive: parameter?.isSensitive ?? false,
        });
        const declarationSql = (draft) => {
            const initial = draft.initial.trim() ? ` = ${draft.initial.trim()}` : '';
            const flags = [
                draft.sensitive ? ' PASSWORD' : '',
                draft.prompt ? ' INPUT' : '',
                draft.required ? ' REQUIRED' : '',
            ].join('');
            return `DECLARE @${parameterName(draft.name)} ${draft.type.trim() || 'VARCHAR'}${initial}${flags};`;
        };
        await hostContext.studioDialog({ kicker: 'Step 2 · Define parameters', title: 'Report parameters', wide: true }, api => {
            let parameters = null;
            const load = async () => {
                try {
                    const parsed = await hostContext.request(hostContext.routes.parse, { body: { script: hostContext.shell.getScriptText() } });
                    parameters = parsed.designState?.parameters || [];
                }
                catch {
                    parameters = [];
                }
                paintList();
            };
            const flagLabels = (parameter) => [
                parameter.isInput ? 'prompts' : null,
                parameter.isRequired ? 'required' : null,
                parameter.isSensitive ? 'sensitive' : null,
                parameter.isOutput ? 'output' : null,
            ].filter(Boolean).join(' · ');
            const paintList = () => api.render({
                lede: 'A <strong>parameter</strong> is a value supplied before the report runs. Marked as a prompt it '
                    + 'appears as a field the reader fills in; either way a dataset query can filter on it.',
                body: parameters === null
                    ? '<div class="etlsql-studio-loading">Reading the script…</div>'
                    : (parameters.length
                        ? `<div class="etlsql-studio-parameter-list">${parameters.map((parameter, index) => `
                            <div class="etlsql-studio-parameter-row${parameter.isBlockScoped ? ' is-readonly' : ''}">
                                <div>
                                    <strong>${escapeHtml(parameter.name)}</strong>
                                    <span>${escapeHtml(parameter.dataType)}${parameter.initialValue ? ` = ${escapeHtml(parameter.initialValue)}` : ''}</span>
                                    ${flagLabels(parameter) ? `<small>${escapeHtml(flagLabels(parameter))}</small>` : ''}
                                </div>
                                ${parameter.isBlockScoped
                            ? '<span class="etlsql-studio-parameter-locked">Declared in a block · edit it in the script</span>'
                            : `<div class="etlsql-studio-parameter-actions">
                                        <button type="button" class="etlsql-studio-btn" data-edit-parameter="${index}">Edit</button>
                                        <button type="button" class="etlsql-studio-btn" data-delete-parameter="${index}">Delete</button>
                                    </div>`}
                            </div>`).join('')}</div>`
                        : guidedNoteMarkup('This report declares no parameters yet.', 'info')),
                actions: [
                    { id: 'close', label: 'Done', run: () => api.close(null) },
                    { id: 'add', label: 'Add a parameter', primary: true, run: () => paintForm(draftFor(null)) },
                ],
                wire: host => {
                    host.querySelectorAll('[data-edit-parameter]').forEach(button => button.addEventListener('click', () => {
                        if (parameters)
                            paintForm(draftFor(parameters[Number(asHtml(button).dataset.editParameter)]));
                    }));
                    host.querySelectorAll('[data-delete-parameter]').forEach(button => button.addEventListener('click', () => {
                        if (parameters)
                            paintDelete(parameters[Number(asHtml(button).dataset.deleteParameter)]);
                    }));
                },
            });
            const paintForm = (draft) => {
                const isEdit = Boolean(draft.original);
                const collides = (parameters || []).some(parameter => parameter.name.toLowerCase() === `@${parameterName(draft.name)}`.toLowerCase()
                    && parameter.name !== draft.original);
                api.render({
                    lede: isEdit
                        ? `Editing <strong>${escapeHtml(draft.original)}</strong>. Renaming rewrites the declaration; references elsewhere in the script are not renamed for you.`
                        : 'Name the value, choose its type, and decide whether the reader is prompted for it.',
                    body: `
                        <label class="etlsql-studio-guided-field"><span>Name</span>
                            <div class="etlsql-studio-prefixed-input"><span>@</span>
                            <input type="text" data-parameter-name value="${escapeHtml(draft.name)}" spellcheck="false"></div></label>
                        <label class="etlsql-studio-guided-field"><span>Type</span>
                            <input type="text" data-parameter-type list="etlsql-parameter-types" value="${escapeHtml(draft.type)}" spellcheck="false">
                            <datalist id="etlsql-parameter-types">${STUDIO_PARAMETER_TYPES.map(type => `<option value="${type}"></option>`).join('')}</datalist></label>
                        <p class="etlsql-studio-guided-hint">Free text, so a sized type such as <code>VARCHAR(50)</code> is kept exactly as written.</p>
                        <label class="etlsql-studio-guided-field"><span>Default value</span>
                            <input type="text" data-parameter-initial value="${escapeHtml(draft.initial)}" spellcheck="false" placeholder="'All'"></label>
                        <label class="etlsql-studio-guided-check"><input type="checkbox" data-parameter-prompt ${draft.prompt ? 'checked' : ''}>
                            Prompt the reader for this value (INPUT)</label>
                        <label class="etlsql-studio-guided-check"><input type="checkbox" data-parameter-required ${draft.required ? 'checked' : ''}>
                            Require a value before the report runs (REQUIRED)</label>
                        <label class="etlsql-studio-guided-check"><input type="checkbox" data-parameter-sensitive ${draft.sensitive ? 'checked' : ''}>
                            Hide the value as a secret (PASSWORD)</label>
                        <p class="etlsql-studio-guided-hint">Text defaults need quotes, exactly as they appear in the script.</p>`
                        + (collides ? guidedNoteMarkup('Another parameter already uses that name.', 'warning') : '')
                        + sqlPreviewMarkup(declarationSql(draft), isEdit
                            ? `Rewrites the declaration of ${draft.name} in place. Queries and visuals that already `
                                + 'reference it keep working; a changed type or default takes effect on the next run.'
                            : `Declares ${draft.name} near the top of the script. Nothing uses it until you reference `
                                + 'it in a dataset query, a filter, or a slicer.'),
                    actions: [
                        { id: 'back', label: 'Back', run: paintList },
                        {
                            id: isEdit ? 'save' : 'add',
                            label: isEdit ? 'Save parameter' : 'Add parameter',
                            primary: true,
                            disabled: collides,
                            run: () => apply(draft),
                        },
                    ],
                    wire: host => {
                        const bind = (selector, read) => {
                            const el = host.querySelector(selector);
                            el?.addEventListener('change', event => {
                                read(asInput(event.target));
                                paintForm(draft);
                            });
                        };
                        bind('[data-parameter-name]', input => { draft.name = input.value; });
                        bind('[data-parameter-type]', input => { draft.type = input.value; });
                        bind('[data-parameter-initial]', input => { draft.initial = input.value; });
                        bind('[data-parameter-prompt]', input => { draft.prompt = input.checked; });
                        bind('[data-parameter-required]', input => { draft.required = input.checked; });
                        bind('[data-parameter-sensitive]', input => { draft.sensitive = input.checked; });
                    },
                });
            };
            const apply = async (draft) => {
                const name = `@${parameterName(draft.name)}`;
                api.busy(true);
                const written = await hostContext.mutate(draft.original ? `Edit parameter ${draft.original}` : `Add parameter ${name}`, design => {
                    design.parameters ||= [];
                    const next = {
                        name,
                        dataType: draft.type.trim() || 'VARCHAR',
                        initialValue: draft.initial.trim() || null,
                        isInput: draft.prompt,
                        isOutput: false,
                        isRequired: draft.required,
                        isSensitive: draft.sensitive,
                    };
                    // Replacing in place keeps the declaration where the author put it. A rename is a
                    // replace too: the patcher removes the old name and writes the new one.
                    const at = draft.original
                        ? design.parameters.findIndex((parameter) => parameter.name === draft.original)
                        : -1;
                    if (at >= 0)
                        design.parameters[at] = next;
                    else
                        design.parameters.push(next);
                    return name;
                });
                api.busy(false);
                if (written) {
                    hostContext.feedback.notify(`${written} is declared. Reference it in a dataset query to filter on it.`, { title: draft.original ? 'Parameter saved' : 'Parameter added', tone: 'success' });
                }
                await load();
            };
            const paintDelete = (parameter) => api.render({
                lede: `Delete <strong>${escapeHtml(parameter.name)}</strong>? Anything still referencing it — a dataset `
                    + 'query, a slicer action — keeps that reference and will not resolve, so check those first.',
                body: sqlPreviewMarkup(`DECLARE ${parameter.name} ${parameter.dataType}${parameter.initialValue ? ` = ${parameter.initialValue}` : ''};`, `Deletes this line from the script. Nothing else is rewritten, so any query still naming `
                    + `${parameter.name} keeps that reference and stops resolving.`, 'Removes this declaration'),
                actions: [
                    { id: 'back', label: 'Keep it', run: paintList },
                    { id: 'delete', label: 'Delete', primary: true, run: () => remove(parameter) },
                ],
            });
            const remove = async (parameter) => {
                api.busy(true);
                const removed = await hostContext.mutate(`Delete parameter ${parameter.name}`, design => {
                    design.parameters = (design.parameters || []).filter((item) => item.name !== parameter.name);
                    return parameter.name;
                });
                api.busy(false);
                if (removed)
                    hostContext.feedback.notify(`${removed} was removed.`, { title: 'Parameter deleted', tone: 'success' });
                await load();
            };
            paintList();
            load();
        });
    }
    async function runDetailsStep() {
        if (!await hostContext.requireDataSample('Step 3 · Groups + details'))
            return;
        const columns = hostContext.guidedColumnNames();
        const numeric = hostContext.guidedNumericColumns();
        const draft = {
            group: columns[0] || '',
            measure: numeric[0] || columns[0] || '',
            includeMatrix: true,
            detail: columns.slice(0, 8),
        };
        await hostContext.studioDialog({ kicker: 'Step 3 · Groups + details', title: 'Add group and detail bands', wide: true }, api => {
            const paint = () => api.render({
                lede: 'A paginated report repeats a <strong>detail</strong> row per record, optionally under a <strong>group</strong> summary. '
                    + 'The matrix pivots one field against a measure; the table lists the rows themselves.',
                body: `
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-details-matrix ${draft.includeMatrix ? 'checked' : ''}>
                        Add a group summary (MATRIX) above the detail rows</label>
                    ${draft.includeMatrix ? `
                    <div class="etlsql-studio-guided-row">
                        <label class="etlsql-studio-guided-field"><span>Group by</span>
                            <select data-details-group>${columns.map(column => `<option ${draft.group === column ? 'selected' : ''}>${escapeHtml(column)}</option>`).join('')}</select></label>
                        <label class="etlsql-studio-guided-field"><span>Summarise</span>
                            <select data-details-measure>${columns.map(column => `<option ${draft.measure === column ? 'selected' : ''}>${escapeHtml(column)}</option>`).join('')}</select></label>
                    </div>` : ''}
                    <div class="etlsql-studio-guided-field"><span>Detail columns</span>
                        <div class="etlsql-studio-check-grid">${columns.map(column => `
                            <label><input type="checkbox" data-detail-column="${escapeHtml(column)}"
                                ${draft.detail.includes(column) ? 'checked' : ''}>${escapeHtml(column)}</label>`).join('')}</div></div>`
                    + mutationExplanationMarkup(`Appends ${draft.includeMatrix ? 'a matrix summarising ' + draft.measure + ' by ' + draft.group + ' and ' : ''}`
                        + `a detail table printing ${draft.detail.length} column${draft.detail.length === 1 ? '' : 's'} `
                        + 'below whatever the page already holds. Existing visuals are not moved or rewritten.')
                    + (draft.detail.length ? '' : guidedNoteMarkup('Pick at least one detail column, or the table has nothing to print.', 'warning')),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add bands', primary: true, disabled: !draft.detail.length, run: async () => {
                            api.busy(true);
                            const binding = hostContext.visualSourceBinding();
                            const added = await hostContext.mutate('Add group and detail bands', design => {
                                const page = design.pages[0];
                                page.mode = 'Paginated';
                                page.visuals ||= [];
                                const bottom = () => page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                                if (draft.includeMatrix) {
                                    page.visuals.push({
                                        id: `studio_group_${Date.now().toString(36)}`,
                                        name: hostContext.uniqueVisualName(design, 'group_summary'),
                                        type: 'MATRIX', gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 4,
                                        title: `${draft.group} summary`,
                                        dataset: binding.dataset,
                                        mappings: { ROW: draft.group, VALUE: draft.measure },
                                        options: { ...binding.options, AGGREGATE: 'SUM' },
                                    });
                                }
                                page.visuals.push({
                                    id: `studio_detail_${Date.now().toString(36)}`,
                                    name: hostContext.uniqueVisualName(design, 'detail_rows'),
                                    type: 'TABLE', gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 7,
                                    title: 'Detail rows',
                                    dataset: binding.dataset,
                                    // A TABLE prints one column per mapping entry, keyed by position.
                                    mappings: Object.fromEntries(draft.detail.map((column, index) => [`COLUMN${index + 1}`, column])),
                                    // PAGE_SIZE = 0 prints every row instead of paging in the browser,
                                    // which is what a physical page needs.
                                    options: { ...binding.options, PAGE_SIZE: '0' },
                                });
                                return true;
                            });
                            api.busy(false);
                            if (added)
                                hostContext.feedback.notify('Detail rows added. Step 4 puts a total under them.', { title: 'Bands added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-details-matrix]')?.addEventListener('change', event => { draft.includeMatrix = asInput(event.target).checked; paint(); });
                    host.querySelector('[data-details-group]')?.addEventListener('change', event => { draft.group = asSelect(event.target).value; });
                    host.querySelector('[data-details-measure]')?.addEventListener('change', event => { draft.measure = asSelect(event.target).value; });
                    host.querySelectorAll('[data-detail-column]').forEach(box => box.addEventListener('change', () => {
                        const column = asHtml(box).dataset.detailColumn;
                        draft.detail = asInput(box).checked
                            ? [...draft.detail, column]
                            : draft.detail.filter(item => item !== column);
                        const add = asButton(hostContext.dialog.box.querySelector('[data-dialog-action="add"]'));
                        if (add)
                            add.disabled = !draft.detail.length;
                    }));
                },
            });
            paint();
        });
    }
    async function runTotalsStep() {
        const designState = hostContext.shell.designerState();
        const tables = (designState?.pages || []).flatMap((page) => page.visuals || []).filter((visual) => visual.type === 'TABLE');
        if (!tables.length) {
            await hostContext.guidedBlocker({
                kicker: 'Step 4 · Add totals',
                title: 'There is no detail table yet',
                lede: 'A grand total is a footer row under a detail table, so the table has to exist first. '
                    + 'Step 3 creates one from the fields you choose.',
                remedyLabel: 'Add detail bands',
                remedy: () => runDetailsStep(),
            });
            return;
        }
        const draft = { target: tables[0].name, aggregate: 'SUM' };
        await hostContext.studioDialog({ kicker: 'Step 4 · Add totals', title: 'Add a grand total' }, api => {
            const paint = () => api.render({
                lede: 'A <strong>grand total</strong> appends one footer row to a detail table, aggregating every numeric column it prints.',
                body: `
                    <label class="etlsql-studio-guided-field"><span>Detail table</span>
                        <select data-total-target>${tables.map((table) => `<option value="${escapeHtml(table.name)}" ${draft.target === table.name ? 'selected' : ''}>${escapeHtml(table.title || table.name)}</option>`).join('')}</select></label>
                    <label class="etlsql-studio-guided-field"><span>Aggregate</span>
                        <select data-total-aggregate>${STUDIO_TOTAL_AGGREGATES.map(aggregate => `<option ${draft.aggregate === aggregate ? 'selected' : ''}>${aggregate}</option>`).join('')}</select></label>`
                    + sqlPreviewMarkup(`OPTIONS (GRAND_TOTAL = ${draft.aggregate})`, `Adds one footer row to ${draft.target} showing the ${draft.aggregate} of each numeric column it prints. `
                        + 'The detail rows above it are unchanged.', 'Adds this option to the table'),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add total', primary: true, run: async () => {
                            api.busy(true);
                            const added = await hostContext.mutate('Add report totals', design => {
                                const table = (design.pages || []).flatMap((page) => page.visuals || [])
                                    .find((visual) => visual.name === draft.target);
                                if (!table)
                                    throw new Error(`The detail table ${draft.target} is no longer in the script.`);
                                table.options ||= {};
                                table.options.GRAND_TOTAL = draft.aggregate;
                                return true;
                            });
                            api.busy(false);
                            if (added)
                                hostContext.feedback.notify(`${draft.target} now prints a ${draft.aggregate} total row.`, { title: 'Total added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-total-target]')?.addEventListener('change', event => { draft.target = asSelect(event.target).value; paint(); });
                    host.querySelector('[data-total-aggregate]')?.addEventListener('change', event => { draft.aggregate = asSelect(event.target).value; paint(); });
                },
            });
            paint();
        });
    }
    async function runFurnitureStep() {
        const doc = hostContext.getActiveDocument();
        const draft = {
            headerKind: 'text',
            header: doc?.name?.replace(/\.rptsql$/i, '').replace(/[_-]+/g, ' ') || 'Report',
            headerImage: '/images/logo.png',
            footer: 'Page {{PAGE}} of {{PAGES}}',
            addHeader: true,
            addFooter: true,
            breakAfterDetails: false,
        };
        await hostContext.studioDialog({ kicker: 'Step 5 · Header + footer', title: 'Add page furniture' }, api => {
            const paint = () => api.render({
                lede: 'Page <strong>furniture</strong> is the content that frames every printed page. '
                    + 'These are TEXT and IMAGE bands with a <code>KEEP_TOGETHER</code> print rule, so they never split across a page boundary.',
                body: `
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-furniture-header ${draft.addHeader ? 'checked' : ''}> Add a page header</label>
                    ${draft.addHeader ? `
                        <div style="display:flex;gap:12px;margin:4px 0 6px 0;">
                            <label style="font-size:12px;"><input type="radio" name="furniture_header_kind" value="text" ${draft.headerKind === 'text' ? 'checked' : ''} data-header-kind> Text & dynamic fields</label>
                            <label style="font-size:12px;"><input type="radio" name="furniture_header_kind" value="image" ${draft.headerKind === 'image' ? 'checked' : ''} data-header-kind> Image / Logo</label>
                        </div>
                        ${draft.headerKind === 'text' ? `
                            <label class="etlsql-studio-guided-field"><span>Header text</span>
                                <input type="text" data-header-text value="${escapeHtml(draft.header)}"></label>
                            <div style="display:flex;gap:6px;margin:2px 0 8px 0;flex-wrap:wrap;align-items:center;">
                                <span style="font-size:11px;color:var(--portal-muted,#7a8798);">Tokens:</span>
                                <button type="button" class="btn btn-xs" data-insert-token="header" data-token="{{PAGE}}">+ Page #</button>
                                <button type="button" class="btn btn-xs" data-insert-token="header" data-token="{{PAGES}}">+ Total Pages</button>
                                <button type="button" class="btn btn-xs" data-insert-token="header" data-token="{{CURRENT_DATE}}">+ Date</button>
                            </div>` : `
                            <label class="etlsql-studio-guided-field"><span>Image URL or file path</span>
                                <input type="text" data-header-image value="${escapeHtml(draft.headerImage)}" placeholder="/images/logo.png or https://..."></label>`}` : ''}
                    <label class="etlsql-studio-guided-check" style="margin-top:8px;">
                        <input type="checkbox" data-furniture-footer ${draft.addFooter ? 'checked' : ''}> Add a page footer</label>
                    ${draft.addFooter ? `
                        <label class="etlsql-studio-guided-field"><span>Footer text</span>
                            <input type="text" data-footer-text value="${escapeHtml(draft.footer)}"></label>
                        <div style="display:flex;gap:6px;margin:2px 0 8px 0;flex-wrap:wrap;align-items:center;">
                            <span style="font-size:11px;color:var(--portal-muted,#7a8798);">Tokens:</span>
                            <button type="button" class="btn btn-xs" data-insert-token="footer" data-token="{{PAGE}}">+ Page #</button>
                            <button type="button" class="btn btn-xs" data-insert-token="footer" data-token="{{PAGES}}">+ Total Pages</button>
                            <button type="button" class="btn btn-xs" data-insert-token="footer" data-token="{{CURRENT_DATE}}">+ Date</button>
                            <button type="button" class="btn btn-xs" data-insert-token="footer" data-token="Page {{PAGE}} of {{PAGES}}">Page X of Y</button>
                        </div>` : ''}
                    <label class="etlsql-studio-guided-check" style="margin-top:8px;">
                        <input type="checkbox" data-furniture-break ${draft.breakAfterDetails ? 'checked' : ''}>
                        Start a new page after the detail table</label>`
                    + mutationExplanationMarkup(`Adds ${[draft.addHeader ? (draft.headerKind === 'image' ? 'a logo image' : 'a header band') : null, draft.addFooter ? 'a footer band' : null]
                        .filter(Boolean).join(' and ') || 'nothing yet'} to the page`
                        + `${draft.breakAfterDetails ? ', and sets the detail table to start a new page after it' : ''}. `
                        + 'The bands print on every physical page; the data visuals are untouched.')
                    + (draft.addHeader || draft.addFooter ? '' : guidedNoteMarkup('Nothing selected — pick a header, a footer, or both.', 'warning')),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add furniture', primary: true, disabled: !draft.addHeader && !draft.addFooter, run: async () => {
                            api.busy(true);
                            const added = await hostContext.mutate('Add page header and footer', design => {
                                const page = design.pages[0];
                                page.mode = 'Paginated';
                                page.visuals ||= [];
                                const bottom = () => page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                                const band = (slug, title, text) => ({
                                    id: `studio_${slug}_${Date.now().toString(36)}`,
                                    name: hostContext.uniqueVisualName(design, `page_${slug}`),
                                    type: 'TEXT', gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 2,
                                    title,
                                    dataset: null,
                                    mappings: {},
                                    options: {
                                        text_default: `'${String(text).replace(/'/g, "''")}'`,
                                        print_layout: 'PRINT_LAYOUT (KEEP_TOGETHER = ON)',
                                    },
                                });
                                if (draft.addHeader) {
                                    if (draft.headerKind === 'image') {
                                        page.visuals.push({
                                            id: `studio_header_logo_${Date.now().toString(36)}`,
                                            name: hostContext.uniqueVisualName(design, 'page_header_logo'),
                                            type: 'IMAGE', gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 2,
                                            title: 'Report logo',
                                            dataset: null,
                                            mappings: {},
                                            options: {
                                                src: `'${String(draft.headerImage).replace(/'/g, "''")}'`,
                                                print_layout: 'PRINT_LAYOUT (KEEP_TOGETHER = ON)',
                                            },
                                        });
                                    }
                                    else {
                                        page.visuals.push(band('header', 'Page header', draft.header));
                                    }
                                }
                                if (draft.addFooter)
                                    page.visuals.push(band('footer', 'Page footer', draft.footer));
                                if (draft.breakAfterDetails) {
                                    const table = page.visuals.find((visual) => visual.type === 'TABLE');
                                    if (table) {
                                        table.options ||= {};
                                        table.options.print_layout = 'PRINT_LAYOUT (PAGE_BREAK_AFTER = ON, KEEP_TOGETHER = ON)';
                                    }
                                }
                                return true;
                            });
                            api.busy(false);
                            if (added)
                                hostContext.feedback.notify('Page bands added.', { title: 'Furniture added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-furniture-header]')?.addEventListener('change', event => { draft.addHeader = asInput(event.target).checked; paint(); });
                    host.querySelector('[data-furniture-footer]')?.addEventListener('change', event => { draft.addFooter = asInput(event.target).checked; paint(); });
                    host.querySelector('[data-furniture-break]')?.addEventListener('change', event => { draft.breakAfterDetails = asInput(event.target).checked; });
                    host.querySelectorAll('[data-header-kind]').forEach(r => r.addEventListener('change', event => { draft.headerKind = asInput(event.target).value; paint(); }));
                    host.querySelector('[data-header-text]')?.addEventListener('input', event => { draft.header = asInput(event.target).value; });
                    host.querySelector('[data-header-image]')?.addEventListener('input', event => { draft.headerImage = asInput(event.target).value; });
                    host.querySelector('[data-footer-text]')?.addEventListener('input', event => { draft.footer = asInput(event.target).value; });
                    host.querySelectorAll('[data-insert-token]').forEach(btn => btn.addEventListener('click', () => {
                        const target = asHtml(btn).dataset.insertToken;
                        const token = asHtml(btn).dataset.token || '';
                        if (target === 'header') {
                            const input = host.querySelector('[data-header-text]');
                            if (input) {
                                input.value = input.value ? `${input.value} ${token}` : token;
                                draft.header = input.value;
                            }
                        }
                        else if (target === 'footer') {
                            const input = host.querySelector('[data-footer-text]');
                            if (input) {
                                input.value = input.value ? `${input.value} ${token}` : token;
                                draft.footer = input.value;
                            }
                        }
                    }));
                },
            });
            paint();
        });
    }
    return { runParameterStep, runDetailsStep, runTotalsStep, runFurnitureStep };
}
