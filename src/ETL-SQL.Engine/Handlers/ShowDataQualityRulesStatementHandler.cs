using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles <c>SHOW DATA QUALITY RULES</c>: lists the <c>EXPECT</c> rules protecting
/// each column, read from the lineage the run recorded.
/// <para>
/// Rules are declared as governance tags precisely so a steward can see what protects a column
/// without reading engine internals — but that only holds if something surfaces them. This is that
/// surface. Rules appear against the statement that declares them (they are deliberately not
/// inherited downstream, since they are enforcement directives rather than descriptive metadata).
/// </para>
/// </summary>
public class ShowDataQualityRulesStatementHandler(ILogger logger) : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowDataQualityRulesStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowDataQualityRulesStatement)statement;

        var table = new DataTable();
        table.SetColumns([
            "TargetTable", "TargetColumn", "RuleClause", "Rule", "Action",
            "SourceFile", "Line"
        ]);

        foreach (var row in CollectRules(context, stmt))
            await table.AddRowAsync(row(table));

        if (stmt.IntoTable != null)
        {
            if (!context.Connections.ContainsKey(stmt.IntoTable))
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            var destination = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await destination.WriteBatches(new[] { table }.ToAsyncEnumerable(), append: false);
            logger.WriteLine($"{table.Rows.Count} data-quality rule(s) written to {stmt.IntoTable}.", ConsoleColor.Green);
            return;
        }

        if (table.Rows.Count == 0)
        {
            context.Log(
                "No data-quality rules recorded for this session. Rules are captured when a statement "
                + "carrying EXPECT rules runs; nothing has run yet in this session, or the filters matched nothing.",
                ConsoleColor.Cyan);
        }
        else if (!context.RedirectOutput)
        {
            ResultFormatter.PrintTable(table);
        }

        context.LastResult = table;
        context.LastResultSets.Add(table);
        context.OnResultSet?.Invoke(table);
    }

    /// <summary>
    /// Projects one output row per individual rule (not per tag), so a column carrying
    /// <c>'NOT NULL, &gt;= 0'</c> reads as two protections rather than one opaque string.
    /// </summary>
    private static IEnumerable<Func<DataTable, Row>> CollectRules(
        IExecutionContext context, ShowDataQualityRulesStatement stmt)
    {
        var wantedTable = stmt.TargetTable?.TableName;
        var wantedColumn = stmt.ColumnName;

        var entries = context.LineageTracker.GetFullLineage()
            .Where(e => ColumnRuleParser.HasRuleTags(e.Metadata))
            .Where(e => wantedTable == null
                || e.TargetTable.Equals(wantedTable, StringComparison.OrdinalIgnoreCase))
            .Where(e => wantedColumn == null
                || (e.TargetColumn?.Equals(wantedColumn, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(e => e.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.TargetColumn ?? "", StringComparer.OrdinalIgnoreCase);

        // One lineage entry per (target, column) is the norm, but a column re-declared across
        // several statements legitimately appears more than once — dedupe on the rule itself so a
        // steward sees each distinct protection once.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            IReadOnlyList<ColumnRuleBinding> bindings;
            try
            {
                bindings = ColumnRuleParser.ParseBindings(entry.Metadata);
            }
            catch (ColumnRuleParseException)
            {
                // Malformed rules are reported by the linter; do not fail the listing over them.
                continue;
            }

            foreach (var binding in bindings)
            {
                foreach (var rule in binding.Rules)
                {
                    var key = $"{entry.TargetTable}|{entry.TargetColumn}|{binding.ExpectKey}|{rule.Text}";
                    if (!seen.Add(key)) continue;

                    var captured = entry;
                    var capturedBinding = binding;
                    var capturedRule = rule;
                    yield return owner =>
                    {
                        var row = owner.NewRow();
                        row["TargetTable"] = captured.TargetTable;
                        row["TargetColumn"] = captured.TargetColumn;
                        row["RuleClause"] = capturedBinding.ClauseLabel;
                        row["Rule"] = capturedRule.Text;
                        row["Action"] = capturedBinding.Action.ToString().ToUpperInvariant()
                            + (capturedBinding.ActionExplicit ? "" : " (default)");
                        row["SourceFile"] = captured.SourceFile;
                        row["Line"] = (decimal)captured.Line;
                        return row;
                    };
                }
            }
        }
    }
}
