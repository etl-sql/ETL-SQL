using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// Enforces centrally registered governance policies against parsed AST statements.
    /// </summary>
    public class GovernancePolicyRule : ILintRule
    {
        private readonly IGovernancePolicyRegistry _policies;

        public GovernancePolicyRule() : this(null)
        {
        }

        public GovernancePolicyRule(IGovernancePolicyRegistry? policies)
        {
            _policies = policies ?? GovernancePolicyRegistry.CreateDefault();
        }

        public string Name => "GovernancePolicy";
        public string Description => "Applies central governance policy decisions to parsed AST statements.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            foreach (var statement in script.Statements)
                AnalyzeStatement(statement, results);

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void AnalyzeStatement(Statement statement, List<LintResult> results)
        {
            if (statement is SetAllowPlaintextSecretsStatement { Enabled: true } allowPlaintext)
            {
                var policy = _policies.GetRequired("Engine:AllowPlaintextSecrets");
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Code = "GOV-FORBIDDEN-POLICY",
                    Severity = LintSeverity.Error,
                    Message = "SET ALLOW_PLAINTEXT_SECRETS ON is forbidden by governance policy Engine:AllowPlaintextSecrets.",
                    LineNumber = allowPlaintext.Line,
                    ColumnNumber = allowPlaintext.Column,
                    PolicyDecision = GovernancePolicyDecision.Violation(
                        policy,
                        "SET ALLOW_PLAINTEXT_SECRETS ON",
                        "Scripts may not enable plaintext secret persistence when the central policy forbids it.")
                });
            }

            switch (statement)
            {
                case BlockStatement block:
                    foreach (var child in block.Statements) AnalyzeStatement(child, results);
                    break;
                case IfStatement ifStmt:
                    AnalyzeStatement(ifStmt.IfBody, results);
                    if (ifStmt.ElseIfClauses != null)
                        foreach (var clause in ifStmt.ElseIfClauses) AnalyzeStatement(clause.Body, results);
                    if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, results);
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
                case ParallelStatement parallel:
                    AnalyzeStatement(parallel.Body, results);
                    break;
                case ParallelForStatement parallelFor:
                    AnalyzeStatement(parallelFor.Body, results);
                    break;
                case CreateProcedureStatement proc:
                    AnalyzeStatement(proc.Body, results);
                    break;
                case CreateFunctionStatement func:
                    AnalyzeStatement(func.Body, results);
                    break;
            }
        }
    }
}
