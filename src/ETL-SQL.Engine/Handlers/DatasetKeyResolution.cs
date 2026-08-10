using ETL_SQL.Core.Security;

namespace ETL_SQL.Engine.Handlers;

/// <summary>Resolves a dataset key only for the duration of one cache operation.</summary>
internal sealed class DatasetKeyResolution : IDisposable
{
    private readonly ResolvedKeyMaterial? _lease;

    private DatasetKeyResolution(string? password, string? version, ResolvedKeyMaterial? lease)
    {
        Password = password;
        Version = version;
        _lease = lease;
    }

    internal string? Password { get; }
    internal string? Version { get; }

    internal static async ValueTask<DatasetKeyResolution> ResolveAsync(
        IExecutionContext context,
        string? version = null)
    {
        if (context is Evaluator evaluator)
        {
            var lease = await evaluator.ResolveDatasetKeyAsync(version, context.CancellationToken);
            if (lease is not null)
                return new DatasetKeyResolution(
                    Convert.ToBase64String(lease.Bytes.Span),
                    lease.Descriptor.Version,
                    lease);

            return new DatasetKeyResolution(evaluator.DatasetAtRestKey, version, null);
        }

        return new DatasetKeyResolution(null, version, null);
    }

    public void Dispose() => _lease?.Dispose();
}
