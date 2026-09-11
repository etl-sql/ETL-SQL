import assert from 'node:assert/strict';
import {
    columnName,
    columnType,
    snapshotColumns,
    updateSnapshotPackage,
    updateSnapshotPackageFromManifest,
    requestSourceSample,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-data.js';

// 1. columnName
assert.equal(columnName('Revenue'), 'Revenue');
assert.equal(columnName({ name: 'Cost' }), 'Cost');
assert.equal(columnName({ columnName: 'Margin' }), 'Margin');
assert.equal(columnName(null), '');
assert.equal(columnName(undefined), '');
assert.equal(columnName({}), '');

// 2. columnType
assert.equal(columnType('id', [{ id: 10 }]), 'number');
assert.equal(columnType({ type: 'INT' }), 'number');
assert.equal(columnType({ dataType: 'DECIMAL(10,2)' }), 'number');
assert.equal(columnType({ type: 'DATETIME' }), 'date');
assert.equal(columnType('createdAt', [{ createdAt: new Date() }]), 'date');
assert.equal(columnType('order_date', [{ order_date: '2026-09-11' }]), 'date');
assert.equal(columnType('title', [{ title: 'Report' }]), 'text');

// 3. snapshotColumns
assert.deepEqual(snapshotColumns({ columns: ['a', 'b'] }), ['a', 'b']);
assert.deepEqual(
    snapshotColumns({ rows: [{ col1: 'hello', col2: 42 }] }),
    [{ name: 'col1', type: 'string' }, { name: 'col2', type: 'number' }],
);
assert.deepEqual(snapshotColumns(null), []);
assert.deepEqual(snapshotColumns({ rows: [] }), []);

// 4. updateSnapshotPackage with filters
const testContext = {
    activeFilters: {
        category: { kind: 'categorical', values: ['A', 'B'] },
        amount: { kind: 'number', minimum: 10, maximum: 50 },
        created: { kind: 'date', minimum: '2026-01-01', maximum: '2026-12-31' },
    },
    snapshotPackage: {},
};
const testSnapshot = {
    source: 'Sales',
    columns: ['category', 'amount', 'created'],
    rows: [
        { category: 'A', amount: 25, created: '2026-05-15T12:00:00' }, // passes all
        { category: 'C', amount: 25, created: '2026-05-15T12:00:00' }, // rejected category
        { category: 'B', amount: 5, created: '2026-05-15T12:00:00' },  // rejected amount (< 10)
        { category: 'B', amount: 30, created: '2025-11-20T00:00:00' }, // rejected date (< 2026-01-01)
        { category: 'B', amount: 45, created: '2026-08-01T08:00:00' }, // passes all
    ],
};
updateSnapshotPackage(testContext, testSnapshot);
assert.deepEqual(testContext.snapshotPackage.columns, ['category', 'amount', 'created']);
assert.equal(testContext.snapshotPackage.metadata.rowCount, 2);
assert.equal(testContext.snapshotPackage.metadata.source, 'Sales');
assert.ok(testContext.snapshotPackage.sampleRows['Sales']);
assert.equal(testContext.snapshotPackage.sampleRows['Sales'].length, 2);

// 5. updateSnapshotPackageFromManifest
const manifestContext = { snapshotPackage: {} };
const manifest = {
    title: 'Executive Dashboard',
    visuals: [
        {
            name: 'kpi_card',
            columns: ['Metric', 'Value'],
            rows: [['Revenue', 120000]],
            nativeSvg: '<svg>card</svg>',
        },
        {
            name: 'monthly_trend',
            columns: ['Month', 'Amount', 'Target'],
            rows: [
                ['Jan', 100, 90],
                ['Feb', 150, 110],
            ],
            nativeSvg: '<svg>trend</svg>',
        },
    ],
};
updateSnapshotPackageFromManifest(manifestContext, manifest);
assert.equal(manifestContext.snapshot.source, 'monthly_trend');
assert.deepEqual(manifestContext.snapshot.columns, ['Month', 'Amount', 'Target']);
assert.equal(manifestContext.snapshot.rowCount, 2);
assert.deepEqual(manifestContext.snapshot.rows[0], { Month: 'Jan', Amount: 100, Target: 90 });
assert.equal(manifestContext.snapshotPackage.metadata.rowCount, 3); // 1 + 2
assert.equal(manifestContext.snapshotPackage.visualSvgs['kpi_card'], '<svg>card</svg>');
assert.equal(manifestContext.snapshotPackage.visualSvgs['monthly_trend'], '<svg>trend</svg>');

// 6. requestSourceSample
const mockFetch = async (_url, options) => {
    const body = JSON.parse(options.body);
    if (body.table === 'missing') {
        return { ok: false, text: async () => 'Table not found' };
    }
    return {
        ok: true,
        json: async () => ({
            source: 'db.Orders',
            columns: ['id', 'status'],
            rowCount: 1,
            rows: [{ id: 1, status: 'Shipped' }],
        }),
    };
};

const sampleResult = await requestSourceSample({
    authFetch: mockFetch,
    url: '/api/sample',
    connection: 'db',
    table: 'Orders',
});
assert.equal(sampleResult.source, 'db.Orders');
assert.equal(sampleResult.rowCount, 1);
assert.deepEqual(sampleResult.columns, ['id', 'status']);
assert.deepEqual(sampleResult.rows, [{ id: 1, status: 'Shipped' }]);

await assert.rejects(
    requestSourceSample({
        authFetch: mockFetch,
        url: '/api/sample',
        connection: 'db',
        table: 'missing',
    }),
    /Table not found/,
);

console.log('studio data: column naming, type inference, filtering, manifest hydration, and sampling passed.');
