using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Phase 3 completion-gate 4: a standalone (unenrolled) deployment must require no enterprise
/// endpoint, certificate, cache, or organization restriction. This proves the default runtime is
/// Standalone with no policy document, and that a battery of otherwise-governed operations runs
/// without any organization restriction when unenrolled.
/// </summary>
public sealed class StandaloneRegressionTests : IDisposable
{
    public StandaloneRegressionTests() => EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
    public void Dispose() => EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

    [Fact]
    public void Standalone_RequiresNoEnterprisePolicyDependency()
    {
        var policy = EffectiveEnterprisePolicy.Standalone;

        Assert.False(policy.IsEnrolled);         // no machine enrollment required
        Assert.False(policy.IsAvailable);        // no policy endpoint/cache required
        Assert.Null(policy.Document);            // no signed organization document
        Assert.Null(policy.PolicyVersion);
        Assert.Empty(policy.ConfigurationValues); // no governed values imposed
    }

    [Fact]
    public async Task MissingEnrollment_StartsWithoutEnterpriseNetworkClientsOrCollector()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etl-sql-standalone-{Guid.NewGuid():N}");
        var enrollmentStore = new EnterpriseEnrollmentStore(Path.Combine(root, "enrollment.json"));
        var previousOutboxPath = Environment.GetEnvironmentVariable(
            SecurityEventOutboxPaths.StandaloneOverrideEnvironmentVariable);
        var previousSink = SecurityEventRuntime.Sink;
        var enterpriseClientCreations = 0;

        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable(
            SecurityEventOutboxPaths.StandaloneOverrideEnvironmentVariable,
            Path.Combine(root, "security-events.db"));
        try
        {
            var policy = await EnterprisePolicyRuntime.InitializeFromMachineAsync(
                enrollmentStore,
                _ =>
                {
                    enterpriseClientCreations++;
                    throw new InvalidOperationException("Standalone startup attempted enterprise networking.");
                });

            var diagnostics = SecurityEventRuntime.GetDiagnostics();
            Assert.False(policy.IsEnrolled);
            Assert.Equal(0, enterpriseClientCreations);
            Assert.False(diagnostics.CollectorConfigured);
            Assert.Null(diagnostics.CollectorReachable);
        }
        finally
        {
            SecurityEventRuntime.Sink = previousSink;
            Environment.SetEnvironmentVariable(
                SecurityEventOutboxPaths.StandaloneOverrideEnvironmentVariable,
                previousOutboxPath);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Standalone_EnterpriseOverlayPreservesLocalConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:MaxParallelDegree"] = "37",
                ["LocalWorkflow:Mode"] = "unchanged"
            })
            .AddEnterprisePolicy(EffectiveEnterprisePolicy.Standalone)
            .Build();

        Assert.Equal(37, configuration.GetValue<int>("Security:MaxParallelDegree"));
        Assert.Equal("unchanged", configuration["LocalWorkflow:Mode"]);
    }

    [Fact]
    public void Standalone_SnapshotImposesNoGovernedCeilingsOrModes()
    {
        var snapshot = ExecutionPolicySnapshot.Capture(
            EffectiveEnterprisePolicy.Standalone, "operator", ScriptExecutionMode.Batch, "hash");

        Assert.False(snapshot.IsEnrolled);
        Assert.Empty(snapshot.GovernedValues);
        // The snapshot-boundary helpers all short-circuit on an unenrolled snapshot — no throw.
        OperationPolicyBoundary.EnforceCeiling(Ctx(snapshot), "Security:MaxFileOperationsPerScript", long.MaxValue, "<probe>");
        OperationPolicyBoundary.EnforceSpillCeiling(Ctx(snapshot), long.MaxValue);
        OperationPolicyBoundary.EnforceAllowedExecutionMode(snapshot);
    }

    [Theory]
    [InlineData("SET MAX_PARALLEL_DEGREE = 999;")]              // no enterprise ceiling to weaken
    [InlineData("SET MAX_SMTP_EMAILS_PER_SCRIPT = 100000;")]
    [InlineData("DELETE FROM some_persistent_table;")]         // no what-if required
    [InlineData("INSERT INTO some_persistent_table (id) VALUES (1);")] // no transaction required
    public async Task Standalone_GovernedOperationsAreNotRestricted(string sql)
    {
        var error = await Record.ExceptionAsync(() => ExecuteAsync(sql));
        var text = error?.ToString() ?? "";

        // The statement may still fail on its own merits (missing table/host), but never on an
        // organization-policy restriction.
        Assert.DoesNotContain("Enterprise policy", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approved filesystem roots", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requires mutations to run within a transaction", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requires WHAT IF", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("execution mode", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Standalone_DockerImageIsUnrestricted()
    {
        var snapshot = ExecutionPolicySnapshot.Capture(
            EffectiveEnterprisePolicy.Standalone, "operator", ScriptExecutionMode.Batch, "hash");
        var ex = Record.Exception(() => ProcessPolicyRules.EnforceDockerImage(Ctx(snapshot), "any/image:latest"));
        Assert.Null(ex);
    }

    private static IExecutionContext Ctx(ExecutionPolicySnapshot snapshot)
    {
        var context = new Moq.Mock<IExecutionContext>();
        context.SetupGet(c => c.ExecutionPolicy).Returns(snapshot);
        return context.Object;
    }

    private static async Task ExecuteAsync(string sql)
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        await evaluator.Evaluate(script);
    }
}
