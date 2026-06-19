using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class GovernancePolicyRegistryTests
{
    [Fact]
    public void Register_RejectsDuplicateKeys_CaseInsensitive()
    {
        var registry = new GovernancePolicyRegistry();
        registry.Register(new GovernancePolicyDefinition(
            "Security:AllowedHosts",
            GovernancePolicyScope.Network,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.HostPatternList,
            "Allowed hosts."));

        Assert.Throws<InvalidOperationException>(() => registry.Register(new GovernancePolicyDefinition(
            "security:allowedhosts",
            GovernancePolicyScope.Network,
            GovernancePolicyClassification.Allowed,
            GovernancePolicyValueKind.HostPatternList,
            "Duplicate allowed hosts.")));
    }

    [Fact]
    public void TryGet_NormalizesEnvironmentStyleKeys()
    {
        var registry = GovernancePolicyRegistry.CreateDefault();

        Assert.True(registry.TryGet("Security__AllowedHosts", out var definition));
        Assert.Equal("Security:AllowedHosts", definition.Key);
        Assert.Equal(GovernancePolicyClassification.Allowed, definition.Classification);
        Assert.Equal(GovernancePolicyValueKind.HostPatternList, definition.ValueKind);
    }

    [Fact]
    public void DefaultRegistry_CoversAllPolicyClassifications()
    {
        var registry = GovernancePolicyRegistry.CreateDefault();
        var classifications = registry.Definitions
            .Select(definition => definition.Classification)
            .Distinct()
            .ToHashSet();

        Assert.Contains(GovernancePolicyClassification.Forbidden, classifications);
        Assert.Contains(GovernancePolicyClassification.Allowed, classifications);
        Assert.Contains(GovernancePolicyClassification.Constrained, classifications);
        Assert.Contains(GovernancePolicyClassification.Locked, classifications);
    }

    [Fact]
    public void DefaultRegistry_PinsCoreGovernanceSurfaces()
    {
        var registry = GovernancePolicyRegistry.CreateDefault();

        Assert.Equal(
            GovernancePolicyClassification.Forbidden,
            registry.GetRequired("Engine:AllowPlaintextSecrets").Classification);
        Assert.Equal(
            GovernancePolicyClassification.Constrained,
            registry.GetRequired("Security:PathProtectionMode").Classification);
        Assert.Equal(
            GovernancePolicyClassification.Allowed,
            registry.GetRequired("Connectors:AllowedTypes").Classification);
        Assert.Equal(
            GovernancePolicyClassification.Locked,
            registry.GetRequired("Audit:RemoteDeliveryRequired").Classification);
    }

    [Fact]
    public void AddEtlSqlEngine_RegistersDefaultGovernanceRegistry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ETL_SQL.Common.ILogger>(ETL_SQL.Common.NullLogger.Instance);
        services.AddEtlSqlEngine(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IGovernancePolicyRegistry>();

        Assert.True(registry.TryGet("Security:MaxFileOperationsPerScript", out var definition));
        Assert.Equal(GovernancePolicyValueKind.Integer, definition.ValueKind);
    }
}

