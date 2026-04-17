export type MessageLevel = 'info' | 'warn' | 'err' | 'sys';

export interface ExecutionNode {
    id: string;
    name: string;
    status: 'Pending' | 'Running' | 'Completed' | 'Error';
    rowsProcessed: number;
    durationMs: number;
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

export type ProtocolMessage = 
    | ResultsMessage 
    | ProgressMessage 
    | LogMessage 
    | PerformanceMessage 
    | ClearMessage 
    | StatusMessage
    | DoneMessage;
