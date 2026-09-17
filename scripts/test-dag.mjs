import assert from 'node:assert/strict';
import {
    _TYPE_COLOR,
    _nodeColor,
    _computeLayout,
    _lineageReach,
    _edgeStyle,
    renderDag,
    flattenDagColumns,
    renderCompactDag,
    renderDagCapsule,
    updateDagLines,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/dag.js';

// ── 1. _TYPE_COLOR & _nodeColor ───────────────────────────────────────────────
assert.equal(_nodeColor('dataset'), '#10b981');
assert.equal(_nodeColor('connection'), '#0ea5e9');
assert.equal(_nodeColor('visual'), '#3b82f6');
assert.equal(_nodeColor('page'), '#8b5cf6');
assert.equal(_nodeColor('container'), '#334155');
assert.equal(_nodeColor('table'), '#64748b');
assert.equal(_nodeColor('column'), '#94a3b8');
assert.equal(_nodeColor('statement'), '#475569');
assert.equal(_nodeColor('conditional'), '#f59e0b');
assert.equal(_nodeColor('loop'), '#f97316');
assert.equal(_nodeColor('parallel'), '#06b6d4');
assert.equal(_nodeColor('transaction'), '#2dd4bf');
assert.equal(_nodeColor('validation'), '#eab308');
assert.equal(_nodeColor('io'), '#14b8a6');
assert.equal(_nodeColor('outbound'), '#0f766e');
assert.equal(_nodeColor('destructive'), '#dc2626');
assert.equal(_nodeColor('procedure'), '#a855f7');

// Fallback for unknown type
assert.equal(_nodeColor('non_existent_type'), '#94a3b8');
assert.equal(_nodeColor(''), '#94a3b8');

// ── 2. _computeLayout ─────────────────────────────────────────────────────────
// Linear pipeline: A -> B -> C
const linearNodes = [{ id: 'A' }, { id: 'B' }, { id: 'C' }];
const linearEdges = [
    { source: 'A', target: 'B' },
    { source: 'B', target: 'C' },
];
const linearLayout = _computeLayout(linearNodes, linearEdges);
assert.ok(linearLayout.A);
assert.ok(linearLayout.B);
assert.ok(linearLayout.C);
assert.equal(linearLayout.A.y, 0);
assert.ok(linearLayout.B.y > linearLayout.A.y, 'B should be placed below A');
assert.ok(linearLayout.C.y > linearLayout.B.y, 'C should be placed below B');

// Branching pipeline: Root -> [Child1, Child2]
const branchNodes = [{ id: 'Root' }, { id: 'Child1' }, { id: 'Child2' }];
const branchEdges = [
    { source: 'Root', target: 'Child1' },
    { source: 'Root', target: 'Child2' },
];
const branchLayout = _computeLayout(branchNodes, branchEdges);
assert.equal(branchLayout.Child1.y, branchLayout.Child2.y, 'Siblings on same layer have same y');
assert.notEqual(branchLayout.Child1.x, branchLayout.Child2.x, 'Siblings should have distinct x');

// Disconnected / cycle graphs don't crash
const cycleNodes = [{ id: 'X' }, { id: 'Y' }];
const cycleEdges = [
    { source: 'X', target: 'Y' },
    { source: 'Y', target: 'X' },
];
const cycleLayout = _computeLayout(cycleNodes, cycleEdges);
assert.ok(cycleLayout.X);
assert.ok(cycleLayout.Y);

// ── 3. _lineageReach ──────────────────────────────────────────────────────────
// Graph: A -> B -> C, and isolated D -> E
const reachNodes = [
    { id: 'A' },
    { id: 'B' },
    { id: 'C' },
    { id: 'D' },
    { id: 'E' },
    { id: 'col_B1', meta: { parent: 'B' } },
    { id: 'col_D1', meta: { parent: 'D' } },
];
const reachEdges = [
    { source: 'A', target: 'B' },
    { source: 'B', target: 'C' },
    { source: 'D', target: 'E' },
];

// Focus on B: reach should include ancestors (A), descendants (C), itself (B), and its column children
const reachB = _lineageReach('B', reachEdges, reachNodes);
assert.ok(reachB.has('B'));
assert.ok(reachB.has('A'), 'Should include ancestor A');
assert.ok(reachB.has('C'), 'Should include descendant C');
assert.ok(reachB.has('col_B1'), 'Should include column child of focused node');
assert.ok(!reachB.has('D'), 'Should not include disconnected node D');
assert.ok(!reachB.has('E'), 'Should not include disconnected node E');
assert.ok(!reachB.has('col_D1'), 'Should not include column child of node D');

// ── 4. _edgeStyle ─────────────────────────────────────────────────────────────
assert.deepEqual(_edgeStyle('ON SUCCESS'), { kind: 'success', color: '#3fb950', dash: null });
assert.deepEqual(_edgeStyle('on success'), { kind: 'success', color: '#3fb950', dash: null });
assert.deepEqual(_edgeStyle('ON FAILURE'), { kind: 'failure', color: '#f85149', dash: '6 4' });
assert.deepEqual(_edgeStyle('on failure'), { kind: 'failure', color: '#f85149', dash: '6 4' });
assert.deepEqual(_edgeStyle('ON COMPLETION'), { kind: 'completion', color: '#58a6ff', dash: '2 3' });
assert.deepEqual(_edgeStyle('WHEN @count > 100'), { kind: 'expression', color: '#d29922', dash: '10 3 2 3' });
assert.deepEqual(_edgeStyle(''), { kind: null, color: '#64748b', dash: null });
assert.deepEqual(_edgeStyle(null), { kind: null, color: '#64748b', dash: null });
assert.deepEqual(_edgeStyle('UNLABELED'), { kind: null, color: '#64748b', dash: null });

// ── 5. flattenDagColumns ──────────────────────────────────────────────────────
const tree = [
    { name: 'Extract Step' },
    {
        isParallelBlock: true,
        children: [
            { name: 'Transform Branch A' },
            { name: 'Transform Branch B' },
        ],
    },
    {
        name: 'Container Step',
        children: [
            { name: 'Load Step 1' },
            { name: 'Load Step 2' },
        ],
    },
];

const flattened = flattenDagColumns(tree);
assert.equal(flattened.length, 4);
assert.equal(flattened[0].type, 'single');
assert.equal(flattened[0].node.name, 'Extract Step');
assert.equal(flattened[1].type, 'parallel');
assert.equal(flattened[1].nodes.length, 2);
assert.equal(flattened[1].nodes[0].name, 'Transform Branch A');
assert.equal(flattened[2].type, 'single');
assert.equal(flattened[2].node.name, 'Load Step 1');
assert.equal(flattened[3].type, 'single');
assert.equal(flattened[3].node.name, 'Load Step 2');

// Edge cases
assert.deepEqual(flattenDagColumns([]), []);
assert.deepEqual(flattenDagColumns(null), []);

// ── 6. renderDagCapsule ───────────────────────────────────────────────────────
const successNode = {
    name: 'Extract Users <script>',
    status: 'Completed',
    rowsProcessed: 12500,
    durationMs: 450.2,
};
const capsuleHtml = renderDagCapsule(successNode, 0, 1);
assert.ok(capsuleHtml.includes('status-completed'));
assert.ok(capsuleHtml.includes('data-col="0"'));
assert.ok(capsuleHtml.includes('data-row="1"'));
assert.ok(capsuleHtml.includes('✅'));
assert.ok(capsuleHtml.includes('Extract Users &lt;script&gt;'));
assert.ok(capsuleHtml.includes('12,500 rows'));
assert.ok(capsuleHtml.includes('450 ms'));

const runningNode = {
    name: 'Transform Orders',
    status: 'Running',
    rowsProcessed: 3200,
};
const runningHtml = renderDagCapsule(runningNode, 1, 0);
assert.ok(runningHtml.includes('status-running'));
assert.ok(runningHtml.includes('🔄'));

const failedNode = {
    name: 'Validation Check',
    status: 'Failed',
    rowsProcessed: 0,
};
const failedHtml = renderDagCapsule(failedNode, 2, 0);
assert.ok(failedHtml.includes('status-failed'));
assert.ok(failedHtml.includes('❌'));

// ── 7. renderCompactDag ───────────────────────────────────────────────────────
assert.equal(renderCompactDag([]), '');
assert.equal(renderCompactDag(null), '');

const compactHtml = renderCompactDag([
    { name: 'Stage Data', status: 'Success', rowsProcessed: 5000, durationMs: 120 },
    {
        isParallelBlock: true,
        children: [
            { name: 'Aggregate Sales', status: 'Success', rowsProcessed: 200, durationMs: 45 },
            { name: 'Aggregate Inventory', status: 'Running', rowsProcessed: 1500 },
        ],
    },
]);

assert.ok(compactHtml.includes('class="etlsql-compact-dag"'));
assert.ok(compactHtml.includes('class="etlsql-compact-dag-svg"'));
assert.ok(compactHtml.includes('class="etlsql-compact-dag-columns"'));
assert.ok(compactHtml.includes('Stage Data'));
assert.ok(compactHtml.includes('Aggregate Sales'));
assert.ok(compactHtml.includes('Aggregate Inventory'));

// ── 8. renderDag empty-state check ───────────────────────────────────────────
const emptyContainer = {
    innerHTML: '',
    style: {},
    classList: { add() {} },
};
const handle = renderDag(emptyContainer, { nodes: [] });
assert.ok(emptyContainer.innerHTML.includes('etlsql-dag-empty'));
assert.ok(emptyContainer.innerHTML.includes('No structure data available.'));
assert.equal(typeof handle.dispose, 'function');
assert.equal(typeof handle.resize, 'function');
assert.equal(typeof handle.showDetail, 'function');

// ── 9. updateDagLines empty container check ──────────────────────────────────
const fakeContainer = {
    querySelector() { return null; },
    getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 100 }; },
    querySelectorAll() { return []; },
};
assert.doesNotThrow(() => updateDagLines(fakeContainer));

console.log('DAG layout and rendering contract passed (all tests successful).');
