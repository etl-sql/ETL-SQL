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
    public async Task SetMaxStringResultSize_WeakerThanEnterpriseCeiling_IsDenied()
    {
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(maxStringResultSize: 1024));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() =>
            ExecuteAsync("SET MAX_STRING_RESULT_SIZE = 2048;"));
        Assert.Contains("MaxStringResultSize", denied.ToString(), StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void RemoteExecutionMode_Disabled_DeniesRemoteModeRun()
    {
        // Remote mode isn't produced by the engine today, so this exercises the boundary directly.
        var policy = EnrolledPolicy(); // default RemoteExecution.Mode = Disabled
        var snapshot = ExecutionPolicySnapshot.Capture(policy, "operator", ScriptExecutionMode.Remote, "hash");

        var denied = Assert.Throws<FileSystemPolicyDeniedException>(() =>
            OperationPolicyBoundary.EnforceRemoteExecutionMode(snapshot));
        Assert.Contains("remote execution", denied.Decision.Reason, StringComparison.OrdinalIgnoreCase);

        // A non-remote (Batch) run under the same policy is unaffected.
        var batch = ExecutionPolicySnapshot.Capture(policy, "operator", ScriptExecutionMode.Batch, "hash");
        Assert.Null(Record.Exception(() => OperationPolicyBoundary.EnforceRemoteExecutionMode(batch)));
    }

    [Fact]
    public async Task PreSetThresholds_WithoutRuntimeCeiling_AreClampedAtExecutionBegin()
    {
        // MaxParallelDegree (consumed directly by the parallel handler) and MaxStringResultSize have
        // no runtime enterprise ceiling, so a value that arrived via config / env var / CLI option /
        // restored session — sources that never pass through the SET ceiling — is clamped down to the
        // locked value at execution begin.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(maxParallelDegree: 4, maxStringResultSize: 2048));
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        evaluator.MaxParallelDegree = 999;
        evaluator.MaxStringResultSize = 1 << 20;

        await evaluator.Evaluate(new Parser(new Lexer("PRINT 'go';").Tokenize(), "PRINT 'go';").Parse());

        Assert.Equal(4, evaluator.MaxParallelDegree);
        Assert.Equal(2048, evaluator.MaxStringResultSize);
    }

    [Fact]
    public async Task PreSetThresholds_StricterThanEnterpriseCeiling_ArePreserved()
    {
        // A stricter (smaller) local value is the operator's prerogative and must survive the clamp.
        EnterprisePolicyRuntime.SetCurrent(EnrolledPolicy(maxParallelDegree: 8));
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        evaluator.MaxParallelDegree = 2;

        await evaluator.Evaluate(new Parser(new Lexer("PRINT 'go';").Tokenize(), "PRINT 'go';").Parse());

        Assert.Equal(2, evaluator.MaxParallelDegree);
    }

    [Fact]
    public async Task PreSetThresholds_Standalone_AreNotClamped()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        evaluator.MaxParallelDegree = 999;

        await evaluator.Evaluate(new Parser(new Lexer("PRINT 'go';").Tokenize(), "PRINT 'go';").Parse());

        Assert.Equal(999, evaluator.MaxParallelDegree);
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
        long? maxStringResultSize = null,
        int? maxFileOperations = null,
        int? maxRecursiveDepth = null,
        string[]? allowedDockerImages = null,
        ScriptExecutionMode[]? allowedExecutionModes = null)
    {
        var execution = new ExecutionPolicySection
        {
            MaxParallelDegree = maxParallelDegree,
            MaxSmtpEmailsPerScript = maxSmtpEmails,
            MaxStringResultSize = maxStringResultSize,
            MaxFileOperationsPerScript = maxFileOperations,
            MaxRecursiveNestingDepth = maxRecursiveDepth
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
