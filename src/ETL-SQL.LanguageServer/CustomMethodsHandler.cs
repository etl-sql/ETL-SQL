using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Reporting;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;

namespace ETL_SQL.LSP
{
    /// <summary>
    /// LSP Notification Handler for setting script-defined connections.
    /// </summary>
    [Method("etlsql/setConnections", Direction.ClientToServer)]
    public interface ISetConnectionsHandler : IJsonRpcNotificationHandler<SetConnectionsParams> { }

    /// <summary>
    /// LSP Request Handler for getting tables from a connection.
    /// </summary>
    [Method("etlsql/getTables", Direction.ClientToServer)]
    public interface IGetTablesHandler : IJsonRpcRequestHandler<GetTablesParams, GetTablesResponse> { }

    /// <summary>
    /// LSP Request Handler for getting columns from a table.
    /// </summary>
    [Method("etlsql/getColumns", Direction.ClientToServer)]
    public interface IGetColumnsHandler : IJsonRpcRequestHandler<GetColumnsParams, GetColumnsResponse> { }

    /// <summary>
    /// LSP Request Handler for getting views from a connection.
    /// </summary>
    [Method("etlsql/getViews", Direction.ClientToServer)]
    public interface IGetViewsHandler : IJsonRpcRequestHandler<GetViewsParams, GetViewsResponse> { }

    /// <summary>
    /// LSP Request Handler for getting document-local temporary tables.
    /// </summary>
    [Method("etlsql/getTempTables", Direction.ClientToServer)]
    public interface IGetTempTablesHandler : IJsonRpcRequestHandler<GetTempTablesParams, GetTempTablesResponse> { }

    /// <summary>
    /// LSP Request Handler for building a report manifest from a script.
    /// </summary>
    [Method("etlsql/getReportManifest", Direction.ClientToServer)]
    public interface IGetReportManifestHandler : IJsonRpcRequestHandler<GetReportManifestParams, GetReportManifestResponse> { }

    /// <summary>
    /// LSP Request Handler for encrypting a script.

    /// <summary>
    /// LSP Request Handler for encrypting a script.
    /// </summary>
    [Method("etlsql/encryptScript", Direction.ClientToServer)]
    public interface IEncryptScriptHandler : IJsonRpcRequestHandler<EncryptScriptParams, EncryptScriptResponse> { }

    /// <summary>
    /// LSP Notification Handler for toggling debug mode.
    /// </summary>
    [Method("etlsql/setDebugMode", Direction.ClientToServer)]
    public interface ISetDebugModeHandler : IJsonRpcNotificationHandler<SetDebugModeParams> { }

    /// <summary>
    /// LSP Notification Handler: client sends the portal.db path so the server can offer
    /// dataset name completions and hover metadata without a network round-trip.
    /// </summary>
    [Method("etlsql/setPortalDbPath", Direction.ClientToServer)]
    public interface ISetPortalDbPathHandler : IJsonRpcNotificationHandler<SetPortalDbPathParams> { }

    /// <summary>
    /// Implementation of specialized ETL-SQL Language Server methods for metadata discovery and configuration.
    /// </summary>
    public class CustomMethodsHandler(IMetadataManager metadata, ILogger<CustomMethodsHandler> logger, IServiceScopeFactory scopeFactory, DatasetStore datasetStore, ETL_SQL.Services.SecurityService security) : ISetConnectionsHandler, IGetTablesHandler, IGetColumnsHandler, ISetDebugModeHandler, IGetViewsHandler, IGetTempTablesHandler, IEncryptScriptHandler, IGetReportManifestHandler, ISetPortalDbPathHandler
    {
        /// <summary>Handles toggle debug mode notification.</summary>
        public Task<Unit> Handle(SetDebugModeParams request, CancellationToken cancellationToken)
        {
            metadata.DebugMode = request.debugMode;
            logger.LogInformation("LSP: DebugMode set to {Value}", request.debugMode);
            return Task.FromResult(Unit.Value);
        }

