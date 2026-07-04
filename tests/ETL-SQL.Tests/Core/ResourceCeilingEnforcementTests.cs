using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Proves an enrolled organization policy's resource ceilings cannot be weakened by a SET
/// statement (the enterprise value is retained), while stricter local values remain allowed,
/// and unenrolled standalone execution is unrestricted.
/// </summary>
public sealed class ResourceCeilingEnforcementTests : IDisposable
{
    public void Dispose() => EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

    [Fact]
    public async Task SetMaxParallelDegree_WeakerThanEnterpriseCeiling_IsDenied()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(maxParallelDegree: 4));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() =>
            ExecuteAsync("SET MAX_PARALLEL_DEGREE = 8;"));
        Assert.Contains("MaxParallelDegree", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetMaxParallelDegree_StricterThanEnterpriseCeiling_IsAllowed()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(maxParallelDegree: 4));

        // A stricter (smaller) value is the user's prerogative and must not be rejected.
        var ex = await Record.ExceptionAsync(() => ExecuteAsync("SET MAX_PARALLEL_DEGREE = 2;"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SetMaxParallelDegree_Standalone_IsUnrestricted()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        var ex = await Record.ExceptionAsync(() => ExecuteAsync("SET MAX_PARALLEL_DEGREE = 8;"));
        Assert.Null(ex);
    }

    private static async Task ExecuteAsync(string sql)
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        await evaluator.Evaluate(script);
    }

    private static EffectiveEnterprisePolicy EnrolledPolicy(int? maxParallelDegree = null)
    {
        var document = new OrganizationPolicyDocument
        {
            Execution = new ExecutionPolicySection { MaxParallelDegree = maxParallelDegree }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }
}
