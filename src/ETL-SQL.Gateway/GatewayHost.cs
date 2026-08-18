using ETL_SQL.Core.Governance;

namespace ETL_SQL.Gateway;

/// <summary>Status of the long-running Gateway daemon host.</summary>
public enum GatewayHostStatus
{
    Stopped,
    Connecting,
    Connected,
    BackingOff,
    Faulted
}

/// <summary>Configuration options for the Gateway daemon host.</summary>
public sealed record GatewayHostOptions(
    GatewaySessionOptions SessionOptions,
    TimeSpan InitialBackoff = default,
    TimeSpan MaxBackoff = default,
    int MaxRetries = -1) // -1 = run indefinitely until cancelled
{
    public TimeSpan InitialBackoff { get; init; } = InitialBackoff == default ? TimeSpan.FromSeconds(1) : InitialBackoff;
    public TimeSpan MaxBackoff { get; init; } = MaxBackoff == default ? TimeSpan.FromSeconds(30) : MaxBackoff;
}

/// <summary>
/// Long-running daemon host for the on-premises Gateway.
///
/// <para>Wraps <see cref="GatewaySessionClient"/> with automatic reconnection, exponential backoff,
/// graceful shutdown, and state notifications for hosting inside a Windows Service, Linux systemd
/// daemon, or container.</para>
/// </summary>
public sealed class GatewayHost(
    GatewayHostOptions options,
    GatewayOperationDispatcher dispatcher,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private GatewayHostStatus _status = GatewayHostStatus.Stopped;
    private readonly Lock _gate = new();

    public GatewayHostStatus Status
    {
        get
        {
            lock (_gate) return _status;
        }
        private set
        {
            lock (_gate) _status = value;
        }
    }

    public event Action<GatewayHostStatus>? StatusChanged;
    public event Action<Exception>? ErrorOccurred;

    /// <summary>
    /// Runs the daemon loop until cancellation is requested or max retries are exceeded.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var attempts = 0;
        var currentBackoff = options.InitialBackoff;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (options.MaxRetries >= 0 && attempts >= options.MaxRetries)
            {
                SetStatus(GatewayHostStatus.Stopped);
                break;
            }

            attempts++;
            SetStatus(GatewayHostStatus.Connecting);

            try
            {
                var client = new GatewaySessionClient(options.SessionOptions, dispatcher);
                SetStatus(GatewayHostStatus.Connected);
                currentBackoff = options.InitialBackoff; // Reset backoff on successful connection

                await client.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);

                if (cancellationToken.IsCancellationRequested)
                    break;

                SetStatus(GatewayHostStatus.BackingOff);
                try
                {
                    await Task.Delay(currentBackoff, _time, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Exponential backoff capped at MaxBackoff
                currentBackoff = TimeSpan.FromTicks(Math.Min(
                    currentBackoff.Ticks * 2,
                    options.MaxBackoff.Ticks));
            }
        }

        SetStatus(GatewayHostStatus.Stopped);
    }

    private void SetStatus(GatewayHostStatus newStatus)
    {
        lock (_gate)
        {
            if (_status == newStatus) return;
            _status = newStatus;
        }
        StatusChanged?.Invoke(newStatus);
    }
}
