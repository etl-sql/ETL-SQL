/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 */

/** Shared types and DOM helpers for designer. */
import { _feedback } from './designer-util.js';
import type { ScriptEditorHandle } from './script-editor.js';
import type { ScriptWorkbenchHandle } from './script-workbench.js';
import type { VisualFormatting } from './visual-format-inspector.js';

export const feedback = _feedback as EtlSqlFeedback;

/** The parser-owned visual options are string-valued; extension payloads stay at unknown. */
export type DesignerVisual = Omit<DesignerVisualDto, 'options' | 'mappings' | 'formatting' | 'title' | 'dataset' | 'containerId'> & {
    options: Record<string, string>;
    mappings: Record<string, string>;
    title?: string | null;
    dataset?: string | null;
    containerId?: string | null;
    formatting?: VisualFormatting;
    width?: string | number;
    height?: string | number;
    target?: string;
};

export type DesignerPage = Omit<DesignerPageDto, 'visuals'> & { visuals: DesignerVisual[] };

export type DesignerDataset = DesignerDatasetDto & {
    columns?: string[];
    schema?: Array<{ name: string; type?: string }>;
};

export interface DesignerSnapshotDataset {
    columns?: string[];
    rows?: unknown[][];
}

export interface DesignerSnapshotPackage {
    datasets?: Record<string, DesignerSnapshotDataset>;
    sampleRows?: Record<string, unknown[][]>;
    visualSvgs?: Record<string, string>;
    columnsByVisual?: Record<string, string[]>;
    columns?: string[];
    metadata?: Record<string, unknown>;
    owner?: string;
    expiresAt?: string;
}

export type DesignerBookmark = Omit<DesignerBookmarkDto, 'title' | 'page'> & {
    title?: string | null;
    page?: string | null;
};

export type DesignerState = Omit<DesignerStateDto, 'pages' | 'datasets' | 'reportStyle' | 'bookmarks' | 'parameters' | 'connections'> & {
    pages: DesignerPage[];
    datasets: DesignerDataset[];
    reportStyle?: DesignerReportStyleDto;
    bookmarks?: DesignerBookmark[];
    parameters?: DesignerParameterDto[];
    connections?: DesignerConnectionDto[];
};

export interface DesignerOptions {
    designState?: DesignerState | null;
    reportId?: number | null;
    reportVersion?: number | null;
    sourceRevision?: string | null;
    sourceControlEnabled?: boolean;
    reportName?: string;
    folderId?: number | null;
    folders?: Array<{ id: number; path?: string; name?: string }>;
    initialMode?: 'design' | 'code';
    apiBase?: string;
    authFetch?: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;
    onSaveScript?: (script: string) => Promise<unknown> | unknown;
    onSave?: (created?: unknown) => void;
    onCancel?: () => void;
    onVisualSelect?: (visualId: string | null) => void;
    onRequestData?: () => void;
    hideSidebar?: boolean;
    hideProps?: boolean;
    hideTopbar?: boolean;
    propertiesHost?: HTMLElement;
    host?: string;
    previewUrl?: string;
    previewApiUrl?: string;
    connectionRef?: string | null;
    documentUri?: string | (() => string);
    script?: string;
    initialScript?: string;
    getScript?: () => string;
    onScriptChange?: (script: string) => void;
    snapshotPackage?: DesignerSnapshotPackage;
    snapshotMode?: boolean;
    requireDataFirst?: boolean;
    canAddVisual?: () => boolean;
    defaultVisualBinding?: () => { dataset: string | null; options: Record<string, string> } | null;
    onAddVisualBlocked?: () => void;
    getDatasetColumns?: (datasetName?: string) => string[];
    isVisualLocked?: (visual: DesignerVisual) => boolean;
}

export interface DesignerHandle {
    applyScriptText: (script: string) => Promise<DesignerApplyResult>;
    invalidateScriptApply: () => void;
    addVisual: (type: string) => void;
    selectVisual: (id: string | null, options?: { toggle?: boolean; multi?: boolean; skipEditorSync?: boolean; skipCanvas?: boolean }) => void;
    mountBookmarks: (host?: HTMLElement) => HTMLElement;
    refreshSnapshot: () => void;
    selectPage: (index: number) => boolean;
    activePageIndex: () => number;
    getState: () => DesignerState;
    dispose: () => void;
}

export interface DesignerApplyResult {
    applied: boolean;
    stale?: boolean;
    error?: string;
    designState?: DesignerState;
}

/** The designer works with native elements; these are the few properties on its own cards. */
export type DesignerDom = HTMLElement & {
    _script?: string;
    gridCol?: number;
    gridRow?: number;
    is?: string;
    html?: string;
};

/** Query helpers retain the source's direct-access behaviour while keeping DOM types native. */
export function queryElement<T extends Element = HTMLElement>(root: ParentNode, selector: string): T {
    return root.querySelector<T>(selector) as T;
}

export function queryElements<T extends Element = HTMLElement>(root: ParentNode, selector: string): NodeListOf<T> {
    return root.querySelectorAll<T>(selector);
}

