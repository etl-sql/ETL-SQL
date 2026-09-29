// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring-pipeline.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-authoring-pipeline.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Pipeline task editing and run-plan confirmation.
 */
import { asInput, asSelect } from './studio-authoring-context.js';
import { escapeHtml, noteMarkup as guidedNoteMarkup, mutationExplanationMarkup } from './studio-authoring-ui.js';
import { taskKindLabel } from './studio-pipeline-canvas.js';
import { createQueryWorkbench } from './studio-query-workbench.js';
export function createStudioAuthoringPipeline(hostContext) {
    const PIPELINE_TASK_FIELDS = {
        // ── Files ────────────────────────────────────────────────────────────
        // Two paths, or one, named after the verb rather than generically: "Copy from" and
        // "Move from" are the same field and a different sentence, and the sentence is what tells
        // the author which statement they are writing.
        copyfile: [
            { name: 'source', label: 'Copy from', placeholder: 'C:\\data\\orders.csv' },
            { name: 'target', label: 'Copy to', placeholder: 'C:\\data\\archive\\orders.csv' },
        ],
        movefile: [
            { name: 'source', label: 'Move from', placeholder: 'C:\\data\\incoming\\orders.csv' },
            { name: 'target', label: 'Move to', placeholder: 'C:\\data\\working\\orders.csv' },
        ],
        // A rename takes a name, not a path: the engine puts the file back where it already was.
        // Saying so on the field is the difference between one attempt and three.
        renamefile: [
            { name: 'source', label: 'Rename', placeholder: 'C:\\data\\working\\orders.csv' },
            { name: 'target', label: 'To this name', placeholder: 'orders_20260904.csv', hint: 'A name, not a path. It stays in the folder it is in.' },
        ],
        deletefile: [
            { name: 'source', label: 'Delete', placeholder: 'C:\\data\\working\\orders.csv' },
        ],
        // ── Directories ──────────────────────────────────────────────────────
        createdirectory: [
            { name: 'source', label: 'Create', placeholder: 'C:\\data\\archive\\2026-09' },
        ],
        copydirectory: [
            { name: 'source', label: 'Copy from', placeholder: 'C:\\data\\archive\\2026-09' },
            { name: 'target', label: 'Copy to', placeholder: 'D:\\mirror\\2026-09' },
        ],
        movedirectory: [
            { name: 'source', label: 'Move from', placeholder: 'C:\\data\\archive\\2026-09' },
            { name: 'target', label: 'Move to', placeholder: 'D:\\cold\\2026-09' },
        ],
        renamedirectory: [
            { name: 'source', label: 'Rename', placeholder: 'C:\\data\\archive\\current' },
            { name: 'target', label: 'To this name', placeholder: '2026-09', hint: 'A name, not a path. It stays where it is.' },
        ],
        deletedirectorycontents: [
            { name: 'source', label: 'Empty', placeholder: 'C:\\data\\working', hint: 'The folder stays; what is inside it goes.' },
        ],
        deletedirectory: [
            { name: 'source', label: 'Delete', placeholder: 'C:\\data\\working', hint: 'The folder and everything in it.' },
        ],
        // ── Checks and messages ──────────────────────────────────────────────
        validation: [
            { name: 'condition', label: 'Assert that', placeholder: '(SELECT COUNT(*) FROM #orders) > 0', mono: true },
            { name: 'message', label: 'Fail with', placeholder: 'No orders were staged.' },
        ],
        notification: [
            { name: 'recipient', label: 'To', placeholder: 'ops@example.com' },
            { name: 'sender', label: 'From', placeholder: 'etl@example.com' },
            { name: 'subject', label: 'Subject', placeholder: 'Nightly load finished' },
            { name: 'body', label: 'Body', placeholder: 'All records processed.' },
        ],
        throw: [
            { name: 'message', label: 'Fail with', placeholder: 'The nightly load found nothing to do.' },
        ],
        waitfor: [
            { name: 'delay', label: 'Wait for', placeholder: '00:00:30', mono: true, hint: 'hh:mm:ss.' },
        ],
        // ── Containers ───────────────────────────────────────────────────────
        // A parallel block and a transaction scope have nothing to fill in: they are named, and then
        // filled by dragging tasks into them.
        parallel: [],
        transaction: [],
        foreach: [
            { name: 'variable', label: 'Item variable', placeholder: '@row', mono: true },
            { name: 'collection', label: 'Iterates over', placeholder: '#orders', mono: true },
        ],
        for: [
            { name: 'variable', label: 'Counter', placeholder: '@day', mono: true },
            { name: 'start', label: 'From', placeholder: '1', mono: true },
            { name: 'end', label: 'To', placeholder: '7', mono: true },
            { name: 'step', label: 'Step', placeholder: '1', mono: true, optional: true, hint: 'Leave blank to count by one.' },
        ],
        while: [
            { name: 'condition', label: 'Repeat while', placeholder: '@rows_left > 0', mono: true },
        ],
        if: [
            { name: 'condition', label: 'Only when', placeholder: '(SELECT COUNT(*) FROM #orders) > 0', mono: true },
        ],
        // ── Moving data ──────────────────────────────────────────────────────
        extract: [
            { name: 'table', label: 'Read from table', placeholder: 'Users', mono: true, picker: 'table' },
            { name: 'columns', label: 'Columns', placeholder: 'All columns', mono: true, optional: true, picker: 'columns', hint: 'Leave all unticked to read every column.' },
            { name: 'condition', label: 'Only rows where', placeholder: 'Active = 1', mono: true, optional: true },
            { name: 'target', label: 'Stage into', placeholder: '#staged_users', mono: true, hint: 'A #temp table. It lasts for this run, and every later step can read it by name.' },
        ],
        load: [
            { name: 'source', label: 'Rows from', placeholder: '#staged_users', mono: true, hint: 'The #temp table an earlier step staged.' },
            { name: 'table', label: 'Insert into table', placeholder: 'UsersArchive', mono: true, picker: 'table' },
            { name: 'columns', label: 'Columns', placeholder: 'UserID, UserName', mono: true, picker: 'columns', hint: 'Named on both sides, so each value lands in the right column.' },
        ],
        upsert: [
            { name: 'source', label: 'Rows from', placeholder: '#staged_users', mono: true, hint: 'The #temp table an earlier step staged.' },
            { name: 'table', label: 'Upsert into table', placeholder: 'Users', mono: true, picker: 'table' },
            { name: 'keys', label: 'Match on', placeholder: 'UserID', mono: true, picker: 'columns', hint: 'The key columns that say a row already exists.' },
            { name: 'columns', label: 'Columns to update', placeholder: 'UserName, Email', mono: true, optional: true, picker: 'columns', hint: 'Updated on a match, and inserted with the keys on a new row.' },
        ],
        // BREAK and CONTINUE are the whole statement. The editor still opens, because the label is
        // how the canvas will address the card afterwards, and it is the only thing to fill in.
        break: [],
        continue: [],
    };
    const PIPELINE_KINDS_NEEDING_CONNECTION = new Set(['execution', 'notification', 'extract', 'load', 'upsert']);
    /**
     * Said once, where the idea first appears, rather than in a help page nobody opens. Each of these
     * is a thing a SQL user would not know about ETL-SQL until they had been bitten by it.
     */
    const PIPELINE_KIND_EXPLANATIONS = {
        extract: 'The connection is one this script declares. The rows are copied into a #temp table that '
            + 'lives only for this run — later steps read it by name, and nothing is written back to the source.',
        load: 'Rows are read from a #temp table an earlier step staged, and appended to the table on the connection.',
        upsert: 'This is a MERGE. The engine holds every matching row while it runs, so at large scale filter '
            + 'the #temp table first, or split the load into batches.',
    };
    /**
     * Where the statement is about to be written, in the words of the script rather than the canvas.
     *
     * The dialog used to promise "the end of the pipeline" whatever the author had done, which
     * stopped being true the moment a chip could be dropped onto a task or into a block. A sentence
     * about where an edit lands is only worth showing while it is accurate.
     */
    function placementPhrase(placement) {
        if (placement?.into)
            return `inside ${placement.into}`;
        if (placement?.after)
            return `after ${placement.after}`;
        return 'at the end of the pipeline';
    }
    /**
     * The task editor behind a pipeline canvas node.
     *
     * An execution task is authored in the shared query workbench, so writing the SQL a task runs
     * gets the same completions, hover, diagnostics, run, and results as the script pane. The other
     * kinds are field forms, because that is all their statement is.
     *
     * It returns the author's intent and never writes to the script. The caller applies it through
     * the canonical pipeline mutation, which owns the bytes.
     *
     * @param kind        Palette kind being authored.
     * @param task        The task being edited, or null for a new one.
     * @param connections `[{ name }]` the script declares, from the canonical parse.
     * @param suggestedId Label to start a new task with.
     * @param placement   `{ after, into }` where the drop landed, so the dialog can say where the
     *                    statement is about to be written rather than assuming the end of the file.
     */
    async function openPipelineTaskEditor({ kind = 'execution', task = null, connections = [], suggestedId = 'task_1', placement = null, } = {}) {
        const editing = Boolean(task);
        const taskKind = String(task?.kind || kind || 'execution').toLowerCase();
        const aliases = (connections || []).map((connection) => connection?.name).filter(Boolean);
        // What an edit may touch is what the host found as an exact span in the statement, with the
        // value it holds now. A field it did not report is written in a form it cannot rewrite
        // without losing something — a path built from a variable — so it is named, not offered.
        const offered = editing ? (task?.fields ?? {}) : {};
        const needsConnection = PIPELINE_KINDS_NEEDING_CONNECTION.has(taskKind)
            && (!editing || taskKind === 'execution' || 'connection' in offered);
        // A task that runs against a connection cannot be written before one is declared, and a
        // free-text alias would let the author name one the script does not declare — which previews
        // fine here and fails for every other reader. Resume in the editor rather than dropping them
        // back on the canvas: making them re-open it is exactly the dead end the dataset wizard fixed.
        if (needsConnection && !aliases.length) {
            let created = null;
            const took = await hostContext.guidedBlocker({
                kicker: 'Pipeline task',
                title: 'This script declares no connections yet',
                lede: 'This task runs against a connection the script declares. Add one and this editor '
                    + 'picks up where it left off.',
                remedyLabel: 'Create a connection',
                remedy: () => new Promise(resolve => hostContext.shell.openConnectionWizard({
                    onDone: alias => { created = alias || null; resolve(); },
                })),
            });
            if (!took || !created)
                return null;
            return openPipelineTaskEditor({ kind, task, connections: [{ name: created }], suggestedId });
        }
        // The host rewrites an existing task one span at a time. A field sent that it could not find
        // would be refused, so the dialog asks only about what it reported — and names the rest,
        // rather than showing a box whose contents would never reach the script.
        const kindFields = PIPELINE_TASK_FIELDS[taskKind] ?? [];
        const fields = editing ? kindFields.filter(field => field.name in offered) : kindFields;
        const withheld = editing ? kindFields.filter(field => !(field.name in offered)) : [];
        const currentConnection = taskKind === 'execution' ? task?.connection : offered.connection;
        const draft = {
            id: task?.id || suggestedId,
            connection: aliases.includes(currentConnection) ? currentConnection : aliases[0] || '',
            body: task?.body || '',
            error: null,
        };
        // Prefilled with what the statement holds now, so Apply without a change writes nothing.
        for (const field of fields)
            draft[field.name] = String(offered[field.name] ?? '');
        let workbench = null;
        // The connection's tables, read once per connection chosen, for the table and column pickers.
        // A failed read is kept apart from an empty one: the fields still take typing either way, and
        // the dialog says which it was rather than implying the connection has no tables.
        const usesPickers = fields.some(field => field.picker);
        let schema = null;
        const tableNamed = (name) => (schema?.tables ?? [])
            .find((table) => String(table.name).toLowerCase() === String(name || '').trim().toLowerCase());
        const columnsOf = (name) => (tableNamed(name)?.columns ?? [])
            .map((column) => String(column?.name ?? column));
        const listOf = (text) => String(text || '').split(',').map(part => part.trim()).filter(Boolean);
        // What the draft would write, shown while it is filled in. It comes from the host's own
        // renderer, so the statement on screen is the statement Add writes — never a browser copy.
        const previews = !editing && taskKind !== 'execution';
        let previewTimer = null;
        let previewTurn = 0;
        return hostContext.studioDialog({
            kicker: 'Pipeline task',
            title: editing ? `Edit ${task.id}` : `New ${taskKindLabel(taskKind).toLowerCase()} task`,
            wide: taskKind === 'execution',
        }, api => {
            const readFields = (host) => {
                for (const field of fields) {
                    draft[field.name] = asInput(host.querySelector(`[data-task-field="${field.name}"]`))?.value ?? draft[field.name];
                }
            };
            const save = () => {
                const host = hostContext.dialog.box;
                readFields(host);
                draft.body = taskKind === 'execution' ? (workbench?.getValue?.() ?? draft.body) : draft.body;
                const id = asInput(host.querySelector('[data-task-id]'))?.value.trim() ?? '';
                if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(id)) {
                    draft.id = id;
                    draft.error = `"${id}" is not a usable label. Use letters, digits, and underscores, starting with a letter.`;
                    return repaint();
                }
                if (taskKind === 'execution' && !draft.body.trim()) {
                    draft.error = 'Write the SQL this task runs before adding it.';
                    return repaint();
                }
                const blank = fields.find(field => !field.optional && !String(draft[field.name] || '').trim());
                if (blank) {
                    draft.error = `${blank.label} is needed before this task can be written.`;
                    return repaint();
                }
                const intent = { id, kind: taskKind };
                if (needsConnection && (!editing || draft.connection !== currentConnection))
                    intent.connection = draft.connection;
                if (taskKind === 'execution')
                    intent.body = draft.body;
                // An edit sends only what changed. Rewriting an untouched field would replace the
                // author's spelling of it — N'…', a spacing — with this dialog's.
                for (const field of fields) {
                    if (!editing || draft[field.name] !== String(offered[field.name] ?? ''))
                        intent[field.name] = draft[field.name];
                }
                return api.close(intent);
            };
            const paint = () => api.render({
                lede: `This task becomes a labelled statement in the script. The label is what the canvas `
                    + `tracks it by, so it survives a hand edit.`,
                body: (draft.error ? guidedNoteMarkup(draft.error, 'error') : '')
                    + (withheld.length ? guidedNoteMarkup([
                        withheld.map(field => field.label).join(', '),
                        withheld.length === 1 ? ' is' : ' are',
                        ' written in the script in a form this dialog cannot rewrite without losing '
                            + 'something — an expression, or a variable. Edit ',
                        withheld.length === 1 ? 'it' : 'them',
                        ' there: Show in script opens the right line.',
                    ], 'info') : '')
                    + `<div class="etlsql-studio-pipeline-fields">
                            <label>Label
                                <input type="text" data-task-id value="${escapeHtml(draft.id)}" spellcheck="false">
                            </label>
                            ${needsConnection ? `<label>Connection
                                <select data-task-connection>${aliases.map(alias => `<option${alias === draft.connection ? ' selected' : ''}>${escapeHtml(alias)}</option>`).join('')}</select>
                            </label>` : ''}
                            ${fields.map(fieldMarkup).join('')}
                        </div>`
                    + (usesPickers ? `<div data-schema-note>${schema?.failed ? guidedNoteMarkup([
                        `The tables of ${draft.connection} could not be read (${schema.failed}). Type the names instead.`,
                    ], 'warning') : ''}</div>` : '')
                    + (!editing && PIPELINE_KIND_EXPLANATIONS[taskKind]
                        ? guidedNoteMarkup(PIPELINE_KIND_EXPLANATIONS[taskKind], 'info') : '')
                    + (previews ? `<div class="etlsql-studio-task-preview">
                            <span>Writes this into the script</span>
                            <pre data-task-preview aria-live="polite"></pre>
                        </div>` : '')
                    + (taskKind === 'execution'
                        ? `<div class="etlsql-studio-workbench" data-task-workbench></div>`
                            + guidedNoteMarkup([
                                'Run previews this block in ',
                                { strong: 'remote context' },
                                ' (',
                                { code: `EXECUTE ${draft.connection} BEGIN … END` },
                                ') using only this connection\'s declaration. Preceding script variables and #temp staging tables are not executed.',
                            ], 'info')
                        : '')
                    + mutationExplanationMarkup(editing
                        ? `Rewrites ${task.id} in place. Only that statement changes: hand edits elsewhere, and `
                            + 'the tasks that wait for this one, are left as they are.'
                        : `Adds one ${taskKindLabel(taskKind).toLowerCase()} task ${placementPhrase(placement)}, under the `
                            + 'label above. Nothing already in the script is moved or rewritten, and nothing runs until '
                            + 'you run it.'),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    { id: 'save', label: editing ? 'Apply' : 'Add task', primary: true, run: save },
                ],
                wire: async (host) => {
                    host.querySelector('[data-task-id]')?.addEventListener('input', event => {
                        draft.id = asInput(event.target).value;
                        schedulePreview(host);
                    });
                    for (const input of host.querySelectorAll('[data-task-field]')) {
                        input.addEventListener('input', () => { readFields(host); schedulePreview(host); });
                    }
                    // A different table has different columns, so its column ticks are refilled.
                    for (const input of host.querySelectorAll('[data-task-field][list]')) {
                        input.addEventListener('change', () => { readFields(host); refreshPickers(host); });
                    }
                    // On the container, because its ticks are replaced whenever the table changes.
                    for (const picks of host.querySelectorAll('[data-column-picks]')) {
                        picks.addEventListener('change', () => {
                            const name = asInput(picks).dataset.columnPicks || '';
                            const picked = [...picks.querySelectorAll('[data-column-pick]')]
                                .filter(item => item.checked)
                                .map(item => asInput(item).value);
                            const field = asInput(host.querySelector(`[data-task-field="${name}"]`));
                            if (field)
                                field.value = picked.join(', ');
                            readFields(host);
                            schedulePreview(host);
                        });
                    }
                    if (previews)
                        schedulePreview(host, 0);
                    if (usesPickers && needsConnection && draft.connection && schema?.connection !== draft.connection) {
                        void loadSchema(host, draft.connection);
                    }
                    host.querySelector('[data-task-connection]')?.addEventListener('change', event => {
                        // The workbench binds its run and its preamble to one alias, so repointing
                        // rebuilds it rather than leaving it running against the previous one.
                        readFields(host);
                        draft.body = workbench?.getValue?.() ?? draft.body;
                        draft.connection = asSelect(event.target).value;
                        repaint();
                    });
                    if (taskKind !== 'execution')
                        return;
                    const workbenchEl = host.querySelector('[data-task-workbench]');
                    if (!workbenchEl)
                        return;
                    workbench = await createQueryWorkbench(workbenchEl, {
                        connection: draft.connection,
                        context: 'remote',
                        routes: hostContext.routes,
                        request: hostContext.request,
                        editorTransport: hostContext.editorTransport,
                        documentUri: () => hostContext.getActiveDocument()?.path || 'untitled.etlsql',
                        scriptText: () => hostContext.shell.getScriptText(),
                        value: draft.body,
                        label: `Remote query · ${draft.connection}`,
                        runLabel: 'Run this task',
                        onChange: (value) => { draft.body = value; },
                    });
                },
            });
            const repaint = () => {
                workbench?.dispose?.();
                workbench = null;
                paint();
            };
            /** One field: a text box, with the connection's tables or a table's columns to pick from. */
            function fieldMarkup(field) {
                const value = String(draft[field.name] || '');
                return `<label>${escapeHtml(field.label)}${field.optional ? ' <em>(optional)</em>' : ''}
                        <input type="text" data-task-field="${escapeHtml(field.name)}"
                            value="${escapeHtml(value)}"
                            placeholder="${escapeHtml(field.placeholder || '')}"
                            ${field.picker === 'table' ? `list="etlsql-task-tables-${escapeHtml(field.name)}"` : ''}
                            ${field.mono ? 'spellcheck="false"' : ''}>
                        ${field.picker === 'table' ? `<datalist id="etlsql-task-tables-${escapeHtml(field.name)}"
                            data-table-picks>${tableOptionsMarkup()}</datalist>` : ''}
                        ${field.picker === 'columns' ? `<span class="etlsql-studio-column-picks"
                            data-column-picks="${escapeHtml(field.name)}">${columnPicksMarkup(field.name)}</span>` : ''}
                        ${field.hint ? `<small>${escapeHtml(field.hint)}</small>` : ''}
                    </label>`;
            }
            function tableOptionsMarkup() {
                const tables = (schema?.connection === draft.connection ? schema?.tables : null) ?? [];
                return tables.map((table) => `<option value="${escapeHtml(table.name)}">`).join('');
            }
            function columnPicksMarkup(name) {
                const chosen = new Set(listOf(draft[name]).map(column => column.toLowerCase()));
                return columnsOf(draft.table).map(column => `<label>
                        <input type="checkbox" data-column-pick="${escapeHtml(name)}" value="${escapeHtml(column)}"
                            ${chosen.has(column.toLowerCase()) ? 'checked' : ''}> ${escapeHtml(column)}</label>`).join('');
            }
            /**
             * Refills the table suggestions and column ticks in place. Never a repaint: the schema
             * arrives while the author is typing, and redrawing the dialog under them moved what
             * they typed into whichever box the caret landed in next.
             */
            function refreshPickers(host) {
                for (const list of host.querySelectorAll('[data-table-picks]'))
                    list.innerHTML = tableOptionsMarkup();
                for (const picks of host.querySelectorAll('[data-column-picks]')) {
                    picks.innerHTML = columnPicksMarkup(asInput(picks).dataset.columnPicks || '');
                }
                const note = host.querySelector('[data-schema-note]');
                if (note) {
                    note.innerHTML = usesPickers && schema?.failed
                        ? guidedNoteMarkup([`The tables of ${draft.connection} could not be read (${schema.failed}). Type the names instead.`], 'warning')
                        : '';
                }
            }
            async function loadSchema(host, connection) {
                schema = { connection, tables: null, failed: null };
                const documentUri = hostContext.getActiveDocument()?.path || 'studio';
                try {
                    // A host learns a document's connections by analysing its script. The editor
                    // does that on a debounce, so a dialog opened soon after the connection was
                    // written asked about one the host had not registered yet, and was told it
                    // did not exist. The Data rail's connection list does the same first.
                    await hostContext.request(hostContext.routes.analyze, {
                        body: { script: hostContext.shell.getScriptText(), documentUri },
                    }).catch(() => undefined);
                    const read = await hostContext.request(hostContext.routes.schema, {
                        method: 'GET',
                        query: { connection, documentUri },
                        fallbackError: `The tables of ${connection} could not be read.`,
                    });
                    if (schema?.connection !== connection)
                        return;
                    schema = { connection, tables: read?.tables ?? [], failed: null };
                }
                catch (error) {
                    if (schema?.connection !== connection)
                        return;
                    schema = { connection, tables: [], failed: error?.message || 'unknown error' };
                }
                readFields(host);
                refreshPickers(host);
            }
            function schedulePreview(host, delay = 250) {
                if (!previews)
                    return;
                if (previewTimer)
                    clearTimeout(previewTimer);
                previewTimer = setTimeout(() => { void refreshPreview(host); }, delay);
            }
            async function refreshPreview(host) {
                const target = host.querySelector('[data-task-preview]');
                if (!target)
                    return;
                const turn = ++previewTurn;
                const body = {
                    op: 'preview',
                    kind: taskKind,
                    id: asInput(host.querySelector('[data-task-id]'))?.value.trim() ?? draft.id,
                    connection: needsConnection ? draft.connection : undefined,
                };
                for (const field of fields)
                    body[field.name] = draft[field.name];
                try {
                    const answer = await hostContext.request(hostContext.routes.pipelineTask, { body: { script: '', ...body } });
                    if (turn !== previewTurn)
                        return;
                    target.classList.toggle('is-incomplete', !answer?.preview);
                    target.textContent = answer?.preview || answer?.error || 'Fill in the fields to see the statement.';
                }
                catch (error) {
                    if (turn !== previewTurn)
                        return;
                    target.classList.add('is-incomplete');
                    target.textContent = error?.message || 'The statement could not be previewed.';
                }
            }
            paint();
        });
    }
    /**
     * Asks whether to run to a selected task, naming everything the run would leave behind.
     *
     * <p>The point of this dialog is the list, not the question. "Are you sure?" teaches an author to
     * click Yes; "this will MERGE into warehouse.Customers and send mail to ops@example.com" is
     * something they can actually be wrong about, and refuse.</p>
     *
     * A plan with no effects never reaches here — the caller runs it — because a confirmation that
     * appears when there is nothing to confirm is the fastest way to make the real one invisible.
     *
     * @param taskId The selected task, which the run stops at.
     * @param plan   `{ included, skipped, effects }` as the host planned it.
     * @returns true to run, false or null to leave the script alone.
     */
    function openPipelineRunPlanConfirm({ taskId, plan }) {
        const effects = plan?.effects ?? [];
        const skipped = plan?.skipped ?? [];
        const included = plan?.included ?? [];
        // Grouped by the task that performs them, because that is the unit the author selected and
        // can go look at. An effect the planner could not attribute is ambient script, and says so
        // rather than being filed under whichever task happens to sit near it.
        const groups = new Map();
        for (const effect of effects) {
            const owner = effect?.taskId || '';
            if (!groups.has(owner))
                groups.set(owner, []);
            groups.get(owner).push(effect);
        }
        const groupMarkup = [...groups.entries()].map(([owner, list]) => `
            <li>
                <span class="etlsql-studio-runplan-owner">${owner
            ? escapeHtml(owner)
            : 'Script outside any task'}</span>
                <ul class="etlsql-studio-runplan-effects">
                    ${list.map(effect => `<li>
                        <span class="etlsql-studio-runplan-action">${escapeHtml(effect.action)}</span>
                        <code>${escapeHtml(effect.target)}</code>
                        <span class="etlsql-studio-runplan-line">line ${Number(effect.line) || 0}</span>
                    </li>`).join('')}
                </ul>
            </li>`).join('');
        return hostContext.studioDialog({ kicker: 'Run to here', title: `Run the pipeline through ${taskId}` }, api => {
            api.render({
                lede: `This runs ${included.length} task${included.length === 1 ? '' : 's'} for real, `
                    + `against the connections the script declares. `
                    + `${effects.length === 1 ? 'One thing' : `${effects.length} things`} below will `
                    + `outlive the run.`,
                body: `<ul class="etlsql-studio-runplan">${groupMarkup}</ul>`
                    // Named, not hidden. A skipped sibling is the most likely reason a run that
                    // "should have worked" did not, and the author cannot guess it from the canvas.
                    + (skipped.length
                        ? guidedNoteMarkup(`Skipped, because ${escapeHtml(taskId)} does not declare that it `
                            + `waits for ${skipped.length === 1 ? 'it' : 'them'}: `
                            + skipped.map((id) => `<code>${escapeHtml(id)}</code>`).join(', '), 'info')
                        : ''),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(false) },
                    { id: 'run', label: 'Run it', primary: true, run: () => api.close(true) },
                ],
            });
        });
    }
    return { openPipelineTaskEditor, openPipelineRunPlanConfirm };
}
