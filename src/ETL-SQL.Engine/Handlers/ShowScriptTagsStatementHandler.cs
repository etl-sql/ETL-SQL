using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SHOW TAGS FOR SCRIPT statement, returning the metadata tags associated with the current script.
    /// </summary>
    public class ShowScriptTagsStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowScriptTagsStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowScriptTagsStatement)statement;

            var dt = new DataTable
            {
                Schema = new TableSchema(new[] { "TagName", "TagValue" })
            };

            foreach (var kvp in context.LineageTracker.GlobalMetadata)
            {
                await dt.AddRowAsync(new Row(dt.Schema, new object?[] { kvp.Key, kvp.Value }));
            }

            if (!string.IsNullOrEmpty(stmt.IntoTable))
            {
                if (!context.Connections.ContainsKey(stmt.IntoTable))
                {
                    context.Connections[stmt.IntoTable] = new ETL_SQL.Data.InMemoryDataSource();
                }
                var destination = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
                await destination.WriteBatches(new[] { dt }.ToAsyncEnumerable());
            }
            else
            {
                context.LastResult = dt;
                context.LastResultSets.Add(dt);
            }
        }
    }
}
