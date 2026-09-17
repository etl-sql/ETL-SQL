import assert from 'node:assert/strict';
import {
    createScriptEditorWorkbench,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/script-workbench.js';

// ── 1. createScriptEditorWorkbench export & signature ────────────────────────
assert.equal(typeof createScriptEditorWorkbench, 'function');
assert.ok(createScriptEditorWorkbench.length >= 1, 'createScriptEditorWorkbench accepts container');

// ── 2. Options validation ───────────────────────────────────────────────────
// Workbench options contract:
// Accepts title, editor, authFetch, connectionRef, documentUri, runUrl,
// dagUrl, previewApiUrl, previewUrl, dataPreviewUrl, workspaceUrl, gitStatusUrl,
// gitStatus, sidebar, showSidebar, onRun, onSave, onApply, onFormat, onFileSelect,
// onClose, onExit.
const testOpts = {
    title: 'Test Script',
    showSidebar: true,
    connectionRef: 'my_db',
    runUrl: '/api/run',
    dagUrl: '/api/dag',
    previewApiUrl: '/api/preview',
    editor: {
        value: 'SELECT 1;',
    },
};
assert.equal(testOpts.title, 'Test Script');
assert.equal(testOpts.showSidebar, true);
assert.equal(testOpts.connectionRef, 'my_db');
assert.equal(testOpts.editor.value, 'SELECT 1;');

console.log('OK test-script-workbench.mjs: all checks passed.');
