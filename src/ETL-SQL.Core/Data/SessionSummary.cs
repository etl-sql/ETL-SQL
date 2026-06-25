using System;

namespace ETL_SQL.Core.Data;
public record SessionSummary
{
    public string SessionId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime LastModifiedAt { get; init; }
    public long TotalSizeBytes { get; init; }
    public int TempTableCount { get; init; }
    public int VariableCount { get; init; }
    public string? LastScriptSource { get; init; }
    public string? OwnerUser { get; init; }
    public string? OwnerMachine { get; init; }

    public double SizeMB => Math.Round(TotalSizeBytes / (1024.0 * 1024.0), 2);
}
