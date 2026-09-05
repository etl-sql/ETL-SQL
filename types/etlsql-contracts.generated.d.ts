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
    | 'waitfor';

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
}

interface PipelineTaskResponse {
    applied: boolean;
    script: string;
    error?: string;
    tasks: PipelineTaskDto[];
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
