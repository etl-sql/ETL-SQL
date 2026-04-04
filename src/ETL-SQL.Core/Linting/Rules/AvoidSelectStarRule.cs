using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace ETL_SQL.Core.Linting.Rules
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
            if (statement is SelectStatement select)
            {
                if (select.Columns.Any(c => c.Expression is IdentifierExpression id && id.Name == "*"))
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
                foreach (var join in select.Joins)
                {
                    if (join.Table.Subquery != null) AnalyzeStatement(join.Table.Subquery, results);
                }
            }
            
            // Recurse into blocks/conditionals/containers
            if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) AnalyzeStatement(s, results);
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
            else if (statement is InsertStatement insert && insert.SelectQuery != null)
            {
                AnalyzeStatement(insert.SelectQuery, results);
            }
            else if (statement is MergeStatement merge)
            {
                // Merge target/source are TableReferences, but source could be a subquery? 
                // TableReference.Subquery is already checked inside SelectStatement but MERGE uses TableReference directly.
                if (merge.TargetTable.Subquery != null) AnalyzeStatement(merge.TargetTable.Subquery, results);
                if (merge.SourceTable.Subquery != null) AnalyzeStatement(merge.SourceTable.Subquery, results);
            }
        }
    }
}
