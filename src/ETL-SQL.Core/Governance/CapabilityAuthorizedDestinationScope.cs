using ETL_SQL.Services;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Per-attempt capability-authorized destination boundary (§12 Default-Deny Egress).
///
/// <para>When an execution attempt is bounded to specific capability-authorized destinations
/// (e.g. an authorized Gateway Broker URI, storage endpoint, or specific database host),
/// this ambient scope ensures no other outbound connection can be established by the attempt.</para>
/// </summary>
public sealed class CapabilityAuthorizedDestinationScope : IDisposable
{
    private static readonly AsyncLocal<CapabilityAuthorizedDestinationScope?> _current = new();

    public static CapabilityAuthorizedDestinationScope? Current => _current.Value;

    private readonly HashSet<string> _allowedDestinations;
    private readonly CapabilityAuthorizedDestinationScope? _parent;
    private bool _disposed;

    private CapabilityAuthorizedDestinationScope(IEnumerable<string> allowedDestinations)
    {
        ArgumentNullException.ThrowIfNull(allowedDestinations);
        _allowedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dest in allowedDestinations)
        {
            if (!string.IsNullOrWhiteSpace(dest))
            {
                _allowedDestinations.Add(Normalize(dest));
            }
        }
        _parent = _current.Value;
    }

    /// <summary>
    /// Begins a capability-authorized destination scope for the current asynchronous execution flow.
    /// </summary>
    public static CapabilityAuthorizedDestinationScope Enter(IEnumerable<string> allowedDestinations)
    {
        var scope = new CapabilityAuthorizedDestinationScope(allowedDestinations);
        _current.Value = scope;
        return scope;
    }

    /// <summary>
    /// Enforces that the requested host or target is permitted by the active capability scope (if any).
    /// </summary>
    public static void Enforce(string? host, string? target = null)
    {
        var scope = Current;
        if (scope == null || scope._allowedDestinations.Count == 0)
        {
            // No capability restriction active; ordinary policy & infrastructure fence apply
            return;
        }

        var normalizedHost = !string.IsNullOrWhiteSpace(host) ? Normalize(host) : null;
        var normalizedTargetHost = Uri.TryCreate(target, UriKind.Absolute, out var targetUri)
            ? Normalize(targetUri.Host)
            : null;

        var isAllowed = (normalizedHost != null && scope._allowedDestinations.Contains(normalizedHost)) ||
                        (normalizedTargetHost != null && scope._allowedDestinations.Contains(normalizedTargetHost)) ||
                        (!string.IsNullOrWhiteSpace(target) && scope._allowedDestinations.Contains(Normalize(target)));

        if (!isAllowed)
        {
            var destination = normalizedHost ?? target ?? "<unknown>";
            throw new SecurityException(
                $"Outbound connection to '{destination}' was denied because it is not in the active attempt's capability-authorized destination set.");
        }
    }

    private static string Normalize(string destination)
    {
        var trimmed = destination.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.Host.ToLowerInvariant();
        }
        return trimmed.ToLowerInvariant();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _current.Value = _parent;
        }
    }
}
