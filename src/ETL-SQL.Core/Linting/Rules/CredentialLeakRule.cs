using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// SEC-4: Warns when a PRINT, SEND EMAIL, or RAISERROR statement references a variable that likely contains credentials.
    /// Detection is based on variable names containing keywords (e.g. @password) or variables declared as ENCRYPTED.
    /// </summary>
    public class CredentialLeakRule : ILintRule
    {
        public string Name => "CredentialLeak";
        public string Description => "Detects potential credential leaks in output statements (PRINT, EMAIL, RAISERROR).";

        private static readonly string[] SensitiveKeywords = { "password", "secret", "token", "key", "pwd", "apikey", "connectionstring", "conn", "connection", "accesskey", "bearer", "auth", "cert", "privatekey", "passphrase", "pat", "credential", "auth_type", "accountkey", "sshkey", "fingerprint", "access_token", "refresh_token", "client_secret", "client_id", "credentials", "authorization", "proxy_info", "keyfile", "hostkey" };

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            var scopes = new Stack<Dictionary<string, bool>>();
            scopes.Push(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

            foreach (var statement in script.Statements)
            {
                AnalyzeStatement(statement, scopes, results);
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void AnalyzeStatement(Statement statement, Stack<Dictionary<string, bool>> scopes, List<LintResult> results)
        {
            if (statement is DeclareStatement declare)
            {
                bool isSensitive = declare.IsSensitive || declare.DataType.Equals("ENCRYPTED", StringComparison.OrdinalIgnoreCase) ||
                                 SensitiveKeywords.Any(k => declare.VariableName.Contains(k, StringComparison.OrdinalIgnoreCase));
                scopes.Peek()[declare.VariableName] = isSensitive;
            }
            else if (statement is PrintStatement print)
            {
                CheckLeak(print.Message, print, scopes, results, "PRINT");
            }
            else if (statement is EmailStatement email)
            {
                CheckLeak(email.Body, email, scopes, results, "SEND EMAIL body");
                CheckLeak(email.Subject, email, scopes, results, "SEND EMAIL subject");
            }
            else if (statement is RaiseErrorStatement raise)
            {
                CheckLeak(raise.Message, raise, scopes, results, "RAISERROR message");
                foreach (var p in raise.Parameters)
                {
                    CheckLeak(p, raise, scopes, results, "RAISERROR parameter");
                }
            }
            else if (statement is ExecStatement exec)
            {
                CheckLeak(exec.SqlExpression, exec, scopes, results, "EXECUTE/dynamic SQL");
            }
            else if (statement is ExecutePushdownStatement pushdown)
            {
                // Pushdown SQL text is often a string literal, but we should check it for sensitive variables
                CheckSqlTextLeak(pushdown.SqlText, pushdown, scopes, results, "EXECUTE pushdown SQL text");

                foreach (var p in pushdown.Parameters)
                {
                    CheckLeak(p, pushdown, scopes, results, "EXECUTE pushdown parameter");
                }
            }
            else if (statement is SetVariableStatement setStmt)
            {
                // SEC-4: Taint tracking - if RHS has sensitive variables, LHS becomes sensitive
                bool rhsSensitive = FindSensitiveVariables(setStmt.Value, scopes).Any();
                bool nameSensitive = SensitiveKeywords.Any(k => setStmt.VariableName.Contains(k, StringComparison.OrdinalIgnoreCase));
                
                scopes.Peek()[setStmt.VariableName] = rhsSensitive || nameSensitive;
            }
            else if (statement is BlockStatement block)
            {
                scopes.Push(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
                foreach (var s in block.Statements) AnalyzeStatement(s, scopes, results);
                scopes.Pop();
            }
            else if (statement is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody, scopes, results);
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, scopes, results);
                }
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, scopes, results);
            }
            else if (statement is WhileStatement whileStmt)
            {
                AnalyzeStatement(whileStmt.Body, scopes, results);
            }
            else if (statement is ForStatement forStmt)
            {
                scopes.Peek()[forStmt.VariableName] = SensitiveKeywords.Any(k => forStmt.VariableName.Contains(k, StringComparison.OrdinalIgnoreCase));
                AnalyzeStatement(forStmt.Body, scopes, results);
            }
            else if (statement is ForeachStatement foreachStmt)
            {
                scopes.Peek()[foreachStmt.VariableName] = SensitiveKeywords.Any(k => foreachStmt.VariableName.Contains(k, StringComparison.OrdinalIgnoreCase));
                AnalyzeStatement(foreachStmt.Body, scopes, results);
            }
        }

        private void CheckLeak(Expression expr, AstNode node, Stack<Dictionary<string, bool>> scopes, List<LintResult> results, string sinkName)
        {
            var sensitiveVars = FindSensitiveVariables(expr, scopes);
            ReportLeaks(sensitiveVars, node, results, sinkName);
        }

        private void CheckSqlTextLeak(string sql, AstNode node, Stack<Dictionary<string, bool>> scopes, List<LintResult> results, string sinkName)
        {
            var detected = new List<string>();
            // Use regex to find potential internal variable mentions in the raw SQL text
            var matches = System.Text.RegularExpressions.Regex.Matches(sql, @"@\w+");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (IsVariableSensitive(match.Value, scopes))
                {
                    detected.Add(match.Value);
                }
            }
            ReportLeaks(detected.Distinct().ToList(), node, results, sinkName);
        }

        private void ReportLeaks(List<string> sensitiveVars, AstNode node, List<LintResult> results, string sinkName)
        {
            if (sensitiveVars.Any())
            {
                string varList = string.Join(", ", sensitiveVars);
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Potential credential leak: {sinkName} references sensitive variable(s) ({varList}).",
                    LineNumber = node.Line,
                    ColumnNumber = node.Column
                });
            }
        }

        private List<string> FindSensitiveVariables(Expression expr, Stack<Dictionary<string, bool>> scopes)
        {
            var detected = new List<string>();
            FindSensitiveVariablesRecursive(expr, scopes, detected);
            return detected.Distinct().ToList();
        }

        private void FindSensitiveVariablesRecursive(Expression expr, Stack<Dictionary<string, bool>> scopes, List<string> detected)
        {
            if (expr is VariableExpression varExpr)
            {
                if (IsVariableSensitive(varExpr.Name, scopes))
                {
                    detected.Add(varExpr.Name);
                }
            }
            else if (expr is BinaryExpression binary)
            {
                FindSensitiveVariablesRecursive(binary.Left, scopes, detected);
                FindSensitiveVariablesRecursive(binary.Right, scopes, detected);
            }
            else if (expr is UnaryExpression unary)
            {
                FindSensitiveVariablesRecursive(unary.Expression, scopes, detected);
            }
            else if (expr is FunctionCallExpression call)
            {
                foreach (var arg in call.Arguments) FindSensitiveVariablesRecursive(arg, scopes, detected);
            }
            else if (expr is CaseExpression @case)
            {
                foreach (var when in @case.WhenClauses)
                {
                    FindSensitiveVariablesRecursive(when.Condition, scopes, detected);
                    FindSensitiveVariablesRecursive(when.Result, scopes, detected);
                }
                if (@case.ElseResult != null) FindSensitiveVariablesRecursive(@case.ElseResult, scopes, detected);
            }
            else if (expr is LiteralExpression literal && literal.Value is string s && s.StartsWith("ENC:", StringComparison.OrdinalIgnoreCase))
            {
                detected.Add("hardcoded 'ENC:' string");
            }
            else if (expr is ListExpression list)
            {
                foreach (var item in list.Items) FindSensitiveVariablesRecursive(item, scopes, detected);
            }
            else if (expr is MemberAccessExpression member)
            {
                FindSensitiveVariablesRecursive(member.Expression, scopes, detected);
            }
            // Add other expression types as needed (Subquery, etc.)
        }

        private bool IsVariableSensitive(string name, Stack<Dictionary<string, bool>> scopes)
        {
            foreach (var scope in scopes)
            {
                if (scope.TryGetValue(name, out bool isSensitive))
                {
                    return isSensitive;
                }
            }
            // Fallback: keywords check if declaration was missed (though linter usually walks in order)
            return SensitiveKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
    }
}
