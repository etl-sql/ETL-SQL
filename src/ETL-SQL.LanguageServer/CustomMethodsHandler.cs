using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Connectors;
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
    /// LSP Request Handler for discovering connector schemas.
    /// </summary>
    [Method("etlsql/getConnectorSchemas", Direction.ClientToServer)]
    public interface IGetConnectorSchemasHandler : IJsonRpcRequestHandler<GetConnectorSchemasParams, GetConnectorSchemasResponse> { }

    /// <summary>
    /// LSP Request Handler for parsing unformatted connection strings into canonical options.
    /// </summary>
    [Method("etlsql/parseConnectionString", Direction.ClientToServer)]
    public interface IParseConnectionStringHandler : IJsonRpcRequestHandler<ParseConnectionStringParams, ParseConnectionStringResponse> { }

    /// <summary>
    /// LSP Request Handler for executing layered connection diagnostics.
    /// </summary>
    [Method("etlsql/testConnection", Direction.ClientToServer)]
    public interface ITestConnectionHandler : IJsonRpcRequestHandler<TestConnectionParams, TestConnectionResponse> { }

    /// <summary>
    /// Implementation of specialized ETL-SQL Language Server methods for metadata discovery and configuration.
    /// </summary>
    public class CustomMethodsHandler(
        IMetadataManager metadata,
        ILogger<CustomMethodsHandler> logger,
        IServiceScopeFactory scopeFactory,
        DatasetStore datasetStore,
        ETL_SQL.Services.SecurityService security,
        IConnectorRegistry connectorRegistry,
        ETL_SQL.Core.Diagnostics.ConnectionDiagnosticEngine diagnosticEngine) :
        ISetConnectionsHandler,
        IGetTablesHandler,
        IGetColumnsHandler,
        ISetDebugModeHandler,
        IGetViewsHandler,
        IGetTempTablesHandler,
        IEncryptScriptHandler,
        IGetReportManifestHandler,
        ISetPortalDbPathHandler,
        IGetConnectorSchemasHandler,
        IParseConnectionStringHandler,
        ITestConnectionHandler
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
            // DUAL is a synthetic one-row table the metadata layer adds to every connection so
            // "SELECT 1 FROM DUAL" completes. It is not a browsable object — it appeared in the
            // Metadata Explorer under every connection with a meaningless DUMMY column beneath it.
            // Completions read the metadata manager directly and still see it.
            var tables = (await metadata.GetTablesAsync(request.connectionName, request.uri))
                .Where(t => !string.Equals(t, "DUAL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new GetTablesResponse { tables = tables };
        }

        /// <summary>Handles requests to list columns for a table.</summary>
        public async Task<GetColumnsResponse> Handle(GetColumnsParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/getColumns requested for {Conn}.{Table} (URI: {Uri})", request.connectionName, request.tableName, request.uri);
            var columns = (await metadata.GetColumnsAsync(request.connectionName, request.tableName, request.uri)).ToList();

            // Types are best-effort: sources that cannot report them still return names, and the
            // explorer simply shows no type rather than an invented one.
            var details = new List<ColumnDetail>();
            try
            {
                var known = (await metadata.GetColumnDetailsAsync(request.connectionName, request.tableName, request.uri))
                    .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().DataType, StringComparer.OrdinalIgnoreCase);

                details = columns
                    .Select(c => new ColumnDetail
                    {
                        name = c,
                        dataType = known.TryGetValue(c, out var t) && !string.IsNullOrWhiteSpace(t) ? t : null
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Column types unavailable for {Conn}.{Table}; returning names only.",
                    request.connectionName, request.tableName);
                details = columns.Select(c => new ColumnDetail { name = c }).ToList();
            }

            return new GetColumnsResponse { columns = columns, columnDetails = details };
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

                // The webview preview is a browser client, so it gets the browser projection —
                // not the server's working object with its full semantic contracts attached.
                response.manifestJson = ETL_SQL.Reporting.BrowserDeliveryProjection.Serialize(manifest);
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "LSP: Error building report manifest.");
                response.errors.Add(ex.Message);
            }

            return response;
        }

        /// <summary>Handles requests to discover connector schemas and option descriptors.</summary>
        public Task<GetConnectorSchemasResponse> Handle(GetConnectorSchemasParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/getConnectorSchemas requested (Type: {Type})", request.type);
            var schemas = string.IsNullOrWhiteSpace(request.type)
                ? connectorRegistry.GetAllConnectorSchemas().ToList()
                : (connectorRegistry.GetConnectorSchema(request.type) is { } s ? new List<ConnectorSchemaDescriptor> { s } : new List<ConnectorSchemaDescriptor>());

            return Task.FromResult(new GetConnectorSchemasResponse { schemas = schemas });
        }

        /// <summary>Handles requests to parse unformatted connection strings into canonical connector options.</summary>
        public Task<ParseConnectionStringResponse> Handle(ParseConnectionStringParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/parseConnectionString requested.");
            var parsed = ConnectionStringParser.Parse(request.connectionString, request.hintProvider);
            return Task.FromResult(new ParseConnectionStringResponse
            {
                detectedProvider = parsed.DetectedProvider,
                options = parsed.Options,
                extractedCredential = parsed.ExtractedCredential,
                suggestedSecretKey = parsed.SuggestedSecretKey
            });
        }

        /// <summary>Handles requests to run layered connection diagnostic probes.</summary>
        public async Task<TestConnectionResponse> Handle(TestConnectionParams request, CancellationToken cancellationToken)
        {
            logger.LogInformation("LSP: etlsql/testConnection requested for {Type} (Target: {Target})", request.connectorType, request.target);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<IExecutionContext>();
                var report = await diagnosticEngine.DiagnoseTargetAsync(
                    context,
                    request.alias ?? "test_connection",
                    request.connectorType,
                    request.target ?? string.Empty,
                    request.options,
                    request.probeTimeoutSeconds > 0 ? request.probeTimeoutSeconds : 5,
                    cancellationToken);

                return new TestConnectionResponse
                {
                    succeeded = report.Succeeded,
                    connection = report.Connection,
                    connectorType = report.ConnectorType,
                    steps = report.Steps.Select(s => new DiagnosticStepDto
                    {
                        layer = s.Layer,
                        status = s.Status.ToString().ToLowerInvariant(),
                        detail = s.Detail,
                        remedy = s.Remedy
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LSP: Error testing connection.");
                return new TestConnectionResponse
                {
                    succeeded = false,
                    connection = request.alias ?? "test_connection",
                    connectorType = request.connectorType,
                    error = SecretRedactor.Redact(ex.Message)
                };
            }
        }
    }
}
