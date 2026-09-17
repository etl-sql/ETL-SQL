import assert from 'node:assert/strict';
import {
    createStudioDocumentContext,
    createStudioState,
    createStudioContextStore
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-state.js';

// 1. createStudioDocumentContext
const defaultContext = createStudioDocumentContext();
assert.equal(defaultContext.snapshot, null);
assert.equal(defaultContext.snapshotPackage.metadata.isSampled, true);
assert.deepEqual(defaultContext.snapshotPackage.columns, []);
assert.deepEqual(defaultContext.snapshotPackage.sampleRows, {});
assert.ok(defaultContext.snapshotCache instanceof Map);
assert.deepEqual(defaultContext.activeFilters, {});
assert.deepEqual(defaultContext.filterFields, []);
assert.equal(defaultContext.selectedSource, null);
assert.deepEqual(defaultContext.sourceColumns, []);
assert.deepEqual(defaultContext.diagnostics, []);
assert.equal(defaultContext.runAbort, null);
assert.equal(defaultContext.runActive, false);
assert.equal(defaultContext.previewAbort, null);
assert.equal(defaultContext.dagAbort, null);
assert.equal(defaultContext.dagRevision, 0);
assert.equal(defaultContext.modelRevision, 0);
assert.equal(defaultContext.lastValidDag, null);
assert.equal(defaultContext.syncRevision, 0);
assert.equal(defaultContext.previewedDatasetSignature, null);
assert.deepEqual(defaultContext.resultsTrace, []);

// With initial snapshot
const customSnapshot = { source: 'Sales', rowCount: 100 };
const snapshotContext = createStudioDocumentContext(customSnapshot);
assert.equal(snapshotContext.snapshot, customSnapshot);

// 2. createStudioState - default options
const defaultState = createStudioState();
assert.deepEqual(defaultState.workspaceFiles, []);
assert.deepEqual(defaultState.catalogReports, []);
assert.deepEqual(defaultState.catalogFolders, []);
assert.ok(defaultState.capabilities instanceof Set);
assert.equal(defaultState.capabilities.size, 0);
assert.equal(defaultState.deploymentMode, 'Desktop');
assert.equal(defaultState.sourceControlEnabled, false);
assert.deepEqual(defaultState.documents, []);
assert.deepEqual(defaultState.workspaceFolders, []);
assert.ok(defaultState.explorerExpanded instanceof Set);
assert.equal(defaultState.activeDocId, '__home__');
assert.equal(defaultState.activeActivity, 'explorer');
assert.equal(defaultState.filterSidebarOpen, false);
assert.equal(defaultState.selectedVisualId, null);
assert.equal(defaultState.sidebarOpen, false);
assert.equal(defaultState.editorInstance, null);
assert.equal(defaultState.resultsPanel, null);
assert.equal(defaultState.dagInstance, null);
assert.equal(defaultState.dagDocumentId, null);
assert.equal(defaultState.dataModelInstance, null);
assert.equal(defaultState.enginePlanScope, null);
assert.equal(defaultState.governance, null);
assert.equal(defaultState.governanceScopeId, null);
assert.equal(defaultState.previewAs, null);
assert.equal(defaultState.previewAsVocabulary, null);

// 3. createStudioState - initialFile & initialContent
const fileState = createStudioState({
    initialFile: 'reports/sales/q3_summary.rptsql',
    initialContent: 'PAGE Sales;'
});
assert.equal(fileState.documents.length, 1);
assert.equal(fileState.documents[0].id, 'doc-1');
assert.equal(fileState.documents[0].path, 'reports/sales/q3_summary.rptsql');
assert.equal(fileState.documents[0].name, 'q3_summary.rptsql');
assert.equal(fileState.documents[0].content, 'PAGE Sales;');
assert.equal(fileState.documents[0].isDirty, false);
assert.equal(fileState.documents[0].projection, 'split');
assert.equal(fileState.activeDocId, 'doc-1');

// Windows path handling in initialFile
const winPathState = createStudioState({
    initialFile: 'C:\\Users\\tester\\pipeline.etlsql'
});
assert.equal(winPathState.documents[0].name, 'pipeline.etlsql');

// Fallback path when only initialContent is provided
const contentOnlyState = createStudioState({
    initialContent: 'SELECT 1;'
});
assert.equal(contentOnlyState.documents[0].path, 'untitled_1.rptsql');
assert.equal(contentOnlyState.documents[0].name, 'untitled_1.rptsql');
assert.equal(contentOnlyState.documents[0].content, 'SELECT 1;');

// Custom options
const customState = createStudioState({
    workspaceFiles: [{ path: 'f1.sql' }],
    catalogReports: [{ id: 1, name: 'Rep 1' }],
    catalogFolders: [{ id: 'fld-1', name: 'Folder 1' }],
    capabilities: ['ScriptSave', 'ReportPublish'],
    deploymentMode: 'Portal',
    sourceControlEnabled: true,
    documents: [{ id: 'existing-doc', path: 'test.rptsql', content: '', isDirty: true }],
    workspaceFolders: ['folder1', 'folder2'],
    activeDocId: 'custom-active'
});
assert.equal(customState.workspaceFiles.length, 1);
assert.equal(customState.catalogReports.length, 1);
assert.equal(customState.catalogFolders.length, 1);
assert.equal(customState.capabilities.has('ScriptSave'), true);
assert.equal(customState.capabilities.has('ReportPublish'), true);
assert.equal(customState.deploymentMode, 'Portal');
assert.equal(customState.sourceControlEnabled, true);
assert.equal(customState.documents.length, 1);
assert.equal(customState.workspaceFolders.length, 2);
assert.equal(customState.activeDocId, 'custom-active');

// 4. createStudioContextStore
const docs = [
    { id: 'd1', path: 'd1.rptsql', content: '', isDirty: false },
    { id: 'd2', path: 'd2.rptsql', content: '', isDirty: false }
];
const store = createStudioContextStore(docs, { source: 'InitialDS' });

// home context
assert.ok(store.home);
assert.equal(store.forDocument(null), store.home);
assert.equal(store.forDocument(undefined), store.home);

// Document contexts are initialized
assert.ok(docs[0].studioContext);
assert.ok(docs[1].studioContext);
assert.equal(store.forDocument(docs[0]), docs[0].studioContext);
assert.equal(store.forDocument(docs[1]), docs[1].studioContext);

// Initial snapshot assigned to first document
assert.equal(docs[0].studioContext.snapshot.source, 'InitialDS');
assert.equal(docs[1].studioContext.snapshot, null);

// Lazily initializes studioContext if document wasn't in original array
const lateDoc = { id: 'd3', path: 'd3.rptsql', content: '', isDirty: false };
assert.equal(lateDoc.studioContext, undefined);
const lateContext = store.forDocument(lateDoc);
assert.ok(lateContext);
assert.equal(lateDoc.studioContext, lateContext);

console.log('test-studio-state: all checks passed');
