/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Guided authoring surfaces for ETL-SQL Studio — the wizards and guided steps that let an author
 * produce Report-SQL without writing it.
 *
 * ── The authoring component contract ──────────────────────────────────────────────────────────
 *
 * Every surface in this module, and every surface added to it, obeys five rules. They exist because
 * each one has already been broken once, and each break was invisible until someone clicked the
 * button and nothing happened.
 *
 *   1. HOST-NEUTRAL. No `window`, no `localStorage`, no `document.querySelector` against the Studio
 *      shell, no knowledge of which host is running. Everything the surface needs arrives through
 *      `createStudioAuthoringSurfaces`. A surface that reaches for the shell works on the host it was
 *      written against and silently degrades on the others.
 *
 *   2. NO NETWORK OF ITS OWN. All I/O goes through the injected `request`, which is the only thing
 *      that knows about `authFetch` and the API base. Literal `/api/...` paths are banned — routes
 *      come from the injected table, so a route that exists on one host and not another fails a
 *      contract test instead of a user's click. The one deliberate exception is `editorTransport`,
 *      handed straight to `createScriptEditor` because the editor is a child component that owns its
 *      own transport; this module never calls it.
 *
 *   3. NO SCRIPT WRITING OF ITS OWN. Every change to the document goes through the injected `mutate`
 *      (the canonical parse → mutate → patch round-trip), so a hand edit is never clobbered and an
 *      unparseable document is never overwritten. `shell.setScriptText` exists for the one statement
 *      form the patcher cannot express — `USE DATASET` — and is not a general escape hatch.
 *
 *   4. PREVIEW BEFORE WRITE. A surface shows the exact Report-SQL it is about to write, and writes
 *      only on an explicit confirm. A step that cannot run yet says what is missing and offers the
 *      control that fixes it, rather than writing something half-formed or failing into a toast.
 *
 *   5. READ STATE FROM THE PARSE. A surface reads its starting state from the canonical parse of the
 *      current document, never from what it wrote last time. This is what makes the wizards safe to
 *      reopen after the author has hand-edited the script.
 *
 * `StudioAuthoringContractTests` enforces rules 1–3 by inspection. Rules 4 and 5 are behavioural and
 * belong to the wizard test lane.
 */

import { columnName, columnType, snapshotColumns, updateSnapshotPackage as writeSnapshotPackage } from './studio-data.js';
import {
    escapeHtml, mutationExplanationMarkup, noteMarkup as guidedNoteMarkup,
    sampleGridMarkup as sampleRowsMarkup, sqlPreviewMarkup,
} from './studio-authoring-ui.js';
import { createQueryWorkbench } from './studio-query-workbench.js';
import { taskKindLabel } from './studio-pipeline-canvas.js';
import {
    CHART_AGGREGATES, STUDIO_VISUAL_GROUPS, aggregateRows, buildAggregatedSource, defaultAggregateAlias,
    missingRequiredRoles, renderVisualSample, rolesForVisualType,
} from './visual-preview.js';

/** Connection aliases the script itself declares. Host-registered aliases deliberately do not count. */
export function declaredConnectionNames(scriptText) {
    const names = [];
    const pattern = /CREATE\s+(?:OR\s+REPLACE\s+)?CONNECTION\s+(?:IF\s+NOT\s+EXISTS\s+)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?/gi;
    let match;
    while ((match = pattern.exec(String(scriptText || ''))) !== null) names.push(match[1]);
    return names;
}

/** Parameter data types the guided step offers; the script accepts any type the parser knows. */
const STUDIO_PARAMETER_TYPES = ['VARCHAR', 'INT', 'DECIMAL', 'DATE', 'DATETIME', 'BOOLEAN'];

/** Aggregates a TABLE's GRAND_TOTAL accepts. */
const STUDIO_TOTAL_AGGREGATES = ['SUM', 'AVG', 'COUNT'];

/**
 * Suggested format patterns. These are suggestions in a free-text field, not a closed list: the
 * renderer takes any .NET numeric or date pattern, and offering only these would make the common
 * ones reachable at the cost of making everything else look unsupported.
 */
const STUDIO_FORMAT_PATTERNS = Object.freeze([
    { pattern: 'N0', label: 'Whole number — 1,235' },
    { pattern: 'N2', label: 'Number, 2 decimals — 1,234.50' },
    { pattern: 'C0', label: 'Currency — $1,235' },
    { pattern: 'C2', label: 'Currency, 2 decimals — $1,234.50' },
    { pattern: 'P1', label: 'Percentage — 12.3%' },
    { pattern: '$#,##0.00', label: 'Custom currency — $1,234.50' },
    { pattern: 'd', label: 'Short date — 8/23/2026' },
    { pattern: 'MMM yyyy', label: 'Month and year — Aug 2026' },
    { pattern: 'yyyy-MM-dd', label: 'ISO date — 2026-08-23' },
]);

/**
 * Builds the guided authoring surfaces against one Studio workbench.
 *
 * @param dialog          `{ backdrop, box }` — the shell's modal elements.
 * @param routes          Route table; `catalogRoutes` carries the ones only a catalog host serves.
 * @param request         `(route, { method, body, query, fallbackError }) => Promise<json>`; throws
 *                        with the server's message on failure. The module's only network path.
 * @param editorTransport `{ url(route), authFetch }` handed through to the embedded script editor.
 * @param mutate          The canonical parse → mutate → patch round-trip.
 * @param shell           Callbacks into the workbench: navigation, rendering, and script access.
 */
