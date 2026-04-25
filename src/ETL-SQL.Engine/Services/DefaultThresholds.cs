using Microsoft.Extensions.Configuration;
using ETL_SQL.Common;

namespace ETL_SQL.Engine.Services
{
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

        public static long TempTableSpillThresholdRows(IConfiguration? config)
            => config?.GetValue<long?>("Engine:TempTableSpillThresholdRows") ?? LanguageMetadata.DefaultTempTableSpillThresholdRows;

        public static bool SpillEncryptionEnabled(IConfiguration? config)
            => config?.GetValue<bool>("Security:SpillEncryptionEnabled") ?? true;

        public static bool SpillCompressionEnabled(IConfiguration? config)
            => config?.GetValue<bool>("Security:SpillCompressionEnabled") ?? true;

        public static string SpillFormat(IConfiguration? config)
            => config?.GetValue<string>("Security:SpillFormat") ?? "Arrow";
    }
}
