import assert from 'node:assert/strict';
import { createStudioSqlMutationService } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-sql-mutations.js';

// Setup mock state and service environment
let notifyCalls = [];
const mockFeedback = {
    notify(msg, opts) {
        notifyCalls.push({ msg, opts });
    }
};

let undoOffers = [];
const offerUndo = (label, payload) => {
    undoOffers.push({ label, payload });
};

let visualStageRendered = false;
const renderVisualStage = () => {
    visualStageRendered = true;
};

let workflowRendered = null;
const renderWorkflow = (doc, designState) => {
    workflowRendered = { doc, designState };
};

let tabsRendered = false;
const renderTabs = () => {
    tabsRendered = true;
};

let mockActiveDoc = {
    content: 'SELECT 1;',
    isDirty: false,
    studioContext: {}
};

let mockContext = {
    activeFilters: {
        amount: { scope: 'visual', target: 'v1', kind: 'number', minimum: 10, maximum: 50 },
        category: { scope: 'dataset', target: 'ds1', kind: 'categorical', values: ['Books', 'Music'] },
        ignored: { scope: 'other', target: 'v2', kind: 'text' }
    },
    snapshot: { source: 'ds1' }
};

const routes = {
    queryFilter: '/api/designer/query-filter',
    parse: '/api/designer/parse',
    patch: '/api/designer/patch',
    pipelineTask: '/api/designer/pipeline-task'
};

const apiCalls = [];
let mockApiResponse = {};
const designerApiJson = async (route, body) => {
    apiCalls.push({ route, body });
    if (typeof mockApiResponse[route] === 'function') {
        return mockApiResponse[route](body);
    }
    return mockApiResponse[route] || {};
};

const editorInstance = {
    _value: 'SELECT 1;',
    getValue() {
        return this._value;
    },
    replaceAll(val) {
        this._value = val;
        return { from: 0, to: val.length };
    },
    revealRange(from, to) {
        this._revealed = { from, to };
    }
};

const designerInstance = {
    async applyScriptText(script) {
        this._applied = script;
        return { designState: { pages: [{ id: 'p1', visuals: [] }] } };
    }
};

const state = {
    selectedVisualId: 'v1',
    editorInstance,
    designerInstance
};

const service = createStudioSqlMutationService({
    state,
    getActiveDocument: () => mockActiveDoc,
    activeDocumentContext: () => mockContext,
    designerApiJson,
    routes,
    renderVisualStage,
    renderWorkflow,
    renderTabs,
    offerUndo,
    feedback: mockFeedback
});

// 1. filterContract
const fc1 = service.filterContract('amount', {
    kind: 'number',
    minimum: 10,
    maximum: 50,
    operator: 'BETWEEN'
});
assert.equal(fc1.id, 'amount');
assert.equal(fc1.column, 'amount');
assert.equal(fc1.kind, 'number');
assert.equal(fc1.minimum, '10');
assert.equal(fc1.maximum, '50');
assert.equal(fc1.operator, 'BETWEEN');
assert.equal(fc1.values, null);

const fc2 = service.filterContract('category', {
    id: 'custom_cat',
    kind: 'categorical',
    values: ['Tech', 'Science']
});
assert.equal(fc2.id, 'custom_cat');
assert.equal(fc2.column, 'category');
assert.deepEqual(fc2.values, ['Tech', 'Science']);
assert.equal(fc2.minimum, null);
assert.equal(fc2.maximum, null);

// 2. matchingFilters
const matchingVisual = service.matchingFilters(mockContext, 'visual', 'v1');
assert.equal(matchingVisual.length, 1);
assert.equal(matchingVisual[0].column, 'amount');

const matchingDataset = service.matchingFilters(mockContext, 'dataset', 'ds1');
assert.equal(matchingDataset.length, 1);
assert.equal(matchingDataset[0].column, 'category');

const matchingNone = service.matchingFilters(mockContext, 'visual', 'v999');
assert.equal(matchingNone.length, 0);

// 3. findDesignerVisual
const mockDesignState = {
    pages: [
        {
            id: 'p1',
            visuals: [
                { id: 'v1', name: 'RevenueChart', dataset: 'ds1', options: {} },
                { id: 'v2', name: 'SalesTable', dataset: 'ds1', options: { inline_source: 'SELECT * FROM Sales' } }
            ]
        },
        {
            id: 'p2',
            visuals: [
                { id: 'v3', name: 'KpiCard', dataset: 'ds2', options: {} }
            ]
        }
    ],
    datasets: [
        { id: 'ds1', name: 'Sales', query: 'SELECT * FROM orders' },
        { id: 'ds2', name: 'Targets', query: 'SELECT * FROM goals' }
    ]
};

