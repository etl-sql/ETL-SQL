using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace ETL_SQL.Analysis.Linting.Rules
{
    public class AvoidSelectStarRule : ILintRule
    {
        public string Name => "AvoidSelectStar";
        public string Description => "Warns when SELECT * is used.";

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
                if (select.Columns != null && select.Columns.Any(c => c.Expression?.ToSql() == "*"))
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Warning,
                        Message = "Avoid using 'SELECT *'. Explicitly list the columns you need for better performance and maintainability.",
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
            
            // Recurse into blocks/conditionals/containers
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
            
            // Check other statements that might contain subqueries or nested queries
            if (statement is InsertStatement insert && insert.SelectQuery != null)
            {
                AnalyzeStatement(insert.SelectQuery, results);
            }
            else if (statement is MergeStatement merge)
            {
                if (merge.TargetTable?.Subquery != null) AnalyzeStatement(merge.TargetTable.Subquery, results);
                if (merge.SourceTable?.Subquery != null) AnalyzeStatement(merge.SourceTable.Subquery, results);
            }
            else if (statement is UpdateStatement update)
            {
                if (update.FromTable?.Subquery != null) AnalyzeStatement(update.FromTable.Subquery, results);
                if (update.Joins != null)
                {
                    foreach (var join in update.Joins)
                    {
                        if (join.Table?.Subquery != null) AnalyzeStatement(join.Table.Subquery, results);
                    }
                }
            }
            else if (statement is DeleteStatement delete)
            {
                if (delete.TargetTable?.Subquery != null) AnalyzeStatement(delete.TargetTable.Subquery, results);
            }
        }
    }
}
