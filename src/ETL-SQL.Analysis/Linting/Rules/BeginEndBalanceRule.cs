using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
    public class BeginEndBalanceRule : ILintRule
    {
        public string Name => "BeginEndBalance";
        public string Description => "Checks if an EXECUTE pushdown block has matching BEGIN and END statements.";

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
            
            if (statement is ExecutePushdownStatement pushdown)
            {
                if (pushdown.HasUnbalancedBlocks)
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Error,
                        Message = "Mismatched BEGIN and END blocks in native SQL pushdown.",
                        LineNumber = pushdown.Line,
                        ColumnNumber = pushdown.Column
                    });
                }
            }
            else if (statement is BlockStatement block)
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
            else if (statement is CreateProcedureStatement proc)
            {
                if (proc.Body != null) AnalyzeStatement(proc.Body, results);
            }
            else if (statement is CreateFunctionStatement func)
            {
                if (func.Body != null) AnalyzeStatement(func.Body, results);
            }
            else if (statement is ExecuteRemoteBlockStatement remoteBlock)
            {
                if (remoteBlock.Body != null) AnalyzeStatement(remoteBlock.Body, results);
            }
        }
    }
}