assert.equal(service.findDesignerVisual(mockDesignState, 'v1')?.name, 'RevenueChart');
assert.equal(service.findDesignerVisual(mockDesignState, 'SalesTable')?.id, 'v2');
assert.equal(service.findDesignerVisual(mockDesignState, 'v3')?.name, 'KpiCard');
assert.equal(service.findDesignerVisual(mockDesignState, 'non_existent'), null);

// 4. uniqueVisualName
assert.equal(service.uniqueVisualName(mockDesignState, 'NewVisual'), 'NewVisual');
assert.equal(service.uniqueVisualName(mockDesignState, 'revenuechart'), 'revenuechart_2');
assert.equal(service.uniqueVisualName(mockDesignState, 'RevenueChart'), 'RevenueChart_2');

const stateWithCollision = {
    pages: [
        {
            visuals: [
                { id: 'x1', name: 'Chart' },
                { id: 'x2', name: 'Chart_2' }
            ]
        }
    ]
};
assert.equal(service.uniqueVisualName(stateWithCollision, 'Chart'), 'Chart_3');

// 5. resolveFilterTarget
// Dataset scope
const resolvedDs = service.resolveFilterTarget(mockDesignState, { scope: 'dataset', target: 'Targets' });
assert.equal(resolvedDs.scope, 'dataset');
assert.equal(resolvedDs.target, 'Targets');
assert.equal(resolvedDs.source, 'SELECT * FROM goals');

// Dataset fallback
const resolvedDsFallback = service.resolveFilterTarget({ datasets: [{ name: 'DefaultDS', query: 'SELECT 1' }] }, { scope: 'dataset', target: 'unknown' });
assert.equal(resolvedDsFallback.target, 'DefaultDS');

// Dataset error when none exist
assert.throws(() => {
    service.resolveFilterTarget({ datasets: [] }, { scope: 'dataset', target: 'missing' });
}, /Add or select a CREATE DATASET/);

// Visual scope
const resolvedVis = service.resolveFilterTarget(mockDesignState, { scope: 'visual', target: 'v2' });
assert.equal(resolvedVis.scope, 'visual');
assert.equal(resolvedVis.target, 'SalesTable');
assert.equal(resolvedVis.source, 'SELECT * FROM Sales');

// Visual scope error when visual not found
assert.throws(() => {
    service.resolveFilterTarget(mockDesignState, { scope: 'visual', target: 'missing_visual' });
}, /Select a visual before applying a visual-local filter/);

// 6. composeFilteredSource
mockApiResponse[routes.queryFilter] = ({ source, filters }) => ({
    source: `${source} WHERE ${filters.map(f => f.column).join(' AND ')}`
});
const filteredSql = await service.composeFilteredSource('SELECT * FROM tbl', [{ column: 'active' }]);
assert.equal(filteredSql, 'SELECT * FROM tbl WHERE active');

// 7. canonicalDesignerMutation
visualStageRendered = false;
tabsRendered = false;
undoOffers = [];
mockApiResponse[routes.parse] = () => ({
    designState: {
        pages: [{ id: 'p1', name: 'Page 1', visuals: [] }],
        datasets: []
    }
});
mockApiResponse[routes.patch] = () => ({
    script: 'SELECT 1; -- patched'
});

const mutationRes = await service.canonicalDesignerMutation('Add Visual', (ds) => {
    ds.pages[0].visuals.push({ id: 'v_new', name: 'New_Visual' });
    return 'created_v_new';
});

assert.equal(mutationRes, 'created_v_new');
assert.equal(mockActiveDoc.content, 'SELECT 1; -- patched');
assert.equal(mockActiveDoc.isDirty, true);
assert.equal(editorInstance._value, 'SELECT 1; -- patched');
assert.equal(visualStageRendered, true);
assert.equal(tabsRendered, true);
assert.equal(undoOffers.length, 1);
assert.equal(undoOffers[0].label, 'Add Visual');
assert.equal(undoOffers[0].payload.after, 'SELECT 1; -- patched');

// canonicalDesignerMutation failure path
notifyCalls = [];
mockApiResponse[routes.parse] = () => ({ error: 'Syntax error on line 5' });
const failedMutationRes = await service.canonicalDesignerMutation('Broken Mutation', () => {});
assert.equal(failedMutationRes, null);
assert.equal(notifyCalls.length, 1);
assert.match(notifyCalls[0].msg, /Broken Mutation failed: Syntax error on line 5/);
assert.equal(notifyCalls[0].opts.tone, 'error');

