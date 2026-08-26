using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Data;
using MediatR;

namespace ETL_SQL.LSP
{
    public class SetConnectionsParams : IRequest
    {
        public List<ConnectionInfo> connections { get; set; } = new();
    }

    public class SetDebugModeParams : IRequest
    {
        public bool debugMode { get; set; }
    }

    public class GetTablesParams : IRequest<GetTablesResponse>
    {
        public string connectionName { get; set; } = "";
        public string? uri { get; set; }
    }

    public class GetTablesResponse
    {
        public List<string> tables { get; set; } = new();
    }

    public class GetColumnsParams : IRequest<GetColumnsResponse>
    {
        public string connectionName { get; set; } = "";
        public string tableName { get; set; } = "";
        public string? uri { get; set; }
    }

    public class GetColumnsResponse
    {
        public List<string> columns { get; set; } = new();

        /// <summary>
        /// Column names paired with their declared type, positionally aligned with
        /// <see cref="columns"/>. The explorer shows the type beside the name; the name alone is
        /// what a drag inserts, so the two are kept apart rather than formatted into one string.
        /// Empty when the source cannot report types.
        /// </summary>
        public List<ColumnDetail> columnDetails { get; set; } = new();
    }

    public class ColumnDetail
    {
        public string name { get; set; } = "";
        public string? dataType { get; set; }
    }

    public class GetViewsParams : IRequest<GetViewsResponse>
    {
        public string connectionName { get; set; } = "";
        public string? uri { get; set; }
    }

    public class GetViewsResponse
    {
        public List<string> views { get; set; } = new();
    }

    public class GetTempTablesParams : IRequest<GetTempTablesResponse>
    {
        public string? uri { get; set; }
    }

    public class GetTempTablesResponse
    {
        public List<string> tables { get; set; } = new();
    }

    public class EncryptScriptParams : IRequest<EncryptScriptResponse>
    {
        public string text { get; set; } = "";
        public string password { get; set; } = "";
    }

    public class EncryptScriptResponse
    {
        public string encryptedText { get; set; } = "";
    }

    public class GetReportManifestParams : IRequest<GetReportManifestResponse>
    {
        public string text { get; set; } = "";
        public string? uri { get; set; }
        public Dictionary<string, string> parameters { get; set; } = new();
    }

    public class GetReportManifestResponse
    {
        public string? manifestJson { get; set; }
        public List<string> errors { get; set; } = new();
    }

    public class SetPortalDbPathParams : IRequest
    {
        /// <summary>Absolute path to portal.db. Null or empty disables dataset awareness.</summary>
        public string? path { get; set; }
    }

    // ── Designer parse / generate ─────────────────────────────────────────────

    public class DesignerParseParams : IRequest<DesignerParseResponse>
    {
        public string script { get; set; } = "";
    }

    public class DesignerParseResponse
    {
        public string? designStateJson { get; set; }
        public string? error { get; set; }
    }

    public class DesignerGenerateParams : IRequest<DesignerGenerateResponse>
    {
        public string designStateJson { get; set; } = "";
        public string? script { get; set; }
    }

    public class DesignerGenerateResponse
    {
        public string script { get; set; } = "";
    }

    // ── Script flow DAG ───────────────────────────────────────────────────────

    public class ScriptDagParams : IRequest<ScriptDagResponse>
    {
        public string script { get; set; } = "";
    }

    public class ScriptDagNodeDto
    {
        public string id { get; set; } = "";
        public string label { get; set; } = "";
        public string type { get; set; } = "";
        public int line { get; set; }
    }

    public class ScriptDagEdgeDto
    {
        public string source { get; set; } = "";
        public string target { get; set; } = "";
    }

    public class ScriptDagResponse
    {
        public List<ScriptDagNodeDto> nodes { get; set; } = new();
        public List<ScriptDagEdgeDto> edges { get; set; } = new();
        public string? error { get; set; }
    }

    public class GetConnectorSchemasParams : IRequest<GetConnectorSchemasResponse>
    {
        public string? type { get; set; }
    }

    public class GetConnectorSchemasResponse
    {
        public List<ConnectorSchemaDescriptor> schemas { get; set; } = new();
    }

    public class ParseConnectionStringParams : IRequest<ParseConnectionStringResponse>
    {
        public string connectionString { get; set; } = "";
        public string? hintProvider { get; set; }
    }

    public class ParseConnectionStringResponse
    {
        public string? detectedProvider { get; set; }
        public Dictionary<string, string> options { get; set; } = new();
        public string? extractedCredential { get; set; }
        public string? suggestedSecretKey { get; set; }
    }

    public class TestConnectionParams : IRequest<TestConnectionResponse>
    {
        public string? alias { get; set; }
        public string connectorType { get; set; } = "";
        public string? target { get; set; }
        public Dictionary<string, string>? options { get; set; }
        public int probeTimeoutSeconds { get; set; } = 5;
    }

    public class TestConnectionResponse
    {
        public bool succeeded { get; set; }
        public string connection { get; set; } = "";
        public string connectorType { get; set; } = "";
        public List<DiagnosticStepDto> steps { get; set; } = new();
        public string? error { get; set; }
    }

    public class DiagnosticStepDto
    {
        public string layer { get; set; } = "";
        public string status { get; set; } = "";
        public string detail { get; set; } = "";
        public string? remedy { get; set; }
    }
}
