using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;

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
            var connMap = BuildConnectionMap(script.Statements);

            foreach (var statement in script.Statements)
            {
                AnalyzeStatement(statement, connMap, results, context);
            }
            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void AnalyzeStatement(Statement statement, Dictionary<string, string> connMap, List<LintResult> results, ILintContext context)
        {
            if (statement is ExecutePushdownStatement pushdown)
            {
                var connName = pushdown.ConnectionName is IdentifierExpression id ? id.Name : pushdown.ConnectionName.ToSql();
                if (!connMap.TryGetValue(connName, out var connType))
                {
                    connType = context.Metadata?.GetConnectionType(connName);
                }

                if (string.Equals(connType, "NEO4J", StringComparison.OrdinalIgnoreCase))
                {
                    return; // Cypher query is not SQL
                }

                ValidateNativeBlock(pushdown.SqlText, pushdown, results);
            }
            else if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) AnalyzeStatement(s, connMap, results, context);
            }
            else if (statement is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody, connMap, results, context);
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, connMap, results, context);
                }
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, connMap, results, context);
            }
            else if (statement is WhileStatement whileStmt)
            {
                AnalyzeStatement(whileStmt.Body, connMap, results, context);
            }
            else if (statement is ForStatement forStmt)
            {
                AnalyzeStatement(forStmt.Body, connMap, results, context);
            }
            else if (statement is ForeachStatement foreachStmt)
            {
                AnalyzeStatement(foreachStmt.Body, connMap, results, context);
            }
        }

        private static Dictionary<string, string> BuildConnectionMap(IEnumerable<Statement> statements)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var stmt in statements)
            {
                CollectConnections(stmt, map);
            }
            return map;
        }

        private static void CollectConnections(Statement stmt, Dictionary<string, string> map)
        {
            if (stmt is CreateConnectionStatement conn)
            {
                if (conn.name != null && conn.type != null)
                    map[conn.name] = conn.type;
            }
            else if (stmt is AlterConnectionStatement alter)
            {
                if (alter.ConnectionName != null && alter.ConnectionType != null)
                    map[alter.ConnectionName] = alter.ConnectionType;
            }
            else if (stmt is BlockStatement block)
            {
                foreach (var s in block.Statements) CollectConnections(s, map);
            }
            else if (stmt is IfStatement ifStmt)
            {
                CollectConnections(ifStmt.IfBody, map);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) CollectConnections(ei.Body, map);
                if (ifStmt.ElseBody != null) CollectConnections(ifStmt.ElseBody, map);
            }
            else if (stmt is WhileStatement whileStmt) CollectConnections(whileStmt.Body, map);
            else if (stmt is ForStatement forStmt) CollectConnections(forStmt.Body, map);
            else if (stmt is ForeachStatement foreachStmt) CollectConnections(foreachStmt.Body, map);
            else if (stmt is TryCatchStatement tryCatch)
            {
                CollectConnections(tryCatch.TryBody, map);
                CollectConnections(tryCatch.CatchBody, map);
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
