using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    public class GovernancePolicyRuleTests
    {
        [Fact]
        public async Task SetAllowPlaintextSecretsOn_ProducesPolicyDiagnostic()
        {
            var script = Parse("SET ALLOW_PLAINTEXT_SECRETS = ON;");
            var rule = new GovernancePolicyRule();

            var result = Assert.Single(await rule.AnalyzeAsync(script, new DefaultLintContext()));

            Assert.Equal("GOV-FORBIDDEN-POLICY", result.Code);
            Assert.Equal(LintSeverity.Error, result.Severity);
            Assert.NotNull(result.PolicyDecision);
            Assert.Equal("Engine:AllowPlaintextSecrets", result.PolicyDecision.PolicyKey);
            Assert.Equal(GovernancePolicyClassification.Forbidden, result.PolicyDecision.Classification);
            Assert.True(result.PolicyDecision.IsViolation);
        }

        [Fact]
        public async Task SetAllowPlaintextSecretsOff_DoesNotProduceDiagnostic()
        {
            var script = Parse("SET ALLOW_PLAINTEXT_SECRETS = OFF;");
            var rule = new GovernancePolicyRule();

            var results = await rule.AnalyzeAsync(script, new DefaultLintContext());

            Assert.Empty(results);
        }

        [Fact]
        public async Task NestedStatements_AreAnalyzedFromAst()
        {
            var script = Parse("IF 1 = 1 BEGIN SET ALLOW_PLAINTEXT_SECRETS = ON; END");
            var rule = new GovernancePolicyRule();

            var result = (await rule.AnalyzeAsync(script, new DefaultLintContext())).Single();

            Assert.Equal("Engine:AllowPlaintextSecrets", result.PolicyDecision?.PolicyKey);
        }

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
