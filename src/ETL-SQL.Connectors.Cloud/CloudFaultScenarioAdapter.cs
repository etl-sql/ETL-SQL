using ETL_SQL.Core.Reliability;

namespace ETL_SQL.Connectors.Cloud;

/// <summary>
/// Connects a cloud-provider fault driver to the same scenario and evidence contract used locally and
/// in Docker. Provider SDK details remain in the supplied driver and cannot alter matrix semantics.
/// </summary>
public sealed class CloudFaultScenarioAdapter(
    Func<FaultRunRequest, IFaultInjectionHook, CancellationToken, Task<FaultRunObservation>> execute)
    : IFaultScenarioAdapter
{
    public string AdapterKind => "cloud";

    public Task<FaultRunObservation> ExecuteAsync(
        FaultRunRequest request,
        IFaultInjectionHook hook,
        CancellationToken cancellationToken = default) => execute(request, hook, cancellationToken);
}
