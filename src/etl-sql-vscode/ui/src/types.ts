export type MessageLevel = 'info' | 'warn' | 'err' | 'sys';

export interface ExecutionNode {
    id: string;
    name: string;
    status: 'Pending' | 'Running' | 'Completed' | 'Error';
    rowsProcessed: number;
    durationMs: number;
    iterationCount?: number;
    children?: ExecutionNode[];
}

export interface PerformanceMetrics {
    executionMs: number;
    rowsProcessed: number;
    memoryMb: number;
    statements: Array<{
        type: string;
        totalMs: number;
    }>;
}

export interface ResultsMessage {
    type: 'results';
    rows: any[];
    columns: string[];
}

export interface ProgressMessage {
    type: 'progress';
    data: {
        roots: ExecutionNode[];
    };
}

export interface LogMessage {
    type: 'message';
    text: string;
    level: MessageLevel;
}

export interface PerformanceMessage {
    type: 'performance';
    metrics: PerformanceMetrics;
}

export interface ClearMessage {
    type: 'clear';
}

export interface StatusMessage {
    type: 'status';
    status: 'ready';
    buildId?: string;
}

export interface DoneMessage {
    type: 'done';
    exitCode: number;
}

export interface Connection {
    name: string;
    type: string;
    connectionString: string;
}

export interface Variable {
    name: string;
    value: string;
    typeName: string;
}

export interface ConnectionsMessage {
    type: 'connections';
    connections: Connection[];
}

export interface ScriptConnectionsMessage {
    type: 'scriptConnections';
    uri: string;
    connections: any[];
}

export interface VariablesMessage {
    type: 'variables';
    variables?: Variable[];
    data?: any[]; // For backwards compatibility with some engine versions
}

export interface TablesResponse {
    type: 'tablesResponse';
    requestId: string;
    tables: string[];
}

export interface ColumnsResponse {
    type: 'columnsResponse';
    requestId: string;
    columns: string[];
}

export interface TempTablesResponse {
    type: 'tempTablesResponse';
    requestId: string;
    tables: string[];
}

export interface ActiveEditorChangedMessage {
    type: 'activeEditorChanged';
    uri: string;
}

export interface ScriptVariablesMessage {
    type: 'scriptVariables';
    uri: string;
    variables: any[];
}

export type ProtocolMessage = 
    | ResultsMessage 
    | ProgressMessage 
    | LogMessage 
    | PerformanceMessage 
    | ClearMessage 
    | StatusMessage
    | DoneMessage
    | ConnectionsMessage
    | ScriptConnectionsMessage
    | VariablesMessage
    | ScriptVariablesMessage
    | TablesResponse
    | ColumnsResponse
    | TempTablesResponse
    | ActiveEditorChangedMessage;
