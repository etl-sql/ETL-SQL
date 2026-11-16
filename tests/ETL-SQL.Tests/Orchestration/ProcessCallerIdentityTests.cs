using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class ProcessCallerIdentityTests : IDisposable
{
    private const string TestSigningSecret = "0123456789abcdef0123456789abcdef_test_signing_secret";
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"etlsql-caller-id-{Guid.NewGuid():N}");

    public ProcessCallerIdentityTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static ExecutionIdentity AliceIdentity(bool isAdmin = false, string tenantId = "tenant-a") => new()
    {
        EffectiveUser = "user:alice",
        EffectiveUserId = 101,
        TenantId = tenantId,
        RealUser = "user:alice",
        IsAdmin = isAdmin,
        Roles = ["Publisher"],
        Groups = ["grp-finance"]
    };

    [Fact]
    public void CI01_CreateAndConsume_RoundTripsIdentityTenantAndScopes()
    {
        var guard = new ProcessCallerNonceReplayGuard();
        var scriptHash = ProcessCallerIdentityEnvelope.ComputeScriptHash("PRINT 'ok';"u8.ToArray());
        var identity = AliceIdentity() with { Scopes = ["orchestrator.execute", "orchestrator.publish"] };

        var envelope = ProcessCallerIdentityEnvelope.Create(
            identity, "NightlySales", scriptHash, TestSigningSecret);

        var resolved = ProcessCallerIdentityEnvelope.ValidateAndConsume(
            envelope, TestSigningSecret, scriptHash, expectedJobId: "NightlySales", replayGuard: guard);

        Assert.Equal("user:alice", resolved.EffectiveUser);
        Assert.Equal("user:alice", resolved.RealUser);
        Assert.Equal("tenant-a", resolved.TenantId);
        Assert.False(resolved.IsAdmin);
        Assert.Contains("orchestrator.execute", resolved.Scopes);
        Assert.Contains("grp-finance", resolved.Groups);
    }

    [Fact]
    public void CI02_RejectsExpiredAlteredWrongScriptWrongJobAndReplayedEnvelopes()
    {
        var scriptHash = ProcessCallerIdentityEnvelope.ComputeScriptHash("SELECT 1;"u8.ToArray());
        var otherHash = ProcessCallerIdentityEnvelope.ComputeScriptHash("SELECT 2;"u8.ToArray());
        var now = DateTimeOffset.UtcNow;
        var identity = AliceIdentity();

        // Altered signature / payload
        var envelope = ProcessCallerIdentityEnvelope.Create(identity, "JobA", scriptHash, TestSigningSecret, now: now);
        var parts = envelope.Split('.');
        var alteredSig = parts[0] + "." + (parts[1][0] == 'a' ? 'b' : 'a') + parts[1][1..];
        Assert.Throws<UnauthorizedAccessException>(() =>
            ProcessCallerIdentityEnvelope.ValidateAndConsume(
                alteredSig, TestSigningSecret, scriptHash, expectedJobId: "JobA", now: now,
                replayGuard: new ProcessCallerNonceReplayGuard()));

        // Expired envelope
        var expired = ProcessCallerIdentityEnvelope.Create(
            identity, "JobA", scriptHash, TestSigningSecret,
            lifetime: TimeSpan.FromSeconds(5), now: now.AddMinutes(-5));
        Assert.Throws<UnauthorizedAccessException>(() =>
            ProcessCallerIdentityEnvelope.ValidateAndConsume(
                expired, TestSigningSecret, scriptHash, expectedJobId: "JobA", now: now,
                replayGuard: new ProcessCallerNonceReplayGuard()));

        // Wrong script hash
        Assert.Throws<UnauthorizedAccessException>(() =>
            ProcessCallerIdentityEnvelope.ValidateAndConsume(
                envelope, TestSigningSecret, otherHash, expectedJobId: "JobA", now: now,
                replayGuard: new ProcessCallerNonceReplayGuard()));

        // Wrong job id
        Assert.Throws<UnauthorizedAccessException>(() =>
            ProcessCallerIdentityEnvelope.ValidateAndConsume(
                envelope, TestSigningSecret, scriptHash, expectedJobId: "JobB", now: now,
                replayGuard: new ProcessCallerNonceReplayGuard()));

        // Cross-audience token (HTTP assertion cannot replay as child envelope)
        var httpToken = OrchestratorIdentityAssertion.Create(
            new OrchestratorCaller("user", "alice", "Alice", ["Publisher"], [], "tenant-a", ["orchestrator.execute"]),
            TestSigningSecret, now);
        Assert.Throws<UnauthorizedAccessException>(() =>
            ProcessCallerIdentityEnvelope.ValidateAndConsume(
                httpToken, TestSigningSecret, scriptHash, expectedJobId: "JobA", now: now,
                replayGuard: new ProcessCallerNonceReplayGuard()));

        // Replay of the exact same nonce inside expiry window
        var replayGuard = new ProcessCallerNonceReplayGuard();
        ProcessCallerIdentityEnvelope.ValidateAndConsume(
            envelope, TestSigningSecret, scriptHash, expectedJobId: "JobA", now: now, replayGuard: replayGuard);
        Assert.Throws<UnauthorizedAccessException>(() =>
            ProcessCallerIdentityEnvelope.ValidateAndConsume(
                envelope, TestSigningSecret, scriptHash, expectedJobId: "JobA", now: now, replayGuard: replayGuard));
    }

    [Fact]
    public async Task CI01_CI02_LoaderReadsOnceAndDeletesEnvelopeFile()
    {
        var scriptPath = Path.Combine(_tempDir, "script.etlsql");
        var scriptBytes = "SELECT 1 AS Ok;"u8.ToArray();
        await File.WriteAllBytesAsync(scriptPath, scriptBytes);
        var scriptHash = ProcessCallerIdentityEnvelope.ComputeScriptHash(scriptBytes);

        var envelopePath = Path.Combine(_tempDir, "caller.tok");
        var signed = ProcessCallerIdentityEnvelope.Create(AliceIdentity(), "LoaderJob", scriptHash, TestSigningSecret);
        await File.WriteAllTextAsync(envelopePath, signed);

        var loaded = await ProcessCallerIdentityLoader.ReadAndConsumeAsync(
            scriptPath, envelopePath, expectedJobId: "LoaderJob", TestSigningSecret);

        Assert.NotNull(loaded);
        Assert.Equal("user:alice", loaded.EffectiveUser);
        Assert.Equal("tenant-a", loaded.TenantId);
        Assert.False(File.Exists(envelopePath));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ProcessCallerIdentityLoader.ReadAndConsumeAsync(
                scriptPath, envelopePath, expectedJobId: "LoaderJob", TestSigningSecret));
    }

    [Fact]
    public async Task CI01_CI02_CI03_ProcessSpawnRunsAsCallerAndEnforcesPerObjectAuthorization()
    {
        var dbPath = Path.Combine(_tempDir, "orch-spawn.db");
        var store = new SQLiteJobHistoryStore(dbPath);
        await store.SaveJobAsync(new JobDefinition(
            "OwnedByAlice", "PRINT 'alice';", 1, "HOUR", null, null, null,
            CreatedBy: "user:alice", TenantId: "tenant-a"));
        await store.SaveJobAsync(new JobDefinition(
            "OwnedByBob", "PRINT 'bob';", 1, "HOUR", null, null, null,
            CreatedBy: "user:bob", TenantId: "tenant-a"));
        await store.SaveJobAsync(new JobDefinition(
            "TenantBJob", "PRINT 'b';", 1, "HOUR", null, null, null,
            CreatedBy: "user:alice", TenantId: "tenant-b"));

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:IdentitySigningSecret"] = TestSigningSecret,
                ["Orchestrator:DatabasePath"] = dbPath,
                ["Orchestrator:Database:Provider"] = "Sqlite"
            })
            .Build();
        var tracker = new ChildProcessTracker(
            new Mock<ILogger<ChildProcessTracker>>().Object,
            Path.Combine(_tempDir, "child-pids.json"));
        var executor = new ProcessJobExecutor(
            Options.Create(new ProcessJobExecutorOptions
            {
                ExecutablePath = ResolveChildExecutablePath(),
                TimeoutSeconds = 60
            }),
            tracker,
            new Mock<ILogger<ProcessJobExecutor>>().Object,
            cfg);

        var identity = AliceIdentity();

        // 1. Caller runs as its own ExecutionIdentity & tenant and can manage its own tenant-a job
        var allowed = await executor.ExecuteTextAsync(
            """
            IF @@CURRENT_USER <> 'user:alice' OR CURRENT_TENANT() <> 'tenant-a'
            BEGIN
                THROW 'IDENTITY_MISMATCH';
            END;
            DISABLE JOB OwnedByAlice;
            """,
            sessionId: "ci03-allow",
            jobName: "AdHocCallerCheck",
            executionIdentity: identity);
        Assert.True(allowed.Success, allowed.ErrorMessage);
        Assert.False((await store.GetJobAsync("tenant-a", "OwnedByAlice"))!.IsEnabled);

        // 2. Caller is refused an ungranted job owned by Bob in the same tenant
        var deniedBob = await executor.ExecuteTextAsync(
            "DISABLE JOB OwnedByBob;",
            sessionId: "ci03-deny-bob",
            jobName: "DeniedBobCheck",
            executionIdentity: identity);
        Assert.False(deniedBob.Success);
        Assert.Contains("lacks MANAGE authority", deniedBob.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // 3. Caller cannot reach tenant-b objects at all (isolated by ActingTenant)
        var deniedTenantB = await executor.ExecuteTextAsync(
            "DISABLE JOB TenantBJob;",
            sessionId: "ci03-deny-tenant",
            jobName: "DeniedTenantCheck",
            executionIdentity: identity);
        Assert.False(deniedTenantB.Success);
        Assert.Contains("not found", deniedTenantB.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // 4. Child process rejects an altered envelope before running any SQL
        var childScript = Path.Combine(_tempDir, "tampered-target.etlsql");
        await File.WriteAllTextAsync(childScript, "ENABLE JOB OwnedByAlice;");
        var tamperedEnvelope = Path.Combine(_tempDir, "tampered.tok");
        await File.WriteAllTextAsync(tamperedEnvelope, "tampered.signature");
        var psi = new System.Diagnostics.ProcessStartInfo(ResolveChildExecutablePath())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(childScript);
        psi.ArgumentList.Add("--json");
        psi.EnvironmentVariables[ProcessCallerIdentityEnvelope.EnvRequired] = "1";
        psi.EnvironmentVariables[ProcessCallerIdentityEnvelope.EnvFile] = tamperedEnvelope;
        psi.EnvironmentVariables["Orchestrator__IdentitySigningSecret"] = TestSigningSecret;
        psi.EnvironmentVariables["Orchestrator__DatabasePath"] = dbPath;
        psi.EnvironmentVariables["Orchestrator__Database__Provider"] = "Sqlite";

        using var proc = System.Diagnostics.Process.Start(psi)!;
        await proc.WaitForExitAsync();
        Assert.NotEqual(0, proc.ExitCode);
        Assert.False((await store.GetJobAsync("tenant-a", "OwnedByAlice"))!.IsEnabled);
    }

    [Fact]
    public async Task CI02_WarmRunner_ExecutesAsSignedCallerAndRejectsForgedOrExpiredEnvelope()
    {
        var dbPath = Path.Combine(_tempDir, "orch-warm.db");
        var store = new SQLiteJobHistoryStore(dbPath);
        await store.SaveJobAsync(new JobDefinition(
            "WarmAliceJob", "PRINT 'alice';", 1, "HOUR", null, null, null,
            CreatedBy: "user:alice", TenantId: "tenant-a"));
        await store.SaveJobAsync(new JobDefinition(
            "WarmBobJob", "PRINT 'bob';", 1, "HOUR", null, null, null,
            CreatedBy: "user:bob", TenantId: "tenant-a"));

        var sp = DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
        {
            ["Orchestrator:IdentitySigningSecret"] = TestSigningSecret,
            ["Orchestrator:DatabasePath"] = dbPath,
            ["Orchestrator:Database:Provider"] = "Sqlite"
        });

        var scriptPath = Path.Combine(_tempDir, "warm.etlsql");
        var scriptText =
            """
            IF @@CURRENT_USER <> 'user:alice' OR CURRENT_TENANT() <> 'tenant-a'
            BEGIN
                THROW 'IDENTITY_MISMATCH';
            END;
            DISABLE JOB WarmAliceJob;
            """;
        var scriptBytes = Encoding.UTF8.GetBytes(scriptText);
        await File.WriteAllBytesAsync(scriptPath, scriptBytes);
        var scriptHash = ProcessCallerIdentityEnvelope.ComputeScriptHash(scriptBytes);

        var envPath = Path.Combine(_tempDir, "warm-valid.tok");
        await File.WriteAllTextAsync(
            envPath,
            ProcessCallerIdentityEnvelope.Create(AliceIdentity(), "WarmJob", scriptHash, TestSigningSecret));

        var ok = await WarmJobRunner.ExecuteRequestAsync(sp, new ETL_SQL.App.WarmRunnerRequest(
            "req-1", scriptPath, "warm-sess-1", "WarmJob", 0, 10000, IdentityEnvelopeFile: envPath));
        Assert.True(ok.Success, ok.ErrorMessage);
        Assert.False((await store.GetJobAsync("tenant-a", "WarmAliceJob"))!.IsEnabled);
        Assert.False(File.Exists(envPath));

        // Expired envelope in warm runner fails closed
        var expiredPath = Path.Combine(_tempDir, "warm-expired.tok");
        await File.WriteAllTextAsync(
            expiredPath,
            ProcessCallerIdentityEnvelope.Create(
                AliceIdentity(), "WarmJob", scriptHash, TestSigningSecret,
                lifetime: TimeSpan.FromSeconds(5), now: DateTimeOffset.UtcNow.AddMinutes(-5)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            WarmJobRunner.ExecuteRequestAsync(sp, new ETL_SQL.App.WarmRunnerRequest(
                "req-2", scriptPath, "warm-sess-2", "WarmJob", 0, 10000, IdentityEnvelopeFile: expiredPath)));

        // Forged signature fails closed
        var forgedPath = Path.Combine(_tempDir, "warm-forged.tok");
        await File.WriteAllTextAsync(forgedPath, "invalid.payload");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            WarmJobRunner.ExecuteRequestAsync(sp, new ETL_SQL.App.WarmRunnerRequest(
                "req-3", scriptPath, "warm-sess-3", "WarmJob", 0, 10000, IdentityEnvelopeFile: forgedPath)));

        // Per-object check denies Bob's job to Alice in WarmRunner too
        var denyScript = Path.Combine(_tempDir, "warm-deny.etlsql");
        var denyBytes = "DISABLE JOB WarmBobJob;"u8.ToArray();
        await File.WriteAllBytesAsync(denyScript, denyBytes);
        var denyEnv = Path.Combine(_tempDir, "warm-deny.tok");
        await File.WriteAllTextAsync(
            denyEnv,
            ProcessCallerIdentityEnvelope.Create(
                AliceIdentity(), "WarmDeny", ProcessCallerIdentityEnvelope.ComputeScriptHash(denyBytes), TestSigningSecret));
        var denied = await WarmJobRunner.ExecuteRequestAsync(sp, new ETL_SQL.App.WarmRunnerRequest(
            "req-4", denyScript, "warm-sess-4", "WarmDeny", 0, 10000, IdentityEnvelopeFile: denyEnv));
        Assert.False(denied.Success);
        Assert.Contains("lacks MANAGE authority", denied.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveChildExecutablePath()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[] { "ETL-SQL.exe", "ETL-SQL" })
        {
            var full = Path.Combine(baseDir, candidate);
            if (File.Exists(full))
                return full;
        }

        throw new FileNotFoundException($"Could not locate ETL-SQL executable under {baseDir}.");
    }
}
