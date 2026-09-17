/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Per-document and workbench state for ETL-SQL Studio.
 */

export interface StudioDocumentContext {
    snapshot: unknown | null;
    snapshotPackage: {
        metadata: { isSampled: boolean; [key: string]: unknown };
        columns: string[];
        sampleRows: Record<string, unknown>;
        [key: string]: unknown;
    };
    snapshotCache: Map<string, unknown>;
    activeFilters: Record<string, unknown>;
    filterFields: string[];
    selectedSource: string | null;
    sourceColumns: unknown[];
    diagnostics: unknown[];
    runAbort: AbortController | null;
    runActive: boolean;
    previewAbort: AbortController | null;
    dagAbort: AbortController | null;
    dagRevision: number;
    modelRevision: number;
    lastValidDag: unknown | null;
    syncRevision: number;
    previewedDatasetSignature: string | null;
    resultsTrace: unknown[];
    skipCrossFilters?: boolean;
    patchQueue?: Promise<unknown>;
    [key: string]: unknown;
}

export interface StudioDocument {
    id: string;
    path: string;
    name?: string;
    content: string;
    isDirty: boolean;
    contentRevision?: number;
    editorState?: unknown;
    scrollTop?: number;
    scrollLeft?: number;
    projection?: string;
    studioContext?: StudioDocumentContext;
    lease?: { acquired?: boolean; [key: string]: unknown };
    canSave?: boolean;
    readOnlyReason?: string;
    [key: string]: unknown;
}

export interface StudioWorkspaceFile {
    path: string;
    name?: string;
    [key: string]: unknown;
}

export interface StudioCatalogReport {
    id: string | number;
    name?: string;
    path?: string;
    [key: string]: unknown;
}

export interface StudioCatalogFolder {
    id?: string | number;
    name?: string;
    path?: string;
    [key: string]: unknown;
}

export interface StudioStateOptions {
    workspaceFiles?: StudioWorkspaceFile[];
    documents?: StudioDocument[];
    initialFile?: string;
    initialContent?: string;
    catalogReports?: StudioCatalogReport[];
    catalogFolders?: StudioCatalogFolder[];
    capabilities?: Iterable<string>;
    deploymentMode?: string;
    sourceControlEnabled?: boolean;
    workspaceFolders?: string[];
    activeDocId?: string;
    [key: string]: unknown;
}

export interface StudioState {
    workspaceFiles: StudioWorkspaceFile[];
    catalogReports: StudioCatalogReport[];
    catalogFolders: StudioCatalogFolder[];
    capabilities: Set<string>;
    deploymentMode: string;
    sourceControlEnabled: boolean;
    documents: StudioDocument[];
    workspaceFolders: string[];
    explorerExpanded: Set<string>;
    activeDocId: string;
    activeActivity: string;
    filterSidebarOpen: boolean;
    selectedVisualId: string | null;
    sidebarOpen: boolean;
    editorInstance: unknown;
    isEditorDegraded?: boolean;
    resultsPanel: unknown;
    dagInstance: unknown;
    dagDocumentId: string | null;
    dataModelInstance: unknown;
    enginePlanScope: unknown;
    governance: unknown;
    governanceScopeId: string | null;
    previewAs: unknown;
    previewAsVocabulary: unknown;
    [key: string]: unknown;
}

export interface StudioContextStore {
    home: StudioDocumentContext;
    forDocument: (document?: StudioDocument | null) => StudioDocumentContext;
}

export function createStudioDocumentContext(snapshot: unknown = null): StudioDocumentContext {
    return {
        snapshot,
        snapshotPackage: { metadata: { isSampled: true }, columns: [], sampleRows: {} },
        snapshotCache: new Map(),
        activeFilters: {},
        filterFields: [],
        selectedSource: null,
        sourceColumns: [],
        diagnostics: [],
        runAbort: null,
        runActive: false,
        previewAbort: null,
        dagAbort: null,
        dagRevision: 0,
        modelRevision: 0,
        lastValidDag: null,
        syncRevision: 0,
        previewedDatasetSignature: null,
        resultsTrace: [],
        skipCrossFilters: false
    };
}

export function createStudioState(options: StudioStateOptions = {}): StudioState {
    const workspaceFiles = options.workspaceFiles || [];
    const documents = options.documents ? [...options.documents] : [];
    if (!documents.length && (options.initialFile || options.initialContent)) {
        const path = options.initialFile || 'untitled_1.rptsql';
        documents.push({
            id: 'doc-1',
            path,
            name: path.split('/').pop()!.split('\\').pop(),
            content: options.initialContent || '',
            isDirty: false,
            projection: 'split',
        });
    }

    return {
        workspaceFiles,
        catalogReports: [...(options.catalogReports || [])],
        catalogFolders: [...(options.catalogFolders || [])],
        capabilities: new Set(options.capabilities || []),
        deploymentMode: options.deploymentMode || 'Desktop',
        sourceControlEnabled: Boolean(options.sourceControlEnabled),
        documents,
        workspaceFolders: [...(options.workspaceFolders || [])],
        // Folder contents stay out of the way until the author asks for them. New and renamed
        // folders are expanded by the operation that creates them, so those results remain visible.
        explorerExpanded: new Set(),
        activeDocId: options.activeDocId || (documents.length ? documents[0].id : '__home__'),
        activeActivity: 'explorer',
        filterSidebarOpen: false,
        selectedVisualId: null,
        // Studio opens on the canvas and the script, not on a file tree. The Explorer is one click
        // away on the rail, and starting collapsed gives the work itself the width.
        sidebarOpen: false,
        editorInstance: null,
        resultsPanel: null,
        dagInstance: null,
        dagDocumentId: null,
        dataModelInstance: null,
        enginePlanScope: null,
        governance: null,
        governanceScopeId: null,
        // The audience row-level-security predicates are evaluated as, or null to run as yourself.
        previewAs: null,
        previewAsVocabulary: null,
    };
}

export function createStudioContextStore(documents: StudioDocument[], initialSnapshot: unknown = null): StudioContextStore {
    const home = createStudioDocumentContext();
    const forDocument = (document?: StudioDocument | null) => {
        if (!document) return home;
        document.studioContext ||= createStudioDocumentContext();
        return document.studioContext;
    };
    documents.forEach(forDocument);
    if (initialSnapshot && documents.length) forDocument(documents[0]).snapshot = initialSnapshot;
    return { home, forDocument };
}
