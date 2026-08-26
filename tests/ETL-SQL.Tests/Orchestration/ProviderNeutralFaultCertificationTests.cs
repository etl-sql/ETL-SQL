using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Connectors.Cloud;
using ETL_SQL.Core.Reliability;
using ETL_SQL.Infrastructure.Docker;

namespace ETL_SQL.Tests.Orchestration;

[Trait("Category", "FaultCertification")]
public sealed class ProviderNeutralFaultCertificationTests
{
    [Fact]
    public void CatalogPinsEveryRequiredProviderNeutralScenario()
    {
        Assert.Equal(Enum.GetValues<FaultScenarioId>().Length, ProviderNeutralFaultScenarios.All.Count);
        Assert.Equal(ProviderNeutralFaultScenarios.All.Count,
            ProviderNeutralFaultScenarios.All.Select(scenario => scenario.Id).Distinct().Count());
        Assert.All(ProviderNeutralFaultScenarios.All, scenario =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.Trigger));
            Assert.False(string.IsNullOrWhiteSpace(scenario.ExpectedSafeOutcome));
        });
    }

    [Fact]
    public async Task LocalDockerAndCloudAdaptersPreserveScenarioIdentityAndSemantics()
    {
        var scenario = ProviderNeutralFaultScenarios.Get(FaultScenarioId.NetworkPartition);
        var request = new FaultRunRequest(
            scenario, "provider-a", "Enterprise", 1, "operation-a",
            FaultCheckpointContract.NonResumable);
        var seen = new List<FaultRunRequest>();

        Task<FaultRunObservation> Driver(
            FaultRunRequest received,
            IFaultInjectionHook hook,
            CancellationToken cancellationToken)
        {
            seen.Add(received);
            return ExerciseAsync(received, hook, cancellationToken);
        }

        IFaultScenarioAdapter[] adapters =
        [
            new LocalFaultScenarioAdapter(Driver),
            new DockerFaultScenarioAdapter(Driver),
            new CloudFaultScenarioAdapter(Driver)
        ];

        foreach (var adapter in adapters)
        {
            var hook = new DeterministicFaultInjectionHook(scenario.InjectionPoint);
            var observation = await adapter.ExecuteAsync(request, hook);
            Assert.True(hook.Activated);
            Assert.True(ProviderNeutralFaultCertificationRunner.Verify(request, observation).Passed);
        }

        Assert.Equal(3, seen.Count);
        Assert.All(seen, received => Assert.Same(request, received));
        Assert.Equal(["local", "docker", "cloud"], adapters.Select(adapter => adapter.AdapterKind));
    }

    [Fact]
    public async Task RepeatedMatrixProducesDurableInvariantAndRecoveryEvidence()
    {
        var provider = Environment.GetEnvironmentVariable("ETLSQL_FAULT_CERT_PROVIDER") ?? "local-filesystem-sqlite";
        var profile = Environment.GetEnvironmentVariable("ETLSQL_FAULT_CERT_PROFILE") ?? "Solo";
        var adapterKind = Environment.GetEnvironmentVariable("ETLSQL_FAULT_CERT_ADAPTER") ?? "local";
        var repetitions = int.TryParse(
            Environment.GetEnvironmentVariable("ETLSQL_FAULT_CERT_REPETITIONS"), out var configured)
            ? configured
            : 2;

        IFaultScenarioAdapter adapter = adapterKind.ToLowerInvariant() switch
        {
            "local" => new LocalFaultScenarioAdapter(ExerciseAsync),
            "docker" => new DockerFaultScenarioAdapter(ExerciseAsync),
            "cloud" => new CloudFaultScenarioAdapter(ExerciseAsync),
            _ => throw new InvalidOperationException($"Unknown fault adapter '{adapterKind}'.")
        };

        var report = await ProviderNeutralFaultCertificationRunner.RunAsync(
            adapter,
            provider,
            profile,
            repetitions,
            scenario => scenario.Id == FaultScenarioId.ProcessOrWorkerLoss
                ? FaultCheckpointContract.Named("after-stage")
                : FaultCheckpointContract.NonResumable);

        Assert.True(report.Passed);
        Assert.Equal(repetitions * ProviderNeutralFaultScenarios.All.Count, report.Runs.Count);
        Assert.All(report.Runs, run =>
        {
            Assert.Equal(provider, run.Request.Provider);
            Assert.Equal(profile, run.Request.DeploymentProfile);
            Assert.Equal(adapter.AdapterKind, run.AdapterKind);
            Assert.True(run.FaultActivated);
            Assert.True(run.Invariants.Passed);
        });
        Assert.All(report.Runs.Where(run =>
                run.Request.Scenario.Id == FaultScenarioId.ProcessOrWorkerLoss),
            run => Assert.Equal(FaultRecoveryClaim.NamedCheckpointResume, run.Observation.RecoveryClaim));
        Assert.All(report.Runs.Where(run =>
                run.Request.Scenario.Id != FaultScenarioId.ProcessOrWorkerLoss),
            run => Assert.Equal(FaultRecoveryClaim.SafeFailureAndDeliberateRetry, run.Observation.RecoveryClaim));

        await WriteEvidenceAsync($"ProviderNeutralFault-{profile}-{provider}-{adapter.AdapterKind}", report);
    }

    [Theory]
    [InlineData("split-brain")]
    [InlineData("stale-authority")]
    [InlineData("silent-loss")]
    [InlineData("duplicate-commit")]
    [InlineData("false-checkpoint-resume")]
    public void CertificationFailsClosedForEveryRequiredInvariant(string defect)
    {
        var request = new FaultRunRequest(
            ProviderNeutralFaultScenarios.Get(FaultScenarioId.DatabaseDisconnect),
            "provider-a", "Enterprise", 1, "operation-a", FaultCheckpointContract.NonResumable);
        var observation = PassingSafeFailure() with
        {
            MaximumConcurrentMutationAuthorities = defect == "split-brain" ? 2 : 1,
            StaleAuthorityRejected = defect != "stale-authority",
            ResultWasSilentlyLost = defect == "silent-loss",
            CommittedResults = defect == "duplicate-commit" ? 2 : 0,
            RecoveryClaim = defect == "false-checkpoint-resume"
                ? FaultRecoveryClaim.NamedCheckpointResume
                : FaultRecoveryClaim.SafeFailureAndDeliberateRetry,
            ResumedCheckpointName = defect == "false-checkpoint-resume" ? "invented" : null
        };

        Assert.False(ProviderNeutralFaultCertificationRunner.Verify(request, observation).Passed);
    }

    [Fact]
    public void NamedResumeMustMatchAnExplicitCheckpointContract()
    {
        var scenario = ProviderNeutralFaultScenarios.Get(FaultScenarioId.ProcessOrWorkerLoss);
        var explicitRequest = new FaultRunRequest(
            scenario, "provider-a", "SaaS", 1, "operation-a", FaultCheckpointContract.Named("stage-1"));
        var matching = PassingSafeFailure() with
        {
            RecoveryClaim = FaultRecoveryClaim.NamedCheckpointResume,
            ResumedCheckpointName = "stage-1",
            DeliberateRetryEligible = false
        };

        Assert.True(ProviderNeutralFaultCertificationRunner.Verify(explicitRequest, matching)
            .RecoveryClaimMatchesCheckpointContract);
        Assert.False(ProviderNeutralFaultCertificationRunner.Verify(
            explicitRequest with { CheckpointContract = FaultCheckpointContract.NonResumable }, matching)
            .RecoveryClaimMatchesCheckpointContract);
        Assert.False(ProviderNeutralFaultCertificationRunner.Verify(
            explicitRequest, matching with { ResumedCheckpointName = "stage-2" })
            .RecoveryClaimMatchesCheckpointContract);
    }

    private static async Task<FaultRunObservation> ExerciseAsync(
        FaultRunRequest request,
        IFaultInjectionHook hook,
        CancellationToken cancellationToken)
    {
        try
        {
            await hook.HitAsync(request.Scenario.InjectionPoint, cancellationToken);
            throw new InvalidOperationException("The configured deterministic fault did not activate.");
        }
        catch (FaultInjectedException exception) when (exception.Point == request.Scenario.InjectionPoint)
        {
            var namedResume = request.CheckpointContract.IsExplicitlyResumable;
            var duplicate = request.Scenario.Id == FaultScenarioId.DuplicateDelivery;
            return PassingSafeFailure() with
            {
                AcceptedDeliveries = duplicate ? 2 : 1,
                CommittedResults = duplicate ? 1 : 0,
                VisiblyFailedDeliveries = 1,
                RecoveryClaim = namedResume
                    ? FaultRecoveryClaim.NamedCheckpointResume
                    : FaultRecoveryClaim.SafeFailureAndDeliberateRetry,
                ResumedCheckpointName = namedResume ? request.CheckpointContract.CheckpointName : null,
                DeliberateRetryEligible = !namedResume,
                Diagnostics = new Dictionary<string, string>
                {
                    ["faultPoint"] = exception.Point.ToString(),
                    ["operationId"] = request.OperationId
                }
            };
        }
    }

    private static FaultRunObservation PassingSafeFailure() => new()
    {
        MaximumConcurrentMutationAuthorities = 1,
        StaleAuthorityRejected = true,
        AcceptedDeliveries = 1,
        CommittedResults = 0,
        VisiblyFailedDeliveries = 1,
        ResultWasSilentlyLost = false,
        RecoveryClaim = FaultRecoveryClaim.SafeFailureAndDeliberateRetry,
        DeliberateRetryEligible = true
    };

    private static async Task WriteEvidenceAsync(string name, FaultCertificationReport report)
    {
        var directory = Environment.GetEnvironmentVariable("ETLSQL_FAULT_CERT_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        var safeName = string.Concat(name.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        var path = Path.Combine(directory, safeName + ".json");
        var temporaryPath = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(report, options));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
