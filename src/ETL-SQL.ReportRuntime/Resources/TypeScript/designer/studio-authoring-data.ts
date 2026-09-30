/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Data selection, samples, dataset registry, and data wizard.
 */

import type { StudioAuthoringOptions } from './studio-authoring-context.js';


import { asButton, asHtml, asInput, declaredConnectionNames, readConnectionSchema } from './studio-authoring-context.js';

import type { StudioAuthoringDialogApi, StudioAuthoringDialogElements, StudioAuthoringEditorTransport, StudioAuthoringFeedback, StudioAuthoringRequestOptions, StudioAuthoringShell } from './studio-authoring-context.js';
import {
escapeHtml,
noteMarkup as guidedNoteMarkup,
sampleGridMarkup as sampleRowsMarkup, sqlPreviewMarkup
} from './studio-authoring-ui.js';
import { columnName, columnType, snapshotColumns, updateSnapshotPackage as writeSnapshotPackage } from './studio-data.js';
import { createQueryWorkbench } from './studio-query-workbench.js';

export interface StudioAuthoringDataContext {
    readonly activeContext: () => any;
    readonly catalogRoutes: StudioAuthoringOptions['catalogRoutes'];
    readonly contextFor: (doc: any) => any;
    readonly dialog: StudioAuthoringDialogElements;
    readonly editorTransport: StudioAuthoringEditorTransport;
    readonly feedback: StudioAuthoringFeedback;
    readonly getActiveDocument: () => any;
    readonly guidedBlocker: ({ kicker, title, lede, remedyLabel, remedy }: { kicker: string; title: string; lede: string; remedyLabel: string; remedy: () => Promise<any> | any; }) => Promise<boolean>;
    readonly hasWorkspaceHost: boolean;
    readonly mutate: (label: string, mutator: (design: any) => any) => Promise<any>;
    readonly request: (route: string, options?: StudioAuthoringRequestOptions) => Promise<any>;
    readonly routes: StudioAuthoringOptions['routes'];
    readonly shell: StudioAuthoringShell;
    readonly studioDialog: <T = any>({ kicker, title, wide }: { kicker: string; title: string; wide?: boolean; }, controller: (api: StudioAuthoringDialogApi) => void) => Promise<T | null>;
}

