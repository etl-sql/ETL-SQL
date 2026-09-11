import assert from 'node:assert/strict';
import {
    VISUAL_ROLES,
    STUDIO_VISUAL_GROUPS,
    rolesForVisualType,
    missingRequiredRoles,
    CHART_AGGREGATES,
    aggregateExpression,
    defaultAggregateAlias,
    buildAggregatedSource,
    aggregateRows,
    aggregationFromSource,
    renderVisualSample,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/visual-preview.js';

// 1. VISUAL_ROLES and rolesForVisualType
assert.ok(VISUAL_ROLES.BAR);
assert.ok(VISUAL_ROLES.LINE);
assert.ok(VISUAL_ROLES.SCATTER);
assert.ok(VISUAL_ROLES.PIE);
assert.ok(VISUAL_ROLES.GAUGE);
assert.ok(VISUAL_ROLES.HEATMAP);
assert.ok(VISUAL_ROLES.CARD);
assert.ok(VISUAL_ROLES.MATRIX);
assert.ok(VISUAL_ROLES.TABLE);
assert.ok(VISUAL_ROLES.SLICER);
assert.deepEqual(VISUAL_ROLES.TEXT, []);

// Roles lookup by direct name (case-insensitive)
assert.equal(rolesForVisualType('bar'), VISUAL_ROLES.BAR);
assert.equal(rolesForVisualType('BAR'), VISUAL_ROLES.BAR);
assert.equal(rolesForVisualType('Line'), VISUAL_ROLES.LINE);

// Aliases
assert.equal(rolesForVisualType('HBAR'), VISUAL_ROLES.BAR);
assert.equal(rolesForVisualType('COLUMN'), VISUAL_ROLES.BAR);
assert.equal(rolesForVisualType('WATERFALL'), VISUAL_ROLES.BAR);
assert.equal(rolesForVisualType('AREA'), VISUAL_ROLES.LINE);
assert.equal(rolesForVisualType('COMBO'), VISUAL_ROLES.LINE);
assert.equal(rolesForVisualType('DONUT'), VISUAL_ROLES.PIE);
assert.equal(rolesForVisualType('FUNNEL'), VISUAL_ROLES.PIE);
assert.equal(rolesForVisualType('TREEMAP'), VISUAL_ROLES.PIE);
assert.equal(rolesForVisualType('SUNBURST'), VISUAL_ROLES.PIE);
assert.equal(rolesForVisualType('BUBBLE'), VISUAL_ROLES.SCATTER);
assert.equal(rolesForVisualType('MULTISELECT'), VISUAL_ROLES.SLICER);
assert.equal(rolesForVisualType('DATEPICKER'), VISUAL_ROLES.SLICER);

// Unknown type falls back to BAR
assert.equal(rolesForVisualType('UNKNOWN_TYPE'), VISUAL_ROLES.BAR);
assert.equal(rolesForVisualType(null), VISUAL_ROLES.BAR);
assert.equal(rolesForVisualType(undefined), VISUAL_ROLES.BAR);

// 2. missingRequiredRoles
assert.deepEqual(
    missingRequiredRoles({ type: 'BAR', mappings: { X: 'category' } }),
    ['Value (Y)']
);
assert.deepEqual(
    missingRequiredRoles({ type: 'BAR', mappings: { X: 'category', Y: 'val' } }),
    []
);
// TABLE uses repeatable COLUMNS
assert.deepEqual(
    missingRequiredRoles({ type: 'TABLE', mappings: {} }),
    ['Columns']
);
assert.deepEqual(
    missingRequiredRoles({ type: 'TABLE', mappings: { COLUMN1: 'col_a' } }),
    []
);
assert.deepEqual(
    missingRequiredRoles(null),
    ['Category (X)', 'Value (Y)']
);

// 3. STUDIO_VISUAL_GROUPS
assert.equal(STUDIO_VISUAL_GROUPS.length, 4);
const groupNames = STUDIO_VISUAL_GROUPS.map(g => g.name);
assert.ok(groupNames.includes('Charts'));
assert.ok(groupNames.includes('Data & Content'));
assert.ok(groupNames.includes('Filters & Inputs'));
assert.ok(groupNames.includes('Layout & Actions'));

const chartsGroup = STUDIO_VISUAL_GROUPS.find(g => g.name === 'Charts');
assert.ok(chartsGroup.types.includes('BAR'));
assert.ok(chartsGroup.types.includes('LINE'));
assert.ok(chartsGroup.types.includes('PIE'));

// 4. CHART_AGGREGATES, aggregateExpression, defaultAggregateAlias
assert.equal(CHART_AGGREGATES.length, 7);
assert.equal(aggregateExpression('COUNT', 'id'), 'COUNT(id)');
assert.equal(aggregateExpression('COUNT_DISTINCT', 'user_id'), 'COUNT(DISTINCT user_id)');
assert.equal(aggregateExpression('SUM', 'amount'), 'SUM(amount)');
assert.equal(aggregateExpression('AVG', 'price'), 'AVG(price)');

// defaultAggregateAlias strips trailing _id for count aggregates
assert.equal(defaultAggregateAlias('COUNT', 'user_id'), 'user_count');
assert.equal(defaultAggregateAlias('COUNT_DISTINCT', 'customer_id'), 'customer_distinct_count');
assert.equal(defaultAggregateAlias('SUM', 'sales_amount'), 'sales_amount_sum');
assert.equal(defaultAggregateAlias('AVG', 'price'), 'price_avg');
assert.equal(defaultAggregateAlias('MIN', 'score'), 'score_min');
assert.equal(defaultAggregateAlias('MAX', 'score'), 'score_max');
assert.equal(defaultAggregateAlias('COUNT', null), 'value_count');

// 5. buildAggregatedSource
const builtSql = buildAggregatedSource({
    base: 'orders',
    groupBy: ['region', 'category'],
    measure: { aggregate: 'SUM', column: 'amount', alias: 'total_sales' },
});
assert.equal(builtSql, '(SELECT region, category, SUM(amount) AS total_sales FROM orders GROUP BY region, category)');

const inlineBaseSql = buildAggregatedSource({
    base: '(SELECT * FROM raw_data)',
    groupBy: ['team'],
    measure: { aggregate: 'COUNT', column: 'member_id', alias: 'member_count' },
});
assert.equal(inlineBaseSql, '(SELECT team, COUNT(member_id) AS member_count FROM (SELECT * FROM raw_data) AS source_rows GROUP BY team)');

const noGroupingSql = buildAggregatedSource({
    base: 'metrics',
    groupBy: [],
    measure: { aggregate: 'AVG', column: 'latency', alias: 'avg_latency' },
});
assert.equal(noGroupingSql, '(SELECT AVG(latency) AS avg_latency FROM metrics)');

// 6. aggregationFromSource
const parsedAgg = aggregationFromSource('(SELECT region, category, SUM(amount) AS total_sales FROM orders GROUP BY region, category)');
assert.ok(parsedAgg);
assert.deepEqual(parsedAgg.groupBy, ['region', 'category']);
assert.equal(parsedAgg.measure.aggregate, 'SUM');
assert.equal(parsedAgg.measure.column, 'amount');
assert.equal(parsedAgg.measure.alias, 'total_sales');

const parsedDistinct = aggregationFromSource('(SELECT dept, COUNT(DISTINCT emp_id) AS distinct_emps FROM staff GROUP BY dept)');
assert.ok(parsedDistinct);
assert.deepEqual(parsedDistinct.groupBy, ['dept']);
assert.equal(parsedDistinct.measure.aggregate, 'COUNT_DISTINCT');
assert.equal(parsedDistinct.measure.column, 'emp_id');
assert.equal(parsedDistinct.measure.alias, 'distinct_emps');

assert.equal(aggregationFromSource('SELECT * FROM orders'), null);
assert.equal(aggregationFromSource(null), null);
assert.equal(aggregationFromSource(''), null);
// Mismatched grouping vs projection returns null
assert.equal(aggregationFromSource('(SELECT region, state, SUM(amount) AS total FROM orders GROUP BY region)'), null);

// 7. aggregateRows
const sampleRows = [
    { dept: 'Eng', sub: 'Dev', salary: 100 },
    { dept: 'Eng', sub: 'QA', salary: 80 },
    { dept: 'HR', sub: 'Recruit', salary: 70 },
    { dept: 'HR', sub: 'Ops', salary: 90 },
];
const aggResult = aggregateRows(
    { columns: ['dept', 'sub', 'salary'], rows: sampleRows },
    { groupBy: ['dept'], measure: { aggregate: 'SUM', column: 'salary', alias: 'dept_total' } }
);
assert.equal(aggResult.rowCount, 2);
assert.deepEqual(aggResult.columns, ['dept', 'dept_total']);
assert.deepEqual(aggResult.rows, [
    { dept: 'Eng', dept_total: 180 },
    { dept: 'HR', dept_total: 160 },
]);

// Positional array rows
const posSample = {
    columns: ['category', 'qty'],
    rows: [
        ['Widgets', 10],
        ['Widgets', 20],
        ['Gadgets', 5],
    ],
};
const posAggResult = aggregateRows(
    posSample,
    { groupBy: ['category'], measure: { aggregate: 'SUM', column: 'qty', alias: 'total_qty' } }
);
assert.equal(posAggResult.rowCount, 2);
assert.deepEqual(posAggResult.rows, [
    { category: 'Widgets', total_qty: 30 },
    { category: 'Gadgets', total_qty: 5 },
]);

// Test aggregates: COUNT, COUNT_DISTINCT, AVG, MIN, MAX
const aggTypesSample = {
    columns: ['cat', 'val'],
    rows: [
        { cat: 'A', val: 10 },
        { cat: 'A', val: 20 },
        { cat: 'A', val: 20 },
        { cat: 'A', val: null },
    ],
};
assert.equal(aggregateRows(aggTypesSample, { groupBy: ['cat'], measure: { aggregate: 'COUNT', column: 'val', alias: 'res' } }).rows[0].res, 3);
assert.equal(aggregateRows(aggTypesSample, { groupBy: ['cat'], measure: { aggregate: 'COUNT_DISTINCT', column: 'val', alias: 'res' } }).rows[0].res, 2);
assert.equal(aggregateRows(aggTypesSample, { groupBy: ['cat'], measure: { aggregate: 'AVG', column: 'val', alias: 'res' } }).rows[0].res, 12.5);
assert.equal(aggregateRows(aggTypesSample, { groupBy: ['cat'], measure: { aggregate: 'MIN', column: 'val', alias: 'res' } }).rows[0].res, 0);
assert.equal(aggregateRows(aggTypesSample, { groupBy: ['cat'], measure: { aggregate: 'MAX', column: 'val', alias: 'res' } }).rows[0].res, 20);

// Single aggregate collapse (empty groupBy)
const singleCardAgg = aggregateRows(
    aggTypesSample,
    { groupBy: [], measure: { aggregate: 'SUM', column: 'val', alias: 'sum_val' } }
);
assert.equal(singleCardAgg.rowCount, 1);
assert.equal(singleCardAgg.rows[0].sum_val, 50);

// 8. renderVisualSample
const mockHost = { innerHTML: '' };

// Null host safe
renderVisualSample(null, { type: 'BAR' }, { columns: [], rows: [] });

// Empty sample
renderVisualSample(mockHost, { type: 'BAR', mappings: { X: 'dept', Y: 'total' } }, { columns: [], rows: [] });
assert.ok(mockHost.innerHTML.includes('etlsql-visual-preview-empty'));
assert.ok(mockHost.innerHTML.includes('No sample rows'));

// Missing required roles
renderVisualSample(mockHost, { type: 'BAR', mappings: { X: 'dept' } }, { columns: ['dept'], rows: [{ dept: 'Eng' }] });
assert.ok(mockHost.innerHTML.includes('etlsql-visual-preview-empty'));
assert.ok(mockHost.innerHTML.includes('Assign Value (Y)'));

// TABLE rendering
const tableData = {
    columns: ['id', 'name', 'score'],
    rows: [{ id: 1, name: 'Alice', score: 95 }, { id: 2, name: 'Bob', score: 88 }],
};
renderVisualSample(mockHost, { type: 'TABLE', mappings: { COLUMN1: 'name', COLUMN2: 'score' } }, tableData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-table"'));
assert.ok(mockHost.innerHTML.includes('<th>name</th>'));
assert.ok(mockHost.innerHTML.includes('<td>Alice</td>'));

// MATRIX rendering (with and without COL)
const matrixData = {
    columns: ['region', 'quarter', 'revenue'],
    rows: [
        { region: 'East', quarter: 'Q1', revenue: 100 },
        { region: 'East', quarter: 'Q2', revenue: 120 },
        { region: 'West', quarter: 'Q1', revenue: 80 },
    ],
};
renderVisualSample(mockHost, { type: 'MATRIX', mappings: { ROW: 'region', COL: 'quarter', VALUE: 'revenue' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-table"'));
assert.ok(mockHost.innerHTML.includes('<th>region</th>'));
assert.ok(mockHost.innerHTML.includes('<th>Q1</th>'));

renderVisualSample(mockHost, { type: 'MATRIX', mappings: { ROW: 'region', VALUE: 'revenue' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('<th>revenue</th>'));

// CARD rendering
renderVisualSample(mockHost, { type: 'CARD', mappings: { VALUE: 'revenue', LABEL: 'Total Revenue', GOAL: 'revenue' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-card"'));
assert.ok(mockHost.innerHTML.includes('Total Revenue'));
assert.ok(mockHost.innerHTML.includes('Goal'));

// GAUGE rendering
renderVisualSample(mockHost, { type: 'GAUGE', mappings: { VALUE: 'revenue' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('<svg viewBox="0 0 200 120"'));

// PIE, DONUT, FUNNEL, TREEMAP
renderVisualSample(mockHost, { type: 'PIE', mappings: { LABEL: 'region', VALUE: 'revenue' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-share"'));

renderVisualSample(mockHost, { type: 'DONUT', mappings: { LABEL: 'region', VALUE: 'revenue' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-share"'));

renderVisualSample(mockHost, { type: 'FUNNEL', mappings: { LABEL: 'region', VALUE: 'revenue' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-bars"'));

renderVisualSample(mockHost, { type: 'TREEMAP', mappings: { LABEL: 'region', VALUE: 'revenue' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-bars"'));

// HEATMAP rendering
renderVisualSample(mockHost, { type: 'HEATMAP', mappings: { X: 'quarter', Y: 'region', VALUE: 'revenue' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-heatmap"'));

// SLICER rendering
renderVisualSample(mockHost, { type: 'SLICER', mappings: { VALUE: 'region' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-slicer"'));
assert.ok(mockHost.innerHTML.includes('<button type="button" disabled>East</button>'));

// SCATTER rendering
const scatterData = {
    columns: ['x_val', 'y_val', 'grp'],
    rows: [{ x_val: 10, y_val: 20, grp: 'A' }, { x_val: 15, y_val: 25, grp: 'B' }],
};
renderVisualSample(mockHost, { type: 'SCATTER', mappings: { X: 'x_val', Y: 'y_val', SERIES: 'grp' } }, scatterData);
assert.ok(mockHost.innerHTML.includes('<svg viewBox="0 0 360 180"'));

// BAR and LINE series rendering
renderVisualSample(mockHost, { type: 'BAR', mappings: { X: 'region', Y: 'revenue', SERIES: 'quarter' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('<svg viewBox="0 0 360 180"'));
assert.ok(mockHost.innerHTML.includes('<rect'));

renderVisualSample(mockHost, { type: 'LINE', mappings: { X: 'region', Y: 'revenue', SERIES: 'quarter' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('<svg viewBox="0 0 360 180"'));
assert.ok(mockHost.innerHTML.includes('<polyline'));

renderVisualSample(mockHost, { type: 'AREA', mappings: { X: 'region', Y: 'revenue', SERIES: 'quarter' } }, matrixData);
assert.ok(mockHost.innerHTML.includes('<polygon'));

// Inline source aggregation automatic reshaping
const visualWithDerived = {
    type: 'CARD',
    mappings: { VALUE: 'total_rev', LABEL: 'Rev' },
    options: {
        inline_source: '(SELECT SUM(revenue) AS total_rev FROM orders)',
    },
};
renderVisualSample(mockHost, visualWithDerived, matrixData);
assert.ok(mockHost.innerHTML.includes('class="etlsql-visual-preview-card"'));
assert.ok(mockHost.innerHTML.includes('300'));

console.log('test-visual-preview: all checks passed');
