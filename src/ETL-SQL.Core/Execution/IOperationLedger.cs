using System.Threading.Tasks;

namespace ETL_SQL.Core.Execution;

public enum OperationStatus
{
    Started,
    Completed,
    Failed
}

public class OperationState
{
    public string OperationId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public OperationStatus Status { get; set; }
    public int? ExitCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// A durable ledger for recording side-effecting operations to guarantee idempotency
/// and prevent ambiguous retries on crash recovery.
/// </summary>
public interface IOperationLedger
{
    Task RecordStartAsync(string operationId, string operationType, string payload);
    Task RecordCompletionAsync(string operationId, int exitCode, string? error);
    Task<OperationState?> GetStateAsync(string operationId);
}

public interface IOperationLedgerFactory
{
    IOperationLedger Create(string sessionRoot, string sessionId);
}

