using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules;
public class FullyMaterializingDmlRule : ILintRule
{
    public string Name => "FullyMaterializingDml";
    public string Description => "Warns when MERGE, UPDATE, or DELETE use fully materializing execution paths.";

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
        if (statement == null)
        {
            return;
        }

        switch (statement)
        {
            case MergeStatement merge:
                AddWarning(results, "MERGE", merge.TargetTable.ToString(), merge.Line, merge.Column);
                AnalyzeTableSubquery(merge.TargetTable, results);
                AnalyzeTableSubquery(merge.SourceTable, results);
                break;

            case UpdateStatement update:
                AddWarning(results, "UPDATE", update.TargetTable.ToString(), update.Line, update.Column);
                AnalyzeTableSubquery(update.FromTable, results);
                if (update.Joins != null)
                {
                    foreach (var join in update.Joins)
                    {
                        AnalyzeTableSubquery(join.Table, results);
                    }
                }
                break;

            case DeleteStatement delete:
                AddWarning(results, "DELETE", delete.TargetTable.ToString(), delete.Line, delete.Column);
                AnalyzeTableSubquery(delete.TargetTable, results);
                break;

            case InsertStatement insert when insert.SelectQuery != null:
                AnalyzeStatement(insert.SelectQuery, results);
                break;

            case SelectStatement select:
                AnalyzeTableSubquery(select.FromTable, results);
                if (select.Joins != null)
                {
                    foreach (var join in select.Joins)
                    {
                        AnalyzeTableSubquery(join.Table, results);
                    }
                }
                break;

            case SetOperationStatement setOp:
                AnalyzeStatement(setOp.Left, results);
                AnalyzeStatement(setOp.Right, results);
                break;
        }

        AnalyzeNestedStatements(statement, results);
    }

    private void AnalyzeNestedStatements(Statement statement, List<LintResult> results)
    {
        switch (statement)
        {
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    AnalyzeStatement(child, results);
                }
                break;

            case IfStatement ifStmt:
                AnalyzeStatement(ifStmt.IfBody, results);
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var elseIf in ifStmt.ElseIfClauses)
                    {
                        AnalyzeStatement(elseIf.Body, results);
                    }
                }

                if (ifStmt.ElseBody != null)
                {
                    AnalyzeStatement(ifStmt.ElseBody, results);
                }
                break;

            case WhileStatement whileStmt:
                AnalyzeStatement(whileStmt.Body, results);
                break;

            case ForStatement forStmt:
                AnalyzeStatement(forStmt.Body, results);
                break;

            case ForeachStatement foreachStmt:
                AnalyzeStatement(foreachStmt.Body, results);
                break;

            case TryCatchStatement tryCatch:
                AnalyzeStatement(tryCatch.TryBody, results);
                AnalyzeStatement(tryCatch.CatchBody, results);
                break;

            case CreateProcedureStatement proc:
                AnalyzeStatement(proc.Body, results);
                break;

            case CreateFunctionStatement func:
                AnalyzeStatement(func.Body, results);
                break;

            case ParallelStatement parallel:
                AnalyzeStatement(parallel.Body, results);
                break;

            case ParallelForStatement parallelFor:
                AnalyzeStatement(parallelFor.Body, results);
                break;
        }
    }

    private void AnalyzeTableSubquery(TableReference? table, List<LintResult> results)
    {
        if (table?.Subquery != null)
        {
            AnalyzeStatement(table.Subquery, results);
        }
    }

    private void AddWarning(List<LintResult> results, string operation, string target, int line, int column)
    {
        results.Add(new LintResult
        {
            RuleName = Name,
            Severity = LintSeverity.Warning,
            Message = $"{operation} on '{target}' currently fully materializes the match set and is not certified as a bounded large-data operation. Use selective predicates, batch the mutation, or stage candidate rows before running at large scale.",
            LineNumber = line,
            ColumnNumber = column
        });
    }
}
