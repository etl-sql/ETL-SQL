using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Formatting;

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
        TempTableSpill
    }

    public record SetThresholdStatement(ThresholdType Type, Expression Value) : Statement
    {
        public override string ToSql()
        {
            return AstSerializer.Format(this);
        }
    }
}
