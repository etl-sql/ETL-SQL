namespace ETL_SQL.Core.Adaptive;

/// <summary>
/// Per-job view of adaptive setpoints. Operators consult this at natural boundaries; the advisor
/// does not mutate configured ceilings or interrupt in-flight work.
/// </summary>
public sealed class AdaptiveAdvisor : IDisposable
{
    private readonly AdaptiveExecutionController _controller;
    private readonly object _gate = new();
    private bool _disposed;

    internal AdaptiveAdvisor(
        AdaptiveExecutionController controller,
        Guid id,
        AdaptiveExecutionCeilings configuredCeilings,
        AdaptiveSetpoints initialSetpoints)
    {
        _controller = controller;
        Id = id;
        ConfiguredCeilings = configuredCeilings;
        Current = initialSetpoints;
    }

    public Guid Id { get; }
    public AdaptiveExecutionCeilings ConfiguredCeilings { get; }
    public AdaptiveSetpoints Current { get; private set; }

    internal AdaptiveSetpoints Snapshot()
    {
        lock (_gate)
            return Current;
    }

    internal void Update(AdaptiveSetpoints setpoints)
    {
        lock (_gate)
            Current = setpoints;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Unregister(Id);
    }
}
