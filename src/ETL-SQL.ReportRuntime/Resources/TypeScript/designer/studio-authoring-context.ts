/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 */

/** Shared types and DOM helpers for studio-authoring. */
import type { STUDIO_CATALOG_ROUTES, STUDIO_ROUTES } from './studio-contracts.js';

// DOM cast helpers for emitted JavaScript checkJs evaluation
/**
 * @param {unknown} el
 * @returns {any}
 */
export function asHtml(el: unknown): HTMLElement {
    return el as HTMLElement;
}

/**
 * @param {unknown} el
 * @returns {any}
 */
export function asInput(el: unknown): HTMLInputElement {
    return el as HTMLInputElement;
}

/**
 * @param {unknown} el
 * @returns {any}
 */
export function asSelect(el: unknown): HTMLSelectElement {
    return el as HTMLSelectElement;
}

/**
 * @param {unknown} el
 * @returns {any}
 */
export function asButton(el: unknown): HTMLButtonElement {
    return el as HTMLButtonElement;
}

export interface StudioAuthoringDialogElements {
    backdrop: HTMLElement;
    box: HTMLElement;
}

export interface StudioAuthoringEditorTransport {
    url(route: string): string;
    authFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response>;
}

export interface StudioAuthoringShell {
    getScriptText(): string;
    setScriptText(text: string, label?: string): void;
    designerState(): any;
    refreshSnapshot(): void;
    setActivity(activity: string): void;
    setProjection(projection: string): void;
    renderSidebar(): void;
    selectVisual?(name: string): void;
    openConnectionWizard(options: { onDone?: (alias: string) => void }): void;
    runReport(parameters?: Record<string, unknown> | null): void;
    renderTabs(): void;
    availableConnections?(): Promise<Array<string | { alias?: string; name?: string }>> | Array<string | { alias?: string; name?: string }>;
}

export interface StudioAuthoringFeedback {
    notify(message: string, options?: { title?: string; tone?: 'info' | 'success' | 'warning' | 'error' }): void;
}

export interface StudioAuthoringRequestOptions {
    method?: string;
    body?: any;
    query?: Record<string, any>;
    accept?: string;
    fallbackError?: string;
}

export interface StudioAuthoringOptions {
    dialog: StudioAuthoringDialogElements;
    routes: typeof STUDIO_ROUTES;
    catalogRoutes: typeof STUDIO_CATALOG_ROUTES;
    request(route: string, options?: StudioAuthoringRequestOptions): Promise<any>;
    editorTransport: StudioAuthoringEditorTransport;
    getActiveDocument(): any;
    activeContext(): any;
    contextFor(doc: any): any;
    mutate(label: string, mutator: (design: any) => any): Promise<any>;
    uniqueVisualName(design: any, base: string): string;
    hasWorkspaceHost: boolean;
    feedback: StudioAuthoringFeedback;
    shell: StudioAuthoringShell;
}

export interface StudioAuthoringDialogAction {
    id: string;
    label: string;
    primary?: boolean;
    disabled?: boolean;
    run?(): void | Promise<void>;
}

export interface StudioAuthoringDialogRenderOptions {
    lede?: string;
    body?: string;
    actions?: StudioAuthoringDialogAction[];
    wire?(host: HTMLElement): void;
}

export interface StudioAuthoringDialogApi {
    close(value: any): void;
    setTitle(next: string): void;
    busy(flag: boolean): void;
    render(content?: StudioAuthoringDialogRenderOptions): void;
}

export interface PipelineTaskField {
    name: string;
    label: string;
    placeholder: string;
    mono?: boolean;
    optional?: boolean;
    hint?: string;
    /**
     * A field filled from the connection rather than typed: `table` offers the connection's tables,
     * and `columns` offers the chosen table's columns as checkboxes, written as a comma list. Typing
     * still works — a table the schema read could not see is still a table.
     */
    picker?: 'table' | 'columns' | 'temp' | 'choice' | 'toggle';
    /**
     * For a `columns` picker: whose columns to offer. `table` is the connection table in the `table`
     * field; `source` and `right` are the #temp tables named in those fields.
     */
    columnsFrom?: 'table' | 'source' | 'right';
    /** For a `choice` picker: the values it may hold, in the order they are offered. */
    choices?: ReadonlyArray<string>;
}

export interface StudioAuthoringSurfacesHandle {
    openDataWizard(options?: { intent?: string | null; connection?: string | null }): Promise<string | null>;
    openPipelineTaskEditor(options?: {
        kind?: string;
        task?: any;
        connections?: any[];
        suggestedId?: string;
        placement?: { after?: string; into?: string } | null;
    }): Promise<any>;
    openPipelineRunPlanConfirm(options: { taskId: string; plan: any }): Promise<boolean | null>;
    openChartBuilder(seed?: any): Promise<string | null>;
    runChooseDataStep(): Promise<string | null>;
    runParameterStep(): Promise<void>;
    runDetailsStep(): Promise<void>;
    runTotalsStep(): Promise<void>;
    runFurnitureStep(): Promise<void>;
    runPreviewStep(): Promise<void>;
    runExportStep(): Promise<void>;
    runVisualsStep(): Promise<void>;
    runCrossFilterStep(): Promise<void>;
    hasDataSample(): boolean;
    visualSourceBinding(): { dataset: string | null; options: Record<string, string> };
}

/** Connection aliases the script itself declares. Host-registered aliases deliberately do not count. */
export function declaredConnectionNames(scriptText: string | null | undefined): string[] {
    const names: string[] = [];
    const pattern = /CREATE\s+(?:OR\s+REPLACE\s+)?CONNECTION\s+(?:IF\s+NOT\s+EXISTS\s+)?\[?([A-Za-z_][A-Za-z0-9_]*)\]?/gi;
    let match: RegExpExecArray | null;
    while ((match = pattern.exec(String(scriptText || ''))) !== null) names.push(match[1]);
    return names;
}

/** Parameter data types the guided step offers; the script accepts any type the parser knows. */
export const STUDIO_PARAMETER_TYPES = ['VARCHAR', 'INT', 'DECIMAL', 'DATE', 'DATETIME', 'BOOLEAN'];

/** Aggregates a TABLE's GRAND_TOTAL accepts. */
export const STUDIO_TOTAL_AGGREGATES = ['SUM', 'AVG', 'COUNT'];

/**
 * Suggested format patterns. These are suggestions in a free-text field, not a closed list: the
 * renderer takes any .NET numeric or date pattern, and offering only these would make the common
 * ones reachable at the cost of making everything else look unsupported.
 */
export const STUDIO_FORMAT_PATTERNS = Object.freeze([
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
