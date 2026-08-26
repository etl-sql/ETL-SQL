using System.Text.Json;

namespace ETL_SQL.Worker;

internal sealed record WorkerProfileFeatures(
    bool InteractiveUi,
    bool GatewayAdministration,
    bool Scheduler,
    bool ReportAuthoring,
    bool ScriptExecution,
    bool NamedCheckpointResume,
    bool Governance);

internal sealed record WorkerProfileManifest(
    int SchemaVersion,
    string Profile,
    string[] Commands,
    string[] ConnectorGroups,
    WorkerProfileFeatures Features)
{
    internal static async Task<WorkerProfileManifest> LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "worker-profile.json");
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<WorkerProfileManifest>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (manifest is null || manifest.SchemaVersion != 1 ||
            !string.Equals(manifest.Profile, "engine-worker-full-connectors", StringComparison.Ordinal) ||
            !manifest.Features.ScriptExecution || !manifest.Features.Governance ||
            manifest.Features.InteractiveUi || manifest.Features.GatewayAdministration || manifest.Features.Scheduler)
        {
            throw new InvalidDataException("The worker profile manifest is missing or does not match the certified engine-worker contract.");
        }

        var requiredCommands = new[] { "run", "runner", "profile-probe" };
        if (requiredCommands.Except(manifest.Commands, StringComparer.Ordinal).Any())
            throw new InvalidDataException("The worker profile manifest omits a required command.");

        return manifest;
    }
}
