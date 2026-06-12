using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Validates <see cref="TagValueValidationRule"/> — value/type checks for the standard
    /// governance tag catalog (enum, boolean, and duration tags).
    /// </summary>
    public class TagValueValidationRuleTests
    {
        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, sql);
            return parser.Parse();
        }

        private static async Task<System.Collections.Generic.List<LintResult>> Lint(string sql)
        {
            var linter = new Linter();
            linter.AddRule(new TagValueValidationRule());
            return await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext());
        }

        [Fact]
        public async Task Classification_InvalidValue_IsWarning()
        {
            var results = await Lint("SELECT Id /* @classification: secret; */ FROM #t;");
            var r = Assert.Single(results);
            Assert.Equal("TagValue", r.RuleName);
            Assert.Equal(LintSeverity.Warning, r.Severity);
            Assert.Contains("classification", r.Message);
        }

        [Fact]
        public async Task Classification_ValidValue_NoWarning()
        {
            var results = await Lint("SELECT Id /* @classification: confidential; */ FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Quality_InvalidValue_IsWarning()
        {
            var results = await Lint("SELECT Id /* @quality: platinum; */ FROM #t;");
            Assert.Single(results);
        }

        [Fact]
        public async Task Quality_ValidValue_CaseInsensitive_NoWarning()
        {
            var results = await Lint("SELECT Id /* @quality: GOLD; */ FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Freshness_NonDuration_IsWarning()
        {
            var results = await Lint("SELECT Id /* @freshness: yesterday; */ FROM #t;");
            Assert.Single(results);
        }

        [Theory]
        [InlineData("1h")]
        [InlineData("24h")]
        [InlineData("7d")]
        [InlineData("30s")]
        public async Task Freshness_ValidDuration_NoWarning(string duration)
        {
            var results = await Lint($"SELECT Id /* @freshness: {duration}; */ FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task Boolean_NonBoolValue_IsWarning()
        {
            var results = await Lint("SELECT Id /* @pii: maybe; */ FROM #t;");
            Assert.Single(results);
        }

        [Fact]
        public async Task Boolean_BareTag_TreatedAsTrue_NoWarning()
        {
            // Parser stores a bare @tag as "true".
            var results = await Lint("SELECT Id /* @pii */ FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task LoadPattern_InvalidValue_IsWarning()
        {
            var results = await Lint("SELECT Id /* @load_pattern: streaming; */ FROM #t;");
            Assert.Single(results);
        }

        [Fact]
        public async Task FreeFormStringTag_NotValidated_NoWarning()
        {
            // @owner / @sla are free-form strings — never flagged.
            var results = await Lint("SELECT Id /* @owner: anyone; @sla: by 6am; */ FROM #t;");
            Assert.Empty(results);
        }

        [Fact]
        public async Task TableLevelTag_IsValidated()
        {
            var results = await Lint("SELECT Id FROM #t /* @classification: nope; */;");
            Assert.Single(results);
        }
    }
}
