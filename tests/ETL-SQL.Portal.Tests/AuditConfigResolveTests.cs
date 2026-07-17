using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Portal.Tests;

/// <summary>
/// Effective fail-closed audit policy: an explicit value always wins; when unset, it is on only for
/// an enrolled deployment that has a collector configured. See docs/architecture/decisions/RowLevelSecurity.md sibling
/// governance docs and the docs/guides/administration.md §4 audit section.
/// </summary>
public sealed class AuditConfigResolveTests
{
    [Theory]
    // explicitValue, enrolled, hasCollector, expected
    [InlineData(true, false, false, true)]    // explicit true wins even standalone/no collector
    [InlineData(false, true, true, false)]    // explicit false wins even enrolled + collector
    [InlineData(null, true, true, true)]      // unset + enrolled + collector → on
    [InlineData(null, true, false, false)]    // unset + enrolled + no collector → off (nothing to deliver)
    [InlineData(null, false, true, false)]    // unset + standalone → off (local-only)
    [InlineData(null, false, false, false)]   // unset + standalone + no collector → off
    public void ResolveRequireRemoteDelivery(bool? explicitValue, bool enrolled, bool hasCollector, bool expected)
    {
        var audit = new AuditConfig
        {
            RequireRemoteDelivery = explicitValue,
            TransportEndpoint = hasCollector ? "https://collector.example.com/audit" : null
        };

        Assert.Equal(expected, audit.ResolveRequireRemoteDelivery(enrolled));
    }
}
