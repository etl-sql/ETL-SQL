using ETL_SQL.App.Portability;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ETL_SQL.Tests.Portability;

public sealed class TenantPortabilityTargetsTests
{
    private const string Bootstrap = """
        EXECUTE portal BEGIN
          CREATE USER 'etl' WITH (EMAIL = 'etl@example.test', PASSWORD = '${PORTAL_USER_ETL_PASSWORD}', ROLE = Viewer);
        END;
        """;

    [Fact]
    public void SecretPlaceholderBindingProducesAParseableSecretReference()
    {
        var bound = EnginePortalConfigurationTarget.BindSecretPlaceholders(Bootstrap,
            new Dictionary<string, string>
            {
                ["SECRET:PORTAL_USER_ETL_PASSWORD"] = "SECRET:target-etl-password"
            });

        Assert.Contains("PASSWORD = 'SECRET:target-etl-password'", bound, StringComparison.Ordinal);
        Assert.DoesNotContain("${", bound, StringComparison.Ordinal);
        var parsed = new ETL_SQL.Core.Parser.Parser(
            new ETL_SQL.Core.Parser.Lexer(bound).Tokenize(), bound).Parse();
        Assert.NotEmpty(parsed.Statements);
    }

    [Fact]
    public void SecretPlaceholderBindingRefusesRawValuesAndUnresolvedPlaceholders()
    {
        Assert.Throws<TenantBundleCompositionException>(() =>
            EnginePortalConfigurationTarget.BindSecretPlaceholders(Bootstrap,
                new Dictionary<string, string>
                {
                    ["SECRET:PORTAL_USER_ETL_PASSWORD"] = "raw-password"
                }));
        Assert.Throws<TenantBundleCompositionException>(() =>
            EnginePortalConfigurationTarget.BindSecretPlaceholders(Bootstrap,
                new Dictionary<string, string>()));
    }

    [Fact]
    public async Task PortalTargetPlansBeforeExecutingThroughTheEngine()
    {
        var services = DependencyInjectionSetup.BuildServiceProvider();
        var connection = new Mock<IPortalAdminConnection>();
        connection.Setup(value => value.PlanAdminStatementAsync(
                It.IsAny<Statement>(), It.IsAny<IExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Would create user etl.");
        connection.Setup(value => value.ExecuteAdminStatementAsync(
                It.IsAny<Statement>(), It.IsAny<IExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var target = new EnginePortalConfigurationTarget(services, () => connection.Object);
        var bindings = new Dictionary<string, string>
        {
            ["SECRET:PORTAL_USER_ETL_PASSWORD"] = "SECRET:target-etl-password"
        };

        var plan = await target.PlanAsync(Bootstrap, bindings, CancellationToken.None);
        await target.ApplyAsync(Bootstrap, bindings, CancellationToken.None);

        var entry = Assert.Single(plan);
        Assert.Equal("Create", entry.Action);
        Assert.Contains("Would create", entry.Name, StringComparison.Ordinal);
        connection.Verify(value => value.PlanAdminStatementAsync(
            It.IsAny<Statement>(), It.IsAny<IExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        connection.Verify(value => value.ExecuteAdminStatementAsync(
            It.IsAny<Statement>(), It.IsAny<IExecutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("PRINT 'outside';")]
    [InlineData("EXECUTE other BEGIN PRINT 'wrong target'; END;")]
    public async Task PortalTargetRefusesStatementsOutsideTheBoundPortalConnection(string script)
    {
        var target = new EnginePortalConfigurationTarget(
            DependencyInjectionSetup.BuildServiceProvider(),
            () => new Mock<IPortalAdminConnection>().Object);

        await Assert.ThrowsAsync<TenantBundleCompositionException>(() =>
            target.PlanAsync(script, new Dictionary<string, string>(), CancellationToken.None));
    }

    [Theory]
    [InlineData("WHAT IF: group 'etl' already exists — would skip.", "Match")]
    [InlineData("WHAT IF: would update alert 'etl'.", "Collision")]
    [InlineData("WHAT IF: would create group 'etl'.", "Create")]
    public async Task PortalTargetPreservesThePortalPlanVocabulary(string description, string action)
    {
        var connection = new Mock<IPortalAdminConnection>();
        connection.Setup(value => value.PlanAdminStatementAsync(
                It.IsAny<Statement>(), It.IsAny<IExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(description);
        var target = new EnginePortalConfigurationTarget(
            DependencyInjectionSetup.BuildServiceProvider(), () => connection.Object);

        var plan = await target.PlanAsync(
            Bootstrap,
            new Dictionary<string, string>
            {
                ["SECRET:PORTAL_USER_ETL_PASSWORD"] = "SECRET:target-etl-password"
            }, CancellationToken.None);

        Assert.Equal(action, Assert.Single(plan).Action);
    }
}
