/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-state.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Per-document and workbench state for ETL-SQL Studio.
 */

export function createStudioDocumentContext(snapshot = null) {
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
        resultsTrace: []
    };
}

export function createStudioState(options = {}) {
    const workspaceFiles = options.workspaceFiles || [];
    const documents = options.documents ? [...options.documents] : [];
    if (!documents.length && (options.initialFile || options.initialContent)) {
        const path = options.initialFile || 'untitled_1.rptsql';
        documents.push({
            id: 'doc-1',
            path,
            name: path.split('/').pop().split('\\').pop(),
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
    };
}

export function createStudioContextStore(documents, initialSnapshot = null) {
    const home = createStudioDocumentContext();
    const forDocument = document => {
        if (!document) return home;
        document.studioContext ||= createStudioDocumentContext();
        return document.studioContext;
    };
    documents.forEach(forDocument);
    if (initialSnapshot && documents.length) forDocument(documents[0]).snapshot = initialSnapshot;
    return { home, forDocument };
}