// 7b. Reject stale visual edits before they overwrite newer typing
notifyCalls = [];
mockActiveDoc.content = 'SELECT 1;';
mockActiveDoc.contentRevision = 1;
editorInstance._value = 'SELECT 1;';
mockApiResponse[routes.parse] = () => ({
    designState: {
        pages: [{ id: 'p1', name: 'Page 1', visuals: [] }],
        datasets: []
    }
});
mockApiResponse[routes.patch] = () => {
    // Simulate user typing while the patch was processing
    editorInstance._value = 'SELECT 1; -- user typed this while waiting';
    mockActiveDoc.content = 'SELECT 1; -- user typed this while waiting';
    mockActiveDoc.contentRevision = 2;
    return { script: 'SELECT 1; -- stale patch' };
};

const conflictRes = await service.canonicalDesignerMutation('Add Visual Concurrent', (ds) => {
    ds.pages[0].visuals.push({ id: 'v_conflict', name: 'Conflict_Visual' });
    return 'created_v_conflict';
});

// Patch must be rejected to preserve user's newer typing:
assert.equal(conflictRes, null);
assert.equal(editorInstance._value, 'SELECT 1; -- user typed this while waiting');
assert.equal(mockActiveDoc.content, 'SELECT 1; -- user typed this while waiting');
assert.equal(notifyCalls.length, 1);
assert.match(notifyCalls[0].msg, /cancelled because the script was modified while processing/);
assert.equal(notifyCalls[0].opts.tone, 'warning');

// 8. canonicalPipelineMutation & canonicalScriptMutation
visualStageRendered = false;
undoOffers = [];
mockApiResponse[routes.pipelineTask] = ({ script, task }) => ({
    applied: true,
    script: `${script}\n-- added task ${task}`
});

const scriptMutationRes = await service.canonicalPipelineMutation('Add Task', { task: 'load_data' });
assert.equal(scriptMutationRes.applied, true);
assert.match(mockActiveDoc.content, /-- added task load_data/);
assert.equal(undoOffers.length, 1);
assert.equal(undoOffers[0].label, 'Add Task');

// 8b. Reject stale pipeline edits when user typed meanwhile
notifyCalls = [];
mockActiveDoc.content = 'SELECT 100;';
mockActiveDoc.contentRevision = 1;
editorInstance._value = 'SELECT 100;';
mockApiResponse[routes.pipelineTask] = () => {
    editorInstance._value = 'SELECT 100; -- user typed newer content';
    mockActiveDoc.content = 'SELECT 100; -- user typed newer content';
    mockActiveDoc.contentRevision = 2;
    return { applied: true, script: 'SELECT 100;\n-- task applied' };
};

const pipelineConflictRes = await service.canonicalPipelineMutation('Concurrent Task', { task: 't1' });
assert.equal(pipelineConflictRes, null);
assert.equal(editorInstance._value, 'SELECT 100; -- user typed newer content');
assert.equal(mockActiveDoc.content, 'SELECT 100; -- user typed newer content');
assert.equal(notifyCalls.length, 1);
assert.match(notifyCalls[0].msg, /cancelled because the script was modified while processing/);
assert.equal(notifyCalls[0].opts.tone, 'warning');

// Refused mutation
notifyCalls = [];
mockApiResponse[routes.pipelineTask] = () => ({
    applied: false,
    error: 'Task label already exists'
});
const refusedRes = await service.canonicalPipelineMutation('Duplicate Task', { task: 'dup' });
assert.equal(refusedRes, null);
assert.equal(notifyCalls.length, 1);
assert.match(notifyCalls[0].msg, /Duplicate Task failed: Task label already exists/);

// 9. persistFilter
mockApiResponse[routes.parse] = () => ({
    designState: {
        pages: [
            { id: 'p1', visuals: [{ id: 'v1', name: 'v1', options: { inline_source: 'SELECT * FROM tbl' } }] }
        ],
        datasets: []
    }
});
mockApiResponse[routes.patch] = () => ({
    script: 'SELECT * FROM tbl WHERE amount BETWEEN 10 AND 50'
});
const persistTarget = await service.persistFilter('amount');
assert.equal(persistTarget, 'v1');

console.log('test-studio-sql-mutations: all checks passed');
