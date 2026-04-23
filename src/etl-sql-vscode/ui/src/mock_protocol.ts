import type { ProtocolMessage } from './types';

export const mockTrace: ProtocolMessage[] = [
    { type: 'activeEditorChanged', uri: 'mock:///demo.etlsql' },
    { type: 'status', status: 'ready', buildId: 'DEV-SANDBOX-2026' },
    { type: 'message', text: 'Executing: CREATE CONNECTION m ON MOCKDB()', level: 'sys' },
    { type: 'progress', data: { execution_nodes: [
        { id: '1', name: 'Create Connection [m]', status: 'Completed', rowsProcessed: 0, durationMs: 12 }
    ]}} as any,
    { type: 'message', text: 'Executing: SELECT * FROM m.Users', level: 'sys' },
    { type: 'progress', data: { execution_nodes: [
        { id: '1', name: 'Create Connection [m]', status: 'Completed', rowsProcessed: 0, durationMs: 12 },
        { id: '2', name: 'Scan Users', status: 'Running', rowsProcessed: 50, durationMs: 5 }
    ]}} as any,
    { type: 'message', text: 'Fetched 5,000 rows.', level: 'info' },
    { type: 'progress', data: { execution_nodes: [
        { id: '1', name: 'Create Connection [m]', status: 'Completed', rowsProcessed: 0, durationMs: 12 },
        { id: '2', name: 'Scan Users', status: 'Completed', rowsProcessed: 5000, durationMs: 45 }
    ]}},
    { type: 'results', columns: ['id', 'username', 'email'], rows: [
        { id: 1, username: 'admin', email: 'admin@mock.db' },
        { id: 2, username: 'user1', email: 'user1@mock.db' }
    ]},
    { type: 'message', text: 'Executing: SELECT COUNT(*) FROM m.Orders', level: 'sys' },
    { type: 'results', columns: ['count'], rows: [
        { count: 12450 }
    ]},
    { type: 'performance', metrics: {
        executionMs: 85,
        rowsProcessed: 5000,
        memoryMb: 12.4,
        statements: [
            { type: 'CONN', totalMs: 12 },
            { type: 'SELECT', totalMs: 45 }
        ]
    }},
    { type: 'connections', connections: [
        { name: 'PROD_DB', type: 'MSSQL', connectionString: 'Server=prod;Database=sales' },
        { name: 'STAGING_CSV', type: 'FLATFILE', connectionString: 'C:/Data/staging.csv' }
    ]},
    { type: 'scriptConnections', uri: 'mock:///demo.etlsql', connections: [
        { name: 'LOCAL_SCRATCH', type: 'MOCKDB' }
    ]},
    { type: 'variables', variables: [
        { name: '@batch_id', value: '4502', typeName: 'INT' },
        { name: '@process_date', value: '2026-04-17', typeName: 'DATE' },
        { name: '@is_active', value: 'true', typeName: 'BIT' }
    ]},
    { type: 'done', exitCode: 0 }
];
