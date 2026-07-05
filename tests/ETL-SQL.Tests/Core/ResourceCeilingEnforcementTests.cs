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

    [Fact]
    public async Task SetMaxSmtpEmails_WeakerThanEnterpriseCeiling_IsDenied()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(maxSmtpEmails: 1));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() =>
            ExecuteAsync("SET MAX_SMTP_EMAILS_PER_SCRIPT = 1000;"));
        Assert.Contains("MaxSmtpEmailsPerScript", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordSmtpEmailSend_BeyondEnterpriseCeiling_IsDenied()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(maxSmtpEmails: 1));
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

        evaluator.RecordSmtpEmailSend(); // first send within the ceiling
        var denied = Assert.ThrowsAny<Exception>(() => evaluator.RecordSmtpEmailSend());
        Assert.Contains("MaxSmtpEmailsPerScript", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UseDocker_ImageOutsideEnterpriseAllowlist_IsDeniedBeforeStart()
    {
        // Denial fires before the Docker manager is touched, so no Docker daemon is needed.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedDockerImages: ["postgres"]));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() =>
            ExecuteAsync("USE DOCKER('redis:7');"));
        Assert.Contains("Docker image", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutionMode_OutsideEnterpriseAllowlist_IsDenied()
    {
        // Only Scheduled is permitted; a normal (Batch/Interactive) run is denied at execution start.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(allowedExecutionModes: [ScriptExecutionMode.Scheduled]));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() => ExecuteAsync("PRINT 'hello';"));
        Assert.Contains("execution mode", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecutionMode_WithinEnterpriseAllowlist_IsAllowed()
    {
        // The default allowlist includes Batch and Interactive, so a normal run proceeds.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(
            allowedExecutionModes: [ScriptExecutionMode.Interactive, ScriptExecutionMode.Batch, ScriptExecutionMode.Scheduled]));

        var ex = await Record.ExceptionAsync(() => ExecuteAsync("PRINT 'hello';"));
        Assert.Null(ex);
    }

    private static async Task ExecuteAsync(string sql)
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        await evaluator.Evaluate(script);
    }

    private static EffectiveEnterprisePolicy EnrolledPolicy(
        int? maxParallelDegree = null,
        int? maxSmtpEmails = null,
        string[]? allowedDockerImages = null,
        ScriptExecutionMode[]? allowedExecutionModes = null)
    {
        var execution = new ExecutionPolicySection
        {
            MaxParallelDegree = maxParallelDegree,
            MaxSmtpEmailsPerScript = maxSmtpEmails
        };
        if (allowedExecutionModes is not null)
            execution = execution with { AllowedModes = allowedExecutionModes };
        var document = new OrganizationPolicyDocument
        {
            Execution = execution,
            Process = new ProcessPolicySection { AllowedDockerImages = allowedDockerImages ?? [] }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }
}
