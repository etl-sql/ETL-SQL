using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core
{
    public enum ThresholdType
    {
        JoinSpill,
        WindowSpill,
        ExternalHashPartitions,
        ExternalSortChunkSize,
        BatchSize,
        MaxRecursiveDepth,
        MaxInMemoryBatches,
        ForeachPageSize,
        MaxMessages,
        MaxParallelDegree,
        MaxStringResultSize,
        RegexMatchTimeout,
        MaxFileOperations,
        MaxGroupingSets,
        MaxSessionSize,
        Telemetry,
        TempTableSpill,
        MaxLastResultRows,
        MaxGenerateRows,
        MaxSmtpEmailsPerScript,
        MaxInternalOperations,
        InteractiveMode,
        CaseSensitive,
        Lineage,
        LineageNamespace,
        LineageImportCatalog,
        TruncateString,
        SkipError
    }

    public record SetThresholdStatement(ThresholdType Type, Expression Value) : Statement
    {
        public override string ToSql()
        {
            return AstSerializer.Format(this);
        }
    }
}
