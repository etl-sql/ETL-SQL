using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the SHOW TAGS FOR TABLE statement, listing all metadata tags.
    /// </summary>
    public class ShowTagsHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowTagsStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowTagsStatement)statement;
            
            Dictionary<string, string> metadata;
            if (stmt.ColumnName != null)
            {
                metadata = (Dictionary<string, string>)context.LineageTracker.GetColumnMetadata(stmt.TableName, stmt.ColumnName);
            }
            else
            {
                metadata = context.LineageTracker.GetTableMetadata(stmt.TableName);
            }

            var table = new DataTable();
            table.AddColumn("TagName");
            table.AddColumn("TagValue");

            if (metadata != null)
            {
                foreach (var kv in metadata)
                {
                    var row = new Row();
                    row["TagName"] = kv.Key;
                    row["TagValue"] = kv.Value;
                    await table.AddRowAsync(row);
                }
            }

            if (stmt.IntoTable != null)
            {
                if (!context.Connections.ContainsKey(stmt.IntoTable))
                {
                    context.Connections[stmt.IntoTable] = new InMemoryDataSource();
                }
                var destination = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
                await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
            }
            else
            {
                context.LastResult = table;
            }
        }
    }
}
