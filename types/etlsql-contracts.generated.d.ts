/**
 * GENERATED FILE - DO NOT EDIT.
 *
 * The C# types the browser code talks to, as TypeScript declarations. Regenerate with:
 *
 *   ETLSQL_UPDATE_BROWSER_CONTRACTS=1 dotnet test tests/ETL-SQL.Portal.Tests \
 *     --filter FullyQualifiedName~BrowserContractsGeneratorTests
 *
 * Source of truth: the records and enums themselves, read by reflection in
 * tests/ETL-SQL.Portal.Tests/BrowserContractsGenerator.cs. Editing this file by hand
 * makes the browser sources check against a shape the server does not send.
 *
 * Field names are camelCase because the hosts serialise with JsonSerializerDefaults.Web.
 */

/** PipelineTaskKind, as it crosses the wire. Matched case-insensitively on the way in. */
type PipelineTaskKind =
    | 'execution'
    | 'copyfile'
    | 'movefile'
    | 'renamefile'
    | 'deletefile'
    | 'createdirectory'
    | 'deletedirectory'
    | 'deletedirectorycontents'
    | 'renamedirectory'
    | 'movedirectory'
    | 'copydirectory'
    | 'validation'
    | 'notification'
    | 'parallel'
    | 'foreach'
    | 'transaction'
    | 'if'
    | 'for'
    | 'while'
    | 'throw'
    | 'break'
    | 'continue'
    | 'waitfor'
    | 'extract'
    | 'load'
    | 'upsert';

/** PipelineEdgeCondition, as it crosses the wire. Matched case-insensitively on the way in. */
type PipelineEdgeCondition =
    | 'always'
    | 'onsuccess'
    | 'onfailure'
    | 'oncompletion'
    | 'expression';

interface DataModelColumnDto {
    name: string;
    type?: string;
    isKey: boolean;
}

interface DataModelEntityDto {
    id: string;
    name: string;
    kind: string;
    connection?: string;
    line: number;
    detail?: string;
    columns: DataModelColumnDto[];
}

interface DataModelRelationshipDto {
    id: string;
    from: string;
    to: string;
    kind: string;
    cardinality: string;
    evidence: string;
    fromColumn?: string;
    toColumn?: string;
    joinType?: string;
    line: number;
}

interface DataModelRequest {
    script?: string;
    documentUri?: string;
}

interface DataModelResponse {
    parsed: boolean;
    error?: string;
    hasSchemaEvidence: boolean;
    entities: DataModelEntityDto[];
    relationships: DataModelRelationshipDto[];
}

interface DesignerBookmarkDto {
    id: string;
    name: string;
    title?: string;
    page?: string;
    isDefault: boolean;
    parameters?: DesignerBookmarkParameterDto[];
    state?: DesignerBookmarkStateDto[];
}

interface DesignerBookmarkParameterDto {
    name: string;
    value: string;
}

interface DesignerBookmarkStateDto {
    objectName: string;
    property: string;
    on: boolean;
}

interface DesignerConditionalFormattingRuleDto {
    condition: string;
    backgroundColor: string;
    fontColor?: string;
}

interface DesignerConnectionDto {
    name: string;
    text: string;
}

interface DesignerDatasetDto {
    id: string;
    name: string;
    query: string;
    ttl?: string;
}

interface DesignerFieldFormattingDto {
    format?: string;
    align?: string;
    displayName?: string;
    dataBar: boolean;
    dataBarColor?: string;
    colorScaleFrom?: string;
    colorScaleTo?: string;
}

interface DesignerPageDto {
    id: string;
    name: string;
    mode: string;
    visuals: DesignerVisualDto[];
    printLayout?: DesignerPageLayoutDto;
}

interface DesignerPageLayoutDto {
    pageSize?: string;
    orientation?: string;
    marginTop?: number;
    marginRight?: number;
    marginBottom?: number;
    marginLeft?: number;
    units?: string;
    overflow?: string;
    customWidth?: number;
    customHeight?: number;
}

interface DesignerParameterDto {
    name: string;
    dataType: string;
    initialValue?: string;
    isInput: boolean;
    isOutput: boolean;
    isRequired: boolean;
    isSensitive: boolean;
    isBlockScoped: boolean;
}

interface DesignerReportStyleDto {
    theme?: string;
    accent?: string;
    background?: string;
    surface?: string;
    text?: string;
}

interface DesignerStateDto {
    pages: DesignerPageDto[];
    datasets: DesignerDatasetDto[];
    reportStyle?: DesignerReportStyleDto;
    bookmarks?: DesignerBookmarkDto[];
    parameters?: DesignerParameterDto[];
    connections?: DesignerConnectionDto[];
}

interface DesignerTextFormattingDto {
    text?: string;
    color?: string;
    font?: string;
    size?: string;
    weight?: string;
    align?: string;
}

interface DesignerVisualDto {
    id: string;
    name: string;
    type: string;
    gridCol: number;
    gridRow: number;
    gridColSpan: number;
    gridRowSpan: number;
    title?: string;
    dataset?: string;
    mappings: Record<string, string>;
    options: Record<string, string>;
    containerId?: string;
    formatting?: DesignerVisualFormattingDto;
}

interface DesignerVisualFormattingDto {
    title?: DesignerTextFormattingDto;
    subtitle?: DesignerTextFormattingDto;
    xAxis?: Record<string, string>;
    yAxis?: Record<string, string>;
    palette?: string[];
    conditionalRules?: DesignerConditionalFormattingRuleDto[];
    fields?: Record<string, DesignerFieldFormattingDto>;
}

interface ParseDesignerResponse {
    designState: DesignerStateDto;
    error?: string;
}

interface PipelineDependencyDto {
    id: string;
    condition: PipelineEdgeCondition;
    expression?: string;
}

interface PipelineRunPlanRequest {
    script?: string;
    id?: string;
}

interface PipelineScopeRequest {
    script?: string;
    id?: string;
    line?: number;
}

interface PipelineTaskDto {
    id: string;
    kind: PipelineTaskKind;
    connection: string;
    body: string;
    line: number;
    dependsOn: PipelineDependencyDto[];
    guarded: boolean;
    container?: string;
    variable?: string;
    collection?: string;
    endLine: number;
    hasElse: boolean;
    fields?: Record<string, string>;
}

interface PipelineTaskResponse {
    applied: boolean;
    script: string;
    error?: string;
    tasks: PipelineTaskDto[];
    preview?: string;
}

interface PreviewAsRequest {
    label?: string;
    groups?: string[];
    roles?: string[];
}

interface RunDesignerRequest {
    script: string;
    selection?: string;
    connectionRef?: string;
    documentUri?: string;
    parameters?: Record<string, string>;
    previewAs?: PreviewAsRequest;
    clientRunId?: string;
}

interface RunDesignerResponse {
    columns: string[];
    rows: Record<string, unknown>[];
    rowCount: number;
    capped: boolean;
    elapsedMs: number;
    message: string;
    pipeline?: unknown;
    byteCapped: boolean;
    bytesReturned: number;
    runId?: string;
}

interface ScriptDagDto {
    nodes: ScriptDagNodeDto[];
    edges: ScriptDagEdgeDto[];
}

interface ScriptDagEdgeDto {
    source: string;
    target: string;
    label?: string;
}

interface ScriptDagNodeDto {
    id: string;
    label: string;
    type: string;
    meta?: unknown;
}

interface ScriptDagProjection {
    parsed: boolean;
    dag: ScriptDagDto;
    error?: string;
}
