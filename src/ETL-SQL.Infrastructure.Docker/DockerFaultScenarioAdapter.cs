using ETL_SQL.Core.Reliability;

namespace ETL_SQL.Infrastructure.Docker;

/// <summary>
/// Connects the provider-neutral fault matrix to a Docker-backed scenario driver. The adapter is
/// intentionally thin: the caller receives the unchanged scenario identity and invariant contract.
/// </summary>
public sealed class DockerFaultScenarioAdapter(
    Func<FaultRunRequest, IFaultInjectionHook, CancellationToken, Task<FaultRunObservation>> execute)
    : IFaultScenarioAdapter
{
    public string AdapterKind => "docker";

    public Task<FaultRunObservation> ExecuteAsync(
        FaultRunRequest request,
        IFaultInjectionHook hook,
        CancellationToken cancellationToken = default) => execute(request, hook, cancellationToken);
}