/**
 * The name an inspector group is remembered by: its summary's own text, without the count its
 * `<span>` carries. Keyed on the whole text, a group closed itself whenever its count changed,
 * because "Analytics 1" is not the group the author opened as "Analytics".
 */
export function inspectorGroupKey(details: Element): string {
    const summary = details.querySelector('summary');
    if (!summary) return '';
    return Array.from(summary.childNodes)
        .filter(node => node.nodeType === 3)
        .map(node => node.textContent || '')
        .join('')
        .trim();
}

export function controlTarget(event: Event): DesignerFormControl {
    return event.target as DesignerFormControl;
}

export function checkedTarget(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
}

export function eventElement(event: Event): HTMLElement {
    return event.target as HTMLElement;
}

export function closestElement<T extends Element = HTMLElement>(event: Event, selector: string): T | null {
    return (event.target as Element | null)?.closest(selector) as T | null;
}

export function datasetValue(element: Element, key: string): string {
    return (element as HTMLElement).dataset[key] as string;
}

export interface DesignerApiError extends Error {
    status?: number;
    payload?: unknown;
}

export interface DesignerLeaseResponse {
    owner?: string;
    expiresAt: string;
}

export interface DesignerGenerateResponse {
    script?: string;
    error?: string;
}

export interface DesignerParseResponse {
    designState?: DesignerState;
    error?: string;
}

export interface DesignerSaveResponse {
    version?: number;
    sourceRevision?: string;
}

export interface DesignerCommitResponse {
    committed?: boolean;
    sourceRevision?: string;
}

export interface DesignerCreatedReport {
    id?: number;
    reportId?: number;
    name?: string;
    path?: string;
}

export type DesignerFormControl = HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;

export type DesignerEvent = Event & {
    target: DesignerFormControl;
    currentTarget: DesignerDom;
    key: string;
    ctrlKey: boolean;
    metaKey: boolean;
    shiftKey: boolean;
    clientX: number;
    clientY: number;
    dataTransfer: DataTransfer;
    relatedTarget: EventTarget | null;
};

export type DesignerRow = unknown[] | Record<string, unknown>;

export type EditorViewBridge = {
    state: { doc: { toString: () => string; length: number }; selection: { main: { anchor: number; head: number } } };
    dispatch: (transaction: unknown) => void;
};

export type DesignerWorkbenchHandle = ScriptWorkbenchHandle & {
    editor: ScriptEditorHandle & { view?: EditorViewBridge };
};

export type CompleteVisualFormatting = VisualFormatting & {
    title: NonNullable<VisualFormatting['title']>;
    subtitle: NonNullable<VisualFormatting['subtitle']>;
    xAxis: NonNullable<VisualFormatting['xAxis']>;
    yAxis: NonNullable<VisualFormatting['yAxis']>;
    palette: string[];
    conditionalRules: NonNullable<VisualFormatting['conditionalRules']>;
    fields: NonNullable<VisualFormatting['fields']>;
};

export type UnsupportedCascade = { supported: false; text: string };

export type CascadeState = {
        supported: true;
        text: string;
        mode: CascadeMode;
        parents: CascadeParent[];
        invalid: string;
        nullPolicy: string;
        allValue: string;
        multiSelect: string;
    };

export type CascadeParent = { parameter: string; column: string };

export type CascadeMode = 'LOCAL' | 'LIVE';

export const VCATEGORIES = [
        {
            name: 'Charts',
            types: [
                ['BAR','#3b82f6'],['LINE','#06b6d4'],['AREA','#0891b2'],['PIE','#8b5cf6'],
                ['DONUT','#a855f7'],['HBAR','#6366f1'],['SCATTER','#6366f1'],['GAUGE','#a855f7'],
                ['FUNNEL','#d946ef'],['TREEMAP','#ec4899'],['HEATMAP','#f43f5e'],['COMBO','#0ea5e9'],
                ['BOXPLOT','#14b8a6'],['WATERFALL','#10b981'],['BUBBLE','#06b6d4'],['RADAR','#8b5cf6'],
                ['CANDLESTICK','#f59e0b'],['MAP','#10b981'],['GANTT','#8b5cf6'],['SANKEY','#14b8a6'],
                ['SUNBURST','#d946ef'],['NETWORK','#6366f1'],['TRELLIS','#64748b'],['MATRIX','#475569'],
                ['CUSTOM','#8b5cf6']
            ]
        },
        {
            name: 'Data & Content',
            types: [
                ['TABLE','#64748b'],['CARD','#10b981'],['TEXT','#f59e0b'],['IMAGE','#ec4899'],['HTML','#059669']
            ]
        },
        {
            name: 'Filters & Inputs',
            types: [
                ['SLICER','#f97316'],['MULTISELECT','#f97316'],['DATEPICKER','#e11d48'],['RELDATEPICKER','#e11d48'],
                ['SLIDER','#f59e0b'],['SEARCH','#0ea5e9'],['CHECKBOX','#10b981'],['TEXTBOX','#64748b'],['NUMBERBOX','#64748b']
            ]
        },
        {
            name: 'Layout & Actions',
            types: [
                ['CONTAINER','#475569'],['BUTTON','#a855f7']
            ]
        }
    ];

export const VTYPES = VCATEGORIES.flatMap(c => c.types);
