import assert from 'node:assert/strict';
import {
    declaredConnectionNames,
    createStudioAuthoringSurfaces,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring.js';

// ── 1. declaredConnectionNames extraction contract ───────────────────────────
// Empty / invalid script handling
assert.deepEqual(declaredConnectionNames(''), []);
assert.deepEqual(declaredConnectionNames('   '), []);
assert.deepEqual(declaredConnectionNames(null), []);
assert.deepEqual(declaredConnectionNames(undefined), []);
assert.deepEqual(declaredConnectionNames(123), []);
assert.deepEqual(declaredConnectionNames('SELECT 1 FROM #temp;'), []);

// Basic CREATE CONNECTION
const script1 = `
CREATE CONNECTION pg_main POSTGRES (HOST = 'localhost', PORT = 5432, DATABASE = 'app');
CREATE CONNECTION snowflake_dw SNOWFLAKE (ACCOUNT = 'xy12345', WAREHOUSE = 'COMPUTE_WH');
`;
assert.deepEqual(declaredConnectionNames(script1), ['pg_main', 'snowflake_dw']);

// Variants: IF NOT EXISTS, OR REPLACE, and bracketed names
const script2 = `
CREATE CONNECTION IF NOT EXISTS [sql_server] MSSQL (SERVER = 'db.local');
CREATE OR REPLACE CONNECTION [analytics_db] DUCKDB (PATH = 'data/lake.duckdb');
CREATE CONNECTION lake_mysql MYSQL (HOST = '10.0.0.1');
`;
assert.deepEqual(declaredConnectionNames(script2), ['sql_server', 'analytics_db', 'lake_mysql']);

// Multiple occurrences preserved in order
const script3 = `
CREATE CONNECTION dw POSTGRES (DATABASE = 'dw1');
CREATE OR REPLACE CONNECTION dw POSTGRES (DATABASE = 'dw2');
`;
assert.deepEqual(declaredConnectionNames(script3), ['dw', 'dw']);

// ── 2. createStudioAuthoringSurfaces factory contract ─────────────────────────
assert.equal(typeof createStudioAuthoringSurfaces, 'function');

// Minimal mock shell and environment
const mockShell = {
    setScriptText() {},
    getScriptText() { return ''; },
    focus() {},
};

const mockRoutes = {
    preview: '/api/designer/preview',
    schema: '/api/designer/schema',
    mutate: '/api/designer/mutate',
};

const mockCatalogRoutes = {
    tables: '/api/catalog/tables',
};

let mockContext = { snapshot: null };

const surfaces = createStudioAuthoringSurfaces({
    dialog: () => ({
        open() {},
        close() {},
        render() {},
        isOpen: () => false,
    }),
    routes: mockRoutes,
    catalogRoutes: mockCatalogRoutes,
    request: async () => ({}),
    editorTransport: {},
    getActiveDocument: () => null,
    activeContext: () => mockContext,
    contextFor: () => null,
    mutate: async () => ({ ok: true }),
    uniqueVisualName: (prefix) => `${prefix}_1`,
    hasWorkspaceHost: () => false,
    feedback: () => {},
    shell: mockShell,
});

assert.ok(surfaces, 'createStudioAuthoringSurfaces returns handle');

// Verify all 15 surface methods
const expectedMethods = [
    'openDataWizard',
    'openPipelineTaskEditor',
    'openPipelineRunPlanConfirm',
    'openChartBuilder',
    'runChooseDataStep',
    'runParameterStep',
    'runDetailsStep',
    'runTotalsStep',
    'runFurnitureStep',
    'runPreviewStep',
    'runExportStep',
    'runVisualsStep',
    'runCrossFilterStep',
    'hasDataSample',
    'visualSourceBinding',
];

for (const method of expectedMethods) {
    assert.equal(typeof surfaces[method], 'function', `Expected ${method} to be a function`);
}

// ── 3. hasDataSample & visualSourceBinding helper logic ───────────────────────
mockContext = { snapshot: null };
assert.equal(surfaces.hasDataSample(), false);

mockContext = { snapshot: {} };
assert.equal(surfaces.hasDataSample(), false);

mockContext = { snapshot: { source: 'sales', columns: [] } };
assert.equal(surfaces.hasDataSample(), false);

mockContext = { snapshot: { source: 'sales', columns: ['id', 'name'] } };
assert.equal(surfaces.hasDataSample(), true);

// visualSourceBinding with dataset (&)
mockContext = { snapshot: { source: '&my_dataset' } };
assert.deepEqual(surfaces.visualSourceBinding(), {
    dataset: '&my_dataset',
    options: {},
});

// visualSourceBinding with qualified connection table (creates inline select)
mockContext = { snapshot: { source: 'pg_conn.users' } };
assert.deepEqual(surfaces.visualSourceBinding(), {
    dataset: null,
    options: { inline_source: '(SELECT * FROM pg_conn.users)' },
});

// visualSourceBinding with null / empty source
mockContext = { snapshot: null };
assert.deepEqual(surfaces.visualSourceBinding(), {
    dataset: null,
    options: {},
});

console.log('OK test-studio-authoring.mjs: all checks passed.');
