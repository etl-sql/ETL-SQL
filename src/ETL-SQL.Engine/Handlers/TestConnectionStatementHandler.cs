using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles <c>TEST CONNECTION &lt;alias&gt; [INTO #tmp]</c>: runs the governed layered connection
/// diagnostic (DNS → TCP → TLS) via <see cref="ConnectionDiagnosticEngine"/> and renders a
/// plain-English troubleshooting report. Secret values never appear in the output.
/// </summary>
public class TestConnectionStatementHandler(IConnectorRegistry connectorRegistry, IConfiguration? config = null)
    : IStatementHandler
{
    private readonly IConnectorRegistry _connectorRegistry = connectorRegistry;
    private readonly IConfiguration? _config = config;

    public Type SupportedStatementType => typeof(TestConnectionStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (TestConnectionStatement)statement;

        var timeoutSeconds = _config?.GetValue<int?>("Engine:Diagnostics:ProbeTimeoutSeconds") ?? 5;
        var engine = new ConnectionDiagnosticEngine(_connectorRegistry);
        var report = await engine.DiagnoseAsync(context, stmt.ConnectionName, timeoutSeconds, context.CancellationToken);

        var table = new DataTable();
        table.AddColumn("Layer");
        table.AddColumn("Status");
        table.AddColumn("Detail");
        table.AddColumn("Remedy");

        foreach (var step in report.Steps)
        {
            var row = new Row();
            row["Layer"] = step.Layer;
            row["Status"] = step.Status.ToString().ToUpperInvariant();
            row["Detail"] = step.Detail;
            row["Remedy"] = step.Remedy ?? string.Empty;
            await table.AddRowAsync(row);
        }

        if (stmt.IntoTable != null)
        {
            await WriteToTempTable(stmt.IntoTable, table, context);
            return;
        }

        if (!context.RedirectOutput)
            PrintReport(report, context);

        context.LastResult = table;
        context.LastResultSets.Add(table);
        context.OnResultSet?.Invoke(table);
    }

    private static void PrintReport(DiagnosticReport report, IExecutionContext context)
    {
        context.Log($"Connection diagnostic for '{report.Connection}' ({report.ConnectorType}):", ConsoleColor.Cyan);
        foreach (var step in report.Steps)
        {
            var (marker, color) = step.Status switch
            {
                DiagnosticStatus.Ok => ("[ OK ]", ConsoleColor.Green),
                DiagnosticStatus.Failed => ("[FAIL]", ConsoleColor.Red),
                DiagnosticStatus.Denied => ("[DENY]", ConsoleColor.Red),
                _ => ("[ -- ]", ConsoleColor.DarkGray),
            };
            context.Log($"  {marker} {step.Layer,-7} {step.Detail}", color);
            if (!string.IsNullOrWhiteSpace(step.Remedy) && step.Status is DiagnosticStatus.Failed or DiagnosticStatus.Denied)
                context.Log($"         ↳ {step.Remedy}", ConsoleColor.Yellow);
        }

        context.Log(
            report.Succeeded ? "Result: all attempted checks passed." : "Result: one or more checks did not pass.",
            report.Succeeded ? ConsoleColor.Green : ConsoleColor.Red);
    }

    private static async Task WriteToTempTable(string tableName, DataTable table, IExecutionContext context)
    {
        if (!context.Connections.ContainsKey(tableName))
            context.Connections[tableName] = new InMemoryDataSource();
        var destination = await context.ResolveDataSourceAsync(new TableReference(tableName));
        await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
    }
}
