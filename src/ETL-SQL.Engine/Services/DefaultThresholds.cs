using ETL_SQL.Common;
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
    /// Process-wide ceiling (MB) on the summed in-memory buffer footprint across concurrent
    /// operators and jobs. 0 (the default when unset) means unbounded — only the per-operator
    /// grant and row-count backstops apply. Generous by design so single queries are unaffected;
    /// it engages under genuine concurrent multi-GB pressure.
    /// </summary>
    public static int TotalMemoryGrantMB(IConfiguration? config)
        => config?.GetValue<int?>("Engine:TotalMemoryGrantMB") ?? 0;

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
