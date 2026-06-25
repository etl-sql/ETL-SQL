using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles SHOW VIEWS [INTO #temp] for session-scoped query aliases.
/// </summary>
public class ShowViewsStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowViewsStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowViewsStatement)statement;
        var table = new DataTable();
        table.AddColumn("Name");
        table.AddColumn("Query");

        foreach (var view in context.VarContext.GetViews().OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
        {
            var row = new Row();
            row["Name"] = view.Key;
            row["Query"] = view.Value.Query.ToSql();
            await table.AddRowAsync(row);
        }

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            var destination = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
        }
        else
        {
            if (table.Rows.Count == 0)
                context.Log("No views found.", ConsoleColor.Cyan);
            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }
}