export function createStudioAuthoringSurfaces({
    dialog,
    routes,
    catalogRoutes,
    request,
    editorTransport,
    getActiveDocument,
    activeContext,
    contextFor,
    mutate,
    uniqueVisualName,
    hasWorkspaceHost,
    feedback,
    shell,
}) {
    /** A surface is only usable once the document has a sample to bind against. */
    function hasDataSample() {
        const snapshot = activeContext().snapshot;
        return Boolean(snapshot?.source && snapshotColumns(snapshot).length);
    }

    function studioDialog({ kicker, title, wide = false }, controller) {
        return new Promise(resolve => {
            let settled = false;
            const close = value => {
                if (settled) return;
                settled = true;
                document.removeEventListener('keydown', onKeyDown, true);
                dialog.backdrop.hidden = true;
                dialog.box.innerHTML = '';
                dialog.box.classList.remove('etlsql-studio-dialog-wide');
                resolve(value === undefined ? null : value);
            };
            const onKeyDown = event => {
                if (event.key !== 'Escape') return;
                event.stopPropagation();
                close(null);
            };

            dialog.box.innerHTML = `
                <div class="etlsql-studio-modal-header">
                    <div><span class="etlsql-studio-kicker">${escapeHtml(kicker)}</span><h2 data-dialog-title>${escapeHtml(title)}</h2></div>
                    <button type="button" class="etlsql-studio-dialog-dismiss" data-dialog-dismiss aria-label="Close">&times;</button>
                </div>
                <div class="etlsql-studio-modal-body etlsql-studio-guided-body" data-dialog-body></div>
                <footer class="etlsql-studio-dialog-actions" data-dialog-actions></footer>`;
            if (wide) dialog.box.classList.add('etlsql-studio-dialog-wide');
            dialog.backdrop.hidden = false;
            document.addEventListener('keydown', onKeyDown, true);
            dialog.box.querySelector('[data-dialog-dismiss]').addEventListener('click', () => close(null));

            const bodyHost = dialog.box.querySelector('[data-dialog-body]');
            const actionHost = dialog.box.querySelector('[data-dialog-actions]');

            // The footer buttons survive a re-render when the same actions are still on offer.
            //
            // A guided form repaints itself when a field changes, to keep its SQL preview and its
            // warnings true. `change` fires on blur — which is what clicking a footer button does —
            // so replacing the buttons on every repaint destroyed the very button being pressed
            // between its mousedown and its mouseup, and a click needs both on one element. The
            // first press after editing any field did nothing at all, silently, and the author had
            // to press again. `currentActions` is what the delegated handler reads, so the buttons
            // can stay put while what they do stays current.
            let currentActions = [];
            let actionSignature = null;
            actionHost.addEventListener('click', async event => {
                const button = event.target.closest('[data-dialog-action]');
                if (!button || button.disabled) return;
                try {
                    await currentActions.find(action => action.id === button.dataset.dialogAction)?.run?.();
                } catch (error) {
                    // A dropped promise here is invisible: the mutation may already have landed
                    // while the dialog silently stops responding. Say so instead.
                    feedback.notify(error?.message || 'That action could not be completed.',
                        { title: 'Action failed', tone: 'error' });
                }
            });
            const api = {
                close,
                setTitle(next) { dialog.box.querySelector('[data-dialog-title]').textContent = next; },
                // Every footer button is disabled while a request is in flight, so a slow schema read
                // cannot be double-submitted into two datasets.
                busy(flag) { actionHost.querySelectorAll('button').forEach(button => { button.disabled = flag; }); },
                render({ lede = '', body = '', actions = [], wire } = {}) {
                    bodyHost.innerHTML = (lede ? `<p class="etlsql-studio-guided-lede">${lede}</p>` : '') + body;
                    currentActions = actions;
                    // Rebuilt only when the offer itself changed. `disabled` is not part of the
                    // signature: it flips while the author types, which is exactly when the buttons
                    // must not be replaced, so it is applied to the buttons already there.
                    const signature = actions
                        .map(action => [action.id, action.label, action.primary ? 1 : 0].join('|'))
                        .join('||');
                    if (signature !== actionSignature) {
                        actionSignature = signature;
                        actionHost.innerHTML = actions.map(action => `<button type="button"
                            class="etlsql-studio-btn${action.primary ? ' is-primary' : ''}"
                            data-dialog-action="${escapeHtml(action.id)}"
                            >${escapeHtml(action.label)}</button>`).join('');
                    }
                    for (const action of actions) {
                        const button = [...actionHost.children].find(node => node.dataset?.dialogAction === action.id);
                        if (button) button.disabled = Boolean(action.disabled);
                    }
                    wire?.(bodyHost);
                    bodyHost.querySelector('input:not([type=hidden]), select, textarea')?.focus();
                },
            };
            controller(api);
        });
    }

    /**
     * Explains why a step cannot run and offers the control that unblocks it. Returns true when the
     * author took the remedy, so the caller can retry the step.
     */
    async function guidedBlocker({ kicker, title, lede, remedyLabel, remedy }) {
        const choice = await studioDialog({ kicker, title }, api => api.render({
            lede,
            actions: [
                { id: 'cancel', label: 'Not now', run: () => api.close(null) },
                { id: 'fix', label: remedyLabel, primary: true, run: () => api.close('fix') },
            ],
        }));
        if (choice !== 'fix') return false;
        await remedy();
        return true;
    }

    /**
     * How a new visual should point at the current sample. A dataset-backed sample is referenced by
     * name so every visual shares one query; a connection-backed sample still has to inline its
     * SELECT, because there is no named query to reference yet.
     */
    function visualSourceBinding() {
        const source = activeContext().snapshot?.source || null;
        if (source && String(source).startsWith('&')) return { dataset: source, options: {} };
        return { dataset: null, options: source ? { inline_source: visualSourceClause(source) } : {} };
    }

    /**
     * A snapshot's source, written as something `SOURCE =` will actually accept.
     *
     * The sample endpoint names what it sampled — `#users`, `&sales`, or `alias.Table`. The first two
     * are already valid SOURCE operands; the third is not, because SOURCE takes a temp table, a
     * dataset name, or a query, and never a qualified connection reference. Passing the qualified
     * name through produced `SOURCE = alias.Table`, which does not parse — so the patch carrying it
     * was refused whole, the script never changed, and the visual the author had just added was gone
     * on the next reload, with nothing said.
     */
    function visualSourceClause(source) {
        const text = String(source).trim();
        if (text.startsWith('#') || text.startsWith('&')) return text;
        if (text.startsWith('(') || /^select\b/i.test(text)) return text;
        return text.includes('.') ? `(SELECT * FROM ${text})` : text;
    }

    function guidedColumnNames() {
        return snapshotColumns(activeContext().snapshot).map(columnName);
    }

    function guidedNumericColumns() {
        const context = activeContext();
        const rows = context.snapshot?.rows || [];
        return snapshotColumns(context.snapshot)
            .filter(column => columnType(column, rows) === 'number')
            .map(columnName);
    }

    /** Samples a named dataset so the canvas, field list, and filters all read from the same rows. */
    async function loadDatasetSample(datasetName) {
        const doc = getActiveDocument();
        const context = contextFor(doc);
        const sample = await request(routes.dataSample, {
            body: {
                sourceKind: 'dataset',
                dataset: datasetName,
                documentUri: doc?.path || 'studio',
                script: shell.getScriptText(),
            },
            fallbackError: `${datasetName} could not be sampled.`,
        });
        context.snapshot = {
            source: sample.source || datasetName,
            columns: sample.columns || [],
            rowCount: sample.rowCount ?? sample.rows?.length ?? 0,
            rows: sample.rows || [],
        };
        context.snapshotCache.set(datasetName, context.snapshot);
        if (getActiveDocument() === doc) {
            writeSnapshotPackage(activeContext(), context.snapshot);
            shell.refreshSnapshot();
            shell.renderSidebar();
        }
        return context.snapshot;
    }

    // --- Step 1: the data wizard ------------------------------------------------------------------
    //
    // There are exactly three ways a report gets data, and they have different prerequisites and
    // different runtime behaviour:
    //
    //   * Use an existing dataset — one this script already declares, or a registered dataset the
    //     signed-in user has permission to read. Reusing a registered one writes `USE DATASET &name`.
    //     No connection is involved; the dataset was produced by some other process.
    //
    //   * Create a new dataset — cached, with a TTL saying how long its rows stay valid. Needs a
    //     connection to read from, so a script with no `CREATE CONNECTION` cannot reach this path until
    //     the connection wizard has written one.
    //
    //   * Live query — no cache at all: the visual's SOURCE holds the query and the connection is read
    //     on every run. Also needs a connection. Chosen when the report must show current data and the
    //     cost of querying every time is acceptable.
    //
    // Host-registered aliases never count as connections here: an alias this script does not declare
    // would preview correctly and then fail for every other reader of the report.

    function datasetBaseName(seed) {
        const cleaned = String(seed || 'dataset').replace(/[^A-Za-z0-9_]/g, '_').replace(/^_+/, '').toLowerCase();
        return /^[a-z]/.test(cleaned) ? cleaned : `data_${cleaned || 'set'}`;
    }

    /** Datasets this script declares, from the canonical parse rather than a text scan. */
    async function scriptDatasetNames() {
        try {
            const parsed = await request(routes.parse, { body: { script: shell.getScriptText() } });
            return (parsed.designState?.datasets || []).map(dataset => ({
                name: String(dataset.name || '').startsWith('&') ? dataset.name : `&${dataset.name}`,
                query: dataset.query || '',
            }));
        } catch {
            // A document mid-keystroke does not parse; an empty list is honest, and the wizard's
            // other path still works. This catch previously swallowed a ReferenceError as well,
            // which disabled the reuse path entirely behind that same honest-looking message.
            return [];
        }
    }

    /**
     * Datasets the signed-in user may read from the report registry. Only the catalog host has one —
     * the desktop workspace has no registry, so it honestly reports none rather than 404ing.
     */
    async function registryDatasets() {
        if (hasWorkspaceHost) return [];
        try {
            const data = await request(catalogRoutes.datasetRegistry, { method: 'GET' });
            return (Array.isArray(data) ? data : data.datasets || [])
                .filter(dataset => dataset?.name)
                .map(dataset => ({
                    name: String(dataset.name).startsWith('&') ? dataset.name : `&${dataset.name}`,
                    folderPath: dataset.folderPath || '',
                    rowCount: dataset.rowCount ?? null,
                    accessLevel: dataset.accessLevel || null,
                    isStale: Boolean(dataset.isStale),
                }));
        } catch {
            return [];
        }
    }

    /**
     * Connection -> table or query -> name -> CREATE DATASET, or reuse an existing dataset. This is
     * the only path that produces a named, reusable query without writing code, which is what every
     * later step depends on. Resolves with the dataset name that is now in play.
     */
    async function openDataWizard({ intent = null, connection = null } = {}) {
        const doc = getActiveDocument();
        if (!doc) return null;
        const context = contextFor(doc);
        const wizard = {
            // Resuming after the connection wizard skips straight back to the pane the author was on.
            pane: intent ? 'connection' : 'start',
            // 'dataset' caches through CREATE DATASET; 'live' binds the query straight to the visuals.
            intent: intent || 'dataset',
            ttl: '',
            scriptDatasets: null,
            registry: null,
            connections: declaredConnectionNames(shell.getScriptText()),
            connection: null,
            tables: null,
            table: null,
            mode: 'table',
            query: '',
            name: '',
            preview: null,
            error: null,
            queryWorkbench: null,
        };

        return await studioDialog({ kicker: 'Step 1 · Choose data', title: 'Choose data', wide: true }, api => {
            const fail = message => { wizard.error = message; paint(); };
            const errorMarkup = () => (wizard.error ? guidedNoteMarkup(wizard.error, 'error') : '');

            const disposeWorkbench = () => {
                wizard.queryWorkbench?.dispose?.();
                wizard.queryWorkbench = null;
            };
            const finish = value => { disposeWorkbench(); api.close(value); };

            // ── Panes ─────────────────────────────────────────────────────────────────────────────

            const paint = () => {
                if (wizard.pane !== 'source' || wizard.mode !== 'query') disposeWorkbench();
                if (wizard.pane === 'start') return paintStart();
                if (wizard.pane === 'existing') return paintExisting();
                if (wizard.pane === 'connection') return paintConnection();
                if (wizard.pane === 'source') return paintSource();
                return paintName();
            };

            const paintStart = () => {
                const available = (wizard.scriptDatasets?.length || 0) + (wizard.registry?.length || 0);
                const loading = wizard.scriptDatasets === null || wizard.registry === null;
                const connectionNote = wizard.connections.length
                    ? `Reads from ${wizard.connections.length === 1 ? `the ${wizard.connections[0]} connection` : 'one of this report’s connections'}`
                    : 'Needs a connection first — the wizard will create one';
                return api.render({
                    lede: 'Three ways to get data, and they behave differently at run time. '
                        + 'A <strong>dataset</strong> is a named query the report caches and reuses; a <strong>live query</strong> '
                        + 'is read fresh from the connection every time the report runs.',
                    body: errorMarkup() + `<div class="etlsql-studio-choice-list">
                        <button type="button" data-start-path="existing" ${loading || !available ? 'disabled' : ''}>
                            <strong>Use an existing dataset</strong>
                            <span>${loading
                                ? 'Looking for datasets you can use…'
                                : available
                                    ? `${available} dataset${available === 1 ? '' : 's'} available · cached, refreshed on its own schedule`
                                    : 'None available — this report declares none, and none are shared with you'}</span>
                        </button>
                        <button type="button" data-start-path="create">
                            <strong>Create a new dataset</strong>
                            <span>Cached with a refresh interval and TTL · ${escapeHtml(connectionNote)}</span>
                        </button>
                        <button type="button" data-start-path="live">
                            <strong>Live query</strong>
                            <span>No cache — the connection is queried on every run · ${escapeHtml(connectionNote)}</span>
                        </button>
                    </div>`,
                    actions: [{ id: 'cancel', label: 'Cancel', run: () => finish(null) }],
                    wire: host => host.querySelectorAll('[data-start-path]').forEach(button => button.addEventListener('click', () => {
                        wizard.error = null;
                        const path = button.dataset.startPath;
                        wizard.intent = path === 'live' ? 'live' : 'dataset';
                        wizard.pane = path === 'existing' ? 'existing' : 'connection';
                        paint();
                    })),
                });
            };

            const paintExisting = () => api.render({
                lede: 'Pick the dataset this report should read from. A dataset already declared here is used as-is; '
                    + 'a registered one is brought in with <code>USE DATASET</code>.',
                body: errorMarkup()
                    + (wizard.scriptDatasets?.length ? `<div class="etlsql-studio-guided-group"><span>In this report</span>
                        <div class="etlsql-studio-choice-list">${wizard.scriptDatasets.map(dataset => `
                            <button type="button" data-use-dataset="${escapeHtml(dataset.name)}" data-dataset-origin="script">
                                <strong>${escapeHtml(dataset.name)}</strong>
                                <span>${escapeHtml((dataset.query || '').replace(/\s+/g, ' ').slice(0, 90)) || 'Declared in this script'}</span>
                            </button>`).join('')}</div></div>` : '')
                    + (wizard.registry?.length ? `<div class="etlsql-studio-guided-group"><span>Shared with you</span>
                        <div class="etlsql-studio-choice-list">${wizard.registry.map(dataset => `
                            <button type="button" data-use-dataset="${escapeHtml(dataset.name)}" data-dataset-origin="registry">
                                <strong>${escapeHtml(dataset.name)}</strong>
                                <span>${escapeHtml(dataset.folderPath || 'Registered dataset')}${dataset.rowCount != null ? ` · ${dataset.rowCount} rows` : ''}${dataset.isStale ? ' · stale' : ''}</span>
                            </button>`).join('')}</div></div>` : '')
                    + (!wizard.scriptDatasets?.length && !wizard.registry?.length
                        ? guidedNoteMarkup('No datasets are available to this report yet. Create one instead.', 'info')
                        : ''),
                actions: [
                    { id: 'back', label: 'Back', run: () => { wizard.pane = 'start'; wizard.error = null; paint(); } },
                    { id: 'create', label: 'Create a new dataset', primary: true, run: () => { wizard.pane = 'connection'; wizard.error = null; paint(); } },
                ],
                wire: host => host.querySelectorAll('[data-use-dataset]').forEach(button =>
                    button.addEventListener('click', () => useExistingDataset(button.dataset.useDataset, button.dataset.datasetOrigin))),
            });

            const paintConnection = () => {
                if (!wizard.connections.length) {
                    return api.render({
                        lede: 'A new dataset reads from a connection, and this report does not declare one yet. '
                            + 'The connection wizard writes a <code>CREATE CONNECTION</code> statement into the script, '
                            + 'which is what makes the report runnable anywhere — not just in this session.',
                        body: errorMarkup() + guidedNoteMarkup(
                            'A report that borrows a connection from this session works for you and fails for everyone else '
                            + 'who opens it — and for its scheduled runs — after previewing perfectly here. That is why the '
                            + 'connections your host already knows about are not offered: only a CREATE CONNECTION statement '
                            + 'inside the script travels with the report.', 'info'),
                        actions: [
                            { id: 'back', label: 'Back', run: () => { wizard.pane = 'start'; wizard.error = null; paint(); } },
                            { id: 'connect', label: 'Create a connection', primary: true, run: openConnectionThenReturn },
                        ],
                    });
                }
                return api.render({
                    lede: 'Pick the connection this dataset reads from. These are the connections this report declares.',
                    body: errorMarkup() + `<div class="etlsql-studio-choice-list">${wizard.connections.map(alias => `
                        <button type="button" data-pick-connection="${escapeHtml(alias)}" class="${wizard.connection === alias ? 'active' : ''}">
                            <strong>${escapeHtml(alias)}</strong><span>Declared in this report</span></button>`).join('')}</div>`,
                    actions: [
                        { id: 'back', label: 'Back', run: () => { wizard.pane = 'start'; wizard.error = null; paint(); } },
                        { id: 'connect', label: 'New connection…', run: openConnectionThenReturn },
                    ],
                    wire: host => host.querySelectorAll('[data-pick-connection]').forEach(button =>
                        button.addEventListener('click', () => openConnection(button.dataset.pickConnection))),
                });
            };

            const paintSource = () => api.render({
                lede: `Reading from <strong>${escapeHtml(wizard.connection)}</strong>. Pick a table to read whole, or build the query yourself.`,
                body: errorMarkup() + `
                    <div class="etlsql-studio-segmented" role="group" aria-label="Dataset source">
                        <button type="button" data-source-mode="table" class="${wizard.mode === 'table' ? 'active' : ''}">Pick a table</button>
                        <button type="button" data-source-mode="query" class="${wizard.mode === 'query' ? 'active' : ''}">Write a query</button>
                    </div>`
                    + (wizard.mode === 'table'
                        ? (wizard.tables === null
                            ? '<div class="etlsql-studio-loading">Loading tables…</div>'
                            : wizard.tables.length
                                ? `<label class="etlsql-studio-guided-field"><span>Filter</span>
                                    <input type="search" data-table-filter placeholder="Search tables"></label>
                                   <div class="etlsql-studio-choice-list is-compact" data-table-list>${wizard.tables.map(table => `
                                    <button type="button" data-pick-table="${escapeHtml(table.name)}" class="${wizard.table === table.name ? 'active' : ''}">
                                        <strong>${escapeHtml(table.name)}</strong>
                                        <span>${table.columns?.length || 0} field${table.columns?.length === 1 ? '' : 's'}</span>
                                    </button>`).join('')}</div>`
                                : guidedNoteMarkup('This connection reported no tables you can read.', 'warning'))
                        : '<div class="etlsql-studio-query-workbench" data-query-workbench></div>')
                    + (wizard.preview ? sampleRowsMarkup(wizard.preview) : ''),
                actions: [
                    { id: 'back', label: 'Back', run: () => { wizard.pane = 'connection'; wizard.error = null; wizard.preview = null; paint(); } },
                    { id: 'next', label: 'Next', primary: true, disabled: !wizardQuery(), run: goToName },
                ],
                wire: host => {
                    host.querySelectorAll('[data-source-mode]').forEach(button => button.addEventListener('click', () => {
                        wizard.mode = button.dataset.sourceMode;
                        wizard.preview = null;
                        paint();
                    }));
                    host.querySelectorAll('[data-pick-table]').forEach(button => button.addEventListener('click', () => {
                        wizard.table = button.dataset.pickTable;
                        wizard.preview = null;
                        paint();
                        // Seeing the rows is the point of this step, so the sample loads on selection
                        // rather than behind another button.
                        previewTable();
                    }));
                    const filter = host.querySelector('[data-table-filter]');
                    filter?.addEventListener('input', () => {
                        const query = filter.value.trim().toLowerCase();
                        host.querySelectorAll('[data-pick-table]').forEach(button => {
                            button.hidden = Boolean(query) && !button.dataset.pickTable.toLowerCase().includes(query);
                        });
                    });
                    const workbenchHost = host.querySelector('[data-query-workbench]');
                    if (workbenchHost) mountQueryWorkbench(workbenchHost);
                },
            });

            const paintName = () => {
                if (wizard.intent === 'live') return paintLive();
                const base = datasetBaseName(wizard.name);
                const collides = (wizard.scriptDatasets || []).some(dataset => dataset.name.replace(/^&/, '').toLowerCase() === base);
                // TTL only. CREATE DATASET ... REFRESH EVERY is retired and the parser rejects it, so
                // emitting it produced a script that would not parse — which the patcher refused
                // wholesale, leaving the wizard reporting success while writing nothing.
                const lifespan = wizard.ttl.trim() ? ` TTL = '${wizard.ttl.trim()}'` : '';
                const sql = `CREATE DATASET &${base}${lifespan} AS (
  ${wizardQuery()}
);`;
                return api.render({
                    lede: 'Name the dataset. Visuals reference it as <code>&amp;name</code>, and the report runs its query once no matter how many visuals read from it.',
                    body: errorMarkup()
                        + `<label class="etlsql-studio-guided-field"><span>Dataset name</span>
                            <div class="etlsql-studio-prefixed-input"><span>&amp;</span>
                            <input type="text" data-dataset-name value="${escapeHtml(base)}" spellcheck="false"></div></label>`
                        + (collides ? guidedNoteMarkup('This report already has a dataset with that name. Studio will add a numeric suffix unless you change it.', 'warning') : '')
                        + `<label class="etlsql-studio-guided-field"><span>Keep cached rows for (TTL)</span>
                            <input type="text" data-dataset-ttl value="${escapeHtml(wizard.ttl)}" placeholder="2h" spellcheck="false"></label>
                          <p class="etlsql-studio-guided-hint">Durations like <code>30m</code>, <code>2h</code>, <code>1d</code>. Leave it blank to use the host’s default — an omitted TTL is not the same as a zero one. To refresh on a schedule, create a schedule and a job for the report; a dataset cannot carry its own refresh interval.</p>`
                        + sqlPreviewMarkup(sql,
                            `Adds a named dataset to the top of the script. Its query runs once per report run, `
                            + `and every visual that reads &${base} shares that one result.`)
                        + (wizard.preview ? sampleRowsMarkup(wizard.preview) : ''),
                    actions: [
                        { id: 'back', label: 'Back', run: () => { wizard.pane = 'source'; wizard.error = null; paint(); } },
                        { id: 'create', label: 'Create dataset', primary: true, run: create },
                    ],
                    wire: host => {
                        host.querySelector('[data-dataset-name]')?.addEventListener('change', event => { wizard.name = event.target.value; paint(); });
                        host.querySelector('[data-dataset-ttl]')?.addEventListener('change', event => { wizard.ttl = event.target.value; paint(); });
                    },
                });
            };

            const paintLive = () => {
                const source = liveSourceClause();
                return api.render({
                    lede: 'A <strong>live query</strong> is bound straight to the visuals you build next, as their '
                        + '<code>SOURCE</code>. Nothing is cached: every run reads the connection again, so readers always '
                        + 'see current data and every run costs a query.',
                    body: errorMarkup()
                        + sqlPreviewMarkup(`CREATE VISUAL … (
    SOURCE = ${source},
    …
);`,
                            'Changes nothing on its own. Each visual you add next is written with this SOURCE, so each one '
                            + 'queries the connection again when the report runs.',
                            'Visuals will be written with this source')
                        + guidedNoteMarkup('Nothing is written to the script yet — the source lands on each visual as you add it. '
                            + 'Switch to a dataset later if the same query starts feeding several visuals.', 'info')
                        + (wizard.preview ? sampleRowsMarkup(wizard.preview) : ''),
                    actions: [
                        { id: 'back', label: 'Back', run: () => { wizard.pane = 'source'; wizard.error = null; paint(); } },
                        { id: 'use', label: 'Use this live source', primary: true, run: useLiveSource },
                    ],
                });
            };

            // ── Actions ───────────────────────────────────────────────────────────────────────────

            // A table is wrapped in its own SELECT rather than named directly: SOURCE does not take a
            // qualified connection reference, so the preview above has to show what will be written.
            const liveSourceClause = () => (wizard.mode === 'table' && wizard.table
                ? `(SELECT * FROM ${wizard.connection}.${wizard.table})`
                : `(${wizardQuery()})`);

            const useLiveSource = async () => {
                api.busy(true);
                const source = liveSourceClause();
                const columns = snapshotColumns(wizard.preview);
                const rows = wizard.preview?.rows || [];
                // No script statement is written here. A live source belongs to each visual's SOURCE
                // clause, so it lands as visuals are added; writing something now would leave a
                // statement behind if the author never adds one.
                context.snapshot = {
                    source,
                    columns,
                    rowCount: wizard.preview?.rowCount ?? rows.length,
                    rows,
                };
                context.snapshotCache.set(source, context.snapshot);
                context.selectedSource = { connection: wizard.connection, table: wizard.table };
                context.sourceColumns = columns;
                writeSnapshotPackage(activeContext(), context.snapshot);
                shell.refreshSnapshot();
                shell.renderSidebar();
                feedback.notify(
                    `Visuals will read live from ${source}. Nothing is cached — every run queries ${wizard.connection}.`,
                    { title: 'Live source ready', tone: 'success' });
                finish(source);
            };

            const wizardQuery = () => (wizard.mode === 'table'
                ? (wizard.connection && wizard.table ? `SELECT * FROM ${wizard.connection}.${wizard.table}` : '')
                : (wizard.queryWorkbench?.getValue?.() ?? wizard.query).trim().replace(/;$/, ''));

            const openConnectionThenReturn = () => {
                // The connection wizard owns the whole modal surface, so this one steps aside. It
                // resumes where it left off rather than at the first pane: the author already said
                // they were creating a dataset, and making them say it again is the whole reason this
                // detour felt like a dead end.
                const resumeIntent = wizard.intent;
                finish(null);
                shell.openConnectionWizard({
                    onDone: alias => openDataWizard({ intent: resumeIntent, connection: alias }),
                });
            };

            const openConnection = async alias => {
                wizard.connection = alias;
                wizard.tables = null;
                wizard.table = null;
                wizard.preview = null;
                wizard.pane = 'source';
                wizard.error = null;
                paint();
                try {
                    const schema = await request(routes.schema, {
                        method: 'GET',
                        query: { connection: alias, documentUri: doc.path || 'studio' },
                        fallbackError: `The schema for ${alias} could not be read.`,
                    });
                    wizard.tables = schema.tables || [];
                } catch (error) {
                    wizard.tables = [];
                    wizard.error = error.message;
                }
                paint();
            };

            const previewTable = async () => {
                if (wizard.mode !== 'table' || !wizard.table) return;
                try {
                    wizard.preview = await sampleConnectionTable(wizard.connection, wizard.table);
                    wizard.error = null;
                } catch (error) {
                    wizard.preview = null;
                    wizard.error = error.message;
                }
                paint();
            };

            const mountQueryWorkbench = async host => {
                if (wizard.queryWorkbench) return;
                wizard.queryWorkbench = await createQueryWorkbench(host, {
                    connection: wizard.connection,
                    routes,
                    request,
                    editorTransport,
                    documentUri: () => getActiveDocument()?.path || 'untitled.rptsql',
                    scriptText: () => shell.getScriptText(),
                    value: wizard.query || `SELECT *\nFROM ${wizard.connection}.`,
                    onChange: value => {
                        wizard.query = value;
                        const next = dialog.box.querySelector('[data-dialog-action="next"]');
                        if (next) next.disabled = !wizardQuery();
                    },
                    onSample: sample => { wizard.preview = sample; },
                });
                const next = dialog.box.querySelector('[data-dialog-action="next"]');
                if (next) next.disabled = !wizardQuery();
            };

            const goToName = async () => {
                wizard.query = wizard.queryWorkbench?.getValue?.() ?? wizard.query;
                // A live source is bound without ever running through the dataset sampler, so this is
                // the last chance to prove the query returns columns the visuals can bind to.
                if (wizard.intent === 'live' && !wizard.preview && wizard.mode === 'table') await previewTable();
                wizard.pane = 'name';
                wizard.error = null;
                wizard.name = wizard.name || datasetBaseName(wizard.mode === 'table' ? wizard.table : `${wizard.connection}_query`);
                paint();
            };

            const useExistingDataset = async (name, origin) => {
                api.busy(true);
                try {
                    if (origin === 'registry') {
                        // USE DATASET is a script statement, not designer state, so it is inserted as
                        // text ahead of the presentation statements the same way CREATE CONNECTION is.
                        insertScriptStatement(`USE DATASET ${name};`);
                    }
                    const snapshot = await loadDatasetSample(name);
                    context.selectedSource = { connection: null, table: name };
                    feedback.notify(
                        `Reading from ${name} — ${snapshot.rowCount} row${snapshot.rowCount === 1 ? '' : 's'} sampled.`,
                        { title: 'Dataset ready', tone: 'success' });
                    finish(name);
                } catch (error) {
                    api.busy(false);
                    fail(error.message);
                }
            };

            const create = async () => {
                const base = datasetBaseName(wizard.name);
                const query = wizardQuery();
                if (!query) return fail('This dataset has no query yet.');
                api.busy(true);
                const created = await mutate('Create dataset', design => {
                    design.datasets ||= [];
                    const taken = new Set(design.datasets.map(item => String(item.name || '').replace(/^&/, '').toLowerCase()));
                    let name = base;
                    let suffix = 2;
                    while (taken.has(name.toLowerCase())) name = `${base}_${suffix++}`;
                    design.datasets.push({
                        id: `studio_ds_${Date.now().toString(36)}`,
                        name: `&${name}`,
                        query,
                        ttl: wizard.ttl.trim() || null,
                    });
                    return `&${name}`;
                });
                if (!created) { api.busy(false); return; }

                context.selectedSource = { connection: wizard.connection, table: wizard.mode === 'table' ? wizard.table : created };
                context.sourceColumns = snapshotColumns(wizard.preview);
                try {
                    const snapshot = await loadDatasetSample(created);
                    feedback.notify(
                        `${created} is ready with ${snapshot.rowCount} sampled row${snapshot.rowCount === 1 ? '' : 's'}. Visuals can reference it by name.`,
                        { title: 'Dataset created', tone: 'success' });
                } catch (error) {
                    // The statement is in the script either way; say so rather than implying the step
                    // failed, because the author's next step depends on knowing it exists.
                    feedback.notify(
                        `${created} was written to the script, but its preview could not run: ${error.message}`,
                        { title: 'Dataset created without a sample', tone: 'warning' });
                }
                finish(created);
            };

            // A connection just created is the one the author meant to use, so open it rather than
            // making them pick it out of a list of one.
            if (intent && connection && wizard.connections.includes(connection)) openConnection(connection);
            else paint();

            // The aliases the host offers, merged in as they arrive. The script's own come first
            // because they are the ones that make the report runnable anywhere; the host's are added
            // rather than substituted, because on the Portal they are the only ones there are.
            Promise.resolve(shell.availableConnections?.() ?? []).then(hosted => {
                const names = (hosted || [])
                    .map(item => (typeof item === 'string' ? item : item?.alias || item?.name))
                    .filter(Boolean);
                const seen = new Set(wizard.connections.map(alias => String(alias).toLowerCase()));
                for (const name of names) {
                    if (seen.has(String(name).toLowerCase())) continue;
                    seen.add(String(name).toLowerCase());
                    wizard.connections.push(name);
                }
                if (wizard.pane === 'start' || wizard.pane === 'connection') paint();
            }).catch(() => {
                // An alias list that cannot be read leaves the script's own, which is what the
                // wizard offered before and is still a usable answer.
            });

            Promise.all([scriptDatasetNames(), registryDatasets()]).then(([scripts, registry]) => {
                wizard.scriptDatasets = scripts;
                // A registered dataset this script already declares would be two routes to the same
                // rows, and only one of them is editable here.
                const declared = new Set(scripts.map(dataset => dataset.name.toLowerCase()));
                wizard.registry = registry.filter(dataset => !declared.has(dataset.name.toLowerCase()));
                if (wizard.pane === 'start' || wizard.pane === 'existing') paint();
            });
        });
    }

    /** Inserts a statement ahead of the presentation statements, leaving everything else untouched. */
    function insertScriptStatement(statement) {
        const doc = getActiveDocument();
        if (!doc) return;
        const script = shell.getScriptText();
        const match = /CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?(?:VISUAL|CONTAINER|BUTTON|PAGE)\b/i.exec(script);
        const at = match ? match.index : script.length;
        const next = script.slice(0, at) + statement + '\n\n' + script.slice(at);
        doc.content = next;
        doc.isDirty = true;
        shell.setScriptText(next, 'Use dataset');
        shell.renderTabs();
    }

    /** Samples a connection table through the host's design-time preview budget. */
    async function sampleConnectionTable(connection, table) {
        const doc = getActiveDocument();
        const sample = await request(routes.dataSample, {
            body: {
                sourceKind: 'connection',
                connection,
                table,
                documentUri: doc?.path || 'studio',
                script: shell.getScriptText(),
            },
            fallbackError: `${table} could not be sampled.`,
        });
        return {
            source: sample.source || `${connection}.${table}`,
            columns: sample.columns || [],
            rows: sample.rows || [],
            rowCount: sample.rowCount ?? sample.rows?.length ?? 0,
        };
    }


    /** Step 1 for both workflows. The wizard's first pane already covers reuse vs. create. */
    async function runChooseDataStep() {
        shell.setActivity('catalog');
        return await openDataWizard();
    }

    /** Every step after the first needs a sample; this is the one place that says so. */
    async function requireDataSample(stepLabel) {
        if (hasDataSample()) return true;
        return await guidedBlocker({
            kicker: stepLabel,
            title: 'Choose data first',
            lede: 'This step writes report items that read from a dataset, so Studio needs one before it can write anything useful. '
                + 'Creating a dataset takes a connection, a table or query, and a name.',
            remedyLabel: 'Create a dataset',
            remedy: () => runChooseDataStep(),
        });
    }

    // --- The chart builder ------------------------------------------------------------------------
    //
    // Picking a visual type and assigning fields to its roles, against the real sample, with the
    // Report-SQL it will write shown before it is written. The same builder serves the dashboard's
    // "add a visual" step, the paginated report's bands, and the sidebar's Build entry, because they
    // are the same task: bind columns to a visual's roles and see the result.

    /** Field kind for a column, used to suggest a sensible default per role. */
    function guidedFieldKind(name) {
        const context = activeContext();
        const column = snapshotColumns(context.snapshot).find(item => columnName(item) === name);
        return column ? columnType(column, context.snapshot?.rows || []) : 'text';
    }

    /**
     * Opens the builder. `seed` may carry a starting type and mappings, so callers that already know
     * what they want (the paginated detail band, say) open it pre-filled rather than blank.
     * Resolves with the created visual's name, or null.
     */
    async function openChartBuilder(seed = {}) {
        if (!await requireDataSample(seed.kicker || 'Build a chart')) return null;
        const context = activeContext();
        const columns = snapshotColumns(context.snapshot).map(columnName);
        const draft = {
            type: (seed.type || 'BAR').toUpperCase(),
            title: seed.title || '',
            mappings: { ...(seed.mappings || {}) },
            // Per measure role: { aggregate, alias }. A chart usually plots a summary rather than a
            // stored column — "users per day" is a COUNT grouped by day — so the aggregate belongs to
            // the visual, letting two charts over one dataset summarise it differently.
            aggregates: {},
            // How the numbers and dates read. Two patterns, not a formatting panel: the value the
            // chart is about, and the axis it is plotted against. Everything past that — colours,
            // grid lines, data labels, axis bounds — belongs to the Format inspector on the selected
            // tile, which already does the job and is opened once this visual is added.
            format: { value: '', axis: '' },
        };
        if (!Object.keys(draft.mappings).length) autoAssignRoles(draft, columns);

        return await studioDialog({ kicker: seed.kicker || 'Build a chart', title: 'Build a visual', wide: true }, api => {
            /** The measure role and its aggregate, when one is set. */
            const activeMeasure = () => {
                for (const role of rolesForVisualType(draft.type)) {
                    if (!role.measure) continue;
                    const setting = draft.aggregates[role.key];
                    const column = draft.mappings[role.key];
                    if (!setting || setting.aggregate === 'NONE' || !column) continue;
                    return {
                        role: role.key,
                        column,
                        aggregate: setting.aggregate,
                        alias: setting.alias || defaultAggregateAlias(setting.aggregate, column),
                    };
                }
                return null;
            };

            /** Everything bound that is not the aggregated measure becomes a grouping column. */
            const groupingColumns = measure => [...new Set(rolesForVisualType(draft.type)
                .filter(role => role.key !== measure.role && !role.repeatable)
                .map(role => draft.mappings[role.key])
                .filter(Boolean))];

            /** Mappings as written: an aggregated role points at the alias, not the source column. */
            const resolvedMappings = () => {
                const measure = activeMeasure();
                if (!measure) return draft.mappings;
                return { ...draft.mappings, [measure.role]: measure.alias };
            };

            const sourceExpression = () => {
                const binding = visualSourceBinding();
                const base = binding.dataset || binding.options.inline_source || '&dataset';
                const measure = activeMeasure();
                return measure
                    ? buildAggregatedSource({ base, groupBy: groupingColumns(measure), measure })
                    : base;
            };

            /** The sample shaped the way the query will shape it, so the preview cannot mislead. */
            const previewSample = () => {
                const measure = activeMeasure();
                return measure
                    ? aggregateRows(context.snapshot, { groupBy: groupingColumns(measure), measure })
                    : context.snapshot;
            };

            const previewVisual = () => ({
                id: 'builder_preview',
                name: draft.title || `${draft.type.toLowerCase()}_visual`,
                type: draft.type,
                title: draft.title,
                mappings: resolvedMappings(),
                options: {},
            });

            /** The category axis a format can apply to; only a cartesian chart has one. */
            const axisRole = () => rolesForVisualType(draft.type).find(role => role.key === 'X') || null;

            /** Format patterns written the way the generator writes them, so preview and write agree. */
            const formatOptions = () => {
                const options = [];
                const value = draft.format.value.trim();
                const axis = draft.format.axis.trim();
                if (value) options.push(`FORMAT = '${value.replace(/'/g, "''")}'`);
                if (axis && axisRole()) options.push(`X_AXIS (FORMAT = '${axis.replace(/'/g, "''")}')`);
                return options;
            };

            const sql = () => {
                const source = sourceExpression();
                const entries = Object.entries(resolvedMappings()).filter(([, value]) => value);
                return `CREATE VISUAL ${datasetBaseName(draft.title || `${draft.type.toLowerCase()}_visual`)} AS ${draft.type} (\n`
                    + `    SOURCE = ${source}`
                    + (entries.length ? `,\n    MAPPINGS (${entries.map(([role, value]) => `${role} = ${value}`).join(', ')})` : '')
                    + (formatOptions().length ? `,\n    OPTIONS (${formatOptions().join(', ')})` : '')
                    + (draft.title ? `,\n    TITLE = '${String(draft.title).replace(/'/g, "''")}'` : '')
                    + '\n);';
            };

            const paint = () => api.render({
                lede: 'Drag a field onto a role, or click a role and pick one. The preview below runs against the '
                    + `sample from <strong>${escapeHtml(context.snapshot.source)}</strong>, so it is the real shape of your data.`,
                body: `
                    <div class="etlsql-studio-builder">
                        <div class="etlsql-studio-builder-types">
                            ${STUDIO_VISUAL_GROUPS.map(group => `<div class="etlsql-studio-builder-group">
                                <span>${escapeHtml(group.name)}</span>
                                <div>${group.types.map(type => `<button type="button" data-builder-type="${type}"
                                    class="${draft.type === type ? 'active' : ''}">${type}</button>`).join('')}</div>
                            </div>`).join('')}
                        </div>
                        <div class="etlsql-studio-builder-main">
                            <div class="etlsql-studio-builder-bind">
                                <div class="etlsql-studio-builder-fields">
                                    <span>Fields</span>
                                    ${columns.map(column => `<button type="button" class="etlsql-studio-builder-field"
                                        draggable="true" data-builder-field="${escapeHtml(column)}"
                                        data-field-kind="${guidedFieldKind(column)}">${escapeHtml(column)}</button>`).join('')}
                                </div>
                                <div class="etlsql-studio-builder-roles">
                                    <span>Roles</span>
                                    ${rolesForVisualType(draft.type).map(role => roleSlotMarkup(role, draft)).join('')
                                        || '<p class="etlsql-studio-guided-hint">This visual type takes no field bindings.</p>'}
                                </div>
                            </div>
                            <div class="etlsql-studio-builder-preview" data-builder-preview></div>
                        </div>
                    </div>
                    <label class="etlsql-studio-guided-field"><span>Title</span>
                        <input type="text" data-builder-title value="${escapeHtml(draft.title)}"
                            placeholder="${escapeHtml(`${draft.type} visual`)}"></label>
                    <div class="etlsql-studio-guided-row">
                        <label class="etlsql-studio-guided-field"><span>Value format</span>
                            <input type="text" data-builder-format list="etlsql-builder-formats"
                                value="${escapeHtml(draft.format.value)}" placeholder="1234.5 unformatted" spellcheck="false">
                        </label>
                        ${axisRole() ? `<label class="etlsql-studio-guided-field"><span>Axis label format</span>
                            <input type="text" data-builder-axis-format list="etlsql-builder-formats"
                                value="${escapeHtml(draft.format.axis)}" placeholder="Auto" spellcheck="false">
                        </label>` : ''}
                    </div>
                    <datalist id="etlsql-builder-formats">${STUDIO_FORMAT_PATTERNS.map(pattern =>
                        `<option value="${escapeHtml(pattern.pattern)}">${escapeHtml(pattern.label)}</option>`).join('')}</datalist>
                    <p class="etlsql-studio-guided-hint">Number and date patterns, as .NET writes them —
                        <code>N0</code>, <code>C2</code>, <code>P1</code>, <code>$#,##0.00</code>,
                        <code>MMM yyyy</code>. Everything else about how this looks — colours, grid lines,
                        data labels, axis bounds — is in <strong>Format</strong> on the selected tile, which
                        opens on the visual as soon as it is added.</p>`
                    + sqlPreviewMarkup(sql(),
                        `Adds one ${draft.type} visual to the page, bound to the fields in the roles above. `
                        + 'It is appended after the statements already in the script; nothing existing is changed.'),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add to canvas', primary: true,
                        disabled: missingRequiredRoles(previewVisual()).length > 0,
                        run: addVisual,
                    },
                ],
                wire: host => {
                    renderVisualSample(host.querySelector('[data-builder-preview]'), previewVisual(), previewSample());

                    host.querySelectorAll('[data-builder-type]').forEach(button => button.addEventListener('click', () => {
                        draft.type = button.dataset.builderType;
                        // Roles differ per type, so carry over only the ones the new type accepts and
                        // fill the rest — an author switching BAR to PIE should not land on a blank.
                        // A repeatable role is a numbered family, so it is matched by prefix; keeping
                        // TABLE's COLUMN1..n on a BAR left every column spoken for and no role bound.
                        const roles = rolesForVisualType(draft.type);
                        const exact = new Set(roles.filter(role => !role.repeatable).map(role => role.key));
                        const prefixes = roles.filter(role => role.repeatable).map(role => role.key.replace(/S$/, ''));
                        draft.mappings = Object.fromEntries(Object.entries(draft.mappings).filter(([role]) =>
                            exact.has(role) || prefixes.some(prefix => new RegExp(`^${prefix}\\d*$`, 'i').test(role))));
                        autoAssignRoles(draft, columns);
                        paint();
                    }));

                    host.querySelectorAll('[data-builder-field]').forEach(field => {
                        field.addEventListener('dragstart', event => {
                            event.dataTransfer.setData('text/plain', field.dataset.builderField);
                            event.dataTransfer.effectAllowed = 'copy';
                        });
                    });

                    host.querySelectorAll('[data-role-slot]').forEach(slot => {
                        const role = slot.dataset.roleSlot;
                        slot.addEventListener('dragover', event => {
                            event.preventDefault();
                            event.dataTransfer.dropEffect = 'copy';
                            slot.classList.add('is-over');
                        });
                        slot.addEventListener('dragleave', () => slot.classList.remove('is-over'));
                        slot.addEventListener('drop', event => {
                            event.preventDefault();
                            slot.classList.remove('is-over');
                            assignRole(role, event.dataTransfer.getData('text/plain'));
                        });
                    });

                    host.querySelectorAll('[data-role-select]').forEach(select => select.addEventListener('change', () =>
                        assignRole(select.dataset.roleSelect, select.value)));
                    host.querySelectorAll('[data-role-aggregate]').forEach(select => select.addEventListener('change', () => {
                        const role = select.dataset.roleAggregate;
                        const column = draft.mappings[role];
                        draft.aggregates[role] = select.value === 'NONE'
                            ? { aggregate: 'NONE', alias: '' }
                            : { aggregate: select.value, alias: defaultAggregateAlias(select.value, column) };
                        paint();
                    }));
                    host.querySelectorAll('[data-role-alias]').forEach(input => input.addEventListener('change', () => {
                        const role = input.dataset.roleAlias;
                        // Renaming the alias changes both the AS in the query and what the role maps to,
                        // which is the whole point of letting it be renamed.
                        draft.aggregates[role] = { ...draft.aggregates[role], alias: datasetBaseName(input.value) };
                        paint();
                    }));
                    host.querySelectorAll('[data-role-clear]').forEach(button => button.addEventListener('click', () =>
                        assignRole(button.dataset.roleClear, '')));
                    host.querySelectorAll('[data-role-add]').forEach(button => button.addEventListener('click', () => {
                        const next = nextRepeatableRole(draft, button.dataset.roleAdd);
                        assignRole(next, columns.find(column => !Object.values(draft.mappings).includes(column)) || columns[0]);
                    }));

                    host.querySelector('[data-builder-title]')?.addEventListener('input', event => { draft.title = event.target.value; });
                    // Repainting on change, not on input: the preview under these fields is the SQL
                    // about to be written, and redrawing it on every keystroke makes a half-typed
                    // pattern look like the decision.
                    host.querySelector('[data-builder-format]')?.addEventListener('change', event => {
                        draft.format.value = event.target.value;
                        paint();
                    });
                    host.querySelector('[data-builder-axis-format]')?.addEventListener('change', event => {
                        draft.format.axis = event.target.value;
                        paint();
                    });
                },
            });

            const assignRole = (role, column) => {
                if (!role) return;
                if (column) draft.mappings[role] = column;
                else delete draft.mappings[role];
                paint();
            };

            const addVisual = async () => {
                api.busy(true);
                const binding = visualSourceBinding();
                const measure = activeMeasure();
                const valueFormat = draft.format.value.trim();
                const axisFormat = axisRole() ? draft.format.axis.trim() : '';
                // An aggregated visual reads from a grouped SELECT, so it carries an inline source
                // rather than a bare dataset reference.
                const source = measure
                    ? { dataset: null, options: { inline_source: sourceExpression() } }
                    : binding;
                const mappings = resolvedMappings();
                const type = draft.type;
                const added = await mutate(`Add ${type} visual`, design => {
                    const page = design.pages[0];
                    page.visuals ||= [];
                    const name = uniqueVisualName(design, datasetBaseName(draft.title || `${type.toLowerCase()}_visual`));
                    const bottom = page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                    const wide = type === 'TABLE' || type === 'MATRIX';
                    page.visuals.push({
                        id: `studio_${Date.now().toString(36)}`,
                        name,
                        type,
                        gridCol: 1,
                        gridRow: bottom + 1,
                        gridColSpan: wide ? 12 : type === 'CARD' ? 3 : 6,
                        gridRowSpan: type === 'CARD' ? 2 : wide ? 6 : 4,
                        title: draft.title || null,
                        dataset: source.dataset,
                        mappings: { ...mappings },
                        options: {
                            ...source.options,
                            ...(valueFormat ? { FORMAT: valueFormat } : {}),
                        },
                        // The axis pattern rides on the visual's formatting, which is where the
                        // Format inspector reads and writes it — the builder starts that record
                        // rather than keeping a second one of its own.
                        ...(axisFormat ? { formatting: { xAxis: { FORMAT: axisFormat } } } : {}),
                    });
                    return name;
                });
                api.busy(false);
                if (added) {
                    feedback.notify(`Added ${type} visual ${added}. Format on the selected tile carries on from here.`,
                        { title: 'Visual added', tone: 'success' });
                    // The hand-off: the builder decides the shape of a visual once, and every later
                    // change belongs to the inspector that already edits every property. Selecting
                    // the new visual is what opens it, so the author lands on the controls that
                    // continue the job instead of reopening a wizard that would start a new one.
                    shell.selectVisual?.(added);
                }
                api.close(added);
            };

            paint();
        });
    }

    /** Human-readable form of an aggregate, for the role hint. */
    function aggregateExpressionLabel(aggregate, column) {
        return aggregate === 'COUNT_DISTINCT' ? `a distinct count of ${column}` : `${aggregate.toLowerCase()} of ${column}`;
    }

    function roleSlotMarkup(role, draft) {
        const columns = snapshotColumns(activeContext().snapshot).map(columnName);
        const options = column => `<option value="">—</option>${columns.map(item =>
            `<option ${item === column ? 'selected' : ''}>${escapeHtml(item)}</option>`).join('')}`;

        if (!role.repeatable) {
            const value = draft.mappings[role.key] || '';
            const setting = draft.aggregates?.[role.key] || { aggregate: 'NONE', alias: '' };
            const aggregated = role.measure && value && setting.aggregate !== 'NONE';
            const alias = setting.alias || (aggregated ? defaultAggregateAlias(setting.aggregate, value) : '');

            // Only a measure role offers an aggregate. Naming the result is part of the same decision:
            // the alias is what the query says AS, and what the role ends up mapped to.
            const aggregateControls = role.measure && value
                ? `<label class="etlsql-studio-role-aggregate"><span>Summarise as</span>
                        <select data-role-aggregate="${role.key}">${CHART_AGGREGATES.map(option =>
                            `<option value="${option.id}" ${setting.aggregate === option.id ? 'selected' : ''}>${escapeHtml(option.label)}</option>`).join('')}</select></label>`
                    + (aggregated
                        ? `<label class="etlsql-studio-role-aggregate"><span>Call it</span>
                            <input type="text" data-role-alias="${role.key}" value="${escapeHtml(alias)}" spellcheck="false"></label>`
                        : '')
                : '';

            return `<div class="etlsql-studio-role-slot${value ? ' is-bound' : ''}${role.required && !value ? ' is-required' : ''}" data-role-slot="${role.key}">
                <span>${escapeHtml(role.label)}${role.required ? ' *' : ''}</span>
                <div><select data-role-select="${role.key}">${options(value)}</select>
                ${value ? `<button type="button" data-role-clear="${role.key}" aria-label="Clear ${escapeHtml(role.label)}">&times;</button>` : ''}</div>
                ${aggregateControls}
                <small>${escapeHtml(aggregated
                    ? `Plots ${aggregateExpressionLabel(setting.aggregate, value)} per ${role.key === 'VALUE' ? 'group' : 'category'}`
                    : (role.hint || ''))}</small>
            </div>`;
        }

        // A repeatable role is a numbered family (COLUMN1, COLUMN2, …); each bound entry gets its own
        // slot and there is always one more to drop onto.
        const bound = Object.entries(draft.mappings)
            .filter(([key, value]) => value && key.toUpperCase().startsWith(role.key.replace(/S$/, '')))
            .sort((left, right) => Number(left[0].replace(/\D/g, '') || 0) - Number(right[0].replace(/\D/g, '') || 0));
        return `<div class="etlsql-studio-role-repeat">
            <span>${escapeHtml(role.label)}${role.required ? ' *' : ''}</span>
            ${bound.map(([key, value]) => `<div class="etlsql-studio-role-slot is-bound" data-role-slot="${escapeHtml(key)}">
                <div><select data-role-select="${escapeHtml(key)}">${options(value)}</select>
                <button type="button" data-role-clear="${escapeHtml(key)}" aria-label="Remove column">&times;</button></div>
            </div>`).join('')}
            <div class="etlsql-studio-role-slot is-empty" data-role-slot="${escapeHtml(nextRepeatableRole(draft, role.key))}">
                <span>Drop a field here</span>
                <button type="button" data-role-add="${role.key}">+ Add column</button>
            </div>
            <small>${escapeHtml(role.hint || '')}</small>
        </div>`;
    }

    function nextRepeatableRole(draft, roleKey) {
        const prefix = roleKey.replace(/S$/, '');
        let index = 1;
        while (draft.mappings[`${prefix}${index}`]) index++;
        return `${prefix}${index}`;
    }

    /** Fills unbound roles with the first column whose kind suits them, so the preview is never blank. */
    function autoAssignRoles(draft, columns) {
        const used = new Set(Object.values(draft.mappings).filter(Boolean));
        // Reusing a column across two roles is legitimate (count by the same field you group by), so
        // running out of unused columns must still bind something rather than leave the role empty.
        const pick = kind => columns.find(column => !used.has(column) && (kind === 'any' || guidedFieldKind(column) === kind))
            || columns.find(column => !used.has(column))
            || columns.find(column => kind === 'any' || guidedFieldKind(column) === kind)
            || columns[0];

        for (const role of rolesForVisualType(draft.type)) {
            if (role.repeatable) {
                if (!Object.keys(draft.mappings).some(key => key.toUpperCase().startsWith(role.key.replace(/S$/, '')))) {
                    columns.slice(0, 6).forEach((column, index) => { draft.mappings[`${role.key.replace(/S$/, '')}${index + 1}`] = column; });
                }
                continue;
            }
            if (draft.mappings[role.key] || !role.required) continue;
            const column = pick(role.kind);
            if (!column) continue;
            draft.mappings[role.key] = column;
            used.add(column);
        }
    }

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
        const draftFor = parameter => ({
            original: parameter?.name ?? null,
            name: parameterName(parameter?.name ?? 'region'),
            type: parameter?.dataType ?? 'VARCHAR',
            initial: parameter?.initialValue ?? "'All'",
            prompt: parameter?.isInput ?? true,
            required: parameter?.isRequired ?? false,
            sensitive: parameter?.isSensitive ?? false,
        });

        const declarationSql = draft => {
            const initial = draft.initial.trim() ? ` = ${draft.initial.trim()}` : '';
            const flags = [
                draft.sensitive ? ' PASSWORD' : '',
                draft.prompt ? ' INPUT' : '',
                draft.required ? ' REQUIRED' : '',
            ].join('');
            return `DECLARE @${parameterName(draft.name)} ${draft.type.trim() || 'VARCHAR'}${initial}${flags};`;
        };

        await studioDialog({ kicker: 'Step 2 · Define parameters', title: 'Report parameters', wide: true }, api => {
            let parameters = null;

            const load = async () => {
                try {
                    const parsed = await request(routes.parse, { body: { script: shell.getScriptText() } });
                    parameters = parsed.designState?.parameters || [];
                } catch {
                    parameters = [];
                }
                paintList();
            };

            const flagLabels = parameter => [
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
                    host.querySelectorAll('[data-edit-parameter]').forEach(button => button.addEventListener('click', () =>
                        paintForm(draftFor(parameters[Number(button.dataset.editParameter)]))));
                    host.querySelectorAll('[data-delete-parameter]').forEach(button => button.addEventListener('click', () =>
                        paintDelete(parameters[Number(button.dataset.deleteParameter)])));
                },
            });

            const paintForm = draft => {
                const isEdit = Boolean(draft.original);
                const collides = (parameters || []).some(parameter =>
                    parameter.name.toLowerCase() === `@${parameterName(draft.name)}`.toLowerCase()
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
                            <datalist id="etlsql-parameter-types">${STUDIO_PARAMETER_TYPES.map(type =>
                                `<option value="${type}"></option>`).join('')}</datalist></label>
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
                        + sqlPreviewMarkup(declarationSql(draft),
                            isEdit
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
                        const bind = (selector, read) => host.querySelector(selector)?.addEventListener('change', event => {
                            read(event.target);
                            paintForm(draft);
                        });
                        bind('[data-parameter-name]', input => { draft.name = input.value; });
                        bind('[data-parameter-type]', input => { draft.type = input.value; });
                        bind('[data-parameter-initial]', input => { draft.initial = input.value; });
                        bind('[data-parameter-prompt]', input => { draft.prompt = input.checked; });
                        bind('[data-parameter-required]', input => { draft.required = input.checked; });
                        bind('[data-parameter-sensitive]', input => { draft.sensitive = input.checked; });
                    },
                });
            };

            const apply = async draft => {
                const name = `@${parameterName(draft.name)}`;
                api.busy(true);
                const written = await mutate(draft.original ? `Edit parameter ${draft.original}` : `Add parameter ${name}`, design => {
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
                        ? design.parameters.findIndex(parameter => parameter.name === draft.original)
                        : -1;
                    if (at >= 0) design.parameters[at] = next;
                    else design.parameters.push(next);
                    return name;
                });
                api.busy(false);
                if (written) {
                    feedback.notify(`${written} is declared. Reference it in a dataset query to filter on it.`,
                        { title: draft.original ? 'Parameter saved' : 'Parameter added', tone: 'success' });
                }
                await load();
            };

            const paintDelete = parameter => api.render({
                lede: `Delete <strong>${escapeHtml(parameter.name)}</strong>? Anything still referencing it — a dataset `
                    + 'query, a slicer action — keeps that reference and will not resolve, so check those first.',
                body: sqlPreviewMarkup(
                    `DECLARE ${parameter.name} ${parameter.dataType}${parameter.initialValue ? ` = ${parameter.initialValue}` : ''};`,
                    `Deletes this line from the script. Nothing else is rewritten, so any query still naming `
                    + `${parameter.name} keeps that reference and stops resolving.`,
                    'Removes this declaration'),
                actions: [
                    { id: 'back', label: 'Keep it', run: paintList },
                    { id: 'delete', label: 'Delete', primary: true, run: () => remove(parameter) },
                ],
            });

            const remove = async parameter => {
                api.busy(true);
                const removed = await mutate(`Delete parameter ${parameter.name}`, design => {
                    design.parameters = (design.parameters || []).filter(item => item.name !== parameter.name);
                    return parameter.name;
                });
                api.busy(false);
                if (removed) feedback.notify(`${removed} was removed.`, { title: 'Parameter deleted', tone: 'success' });
                await load();
            };

            paintList();
            load();
        });
    }
    async function runDetailsStep() {
        if (!await requireDataSample('Step 3 · Groups + details')) return;
        const columns = guidedColumnNames();
        const numeric = guidedNumericColumns();
        const draft = {
            group: columns[0] || '',
            measure: numeric[0] || columns[0] || '',
            includeMatrix: true,
            detail: columns.slice(0, 8),
        };
        await studioDialog({ kicker: 'Step 3 · Groups + details', title: 'Add group and detail bands', wide: true }, api => {
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
                            <select data-details-group>${columns.map(column =>
                                `<option ${draft.group === column ? 'selected' : ''}>${escapeHtml(column)}</option>`).join('')}</select></label>
                        <label class="etlsql-studio-guided-field"><span>Summarise</span>
                            <select data-details-measure>${columns.map(column =>
                                `<option ${draft.measure === column ? 'selected' : ''}>${escapeHtml(column)}</option>`).join('')}</select></label>
                    </div>` : ''}
                    <div class="etlsql-studio-guided-field"><span>Detail columns</span>
                        <div class="etlsql-studio-check-grid">${columns.map(column => `
                            <label><input type="checkbox" data-detail-column="${escapeHtml(column)}"
                                ${draft.detail.includes(column) ? 'checked' : ''}>${escapeHtml(column)}</label>`).join('')}</div></div>`
                    + mutationExplanationMarkup(
                        `Appends ${draft.includeMatrix ? 'a matrix summarising ' + draft.measure + ' by ' + draft.group + ' and ' : ''}`
                        + `a detail table printing ${draft.detail.length} column${draft.detail.length === 1 ? '' : 's'} `
                        + 'below whatever the page already holds. Existing visuals are not moved or rewritten.')
                    + (draft.detail.length ? '' : guidedNoteMarkup('Pick at least one detail column, or the table has nothing to print.', 'warning')),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add bands', primary: true, disabled: !draft.detail.length, run: async () => {
                            api.busy(true);
                            const binding = visualSourceBinding();
                            const added = await mutate('Add group and detail bands', design => {
                                const page = design.pages[0];
                                page.mode = 'Paginated';
                                page.visuals ||= [];
                                const bottom = () => page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                                if (draft.includeMatrix) {
                                    page.visuals.push({
                                        id: `studio_group_${Date.now().toString(36)}`,
                                        name: uniqueVisualName(design, 'group_summary'),
                                        type: 'MATRIX', gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 4,
                                        title: `${draft.group} summary`,
                                        dataset: binding.dataset,
                                        mappings: { ROW: draft.group, VALUE: draft.measure },
                                        options: { ...binding.options, AGGREGATE: 'SUM' },
                                    });
                                }
                                page.visuals.push({
                                    id: `studio_detail_${Date.now().toString(36)}`,
                                    name: uniqueVisualName(design, 'detail_rows'),
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
                            if (added) feedback.notify('Detail rows added. Step 4 puts a total under them.', { title: 'Bands added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-details-matrix]').addEventListener('change', event => { draft.includeMatrix = event.target.checked; paint(); });
                    host.querySelector('[data-details-group]')?.addEventListener('change', event => { draft.group = event.target.value; });
                    host.querySelector('[data-details-measure]')?.addEventListener('change', event => { draft.measure = event.target.value; });
                    host.querySelectorAll('[data-detail-column]').forEach(box => box.addEventListener('change', () => {
                        const column = box.dataset.detailColumn;
                        draft.detail = box.checked
                            ? [...draft.detail, column]
                            : draft.detail.filter(item => item !== column);
                        const add = dialog.box.querySelector('[data-dialog-action="add"]');
                        if (add) add.disabled = !draft.detail.length;
                    }));
                },
            });
            paint();
        });
    }

    async function runTotalsStep() {
        const designState = shell.designerState();
        const tables = (designState?.pages || []).flatMap(page => page.visuals || []).filter(visual => visual.type === 'TABLE');
        if (!tables.length) {
            await guidedBlocker({
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
        await studioDialog({ kicker: 'Step 4 · Add totals', title: 'Add a grand total' }, api => {
            const paint = () => api.render({
                lede: 'A <strong>grand total</strong> appends one footer row to a detail table, aggregating every numeric column it prints.',
                body: `
                    <label class="etlsql-studio-guided-field"><span>Detail table</span>
                        <select data-total-target>${tables.map(table =>
                            `<option value="${escapeHtml(table.name)}" ${draft.target === table.name ? 'selected' : ''}>${escapeHtml(table.title || table.name)}</option>`).join('')}</select></label>
                    <label class="etlsql-studio-guided-field"><span>Aggregate</span>
                        <select data-total-aggregate>${STUDIO_TOTAL_AGGREGATES.map(aggregate =>
                            `<option ${draft.aggregate === aggregate ? 'selected' : ''}>${aggregate}</option>`).join('')}</select></label>`
                    + sqlPreviewMarkup(`OPTIONS (GRAND_TOTAL = ${draft.aggregate})`,
                        `Adds one footer row to ${draft.target} showing the ${draft.aggregate} of each numeric column it prints. `
                        + 'The detail rows above it are unchanged.',
                        'Adds this option to the table'),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add total', primary: true, run: async () => {
                            api.busy(true);
                            const added = await mutate('Add report totals', design => {
                                const table = (design.pages || []).flatMap(page => page.visuals || [])
                                    .find(visual => visual.name === draft.target);
                                if (!table) throw new Error(`The detail table ${draft.target} is no longer in the script.`);
                                table.options ||= {};
                                table.options.GRAND_TOTAL = draft.aggregate;
                                return true;
                            });
                            api.busy(false);
                            if (added) feedback.notify(`${draft.target} now prints a ${draft.aggregate} total row.`, { title: 'Total added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-total-target]').addEventListener('change', event => { draft.target = event.target.value; paint(); });
                    host.querySelector('[data-total-aggregate]').addEventListener('change', event => { draft.aggregate = event.target.value; paint(); });
                },
            });
            paint();
        });
    }

    async function runFurnitureStep() {
        const doc = getActiveDocument();
        const draft = {
            header: doc?.name?.replace(/\.rptsql$/i, '').replace(/[_-]+/g, ' ') || 'Report',
            footer: 'Page {{PAGE}} of {{PAGES}}',
            addHeader: true,
            addFooter: true,
            breakAfterDetails: false,
        };
        await studioDialog({ kicker: 'Step 5 · Header + footer', title: 'Add page furniture' }, api => {
            const paint = () => api.render({
                lede: 'Page <strong>furniture</strong> is the text that frames every printed page. '
                    + 'These are TEXT bands with a <code>KEEP_TOGETHER</code> print rule, so they never split across a page boundary.',
                body: `
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-furniture-header ${draft.addHeader ? 'checked' : ''}> Add a page header</label>
                    ${draft.addHeader ? `<label class="etlsql-studio-guided-field"><span>Header text</span>
                        <input type="text" data-header-text value="${escapeHtml(draft.header)}"></label>` : ''}
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-furniture-footer ${draft.addFooter ? 'checked' : ''}> Add a page footer</label>
                    ${draft.addFooter ? `<label class="etlsql-studio-guided-field"><span>Footer text</span>
                        <input type="text" data-footer-text value="${escapeHtml(draft.footer)}"></label>` : ''}
                    <label class="etlsql-studio-guided-check">
                        <input type="checkbox" data-furniture-break ${draft.breakAfterDetails ? 'checked' : ''}>
                        Start a new page after the detail table</label>`
                    + mutationExplanationMarkup(
                        `Adds ${[draft.addHeader ? 'a header band' : null, draft.addFooter ? 'a footer band' : null]
                            .filter(Boolean).join(' and ') || 'nothing yet'} to the page as TEXT visuals`
                        + `${draft.breakAfterDetails ? ', and sets the detail table to start a new page after it' : ''}. `
                        + 'The bands print on every physical page; the data visuals are untouched.')
                    + (draft.addHeader || draft.addFooter ? '' : guidedNoteMarkup('Nothing selected — pick a header, a footer, or both.', 'warning')),
                actions: [
                    { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                    {
                        id: 'add', label: 'Add furniture', primary: true, disabled: !draft.addHeader && !draft.addFooter, run: async () => {
                            api.busy(true);
                            const added = await mutate('Add page header and footer', design => {
                                const page = design.pages[0];
                                page.mode = 'Paginated';
                                page.visuals ||= [];
                                const bottom = () => page.visuals.reduce((max, visual) => Math.max(max, visual.gridRow + visual.gridRowSpan - 1), 0);
                                // A TEXT band carries its content in DEFAULT and reads no data, so it
                                // gets no SOURCE — one with a source and no text prints nothing.
                                const band = (slug, title, text) => ({
                                    id: `studio_${slug}_${Date.now().toString(36)}`,
                                    name: uniqueVisualName(design, `page_${slug}`),
                                    type: 'TEXT', gridCol: 1, gridRow: bottom() + 1, gridColSpan: 12, gridRowSpan: 2,
                                    title,
                                    dataset: null,
                                    mappings: {},
                                    options: {
                                        text_default: `'${String(text).replace(/'/g, "''")}'`,
                                        print_layout: 'PRINT_LAYOUT (KEEP_TOGETHER = ON)',
                                    },
                                });
                                if (draft.addHeader) page.visuals.push(band('header', 'Page header', draft.header));
                                if (draft.addFooter) page.visuals.push(band('footer', 'Page footer', draft.footer));
                                if (draft.breakAfterDetails) {
                                    const table = page.visuals.find(visual => visual.type === 'TABLE');
                                    if (table) {
                                        table.options ||= {};
                                        table.options.print_layout = 'PRINT_LAYOUT (PAGE_BREAK_AFTER = ON, KEEP_TOGETHER = ON)';
                                    }
                                }
                                return true;
                            });
                            api.busy(false);
                            if (added) feedback.notify('Page bands added.', { title: 'Furniture added', tone: 'success' });
                            api.close(added);
                        },
                    },
                ],
                wire: host => {
                    host.querySelector('[data-furniture-header]').addEventListener('change', event => { draft.addHeader = event.target.checked; paint(); });
                    host.querySelector('[data-furniture-footer]').addEventListener('change', event => { draft.addFooter = event.target.checked; paint(); });
                    host.querySelector('[data-furniture-break]').addEventListener('change', event => { draft.breakAfterDetails = event.target.checked; });
                    host.querySelector('[data-header-text]')?.addEventListener('input', event => { draft.header = event.target.value; });
                    host.querySelector('[data-footer-text]')?.addEventListener('input', event => { draft.footer = event.target.value; });
                },
            });
            paint();
        });
    }

    /**
     * Runs the report and shows the pages it would print.
     *
     * <p>The canvas draws a page-width sheet, which is a hint about paper and not an answer to "how
     * many pages, and what lands on each" — the question a paginated report is written to answer.
     * The engine already compiles that breakdown for the export; this asks for the same manifest and
     * reads it back, so what the dialog lists is what the PDF will contain rather than a second
     * guess at it.</p>
     */
    async function runPreviewStep() {
        const designState = shell.designerState();
        const visuals = (designState?.pages || []).flatMap(page => page.visuals || []);
        if (!visuals.length) {
            await guidedBlocker({
                kicker: 'Step 7 · Preview pagination',
                title: 'There is nothing to paginate yet',
                lede: 'A preview runs the report and lays its visuals onto physical pages. This report has no visuals, so every page would be blank.',
                remedyLabel: 'Add detail bands',
                remedy: () => runDetailsStep(),
            });
            return;
        }

        const parameters = await promptForReportParameters('Step 7 · Preview pagination');
        if (parameters === null) {
            feedback.notify('Preview cancelled, so the report was not run.', { title: 'Preview', tone: 'info' });
            return;
        }

        shell.setProjection('split');
        feedback.notify('Running the report. Rows and messages appear below the script.',
            { title: 'Preview running', tone: 'info' });
        // The answers travel with the run, so what is previewed is the report as answered rather
        // than the defaults the script happens to declare.
        shell.runReport(Object.keys(parameters).length ? parameters : null);

        await showPaginationBreakdown(parameters);
    }

    /** One page of the compiled breakdown, described in the author's terms. */
    function physicalPageSummary(page) {
        const placements = page.visuals || [];
        if (!placements.length) return 'nothing — this page prints empty';
        return placements.map(placement => {
            const name = placement.visual?.name || 'visual';
            const rows = placement.startRowIndex != null && placement.endRowIndex != null
                ? ` rows ${placement.startRowIndex + 1}–${placement.endRowIndex + 1}`
                : '';
            return `${name}${rows}`;
        }).join(', ');
    }

    async function showPaginationBreakdown(parameters) {
        let manifest = null;
        try {
            manifest = await request(routes.preview, {
                body: {
                    script: shell.getScriptText(),
                    parameters,
                    // Every page is run: the point of this step is what the finished document holds.
                    runEveryPage: true,
                },
                fallbackError: 'The pagination could not be compiled.',
            });
        } catch (error) {
            feedback.notify(error?.message || 'The pagination could not be compiled.',
                { title: 'Preview failed', tone: 'error' });
            return;
        }

        const pages = (manifest?.pages || []).filter(page =>
            String(page.mode || '').toUpperCase() === 'PAGINATED' || page.printLayout);

        await studioDialog({ kicker: 'Step 7 · Preview pagination', title: 'Pages this report prints' }, api => api.render({
            lede: 'This is the breakdown the export uses. A detail table that does not fit is split, and the '
                + 'row numbers say where each page continues from.',
            body: pages.length
                ? pages.map(page => {
                    const physical = page.physicalPages || [];
                    return `<div class="etlsql-studio-guided-group">
                        <span>${escapeHtml(page.name || 'Page')} · ${physical.length || 1} `
                        + `physical page${(physical.length || 1) === 1 ? '' : 's'}`
                        + `${page.printLayout?.pageSize ? ` · ${escapeHtml(page.printLayout.pageSize)}` : ''}`
                        + `${page.printLayout?.orientation ? ` ${escapeHtml(String(page.printLayout.orientation).toLowerCase())}` : ''}</span>
                        <ol class="etlsql-studio-guided-list">${physical.length
                            ? physical.map(physicalPage =>
                                `<li><strong>Page ${physicalPage.pageNumber}</strong> — ${escapeHtml(physicalPageSummary(physicalPage))}</li>`).join('')
                            : '<li>Not compiled — this page declares no print layout.</li>'}</ol>
                    </div>`;
                }).join('')
                : guidedNoteMarkup('No page in this report is paginated, so there is nothing to lay onto sheets. '
                    + 'A dashboard page is one scrolling canvas.', 'info'),
            actions: [
                { id: 'close', label: 'Close', run: () => api.close(null) },
                { id: 'export', label: 'Export PDF', primary: true, run: () => { api.close('export'); runExportStep(); } },
            ],
        }));
    }

    /**
     * Asks for the report's INPUT parameters before it runs, and returns the answers.
     *
     * <p>A prompt is what `INPUT` means: the reader is asked, and their answer replaces the
     * declaration's initial value — the same precedence `--var` has at the command line. Running a
     * report that asks for a date range and silently using the defaults previews a report nobody
     * requested, and exports one nobody can explain.</p>
     *
     * @returns `{}` when the report prompts for nothing, a map of answers, or null if the author
     *          cancelled — which must abort the run rather than fall back to defaults.
     */
    async function promptForReportParameters(intent) {
        let parameters = [];
        try {
            const parsed = await request(routes.parse, { body: { script: shell.getScriptText() } });
            parameters = (parsed.designState?.parameters || []).filter(parameter => parameter.isInput);
        } catch {
            // A script that cannot be parsed has no prompts to ask about; the run itself will
            // report the syntax error, which is the more useful message.
            return {};
        }
        if (!parameters.length) return {};

        const answers = Object.fromEntries(parameters.map(parameter =>
            [parameter.name, unquoteParameterValue(parameter.initialValue)]));

        const confirmed = await studioDialog(
            { kicker: intent, title: 'Answer the report’s prompts' },
            api => {
                const paint = () => {
                    const missing = parameters.filter(parameter =>
                        parameter.isRequired && !String(answers[parameter.name] || '').trim());
                    api.render({
                        lede: 'This report declares <code>INPUT</code> parameters. Your answers are used for this '
                            + 'run only — the script keeps the defaults it declares.',
                        body: parameters.map(parameter => `
                            <label class="etlsql-studio-guided-field">
                                <span>${escapeHtml(parameter.name)}${parameter.isRequired ? ' *' : ''}
                                    <small>${escapeHtml(parameter.dataType || '')}</small></span>
                                <input type="${parameter.isSensitive ? 'password' : 'text'}"
                                    data-parameter-answer="${escapeHtml(parameter.name)}"
                                    value="${escapeHtml(answers[parameter.name] || '')}" spellcheck="false">
                            </label>`).join('')
                            + (missing.length
                                ? guidedNoteMarkup(`${missing.map(parameter => parameter.name).join(', ')} `
                                    + `${missing.length === 1 ? 'is' : 'are'} required, so the report cannot run without `
                                    + `${missing.length === 1 ? 'a value' : 'values'}.`, 'warning')
                                : ''),
                        actions: [
                            { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                            {
                                id: 'accept', label: 'Continue', primary: true, disabled: missing.length > 0,
                                run: () => api.close(true),
                            },
                        ],
                        wire: host => host.querySelectorAll('[data-parameter-answer]').forEach(input =>
                            input.addEventListener('input', () => {
                                const name = input.dataset.parameterAnswer;
                                answers[name] = input.value;
                                const blocked = parameters.some(parameter =>
                                    parameter.isRequired && !String(answers[parameter.name] || '').trim());
                                // Repaint only when the form crosses between "can run" and "cannot".
                                // Repainting on every keystroke would rebuild the field under the
                                // cursor, which is how a form starts eating characters.
                                if (blocked === (missing.length > 0)) return;
                                paint();
                                const refreshed = dialog.box.querySelector(`[data-parameter-answer="${name}"]`);
                                if (refreshed) {
                                    refreshed.focus();
                                    refreshed.setSelectionRange(refreshed.value.length, refreshed.value.length);
                                }
                            })),
                    });
                };
                paint();
            });

        return confirmed ? answers : null;
    }

    /** A declaration's initial value is script text — `'North'` — and a prompt wants the value. */
    function unquoteParameterValue(value) {
        const text = String(value ?? '').trim();
        return /^'(?:[^']|'')*'$/.test(text) ? text.slice(1, -1).replace(/''/g, "'") : text;
    }

    /**
     * Exports the report in the buffer as a PDF and hands the file to the reader.
     *
     * The step used to be a page of instructions: it named the PDF export and told the author to go
     * and find it, which for a report that only exists in an unsaved buffer meant there was nowhere
     * to go. The host renders the same manifest the preview builds, so what lands in the file is the
     * pagination the author has been looking at.
     */
    async function exportReportPdf() {
        const doc = getActiveDocument();
        const script = shell.getScriptText();
        if (!script.trim()) {
            feedback.notify('There is nothing to export — the script is empty.', { title: 'Export', tone: 'warning' });
            return false;
        }

        const parameters = await promptForReportParameters('Step 8 · Export');
        if (parameters === null) {
            feedback.notify('Export cancelled, so nothing was written.', { title: 'Export', tone: 'info' });
            return false;
        }

        try {
            const blob = await request(routes.previewPdf, {
                body: { script, page: null, parameters },
                accept: 'application/pdf',
                fallbackError: 'The report could not be exported.',
            });
            const name = String(doc?.name || 'report').replace(/\.[^.]+$/, '') || 'report';
            downloadBlob(blob, `${name}.pdf`);
            feedback.notify(`${name}.pdf was exported with the page setup you configured.`,
                { title: 'Export complete', tone: 'success' });
            return true;
        } catch (error) {
            feedback.notify(error?.message || 'The report could not be exported.',
                { title: 'Export failed', tone: 'error' });
            return false;
        }
    }

    /** Saves bytes as a file. A host that blocks downloads says so rather than doing nothing. */
    function downloadBlob(blob, filename) {
        const url = URL.createObjectURL(blob);
        try {
            const link = document.createElement('a');
            link.href = url;
            link.download = filename;
            document.body.appendChild(link);
            link.click();
            link.remove();
        } finally {
            // Revoked on the next turn: revoking synchronously can cancel the download in progress.
            setTimeout(() => URL.revokeObjectURL(url), 0);
        }
    }

    async function runExportStep() {
        await studioDialog({ kicker: 'Step 8 · Export', title: 'Export this report' }, api => api.render({
            lede: 'A paginated report exports two different things. <strong>PDF</strong> keeps the physical pages, margins, and breaks you configured. '
                + '<strong>CSV and Excel</strong> export the result rows only, with no page layout.',
            body: `<ul class="etlsql-studio-guided-list">
                    <li><strong>PDF</strong> — exported here, from the report as it stands in the editor.</li>
                    <li><strong>CSV / Excel</strong> — run the report, then use the export buttons on the Results pane below the script.</li>
                </ul>`
                + guidedNoteMarkup('Exporting runs the report again against live data, so a parameter prompt applies at export time too.', 'info'),
            actions: [
                { id: 'close', label: 'Close', run: () => api.close(null) },
                { id: 'preview', label: 'Preview pagination', run: () => { api.close('preview'); runPreviewStep(); } },
                {
                    id: 'export', label: 'Export PDF', primary: true, run: async () => {
                        api.busy(true);
                        const exported = await exportReportPdf();
                        api.busy(false);
                        if (exported) api.close('export');
                    },
                },
            ],
        }));
    }

    // --- Dashboard steps --------------------------------------------------------------------------

    async function runVisualsStep() {
        if (!await requireDataSample('Step 2 · Visuals')) return;
        shell.setActivity('palette');
        await studioDialog({ kicker: 'Step 2 · Visuals', title: 'Add visuals to the canvas' }, api => api.render({
            lede: `The Visual Components panel is now open on the left, listing every visual type this report can use. `
                + `They all read from <strong>${escapeHtml(activeContext().snapshot.source)}</strong>.`,
            body: `<ul class="etlsql-studio-guided-list">
                    <li>Click a type to drop it on the canvas, or drag it where you want it.</li>
                    <li>Select a tile, then use the Fields list to assign columns to its chart roles.</li>
                    <li>Every tile you add is written to the script as a <code>CREATE VISUAL</code> statement.</li>
                </ul>`,
            actions: [
                { id: 'close', label: 'Got it', run: () => api.close(null) },
                { id: 'build', label: 'Build a visual', primary: true, run: () => { api.close('build'); openChartBuilder({ kicker: 'Step 2 · Visuals' }); } },
            ],
        }));
    }

    async function runCrossFilterStep() {
        if (!await requireDataSample('Step 3 · Cross-filters')) return;
        shell.setActivity('filters');
        await studioDialog({ kicker: 'Step 3 · Cross-filters', title: 'Filter across visuals' }, api => api.render({
            lede: 'A <strong>filter</strong> narrows the rows every visual sees. Promoting one to a viewer control turns it into a slicer '
                + 'the reader can change, backed by a report parameter.',
            body: `<ul class="etlsql-studio-guided-list">
                    <li>Drag a field from the Fields list into the Filters lane on the left.</li>
                    <li>Choose <em>Dataset global</em> to filter every visual, or <em>Selected visual</em> for just one.</li>
                    <li>Use <em>Promote to viewer control</em> to give the reader a slicer for that field.</li>
                </ul>`,
            actions: [{ id: 'close', label: 'Got it', primary: true, run: () => api.close(null) }],
        }));
    }
    /**
     * The fields a task kind is authored with, beyond its label.
     *
     * Only the execution kind gets the query workbench — its content is SQL the author runs, and the
     * workbench is why it was extracted as a standalone component. For the others a handful of
     * values is the whole task, and a workbench for a file path would be theatre.
     */
    const PIPELINE_TASK_FIELDS = {
        fileoperation: [
            { name: 'source', label: 'Copy from', placeholder: 'C:\\data\\orders.csv' },
            { name: 'target', label: 'Copy to', placeholder: 'C:\\data\\archive\\orders.csv' },
        ],
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
        // A parallel block and a transaction scope have nothing to fill in: they are named, and then
        // filled by dragging tasks into them.
        parallel: [],
        transaction: [],
        foreach: [
            { name: 'variable', label: 'Item variable', placeholder: '@row', mono: true },
            { name: 'collection', label: 'Iterates over', placeholder: '#orders', mono: true },
        ],
    };

    /** Kinds that run against a connection the script declares, and so need one to exist first. */
    const PIPELINE_KINDS_NEEDING_CONNECTION = new Set(['execution', 'notification']);

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
     */
    async function openPipelineTaskEditor({
        kind = 'execution',
        task = null,
        connections = [],
        suggestedId = 'task_1',
    } = {}) {
        const editing = Boolean(task);
        const taskKind = String(task?.kind || kind || 'execution').toLowerCase();
        const aliases = (connections || []).map(connection => connection?.name).filter(Boolean);
        const needsConnection = PIPELINE_KINDS_NEEDING_CONNECTION.has(taskKind);

        // A task that runs against a connection cannot be written before one is declared, and a
        // free-text alias would let the author name one the script does not declare — which previews
        // fine here and fails for every other reader. Resume in the editor rather than dropping them
        // back on the canvas: making them re-open it is exactly the dead end the dataset wizard fixed.
        if (needsConnection && !aliases.length) {
            let created = null;
            const took = await guidedBlocker({
                kicker: 'Pipeline task',
                title: 'This script declares no connections yet',
                lede: 'This task runs against a connection the script declares. Add one and this editor '
                    + 'picks up where it left off.',
                remedyLabel: 'Create a connection',
                remedy: () => new Promise(resolve => shell.openConnectionWizard({
                    onDone: alias => { created = alias || null; resolve(); },
                })),
            });
            if (!took || !created) return null;
            return openPipelineTaskEditor({ kind, task, connections: [{ name: created }], suggestedId });
        }

        const fields = PIPELINE_TASK_FIELDS[taskKind] ?? [];
        const draft = {
            id: task?.id || suggestedId,
            connection: aliases.includes(task?.connection) ? task.connection : aliases[0] || '',
            body: task?.body || '',
            error: null,
        };
        // Prefilled from the task when there is one, so Apply on an existing loop repoints it rather
        // than clearing the header it was opened to show.
        for (const field of fields) draft[field.name] = String(task?.[field.name] ?? '');

        let workbench = null;
        return studioDialog(
            {
                kicker: 'Pipeline task',
                title: editing ? `Edit ${task.id}` : `New ${taskKindLabel(taskKind).toLowerCase()} task`,
                wide: taskKind === 'execution',
            },
            api => {
                const readFields = host => {
                    for (const field of fields) {
                        draft[field.name] = host.querySelector(`[data-task-field="${field.name}"]`)?.value ?? draft[field.name];
                    }
                };

                const save = () => {
                    const host = dialog.box;
                    readFields(host);
                    draft.body = taskKind === 'execution' ? (workbench?.getValue?.() ?? draft.body) : draft.body;

                    const id = host.querySelector('[data-task-id]').value.trim();
                    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(id)) {
                        draft.id = id;
                        draft.error = `"${id}" is not a usable label. Use letters, digits, and underscores, starting with a letter.`;
                        return repaint();
                    }
                    if (taskKind === 'execution' && !draft.body.trim()) {
                        draft.error = 'Write the SQL this task runs before adding it.';
                        return repaint();
                    }
                    const blank = fields.find(field => !String(draft[field.name] || '').trim());
                    if (blank) {
                        draft.error = `${blank.label} is needed before this task can be written.`;
                        return repaint();
                    }

                    const intent = { id, kind: taskKind };
                    if (needsConnection) intent.connection = draft.connection;
                    if (taskKind === 'execution') intent.body = draft.body;
                    for (const field of fields) intent[field.name] = draft[field.name];
                    return api.close(intent);
                };

                const paint = () => api.render({
                    lede: `This task becomes a labelled statement in the script. The label is what the canvas `
                        + `tracks it by, so it survives a hand edit.`,
                    body: (draft.error ? guidedNoteMarkup(draft.error, 'error') : '')
                        + `<div class="etlsql-studio-pipeline-fields">
                            <label>Label
                                <input type="text" data-task-id value="${escapeHtml(draft.id)}" spellcheck="false">
                            </label>
                            ${needsConnection ? `<label>Connection
                                <select data-task-connection>${aliases.map(alias =>
                                    `<option${alias === draft.connection ? ' selected' : ''}>${escapeHtml(alias)}</option>`).join('')}</select>
                            </label>` : ''}
                            ${fields.map(field => `<label>${escapeHtml(field.label)}
                                <input type="text" data-task-field="${escapeHtml(field.name)}"
                                    value="${escapeHtml(draft[field.name] || '')}"
                                    placeholder="${escapeHtml(field.placeholder || '')}"
                                    ${field.mono ? 'spellcheck="false"' : ''}>
                            </label>`).join('')}
                        </div>`
                        + (taskKind === 'execution'
                            ? `<div class="etlsql-studio-workbench" data-task-workbench></div>`
                                + guidedNoteMarkup([
                                    'Run executes this block against ',
                                    { code: draft.connection },
                                    ' behind the connection declarations the script already makes, so an alias '
                                    + 'resolves exactly as it will at run time.',
                                ], 'info')
                            : '')
                        + mutationExplanationMarkup(editing
                            ? `Rewrites ${task.id} in place. Only that statement changes: hand edits elsewhere, and `
                              + 'the tasks that wait for this one, are left as they are.'
                            : `Adds one ${taskKindLabel(taskKind).toLowerCase()} task to the end of the pipeline, under the `
                              + 'label above. Nothing already in the script is moved or rewritten, and nothing runs until '
                              + 'you run it.'),
                    actions: [
                        { id: 'cancel', label: 'Cancel', run: () => api.close(null) },
                        { id: 'save', label: editing ? 'Apply' : 'Add task', primary: true, run: save },
                    ],
                    wire: async host => {
                        host.querySelector('[data-task-id]').addEventListener('input', event => { draft.id = event.target.value; });
                        host.querySelector('[data-task-connection]')?.addEventListener('change', event => {
                            // The workbench binds its run and its preamble to one alias, so repointing
                            // rebuilds it rather than leaving it running against the previous one.
                            readFields(host);
                            draft.body = workbench?.getValue?.() ?? draft.body;
                            draft.connection = event.target.value;
                            repaint();
                        });
                        if (taskKind !== 'execution') return;

                        workbench = await createQueryWorkbench(host.querySelector('[data-task-workbench]'), {
                            connection: draft.connection,
                            routes,
                            request,
                            editorTransport,
                            documentUri: () => getActiveDocument()?.path || 'untitled.etlsql',
                            scriptText: () => shell.getScriptText(),
                            value: draft.body,
                            label: `Runs on ${draft.connection}`,
                            runLabel: 'Run this task',
                            onChange: value => { draft.body = value; },
                        });
                    },
                });

                const repaint = () => {
                    workbench?.dispose?.();
                    workbench = null;
                    paint();
                };

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
            if (!groups.has(owner)) groups.set(owner, []);
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

        return studioDialog(
            { kicker: 'Run to here', title: `Run the pipeline through ${taskId}` },
            api => {
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
                                + skipped.map(id => `<code>${escapeHtml(id)}</code>`).join(', '), 'info')
                            : ''),
                    actions: [
                        { id: 'cancel', label: 'Cancel', run: () => api.close(false) },
                        { id: 'run', label: 'Run it', primary: true, run: () => api.close(true) },
                    ],
                });
            });
    }

    return {
        openDataWizard,
        openPipelineTaskEditor,
        openPipelineRunPlanConfirm,
        openChartBuilder,
        runChooseDataStep,
        runParameterStep,
        runDetailsStep,
        runTotalsStep,
        runFurnitureStep,
        runPreviewStep,
        runExportStep,
        runVisualsStep,
        runCrossFilterStep,
        hasDataSample,
        visualSourceBinding,
    };
}
