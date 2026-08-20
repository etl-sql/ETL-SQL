namespace ETL_SQL.Core.Governance;

/// <summary>Host-owned route from an authorized catalog alias to one authenticated Gateway session.</summary>
public interface IGatewayOperationRouter
{
    Task<GatewayRoutedResult> ExecuteAsync(
        ExecutionIdentity identity,
        GatewayResourceBinding binding,
        GatewayOperationClass operationClass,
        GatewayOperationEffect effect,
        GatewayOperationBounds bounds,
        string request,
        IReadOnlyList<string>? parameters,
        CancellationToken cancellationToken);
}

public sealed record GatewayRoutedResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    bool Truncated = false);
