using System.Security.Cryptography;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Portal.Tests;

[Trait("Category", "Portal")]
public sealed class ReportPublishingPolicyTests : IDisposable
{
    private readonly RsaPolicyEnvelopeSigner signer = new(RSA.Create(2048));
    private readonly InMemoryPolicyAuthorityStore store = new();

    [Fact]
    public async Task ActiveSignedPolicyRejectsMissingDatasetClassificationAndAcceptsTaggedLineage()
    {
        var authority = new PolicyAuthorityService(store, signer);
        await authority.PublishAsync(new OrganizationPolicyDocument
        {
            Metadata = new MetadataGovernancePolicySection
            {
                RequiredTags = [new OrganizationRequiredTagRule { Tag = "@classification", Scopes = ["DATASET"] }]
            }
        }, "acme", "prod", "metadata-v1", "security", "data-office", DateTimeOffset.UtcNow.AddHours(1));
        var service = CreateService();

        var denied = await service.ValidateAsync(
            new Dictionary<string, string>(),
            [Lineage(new Dictionary<string, string>())]);
        var allowed = await service.ValidateAsync(
            new Dictionary<string, string>(),
            [Lineage(new Dictionary<string, string> { ["classification"] = "internal" })]);

        Assert.False(denied.Allowed);
        Assert.Contains(denied.Errors, error => error.Contains("@classification", StringComparison.Ordinal));
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public async Task InvalidActiveEnvelopeFailsClosed()
    {
        var authority = new PolicyAuthorityService(store, signer);
        var published = await authority.PublishAsync(new OrganizationPolicyDocument(),
            "acme", "prod", "metadata-v1", "security", null, DateTimeOffset.UtcNow.AddHours(1));
        var tampered = published with { SignedEnvelopeJson = published.SignedEnvelopeJson.Replace("metadata-v1", "tampered", StringComparison.Ordinal) };
        var badStore = new InMemoryPolicyAuthorityStore();
        await badStore.AppendAsync(tampered);
        var service = new ReportPublishingPolicyService(badStore, signer, Configuration());

        var result = await service.ValidateAsync(new Dictionary<string, string>(), []);

        Assert.False(result.Allowed);
        Assert.Contains(result.Errors, error => error.Contains("could not be verified", StringComparison.Ordinal));
    }

    public void Dispose() => signer.Dispose();

    private ReportPublishingPolicyService CreateService() => new(store, signer, Configuration());

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Portal:PolicyAuthority:Tenant"] = "acme",
            ["Portal:PolicyAuthority:Environment"] = "prod"
        }).Build();

    private static ReportDependencyLineageDto Lineage(IReadOnlyDictionary<string, string> tags) => new(
        "dataset:&customers", "Email", "CREATE DATASET", ["#customers"], ["Email"], tags, 4,
        null, null, null, null);
}
