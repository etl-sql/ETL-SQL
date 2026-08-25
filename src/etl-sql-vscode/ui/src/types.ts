export type MessageLevel = 'info' | 'warn' | 'err' | 'sys';

export interface ExecutionNode {
    id: string;
    name: string;
    status: 'Waiting' | 'Running' | 'Completed' | 'Faulted';
    rowsProcessed: number;
    durationMs: number;
    iterationCount?: number;
    isParallelBlock?: boolean;
    velocity?: number;
    error?: string;
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
    rows: Record<string, unknown>[];
    columns: string[];
}

export interface ProgressMessage {
    type: 'progress';
    data: ExecutionNode[];
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
    resetHistory?: boolean;
    scriptUri?: string;
}

export interface StatusMessage {
    type: 'status';
    status: 'ready' | 'running' | 'finished' | 'error';
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
    isScriptOnly?: boolean;
}

export interface ConnectionsMessage {
    type: 'connections';
    connections: Connection[];
}

export interface ScriptConnectionsMessage {
    type: 'scriptConnections';
    uri: string;
    connections: Connection[];
}

export interface VariablesMessage {
    type: 'variables';
    variables?: Variable[];
    data?: Variable[]; // For backwards compatibility with some engine versions
}

export interface TablesResponse {
    type: 'tablesResponse';
    requestId: string;
    tables: string[];
}

export interface ColumnDetail {
    name: string;
    /** Declared type, when the source can report one. */
    dataType?: string;
}

export interface ColumnsResponse {
    type: 'columnsResponse';
    requestId: string;
    columns: string[];
    /** Positionally aligned with `columns`; absent from older servers. */
    columnDetails?: ColumnDetail[];
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
    variables: Variable[];
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
    | ActiveEditorChangedMessage
    | ReportManifest;

export interface ReportAction {
    type: string;
    trigger?: string;
    targetVisual?: string;
    targetPage?: string;
    parameterName?: string;
}

export interface ColumnMetaManifest {
    format?: string;
    align?: string;
    dataBar?: boolean;
    dataBarColor?: string;
    dataBarMin?: number;
    dataBarMax?: number;
    colorScaleFrom?: string;
    colorScaleTo?: string;
    colorScaleMin?: number;
    colorScaleMax?: number;
    cellRenderer?: 'image' | 'hyperlink' | 'sparkline';
    imageWidth?: number;
    hyperlinkLabel?: string;
    sparklineType?: 'line' | 'bar' | 'area';
}

export interface VisualManifest {
    name: string;
    visualType: string;
    chartConfig?: string;
    nativeSvg?: string;
    columns: string[];
    rows: unknown[][];
    rowStyles?: (string | null)[];
    rowFontStyles?: (string | null)[];
    columnMeta?: (ColumnMetaManifest | null)[];
    options: Record<string, string>;
    error?: string;
    actions: ReportAction[];
    /** Compact resolved interaction contract. The only interaction payload v0.19+ manifests carry. */
    interaction?: InteractionManifest;
    /** Pre-v0.19 authored interaction map. Read only through resolveInteraction(). */
    interactions?: Record<string, string>;
    styles?: Record<string, string>;
    defaultValue?: string;
    summaryData?: TableSummaryData;
    gridStyle?: string;
    dataLabels?: DataLabelsManifest;
}

export interface InteractionManifest {
    /** Resolved selection/cross-filter key column. */
    key?: string;
    /** Resolved measure column backing proportional highlighting. */
    valueKey?: string;
    /** NONE | SINGLE | MULTIPLE | INTERVAL */
    select: string;
    /** HIGHLIGHT | FILTER | SETPARAMETER | DRILL | NAVIGATE */
    effect: string;
    /** NONE | CATEGORICAL | PROPORTIONAL */
    highlight: string;
}

export interface DataLabelsManifest {
    show: boolean;
    position?: string;
    color?: string;
    fontSize?: number;
    fontWeight?: string;
    fontFamily?: string;
    format?: string;
}

export interface SummaryItemData {
    column: string;
    aggregate: string;
    value: string;
    alias?: string;
}

export interface TableSummaryData {
    aggregates: SummaryItemData[];
    grandTotals?: Record<string, string>;
}

export interface ContainerManifest {
    name: string;
    containerType: string;
    visuals?: string[];
    structure?: string;
    slotMap?: Record<string, string>;
    styles?: Record<string, string>;
    title?: string;
    subtitle?: string;
}

export interface NavigationManifest {
    name: string;
    navType: string;
    orientation: string;
    defaultPage?: string;
    pages: string[];
}

export interface PageManifest {
    name: string;
    structure: string;
    slotMap: Record<string, string>;
    styles?: Record<string, string>;
}

export interface ReportManifest {
    type: 'reportManifest';
    source: string;
    builtAt: string;
    title?: string;
    description?: string;
    visuals: VisualManifest[];
    pages: PageManifest[];
    containers?: ContainerManifest[];
    navigations?: NavigationManifest[];
    parameters?: Record<string, string>;
}
