using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Warns when CREATE CONNECTION uses an option key that the target connector does not advertise.
    /// </summary>
    public class UnsupportedConnectionOptionRule : ILintRule
    {
        public string Name => "UnsupportedConnectionOption";
        public string Description => "Warns when CREATE CONNECTION uses option keys that are not supported by the target connector.";

        private static readonly HashSet<string> SharedOptions = new(StringComparer.OrdinalIgnoreCase)
        {
            "TEMPLATE"
        };

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            var registry = ConnectorRegistry.Instance;
            if (registry == null)
                return Task.FromResult<IEnumerable<LintResult>>(results);

            foreach (var statement in script.Statements)
                AnalyzeStatement(statement, registry, results);

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void AnalyzeStatement(Statement statement, IConnectorRegistry registry, List<LintResult> results)
        {
            if (statement is CreateConnectionStatement conn)
                CheckConnection(conn, registry, results);

            if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) AnalyzeStatement(s, registry, results);
            }
            else if (statement is IfStatement ifStmt)
            {
                AnalyzeStatement(ifStmt.IfBody, registry, results);
                if (ifStmt.ElseIfClauses != null)
                    foreach (var ei in ifStmt.ElseIfClauses) AnalyzeStatement(ei.Body, registry, results);
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, registry, results);
            }
            else if (statement is WhileStatement whileStmt)
            {
                AnalyzeStatement(whileStmt.Body, registry, results);
            }
            else if (statement is ForStatement forStmt)
            {
                AnalyzeStatement(forStmt.Body, registry, results);
            }
            else if (statement is ForeachStatement foreachStmt)
            {
                AnalyzeStatement(foreachStmt.Body, registry, results);
            }
            else if (statement is TryCatchStatement tryCatch)
            {
                AnalyzeStatement(tryCatch.TryBody, registry, results);
                AnalyzeStatement(tryCatch.CatchBody, registry, results);
            }
        }

        private void CheckConnection(CreateConnectionStatement conn, IConnectorRegistry registry, List<LintResult> results)
        {
            if (string.IsNullOrWhiteSpace(conn.ConnectionType) || conn.Options == null || conn.Options.Count == 0)
                return;

            var connector = registry.GetConnector(conn.ConnectionType);
            if (connector == null)
                return;

            var supported = connector.GetSupportedOptions().Keys
                .Concat(SharedOptions)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var option in conn.Options.Keys.Where(option => !supported.Contains(option)))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Connection '{conn.ConnectionName}': option '{option}' is not supported by {connector.Name}.",
                    LineNumber = conn.Line,
                    ColumnNumber = conn.Column
                });
            }
        }
    }
}
