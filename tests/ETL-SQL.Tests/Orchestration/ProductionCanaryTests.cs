using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Core.Reliability;

namespace ETL_SQL.Tests.Orchestration;

[Trait("Category", "ProductionCanary")]
public sealed class ProductionCanaryTests
{
    [Fact]
    public void HostedPlanPinsSloRegionFailureDomainAndIsolationContracts()
    {
        var plan = LoadPlan();

        plan.Validate();
        Assert.Equal(Enum.GetValues<CanaryJourneyId>(), plan.Journeys.Select(journey => journey.Id));
        Assert.All(plan.Journeys, journey =>
        {
            Assert.Equal(2, journey.Regions.Count);
            Assert.Equal(2, journey.FailureDomains.Count);
            Assert.InRange(journey.Slo.AvailabilityPercent, 99.50m, 100m);
            Assert.NotEqual(journey.EtlSqlAlertRoute, journey.DependencyAlertRoute);
        });
        Assert.StartsWith("synthetic-", plan.Isolation.TenantId);
        Assert.False(plan.Isolation.CustomerNetworkAccessAllowed);
        Assert.False(plan.Isolation.CustomerCapacityAllowed);
        Assert.True(plan.CredentialRotationInterval < plan.MaximumCredentialLifetime);
    }

    [Fact]
    public async Task SyntheticResourcesAreProvisionedWithDedicatedGuardsAndNoCustomerReach()
    {
        var plan = LoadPlan();
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        var evidence = await CanaryProvisioningCertification.ProvisionAndVerifyAsync(
            plan.Isolation, new ProductionLikeProvisioner(), now);

        Assert.True(evidence.Passed);
        Assert.Equal(0, evidence.Observed.CustomerResourceGrants);
        Assert.Equal(0, evidence.Observed.CustomerNetworkRoutes);
        Assert.True(evidence.Observed.UsesDedicatedCapacity);
        await WriteEvidenceFileAsync("production-canary-provisioning.json", evidence);
    }

    [Theory]
    [InlineData("customer-grant")]
    [InlineData("customer-route")]
    [InlineData("shared-capacity")]
    [InlineData("weak-cost-guard")]
    [InlineData("wrong-tenant")]
    public async Task ProvisioningFailsClosedForEveryIsolationDefect(string defect)
    {
        var plan = LoadPlan();
        var provisioner = new DefectiveProvisioner(defect);

        var evidence = await CanaryProvisioningCertification.ProvisionAndVerifyAsync(
            plan.Isolation, provisioner);

        Assert.False(evidence.Passed);
    }

    [Fact]
    public async Task NormalJourneysAndFaultDrillsCoverEveryRegionAndFailureDomain()
    {
        var plan = LoadPlan();
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        var credential = new CanaryCredential("credential-1", now, now + plan.MaximumCredentialLifetime);

        var alerts = new RecordingAlertSink();
        var report = await ProductionCanaryRunner.RunAsync(
            plan, credential, new ProductionLikeCanaryExecutor(), alerts, now: now);

        var expectedRuns = plan.Journeys.Sum(journey =>
            journey.Regions.Count * journey.FailureDomains.Count * 5);
        Assert.True(report.Passed);
        Assert.Equal(expectedRuns, report.Runs.Count);
        Assert.All(report.Runs, run => Assert.True(run.IsolationSatisfied));
        Assert.All(report.Runs.Where(run => run.Request.Fault == CanaryFaultKind.None), run =>
        {
            Assert.True(run.SloSatisfied);
            Assert.Equal(CanaryFailureKind.None, run.FailureKind);
            Assert.Null(run.AlertRoute);
        });
        Assert.All(report.Runs.Where(run => run.Request.Fault == CanaryFaultKind.SyntheticDependencyOutage), run =>
        {
            Assert.Equal(CanaryFailureKind.SyntheticDependency, run.FailureKind);
            Assert.Equal(run.Request.Journey.DependencyAlertRoute, run.AlertRoute);
        });
        Assert.All(report.Runs.Where(run => run.Request.Fault is
            CanaryFaultKind.EtlSqlCorrectnessFailure or CanaryFaultKind.EtlSqlAvailabilityRegression or
            CanaryFaultKind.EtlSqlLatencyRegression), run =>
        {
            Assert.StartsWith("EtlSql", run.FailureKind.ToString());
            Assert.Equal(run.Request.Journey.EtlSqlAlertRoute, run.AlertRoute);
        });
        Assert.Equal(report.Runs.Count(run => run.Request.Fault != CanaryFaultKind.None), alerts.Alerts.Count);
        Assert.All(report.Runs.Where(run => run.Request.Fault != CanaryFaultKind.None), run =>
            Assert.True(run.AlertDelivery!.Delivered));

        await WriteEvidenceAsync(report);
    }

