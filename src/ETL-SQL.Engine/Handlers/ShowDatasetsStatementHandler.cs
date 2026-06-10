using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles SHOW DATASETS [INTO #temp] — returns a result set describing all datasets
    /// visible to the calling context.
    ///
    /// Portal mode: queries the registry and returns Name, FolderPath, AccessLevel, RowCount,
    /// LastRefresh, IsStale, RefreshInterval, and Ttl for each dataset the caller may see.
    ///
    /// Non-portal mode: enumerates datasets created in the current script from IReportContext.
    /// </summary>
    public class ShowDatasetsStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(ShowDatasetsStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (ShowDatasetsStatement)statement;

            var table = new DataTable();
            table.AddColumn("Name");
            table.AddColumn("FolderPath");
            table.AddColumn("AccessLevel");
            table.AddColumn("RowCount");
            table.AddColumn("LastRefresh");
            table.AddColumn("IsStale");
            table.AddColumn("RefreshInterval");
            table.AddColumn("Ttl");

            var registry = context is Evaluator e ? e.DatasetRegistry : null;

            if (registry != null)
            {
                var callerCtx = (context as Evaluator)?.DatasetCallerContext ?? "";
                var datasets  = await registry.ListAll(callerCtx);
                foreach (var ds in datasets)
                {
                    var row = new Row();
                    row["Name"]            = ds.Name;
                    row["FolderPath"]      = ds.FolderPath;
                    row["AccessLevel"]     = ds.AccessLevel.ToString();
                    row["RowCount"]        = ds.RowCount;
                    row["LastRefresh"]     = ds.LastRefresh?.ToString("o") ?? "";
                    row["IsStale"]         = IsStale(ds) ? "1" : "0";
                    row["RefreshInterval"] = ds.RefreshInterval ?? "";
                    row["Ttl"]             = ds.Ttl ?? "";
                    await table.AddRowAsync(row);
                }
            }
            else if (context is IReportContext rc)
            {
                foreach (var kv in rc.DatasetDefinitions)
                {
                    var row = new Row();
                    row["Name"]            = kv.Key;
                    row["FolderPath"]      = Path.GetDirectoryName(context.CurrentScriptPath) ?? "";
                    row["AccessLevel"]     = DatasetAccessLevel.Private.ToString();
                    row["RowCount"]        = "";
                    row["LastRefresh"]     = "";
                    row["IsStale"]         = "0";
                    row["RefreshInterval"] = kv.Value.RefreshInterval ?? "";
                    row["Ttl"]             = kv.Value.Ttl ?? "";
                    await table.AddRowAsync(row);
                }
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
                    context.Log("No datasets found.", ConsoleColor.Cyan);
                context.LastResult = table;
                context.LastResultSets.Add(table);
            }
        }

        private static bool IsStale(DatasetMetadata ds)
        {
            if (!ds.LastRefresh.HasValue) return true;
            if (string.IsNullOrWhiteSpace(ds.Ttl)) return false;
            if (!TryParseDuration(ds.Ttl, out var ttl)) return false;
            return ds.LastRefresh.Value + ttl <= DateTime.UtcNow;
        }

        private static bool TryParseDuration(string duration, out TimeSpan result)
        {
            result = default;
            var m = System.Text.RegularExpressions.Regex.Match(
                duration.Trim(), @"^(\d+)([smhd])$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            int v = int.Parse(m.Groups[1].Value);
            result = m.Groups[2].Value.ToUpperInvariant() switch
            {
                "S" => TimeSpan.FromSeconds(v),
                "M" => TimeSpan.FromMinutes(v),
                "H" => TimeSpan.FromHours(v),
                "D" => TimeSpan.FromDays(v),
                _   => TimeSpan.Zero
            };
            return result != TimeSpan.Zero || v == 0;
        }
    }
}
