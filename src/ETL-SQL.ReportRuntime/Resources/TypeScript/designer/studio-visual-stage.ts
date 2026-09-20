/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Data-model and report-canvas projections.
 */

import { _escapeHtml, _feedback, errorMessage, queryElement, queryElements } from './studio-context.js';
import type { StudioCapabilityState } from './studio-host.js';

import { createDesigner, renderDag } from './designer.js';
import type { StudioDesignState, StudioDomElement, StudioDynamic, StudioOptions, StudioRuntimeContext, StudioRuntimeDocument, StudioRuntimeState } from './studio-context.js';
import { STUDIO_ROUTES } from './studio-contracts.js';
import { columnName as _columnName, snapshotColumns as _snapshotColumns } from './studio-data.js';

export interface StudioVisualStageContext {
    readonly activeDocumentContext: () => StudioRuntimeContext;
    readonly activeScriptText: () => any;
    readonly apiBase: string;
    readonly authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    readonly canvasContainer: StudioDomElement;
    readonly disposePipelineDag: () => void;
    readonly documentContext: (document?: StudioRuntimeDocument | null) => StudioRuntimeContext;
    readonly getActiveDoc: () => StudioRuntimeDocument | null;
    readonly hasDataSample: () => boolean;
    readonly host: { authFetch: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>; apiBase: string; hasWorkspaceHost: boolean; hasGitHost: boolean; hasCapability(state: StudioCapabilityState, capability: string): boolean; };
    readonly inspector: StudioDomElement;
    isSyncingFromDesigner: boolean;
    readonly isVisualLocked: (visual: StudioDynamic, doc?: StudioRuntimeDocument | null) => boolean;
    readonly opts: StudioOptions;
    readonly propertiesHost: StudioDomElement;
    readonly propertyFields: StudioDomElement;
    readonly renderFilterPanel: () => void;
    readonly renderOutlineTree: () => void;
    readonly renderPipelineDag: (doc: StudioRuntimeDocument, content: string) => Promise<void>;
    readonly renderReportWorkflowChrome: (doc: StudioRuntimeDocument | null, designState?: StudioDesignState | undefined) => void;
    readonly renderSidebarContent: (activity: string) => void;
    readonly renderTabs: () => void;
    readonly runChooseDataStep: () => Promise<string | null>;
    readonly scheduleDraftSave: (doc: StudioRuntimeDocument | null | undefined) => void;
    readonly setActivity: (activity: string) => void;
    readonly setProjection: (mode: string) => void;
    readonly sidebarContent: StudioDomElement;
    readonly sidebarTitle: StudioDomElement;
    readonly state: StudioRuntimeState;
    readonly visualSourceBinding: () => { dataset: string | null; options: Record<string, string>; };
}

