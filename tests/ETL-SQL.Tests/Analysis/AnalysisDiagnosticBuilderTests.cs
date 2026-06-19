using ETL_SQL.Analysis.Diagnostics;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using Xunit;
using CoreDiagnostic = ETL_SQL.Core.Common.Diagnostic;

namespace ETL_SQL.Tests.Analysis
{
    public class AnalysisDiagnosticBuilderTests
    {
        [Fact]
        public void ParserDiagnostics_AreMappedToNeutralDiagnostics()
        {
            var diagnostics = AnalysisDiagnosticBuilder.FromParserDiagnostics(
                new[]
                {
                    new CoreDiagnostic("bad syntax", 2, 3, DiagnosticSeverity.Error, "SYNTAX")
                    {
                        Source = "SYNTAX"
                    }
                },
                new[] { "SELECT", "BAD TOKEN" });

            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(1, diagnostic.StartLine);
            Assert.Equal(2, diagnostic.StartColumn);
            Assert.Equal(7, diagnostic.EndColumn);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("SYNTAX", diagnostic.Code);
            Assert.Equal("ETL-SQL SYNTAX", diagnostic.Source);
        }

        [Fact]
        public void LintResults_AreMappedToNeutralDiagnostics()
        {
            var diagnostics = AnalysisDiagnosticBuilder.FromLintResults(
                new[]
                {
                    new LintResult
                    {
                        LineNumber = 1,
                        ColumnNumber = 1,
                        Severity = LintSeverity.Warning,
                        Message = "avoid star",
                        RuleName = "AvoidSelectStar"
                    }
                },
                new[] { "SELECT * FROM #t" });

            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(0, diagnostic.StartLine);
            Assert.Equal(0, diagnostic.StartColumn);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("AvoidSelectStar", diagnostic.Code);
            Assert.Equal("ETL-SQL Linter", diagnostic.Source);
        }

        [Fact]
        public void LintResults_PreserveGovernancePolicyDecisions()
        {
            var policy = GovernancePolicyRegistry.CreateDefault().GetRequired("Engine:AllowPlaintextSecrets");
            var decision = GovernancePolicyDecision.Violation(
                policy,
                "connector option PASSWORD",
                "Plaintext connector secrets are forbidden.");

            var diagnostics = AnalysisDiagnosticBuilder.FromLintResults(
                new[]
                {
                    new LintResult
                    {
                        LineNumber = 1,
                        ColumnNumber = 1,
                        Severity = LintSeverity.Warning,
                        Message = "plaintext password",
                        RuleName = "ConnectionEncryption",
                        PolicyDecision = decision
                    }
                },
                new[] { "CREATE CONNECTION c AS MSSQL(PASSWORD='secret');" });

            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(decision, diagnostic.PolicyDecision);
            Assert.Equal("Engine:AllowPlaintextSecrets", diagnostic.PolicyDecision?.PolicyKey);
        }
    }
}