export function createStudioAuthoringData(hostContext: StudioAuthoringDataContext) {

    /** A surface is only usable once the document has a sample to bind against. */
    function hasDataSample(): boolean {
        const snapshot = hostContext.activeContext().snapshot;
        return Boolean(snapshot?.source && snapshotColumns(snapshot).length);
    }

    /**
     * How a new visual should point at the current sample. A dataset-backed sample is referenced by
     * name so every visual shares one query; a connection-backed sample still has to inline its
     * SELECT, because there is no named query to reference yet.
     */
    function visualSourceBinding(): { dataset: string | null; options: Record<string, string> } {
        const source = hostContext.activeContext().snapshot?.source || null;
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
    function visualSourceClause(source: unknown): string {
        const text = String(source).trim();
        if (text.startsWith('#') || text.startsWith('&')) return text;
        if (text.startsWith('(') || /^select\b/i.test(text)) return text;
        return text.includes('.') ? `(SELECT * FROM ${text})` : text;
    }

    function guidedColumnNames(): string[] {
        return snapshotColumns(hostContext.activeContext().snapshot).map(columnName);
    }

    function guidedNumericColumns(): string[] {
        const context = hostContext.activeContext();
        const rows = context.snapshot?.rows || [];
        return snapshotColumns(context.snapshot)
            .filter((column: any) => columnType(column, rows) === 'number')
            .map(columnName);
    }

    /** Samples a named dataset so the canvas, field list, and filters all read from the same rows. */
    async function loadDatasetSample(datasetName: string): Promise<any> {
        const doc = hostContext.getActiveDocument();
        const context = hostContext.contextFor(doc);
        const sample = await hostContext.request(hostContext.routes.dataSample, {
            body: {
                sourceKind: 'dataset',
                dataset: datasetName,
                documentUri: doc?.path || 'studio',
                script: hostContext.shell.getScriptText(),
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
        if (hostContext.getActiveDocument() === doc) {
            writeSnapshotPackage(hostContext.activeContext(), context.snapshot);
            hostContext.shell.refreshSnapshot();
            hostContext.shell.renderSidebar();
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

    function datasetBaseName(seed: unknown): string {
        const cleaned = String(seed || 'dataset').replace(/[^A-Za-z0-9_]/g, '_').replace(/^_+/, '').toLowerCase();
        return /^[a-z]/.test(cleaned) ? cleaned : `data_${cleaned || 'set'}`;
    }

    /** Datasets this script declares, from the canonical parse rather than a text scan. */
    async function scriptDatasetNames(): Promise<Array<{ name: string; query: string }>> {
        try {
            const parsed = await hostContext.request(hostContext.routes.parse, { body: { script: hostContext.shell.getScriptText() } });
            return (parsed.designState?.datasets || []).map((dataset: any) => ({
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
    async function registryDatasets(): Promise<Array<{
        name: string;
        folderPath: string;
        rowCount: number | null;
        accessLevel: string | null;
        isStale: boolean;
    }>> {
        if (hostContext.hasWorkspaceHost) return [];
        try {
            const data = await hostContext.request(hostContext.catalogRoutes.datasetRegistry, { method: 'GET' });
            return (Array.isArray(data) ? data : data.datasets || [])
                .filter((dataset: any) => dataset?.name)
                .map((dataset: any) => ({
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
    async function openDataWizard({ intent = null, connection = null }: { intent?: string | null; connection?: string | null } = {}): Promise<string | null> {
        const doc = hostContext.getActiveDocument();
        if (!doc) return null;
        const context = hostContext.contextFor(doc);
        const wizard: {
            pane: 'start' | 'existing' | 'connection' | 'source' | 'name';
            intent: string;
            ttl: string;
            scriptDatasets: Array<{ name: string; query: string }> | null;
            registry: Array<{ name: string; folderPath: string; rowCount: number | null; accessLevel: string | null; isStale: boolean }> | null;
            connections: string[];
            scriptConnections: Set<string>;
            connection: string | null;
            tables: any[] | null;
            tablesFailed?: boolean;
            table: string | null;
            mode: string;
            query: string;
            name: string;
            preview: any;
            error: string | null;
            queryWorkbench: any;
        } = {
            // Resuming after the connection wizard skips straight back to the pane the author was on.
            pane: intent ? 'connection' : 'start',
            // 'dataset' caches through CREATE DATASET; 'live' binds the query straight to the visuals.
            intent: intent || 'dataset',
            ttl: '',
            scriptDatasets: null,
            registry: null,
            connections: declaredConnectionNames(hostContext.shell.getScriptText()),
            // Which of `connections` the script itself declares. The host's aliases are merged into
            // the same list below, and without this every entry was labelled "Declared in this
            // report" - including the ones that are not, which is the difference that decides
            // whether the report still runs for anybody else.
            scriptConnections: new Set(
                declaredConnectionNames(hostContext.shell.getScriptText()).map(alias => String(alias).toLowerCase())),
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

        return await hostContext.studioDialog<string>({ kicker: 'Step 1 · Choose data', title: 'Choose data', wide: true }, api => {
            const fail = (message: string) => { wizard.error = message; paint(); };
            const errorMarkup = () => (wizard.error ? guidedNoteMarkup(wizard.error, 'error') : '');

            const disposeWorkbench = () => {
                wizard.queryWorkbench?.dispose?.();
                wizard.queryWorkbench = null;
            };
            const finish = (value: string | null) => { disposeWorkbench(); api.close(value); };

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
                        const path = asHtml(button).dataset.startPath;
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
                    button.addEventListener('click', () => useExistingDataset(asHtml(button).dataset.useDataset || '', asHtml(button).dataset.datasetOrigin || ''))),
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
                    lede: 'Pick the connection this dataset reads from. The ones this report declares travel with it; '
                        + 'the ones this host knows about do not, so a report built on those runs here and fails elsewhere.',
                    body: errorMarkup() + `<div class="etlsql-studio-choice-list">${wizard.connections.map(alias => {
                        const declared = wizard.scriptConnections.has(String(alias).toLowerCase());
                        return `
                        <button type="button" data-pick-connection="${escapeHtml(alias)}" data-connection-origin="${declared ? 'script' : 'host'}" class="${wizard.connection === alias ? 'active' : ''}">
                            <strong>${escapeHtml(alias)}</strong><span>${declared
                                ? 'Declared in this report'
                                : 'Known to this host — not declared in this report'}</span></button>`;
                    }).join('')}</div>`,
                    actions: [
                        { id: 'back', label: 'Back', run: () => { wizard.pane = 'start'; wizard.error = null; paint(); } },
                        { id: 'connect', label: 'New connection…', run: openConnectionThenReturn },
                    ],
                    wire: host => host.querySelectorAll('[data-pick-connection]').forEach(button =>
                        button.addEventListener('click', () => openConnection(asHtml(button).dataset.pickConnection || ''))),
                });
            };

            const paintSource = () => api.render({
                lede: `Reading from <strong>${escapeHtml(wizard.connection || '')}</strong>. Pick a table to read whole, or build the query yourself.`,
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
                                : guidedNoteMarkup(wizard.tablesFailed
                                    ? 'The tables for this connection could not be read, so there is nothing to pick from yet. '
                                      + 'The reason is above. Writing the query yourself does not need this list.'
                                    : 'This connection reported no tables you can read.', 'warning'))
                        : '<div class="etlsql-studio-query-workbench" data-query-workbench></div>')
                    + (wizard.preview ? sampleRowsMarkup(wizard.preview) : ''),
                actions: [
                    { id: 'back', label: 'Back', run: () => { wizard.pane = 'connection'; wizard.error = null; wizard.preview = null; paint(); } },
                    { id: 'next', label: 'Next', primary: true, disabled: !wizardQuery(), run: goToName },
                ],
                wire: host => {
                    host.querySelectorAll('[data-source-mode]').forEach(button => button.addEventListener('click', () => {
                        wizard.mode = asHtml(button).dataset.sourceMode || 'table';
                        wizard.preview = null;
                        paint();
                    }));
                    host.querySelectorAll('[data-pick-table]').forEach(button => button.addEventListener('click', () => {
                        wizard.table = asHtml(button).dataset.pickTable || null;
                        wizard.preview = null;
                        paint();
                        // Seeing the rows is the point of this step, so the sample loads on selection
                        // rather than behind another button.
                        previewTable();
                    }));
                    const filter = asInput(host.querySelector('[data-table-filter]'));
                    filter?.addEventListener('input', () => {
                        const query = filter.value.trim().toLowerCase();
                        host.querySelectorAll('[data-pick-table]').forEach(button => {
                            const pickTable = asHtml(button).dataset.pickTable || '';
                            asHtml(button).hidden = Boolean(query) && !pickTable.toLowerCase().includes(query);
                        });
                    });
                    const workbenchHost = asHtml(host.querySelector('[data-query-workbench]'));
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
                const sql = `CREATE DATASET &${base}${lifespan} AS (\n  ${wizardQuery()}\n);`;
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
                        host.querySelector('[data-dataset-name]')?.addEventListener('change', event => { wizard.name = asInput(event.target).value; paint(); });
                        host.querySelector('[data-dataset-ttl]')?.addEventListener('change', event => { wizard.ttl = asInput(event.target).value; paint(); });
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
                        + sqlPreviewMarkup(`CREATE VISUAL … (\n    SOURCE = ${source},\n    …\n);`,
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
                writeSnapshotPackage(hostContext.activeContext(), context.snapshot);
                hostContext.shell.refreshSnapshot();
                hostContext.shell.renderSidebar();
                hostContext.feedback.notify(
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
                hostContext.shell.openConnectionWizard({
                    onDone: alias => openDataWizard({ intent: resumeIntent, connection: alias }),
                });
            };

            const openConnection = async (alias: string) => {
                wizard.connection = alias;
                wizard.tables = null;
                wizard.tablesFailed = false;
                wizard.table = null;
                wizard.preview = null;
                wizard.pane = 'source';
                wizard.error = null;
                paint();
                try {
                    const schema = await readConnectionSchema(hostContext, hostContext.shell.getScriptText(),
                        doc.path || 'studio', alias, `The schema for ${alias} could not be read.`);
                    // The author may have picked another connection while this one was being read.
                    if (wizard.connection !== alias) return;
                    wizard.tables = schema?.tables || [];
                    wizard.tablesFailed = false;
                } catch (error: any) {
                    if (wizard.connection !== alias) return;
                    // An empty list here is not the same claim as "this connection has no tables",
                    // but the step read it as one: the catch set tables to [], which is exactly what
                    // the zero-tables branch tests, so a failed schema read rendered as a confident
                    // statement about the connection. That is what sent a manual evaluation of this
                    // step looking for an empty result the server never returned.
                    wizard.tables = [];
                    wizard.tablesFailed = true;
                    wizard.error = error.message;
                }
                paint();
            };

            const previewTable = async () => {
                if (wizard.mode !== 'table' || !wizard.table || !wizard.connection) return;
                try {
                    wizard.preview = await sampleConnectionTable(wizard.connection, wizard.table);
                    wizard.error = null;
                } catch (error: any) {
                    wizard.preview = null;
                    wizard.error = error.message;
                }
                paint();
            };

            const mountQueryWorkbench = async (host: HTMLElement) => {
                if (wizard.queryWorkbench) return;
                wizard.queryWorkbench = await createQueryWorkbench(host, {
                    connection: wizard.connection || '',
                    context: 'engine',
                    routes: hostContext.routes,
                    request: hostContext.request,
                    editorTransport: hostContext.editorTransport,
                    documentUri: () => hostContext.getActiveDocument()?.path || 'untitled.rptsql',
                    scriptText: () => hostContext.shell.getScriptText(),
                    value: wizard.query || `SELECT *\nFROM ${wizard.connection}.`,
                    label: 'Dataset query · Engine context',
                    onChange: (value: string) => {
                        wizard.query = value;
                        const next = asButton(hostContext.dialog.box.querySelector('[data-dialog-action="next"]'));
                        if (next) next.disabled = !wizardQuery();
                    },
                    onSample: (sample: any) => { wizard.preview = sample; },
                });
                const next = asButton(hostContext.dialog.box.querySelector('[data-dialog-action="next"]'));
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

            const useExistingDataset = async (name: string, origin: string) => {
                api.busy(true);
                try {
                    if (origin === 'registry') {
                        // USE DATASET is a script statement, not designer state, so it is inserted as
                        // text ahead of the presentation statements the same way CREATE CONNECTION is.
                        insertScriptStatement(`USE DATASET ${name};`);
                    }
                    const snapshot = await loadDatasetSample(name);
                    context.selectedSource = { connection: null, table: name };
                    hostContext.feedback.notify(
                        `Reading from ${name} — ${snapshot.rowCount} row${snapshot.rowCount === 1 ? '' : 's'} sampled.`,
                        { title: 'Dataset ready', tone: 'success' });
                    finish(name);
                } catch (error: any) {
                    api.busy(false);
                    fail(error.message);
                }
            };

            const create = async () => {
                const base = datasetBaseName(wizard.name);
                const query = wizardQuery();
                if (!query) return fail('This dataset has no query yet.');
                api.busy(true);
                const created = await hostContext.mutate('Create dataset', design => {
                    design.datasets ||= [];
                    const taken = new Set(design.datasets.map((item: any) => String(item.name || '').replace(/^&/, '').toLowerCase()));
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
                    hostContext.feedback.notify(
                        `${created} is ready with ${snapshot.rowCount} sampled row${snapshot.rowCount === 1 ? '' : 's'}. Visuals can reference it by name.`,
                        { title: 'Dataset created', tone: 'success' });
                } catch (error: any) {
                    // The statement is in the script either way; say so rather than implying the step
                    // failed, because the author's next step depends on knowing it exists.
                    hostContext.feedback.notify(
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
            Promise.resolve(hostContext.shell.availableConnections?.() ?? []).then((hosted: any) => {
                const names = (hosted || [])
                    .map((item: any) => (typeof item === 'string' ? item : item?.alias || item?.name))
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
    function insertScriptStatement(statement: string): void {
        const doc = hostContext.getActiveDocument();
        if (!doc) return;
        const script = hostContext.shell.getScriptText();
        const match = /CREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?(?:VISUAL|CONTAINER|BUTTON|PAGE)\b/i.exec(script);
        const at = match ? match.index : script.length;
        const next = script.slice(0, at) + statement + '\n\n' + script.slice(at);
        doc.content = next;
        doc.isDirty = true;
        hostContext.shell.setScriptText(next, 'Use dataset');
        hostContext.shell.renderTabs();
    }

    /** Samples a connection table through the host's design-time preview budget. */
    async function sampleConnectionTable(connection: string, table: string): Promise<any> {
        const doc = hostContext.getActiveDocument();
        const sample = await hostContext.request(hostContext.routes.dataSample, {
            body: {
                sourceKind: 'connection',
                connection,
                table,
                documentUri: doc?.path || 'studio',
                script: hostContext.shell.getScriptText(),
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
    async function runChooseDataStep(): Promise<string | null> {
        hostContext.shell.setActivity('catalog');
        return await openDataWizard();
    }

    /** Every step after the first needs a sample; this is the one place that says so. */
    async function requireDataSample(stepLabel: string): Promise<boolean> {
        if (hasDataSample()) return true;
        return await hostContext.guidedBlocker({
            kicker: stepLabel,
            title: 'Choose data first',
            lede: 'This step writes report items that read from a dataset, so Studio needs one before it can write anything useful. '
                + 'Creating a dataset takes a connection, a table or query, and a name.',
            remedyLabel: 'Create a dataset',
            remedy: () => runChooseDataStep(),
        });
    }

    return { hasDataSample, visualSourceBinding, guidedColumnNames, guidedNumericColumns, datasetBaseName, openDataWizard, runChooseDataStep, requireDataSample };
}
