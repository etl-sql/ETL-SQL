/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-data-panel.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Dataset fields, samples, and data-workflow controls.
 */
import { _escapeHtml, _feedback, _readErrorText, _studioIcon, errorMessage, queryElement, queryElements } from './studio-context.js';
import { STUDIO_ROUTES, STUDIO_WORKSPACE_ROUTES } from './studio-contracts.js';
import { columnName as _columnName, columnType as _columnType, snapshotColumns as _snapshotColumns, requestSourceSample } from './studio-data.js';
import { STUDIO_VISUAL_GROUPS } from './visual-preview.js';
export function createStudioDataPanel(hostContext) {
    function fieldListMarkup() {
        const context = hostContext.activeDocumentContext();
        const columns = _snapshotColumns(context.snapshot).length ? _snapshotColumns(context.snapshot) : context.sourceColumns;
        if (!columns.length)
            return '<div class="etlsql-studio-empty-guidance"><strong>No fields loaded</strong><span>Choose a connection and table. Studio will create a bounded reusable sample.</span></div>';
        return columns.map((column) => {
            const name = _columnName(column);
            const type = _columnType(column, context.snapshot?.rows || []);
            return `<button type="button" class="etlsql-studio-field-pill" draggable="true" data-field="${_escapeHtml(name)}"><span>${type === 'number' ? '#' : type === 'date' ? '◷' : 'Aa'}</span><strong>${_escapeHtml(name)}</strong><small>${type}</small></button>`;
        }).join('');
    }
    function wireFields(host = hostContext.sidebarContent) {
        queryElements(host, '[data-field]').forEach(button => {
            button.addEventListener('dragstart', (event) => {
                event.dataTransfer?.setData('application/x-etlsql-field', button.dataset.field || '');
                event.dataTransfer?.setData('text/plain', button.dataset.field || '');
            });
            button.addEventListener('click', () => hostContext.openFilterSetupDialog(button.dataset.field || ''));
        });
    }
    async function loadSourceSample(connection, table) {
        const document = hostContext.getActiveDoc();
        const context = hostContext.documentContext(document);
        const key = `${connection}.${table}`;
        const cached = context.snapshotCache.get(key);
        if (cached) {
            context.snapshot = cached;
            if (hostContext.getActiveDoc() === document) {
                hostContext.updateSnapshotPackage(cached);
                hostContext.state.designerInstance?.refreshSnapshot?.();
                hostContext.renderSidebarContent(hostContext.state.activeActivity);
            }
            return;
        }
        const sample = await requestSourceSample({
            authFetch: hostContext.authFetch,
            url: hostContext.apiBase + STUDIO_ROUTES.dataSample,
            connection,
            table,
            documentUri: hostContext.getActiveDoc()?.path || 'studio',
            script: hostContext.state.editorInstance?.getValue?.() ?? hostContext.getActiveDoc()?.content ?? ''
        });
        context.snapshot = { ...sample, columns: (sample.columns.length ? sample.columns : context.sourceColumns) };
        context.snapshotCache.set(key, context.snapshot);
        if (hostContext.getActiveDoc() === document) {
            hostContext.updateSnapshotPackage(context.snapshot);
            hostContext.state.designerInstance?.refreshSnapshot?.();
            hostContext.renderSidebarContent(hostContext.state.activeActivity);
        }
        _feedback.notify(`Created a reusable sample with ${context.snapshot?.rowCount ?? 0} rows from ${table}.`, { title: 'Data ready', tone: 'success' });
    }
    function datasetForPreview(designState, context) {
        const datasets = designState?.datasets || [];
        if (!datasets.length)
            return null;
        const snapshotName = String(context.snapshot?.source || '').replace(/^[&]/, '');
        const visualDataset = (designState.pages || [])
            .flatMap((page) => page.visuals || [])
            .map((visual) => visual.dataset)
            .find(Boolean);
        return datasets.find((dataset) => String(dataset.name || '').replace(/^[&]/, '') === snapshotName)
            || datasets.find((dataset) => dataset.name === visualDataset)
            || datasets[0];
    }
    async function synchronizeCodeToCanvas(document, script, revision) {
        if (hostContext.getActiveDoc() !== document || hostContext.documentContext(document).syncRevision !== Number(revision))
            return;
        const result = await hostContext.state.designerInstance?.applyScriptText?.(script);
        const context = hostContext.documentContext(document);
        if (!result?.applied || hostContext.getActiveDoc() !== document || context.syncRevision !== Number(revision))
            return;
        const inferredWorkflow = hostContext.explicitReportWorkflow(script, result.designState);
        if (inferredWorkflow)
            document.reportWorkflow = inferredWorkflow;
        hostContext.renderReportWorkflowChrome(document, result.designState);
        const dataset = datasetForPreview(result.designState, context);
        if (!dataset?.name || !dataset?.query)
            return;
        const signature = `${dataset.name}\n${dataset.query}`;
        if (context.previewedDatasetSignature === signature)
            return;
        context.previewAbort?.abort();
        const controller = new AbortController();
        context.previewAbort = controller;
        try {
            const response = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.dataSample, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: controller.signal,
                body: JSON.stringify({
                    sourceKind: 'dataset',
                    dataset: dataset.name,
                    documentUri: document.path || 'studio',
                    script,
                }),
            });
            if (!response.ok)
                throw new Error(await _readErrorText(response));
            const sample = await response.json();
            if (controller.signal.aborted || hostContext.getActiveDoc() !== document || context.syncRevision !== Number(revision))
                return;
            context.snapshot = {
                source: sample.source || dataset.name,
                columns: sample.columns || [],
                rowCount: sample.rowCount ?? sample.rows?.length ?? 0,
                rows: sample.rows || [],
            };
            context.snapshotCache.set(`dataset:${dataset.name}`, context.snapshot);
            context.previewedDatasetSignature = signature;
            hostContext.updateSnapshotPackage(context.snapshot);
            hostContext.state.designerInstance?.refreshSnapshot?.();
            if (hostContext.state.activeActivity === 'catalog' || hostContext.state.activeActivity === 'palette') {
                hostContext.renderSidebarContent(hostContext.state.activeActivity);
            }
            else if (hostContext.state.filterSidebarOpen) {
                hostContext.renderFilterPanel();
            }
        }
        catch (error) {
            if (error?.name !== 'AbortError' && !controller.signal.aborted && context.syncRevision === Number(revision)) {
                _feedback.notify(errorMessage(error) || 'The dataset preview could not be refreshed.', {
                    title: 'Preview kept previous data',
                    tone: 'warning',
                });
            }
        }
        finally {
            if (context.previewAbort === controller)
                context.previewAbort = null;
        }
    }
    function renderDataWorkflow() {
        const context = hostContext.activeDocumentContext();
        const selectedSource = context.selectedSource;
        hostContext.sidebarTitle.textContent = 'Data';
        hostContext.sidebarContent.innerHTML = `<div class="etlsql-studio-data-workflow"><div class="etlsql-studio-data-actions"><button type="button" class="etlsql-studio-btn is-primary etlsql-studio-new-connection" data-action="wizard">${_studioIcon('plus', 13)} New connection</button><button type="button" class="etlsql-studio-btn etlsql-studio-new-dataset" data-new-dataset>${_studioIcon('plus', 13)} New dataset</button><button type="button" class="etlsql-studio-btn etlsql-studio-build-chart" data-build-chart>${_studioIcon('plus', 13)} Build a chart</button></div>${hostContext.guidedRailToggleMarkup()}<section><div class="etlsql-studio-subhead"><div><strong>Connections</strong><span>${selectedSource?.connection ? _escapeHtml(selectedSource.connection) : 'Choose one to browse tables'}</span></div></div><div class="etlsql-catalog-conn-list"><span class="etlsql-studio-loading">Loading connections…</span></div><div class="etlsql-catalog-table-list" data-table-list></div></section><section><div class="etlsql-studio-subhead"><div><strong>Fields</strong><span>${hostContext.hasDataSample() ? `${context.snapshot?.rowCount ?? 0} rows cached · drag into Filters` : 'Choose a table to create a sample'}</span></div><span class="etlsql-studio-count">${_snapshotColumns(context.snapshot).length || context.sourceColumns.length}</span></div><div class="etlsql-studio-field-list">${fieldListMarkup()}</div></section></div>`;
        queryElement(hostContext.sidebarContent, '[data-action="wizard"]')?.addEventListener('click', () => hostContext.handleOpenConnectionWizard());
        queryElement(hostContext.sidebarContent, '[data-new-dataset]')?.addEventListener('click', () => hostContext.openDataWizard());
        queryElement(hostContext.sidebarContent, '[data-build-chart]')?.addEventListener('click', () => hostContext.openChartBuilder());
        hostContext.wireGuidedRailToggle(hostContext.sidebarContent);
        wireFields();
        const renderConnections = (connections) => {
            const list = queryElement(hostContext.sidebarContent, '.etlsql-catalog-conn-list');
            if (!list)
                return;
            list.innerHTML = connections.map((item) => { const alias = typeof item === 'string' ? item : item.alias || item.name; return `<button type="button" class="etlsql-studio-source-btn" data-connection="${_escapeHtml(alias)}">${_studioIcon('catalog', 14)}<strong>${_escapeHtml(alias)}</strong></button>`; }).join('') || '<div class="etlsql-studio-empty-compact">No connections configured.</div>';
            queryElements(list, '[data-connection]').forEach(button => button.addEventListener('click', async () => {
                const connection = button.dataset.connection || '';
                hostContext.activeDocumentContext().selectedSource = { connection, table: '' };
                const tableList = queryElement(hostContext.sidebarContent, '[data-table-list]');
                tableList.innerHTML = '<span class="etlsql-studio-loading">Loading tables…</span>';
                const documentUri = hostContext.getActiveDoc()?.path || 'studio';
                const response = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.schema + `?connection=${encodeURIComponent(connection)}&documentUri=${encodeURIComponent(documentUri)}`);
                const data = response.ok ? await response.json() : { tables: [] };
                tableList.innerHTML = (data.tables || []).map((table) => `<button type="button" class="etlsql-studio-table-btn" data-table="${_escapeHtml(table.name)}"><span>${_studioIcon('table', 13)} ${_escapeHtml(table.name)}</span><small>${table.columns?.length || 0} fields</small></button>`).join('');
                queryElements(tableList, '[data-table]').forEach(tableButton => tableButton.addEventListener('click', async () => { const table = data.tables.find((item) => item.name === tableButton.dataset.table); const activeContext = hostContext.activeDocumentContext(); activeContext.selectedSource = { connection, table: table?.name || '' }; activeContext.sourceColumns = table?.columns || []; hostContext.renderSidebarContent(hostContext.state.activeActivity); try {
                    await loadSourceSample(connection, table?.name || '');
                }
                catch (error) {
                    _feedback.notify(errorMessage(error), { title: 'Data sample failed', tone: 'error' });
                } }));
            }));
        };
        loadConnectionAliases().then(renderConnections).catch(() => renderConnections([]));
    }
    /**
     * The catalog connections this host shares, with their connector types, as last read. Studio
     * declares the ones a script uses whenever it writes the script.
     */
    let sharedConnections = [];
    const sharedConnectionList = () => sharedConnections;
    // Connection aliases come from different places per host: the desktop reads the workspace's
    // registered connections, the Portal exposes only ACL-filtered aliases via session metadata.
    // Session metadata exists on both, so it is the fallback rather than a second guess.
    async function loadConnectionAliases() {
        if (hostContext.hasWorkspaceHost) {
            try {
                const documentUri = hostContext.getActiveDoc()?.path || 'studio';
                const script = hostContext.state.editorInstance?.getValue?.() ?? hostContext.getActiveDoc()?.content ?? '';
                await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.analyze, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ script, documentUri }),
                });
                const res = await hostContext.authFetch(hostContext.apiBase + STUDIO_WORKSPACE_ROUTES.connections + `?documentUri=${encodeURIComponent(documentUri)}`);
                if (res.ok) {
                    const data = await res.json();
                    const connections = data.connections || (Array.isArray(data) ? data : []);
                    if (connections.length)
                        return connections;
                }
            }
            catch {
                // Fall through to session metadata below.
            }
        }
        const sessionResponse = await hostContext.authFetch(hostContext.apiBase + STUDIO_ROUTES.sessionMetadata);
        if (!sessionResponse.ok)
            return [];
        const metadata = await sessionResponse.json();
        sharedConnections = (metadata.sharedConnections || [])
            .filter((item) => item?.alias && item?.connectorType)
            .map((item) => ({ alias: String(item.alias), connectorType: String(item.connectorType) }));
        return metadata.connections || [];
    }
    function renderVisualLibrary() {
        hostContext.sidebarTitle.textContent = 'Visual Components';
        hostContext.sidebarContent.innerHTML = `<section class="etlsql-studio-library-section"><div class="etlsql-studio-subhead"><div><strong>On this page</strong><span>Report tree</span></div></div><div class="etlsql-studio-report-tree">${hostContext.reportTreeMarkup()}</div></section><section class="etlsql-studio-library-section"><label class="etlsql-studio-library-search"><span>Add a visual</span><input type="search" data-visual-search placeholder="Search visual types" ${hostContext.hasDataSample() ? '' : 'disabled'}></label>${hostContext.hasDataSample() ? '' : '<div class="etlsql-studio-empty-guidance"><strong>Data comes first</strong><span>Create a dataset so every visual can read from one named query.</span><button type="button" class="etlsql-studio-btn is-primary" data-choose-data>Create a dataset</button></div>'}<div data-visual-groups>${STUDIO_VISUAL_GROUPS.map(group => `<div class="etlsql-studio-visual-group" data-visual-group><strong>${group.name}</strong><div>${group.types.map(type => `<button type="button" class="etlsql-palette-sidebar-btn" data-add-visual="${type}" data-visual-name="${type}" ${hostContext.hasDataSample() ? '' : 'disabled'}>${type}</button>`).join('')}</div></div>`).join('')}</div></section><section class="etlsql-studio-library-section"><div class="etlsql-studio-subhead"><div><strong>Presentation</strong><span>Theme, colours, and saved views</span></div></div><button type="button" class="etlsql-studio-btn" data-report-style>${_studioIcon('canvas', 13)} Report theme and style</button><div data-bookmark-host></div></section>`;
        queryElement(hostContext.sidebarContent, '[data-choose-data]')?.addEventListener('click', () => hostContext.runChooseDataStep());
        queryElements(hostContext.sidebarContent, '[data-add-visual]').forEach(button => { button.draggable = !button.disabled; button.addEventListener('dragstart', (event) => { event.dataTransfer?.setData('application/x-etlsql-visual', button.dataset.addVisual || ''); event.dataTransfer?.setData('text/plain', button.dataset.addVisual || ''); }); button.addEventListener('click', () => hostContext.openChartBuilder({ type: button.dataset.addVisual })); });
        queryElements(hostContext.sidebarContent, '[data-tree-visual]').forEach(button => button.addEventListener('click', () => hostContext.state.designerInstance?.selectVisual?.(button.dataset.treeVisual)));
        queryElement(hostContext.sidebarContent, '[data-report-style]')?.addEventListener('click', hostContext.showReportProperties);
        // The designer's own bookmark editor, moved into this rail rather than reimplemented beside
        // it. Studio hides the designer sidebar, which is where this section otherwise lives — so
        // without the move it exists, works, and is unreachable from the workbench.
        hostContext.state.designerInstance?.mountBookmarks?.(queryElement(hostContext.sidebarContent, '[data-bookmark-host]'));
        const search = queryElement(hostContext.sidebarContent, '[data-visual-search]');
        search?.addEventListener('input', () => { const query = search.value.trim().toUpperCase(); queryElements(hostContext.sidebarContent, '[data-visual-name]').forEach(button => button.hidden = Boolean(query) && !button.dataset.visualName.includes(query)); });
    }
    return { synchronizeCodeToCanvas, renderDataWorkflow, loadConnectionAliases, sharedConnectionList, renderVisualLibrary };
}
