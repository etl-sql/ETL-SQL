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
import { escapeHtml, noteMarkup as guidedNoteMarkup, sampleGridMarkup as sampleRowsMarkup, sqlPreviewMarkup } from './studio-authoring-ui.js';
import { createQueryWorkbench } from './studio-query-workbench.js';
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
            const api = {
                close,
                setTitle(next) { dialog.box.querySelector('[data-dialog-title]').textContent = next; },
                // Every footer button is disabled while a request is in flight, so a slow schema read
                // cannot be double-submitted into two datasets.
                busy(flag) { actionHost.querySelectorAll('button').forEach(button => { button.disabled = flag; }); },
                render({ lede = '', body = '', actions = [], wire } = {}) {
                    bodyHost.innerHTML = (lede ? `<p class="etlsql-studio-guided-lede">${lede}</p>` : '') + body;
                    actionHost.innerHTML = actions.map(action => `<button type="button"
                        class="etlsql-studio-btn${action.primary ? ' is-primary' : ''}"
                        data-dialog-action="${escapeHtml(action.id)}"${action.disabled ? ' disabled' : ''}
                        >${escapeHtml(action.label)}</button>`).join('');
                    actionHost.querySelectorAll('[data-dialog-action]').forEach(button => button.addEventListener('click', async () => {
                        try {
                            await actions.find(action => action.id === button.dataset.dialogAction)?.run?.();
                        } catch (error) {
                            // A dropped promise here is invisible: the mutation may already have landed
                            // while the dialog silently stops responding. Say so instead.
                            feedback.notify(error?.message || 'That action could not be completed.',
                                { title: 'Action failed', tone: 'error' });
                        }
                    }));
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
        return { dataset: null, options: source ? { inline_source: source } : {} };
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
    async function openDataWizard() {
        const doc = getActiveDocument();
        if (!doc) return null;
        const context = contextFor(doc);
        const wizard = {
            pane: 'start',
            // 'dataset' caches through CREATE DATASET; 'live' binds the query straight to the visuals.
            intent: 'dataset',
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
            const errorMarkup = () => (wizard.error ? guidedNoteMarkup(escapeHtml(wizard.error), 'error') : '');

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
                            'Host-registered aliases are deliberately not offered here. A dataset built on an alias this script '
                            + 'does not declare would preview correctly and then fail for every other reader.', 'info'),
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
                        + sqlPreviewMarkup(sql)
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
);`, 'Visuals will be written with this source')
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

            const liveSourceClause = () => (wizard.mode === 'table' && wizard.table
                ? `${wizard.connection}.${wizard.table}`
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
                // The connection wizard owns the whole modal surface, so this one steps aside and the
                // author returns to a data wizard that can now see the connection they just wrote.
                finish(null);
                shell.openConnectionWizard({ onDone: () => openDataWizard() });
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

            paint();
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
        shell.setScriptText(next);
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

            const sql = () => {
                const source = sourceExpression();
                const entries = Object.entries(resolvedMappings()).filter(([, value]) => value);
                return `CREATE VISUAL ${datasetBaseName(draft.title || `${draft.type.toLowerCase()}_visual`)} AS ${draft.type} (\n`
                    + `    SOURCE = ${source}`
                    + (entries.length ? `,\n    MAPPINGS (${entries.map(([role, value]) => `${role} = ${value}`).join(', ')})` : '')
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
                            placeholder="${escapeHtml(`${draft.type} visual`)}"></label>`
                    + sqlPreviewMarkup(sql()),
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
                        options: { ...source.options },
                    });
                    return name;
                });
                api.busy(false);
                if (added) feedback.notify(`Added ${type} visual ${added}.`, { title: 'Visual added', tone: 'success' });
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
                        + sqlPreviewMarkup(declarationSql(draft)),
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
                    + sqlPreviewMarkup(`OPTIONS (GRAND_TOTAL = ${draft.aggregate})`, 'Adds this option to the table'),
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
        shell.setProjection('split');
        feedback.notify('Running the report. Physical pages appear in the canvas; rows and messages appear below the script.',
            { title: 'Preview running', tone: 'info' });
        shell.runReport();
    }

    async function runExportStep() {
        await studioDialog({ kicker: 'Step 8 · Export', title: 'Export this report' }, api => api.render({
            lede: 'A paginated report exports two different things. <strong>PDF</strong> keeps the physical pages, margins, and breaks you configured. '
                + '<strong>CSV and Excel</strong> export the result rows only, with no page layout.',
            body: `<ul class="etlsql-studio-guided-list">
                    <li><strong>PDF</strong> — use the Report-SQL PDF export, which renders the same page setup as the preview.</li>
                    <li><strong>CSV / Excel</strong> — run the report, then use the export buttons on the Results pane below the script.</li>
                </ul>`
                + guidedNoteMarkup('Export runs the report again against live data, so parameter prompts apply at export time too.', 'info'),
            actions: [
                { id: 'close', label: 'Close', run: () => api.close(null) },
                { id: 'run', label: 'Run the report now', primary: true, run: () => { api.close('run'); runPreviewStep(); } },
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
    return {
        openDataWizard,
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
