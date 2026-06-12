using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
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
            // We don't return early here because we might be validating subqueries that don't need external metadata.

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
            if (tableRef.Subquery != null)
            {
                var subqueryColumns = DeriveColumnsFromSubquery(tableRef.Subquery);
                if (subqueryColumns != null)
                {
                    await ValidateTableOperatorsAsync(tableRef, subqueryColumns, context, results);
                    return;
                }
            }

            var connName = tableRef.ConnectionName ?? context.Metadata?.GetConnections().FirstOrDefault() ?? "DEFAULT";
            var cols = context.Metadata == null ? null : (await context.Metadata.GetColumnsAsync(connName, tableRef.TableName))?.ToList();

            if (cols == null || !cols.Any()) return;
            await ValidateTableOperatorsAsync(tableRef, cols, context, results);
        }

        private async Task ValidateTableOperatorsAsync(TableReference tableRef, List<string> cols, ILintContext context, List<LintResult> results)
        {
            foreach (var op in tableRef.TableOperators)
            {
                if (op is PivotClause pivot)
                {
                    ValidatePivot(tableRef, pivot, cols, results);
                }
                else if (op is UnpivotClause unpivot)
                {
                    ValidateUnpivot(tableRef, unpivot, cols, results);
                }
            }
        }

        private List<string>? DeriveColumnsFromSubquery(Statement subquery)
        {
            if (subquery is SelectStatement sel)
            {
                var cols = new List<string>();
                foreach (var col in sel.Columns)
                {
                    if (!string.IsNullOrEmpty(col.Alias))
                    {
                        cols.Add(col.Alias);
                    }
                    else if (col.Expression is IdentifierExpression id)
                    {
                        cols.Add(id.Name);
                    }
                    else if (col.Expression is MemberAccessExpression ma)
                    {
                        cols.Add(ma.MemberName);
                    }
                }
                return cols.Any() ? cols : null;
            }
            return null;
        }

        private void ValidatePivot(TableReference tableRef, PivotClause pivot, List<string> cols, List<LintResult> results)
        {
            // Validate Aggregate Column
            if (!cols.Any(c => string.Equals(c, pivot.AggregateColumn, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Aggregate column '{pivot.AggregateColumn}' not found in source {(tableRef.Subquery != null ? "subquery" : $"table '{tableRef.TableName}'")}.",
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
                    Message = $"Pivot column '{pivot.PivotColumn}' not found in source {(tableRef.Subquery != null ? "subquery" : $"table '{tableRef.TableName}'")}.",
                    LineNumber = pivot.Line,
                    ColumnNumber = pivot.Column
                });
            }
        }

        private void ValidateUnpivot(TableReference tableRef, UnpivotClause unpivot, List<string> cols, List<LintResult> results)
        {
            foreach (var col in unpivot.UnpivotColumns)
            {
                if (!cols.Any(c => string.Equals(c, col, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Warning,
                        Message = $"Unpivot source column '{col}' not found in source {(tableRef.Subquery != null ? "subquery" : $"table '{tableRef.TableName}'")}.",
                        LineNumber = unpivot.Line,
                        ColumnNumber = unpivot.Column
                    });
                }
            }
        }
    }
}