    [Fact]
    public async Task ConfiguredExecutorRequiresAndDispatchesEveryConcreteJourney()
    {
        var plan = LoadPlan();
        var seen = new List<CanaryJourneyId>();
        Task<CanaryExecutionObservation> Handle(CanaryExecutionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen.Add(request.Journey.Id);
            return Task.FromResult(PassingObservation(plan));
        }
        var incomplete = Enum.GetValues<CanaryJourneyId>().Skip(1).ToDictionary(
            id => id,
            _ => (Func<CanaryExecutionRequest, CancellationToken, Task<CanaryExecutionObservation>>)Handle);
        Assert.Throws<ArgumentException>(() => new ConfiguredProductionCanaryExecutor("hosted", incomplete));

        var complete = Enum.GetValues<CanaryJourneyId>().ToDictionary(
            id => id,
            _ => (Func<CanaryExecutionRequest, CancellationToken, Task<CanaryExecutionObservation>>)Handle);
        var executor = new ConfiguredProductionCanaryExecutor("hosted", complete);
        foreach (var journey in plan.Journeys)
            await executor.ExecuteAsync(Request(plan) with { Journey = journey });

        Assert.Equal(Enum.GetValues<CanaryJourneyId>(), seen);
    }

    [Fact]
    public void EvidenceFailsClosedForCustomerAccessCapacityAndCost()
    {
        var plan = LoadPlan();
        var request = Request(plan);
        var observations = new[]
        {
            PassingObservation(plan) with { AccessedTenantIds = ["customer-tenant"] },
            PassingObservation(plan) with { CustomerDataAccessed = true },
            PassingObservation(plan) with { CustomerSystemsAccessed = true },
            PassingObservation(plan) with { CustomerCapacityConsumed = true },
            PassingObservation(plan) with { AttributedMonthlyCostUsd = plan.Isolation.MaximumMonthlyCostUsd + 0.01m }
        };

        Assert.All(observations, observation =>
        {
            var evidence = ProductionCanaryRunner.Evaluate(request, observation);
            Assert.False(evidence.Passed);
            Assert.False(evidence.IsolationSatisfied);
            Assert.Equal(CanaryFailureKind.IsolationViolation, evidence.FailureKind);
            Assert.Equal(request.Journey.EtlSqlAlertRoute, evidence.AlertRoute);
        });
    }

    [Fact]
    public void DependencyFailureHasUnambiguousAttributionEvenWhenTheJourneyResultIsWrong()
    {
        var plan = LoadPlan();
        var request = Request(plan) with { Fault = CanaryFaultKind.SyntheticDependencyOutage };
        var evidence = ProductionCanaryRunner.Evaluate(request,
            PassingObservation(plan) with { CorrectResult = false, SyntheticDependencyHealthy = false });

        Assert.Equal(CanaryFailureKind.SyntheticDependency, evidence.FailureKind);
        Assert.Equal(request.Journey.DependencyAlertRoute, evidence.AlertRoute);
        Assert.NotEqual(request.Journey.EtlSqlAlertRoute, evidence.AlertRoute);
    }

    [Fact]
    public void AvailabilitySloUsesWindowSamplesAndRoutesAProductAlert()
    {
        var plan = LoadPlan();
        var request = Request(plan) with { Fault = CanaryFaultKind.EtlSqlAvailabilityRegression };
        var evidence = ProductionCanaryRunner.Evaluate(request,
            PassingObservation(plan) with { SuccessfulSamples = 99, TotalSamples = 100 });

        Assert.False(evidence.SloSatisfied);
        Assert.Equal(CanaryFailureKind.EtlSqlAvailability, evidence.FailureKind);
        Assert.Equal(request.Journey.EtlSqlAlertRoute, evidence.AlertRoute);
        Assert.False(evidence.Passed);
        Assert.Null(evidence.AlertDelivery);
    }

    [Fact]
    public void DrillFailsWhenObservedFailureDoesNotMatchTheInjectedFault()
    {
        var plan = LoadPlan();
        var request = Request(plan) with { Fault = CanaryFaultKind.EtlSqlLatencyRegression };
        var evidence = ProductionCanaryRunner.Evaluate(request,
            PassingObservation(plan) with { CorrectResult = false });

        Assert.Equal(CanaryFailureKind.EtlSqlCorrectness, evidence.FailureKind);
        Assert.False(evidence.Passed);
    }

