using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using ETL_SQL.Core;

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
    /// LSP Request Handler for getting temporary tables from a document.
    /// </summary>
    [Method("etlsql/getTempTables", Direction.ClientToServer)]
    public interface IGetTempTablesHandler : IJsonRpcRequestHandler<GetTempTablesParams, GetTempTablesResponse> { }

    /// <summary>
    /// LSP Notification Handler for toggling debug mode.
    /// </summary>
    [Method("etlsql/setDebugMode", Direction.ClientToServer)]
    public interface ISetDebugModeHandler : IJsonRpcNotificationHandler<SetDebugModeParams> { }

    /// <summary>
    /// Implementation of specialized ETL-SQL Language Server methods for metadata discovery and configuration.
    /// </summary>
    /// <param name="metadata">The metadata manager for connection and schema info.</param>
    /// <param name="logger">The logger instance.</param>
    public class CustomMethodsHandler(IMetadataManager metadata, ILogger<CustomMethodsHandler> logger) : ISetConnectionsHandler, IGetTablesHandler, IGetColumnsHandler, ISetDebugModeHandler, IGetViewsHandler, IGetTempTablesHandler
    {
        /// <summary>Handles toggle debug mode notification.</summary>
        public Task<Unit> Handle(SetDebugModeParams request, CancellationToken cancellationToken)
        {
            metadata.DebugMode = request.debugMode;
            logger.LogInformation("LSP: DebugMode set to {Value}", request.debugMode);
            return Task.FromResult(Unit.Value);
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
    }
}
