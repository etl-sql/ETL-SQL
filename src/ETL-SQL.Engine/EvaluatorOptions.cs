using System;
using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Services;

namespace ETL_SQL.Engine;
/// <summary>
/// Configuration and security options for the <see cref="Evaluator"/>.
/// </summary>
public class EvaluatorOptions
{
    // --- Security & Resource Limits ---
    public int MaxRecursiveDepth { get; set; } = 10000;
    public int MaxFileOperations { get; set; } = SecurityService.DefaultMaxFileOperations;
    public int MaxGroupingSets { get; set; } = LanguageMetadata.DefaultMaxGroupingSets;
    public long MaxSessionSize { get; set; } = 200 * 1024 * 1024; // 200MB Default
    public int MaxLastResultRows { get; set; } = LanguageMetadata.DefaultMaxLastResultRows;
    public int MaxGenerateRows { get; set; } = SecurityService.DefaultMaxGenerateRows;
    public int MaxSmtpEmailsPerScript { get; set; } = SecurityService.DefaultMaxSmtpEmailsPerScript;
    public int MaxInternalOperations { get; set; } = 100000;
    public int MaxConnectionsPerScript { get; set; } = 100;
    public int MaxTempTablesPerScript { get; set; } = 100;
    public int MaxVariablesPerScript { get; set; } = 100;
    public int MaxVisualsPerScript { get; set; } = 100;

    public int RegexMatchTimeoutMs { get; set; } = (int)SecurityService.DefaultRegexMatchTimeout.TotalMilliseconds;
    public long MaxStringResultSize { get; set; } = LanguageMetadata.DefaultMaxStringResultSize;

    // --- Permissions ---
    public bool AllowUnknownFileTypes { get; set; }
    public bool AllowLargeFileOperationCount { get; set; }
    public bool AllowDeepRecursion { get; set; }
    public bool AllowLargeStringResults { get; set; }
    public HashSet<string> AllowedFileTypeOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    // --- Engine Thresholds ---
    public int BatchSize { get; set; } = 10000;
    public int MaxInMemoryBatches { get; set; } = LanguageMetadata.DefaultMaxInMemoryBatches;
    public int ForeachPageSize { get; set; } = 0;
    public int JoinSpillThreshold { get; set; } = 100000;
    public int ExternalHashPartitions { get; set; } = 32;
    public int ExternalSortChunkSize { get; set; } = 100000;
    public int WindowSpillThreshold { get; set; } = LanguageMetadata.DefaultWindowSpillThreshold;
    public int OperatorMemoryGrantMB { get; set; } = 256;
    public MemoryGovernorPolicy MemoryGovernorPolicy { get; set; } = MemoryGovernorPolicy.SpillOrFail;
    public int SubqueryCacheSize { get; set; } = 5000;
    public long SubquerySpillThresholdRows { get; set; } = LanguageMetadata.DefaultSubquerySpillThresholdRows;
    public long TempTableSpillThresholdRows { get; set; }
    public int MaxParallelDegree { get; set; } = LanguageMetadata.DefaultMaxParallelDegree;

    // --- Features ---
    public bool TelemetryEnabled { get; set; } = true;
    public bool LineageEnabled { get; set; } = true;
    public string? LineageNamespace { get; set; } = "etl-sql";
    public string? JobName { get; set; }
    public bool LineageImportCatalog { get; set; }
    public bool TruncateString { get; set; } = false;
    public bool SkipError { get; set; } = false;
    public bool SpillEncryptionEnabled { get; set; } = true;
    public bool SpillCompressionEnabled { get; set; } = true;
    public string SpillFormat { get; set; } = "Arrow";
    public bool AutoRollbackOnFinish { get; set; } = true;
    public bool ReuseLoopNodes { get; set; } = true;
    public bool UseColumnarTempTables { get; set; } = true;
    public bool DisplayExecuteTree { get; set; } = true;
    public bool IsProfiling { get; set; } = true;

    // --- Date/Time ---
    public DayOfWeek WeekStartDay { get; set; } = DayOfWeek.Monday;

    // --- Features ---
    public bool CaseSensitiveComparison { get; set; }

    // --- Security ---
    public string ScriptHashPolicy { get; set; } = "Warn";

    // --- UI / Verbosity ---
    public bool IsVerbose { get; set; }
    public bool RedirectOutput { get; set; }
    public int? PreviewLimit { get; set; }
    public bool ShowPassword { get; set; }
    public bool AllowPlaintextSecrets { get; set; }
    public bool NoSaveSensitive { get; set; }
    public bool NoSaveConnection { get; set; }
    public bool ConnectionEncryption { get; set; }
    public int MaxMessages { get; set; } = 1000;
}
