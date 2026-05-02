using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace ETL_SQL.Core.Linting.Rules
{
    public class AggregateWithoutGroupByRule : ILintRule
    {
        public string Name => "AggregateWithoutGroupBy";
        public string Description => "Warns when aggregate functions are used without a GROUP BY clause, which results in a single row.";

        private static readonly HashSet<string> _aggregates = new(new[]
        {
            "COUNT", "SUM", "AVG", "MIN", "MAX", "STRING_AGG", "LIST_AGG",
            "PERCENTILE_CONT", "PERCENTILE_DISC", "VAR", "VARP", "VAR_SAMP", "VAR_POP",
            "STDEV", "STDEVP", "STDDEV", "STDDEV_SAMP", "STDDEV_POP",
            "CORR", "COVAR_SAMP", "COVAR_POP"
        });

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            foreach (var statement in script.Statements)
            {
                AnalyzeStatement(statement, results);
            }
            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void AnalyzeStatement(Statement statement, List<LintResult> results)
        {
            if (statement == null) return;

            if (statement is SelectStatement select)
            {
                bool hasGroupBy = (select.GroupBy != null && select.GroupBy.Count > 0) || select.GroupingSet != null;
                bool hasAggregates = select.Columns.Any(c => ContainsAggregate(c.Expression)) || ContainsAggregate(select.HavingClause);

                if (hasAggregates && !hasGroupBy)
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Info,
                        Message = "This query uses aggregate functions without a GROUP BY clause. It will return a single summary row for the entire dataset.",
                        LineNumber = select.Line,
                        ColumnNumber = select.Column
                    });
                }

                // Check subqueries
                if (select.FromTable?.Subquery != null) AnalyzeStatement(select.FromTable.Subquery, results);
                if (select.Joins != null)
                {
                    foreach (var join in select.Joins)
                    {
                        if (join.Table?.Subquery != null) AnalyzeStatement(join.Table.Subquery, results);
                    }
                }
            }
            else if (statement is SetOperationStatement setOp)
            {
                AnalyzeStatement(setOp.Left, results);
                AnalyzeStatement(setOp.Right, results);
            }
            
            // Recurse into blocks/conditionals
            if (statement is BlockStatement block)
            {
                if (block.Statements != null)
                {
                    foreach (var s in block.Statements) AnalyzeStatement(s, results);
                }
            }
            else if (statement is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody, results);
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, results);
                }
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results);
            }
            else if (statement is WhileStatement whileStmt)
            {
                AnalyzeStatement(whileStmt.Body, results);
            }
            else if (statement is ForStatement forStmt)
            {
                AnalyzeStatement(forStmt.Body, results);
            }
            else if (statement is ForeachStatement foreachStmt)
            {
                AnalyzeStatement(foreachStmt.Body, results);
            }
            else if (statement is TryCatchStatement tryCatch)
            {
                AnalyzeStatement(tryCatch.TryBody, results);
                AnalyzeStatement(tryCatch.CatchBody, results);
            }
        }

        private bool ContainsAggregate(Expression? expr)
        {
            if (expr == null) return false;

            if (expr is FunctionCallExpression f)
            {
                if (f.Window != null) return false; // Window functions are not aggregates in this context
                if (_aggregates.Contains(f.FunctionName.ToUpperInvariant())) return true;
                
                // Recurse into arguments
                return f.Arguments.Any(ContainsAggregate);
            }
            
            if (expr is BinaryExpression b) return ContainsAggregate(b.Left) || ContainsAggregate(b.Right);
            if (expr is UnaryExpression u) return ContainsAggregate(u.Expression);
            if (expr is InExpression i) return ContainsAggregate(i.Left) || ContainsAggregate(i.Right);
            if (expr is LikeExpression l) return ContainsAggregate(l.Left) || ContainsAggregate(l.Pattern);
            if (expr is IsNullExpression n) return ContainsAggregate(n.Expression);
            if (expr is SubqueryExpression s) return false; // Aggregate inside subquery doesn't affect outer query's grouping

            return false;
        }
    }
}
