namespace ETL_SQL.Core.Governance;

/// <summary>Outcome storage. Save must be durable before returning; a failure refuses execution.</summary>
public interface IGatewayOutcomeStore
{
    GatewayOperationOutcome? Find(string tenantId, string operationId);
    void Save(GatewayOperationOutcome outcome);
    IReadOnlyList<GatewayOperationOutcome> ListAmbiguousMutating(string tenantId);
}

internal sealed class MemoryGatewayOutcomeStore : IGatewayOutcomeStore
{
    private readonly Dictionary<(string TenantId, string OperationId), GatewayOperationOutcome> _outcomes = new();

    public GatewayOperationOutcome? Find(string tenantId, string operationId) =>
        _outcomes.GetValueOrDefault((tenantId, operationId));

    public void Save(GatewayOperationOutcome outcome) =>
        _outcomes[(outcome.TenantId, outcome.OperationId)] = outcome;

    public IReadOnlyList<GatewayOperationOutcome> ListAmbiguousMutating(string tenantId) =>
        _outcomes.Values.Where(item => item.TenantId == tenantId
            && item.Effect == GatewayOperationEffect.Mutating
            && item.State == GatewayOutcomeState.Ambiguous).ToList();
}

/// <summary>Read history can expire. Mutating receipts never expire; only their detail is compacted.</summary>
public sealed record GatewayOutcomeRetentionOptions
{
    public int ReadOnlyRetentionDays { get; init; } = 7;
    public int ReceiptDetailRetentionDays { get; init; } = 30;
    public int MaintenanceEveryTransitions { get; init; } = 1000;
    public int MaintenanceBatchSize { get; init; } = 1000;
    public int IncrementalVacuumPages { get; init; } = 256;

    public void Validate()
    {
        if (ReadOnlyRetentionDays <= 0 || ReceiptDetailRetentionDays <= 0
            || MaintenanceEveryTransitions <= 0 || MaintenanceBatchSize <= 0 || IncrementalVacuumPages < 0)
            throw new ArgumentOutOfRangeException(nameof(GatewayOutcomeRetentionOptions), "Gateway retention settings must be positive (vacuum pages may be zero).");
    }
}