    [Fact]
    public async Task CredentialsRotateOnScheduleAndCompromiseRevokesBeforeReplacement()
    {
        var plan = LoadPlan();
        var authority = new RecordingCredentialAuthority();
        var lifecycle = new CanaryCredentialLifecycle(authority);
        var issued = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var original = new CanaryCredential("old", issued, issued + plan.MaximumCredentialLifetime);

        Assert.Null(await lifecycle.RotateIfDueAsync(plan, original,
            issued + plan.CredentialRotationInterval - TimeSpan.FromSeconds(1)));
        var scheduled = await lifecycle.RotateIfDueAsync(plan, original,
            issued + plan.CredentialRotationInterval);
        Assert.Equal("scheduled", scheduled!.Reason);
        Assert.Equal(["revoke:old", "issue:synthetic-canary-runner"], authority.Events);

        authority.Events.Clear();
        var compromised = await lifecycle.RespondToCompromiseAsync(plan, scheduled.Current,
            scheduled.CompletedUtc + TimeSpan.FromMinutes(1));
        Assert.Equal("compromise", compromised.Reason);
        Assert.Equal($"revoke:{scheduled.Current.CredentialId}", authority.Events[0]);
        Assert.StartsWith("issue:", authority.Events[1]);
        var report = new CanaryCredentialLifecycleReport(
            "etl-sql.production-canary-credential-lifecycle/v1",
            compromised.CompletedUtc,
            scheduled,
            compromised);
        Assert.True(report.Passed);
        await WriteEvidenceFileAsync("production-canary-credential-lifecycle.json", report);
    }

