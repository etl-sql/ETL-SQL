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

import type { StudioAuthoringOptions, StudioAuthoringSurfacesHandle } from './studio-authoring-context.js';
export type { PipelineTaskField, StudioAuthoringDialogAction, StudioAuthoringDialogApi, StudioAuthoringDialogElements, StudioAuthoringDialogRenderOptions, StudioAuthoringEditorTransport, StudioAuthoringFeedback, StudioAuthoringOptions, StudioAuthoringRequestOptions, StudioAuthoringShell, StudioAuthoringSurfacesHandle } from './studio-authoring-context.js';

import { createStudioAuthoringChart } from './studio-authoring-chart.js';
import { createStudioAuthoringData } from './studio-authoring-data.js';
import { createStudioAuthoringDialog } from './studio-authoring-dialog.js';
import { createStudioAuthoringPipeline } from './studio-authoring-pipeline.js';
import { createStudioAuthoringPreview } from './studio-authoring-preview.js';
import { createStudioAuthoringReport } from './studio-authoring-report.js';

/**
 * @typedef {Object} StudioAuthoringOptions
 * @property {{ backdrop: HTMLElement, box: HTMLElement }} dialog
 * @property {typeof import('./studio-contracts.js').STUDIO_ROUTES} routes
 * @property {typeof import('./studio-contracts.js').STUDIO_CATALOG_ROUTES} catalogRoutes
 * @property {(route: string, options?: any) => Promise<any>} request
 * @property {{ url: (route: string) => string, authFetch: any }} editorTransport
 * @property {() => any} getActiveDocument
 * @property {() => any} activeContext
 * @property {(doc: any) => any} contextFor
 * @property {(label: string, mutator: (design: any) => any) => Promise<any>} mutate
 * @property {(design: any, base: string) => string} uniqueVisualName
 * @property {boolean} hasWorkspaceHost
 * @property {{ notify: Function }} feedback
 * @property {any} shell
 */

/**
 * @typedef {Object} StudioAuthoringSurfacesHandle
 * @property {(options?: { intent?: string | null; connection?: string | null }) => Promise<string | null>} openDataWizard
 * @property {(options?: { kind?: string; task?: any; connections?: any[]; suggestedId?: string; placement?: { after?: string; into?: string } | null }) => Promise<any>} openPipelineTaskEditor
 * @property {(options: { taskId: string; plan: any }) => Promise<boolean | null>} openPipelineRunPlanConfirm
 * @property {(seed?: any) => Promise<string | null>} openChartBuilder
 * @property {() => Promise<string | null>} runChooseDataStep
 * @property {() => Promise<void>} runParameterStep
 * @property {() => Promise<void>} runDetailsStep
 * @property {() => Promise<void>} runTotalsStep
 * @property {() => Promise<void>} runFurnitureStep
 * @property {() => Promise<void>} runPreviewStep
 * @property {() => Promise<void>} runExportStep
 * @property {() => Promise<void>} runVisualsStep
 * @property {() => Promise<void>} runCrossFilterStep
 * @property {() => boolean} hasDataSample
 * @property {() => { dataset: string | null; options: Record<string, string> }} visualSourceBinding
 */

/**
 * Builds the guided authoring surfaces against one Studio workbench.
 *
 * @param {StudioAuthoringOptions} options
 * @returns {StudioAuthoringSurfacesHandle}
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
}: StudioAuthoringOptions): StudioAuthoringSurfacesHandle {
    // Getters keep sibling callbacks and shared state live as the shell initializes.
    // Controllers own private state; creating them must not read these deferred bindings.
    const { studioDialog, guidedBlocker } = createStudioAuthoringDialog({
        get dialog() { return dialog; },
        get feedback() { return feedback; },
    });

    const { hasDataSample, visualSourceBinding, guidedColumnNames, guidedNumericColumns, datasetBaseName, openDataWizard, runChooseDataStep, requireDataSample } = createStudioAuthoringData({
        get activeContext() { return activeContext; },
        get catalogRoutes() { return catalogRoutes; },
        get contextFor() { return contextFor; },
        get dialog() { return dialog; },
        get editorTransport() { return editorTransport; },
        get feedback() { return feedback; },
        get getActiveDocument() { return getActiveDocument; },
        get guidedBlocker() { return guidedBlocker; },
        get hasWorkspaceHost() { return hasWorkspaceHost; },
        get mutate() { return mutate; },
        get request() { return request; },
        get routes() { return routes; },
        get shell() { return shell; },
        get studioDialog() { return studioDialog; },
    });

    const { openChartBuilder } = createStudioAuthoringChart({
        get activeContext() { return activeContext; },
        get datasetBaseName() { return datasetBaseName; },
        get feedback() { return feedback; },
        get mutate() { return mutate; },
        get requireDataSample() { return requireDataSample; },
        get shell() { return shell; },
        get studioDialog() { return studioDialog; },
        get uniqueVisualName() { return uniqueVisualName; },
        get visualSourceBinding() { return visualSourceBinding; },
    });

    const { runParameterStep, runDetailsStep, runTotalsStep, runFurnitureStep } = createStudioAuthoringReport({
        get dialog() { return dialog; },
        get feedback() { return feedback; },
        get getActiveDocument() { return getActiveDocument; },
        get guidedBlocker() { return guidedBlocker; },
        get guidedColumnNames() { return guidedColumnNames; },
        get guidedNumericColumns() { return guidedNumericColumns; },
        get mutate() { return mutate; },
        get request() { return request; },
        get requireDataSample() { return requireDataSample; },
        get routes() { return routes; },
        get shell() { return shell; },
        get studioDialog() { return studioDialog; },
        get uniqueVisualName() { return uniqueVisualName; },
        get visualSourceBinding() { return visualSourceBinding; },
    });

    const { runPreviewStep, runExportStep, runVisualsStep, runCrossFilterStep } = createStudioAuthoringPreview({
        get activeContext() { return activeContext; },
        get dialog() { return dialog; },
        get feedback() { return feedback; },
        get getActiveDocument() { return getActiveDocument; },
        get guidedBlocker() { return guidedBlocker; },
        get openChartBuilder() { return openChartBuilder; },
        get request() { return request; },
        get requireDataSample() { return requireDataSample; },
        get routes() { return routes; },
        get runDetailsStep() { return runDetailsStep; },
        get shell() { return shell; },
        get studioDialog() { return studioDialog; },
    });

    const { openPipelineTaskEditor, openPipelineRunPlanConfirm } = createStudioAuthoringPipeline({
        get dialog() { return dialog; },
        get editorTransport() { return editorTransport; },
        get getActiveDocument() { return getActiveDocument; },
        get guidedBlocker() { return guidedBlocker; },
        get request() { return request; },
        get routes() { return routes; },
        get shell() { return shell; },
        get studioDialog() { return studioDialog; },
    });

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

export { declaredConnectionNames } from './studio-authoring-context.js';
