/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * ETL-SQL Studio — Flagship Unified Dual-Projection Visual & Script Workbench
 *
 * Exported functions:
 *   createStudioWorkbench(container, options)
 */
import { _feedback, _studioIcon, closestElement, getStoredProjectionPreference, queryElement, queryElements, storeProjectionPreference } from './studio-context.js';
import { createStudioNavigation } from './studio-navigation.js';
import { createScriptResultsPanel } from './designer.js';
import { createStudioAuthoringSurfaces } from './studio-authoring.js';
import { STUDIO_CATALOG_ROUTES, STUDIO_ROUTES } from './studio-contracts.js';
import { createStudioDataPanel } from './studio-data-panel.js';
import { createStudioDocumentSession } from './studio-document-session.js';
import { createStudioDocumentTabs } from './studio-document-tabs.js';
import { createStudioEditorHost } from './studio-editor-host.js';
import { createStudioEnginePanel } from './studio-engine-panel.js';
import { createStudioFileCommands } from './studio-file-commands.js';
import { createStudioFilterPanel } from './studio-filter-panel.js';
import { createStudioGovernanceDatasets } from './studio-governance-datasets.js';
import { createStudioGovernanceQuality } from './studio-governance-quality.js';
import { createStudioGovernanceSecurity } from './studio-governance-security.js';
import { createStudioGovernanceTags } from './studio-governance-tags.js';
import { createStudioHostAdapter } from './studio-host.js';
import { createStudioLeaseLifecycle } from './studio-lifecycle.js';
import { createStudioOutline } from './studio-outline.js';
import { createStudioPipelineView } from './studio-pipeline-view.js';
import { createStudioReportWorkflow } from './studio-report-workflow.js';
import { createStudioRunSession } from './studio-run-session.js';
import { declareSharedConnections } from './studio-shared-connections.js';
import { createStudioSqlMutationService } from './studio-sql-mutations.js';
import { createStudioContextStore, createStudioState } from './studio-state.js';
import { createStudioSyntaxBridge } from './studio-syntax-bridge.js';
import { createStudioVisualStage } from './studio-visual-stage.js';
import { createStudioWorkspaceExplorer } from './studio-workspace-explorer.js';
/// <reference path="../../../../../types/browser-globals.d.ts" />
export { secureStudioScriptForSave } from './studio-security.js';
export async function createStudioWorkbench(container, opts = {}) {
    // Getters keep sibling callbacks and shared state live as the shell initializes.
    // Controllers own private state; creating them must not read these deferred bindings.
    const { mountShell, applySplitPct, setupModalAccessibility, setProjection, renderStudioHome, setFilterSidebar, setContextualRailVisibility, setActivity, renderSidebarContent } = createStudioNavigation({
        get bindWorkspaceExplorer() { return bindWorkspaceExplorer; },
        get codeStage() { return codeStage; },
        get container() { return container; },
        get createNewFile() { return createNewFile; },
        get currentSplitPct() { return currentSplitPct; },
        set currentSplitPct(value) { currentSplitPct = value; },
        get filterSidebar() { return filterSidebar; },
        get getActiveDoc() { return getActiveDoc; },
        get hasWorkspaceHost() { return hasWorkspaceHost; },
        get homeStage() { return homeStage; },
        get inspector() { return inspector; },
        get openCatalogReport() { return openCatalogReport; },
        get openWorkspaceFile() { return openWorkspaceFile; },
        get opts() { return opts; },
        get renderDataWorkflow() { return renderDataWorkflow; },
        get renderEnginePanel() { return renderEnginePanel; },
        get renderFilterPanel() { return renderFilterPanel; },
        get renderGitSidebar() { return renderGitSidebar; },
        get renderGovernancePanel() { return renderGovernancePanel; },
        get renderOutlineTree() { return renderOutlineTree; },
        get renderVisualLibrary() { return renderVisualLibrary; },
        get renderVisualStage() { return renderVisualStage; },
        get resizer() { return resizer; },
        get shell() { return shell; },
        get sidebar() { return sidebar; },
        get sidebarContent() { return sidebarContent; },
        get sidebarTitle() { return sidebarTitle; },
        get state() { return state; },
        get switchDoc() { return switchDoc; },
        get visualStage() { return visualStage; },
        get workspaceTreeMarkup() { return workspaceTreeMarkup; },
    });
    const { updateSnapshotPackage, setDocumentTrace, paintResults, setDocumentDiagnostics, updateRunControls, handleStopRun, executeRun } = createStudioRunSession({
        get activeDocumentContext() { return activeDocumentContext; },
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get documentContext() { return documentContext; },
        get getActiveDoc() { return getActiveDoc; },
        get shell() { return shell; },
        get state() { return state; },
    });
    const { explicitReportWorkflow, ensureReportWorkflow, guidedRailToggleMarkup, wireGuidedRailToggle, renderReportWorkflowChrome } = createStudioReportWorkflow({
        get activeDocumentContext() { return activeDocumentContext; },
        get canonicalDesignerMutation() { return canonicalDesignerMutation; },
        get designerApiJson() { return designerApiJson; },
        get getActiveDoc() { return getActiveDoc; },
        get hasDataSample() { return hasDataSample; },
        get modalBackdrop() { return modalBackdrop; },
        get modalBox() { return modalBox; },
        get renderSidebarContent() { return renderSidebarContent; },
        get runChooseDataStep() { return runChooseDataStep; },
        get runCrossFilterStep() { return runCrossFilterStep; },
        get runDetailsStep() { return runDetailsStep; },
        get runExportStep() { return runExportStep; },
        get runFurnitureStep() { return runFurnitureStep; },
        get runParameterStep() { return runParameterStep; },
        get runPreviewStep() { return runPreviewStep; },
        get runTotalsStep() { return runTotalsStep; },
        get runVisualsStep() { return runVisualsStep; },
        get setProjection() { return setProjection; },
        get setupModalAccessibility() { return setupModalAccessibility; },
        get state() { return state; },
        get visualStage() { return visualStage; },
        get workflowBar() { return workflowBar; },
    });
    const { disposePipelineDag, renderPipelineDag } = createStudioPipelineView({
        get activeScriptText() { return activeScriptText; },
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get canonicalPipelineMutation() { return canonicalPipelineMutation; },
        get canvasContainer() { return canvasContainer; },
        get documentContext() { return documentContext; },
        get executeRun() { return executeRun; },
        get getActiveDoc() { return getActiveDoc; },
        get openPipelineRunPlanConfirm() { return openPipelineRunPlanConfirm; },
        get openPipelineTaskEditor() { return openPipelineTaskEditor; },
        get renderVisualStage() { return renderVisualStage; },
        get setProjection() { return setProjection; },
        get state() { return state; },
    });
    const { renderVisualStage, hasCapability, showReportProperties, assignFieldToProperty, designerApiJson } = createStudioVisualStage({
        get activeDocumentContext() { return activeDocumentContext; },
        get activeScriptText() { return activeScriptText; },
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get canvasContainer() { return canvasContainer; },
        get disposePipelineDag() { return disposePipelineDag; },
        get documentContext() { return documentContext; },
        get getActiveDoc() { return getActiveDoc; },
        get hasDataSample() { return hasDataSample; },
        get host() { return host; },
        get inspector() { return inspector; },
        get isSyncingFromDesigner() { return isSyncingFromDesigner; },
        set isSyncingFromDesigner(value) { isSyncingFromDesigner = value; },
        get isVisualLocked() { return isVisualLocked; },
        get opts() { return opts; },
        get propertiesHost() { return propertiesHost; },
        get propertyFields() { return propertyFields; },
        get renderFilterPanel() { return renderFilterPanel; },
        get renderOutlineTree() { return renderOutlineTree; },
        get renderPipelineDag() { return renderPipelineDag; },
        get renderReportWorkflowChrome() { return renderReportWorkflowChrome; },
        get renderSidebarContent() { return renderSidebarContent; },
        get renderTabs() { return renderTabs; },
        get runChooseDataStep() { return runChooseDataStep; },
        get scheduleDraftSave() { return scheduleDraftSave; },
        get setActivity() { return setActivity; },
        get setProjection() { return setProjection; },
        get sidebarContent() { return sidebarContent; },
        get sidebarTitle() { return sidebarTitle; },
        get state() { return state; },
        get visualSourceBinding() { return visualSourceBinding; },
    });
    const { openSyntaxBridge, closeSyntaxBridge, offerUndo, authoringRequest, addVisualToCanvas, duplicateVisual, deleteVisual, surgicalPatchVisualOption, surgicalPatchVisualMapping } = createStudioSyntaxBridge({
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get canonicalDesignerMutation() { return canonicalDesignerMutation; },
        get findDesignerVisual() { return findDesignerVisual; },
        get getActiveDoc() { return getActiveDoc; },
        get hasDataSample() { return hasDataSample; },
        get inspector() { return inspector; },
        get setActivity() { return setActivity; },
        get setProjection() { return setProjection; },
        get state() { return state; },
        get switchDoc() { return switchDoc; },
        get syntaxBridgeBtn() { return syntaxBridgeBtn; },
        get syntaxBridgePanel() { return syntaxBridgePanel; },
        get uniqueVisualName() { return uniqueVisualName; },
        get visualSourceBinding() { return visualSourceBinding; },
    });
    const { renderTabs, updateTabOverflowState, toggleTabDropdown } = createStudioDocumentTabs({
        get closeDoc() { return closeDoc; },
        get getActiveDoc() { return getActiveDoc; },
        get opts() { return opts; },
        get renderSidebarContent() { return renderSidebarContent; },
        get scrollLeftBtn() { return scrollLeftBtn; },
        get scrollRightBtn() { return scrollRightBtn; },
        get shell() { return shell; },
        get state() { return state; },
        get switchDoc() { return switchDoc; },
        get tabDropdown() { return tabDropdown; },
        get tabOverflowBtn() { return tabOverflowBtn; },
        get tabsContainer() { return tabsContainer; },
    });
    const { switchDoc, closeDoc, handlePublishCatalogReport, createNewFile, openCatalogReport, openWorkspaceFile } = createStudioDocumentSession({
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get authoringRequest() { return authoringRequest; },
        get checkRecoverableDraft() { return checkRecoverableDraft; },
        get codeMirrorDebounce() { return codeMirrorDebounce; },
        set codeMirrorDebounce(value) { codeMirrorDebounce = value; },
        get documentContext() { return documentContext; },
        get ensureReportWorkflow() { return ensureReportWorkflow; },
        get getActiveDoc() { return getActiveDoc; },
        get handleSave() { return handleSave; },
        get hasCapability() { return hasCapability; },
        get homeStage() { return homeStage; },
        get isSettingDocumentContent() { return isSettingDocumentContent; },
        set isSettingDocumentContent(value) { isSettingDocumentContent = value; },
        get leaseLifecycle() { return leaseLifecycle; },
        get modalBackdrop() { return modalBackdrop; },
        get modalBox() { return modalBox; },
        get opts() { return opts; },
        get paintResults() { return paintResults; },
        get rememberLineEnding() { return rememberLineEnding; },
        get renderReportWorkflowChrome() { return renderReportWorkflowChrome; },
        get renderSidebarContent() { return renderSidebarContent; },
        get renderStudioHome() { return renderStudioHome; },
        get renderTabs() { return renderTabs; },
        get renderVisualStage() { return renderVisualStage; },
        get setContextualRailVisibility() { return setContextualRailVisibility; },
        get setProjection() { return setProjection; },
        get setupModalAccessibility() { return setupModalAccessibility; },
        get state() { return state; },
        get updateRunControls() { return updateRunControls; },
        get updateSnapshotPackage() { return updateSnapshotPackage; },
        get withLineEnding() { return withLineEnding; },
    });
    const { reportTreeMarkup, isVisualLocked, renderOutlineTree } = createStudioOutline({
        get canonicalDesignerMutation() { return canonicalDesignerMutation; },
        get findDesignerVisual() { return findDesignerVisual; },
        get getActiveDoc() { return getActiveDoc; },
        get sidebarContent() { return sidebarContent; },
        get sidebarTitle() { return sidebarTitle; },
        get state() { return state; },
        get surgicalPatchVisualOption() { return surgicalPatchVisualOption; },
    });
    const { promoteFilterToSlicer, openFilterSetupDialog, renderFilterPanel } = createStudioFilterPanel({
        get activeDocumentContext() { return activeDocumentContext; },
        get canonicalDesignerMutation() { return canonicalDesignerMutation; },
        get composeFilteredSource() { return composeFilteredSource; },
        get designerApiJson() { return designerApiJson; },
        get filterContract() { return filterContract; },
        get filterSidebarContent() { return filterSidebarContent; },
        get matchingFilters() { return matchingFilters; },
        get modalBackdrop() { return modalBackdrop; },
        get modalBox() { return modalBox; },
        get persistFilter() { return persistFilter; },
        get resolveFilterTarget() { return resolveFilterTarget; },
        get setActivity() { return setActivity; },
        get setFilterSidebar() { return setFilterSidebar; },
        get setupModalAccessibility() { return setupModalAccessibility; },
        get state() { return state; },
        get uniqueVisualName() { return uniqueVisualName; },
        get updateSnapshotPackage() { return updateSnapshotPackage; },
    });
    const { synchronizeCodeToCanvas, renderDataWorkflow, loadConnectionAliases, sharedConnectionList, renderVisualLibrary } = createStudioDataPanel({
        get activeDocumentContext() { return activeDocumentContext; },
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get documentContext() { return documentContext; },
        get explicitReportWorkflow() { return explicitReportWorkflow; },
        get getActiveDoc() { return getActiveDoc; },
        get guidedRailToggleMarkup() { return guidedRailToggleMarkup; },
        get handleOpenConnectionWizard() { return handleOpenConnectionWizard; },
        get hasDataSample() { return hasDataSample; },
        get hasWorkspaceHost() { return hasWorkspaceHost; },
        get openChartBuilder() { return openChartBuilder; },
        get openDataWizard() { return openDataWizard; },
        get openFilterSetupDialog() { return openFilterSetupDialog; },
        get renderFilterPanel() { return renderFilterPanel; },
        get renderReportWorkflowChrome() { return renderReportWorkflowChrome; },
        get renderSidebarContent() { return renderSidebarContent; },
        get reportTreeMarkup() { return reportTreeMarkup; },
        get runChooseDataStep() { return runChooseDataStep; },
        get showReportProperties() { return showReportProperties; },
        get sidebarContent() { return sidebarContent; },
        get sidebarTitle() { return sidebarTitle; },
        get state() { return state; },
        get updateSnapshotPackage() { return updateSnapshotPackage; },
        get wireGuidedRailToggle() { return wireGuidedRailToggle; },
    });
    const { workspaceTreeMarkup, bindWorkspaceExplorer } = createStudioWorkspaceExplorer({
        get openWorkspaceFile() { return openWorkspaceFile; },
        get opts() { return opts; },
        get renderSidebarContent() { return renderSidebarContent; },
        get renderStudioHome() { return renderStudioHome; },
        get renderTabs() { return renderTabs; },
        get sidebarContent() { return sidebarContent; },
        get state() { return state; },
        get switchDoc() { return switchDoc; },
    });
    const { renderEnginePanel, applyDiagnosticQuickFix } = createStudioEnginePanel({
        get activeScriptText() { return activeScriptText; },
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get designerApiJson() { return designerApiJson; },
        get getActiveDoc() { return getActiveDoc; },
        get offerUndo() { return offerUndo; },
        get renderTabs() { return renderTabs; },
        get sidebarContent() { return sidebarContent; },
        get sidebarTitle() { return sidebarTitle; },
        get state() { return state; },
    });
    const { renderGovernancePanel, paintGovernancePanel } = createStudioGovernanceTags({
        get activeScriptText() { return activeScriptText; },
        get bindGovernanceDatasets() { return bindGovernanceDatasets; },
        get bindGovernanceQuality() { return bindGovernanceQuality; },
        get bindGovernanceSchedule() { return bindGovernanceSchedule; },
        get bindGovernanceSecurity() { return bindGovernanceSecurity; },
        get canonicalScriptMutation() { return canonicalScriptMutation; },
        get designerApiJson() { return designerApiJson; },
        get getActiveDoc() { return getActiveDoc; },
        get governanceDatasetsMarkup() { return governanceDatasetsMarkup; },
        get governanceRoutingMarkup() { return governanceRoutingMarkup; },
        get governanceRulesMarkup() { return governanceRulesMarkup; },
        get governanceScheduleMarkup() { return governanceScheduleMarkup; },
        get governanceSecurityMarkup() { return governanceSecurityMarkup; },
        get loadPreviewAsVocabulary() { return loadPreviewAsVocabulary; },
        get sidebarContent() { return sidebarContent; },
        get sidebarTitle() { return sidebarTitle; },
        get state() { return state; },
    });
    const { governanceRulesMarkup, governanceRoutingMarkup, bindGovernanceQuality } = createStudioGovernanceQuality({
        get canonicalScriptMutation() { return canonicalScriptMutation; },
        get paintGovernancePanel() { return paintGovernancePanel; },
        get sidebarContent() { return sidebarContent; },
        get state() { return state; },
    });
    const { governanceSecurityMarkup, bindGovernanceSecurity, loadPreviewAsVocabulary } = createStudioGovernanceSecurity({
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get paintGovernancePanel() { return paintGovernancePanel; },
        get shell() { return shell; },
        get sidebarContent() { return sidebarContent; },
        get state() { return state; },
    });
    const { governanceDatasetsMarkup, bindGovernanceDatasets, governanceScheduleMarkup, bindGovernanceSchedule } = createStudioGovernanceDatasets({
        get canonicalScriptMutation() { return canonicalScriptMutation; },
        get getActiveDoc() { return getActiveDoc; },
        get paintGovernancePanel() { return paintGovernancePanel; },
        get sidebarContent() { return sidebarContent; },
        get state() { return state; },
    });
    const { renderGitSidebar, handleSave, rememberLineEnding, withLineEnding, handleOpenConnectionWizard, handleFormatDocument, handleExitStudio } = createStudioFileCommands({
        get activeScriptText() { return activeScriptText; },
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get documentContext() { return documentContext; },
        get getActiveDoc() { return getActiveDoc; },
        get handlePublishCatalogReport() { return handlePublishCatalogReport; },
        get hasCapability() { return hasCapability; },
        get hasGitHost() { return hasGitHost; },
        get leaseLifecycle() { return leaseLifecycle; },
        get modalBackdrop() { return modalBackdrop; },
        get modalBox() { return modalBox; },
        get opts() { return opts; },
        get renderTabs() { return renderTabs; },
        get renderVisualStage() { return renderVisualStage; },
        get scheduleDraftSave() { return scheduleDraftSave; },
        get setupModalAccessibility() { return setupModalAccessibility; },
        get shell() { return shell; },
        get sidebarContent() { return sidebarContent; },
        get sidebarTitle() { return sidebarTitle; },
        get state() { return state; },
    });
    const { mountScriptEditor } = createStudioEditorHost({
        get apiBase() { return apiBase; },
        get authFetch() { return authFetch; },
        get codeMirrorDebounce() { return codeMirrorDebounce; },
        set codeMirrorDebounce(value) { codeMirrorDebounce = value; },
        get codeStage() { return codeStage; },
        get documentContext() { return documentContext; },
        get editorHost() { return editorHost; },
        get getActiveDoc() { return getActiveDoc; },
        get isSettingDocumentContent() { return isSettingDocumentContent; },
        set isSettingDocumentContent(value) { isSettingDocumentContent = value; },
        get isSyncingFromDesigner() { return isSyncingFromDesigner; },
        set isSyncingFromDesigner(value) { isSyncingFromDesigner = value; },
        get renderTabs() { return renderTabs; },
        get renderVisualStage() { return renderVisualStage; },
        get scheduleDraftSave() { return scheduleDraftSave; },
        get setDocumentDiagnostics() { return setDocumentDiagnostics; },
        get shell() { return shell; },
        get state() { return state; },
        get synchronizeCodeToCanvas() { return synchronizeCodeToCanvas; },
    });
    const savedTheme = localStorage.getItem('portal-theme') || 'dark';
    if (savedTheme === 'dark') {
        document.body.classList.add('theme-dark');
    }
    else {
        document.body.classList.remove('theme-dark');
    }
    const catalogMode = Boolean(opts.onOpenDocument);
    const pipelinesBlocked = catalogMode && !opts.catalogPipelines;
    const host = createStudioHostAdapter(opts);
    const { authFetch, apiBase, hasWorkspaceHost, hasGitHost } = host;
    const state = createStudioState(opts);
    const documents = state.documents;
    const contexts = createStudioContextStore(documents, opts.initialSnapshot);
    const documentContext = contexts.forDocument;
    function activeDocumentContext() {
        return documentContext(getActiveDoc());
    }
    const leaseLifecycle = createStudioLeaseLifecycle({
        state: state,
        options: opts,
        documentContext: documentContext,
        feedback: _feedback
    });
    // Two seconds after typing stops, the draft goes to the host: long enough not to send every
    // keystroke, short enough that a crash or an expired sign-in costs almost nothing.
    let draftSaveDebounce = null;
    let warnedDraftRefused = false;
    function scheduleDraftSave(doc) {
        if (!doc || !doc.isDirty || !leaseLifecycle.draftsKept)
            return;
        if (draftSaveDebounce)
            clearTimeout(draftSaveDebounce);
        draftSaveDebounce = setTimeout(async () => {
            const result = await leaseLifecycle.saveDraft(doc);
            if (result === 'refused' && !warnedDraftRefused) {
                warnedDraftRefused = true;
                _feedback.notify(`"${doc.name}" holds a plaintext credential, so its unsaved edits are not kept as a recovery draft. Encrypt the credential or use a SECRET: reference.`, { title: 'Draft Not Kept', tone: 'warning' });
            }
        }, 2000);
    }
    async function checkRecoverableDraft(doc) {
        if (!doc)
            return;
        const draft = await leaseLifecycle.getRecoverableDraft(doc);
        if (draft && draft.content && draft.content !== doc.content) {
            // A draft started from an older version than the one just opened would put back
            // text that someone has since changed; say so rather than restoring it silently.
            const moved = draft.baseVersion != null && doc.version != null && String(draft.baseVersion) !== String(doc.version)
                || draft.baseSourceRevision != null && doc.sourceRevision != null && String(draft.baseSourceRevision) !== String(doc.sourceRevision);
            _feedback.notify(moved
                ? `Unsaved edits to "${doc.name}" from a previous session were kept, but the report has changed since. Restoring puts your edits back over the newer version; saving will then ask you to resolve the conflict.`
                : `Unsaved edits to "${doc.name}" from a previous session were kept.`, {
                title: 'Recover Draft',
                tone: 'info',
                action: {
                    label: 'Restore',
                    onSelect: () => {
                        doc.content = draft.content;
                        doc.isDirty = true;
                        if (state.editorInstance && getActiveDoc() === doc) {
                            state.editorInstance.setValue(draft.content);
                        }
                        renderTabs();
                        renderVisualStage();
                        _feedback.notify(`Draft restored for "${doc.name}".`, { title: 'Draft Restored', tone: 'success' });
                    }
                }
            });
        }
    }
    let isSyncingFromDesigner = false;
    let isSettingDocumentContent = false;
    let codeMirrorDebounce = null;
    mountShell();
    const shell = queryElement(container, '.etlsql-studio-shell');
    const tabsContainer = queryElement(shell, '[data-studio-tabs]');
    const scrollLeftBtn = queryElement(shell, '[data-studio-scroll="left"]');
    const scrollRightBtn = queryElement(shell, '[data-studio-scroll="right"]');
    const tabOverflowBtn = queryElement(shell, '[data-studio-overflow-btn]');
    const tabDropdown = queryElement(shell, '[data-studio-tab-dropdown]');
    const newTabBtn = queryElement(shell, '[data-studio-new-tab]');
    const sidebar = queryElement(shell, '[data-studio-sidebar]');
    const sidebarTitle = queryElement(shell, '[data-sidebar-title]');
    const sidebarContent = queryElement(shell, '[data-sidebar-content]');
    const filterSidebar = queryElement(shell, '[data-filter-sidebar]');
    const filterSidebarContent = queryElement(shell, '[data-filter-sidebar-content]');
    const inspector = queryElement(shell, '[data-studio-inspector]');
    const propertiesHost = queryElement(shell, '[data-properties-host]');
    const propertyFields = queryElement(shell, '[data-property-fields]');
    const homeStage = queryElement(shell, '[data-home-stage]');
    const visualStage = queryElement(shell, '[data-visual-stage]');
    const workflowBar = queryElement(shell, '[data-workflow-bar]');
    const codeStage = queryElement(shell, '[data-code-stage]');
    const syntaxBridgeBtn = queryElement(shell, '[data-action="syntax-bridge"]');
    const syntaxBridgePanel = queryElement(shell, '[data-syntax-bridge]');
    const resizer = queryElement(shell, '[data-stage-resizer]');
    const editorHost = queryElement(shell, '[data-editor-host]');
    const resultsHost = queryElement(shell, '[data-results-host]');
    const canvasContainer = queryElement(shell, '[data-canvas-grid-container]');
    const modalBackdrop = queryElement(shell, '[data-modal-backdrop]');
    const modalBox = queryElement(shell, '[data-modal-box]');
    let currentSplitPct = 50;
    queryElement(inspector, '[data-properties-back]').addEventListener('click', () => {
        state.designerInstance?.selectVisual?.(null);
        renderSidebarContent('palette');
    });
    propertiesHost.addEventListener('dragover', event => {
        if (!closestElement(event, 'input[data-role]') || !event.dataTransfer?.types.includes('application/x-etlsql-field'))
            return;
        event.preventDefault();
    });
    propertiesHost.addEventListener('drop', event => {
        const input = closestElement(event, 'input[data-role]');
        if (!input)
            return;
        event.preventDefault();
        const transfer = event.dataTransfer;
        assignFieldToProperty(transfer?.getData('application/x-etlsql-field') || transfer?.getData('text/plain') || '', input);
    });
    function getActiveDoc() {
        if (state.activeDocId === '__home__')
            return null;
        return state.documents.find(d => d.id === state.activeDocId) || state.documents[0] || null;
    }
    const { canonicalDesignerMutation, canonicalPipelineMutation, canonicalScriptMutation, composeFilteredSource, filterContract, findDesignerVisual, matchingFilters, persistFilter, resolveFilterTarget, uniqueVisualName } = createStudioSqlMutationService({
        state,
        getActiveDocument: getActiveDoc,
        activeDocumentContext,
        designerApiJson,
        routes: STUDIO_ROUTES,
        renderVisualStage,
        renderWorkflow: renderReportWorkflowChrome,
        renderTabs,
        offerUndo,
        declareConnections: (script) => declareSharedConnections(script, sharedConnectionList()),
        feedback: _feedback
    });
    /** Current buffer text, preferring the editor over the last-saved document content. */
    function activeScriptText() {
        return state.editorInstance?.getValue?.() ?? getActiveDoc()?.content ?? '';
    }
    // The guided wizards and steps. Everything they may touch is listed here — the module reaches for
    // nothing else, which is what the authoring contract test enforces.
    const { openDataWizard, openPipelineTaskEditor, openPipelineRunPlanConfirm, openChartBuilder, runChooseDataStep, runParameterStep, runDetailsStep, runTotalsStep, runFurnitureStep, runPreviewStep, runExportStep, runVisualsStep, runCrossFilterStep, hasDataSample, visualSourceBinding } = createStudioAuthoringSurfaces({
        dialog: { backdrop: modalBackdrop, box: modalBox },
        routes: STUDIO_ROUTES,
        catalogRoutes: STUDIO_CATALOG_ROUTES,
        request: authoringRequest,
        editorTransport: { url: (route) => apiBase + route, authFetch },
        getActiveDocument: getActiveDoc,
        activeContext: activeDocumentContext,
        contextFor: documentContext,
        mutate: canonicalDesignerMutation,
        uniqueVisualName,
        hasWorkspaceHost,
        feedback: _feedback,
        shell: {
            getScriptText: activeScriptText,
            // Ranged rather than whole-document, for the same reason the canonical mutations are:
            // the author sees which lines the wizard added, keeps their caret, and gets the Undo
            // offer that only a single reversible transaction can honestly make.
            setScriptText: (written, label = 'That edit') => {
                // A wizard that read a catalog alias also declares it, in the same transaction, so
                // the report runs for readers and schedules and not only in this preview.
                const text = declareSharedConnections(written, sharedConnectionList());
                const doc = getActiveDoc();
                const before = state.editorInstance?.getValue?.();
                const changed = state.editorInstance?.replaceAll?.(text) ?? state.editorInstance?.setValue?.(text);
                if (changed?.from != null)
                    state.editorInstance?.revealRange?.(changed.from, changed.to);
                if (typeof before === 'string')
                    offerUndo(label, { document: doc, before, after: text });
            },
            designerState: () => state.designerInstance?.getState?.(),
            refreshSnapshot: () => state.designerInstance?.refreshSnapshot?.(),
            setActivity,
            setProjection,
            renderSidebar: () => renderSidebarContent(state.activeActivity),
            // Selecting is what opens the Format inspector, so a surface that has finished creating
            // an object hands the author to the one that edits it.
            selectVisual: (name) => {
                if (!name)
                    return;
                if (getActiveDoc()?.projection === 'code')
                    setProjection('split');
                // The canvas selects by id, and a wizard knows the visual it wrote by name. Passing
                // the name straight through opened the inspector while leaving no card selected,
                // which reads as the canvas ignoring the new visual.
                const design = state.designerInstance?.getState?.();
                const visual = (design?.pages || [])
                    .flatMap(page => page.visuals || [])
                    .find(item => item.name === name || item.id === name);
                state.designerInstance?.selectVisual?.(visual?.id || name);
            },
            renderTabs,
            // A guided step that has collected prompt answers runs with them; everything else keeps
            // clicking the toolbar button, which runs the script exactly as written.
            runReport: (parameters) => {
                const doc = getActiveDoc();
                return (parameters && doc
                    ? executeRun(doc, { script: activeScriptText(), label: 'report', parameters })
                    : queryElement(shell, '[data-action="run"]')?.click());
            },
            openConnectionWizard: handleOpenConnectionWizard,
            // The aliases this host will actually let the author read, which is not the same list as
            // the connections the script declares. On the Portal a report normally declares none and
            // reads catalog aliases instead, so a wizard that only knew the script's own found
            // nothing and sent the author off to create a connection they already had.
            availableConnections: loadConnectionAliases
        }
    });
    tabsContainer.addEventListener('scroll', updateTabOverflowState, { passive: true });
    tabsContainer.addEventListener('wheel', (e) => {
        if (e.deltaY !== 0) {
            e.preventDefault();
            tabsContainer.scrollLeft += e.deltaY;
            updateTabOverflowState();
        }
    }, { passive: false });
    scrollLeftBtn?.addEventListener('click', () => {
        tabsContainer.scrollBy({ left: -140, behavior: 'smooth' });
    });
    scrollRightBtn?.addEventListener('click', () => {
        tabsContainer.scrollBy({ left: 140, behavior: 'smooth' });
    });
    tabOverflowBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        toggleTabDropdown();
    });
    const onOutsideClick = (e) => {
        if (tabDropdown && !tabDropdown.hidden && !closestElement(e, '[data-studio-tabbar]')) {
            toggleTabDropdown(false);
        }
    };
    document.addEventListener('click', onOutsideClick);
    // The toolbar has advertised these shortcuts in its tooltips since Studio shipped, but nothing
    // ever bound them. Undo/redo are routed to the editor's history so a canvas action — which is
    // now applied as a ranged text edit — can be undone from anywhere in the workbench.
    const onShellKeyDown = (event) => {
        const mod = event.ctrlKey || event.metaKey;
        if (!mod)
            return;
        const key = event.key.toLowerCase();
        const inEditor = editorHost.contains(event.target);
        if (key === 'n' && !event.shiftKey) {
            event.preventDefault();
            queryElement(shell, '[data-studio-new-tab]')?.click();
            return;
        }
        if (key === 's' && !event.shiftKey) {
            event.preventDefault();
            void handleSave();
            return;
        }
        if (event.key === 'Enter') {
            event.preventDefault();
            const action = event.shiftKey ? 'run' : 'run-selected';
            queryElement(shell, `[data-action="${action}"]`)?.click();
            return;
        }
        // CodeMirror already owns these while it has focus; only handle the case where the author
        // just used the canvas and the editor is not focused.
        if (inEditor)
            return;
        if (key === 'z' && !event.shiftKey) {
            if (state.editorInstance?.undo?.())
                event.preventDefault();
        }
        else if (key === 'y' || (key === 'z' && event.shiftKey)) {
            if (state.editorInstance?.redo?.())
                event.preventDefault();
        }
    };
    document.addEventListener('keydown', onShellKeyDown);
    // Closing a tab already prompts; a browser close did not, so unsaved work could vanish silently.
    const onBeforeUnload = (event) => {
        if (!state.documents.some(doc => doc.isDirty))
            return undefined;
        for (const doc of state.documents) {
            if (doc.isDirty)
                void leaseLifecycle.saveDraft(doc, { keepalive: true });
        }
        event.preventDefault();
        // Browsers show their own wording; a non-empty returnValue is what triggers the prompt.
        event.returnValue = '';
        return '';
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    let tabResizeObserver = null;
    if (typeof ResizeObserver !== 'undefined') {
        tabResizeObserver = new ResizeObserver(() => {
            updateTabOverflowState();
        });
        tabResizeObserver.observe(tabsContainer);
    }
    let newMenuEl = null;
    function closeNewTabMenu() {
        if (newMenuEl) {
            newMenuEl.remove();
            newMenuEl = null;
        }
    }
    newTabBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        if (newMenuEl) {
            closeNewTabMenu();
            return;
        }
        const rect = newTabBtn.getBoundingClientRect();
        newMenuEl = document.createElement('div');
        newMenuEl.className = 'etlsql-tab-new-menu';
        newMenuEl.style.position = 'fixed';
        newMenuEl.style.top = `${rect.bottom + 4}px`;
        newMenuEl.style.left = `${Math.max(10, Math.min(window.innerWidth - 270, rect.left))}px`;
        newMenuEl.style.zIndex = '999999';
        newMenuEl.innerHTML = `
            <button type="button" class="etlsql-tab-new-item" data-new-type="dashboard">
                <span style="color:var(--portal-accent,#388bfd);">${_studioIcon('canvas', 16)}</span>
                <div>
                    <strong>New Dashboard (.rptsql)</strong>
                    <small>Responsive visual canvas</small>
                </div>
            </button>
            <button type="button" class="etlsql-tab-new-item" data-new-type="paginated">
                <span style="color:var(--portal-warning,#d29922);">${_studioIcon('table', 16)}</span>
                <div>
                    <strong>New Paginated Report (.rptsql)</strong>
                    <small>Physical page designer</small>
                </div>
            </button>
            <button type="button" class="etlsql-tab-new-item ${pipelinesBlocked ? 'disabled' : ''}" data-new-type="etl" ${pipelinesBlocked ? 'aria-disabled="true" title="This catalog keeps reports only. Use Workstation Editor."' : ''}>
                <span style="color:var(--portal-success,#2ea043);">${_studioIcon('catalog', 16)}</span>
                <div>
                    <strong>New ETL Pipeline (.etlsql)${pipelinesBlocked ? ' (Workstation only)' : ''}</strong>
                    <small>${pipelinesBlocked ? 'This catalog keeps reports only' : 'Data Movement & DAG Flow'}</small>
                </div>
            </button>
            <button type="button" class="etlsql-tab-new-item ${pipelinesBlocked ? 'disabled' : ''}" data-new-type="sql" ${pipelinesBlocked ? 'aria-disabled="true" title="This catalog keeps reports only. Use Workstation Editor."' : ''}>
                <span style="color:#a371f7;">${_studioIcon('code', 16)}</span>
                <div>
                    <strong>New Script (.etlsql)${pipelinesBlocked ? ' (Workstation only)' : ''}</strong>
                    <small>${pipelinesBlocked ? 'This catalog keeps reports only' : 'Raw SQL Query'}</small>
                </div>
            </button>
        `;
        queryElements(newMenuEl, '[data-new-type]').forEach(btn => {
            btn.addEventListener('click', (ev) => {
                ev.stopPropagation();
                if (btn.getAttribute('aria-disabled') === 'true') {
                    closeNewTabMenu();
                    _feedback.notify('This catalog keeps Report-SQL (.rptsql) documents only. Use the Workstation Editor or VS Code extension for ETL pipeline (.etlsql) authoring.', { title: 'Reports Only', tone: 'info' });
                    return;
                }
                const type = btn.dataset.newType;
                closeNewTabMenu();
                if (type)
                    createNewFile(type);
            });
        });
        document.body.appendChild(newMenuEl);
    });
    document.addEventListener('click', () => closeNewTabMenu());
    queryElements(shell, '[data-projection]').forEach(btn => {
        btn.addEventListener('click', () => {
            const p = btn.dataset.projection;
            if (p) {
                storeProjectionPreference(p);
                setProjection(p);
            }
        });
    });
    queryElements(shell, '.etlsql-studio-rail-btn[data-activity]').forEach(btn => {
        btn.addEventListener('click', () => {
            if (btn.dataset.activity === 'filters')
                setFilterSidebar(!state.filterSidebarOpen);
            else
                setActivity(btn.dataset.activity);
        });
    });
    queryElement(shell, '[data-sidebar-close]')?.addEventListener('click', () => {
        state.sidebarOpen = false;
        sidebar.classList.add('collapsed');
        queryElements(shell, '.etlsql-studio-rail-btn:not([data-activity="filters"])').forEach(b => b.classList.remove('active'));
    });
    queryElement(shell, '[data-filter-sidebar-close]')?.addEventListener('click', () => setFilterSidebar(false));
    queryElements(shell, '[data-add-visual]').forEach(btn => {
        btn.addEventListener('click', () => addVisualToCanvas(btn.dataset.addVisual));
    });
    queryElement(shell, '[data-action="save"]')?.addEventListener('click', handleSave);
    queryElement(shell, '[data-action="publish"]')?.addEventListener('click', () => handlePublishCatalogReport());
    queryElement(shell, '[data-action="exit"]')?.addEventListener('click', handleExitStudio);
    queryElement(shell, '[data-action="syntax-bridge"]')?.addEventListener('click', () => {
        if (syntaxBridgePanel && syntaxBridgePanel.style.display !== 'none') {
            closeSyntaxBridge();
        }
        else {
            void openSyntaxBridge();
        }
    });
    queryElement(shell, '[data-action="code-format"]')?.addEventListener('click', handleFormatDocument);
    queryElement(shell, '[data-action="code-run"]')?.addEventListener('click', () => queryElement(shell, '[data-action="run"]')?.click());
    queryElement(shell, '[data-action="stop"]')?.addEventListener('click', handleStopRun);
    queryElement(shell, '[data-action="code-stop"]')?.addEventListener('click', () => queryElement(shell, '[data-action="stop"]')?.click());
    queryElement(shell, '[data-action="run-selected"]')?.addEventListener('click', async () => {
        const doc = getActiveDoc();
        if (!doc)
            return;
        const script = state.editorInstance?.getValue?.() || doc.content;
        const selectedText = String(state.editorInstance?.getSelection?.() || '').trim();
        if (selectedText) {
            await executeRun(doc, { script, selection: selectedText, label: 'selection' });
            return;
        }
        const currentStatement = String(state.editorInstance?.getCurrentStatement?.() || '').trim();
        if (currentStatement) {
            await executeRun(doc, { script, selection: currentStatement, label: 'statement at cursor' });
            return;
        }
        setDocumentTrace(doc, [
            { type: 'clear', resetHistory: true },
            { type: 'status', status: 'idle' },
            { type: 'message', level: 'warn', text: 'No query or statement selected to run. Select SQL text or place cursor inside a statement.' },
        ]);
        _feedback.notify('No query or statement selected to run. Select SQL text or place cursor inside a statement.', { title: 'Execution Scope', tone: 'warning' });
    });
    queryElement(shell, '[data-action="theme"]')?.addEventListener('click', () => {
        const isDark = document.body.classList.toggle('theme-dark');
        localStorage.setItem('portal-theme', isDark ? 'dark' : 'light');
    });
    queryElement(shell, '[data-action="run"]')?.addEventListener('click', async () => {
        const doc = getActiveDoc();
        if (!doc)
            return;
        const script = state.editorInstance ? state.editorInstance.getValue() : doc.content;
        await executeRun(doc, { script, label: 'script' });
    });
    let isResizing = false;
    resizer.addEventListener('mousedown', () => {
        isResizing = true;
        document.body.style.cursor = 'row-resize';
    });
    resizer.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowUp') {
            e.preventDefault();
            applySplitPct(currentSplitPct - 5);
        }
        else if (e.key === 'ArrowDown') {
            e.preventDefault();
            applySplitPct(currentSplitPct + 5);
        }
        else if (e.key === 'Home') {
            e.preventDefault();
            applySplitPct(15);
        }
        else if (e.key === 'End') {
            e.preventDefault();
            applySplitPct(85);
        }
        else if (e.key === 'Enter') {
            e.preventDefault();
            applySplitPct(50);
        }
    });
    window.addEventListener('mousemove', (e) => {
        if (!isResizing)
            return;
        const stage = queryElement(shell, '[data-studio-stage]');
        if (!stage)
            return;
        const stageRect = stage.getBoundingClientRect();
        const relativeY = e.clientY - stageRect.top;
        const totalHeight = stageRect.height;
        if (totalHeight <= 0)
            return;
        const topPct = (relativeY / totalHeight) * 100;
        applySplitPct(topPct);
    });
    window.addEventListener('mouseup', () => {
        if (isResizing) {
            isResizing = false;
            document.body.style.cursor = 'default';
        }
    });
    // The panel overwrites its container's className, so it gets its own child element rather than
    // the host — the host keeps the Studio sizing rules the panel's `height: 100%` depends on.
    const resultsPanelHost = document.createElement('div');
    resultsHost.appendChild(resultsPanelHost);
    state.resultsPanel = createScriptResultsPanel(resultsPanelHost, {
        onNavigate: (line, column) => {
            setProjection(getActiveDoc()?.projection === 'canvas' ? 'split' : (getActiveDoc()?.projection || 'split'));
            state.editorInstance?.gotoLine?.(line, column);
        },
    });
    state.resultsPanel.setApplyFix?.(fix => applyDiagnosticQuickFix(fix));
    await mountScriptEditor();
    // The Portal's shared connections, read once up front: any write can need their declarations,
    // including one from a surface that never listed connections itself. Desktop hosts have none.
    if (!hasWorkspaceHost)
        loadConnectionAliases().catch(() => { });
    renderTabs();
    renderSidebarContent('explorer');
    // Rendered so the panel is ready, but left collapsed: the rail button opens it on demand.
    if (!state.sidebarOpen)
        sidebar.classList.add('collapsed');
    if (state.activeDocId === '__home__') {
        renderStudioHome();
        setContextualRailVisibility();
    }
    else {
        const active = getActiveDoc();
        if (active) {
            rememberLineEnding(active);
            await ensureReportWorkflow(active);
            checkRecoverableDraft(active);
        }
        setProjection(getActiveDoc()?.projection || getStoredProjectionPreference() || 'split');
        renderVisualStage();
        setContextualRailVisibility();
    }
    return {
        state,
        leaseLifecycle,
        switchDoc,
        setProjection,
        promoteFilterToSlicer,
        persistFilter,
        surgicalPatchVisualOption,
        surgicalPatchVisualMapping,
        addVisualToCanvas,
        setDocumentTrace,
        duplicateVisual,
        deleteVisual,
        openCatalogReport,
        publishCatalogReport: handlePublishCatalogReport,
        openSyntaxBridge,
        closeSyntaxBridge,
        getStoredProjectionPreference,
        storeProjectionPreference,
        dispose: () => {
            document.removeEventListener('click', onOutsideClick);
            document.removeEventListener('keydown', onShellKeyDown);
            window.removeEventListener('beforeunload', onBeforeUnload);
            leaseLifecycle.dispose();
            clearTimeout(codeMirrorDebounce || undefined);
            clearTimeout(draftSaveDebounce || undefined);
            tabResizeObserver?.disconnect();
            disposePipelineDag();
            state.designerInstance?.dispose?.();
            state.designerInstance = null;
            state.resultsPanel?.dispose?.();
            state.resultsPanel = null;
            container.innerHTML = '';
        }
    };
}