    [Fact]
    public async Task RevokedExpiredOrOverlongCredentialsCannotRun()
    {
        var plan = LoadPlan();
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        var invalid = new[]
        {
            new CanaryCredential("revoked", now - TimeSpan.FromHours(1), now + TimeSpan.FromHours(1), true),
            new CanaryCredential("expired", now - TimeSpan.FromHours(2), now),
            new CanaryCredential("overlong", now, now + plan.MaximumCredentialLifetime + TimeSpan.FromSeconds(1))
        };

        foreach (var credential in invalid)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProductionCanaryRunner.RunAsync(
                    plan, credential, new ProductionLikeCanaryExecutor(), new RecordingAlertSink(), now: now));
    }

    [Fact]
    public void PlanRejectsMissingJourneysSharedCustomerBoundariesAndAmbiguousRoutes()
    {
        var plan = LoadPlan();
        Assert.Throws<ArgumentException>(() => (plan with { Journeys = plan.Journeys.Skip(1).ToArray() }).Validate());
        Assert.Throws<ArgumentException>(() => (plan with
        {
            Isolation = plan.Isolation with { CustomerCapacityAllowed = true }
        }).Validate());
        Assert.Throws<ArgumentException>(() => (plan with
        {
            Journeys = plan.Journeys.Select((journey, index) => index == 0
                ? journey with { DependencyAlertRoute = journey.EtlSqlAlertRoute }
                : journey).ToArray()
        }).Validate());
    }

    private static ProductionCanaryPlan LoadPlan()
    {
        var path = Path.Combine(RepoRoot(), "tests", "fixtures", "production-canary-plan.json");
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return document.RootElement.Deserialize<ProductionCanaryPlan>(options)!;
    }

    private static CanaryExecutionRequest Request(ProductionCanaryPlan plan)
    {
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        var journey = plan.Journeys[0];
        return new CanaryExecutionRequest(journey, plan.Isolation,
            new CanaryCredential("credential", now, now + plan.MaximumCredentialLifetime),
            journey.Regions[0], journey.FailureDomains[0], CanaryFaultKind.None);
    }

    private static CanaryExecutionObservation PassingObservation(ProductionCanaryPlan plan) => new()
    {
        CorrectResult = true,
        SuccessfulSamples = 100,
        TotalSamples = 100,
        Latency = TimeSpan.FromMilliseconds(10),
        SyntheticDependencyHealthy = true,
        AccessedTenantIds = [plan.Isolation.TenantId],
        CustomerDataAccessed = false,
        CustomerSystemsAccessed = false,
        CustomerCapacityConsumed = false,
        AttributedMonthlyCostUsd = 0.01m
    };

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ETL-SQL.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private static async Task WriteEvidenceAsync(ProductionCanaryReport report)
    {
        await WriteEvidenceFileAsync("production-canary-report.json", report);
    }

    private static async Task WriteEvidenceFileAsync<T>(string fileName, T evidence)
    {
        var directory = Environment.GetEnvironmentVariable("ETLSQL_CANARY_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var temporary = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(evidence, options));
        File.Move(temporary, path, overwrite: true);
    }

    private sealed class ProductionLikeCanaryExecutor : IProductionCanaryExecutor
    {
        public string ExecutorKind => "production-like";

        public Task<CanaryExecutionObservation> ExecuteAsync(
            CanaryExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = new CanaryExecutionObservation
            {
                CorrectResult = request.Fault != CanaryFaultKind.EtlSqlCorrectnessFailure,
                SuccessfulSamples = request.Fault == CanaryFaultKind.EtlSqlAvailabilityRegression ? 99 : 100,
                TotalSamples = 100,
                Latency = request.Fault == CanaryFaultKind.EtlSqlLatencyRegression
                    ? request.Journey.Slo.MaximumLatency + TimeSpan.FromMilliseconds(1)
                    : request.Journey.Slo.MaximumLatency / 2,
                SyntheticDependencyHealthy = request.Fault != CanaryFaultKind.SyntheticDependencyOutage,
                AccessedTenantIds = [request.Isolation.TenantId],
                CustomerDataAccessed = false,
                CustomerSystemsAccessed = false,
                CustomerCapacityConsumed = false,
                AttributedMonthlyCostUsd = 0.01m,
                Diagnostics = new Dictionary<string, string>
                {
                    ["journey"] = request.Journey.Id.ToString(),
                    ["region"] = request.Region,
                    ["failureDomain"] = request.FailureDomain,
                    ["fault"] = request.Fault.ToString()
                }
            };
            return Task.FromResult(observation);
        }
    }

    private sealed class RecordingCredentialAuthority : ICanaryCredentialAuthority
    {
        private int _issued;
        public List<string> Events { get; } = [];

        public Task<CanaryCredential> IssueAsync(
            string identityId,
            DateTimeOffset now,
            TimeSpan lifetime,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"issue:{identityId}");
            return Task.FromResult(new CanaryCredential($"credential-{++_issued}", now, now + lifetime));
        }

        public Task RevokeAsync(string credentialId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"revoke:{credentialId}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAlertSink : ICanaryAlertSink
    {
        public List<CanaryAlert> Alerts { get; } = [];

        public Task<CanaryAlertDelivery> PublishAsync(
            CanaryAlert alert,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Alerts.Add(alert);
            return Task.FromResult(new CanaryAlertDelivery(
                $"alert-{Alerts.Count}", alert.Route, true));
        }
    }

    private sealed class ProductionLikeProvisioner : ICanaryResourceProvisioner
    {
        public string ProvisionerKind => "production-like";

        public Task<CanaryProvisioningObservation> ProvisionAsync(
            CanaryIsolationBoundary boundary,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new CanaryProvisioningObservation
            {
                TenantId = boundary.TenantId,
                IdentityId = boundary.IdentityId,
                ResourceNamespace = boundary.ResourceNamespace,
                QuotaPool = boundary.QuotaPool,
                EnforcedMonthlyCostLimitUsd = boundary.MaximumMonthlyCostUsd,
                CustomerResourceGrants = 0,
                CustomerNetworkRoutes = 0,
                UsesDedicatedCapacity = true
            });
        }
    }

    private sealed class DefectiveProvisioner(string defect) : ICanaryResourceProvisioner
    {
        public string ProvisionerKind => "defective";

        public Task<CanaryProvisioningObservation> ProvisionAsync(
            CanaryIsolationBoundary boundary,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new CanaryProvisioningObservation
            {
                TenantId = defect == "wrong-tenant" ? "synthetic-wrong" : boundary.TenantId,
                IdentityId = boundary.IdentityId,
                ResourceNamespace = boundary.ResourceNamespace,
                QuotaPool = boundary.QuotaPool,
                EnforcedMonthlyCostLimitUsd = defect == "weak-cost-guard"
                    ? boundary.MaximumMonthlyCostUsd + 1m
                    : boundary.MaximumMonthlyCostUsd,
                CustomerResourceGrants = defect == "customer-grant" ? 1 : 0,
                CustomerNetworkRoutes = defect == "customer-route" ? 1 : 0,
                UsesDedicatedCapacity = defect != "shared-capacity"
            });
        }
    }
}
