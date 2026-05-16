import type { ProtocolMessage } from './types';

export const mockTrace: ProtocolMessage[] = [
    { type: 'activeEditorChanged', uri: 'mock:///demo.etlsql' },
    { type: 'message', text: 'Executing script: demo.etlsql', level: 'sys' },

    // Phase 1: connection setup
    { type: 'progress', data: [
        { id: '1', name: 'Create Connection [m]', status: 'Running', rowsProcessed: 0, durationMs: 0, isParallelBlock: false, children: [] }
    ]},
    { type: 'progress', data: [
        { id: '1', name: 'Create Connection [m]', status: 'Completed', rowsProcessed: 0, durationMs: 14, isParallelBlock: false, children: [] }
    ]},

    // Phase 2: scan + parallel transform
    { type: 'message', text: 'Executing: SELECT * FROM m.Users TRANSFORM(...)', level: 'sys' },
    { type: 'progress', data: [
        { id: '1', name: 'Create Connection [m]', status: 'Completed', rowsProcessed: 0, durationMs: 14, isParallelBlock: false, children: [] },
        {
            id: '2', name: 'Scan Users', status: 'Running', rowsProcessed: 1200, durationMs: 22, isParallelBlock: false,
            children: [
                {
                    id: '3', name: 'PARALLEL (4)', status: 'Running', rowsProcessed: 0, durationMs: 5, isParallelBlock: true,
                    children: [
                        { id: '4', name: 'Normalize Email', status: 'Completed', rowsProcessed: 300, durationMs: 3, isParallelBlock: false, children: [] },
                        { id: '5', name: 'Validate Phone', status: 'Running',   rowsProcessed: 150, durationMs: 5, isParallelBlock: false, children: [] },
                        { id: '6', name: 'Lookup Country',  status: 'Waiting',  rowsProcessed: 0,   durationMs: 0, isParallelBlock: false, children: [] },
                        { id: '7', name: 'Hash Password',   status: 'Waiting',  rowsProcessed: 0,   durationMs: 0, isParallelBlock: false, children: [] },
                    ]
                }
            ]
        }
    ]},

    // Phase 3: all parallel done, scan complete
    { type: 'message', text: 'Fetched 5,000 rows from m.Users', level: 'info' },
    { type: 'progress', data: [
        { id: '1', name: 'Create Connection [m]', status: 'Completed', rowsProcessed: 0, durationMs: 14, isParallelBlock: false, children: [] },
        {
            id: '2', name: 'Scan Users', status: 'Completed', rowsProcessed: 5000, durationMs: 88, isParallelBlock: false,
            children: [
                {
                    id: '3', name: 'PARALLEL (4)', status: 'Completed', rowsProcessed: 5000, durationMs: 41, isParallelBlock: true,
                    children: [
                        { id: '4', name: 'Normalize Email', status: 'Completed', rowsProcessed: 1250, durationMs: 18, isParallelBlock: false, children: [] },
                        { id: '5', name: 'Validate Phone',  status: 'Completed', rowsProcessed: 1250, durationMs: 22, isParallelBlock: false, children: [] },
                        { id: '6', name: 'Lookup Country',  status: 'Completed', rowsProcessed: 1250, durationMs: 19, isParallelBlock: false, children: [] },
                        { id: '7', name: 'Hash Password',   status: 'Completed', rowsProcessed: 1250, durationMs: 41, isParallelBlock: false, children: [] },
                    ]
                }
            ]
        }
    ]},

    { type: 'results', columns: ['id', 'username', 'email', 'country'], rows: [
        { id: 1, username: 'admin',  email: 'admin@mock.db',  country: 'US' },
        { id: 2, username: 'user1',  email: 'user1@mock.db',  country: 'GB' },
        { id: 3, username: 'user2',  email: 'user2@mock.db',  country: 'DE' },
    ]},

    // Phase 4: second query with a faulted node
    { type: 'message', text: 'Executing: SELECT COUNT(*) FROM m.Orders', level: 'sys' },
    { type: 'progress', data: [
        { id: '1', name: 'Create Connection [m]', status: 'Completed', rowsProcessed: 0,     durationMs: 14,  isParallelBlock: false, children: [] },
        { id: '2', name: 'Scan Users',            status: 'Completed', rowsProcessed: 5000,  durationMs: 88,  isParallelBlock: false, children: [] },
        { id: '8', name: 'Scan Orders',           status: 'Completed', rowsProcessed: 12450, durationMs: 112, isParallelBlock: false, children: [] },
    ]},

    { type: 'results', columns: ['count'], rows: [{ count: 12450 }]},

    { type: 'performance', metrics: {
        executionMs: 220,
        rowsProcessed: 17450,
        memoryMb: 18.2,
        statements: [
            { type: 'CONN',   totalMs: 14 },
            { type: 'SELECT', totalMs: 88 },
            { type: 'SELECT', totalMs: 112 },
        ]
    }},

    { type: 'connections', connections: [
        { name: 'PROD_DB',     type: 'MSSQL',    connectionString: 'Server=prod;Database=sales' },
        { name: 'STAGING_CSV', type: 'FLATFILE', connectionString: 'C:/Data/staging.csv' }
    ]},
    { type: 'scriptConnections', uri: 'mock:///demo.etlsql', connections: [
        { name: 'm', type: 'MOCKDB' }
    ]},
    { type: 'variables', variables: [
        { name: '@batch_id',     value: '4502',       typeName: 'INT' },
        { name: '@process_date', value: '2026-04-25', typeName: 'DATE' },
    ]},

    { type: 'reportManifest', source: 'mock:///report.rptsql', title: 'Sales Performance', description: 'Annual breakdown of sales by region and category', builtAt: new Date().toISOString(), parameters: { '@region': 'All' }, visuals: [
        { name: 'Revenue by Region', visualType: 'BAR', columns: ['region', 'revenue'], rows: [['North', 500000], ['South', 300000], ['East', 450000], ['West', 250000]], actions: [], interactions: { ON_SELECT: 'HIGHLIGHT', MATCHING: 'region' }, options: { 'mapping:x': 'region' } },
        { name: 'Category Breakdown', visualType: 'BAR', columns: ['category', 'revenue'], rows: [['Apparel', 541905.80], ['Home', 320000], ['Electronics', 780000]], actions: [], interactions: { ON_SELECT: 'HIGHLIGHT', MATCHING: 'category' }, options: { 'mapping:x': 'category' } },
        { name: 'Details', visualType: 'TABLE', columns: ['id', 'date', 'region', 'category', 'amount'], rows: [[1, '2026-05-01', 'North', 'Apparel', 226254.92], [2, '2026-05-02', 'South', 'Home', 150000]], actions: [], options: {} }
    ], pages: [
        { name: 'Overview', structure: 'A B / C C', slotMap: { 'A': 'Revenue by Region', 'B': 'Category Breakdown', 'C': 'Details' } }
    ] },
    { type: 'done', exitCode: 0 },
];