        /// <summary>Stores the portal.db path and refreshes the dataset cache.</summary>
        public Task<Unit> Handle(SetPortalDbPathParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/setPortalDbPath received: {Path}", request.path);
            datasetStore.SetPortalDbPath(request.path);
            return Task.FromResult(Unit.Value);
        }

        /// <summary>Handles script encryption requests.</summary>
        public Task<EncryptScriptResponse> Handle(EncryptScriptParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/encryptScript requested.");
            var encrypted = security.SecureScriptForSave(request.text, request.password);
            return Task.FromResult(new EncryptScriptResponse { encryptedText = encrypted });
        }

        /// <summary>Handles bulk registration of connections from the client.</summary>
        public Task<Unit> Handle(SetConnectionsParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/setConnections received {Count} connections.", request.connections?.Count ?? 0);
            if (request.connections != null)
            {
                foreach (var c in request.connections)
                {
                    metadata.RegisterConnection(c.Name, c.Type, c.ConnectionString);
                }
            }
            return Task.FromResult(Unit.Value);
        }

        /// <summary>Handles requests to list tables for a connection.</summary>
        public async Task<GetTablesResponse> Handle(GetTablesParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/getTables requested for {Conn} (URI: {Uri})", request.connectionName, request.uri);
            var tables = (await metadata.GetTablesAsync(request.connectionName, request.uri)).ToList();
            return new GetTablesResponse { tables = tables };
        }

        /// <summary>Handles requests to list columns for a table.</summary>
        public async Task<GetColumnsResponse> Handle(GetColumnsParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/getColumns requested for {Conn}.{Table} (URI: {Uri})", request.connectionName, request.tableName, request.uri);
            var columns = (await metadata.GetColumnsAsync(request.connectionName, request.tableName, request.uri)).ToList();
            return new GetColumnsResponse { columns = columns };
        }

        /// <summary>Handles requests to list views for a connection.</summary>
        public async Task<GetViewsResponse> Handle(GetViewsParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/getViews requested for {Conn} (URI: {Uri})", request.connectionName, request.uri);
            var views = (await metadata.GetViewsAsync(request.connectionName, request.uri)).ToList();
            return new GetViewsResponse { views = views };
        }

        /// <summary>Handles requests to list document-local temporary tables.</summary>
        public async Task<GetTempTablesResponse> Handle(GetTempTablesParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/getTempTables requested for (URI: {Uri})", request.uri);
            var tables = (await metadata.GetTempTablesAsync(request.uri)).ToList();
            return new GetTempTablesResponse { tables = tables };
        }

        /// <summary>Handles requests to build a report manifest from a script.</summary>
        public async Task<GetReportManifestResponse> Handle(GetReportManifestParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/getReportManifest requested (URI: {Uri})", request.uri);
            var response = new GetReportManifestResponse();

            try
            {
                using var scope = scopeFactory.CreateScope();
                var evaluator = scope.ServiceProvider.GetRequiredService<Engine.Evaluator>();

                // Inject parameters from request
                foreach (var p in request.parameters)
                {
                    evaluator.DeclareVariable(p.Key, p.Value, new VariableMetadata { IsInput = true });
                }

                // Parse and Evaluate the script
                var lexer = new ETL_SQL.Core.Parser.Lexer(request.text);
                var tokens = lexer.Tokenize();
                var parser = new ETL_SQL.Core.Parser.Parser(tokens, request.text);
                var script = parser.Parse();

                if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    response.errors.AddRange(script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
                    return response;
                }

                await evaluator.Evaluate(script, cancellationToken);

                // Build the manifest
                var manifestBuilder = new ManifestBuilder(evaluator);
                var manifest = await manifestBuilder.BuildAsync(request.text);

                response.manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "LSP: Error building report manifest.");
                response.errors.Add(ex.Message);
            }

            return response;
        }
    }
}
