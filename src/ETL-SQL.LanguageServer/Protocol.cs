using System.Collections.Generic;
using ETL_SQL.Data;
using ETL_SQL.Core;
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
    }

    public class DesignerGenerateResponse
    {
        public string script { get; set; } = "";
    }
}
