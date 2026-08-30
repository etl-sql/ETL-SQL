using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core;

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
    SkipError,
    OperatorMemoryGrant,
    ConnectionPreviewLimit,
    /// <summary>
    /// <c>SET DATA_QUALITY_DRY_RUN = ON</c> — evaluate <c>EXPECT</c> rules and report what they
    /// would do, without diverting rows, writing capture tables, or throwing. Lets a steward
    /// calibrate a new rule against real data before it can affect a production load.
    /// </summary>
    DataQualityDryRun
}

public record SetThresholdStatement(ThresholdType Type, Expression Value) : Statement
{
    public override string ToSql()
    {
        return AstSerializer.Format(this);
    }
}