export function createStudioVisualStage(hostContext: StudioVisualStageContext) {
    const DATA_MODEL_CARDINALITY_LABEL: Record<string, string> = {
        'one-to-one': '1 : 1',
        'many-to-one': 'n : 1',
        'one-to-many': '1 : n',
        'many-to-many': 'n : n',
        unknown: 'not stated',
    };

    const DATA_MODEL_NODE_TYPE: Record<string, string> = {
        connection: 'connection',
        table: 'table',
        temp: 'io',
        cte: 'container',
        dataset: 'dataset',
    };

    function disposeDataModel() {
        hostContext.state.dataModelInstance?.dispose?.();
        hostContext.state.dataModelInstance = null;
    }

    function paintDataModelMessage(title: string, detail: string, tone = 'neutral') {
        disposeDataModel();
        hostContext.canvasContainer.innerHTML = `
            <section class="etlsql-studio-dag-view" data-model-view>
                <header class="etlsql-studio-dag-head">
                    <div><strong>Data model</strong><span>${_escapeHtml(detail)}</span></div>
                    <span class="etlsql-studio-dag-status is-${_escapeHtml(tone)}" data-model-status>${_escapeHtml(title)}</span>
                </header>
                <div class="etlsql-studio-empty-guidance"><strong>${_escapeHtml(title)}</strong><span>${_escapeHtml(detail)}</span></div>
            </section>`;
    }

    /** The edge label carries the evidence, because the reader has no other way to weigh the edge. */
    function dataModelEdgeLabel(relationship: DataModelRelationshipDto) {
        if (relationship.kind === 'derivation') return 'builds';
        if (relationship.kind === 'membership') return '';
        const cardinality = DATA_MODEL_CARDINALITY_LABEL[relationship.cardinality] || relationship.cardinality;
        if (relationship.kind === 'foreign-key') return `FK · ${cardinality}`;
        return `${relationship.fromColumn || ''} = ${relationship.toColumn || ''} · ${cardinality}`.trim();
    }

    function dataModelSummaryMarkup(model: DataModelResponse) {
        const counts = model.entities.reduce<Record<string, number>>((totals, entity) => {
            totals[entity.kind] = (totals[entity.kind] || 0) + 1;
            return totals;
        }, {});
        const joins = model.relationships.filter(item => item.kind === 'join').length;
        const declared = model.relationships.filter(item => item.kind === 'foreign-key').length;
        const stated = model.relationships.filter(item => item.kind === 'join' && item.cardinality !== 'unknown').length;
        const parts = [
            `${counts.table || 0} table${counts.table === 1 ? '' : 's'}`,
            `${counts.temp || 0} #temp`,
            `${counts.cte || 0} CTE${counts.cte === 1 ? '' : 's'}`,
            `${joins} join${joins === 1 ? '' : 's'}`,
            `${declared} declared foreign key${declared === 1 ? '' : 's'}`,
        ];
        const evidence = model.hasSchemaEvidence
            ? `${stated} of ${joins} join${joins === 1 ? '' : 's'} have a cardinality the database states; the rest are not stated by it.`
            : 'No database keys were available, so no cardinality is stated. That is an absence of evidence, not a finding about the data.';
        return `<div class="etlsql-studio-model-summary"><span>${_escapeHtml(parts.join(' · '))}</span><small>${_escapeHtml(evidence)}</small></div>`;
    }

    async function renderDataModelView(doc: StudioRuntimeDocument, content: string) {
        const context = hostContext.documentContext(doc);
        const revision = ++context.modelRevision;
        paintDataModelMessage('Reading the script…', 'Connections, tables, and the relationships between them.');

        let model;
        try {
            model = await designerApiJson<DataModelResponse>(STUDIO_ROUTES.dataModel, {
                script: content,
                documentUri: doc.path || doc.id,
            });
        } catch (error) {
            if (revision !== context.modelRevision || hostContext.getActiveDoc() !== doc) return;
            paintDataModelMessage('The data model could not be read', errorMessage(error) || String(error), 'error');
            return;
        }
        if (revision !== context.modelRevision || hostContext.getActiveDoc() !== doc) return;

        if (!model?.parsed) {
            paintDataModelMessage(
                'The script does not parse yet',
                model?.error || 'Fix the script and the model will redraw.',
                'warning');
            return;
        }
        if (!model.entities?.length) {
            paintDataModelMessage(
                'Nothing to model yet',
                'Add a connection and a query, and this view will show what they read and build.');
            return;
        }

        disposeDataModel();
        hostContext.canvasContainer.innerHTML = `
            <section class="etlsql-studio-dag-view" data-model-view>
                <header class="etlsql-studio-dag-head">
                    <div>
                        <strong>Data model</strong>
                        <span>${model.entities.length} entit${model.entities.length === 1 ? 'y' : 'ies'} · ${model.relationships.length} relationship${model.relationships.length === 1 ? '' : 's'}</span>
                    </div>
                    <span class="etlsql-studio-dag-status is-${model.hasSchemaEvidence ? 'success' : 'neutral'}" data-model-status>${
                        model.hasSchemaEvidence ? 'Script and database evidence' : 'Script evidence only'}</span>
                </header>
                ${dataModelSummaryMarkup(model)}
                <div class="etlsql-studio-dag-canvas" data-model-canvas></div>
            </section>`;

        hostContext.state.dataModelInstance = renderDag(
            queryElement(hostContext.canvasContainer, '[data-model-canvas]'),
            {
                nodes: model.entities.map(entity => ({
                    id: entity.id,
                    label: entity.name,
                    type: DATA_MODEL_NODE_TYPE[entity.kind] || 'table',
                    meta: {
                        line: entity.line,
                        kind: entity.kind,
                        connection: entity.connection,
                        detail: entity.detail,
                        keys: entity.columns.filter(column => column.isKey).map(column => column.name).join(', '),
                    },
                })),
                edges: model.relationships.map(relationship => ({
                    source: relationship.from,
                    target: relationship.to,
                    label: dataModelEdgeLabel(relationship),
                })),
            },
            {
                theme: document.body.classList.contains('theme-dark') ? 'vscode' : 'portal',
                orientation: 'horizontal',
                onNodeClick: (_id, meta: any) => {
                    const line = Number(meta?.line ?? meta?.Line);
                    if (!line || Number.isNaN(line)) return;
                    hostContext.setProjection('split');
                    hostContext.state.editorInstance?.gotoLine?.(line);
                },
            });
    }

    function renderVisualStage() {
        const doc = hostContext.getActiveDoc();
        if (!doc) return;
        const content = hostContext.state.editorInstance ? hostContext.state.editorInstance.getValue() : doc.content;

        // The model is a projection of the same script, so it is chosen before the file kind: an
        // author asking for the model of a report wants the model of a report, not its canvas.
        if (doc.projection === 'model') {
            hostContext.renderReportWorkflowChrome(null);
            hostContext.disposePipelineDag();
            if (hostContext.state.designerInstance) {
                hostContext.state.designerInstance.dispose?.();
                hostContext.state.designerInstance = null;
            }
            void renderDataModelView(doc, content);
            return;
        }
        disposeDataModel();

        const isEtl = (doc.path || '').endsWith('.etlsql') || content.includes('TRANSFORM ') || content.includes('MERGE INTO');

        if (isEtl) {
            hostContext.renderReportWorkflowChrome(null);
            if (hostContext.state.designerInstance) {
                hostContext.state.designerInstance.dispose?.();
                hostContext.state.designerInstance = null;
            }
            void hostContext.renderPipelineDag(doc, content);
        } else {
            hostContext.disposePipelineDag();
            if (!hostContext.state.designerInstance) {
                hostContext.canvasContainer.innerHTML = '';
                hostContext.state.designerInstance = createDesigner(hostContext.canvasContainer, {
                    reportName: doc.name,
                    script: content,
                    initialScript: content,
                    // The designer is constructed once per session but the buffer keeps changing, so
                    // it must ask for the current text rather than keep the text it was built with.
                    getScript: () => hostContext.activeScriptText(),
                    hideTopbar: true,
                    hideSidebar: true,
                    propertiesHost: hostContext.propertiesHost,
                    snapshotMode: true,
                    snapshotPackage: hostContext.activeDocumentContext().snapshotPackage as any,
                    requireDataFirst: true,
                    canAddVisual: hostContext.hasDataSample,
                    // Dragging a card onto the canvas has to bind the same sample the palette's own
                    // click path binds. Without this the canvas added a source-less visual, which
                    // cannot be written as ETL-SQL - the card appeared, the script never changed,
                    // and the visual was gone on the next reload.
                    defaultVisualBinding: () => hostContext.visualSourceBinding(),
                    onRequestData: () => { hostContext.runChooseDataStep(); },
                    onAddVisualBlocked: () => {
                        hostContext.setActivity('catalog');
                        _feedback.notify('Choose a connection and table before adding a visual.', { title: 'Data required', tone: 'info' });
                    },
                    apiBase: hostContext.apiBase,
                    authFetch: hostContext.authFetch,
                    previewUrl: hostContext.opts.previewUrl || '/designer-preview.html',
                    getDatasetColumns: () => _snapshotColumns(hostContext.activeDocumentContext().snapshot).map(_columnName),
                    // The outline's lock. The designer asks rather than being told, so a lock
                    // toggled while a card is on screen takes effect on the next interaction
                    // without the panel having to push anything into the canvas.
                    isVisualLocked: (visual: any) => hostContext.isVisualLocked(visual),
                    onVisualSelect: visualId => {
                        hostContext.state.selectedVisualId = visualId || null;
                        // The outline is itself a selection surface. Switching the rail to the
                        // visual library on every selection would close the panel the author just
                        // clicked in, so while the outline is open it keeps the rail and repaints
                        // its own highlight instead — which is also what makes a canvas click show
                        // up in the tree.
                        if (hostContext.state.activeActivity === 'outline') {
                            hostContext.inspector.style.display = 'none';
                            hostContext.sidebarContent.style.display = '';
                            hostContext.renderOutlineTree();
                            return;
                        }
                        if (!visualId) {
                            hostContext.inspector.style.display = 'none';
                            hostContext.sidebarContent.style.display = '';
                            return;
                        }
                        if (hostContext.state.activeActivity !== 'palette') hostContext.setActivity('palette');
                        showVisualProperties();
                    },
                    onScriptChange: (newScript) => {
                        if (hostContext.state.editorInstance && hostContext.state.editorInstance.getValue() !== newScript) {
                            hostContext.isSyncingFromDesigner = true;
                            hostContext.state.editorInstance.setValue(newScript);
                            setTimeout(() => { hostContext.isSyncingFromDesigner = false; }, 100);
                        }
                        doc.content = newScript;
                        doc.isDirty = true;
                        hostContext.scheduleDraftSave(doc);
                        hostContext.renderTabs();
                        if (hostContext.state.activeActivity === 'outline') {
                            hostContext.renderOutlineTree();
                        } else if (hostContext.state.selectedVisualId) {
                            showVisualProperties();
                        } else if (hostContext.state.activeActivity === 'palette' || hostContext.state.activeActivity === 'catalog') {
                            hostContext.renderSidebarContent(hostContext.state.activeActivity);
                        } else if (hostContext.state.filterSidebarOpen) {
                            hostContext.renderFilterPanel();
                        }
                    }
                });
            } else {
                hostContext.state.designerInstance.applyScriptText(content);
            }
            hostContext.renderReportWorkflowChrome(doc);
        }
    }

    function hasCapability(capability: string) {
        return hostContext.host.hasCapability(hostContext.state, capability);
    }

    function showVisualProperties() {
        hostContext.sidebarTitle.textContent = 'Chart properties';
        hostContext.sidebarContent.style.display = 'none';
        hostContext.inspector.style.display = 'flex';
        const columns = _snapshotColumns(hostContext.activeDocumentContext().snapshot).map(_columnName);
        hostContext.propertyFields.innerHTML = columns.length
            ? `<strong>Data fields</strong><span>Click a field to fill the next empty chart role, or drag it onto a role.</span><div>${columns.map(column => `<button type="button" draggable="true" data-property-field="${_escapeHtml(column)}">${_escapeHtml(column)}</button>`).join('')}</div>`
            : '<div class="etlsql-studio-empty-compact">Load data to assign chart fields.</div>';
        queryElements(hostContext.propertyFields, '[data-property-field]').forEach(button => {
            button.addEventListener('dragstart', (event: DragEvent) => {
                event.dataTransfer?.setData('application/x-etlsql-field', button.dataset.propertyField || '');
                event.dataTransfer?.setData('text/plain', button.dataset.propertyField || '');
            });
            button.addEventListener('click', () => assignFieldToProperty(button.dataset.propertyField || ''));
        });
    }

    /**
     * Shows the report-level inspector: theme, palette colours, and the report title.
     *
     * It is the same panel the designer renders when nothing is selected — deselecting is what
     * produces it. Studio hides the inspector on an empty selection, so the panel was written,
     * wired, and unreachable; this is the door to it rather than a second copy of it.
     */
    function showReportProperties() {
        hostContext.state.designerInstance?.selectVisual?.(null);
        hostContext.sidebarTitle.textContent = 'Report style';
        hostContext.propertyFields.innerHTML = '';
        hostContext.sidebarContent.style.display = 'none';
        hostContext.inspector.style.display = 'flex';
    }

    function assignFieldToProperty(field: string, explicitTarget: HTMLInputElement | null = null) {
        if (!field) return;
        const inputs = [...hostContext.propertiesHost.querySelectorAll<HTMLInputElement>('input[data-role]')];
        const target = explicitTarget || inputs.find(input => !input.value.trim()) || inputs[0];
        if (!target) {
            _feedback.notify('This visual has no field roles to assign.', { title: 'No chart role', tone: 'info' });
            return;
        }
        target.value = field;
        target.dispatchEvent(new Event('input', { bubbles: true }));
        target.dispatchEvent(new Event('change', { bubbles: true }));
        target.focus();
    }

    async function designerApiJson<T = StudioDynamic>(path: string, body: unknown): Promise<T> {
        const response = await hostContext.authFetch(hostContext.apiBase + path, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (!response) throw new Error('The Studio session ended during the script update.');
        if (!response.ok) {
            const problem = await response.json().catch(() => ({})) as Record<string, unknown>;
            throw new Error(typeof problem.error === 'string' ? problem.error : `Designer update failed (${response.status}).`);
        }
        return await response.json() as T;
    }

    return { renderVisualStage, hasCapability, showReportProperties, assignFieldToProperty, designerApiJson };
}
