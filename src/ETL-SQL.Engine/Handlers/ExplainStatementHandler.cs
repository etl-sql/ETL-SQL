using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Explain;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Planning;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the EXPLAIN statement, generating and displaying a high-level execution plan for a given query.
/// </summary>
public class ExplainStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ExplainStatement);
    /// <summary>Executes the EXPLAIN statement, building a plan table and displaying it via Spectre.Console.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ExplainStatement)statement;

        var plan = new DataTable();
        var columns = new List<string> { "ID", "Operation", "Details", "Cost", "Mode", "Est. Rows", "Plan Candidates", "Plan Notes" };
        if (stmt.IsAnalyze)
        {
            columns.Add("Actual Rows");
            columns.Add("Actual Time (ms)");
            columns.Add("Spill Bytes");
            columns.Add("Spill Count");
            columns.Add("Plan Decisions");
            columns.Add("Plan Fallbacks");
            columns.Add("Plan Decision Summary");
        }
        plan.SetColumns(columns.ToArray());

        var metrics = new ExecutionMetrics
        {
            Sql = (stmt.IsAnalyze ? "EXPLAIN ANALYZE: " : "EXPLAIN: ") + stmt.Query.ToSql(),
            Timestamp = DateTime.Now
        };

        // If ANALYZE, run the actual query first to collect metrics
        long actualRows = 0;
        long actualTime = 0;
        long spillBytes = 0;
        int spillCount = 0;
        if (stmt.IsAnalyze)
        {
            var oldProfiling = context.Telemetry.IsProfiling;
            var oldRedirect = context.RedirectOutput;
            context.Telemetry.IsProfiling = true;
            context.RedirectOutput = true;

            long spillBefore = context.Telemetry.TotalSpilledBytes;
            int sortSpillBefore = context.Telemetry.SortSpillCount;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await foreach (var batch in context.ExecuteQuery(stmt.Query))
                    actualRows += batch.Rows.Count;
            }
            finally
            {
                sw.Stop();
                actualTime = sw.ElapsedMilliseconds;
                spillBytes = context.Telemetry.TotalSpilledBytes - spillBefore;
                spillCount = context.Telemetry.SortSpillCount - sortSpillBefore;
                context.Telemetry.IsProfiling = oldProfiling;
                context.RedirectOutput = oldRedirect;
            }
        }

        await new ExplainPlanBuilder().BuildAsync(stmt.Query, plan, context, metrics);
        InitializeStaticPlanHints(plan);
        ApplyStaticPlanHints(stmt.Query, plan);

        if (stmt.IsAnalyze)
        {
            // Initialize ANALYZE columns on all rows.
            foreach (var planRow in plan.Rows)
            {
                planRow["Actual Rows"] = "--";
                planRow["Actual Time (ms)"] = "--";
                planRow["Spill Bytes"] = 0L;
                planRow["Spill Count"] = 0;
                planRow["Plan Decisions"] = "--";
                planRow["Plan Fallbacks"] = "--";
                planRow["Plan Decision Summary"] = "--";
            }

            // Assign total elapsed time and row count to the last plan row.
            var lastRow = plan.Rows.LastOrDefault();
            if (lastRow != null)
            {
                lastRow["Actual Rows"] = actualRows;
                lastRow["Actual Time (ms)"] = actualTime;
                var planDecisions = context.Telemetry.PlanDecisions;
                lastRow["Plan Decisions"] = planDecisions.Count;
                lastRow["Plan Fallbacks"] = planDecisions.Count(d => d.Outcome == PlanDecisionOutcome.Fallback);
                lastRow["Plan Decision Summary"] = PlanDecisionSummary.FormatFallbackSummary(planDecisions);
            }

            // Assign spill stats to the Sort row; fall back to last row if no Sort present.
            if (spillBytes > 0 || spillCount > 0)
            {
                var sortRow = plan.Rows.LastOrDefault(r => r["Operation"]?.ToString() == "Sort")
                              ?? lastRow;
                if (sortRow != null)
                {
                    sortRow["Spill Bytes"] = spillBytes;
                    sortRow["Spill Count"] = spillCount;
                }
            }
        }

        // Populate the context's profile metrics so the UI Performance tab can see it
        metrics.DurationMs = plan.Rows.Sum(r => Convert.ToInt64(r["Cost"] ?? 0));
        context.Telemetry.ProfileMetrics.Add(metrics);

        context.LastResult = plan;
        context.LastResultSets.Add(plan);

        if (stmt.IntoTable != null)
        {
            var destination = await context.ResolveDataSourceAsync(stmt.IntoTable);
            await destination.WriteBatches(new List<DataTable> { plan }.ToAsyncEnumerable());
            context.Log($"Query plan stored in {stmt.IntoTable.TableName}.");
        }
        else
        {
            context.OnResultSet?.Invoke(plan);
            if (!context.RedirectOutput)
            {
                ResultFormatter.PrintTable(plan);
                context.Log($"Total Plan Cost: {metrics.DurationMs}", ConsoleColor.Yellow);
                if (stmt.IsAnalyze)
                {
                    context.Log($"Total Actual Time: {actualTime}ms", ConsoleColor.Green);
                    context.Log($"Total Actual Rows: {actualRows}", ConsoleColor.Green);
                }
            }
        }
    }

    private static void InitializeStaticPlanHints(DataTable plan)
    {
        foreach (var planRow in plan.Rows)
        {
            planRow["Plan Candidates"] = "--";
            planRow["Plan Notes"] = "--";
        }
    }

    private static void ApplyStaticPlanHints(Statement query, DataTable plan)
    {
        if (query is not SelectStatement select) return;

        if (IsSimpleColumnarCandidate(select))
        {
            AddCandidate(
                plan.Rows.FirstOrDefault(row => row["Operation"]?.ToString() is "Scan" or "Index Seek"),
                "ColumnarProjection",
                "Static candidate; runtime source capability and expression support decide acceptance.");
        }

        if (select.Joins.Count > 0)
        {
            foreach (var row in plan.Rows.Where(row => IsJoinOperation(row["Operation"]?.ToString())))
            {
                AddCandidate(
                    row,
                    "ColumnarJoin",
                    "Static candidate for equi-join shapes; runtime source capability, key type, and memory admission decide acceptance.");
            }
        }

        if (select.GroupBy != null || HasAggregate(select))
        {
            AddCandidate(
                plan.Rows.FirstOrDefault(row => row["Operation"]?.ToString() == "Aggregate"),
                select.GroupBy != null ? "ColumnarGroupedAggregate" : "ColumnarAggregate",
                "Static candidate; runtime source capability, aggregate expression, key type, and memory admission decide acceptance.");
        }

        if (select.OrderBy != null && select.OrderBy.Count > 0)
        {
            AddCandidate(
                plan.Rows.FirstOrDefault(row => row["Operation"]?.ToString() == "Sort"),
                "ColumnarSort",
                "Static candidate; runtime source capability, collation, key type, and memory admission decide acceptance.");
        }
    }

    private static bool IsJoinOperation(string? operation)
        => operation is "Hash Join" or "Join" or "Index Join";

    private static void AddCandidate(Row? row, string candidate, string note)
    {
        if (row == null) return;

        var existingCandidates = row["Plan Candidates"]?.ToString();
        row["Plan Candidates"] = string.IsNullOrWhiteSpace(existingCandidates) || existingCandidates == "--"
            ? candidate
            : existingCandidates + ", " + candidate;

        var existingNotes = row["Plan Notes"]?.ToString();
        row["Plan Notes"] = string.IsNullOrWhiteSpace(existingNotes) || existingNotes == "--"
            ? note
            : existingNotes + " " + note;
    }

    private static bool IsSimpleColumnarCandidate(SelectStatement stmt)
        => stmt.FromTable.TableOperators.Count == 0
            && (stmt.Joins == null || stmt.Joins.Count == 0)
            && stmt.GroupBy == null && stmt.GroupingSet == null
            && stmt.OrderBy == null && stmt.Offset == null && stmt.LimitCount == null && stmt.TopCount == null
            && !stmt.IsDistinct && stmt.QualifyClause == null && stmt.Sample == null
            && !stmt.IsTopPercent && !stmt.GroupByAll && !stmt.OrderByAll
            && !HasLateralColumnAlias(stmt.Columns);

    private static bool HasLateralColumnAlias(IReadOnlyList<SelectColumn> columns)
    {
        var previousAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            if (UsesAnyIdentifier(column.Expression, previousAliases)) return true;
            if (!string.IsNullOrWhiteSpace(column.Alias)) previousAliases.Add(column.Alias);
        }

        return false;
    }

    private static bool HasAggregate(SelectStatement select)
        => select.Columns.Any(column => IsAggregate(column.Expression));

    private static bool IsAggregate(Expression? expr)
    {
        if (expr is FunctionCallExpression f)
        {
            var name = f.FunctionName.ToUpperInvariant();
            return name is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX" or "TOTAL" or "GROUP_CONCAT";
        }

        return expr is BinaryExpression b && (IsAggregate(b.Left) || IsAggregate(b.Right));
    }

    private static bool UsesAnyIdentifier(Expression? expression, HashSet<string> names)
    {
        if (expression == null || names.Count == 0) return false;

        switch (expression)
        {
            case IdentifierExpression identifier:
                return names.Contains(identifier.Name);
            case BinaryExpression binary:
                return UsesAnyIdentifier(binary.Left, names) || UsesAnyIdentifier(binary.Right, names);
            case UnaryExpression unary:
                return UsesAnyIdentifier(unary.Expression, names);
            case FunctionCallExpression function:
                return function.Arguments.Any(argument => UsesAnyIdentifier(argument, names));
            case MemberAccessExpression member:
                return UsesAnyIdentifier(member.Expression, names) || names.Contains(member.MemberName);
            case CaseExpression @case:
                return UsesAnyIdentifier(@case.InputExpression, names)
                    || @case.WhenClauses.Any(when => UsesAnyIdentifier(when.Condition, names) || UsesAnyIdentifier(when.Result, names))
                    || UsesAnyIdentifier(@case.ElseResult, names);
            default:
                return false;
        }
    }

}



