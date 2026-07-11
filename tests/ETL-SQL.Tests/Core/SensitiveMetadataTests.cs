using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Tests.Connectors;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Design §6: organization-designated sensitive connection metadata
/// (Governance:Secrets:SensitiveConnectionFields) becomes SECRET:-resolvable and masked, without
/// forcing those fields to be stored as secrets. The designation set is process-wide, so every
/// test restores it in finally.
/// </summary>
public class SensitiveMetadataTests
{
    [Fact]
    public void OrganizationFields_ExtendResolutionAndMasking_ButNotTheCredentialSet()
    {
        try
        {
            SecretResolvableFields.ConfigureOrganizationFields(["HOST", " path "]);

            Assert.True(SecretResolvableFields.IsResolvable("HOST"));
            Assert.True(SecretResolvableFields.IsResolvable("host"));
            Assert.True(SecretResolvableFields.IsResolvable("PATH"));
            Assert.True(SecretResolvableFields.IsOrganizationDesignated("HOST"));
            Assert.False(SecretResolvableFields.IsCredential("HOST"));
            Assert.False(SecretResolvableFields.IsResolvable("DATABASE"));

            Assert.True(SecretRedactor.IsSensitiveKey("HOST"));
            Assert.False(SecretRedactor.IsSensitiveKey("DATABASE"));
        }
        finally
        {
            SecretResolvableFields.ConfigureOrganizationFields(null);
        }

        Assert.False(SecretResolvableFields.IsResolvable("HOST"));
        Assert.False(SecretRedactor.IsSensitiveKey("HOST"));
    }

    [Fact]
    public void CatalogValidation_AllowsRawValuesOnDesignatedMetadata_RejectsRawCredentials()
    {
        try
        {
            SecretResolvableFields.ConfigureOrganizationFields(["HOST"]);

            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOST"] = "pg01.internal",
                ["DATABASE"] = "dw"
            };
            Assert.Null(SharedConnectionValidator.FindRawCredential(options, target: null));

            options["PASSWORD"] = "hunter2";
            Assert.Equal("PASSWORD", SharedConnectionValidator.FindRawCredential(options, target: null));
        }
        finally
        {
            SecretResolvableFields.ConfigureOrganizationFields(null);
        }
    }

    [Fact]
    public async Task CreateConnection_ResolvesSecretReferenceOnDesignatedField()
    {
        try
        {
            SecretResolvableFields.ConfigureOrganizationFields(["HOST"]);

            var connector = new CapturingConnector();
            var handler = new CreateConnectionStatementHandler(
                ConnectionTestDoubles.Registry(connector).Object,
                new Mock<ILogger>().Object,
                secretProvider: new DictionarySecretProvider(("prod_host", "pg01.internal")));
            var statement = new CreateConnectionStatement(
                "dw",
                "CAPTURE",
                options: new Dictionary<string, Expression>
                {
                    ["HOST"] = new LiteralExpression("SECRET:prod_host", TokenType.STRING_LITERAL)
                });

            await handler.Execute(statement, ConnectionTestDoubles.Context());

            Assert.Equal("pg01.internal", connector.LastOptions?["HOST"]);
        }
        finally
        {
            SecretResolvableFields.ConfigureOrganizationFields(null);
        }
    }

    [Fact]
    public async Task LintRule_AcceptsSecretReferenceOnDesignatedField_FlagsItWhenUndesignated()
    {
        var script = Parse("CREATE CONNECTION dw AS POSTGRES(HOST='SECRET:prod_host', PASSWORD='SECRET:pw');");
        var linter = new Linter();
        linter.AddRule(new SecretReferenceUsageRule());

        try
        {
            SecretResolvableFields.ConfigureOrganizationFields(["HOST"]);
            Assert.Empty(await linter.AnalyzeAsync(script, new DefaultLintContext()));
        }
        finally
        {
            SecretResolvableFields.ConfigureOrganizationFields(null);
        }

        var undesignated = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();
        var result = Assert.Single(undesignated);
        Assert.Contains("HOST", result.Message);
        Assert.Contains("SensitiveConnectionFields", result.Message);
    }

    private static Script Parse(string sql)
    {
        var lexer = new Lexer(sql);
        var parser = new Parser(lexer.Tokenize());
        return parser.Parse();
    }
}
