using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.Analysis;

public class SecretReferenceUsageRuleTests
{
    [Fact]
    public async Task NonCredentialOption_WithSecretReference_IsError()
    {
        var sql = "CREATE CONNECTION archive AS S3(BUCKET='SECRET:bucket_name', ACCESS_KEY='SECRET:ak', SECRET_KEY='SECRET:sk');";

        var results = await Analyze(Parse(sql));

        var result = Assert.Single(results);
        Assert.Equal(LintSeverity.Error, result.Severity);
        Assert.Contains("BUCKET", result.Message);
        Assert.Contains("archive", result.Message);
    }

    [Fact]
    public async Task CredentialOptions_WithSecretReferences_NoFindings()
    {
        var sql = "CREATE CONNECTION sales AS MSSQL(SERVER='sql01', DATABASE='Sales', USER_ID='etl', PASSWORD='SECRET:sales_db_password');";

        var results = await Analyze(Parse(sql));

        Assert.Empty(results);
    }

    [Fact]
    public async Task LiteralNonSecretValues_NoFindings()
    {
        var sql = "CREATE CONNECTION archive AS S3(BUCKET='archive-bucket', ACCESS_KEY='AKIA123');";

        var results = await Analyze(Parse(sql));

        Assert.Empty(results);
    }

    [Fact]
    public async Task ConnectionStringTarget_NonCredentialField_IsError()
    {
        var statement = new CreateConnectionStatement(
            "sales",
            "MSSQL",
            new LiteralExpression("Server=db;Bucket=SECRET:bucket_name;Password=SECRET:pw", TokenType.STRING_LITERAL));
        var script = new Script { Statements = { statement } };

        var results = await Analyze(script);

        var result = Assert.Single(results);
        Assert.Equal(LintSeverity.Error, result.Severity);
        Assert.Contains("Bucket", result.Message);
    }

    [Fact]
    public async Task ConnectionInsideIfBlock_IsAnalyzed()
    {
        var sql = "IF 1 = 1 BEGIN CREATE CONNECTION archive AS S3(BUCKET='SECRET:bucket_name'); END";

        var results = await Analyze(Parse(sql));

        var result = Assert.Single(results);
        Assert.Contains("BUCKET", result.Message);
    }

    private static async Task<List<LintResult>> Analyze(Script script)
    {
        var linter = new Linter();
        linter.AddRule(new SecretReferenceUsageRule());
        return (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();
    }

    private static Script Parse(string sql)
    {
        var lexer = new Lexer(sql);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }
}
