using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// LINT-1: Validates that columns referenced in PIVOT and UNPIVOT clauses exist in the source table.
    /// </summary>
    public class PivotColumnValidationRule : ILintRule
    {
        public string Name => "PivotColumnValidation";
        public string Description => "Validates that columns referenced in PIVOT and UNPIVOT clauses exist in the source table.";

        public async Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            if (context.Metadata == null) return results;

            foreach (var stmt in script.Statements)
            {
                await AnalyzeStatementAsync(stmt, context, results);
            }

            return results;
        }

        private async Task AnalyzeStatementAsync(Statement stmt, ILintContext context, List<LintResult> results)
        {
            if (stmt is SelectStatement sel)
            {
                if (sel.FromTable != null) await AnalyzeTableRefAsync(sel.FromTable, context, results);
                foreach (var join in sel.Joins) await AnalyzeTableRefAsync(join.Table, context, results);
            }
            else if (stmt is InsertStatement ins)
            {
                if (ins.TargetTable != null) await AnalyzeTableRefAsync(ins.TargetTable, context, results);
                if (ins.SelectQuery != null) await AnalyzeStatementAsync(ins.SelectQuery, context, results);
            }
            else if (stmt is UpdateStatement upd)
            {
                if (upd.TargetTable != null) await AnalyzeTableRefAsync(upd.TargetTable, context, results);
            }
            else if (stmt is DeleteStatement del)
            {
                if (del.TargetTable != null) await AnalyzeTableRefAsync(del.TargetTable, context, results);
            }
            else if (stmt is BlockStatement block)
            {
                foreach (var s in block.Statements) await AnalyzeStatementAsync(s, context, results);
            }
        }

        private async Task AnalyzeTableRefAsync(TableReference tableRef, ILintContext context, List<LintResult> results)
        {
            if (tableRef.Subquery != null) return; // Skip subqueries for now as schema resolution is complex

            foreach (var op in tableRef.TableOperators)
            {
                if (op is PivotClause pivot)
                {
                    await ValidatePivotAsync(tableRef, pivot, context, results);
                }
                else if (op is UnpivotClause unpivot)
                {
                    await ValidateUnpivotAsync(tableRef, unpivot, context, results);
                }
            }
        }

        private async Task ValidatePivotAsync(TableReference tableRef, PivotClause pivot, ILintContext context, List<LintResult> results)
        {
            var connName = tableRef.ConnectionName ?? context.Metadata!.GetConnections().FirstOrDefault() ?? "DEFAULT";
            var cols = (await context.Metadata!.GetColumnsAsync(connName, tableRef.TableName))?.ToList();
            
            if (cols == null || !cols.Any()) return;

            // Validate Aggregate Column
            if (!cols.Any(c => string.Equals(c, pivot.AggregateColumn, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Aggregate column '{pivot.AggregateColumn}' not found in source table '{tableRef.TableName}'.",
                    LineNumber = pivot.Line,
                    ColumnNumber = pivot.Column
                });
            }

            // Validate Pivot Column
            if (!cols.Any(c => string.Equals(c, pivot.PivotColumn, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Pivot column '{pivot.PivotColumn}' not found in source table '{tableRef.TableName}'.",
                    LineNumber = pivot.Line,
                    ColumnNumber = pivot.Column
                });
            }
        }

        private async Task ValidateUnpivotAsync(TableReference tableRef, UnpivotClause unpivot, ILintContext context, List<LintResult> results)
        {
            var connName = tableRef.ConnectionName ?? context.Metadata!.GetConnections().FirstOrDefault() ?? "DEFAULT";
            var cols = (await context.Metadata!.GetColumnsAsync(connName, tableRef.TableName))?.ToList();
            
            if (cols == null || !cols.Any()) return;

            foreach (var col in unpivot.UnpivotColumns)
            {
                if (!cols.Any(c => string.Equals(c, col, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Warning,
                        Message = $"Unpivot source column '{col}' not found in source table '{tableRef.TableName}'.",
                        LineNumber = unpivot.Line,
                        ColumnNumber = unpivot.Column
                    });
                }
            }
        }
    }
}
