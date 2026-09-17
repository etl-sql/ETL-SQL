import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
    createStudioWorkbench,
    secureStudioScriptForSave,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js';

// ── 1. Exports & signatures ──────────────────────────────────────────────────
assert.equal(typeof createStudioWorkbench, 'function', 'createStudioWorkbench must be exported as a function');
assert.ok(createStudioWorkbench.length >= 1, 'createStudioWorkbench must accept at least (container, options)');
assert.equal(typeof secureStudioScriptForSave, 'function', 'secureStudioScriptForSave must be re-exported');

// ── 2. Options contract validation ──────────────────────────────────────────
const testOpts = {
    host: 'portal',
    apiBase: '/api/studio',
    initialDoc: {
        id: 'doc1',
        name: 'Report 1',
        content: 'PAGE "Overview";',
        path: 'reports/overview.rptsql'
    },
    capabilities: ['StudioAccess', 'ScriptSave'],
};
assert.equal(testOpts.host, 'portal');
assert.equal(testOpts.apiBase, '/api/studio');
assert.equal(testOpts.initialDoc.name, 'Report 1');
assert.ok(testOpts.capabilities.includes('StudioAccess'));

// ── 3. Source contracts ─────────────────────────────────────────────────────
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const studioSource = readFileSync(path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js'), 'utf8');

// Ensure native alert/confirm are not used directly
assert.doesNotMatch(studioSource, /\bwindow\.alert\(/, 'window.alert must not be called directly in studio.js');
assert.doesNotMatch(studioSource, /\bwindow\.confirm\(/, 'window.confirm must not be called directly in studio.js');

// Ensure key routes are referenced via STUDIO_ROUTES
assert.match(studioSource, /STUDIO_ROUTES/, 'STUDIO_ROUTES must be used for route coordination');
assert.match(studioSource, /STUDIO_CATALOG_ROUTES/, 'STUDIO_CATALOG_ROUTES must be used for catalog routes');

// ── 4. Primary-editor and degraded mode contracts ────────────────────────────
// Degraded mode must show alert banner and offer Retry
assert.match(studioSource, /etlsql-studio-degraded-banner/, 'Must render degraded mode banner when CodeMirror fails to load');
assert.match(studioSource, /isEditorDegraded\s*=\s*true/, 'Must mark editor as degraded on failure');
assert.match(studioSource, /mountScriptEditor\(\)/, 'Must provide retry action to re-initialize editor');

// Failed file read must not open clean empty document
assert.match(studioSource, /loadFailed/, 'openWorkspaceFile must track load failure');
assert.match(studioSource, /The file was not opened to prevent overwriting/, 'openWorkspaceFile must warn and refuse to open empty doc on failure');

// Isolated editor state and history per document
assert.match(studioSource, /editor\.getState/, 'switchDoc must preserve editor state when switching away');
assert.match(studioSource, /editor\.setState/, 'switchDoc must restore editor state per document instead of polluting undo history');
assert.match(studioSource, /editor\.createDocState/, 'switchDoc must create isolated editor state for new documents');

// ── 5. Accessibility & Keyboard Navigation contracts ────────────────────────
assert.match(studioSource, /role="tablist"/, 'tabsContainer must declare role=tablist');
assert.match(studioSource, /aria-label="Open documents"/, 'tabsContainer must declare accessible aria-label');
assert.match(studioSource, /setAttribute\('role',\s*'tab'\)/, 'Each tab must declare role=tab');
assert.match(studioSource, /setAttribute\('aria-selected'/, 'Each tab must track aria-selected state');
assert.match(studioSource, /setAttribute\('tabindex'/, 'Tabs must manage tabindex roving focus');
assert.match(studioSource, /handleTabKeydown/, 'Tabs must implement keyboard navigation handler');
assert.match(studioSource, /role="separator"/, 'Stage resizer must declare role=separator');
assert.match(studioSource, /aria-orientation="horizontal"/, 'Stage resizer must declare orientation');
assert.match(studioSource, /applySplitPct/, 'Resizer must support keyboard adjustment');
assert.match(studioSource, /setupModalAccessibility/, 'Modals must use setupModalAccessibility helper for focus trap and ARIA dialog semantics');

const authoringSource = readFileSync(path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring.js'), 'utf8');
assert.match(authoringSource, /setAttribute\('role',\s*'dialog'\)/, 'studioDialog must declare dialog role');
assert.match(authoringSource, /setAttribute\('aria-modal',\s*'true'\)/, 'studioDialog must declare aria-modal');
assert.match(authoringSource, /setAttribute\('aria-labelledby',\s*'etlsql-studio-dialog-title'\)/, 'studioDialog must bind title via aria-labelledby');
assert.match(authoringSource, /previouslyFocused/, 'studioDialog must track previously focused element');

console.log('OK test-studio-entry.mjs: all studio entry checks passed.');
