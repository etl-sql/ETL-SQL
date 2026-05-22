using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Performs a best-effort syntactic linting of native SQL blocks (EXECUTE ... BEGIN ... END).
    /// It attempts to parse the inner SQL text using the standard ETL-SQL parser. 
    /// While it may not catch dialect-specific semantic errors, it identifies basic syntax issues.
    /// </summary>
    public class PushdownValidationRule : ILintRule
    {
        public string Name => "PushdownValidation";
        public string Description => "Performs syntactic linting of native SQL pushdown blocks.";

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
            if (statement is ExecutePushdownStatement pushdown)
            {
                ValidateNativeBlock(pushdown.SqlText, pushdown, results);
            }
            else if (statement is BlockStatement block)
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
        }

        private void ValidateNativeBlock(string sql, ExecutePushdownStatement node, List<LintResult> results)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = "EXECUTE PUSHDOWN block is empty. No SQL will be sent to the remote database.",
                    LineNumber = node.Line,
                    ColumnNumber = node.Column
                });
                return;
            }

            try
            {
                var lexer = new Lexer(sql);
                var tokens = lexer.Tokenize();
                var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
                var parsedScript = parser.Parse();
                
                foreach (var diag in parsedScript.Diagnostics)
                {
                    results.Add(new LintResult
                    {
                        RuleName = Name,
                        Severity = LintSeverity.Warning, // Warning because it might be valid native SQL
                        Message = $"Syntactic check of pushdown block failed. This may be due to native SQL syntax or a syntax error: {diag.Message}",
                        LineNumber = node.Line + diag.Line - 1,
                        ColumnNumber = diag.Column
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Info,
                    Message = $"Could not perform syntactic check of pushdown block: {ex.Message}",
                    LineNumber = node.Line,
                    ColumnNumber = node.Column
                });
            }
        }
    }
}
