using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers
{
    public class ShowLineageHistoryForTableStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowLineageHistoryForTableStatement);
        private readonly ILineageCatalogStore _catalog;
        private readonly IConfiguration? _config;

        public ShowLineageHistoryForTableStatementHandler(ILineageCatalogStore catalog, IConfiguration? config = null)
        {
            _catalog = catalog;
            _config = config;
        }

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowLineageHistoryForTableStatement)statement;

            if (stmt.At != null)
            {
                await LineageHistoryRouting.RouteToRemoteAsync(stmt, stmt.At, context);
                return;
            }

            int defaultLimit = _config?.GetValue<int>("Engine:DefaultHistoryLimit") ?? 100;
            var entries = await _catalog.GetHistoryForTableAsync(stmt.TableName, stmt.Limit ?? defaultLimit);
            var table = await LineageHistoryRouting.BuildTable(entries);

            if (stmt.IntoTable != null)
            {
                if (!context.Connections.ContainsKey(stmt.IntoTable))
                    context.Connections[stmt.IntoTable] = new InMemoryDataSource();
                var dest = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
                await dest.WriteBatches(new[] { table }.ToAsyncEnumerable());
            }
            else
            {
                context.LastResult = table;
                context.LastResultSets.Add(table);
                context.OnResultSet?.Invoke(table);
            }
        }
    }

    public class ShowLineageHistoryForTagStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowLineageHistoryForTagStatement);
        private readonly ILineageCatalogStore _catalog;
        private readonly IConfiguration? _config;

        public ShowLineageHistoryForTagStatementHandler(ILineageCatalogStore catalog, IConfiguration? config = null)
        {
            _catalog = catalog;
            _config = config;
        }

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowLineageHistoryForTagStatement)statement;

            if (stmt.At != null)
            {
                await LineageHistoryRouting.RouteToRemoteAsync(stmt, stmt.At, context);
                return;
            }

            int defaultLimit = _config?.GetValue<int>("Engine:DefaultHistoryLimit") ?? 100;
            var entries = await _catalog.GetHistoryForTagAsync(stmt.TagKey, stmt.TagValue, stmt.Limit ?? defaultLimit);
            var table = await LineageHistoryRouting.BuildTable(entries);

            if (stmt.IntoTable != null)
            {
                if (!context.Connections.ContainsKey(stmt.IntoTable))
                    context.Connections[stmt.IntoTable] = new InMemoryDataSource();
                var dest = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
                await dest.WriteBatches(new[] { table }.ToAsyncEnumerable());
            }
            else
            {
                context.LastResult = table;
                context.LastResultSets.Add(table);
                context.OnResultSet?.Invoke(table);
            }
        }
    }

    public class ShowLineageHistoryForJobStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowLineageHistoryForJobStatement);
        private readonly ILineageCatalogStore _catalog;
        private readonly IConfiguration? _config;

        public ShowLineageHistoryForJobStatementHandler(ILineageCatalogStore catalog, IConfiguration? config = null)
        {
            _catalog = catalog;
            _config = config;
        }

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowLineageHistoryForJobStatement)statement;

            if (stmt.At != null)
            {
                await LineageHistoryRouting.RouteToRemoteAsync(stmt, stmt.At, context);
                return;
            }

            int defaultLimit = _config?.GetValue<int>("Engine:DefaultHistoryLimit") ?? 100;
            var entries = await _catalog.GetHistoryForJobAsync(stmt.JobName, stmt.Limit ?? defaultLimit);
            var table = await LineageHistoryRouting.BuildTable(entries);

            if (stmt.IntoTable != null)
            {
                if (!context.Connections.ContainsKey(stmt.IntoTable))
                    context.Connections[stmt.IntoTable] = new InMemoryDataSource();
                var dest = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
                await dest.WriteBatches(new[] { table }.ToAsyncEnumerable());
            }
            else
            {
                context.LastResult = table;
                context.LastResultSets.Add(table);
                context.OnResultSet?.Invoke(table);
            }
        }
    }

    internal static class LineageHistoryRouting
    {
        internal static async Task RouteToRemoteAsync(Statement stmt, string atConn, IExecutionContext context)
        {
            IDataSource? conn = null;
            if (context.Connections.TryGetValue(atConn, out conn)) { }
            else conn = context.Connections.FirstOrDefault(c => c.Key.Equals(atConn, StringComparison.OrdinalIgnoreCase)).Value;

            if (conn == null)
            {
                var available = string.Join(", ", context.Connections.Keys);
                throw new ExecutionException($"Connection '{atConn}' not found in current session. Registered connections: [{available}]");
            }

            if (conn is not IPortalAdminConnection adminConn)
                throw new ExecutionException($"Connection '{atConn}' (Type: {conn.ConnectorType}) does not support orchestrator operations.");

            await adminConn.ExecuteAdminStatementAsync(stmt, context);
        }

        internal static async Task<DataTable> BuildTable(IEnumerable<LineageHistoryEntry> entries)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "Id", "RunAt", "JobName", "TargetTable", "TargetColumn", "SourceTables", "Operation", "Tags", "SourceFile", "Line" });
            foreach (var e in entries)
            {
                var row = new Row();
                row["Id"] = e.Id;
                row["RunAt"] = e.RunAt;
                row["JobName"] = e.JobName;
                row["TargetTable"] = e.TargetTable;
                row["TargetColumn"] = e.TargetColumn;
                row["SourceTables"] = string.Join(", ", e.SourceTables);
                row["Operation"] = e.Operation;
                row["Tags"] = System.Text.Json.JsonSerializer.Serialize(e.Tags);
                row["SourceFile"] = e.SourceFile;
                row["Line"] = e.Line;
                await table.AddRowAsync(row);
            }
            return table;
        }
    }
}
