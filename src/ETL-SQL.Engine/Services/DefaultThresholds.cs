using ETL_SQL.Common;
using ETL_SQL.Core;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Centralized registry for engine thresholds and resource ceilings.
/// Provides defaults from <see cref="LanguageMetadata"/> and allows overrides via <see cref="IConfiguration"/>.
/// </summary>
public static class DefaultThresholds
{
    public static int MaxInMemoryBatches(IConfiguration? config)
        => config?.GetValue<int?>("Engine:MaxInMemoryBatches") ?? LanguageMetadata.DefaultMaxInMemoryBatches;

    public static int ForeachPageSize(IConfiguration? config)
        => config?.GetValue<int?>("Engine:ForeachPageSize") ?? 0;

    public static int JoinSpillThreshold(IConfiguration? config)
        => config?.GetValue<int?>("Engine:JoinSpillThreshold") ?? LanguageMetadata.DefaultJoinSpillThreshold;

    public static int ExternalHashPartitions(IConfiguration? config)
        => config?.GetValue<int?>("Engine:ExternalHashPartitions") ?? LanguageMetadata.DefaultExternalHashPartitions;

    public static int BatchSize(IConfiguration? config)
        => config?.GetValue<int?>("Engine:BatchSize") ?? 10000;

    public static int MaxLastResultRows(IConfiguration? config)
        => config?.GetValue<int?>("Engine:MaxLastResultRows") ?? LanguageMetadata.DefaultMaxLastResultRows;

    public static int MaxRecursiveDepth(IConfiguration? config)
        => config?.GetValue<int?>("Engine:MaxRecursiveDepth") ?? 10000;

    public static int ExternalSortChunkSize(IConfiguration? config)
        => config?.GetValue<int?>("Engine:ExternalSortChunkSize") ?? LanguageMetadata.DefaultExternalSortChunkSize;

    public static int WindowSpillThreshold(IConfiguration? config)
        => config?.GetValue<int?>("Engine:WindowSpillThreshold") ?? LanguageMetadata.DefaultWindowSpillThreshold;

    public static int OperatorMemoryGrantMB(IConfiguration? config)
        => config?.GetValue<int?>("Engine:OperatorMemoryGrantMB") ?? 256;

    /// <summary>
    /// Process-wide RAM-governor ceiling (MB) on the summed in-memory buffer footprint across
    /// concurrent operators and jobs (the external engines spill/repartition to stay under it).
    /// Resolution of <c>Engine:TotalMemoryGrantMB</c>:
    /// <list type="bullet">
    /// <item><b>&gt; 0</b> — explicit absolute ceiling in MB (overrides auto; use for containers/shared hosts).</item>
    /// <item><b>0</b> — unbounded; the governor is off (power-user escape hatch — risks consuming all RAM).</item>
    /// <item><b>unset or &lt; 0</b> — auto: ~80% of physical RAM (honors container limits), floored at 512 MB.</item>
    /// </list>
    /// </summary>
    public static int TotalMemoryGrantMB(IConfiguration? config)
    {
        var configured = config?.GetValue<int?>("Engine:TotalMemoryGrantMB");
        if (configured is int v && v >= 0) return v; // >0 absolute, 0 = unbounded
        return AutoMemoryGrantMB();                   // null or negative => auto
    }

    /// <summary>
    /// Auto memory-grant ceiling: ~80% of physical RAM, floored at 512 MB. Uses the GC's view of
    /// available memory, which honors container/cgroup limits, so it scales from a 2 GB container to
    /// a 128 GB server without per-host tuning.
    /// </summary>
    internal static int AutoMemoryGrantMB()
    {
        long totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (totalBytes <= 0) return 4096; // unknown environment — safe fallback
        long autoMB = (long)(totalBytes * 0.80 / (1024 * 1024));
        if (autoMB < 512) autoMB = 512;
        if (autoMB > int.MaxValue) autoMB = int.MaxValue;
        return (int)autoMB;
    }

    /// <summary>
    /// RAM governor policy when an in-memory operator would breach the <see cref="TotalMemoryGrantMB"/>
    /// ceiling and cannot spill further: <c>SpillOrFail</c> (default — abort with a clear error) or
    /// <c>SpillOnly</c> (churn: keep going regardless of time/RAM).
    /// </summary>
    public static MemoryGovernorPolicy MemoryGovernorPolicy(IConfiguration? config)
    {
        var s = config?.GetValue<string>("Engine:MemoryGovernorPolicy");
        return Enum.TryParse<MemoryGovernorPolicy>(s, ignoreCase: true, out var policy)
            ? policy
            : Core.MemoryGovernorPolicy.SpillOrFail;
    }

    public static long TempTableSpillThresholdRows(IConfiguration? config)
        => config?.GetValue<long?>("Engine:TempTableSpillThresholdRows") ?? LanguageMetadata.DefaultTempTableSpillThresholdRows;

    public static bool SpillEncryptionEnabled(IConfiguration? config)
        => config?.GetValue<bool>("Security:SpillEncryptionEnabled") ?? true;

    public static bool SpillCompressionEnabled(IConfiguration? config)
        => config?.GetValue<bool>("Security:SpillCompressionEnabled") ?? true;

    public static string SpillFormat(IConfiguration? config)
        => config?.GetValue<string>("Security:SpillFormat") ?? "Arrow";

    public static int SubqueryCacheSize(IConfiguration? config)
        => config?.GetValue<int?>("Engine:SubqueryCacheSize") ?? 5000;

    public static DayOfWeek StartOfWeek(IConfiguration? config)
    {
        var s = config?.GetValue<string>("Engine:StartOfWeek") ?? "Monday";
        return Enum.TryParse<DayOfWeek>(s, ignoreCase: true, out var day) ? day : DayOfWeek.Monday;
    }

    public static string ScriptHashPolicy(IConfiguration? config)
    {
        var policy = config?.GetValue<string>("Engine:ScriptHashPolicy") ?? "Warn";
        return policy.Equals("Block", StringComparison.OrdinalIgnoreCase) ? "Block" : "Warn";
    }

    public static bool PersistenceDefault(IConfiguration? config)
        => config?.GetValue<bool?>("Session:PersistenceDefault") ?? true;

    public static bool CaseSensitiveComparison(IConfiguration? config)
        => config?.GetValue<bool?>("Engine:CaseSensitiveComparison") ?? false;

    public static bool TelemetryEnabled(IConfiguration? config)
        => config?.GetValue<bool?>("Engine:TelemetryEnabled") ?? true;

    public static bool LineageEnabled(IConfiguration? config)
        => config?.GetValue<bool?>("Engine:LineageEnabled") ?? true;

    public static bool AllowPlaintextSecrets(IConfiguration? config)
        => config?.GetValue<bool?>("Engine:AllowPlaintextSecrets") ?? false;

    public static bool NoSaveSensitive(IConfiguration? config)
        => config?.GetValue<bool?>("Engine:NoSaveSensitive") ?? false;

    public static bool NoSaveConnection(IConfiguration? config)
        => config?.GetValue<bool?>("Engine:NoSaveConnection") ?? false;

    public static bool ConnectionEncryption(IConfiguration? config)
        => config?.GetValue<bool?>("Engine:ConnectionEncryption") ?? false;
}
