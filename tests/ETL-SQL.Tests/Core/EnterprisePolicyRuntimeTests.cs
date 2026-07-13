using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Governance;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Tests.Core;

public sealed class EnterprisePolicyRuntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LiveSignedPolicy_IsVerifiedCachedAndFlattened()
    {
        using var key = RSA.Create(2048);
        var enrollment = Enrollment(key);
        var envelope = Envelope(key, Policy(maxParallel: 4), "v42");
        var cache = new MemoryCache();
        var loader = new EnterprisePolicyLoader(new StubSource(envelope), cache, () => Now);

        var effective = await loader.LoadAsync(enrollment);

        Assert.True(effective.IsAvailable);
        Assert.Equal("Live", effective.Status);
        Assert.Equal("v42", effective.PolicyVersion);
        Assert.Equal("4", effective.ConfigurationValues["Security:MaxParallelDegree"]);
        Assert.Equal("MSSQL", effective.ConfigurationValues["Connectors:AllowedTypes:0"]);
        Assert.NotNull(cache.Entry);
    }

    [Fact]
    public void SignatureVerification_RejectsTamperingTenantMismatchAndRotatedKey()
    {
        using var key = RSA.Create(2048);
        using var rotatedKey = RSA.Create(2048);
        var enrollment = Enrollment(key);
        var envelope = Envelope(key, Policy(), "v1");

        Assert.Throws<InvalidOperationException>(() => EnterprisePolicySignature.VerifyAndParse(
            envelope with { PolicyPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")) },
            enrollment, Now));
        Assert.Throws<InvalidOperationException>(() => EnterprisePolicySignature.VerifyAndParse(
            envelope with { Tenant = "another-tenant" }, enrollment, Now));
        Assert.Throws<InvalidOperationException>(() => EnterprisePolicySignature.VerifyAndParse(
            Envelope(rotatedKey, Policy(), "rotated-key"), enrollment, Now));
    }

    [Fact]
    public async Task LiveFailure_UsesVerifiedFreshCache()
    {
        var eventSink = new RecordingSecurityEventSink();
        using var eventScope = SecurityEventRuntime.UseSinkForScope(eventSink);
        using var key = RSA.Create(2048);
        var enrollment = Enrollment(key);
        var cachedEnvelope = Envelope(key, Policy(maxParallel: 3), "cached-v1");
        var cache = new MemoryCache
        {
            Entry = new EnterprisePolicyCacheEntry(cachedEnvelope, Now.AddHours(-1))
        };
        var loader = new EnterprisePolicyLoader(new FailingSource(), cache, () => Now);

        var effective = await loader.LoadAsync(enrollment);

        Assert.Equal("Cached", effective.Status);
        Assert.Equal("cached-v1", effective.PolicyVersion);
        Assert.Equal("3", effective.ConfigurationValues["Security:MaxParallelDegree"]);
        Assert.Contains("Live retrieval failed", effective.Error);
        var securityEvent = Assert.Single(eventSink.Events);
        Assert.Equal(SecurityEventType.PolicyAvailabilityFailure, securityEvent.Type);
        Assert.Equal(SecurityEventSeverity.Warning, securityEvent.Severity);
        Assert.Equal(SecurityEventDecision.Warning, securityEvent.Decision);
        Assert.Equal("corp-production", securityEvent.TenantId);
        Assert.Equal("cached-v1", securityEvent.PolicyVersion);
    }

    [Fact]
    public async Task ExpiredOfflineCache_FailsClosed()
    {
        var eventSink = new RecordingSecurityEventSink();
        using var eventScope = SecurityEventRuntime.UseSinkForScope(eventSink);
        using var key = RSA.Create(2048);
        var enrollment = Enrollment(key) with { MaxOfflineHours = 2 };
        var cache = new MemoryCache
        {
            Entry = new EnterprisePolicyCacheEntry(Envelope(key, Policy(), "v1"), Now.AddHours(-3))
        };
        var loader = new EnterprisePolicyLoader(new FailingSource(), cache, () => Now);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync(enrollment));

        Assert.Contains("offline cache expired", error.ToString(), StringComparison.OrdinalIgnoreCase);
        var securityEvent = Assert.Single(eventSink.Events);
        Assert.Equal(SecurityEventType.PolicyValidationFailure, securityEvent.Type);
        Assert.Equal(SecurityEventSeverity.Error, securityEvent.Severity);
        Assert.Equal(SecurityEventDecision.Failed, securityEvent.Decision);
    }

    [Fact]
    public async Task UnavailablePolicy_CanContinueOnlyWhenEnrollmentExplicitlyAllowsIt()
    {
        using var key = RSA.Create(2048);
        var enrollment = Enrollment(key) with { FailClosed = false };
        var loader = new EnterprisePolicyLoader(new FailingSource(), new MemoryCache(), () => Now);

        var effective = await loader.LoadAsync(enrollment);

        Assert.True(effective.IsEnrolled);
        Assert.False(effective.IsAvailable);
        Assert.Equal("Unavailable", effective.Status);
        Assert.Empty(effective.ConfigurationValues);
    }

    [Fact]
    public async Task OlderLivePolicy_IsRejectedInFavorOfVerifiedCache()
    {
        using var key = RSA.Create(2048);
        var enrollment = Enrollment(key);
        var cached = Envelope(key, Policy(maxParallel: 2), "v2", issuedAt: Now.AddHours(-1));
        var older = Envelope(key, Policy(maxParallel: 9), "v1", issuedAt: Now.AddHours(-2));
        var cache = new MemoryCache { Entry = new EnterprisePolicyCacheEntry(cached, Now) };
        var loader = new EnterprisePolicyLoader(new StubSource(older), cache, () => Now);

        var effective = await loader.LoadAsync(enrollment);

        Assert.Equal("Cached", effective.Status);
        Assert.Equal("v2", effective.PolicyVersion);
        Assert.Equal("2", effective.ConfigurationValues["Security:MaxParallelDegree"]);
    }

    [Fact]
    public void ExpiredEnvelope_IsRejectedEvenWhenSignatureIsValid()
    {
        using var key = RSA.Create(2048);
        var enrollment = Enrollment(key);
        var envelope = Envelope(key, Policy(), "expired", issuedAt: Now.AddHours(-2),
            expiresAt: Now.AddMinutes(-1));

        Assert.Throws<InvalidOperationException>(() =>
            EnterprisePolicySignature.VerifyAndParse(envelope, enrollment, Now));
    }

    [Fact]
    public void SecurityEventCollector_RequiresMachineCertificateIdentity()
    {
        using var key = RSA.Create(2048);
        var enrollment = Enrollment(key);
        var settings = new SecurityEventPolicySection
        {
            CollectorEndpoint = "https://siem.example.test/events",
            MinimumForwardedSeverity = SecurityEventSeverity.Critical
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            EnterprisePolicyRuntime.CreateSecurityEventTransportOptions(enrollment, settings));
        Assert.Contains("client certificate", error.Message, StringComparison.OrdinalIgnoreCase);

        var options = EnterprisePolicyRuntime.CreateSecurityEventTransportOptions(
            enrollment with { ClientCertificateThumbprint = new string('A', 40) }, settings);
        Assert.Equal(enrollment.Tenant, options.TenantId);
        Assert.Equal(enrollment.EnrollmentId, options.EnrollmentId);
        Assert.Equal(enrollment.MachineId, options.MachineId);
        Assert.Equal(SecurityEventSeverity.Critical, options.MinimumSeverity);
    }

    [Fact]
    public void EnterpriseOverlay_HasFinalConfigurationPrecedence()
    {
        var effective = new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            Now, Now.AddDays(1), Now, Policy(maxParallel: 4),
            new Dictionary<string, string?> { ["Security:MaxParallelDegree"] = "4" });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:MaxParallelDegree"] = "999"
            })
            .AddEnterprisePolicy(effective)
            .Build();

        Assert.Equal(4, configuration.GetValue<int>("Security:MaxParallelDegree"));
    }

    [Fact]
    public void EnterpriseOverlay_ReloadsWhenVerifiedRuntimePolicyChanges()
    {
        var initial = Effective(maxParallel: 4, version: "v1");
        var configuration = new ConfigurationBuilder().AddEnterprisePolicy(initial).Build();
        try
        {
            EnterprisePolicyRuntime.SetCurrent(Effective(maxParallel: 2, version: "v2"));

            Assert.Equal(2, configuration.GetValue<int>("Security:MaxParallelDegree"));
        }
        finally
        {
            EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);
        }
    }

    [Fact]
    public void ExecutionSnapshot_FreezesPolicyValuesAndCorrelationContext()
    {
        var values = new Dictionary<string, string?>
        {
            ["Security:MaxParallelDegree"] = "4"
        };
        var effective = new EffectiveEnterprisePolicy(true, true, "Live", "v7", "test",
            Now.AddMinutes(-5), Now.AddHours(1), Now, Policy(maxParallel: 4), values);

        var snapshot = ExecutionPolicySnapshot.Capture(effective, "svc-orchestrator",
            ScriptExecutionMode.Scheduled, "script-hash", "job-42", "correlation-42", Now);
        values["Security:MaxParallelDegree"] = "999";

        Assert.True(snapshot.IsEnrolled);
        Assert.Equal("v7", snapshot.PolicyVersion);
        Assert.NotNull(snapshot.PolicyHash);
        Assert.Equal("4", snapshot.GovernedValues["Security:MaxParallelDegree"]);
        Assert.Equal("job-42", snapshot.JobId);
        Assert.Equal("correlation-42", snapshot.CorrelationId);
        Assert.Equal(ScriptExecutionMode.Scheduled, snapshot.ExecutionMode);
    }

    [Fact]
    public void ExecutionSnapshot_FreshnessFailsExpiredPolicyAndFlagsOrdinaryRefresh()
    {
        var initial = Effective(maxParallel: 4, version: "v1");
        var snapshot = ExecutionPolicySnapshot.Capture(initial, "operator",
            ScriptExecutionMode.Batch, "script-hash", capturedAtUtc: Now);

        var refreshed = Effective(maxParallel: 2, version: "v2");
        var refreshResult = snapshot.GetFreshness(refreshed, Now);
        var expiryResult = snapshot.GetFreshness(initial, Now.AddDays(2));

        Assert.True(refreshResult.CanContinue);
        Assert.True(refreshResult.CurrentPolicyChanged);
        Assert.False(expiryResult.CanContinue);
        Assert.Contains("expired", expiryResult.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OperationDecision_IncludesPolicyAndExecutionCorrelation()
    {
        var snapshot = ExecutionPolicySnapshot.Capture(Effective(4, "v1"), "operator",
            ScriptExecutionMode.Batch, "script-hash", "job-1", "corr-1", Now);

        var decision = OperationPolicyDecision.Deny(snapshot, "Filesystem:ApprovedRoots",
            "<outside-approved-root>", "approved roots only", "Target is outside approved roots.");

        Assert.False(decision.IsAllowed);
        Assert.Equal("corr-1", decision.CorrelationId);
        Assert.Equal("job-1", decision.JobId);
        Assert.Equal("v1", decision.PolicyVersion);
        Assert.Equal(snapshot.PolicyHash, decision.PolicyHash);
    }

    [Fact]
    public async Task ProtectedFileCache_RoundTripsAndInvokesProtection()
    {
        var root = Path.Combine(Path.GetTempPath(), "enterprise_policy_cache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var key = RSA.Create(2048);
            var entry = new EnterprisePolicyCacheEntry(Envelope(key, Policy(), "v1"), Now);
            var protection = new RecordingProtection();
            var store = new FileEnterprisePolicyCacheStore(Path.Combine(root, "cache.json"),
                new AllowProtectionValidator(), protection);

            await store.WriteAsync(entry, "service-account");
            var loaded = await store.ReadAsync();

            Assert.Equal("v1", loaded!.Envelope.PolicyVersion);
            Assert.True(protection.FileProtected);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task HttpsSource_SendsMachineIdentityHeaders_AndReadsWebJson()
    {
        using var key = RSA.Create(2048);
        var enrollment = Enrollment(key);
        var envelope = Envelope(key, Policy(), "v1");
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(envelope,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var source = new HttpsSignedEnterprisePolicySource(http,
            new Uri("https://policy.example.test/etl-sql"));

        var loaded = await source.LoadAsync(enrollment);

        Assert.Equal("v1", loaded.PolicyVersion);
        Assert.Equal(enrollment.Tenant, handler.Headers["X-ETL-SQL-Tenant"]);
        Assert.Equal(enrollment.EnrollmentId, handler.Headers["X-ETL-SQL-Enrollment"]);
        Assert.Equal(enrollment.MachineId, handler.Headers["X-ETL-SQL-Machine"]);
    }

    private static EnterpriseEnrollmentDocument Enrollment(RSA key) => new()
    {
        Tenant = "corp-production",
        PolicyEndpoint = "https://policy.example.test/etl-sql",
        PolicySigningPublicKey = key.ExportRSAPublicKeyPem(),
        MaxOfflineHours = 24,
        FailClosed = true
    };

    private static EffectiveEnterprisePolicy Effective(int maxParallel, string version) => new(
        true, true, "Live", version, "test", Now, Now.AddDays(1), Now,
        Policy(maxParallel),
        new Dictionary<string, string?> { ["Security:MaxParallelDegree"] = maxParallel.ToString() });

    private static OrganizationPolicyDocument Policy(int maxParallel = 4) => new()
    {
        Connectors = new ConnectorPolicySection { AllowedTypes = ["MSSQL", "POSTGRES"] },
        Execution = new ExecutionPolicySection { MaxParallelDegree = maxParallel }
    };

    private static SignedOrganizationPolicyEnvelope Envelope(RSA key, OrganizationPolicyDocument policy,
        string version, DateTimeOffset? issuedAt = null, DateTimeOffset? expiresAt = null)
    {
        var unsigned = new SignedOrganizationPolicyEnvelope
        {
            Tenant = "corp-production",
            PolicyVersion = version,
            IssuedAtUtc = issuedAt ?? Now.AddMinutes(-5),
            ExpiresAtUtc = expiresAt ?? Now.AddDays(1),
            PolicyPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(policy))),
            Signature = "pending"
        };
        return unsigned with { Signature = EnterprisePolicySignature.Sign(unsigned, key) };
    }

    private sealed class StubSource(SignedOrganizationPolicyEnvelope envelope) : ISignedEnterprisePolicySource
    {
        public string Source => "https://policy.example.test/etl-sql";
        public Task<SignedOrganizationPolicyEnvelope> LoadAsync(EnterpriseEnrollmentDocument enrollment,
            CancellationToken cancellationToken = default) => Task.FromResult(envelope);
    }

    private sealed class FailingSource : ISignedEnterprisePolicySource
    {
        public string Source => "https://policy.example.test/etl-sql";
        public Task<SignedOrganizationPolicyEnvelope> LoadAsync(EnterpriseEnrollmentDocument enrollment,
            CancellationToken cancellationToken = default) =>
            Task.FromException<SignedOrganizationPolicyEnvelope>(new HttpRequestException("offline"));
    }

    private sealed class MemoryCache : IEnterprisePolicyCacheStore
    {
        public EnterprisePolicyCacheEntry? Entry { get; set; }
        public Task<EnterprisePolicyCacheEntry?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Entry);
        public Task WriteAsync(EnterprisePolicyCacheEntry entry, string? serviceIdentity,
            CancellationToken cancellationToken = default)
        {
            Entry = entry;
            return Task.CompletedTask;
        }
    }

    private sealed class AllowProtectionValidator : IEnterpriseEnrollmentProtectionValidator
    {
        public void Validate(string enrollmentPath) { }
    }

    private sealed class RecordingProtection : IEnterpriseEnrollmentProtector
    {
        public bool FileProtected { get; private set; }
        public void ProtectDirectory(string directory, string? serviceIdentity) { }
        public void ProtectCacheDirectory(string directory, string? serviceIdentity) { }
        public void ProtectFile(string file, string? serviceIdentity) => FileProtected = true;
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Join(",", header.Value);
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingSecurityEventSink : ISecurityEventSink
    {
        public List<SecurityEvent> Events { get; } = [];
        public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
    }
}
