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
    /**
     * Page furniture: a header and a footer that print on every physical page.
     *
     * Each is a TEXT or IMAGE visual marked `PRINT_LAYOUT (BAND = HEADER | FOOTER)`, so the script
     * says exactly what the page will do. It used to write an ordinary visual with a KEEP_TOGETHER
     * rule: it printed once, where it sat in the layout, and its {{PAGE}} tokens printed as written.
     */
    async function runFurnitureStep() {
        const doc = hostContext.getActiveDocument();
        const draft = {
            header: {
                add: true, kind: 'text', dataset: '',
                text: `${doc?.name?.replace(/\.rptsql$/i, '').replace(/[_-]+/g, ' ') || '{{TITLE}}'} · {{CURRENT_DATE}}`,
                image: 'data:image/png;base64,',
            },
            footer: { add: true, kind: 'text', dataset: '', text: 'Page {{PAGE}} of {{PAGES}}', image: 'data:image/png;base64,' },
            breakAfterDetails: false,
        };
        // What a band can say, read from the script: its parameters, and its datasets for a data field.
        let parameters = [];
        let datasets = [];
        try {
            const parsed = await hostContext.request(hostContext.routes.parse, { body: { script: hostContext.shell.getScriptText() } });
            parameters = (parsed.designState?.parameters || []).map((parameter) => String(parameter.name).replace(/^@/, ''));
            datasets = (parsed.designState?.datasets || []).map((dataset) => String(dataset.name)).filter(Boolean);
        }
        catch {
            // The date, title, and page tokens still work without them.
        }
        const tokenButtons = (which) => [
            ['{{PAGE}}', '+ Page #'],
            ['{{PAGES}}', '+ Total pages'],
            ['Page {{PAGE}} of {{PAGES}}', 'Page X of Y'],
            ['{{CURRENT_DATE}}', '+ Date'],
            ['{{TITLE}}', '+ Report title'],
            ...parameters.map(name => [`{{@${name}}}`, `+ @${name}`]),
        ].map(([token, label]) => `<button type="button" class="btn btn-xs" data-insert-token="${which}"
            data-token="${escapeHtml(token)}">${escapeHtml(label)}</button>`).join('');
        const bandMarkup = (which) => {
            const band = draft[which];
            const title = which === 'header' ? 'header' : 'footer';
            if (!band.add)
                return '';
            return `
                <div style="display:flex;gap:12px;margin:4px 0 6px 0;">
                    <label style="font-size:12px;"><input type="radio" name="furniture_${which}_kind" value="text"
                        ${band.kind === 'text' ? 'checked' : ''} data-band-kind="${which}"> Text & fields</label>
                    <label style="font-size:12px;"><input type="radio" name="furniture_${which}_kind" value="image"
                        ${band.kind === 'image' ? 'checked' : ''} data-band-kind="${which}"> Image / logo</label>
                </div>
                ${band.kind === 'text' ? `
                    <label class="etlsql-studio-guided-field"><span>${escapeHtml(title[0].toUpperCase() + title.slice(1))} text</span>
                        <input type="text" data-band-text="${which}" value="${escapeHtml(band.text)}"></label>
                    <div style="display:flex;gap:6px;margin:2px 0 6px 0;flex-wrap:wrap;align-items:center;">
                        <span style="font-size:11px;color:var(--portal-muted,#7a8798);">Insert:</span>${tokenButtons(which)}
                    </div>
                    ${datasets.length ? `<div style="display:flex;gap:6px;margin:0 0 8px 0;flex-wrap:wrap;align-items:center;">
                        <span style="font-size:11px;color:var(--portal-muted,#7a8798);">A value from data:</span>
                        <select data-band-dataset="${which}"><option value="">Dataset…</option>${datasets.map(name => `<option${name === band.dataset ? ' selected' : ''}>${escapeHtml(name)}</option>`).join('')}</select>
                        <input type="text" data-band-column="${which}" placeholder="Column" style="width:120px" spellcheck="false">
                        <button type="button" class="btn btn-xs" data-insert-column="${which}">Insert</button>
                    </div>` : ''}` : `
                    <label class="etlsql-studio-guided-field"><span>Image as a data: URI</span>
                        <input type="text" data-band-image="${which}" value="${escapeHtml(band.image)}" placeholder="data:image/png;base64,…"></label>`}`;
        };
        await hostContext.studioDialog({ kicker: 'Step 5 · Header + footer', title: 'Add page furniture' }, api => {
            const paint = () => api.render({
                lede: 'Page <strong>furniture</strong> prints at the top and bottom of <em>every</em> physical page. '
                    + 'Each is a TEXT or IMAGE visual marked <code>PRINT_LAYOUT (BAND = HEADER)</code> or '
                    + '<code>BAND = FOOTER</code>; the page numbers, the date, and parameters are filled in as each page prints.',
                body: `
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-furniture-add="header" ${draft.header.add ? 'checked' : ''}> Add a page header</label>
                    ${bandMarkup('header')}
                    <label class="etlsql-studio-guided-check" style="margin-top:8px;">
                        <input type="checkbox" data-furniture-add="footer" ${draft.footer.add ? 'checked' : ''}> Add a page footer</label>
                    ${bandMarkup('footer')}
                    <label class="etlsql-studio-guided-check" style="margin-top:8px;">
                        <input type="checkbox" data-furniture-break ${draft.breakAfterDetails ? 'checked' : ''}>
                        Start a new page after the detail table</label>`
                    + (draft.footer.add ? guidedNoteMarkup('A footer of your own replaces the default "Generated … Page X of Y" line.', 'info') : '')
                    + ([draft.header, draft.footer].some(band => band.add && band.kind === 'image')
                        ? guidedNoteMarkup('An image prints only as a data: URI; one that cannot be embedded is left out of the band.', 'info') : '')
                    + mutationExplanationMarkup(`Adds ${[draft.header.add ? 'a header band' : null, draft.footer.add ? 'a footer band' : null]
                        .filter(Boolean).join(' and ') || 'nothing yet'}`
                        + `${draft.breakAfterDetails ? ', and sets the detail table to start a new page after it' : ''}. `
                        + 'The data visuals are untouched.')
                    + (draft.header.add || draft.footer.add ? '' : guidedNoteMarkup('Nothing selected — pick a header, a footer, or both.', 'warning')),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add furniture', primary: true, disabled: !draft.header.add && !draft.footer.add, run: async () => {
                            api.busy(true);
                            const added = await hostContext.mutate('Add page header and footer', design => {
                                const page = design.pages[0];
                                page.mode = 'Paginated';
                                page.visuals ||= [];
                                const bottom = () => page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                                const quote = (text) => `'${String(text).replace(/'/g, "''")}'`;
                                const addBand = (which) => {
                                    const band = draft[which];
                                    if (!band.add)
                                        return;
                                    const printLayout = `PRINT_LAYOUT (BAND = ${which.toUpperCase()})`;
                                    const common = {
                                        id: `studio_page_${which}_${Date.now().toString(36)}`,
                                        gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 1,
                                        title: which === 'header' ? 'Page header' : 'Page footer',
                                        mappings: {},
                                    };
                                    page.visuals.push(band.kind === 'image'
                                        ? {
                                            ...common,
                                            name: hostContext.uniqueVisualName(design, `page_${which}_logo`),
                                            type: 'IMAGE', dataset: null,
                                            options: { src: quote(band.image), print_layout: printLayout },
                                        }
                                        : {
                                            ...common,
                                            name: hostContext.uniqueVisualName(design, `page_${which}`),
                                            type: 'TEXT',
                                            // A data field reads the band's own first row, so the band reads that dataset.
                                            dataset: band.dataset || null,
                                            options: { text_default: quote(band.text), print_layout: printLayout },
                                        });
                                };
                                addBand('header');
                                addBand('footer');
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
                    const readText = () => {
                        for (const which of ['header', 'footer']) {
                            const text = host.querySelector(`[data-band-text="${which}"]`);
                            if (text)
                                draft[which].text = text.value;
                            const image = host.querySelector(`[data-band-image="${which}"]`);
                            if (image)
                                draft[which].image = image.value;
                        }
                    };
                    const append = (which, token) => {
                        const input = host.querySelector(`[data-band-text="${which}"]`);
                        if (!input)
                            return;
                        input.value = input.value ? `${input.value} ${token}` : token;
                        draft[which].text = input.value;
                    };
                    host.querySelectorAll('[data-furniture-add]').forEach(box => box.addEventListener('change', event => {
                        readText();
                        draft[asHtml(box).dataset.furnitureAdd].add = asInput(event.target).checked;
                        paint();
                    }));
                    host.querySelector('[data-furniture-break]')?.addEventListener('change', event => { draft.breakAfterDetails = asInput(event.target).checked; });
                    host.querySelectorAll('[data-band-kind]').forEach(radio => radio.addEventListener('change', event => {
                        readText();
                        draft[asHtml(radio).dataset.bandKind].kind = asInput(event.target).value;
                        paint();
                    }));
                    host.querySelectorAll('[data-band-text], [data-band-image]').forEach(input => input.addEventListener('input', readText));
                    host.querySelectorAll('[data-band-dataset]').forEach(select => select.addEventListener('change', event => {
                        draft[asHtml(select).dataset.bandDataset].dataset = asSelect(event.target).value;
                    }));
                    host.querySelectorAll('[data-insert-token]').forEach(btn => btn.addEventListener('click', () => {
                        append(asHtml(btn).dataset.insertToken, asHtml(btn).dataset.token || '');
                    }));
                    host.querySelectorAll('[data-insert-column]').forEach(btn => btn.addEventListener('click', () => {
                        const which = asHtml(btn).dataset.insertColumn;
                        const column = host.querySelector(`[data-band-column="${which}"]`)?.value.trim() || '';
                        if (!/^[A-Za-z0-9_]+$/.test(column) || !draft[which].dataset)
                            return;
                        append(which, `{${column}}`);
                    }));
                },
            });
            paint();
        });
    }
    return { runParameterStep, runDetailsStep, runTotalsStep, runFurnitureStep };
}
