namespace ETL_SQL.Portal.Models;

public record JobDefinitionDto(
    string Name,
    string Script,
    int Interval,
    string Unit,
    string? AtTime,
    DateTime? LastRun,
    DateTime? NextRun,
    bool IsEnabled,
    int MaxRetries,
    int RetryDelaySeconds,
    string? ScriptHash,
    string HashPolicy,
    long Version = 1,
    string? TenantId = null,
    /// <summary>
    /// Who created and last changed this job. Persisted since attribution shipped and never shown,
    /// which made ownership invisible exactly where it decides access: the owner of an object can
    /// manage it, so an administrator looking at a job they cannot change had no way to see why.
    /// </summary>
    string? CreatedBy = null,
    string? ModifiedBy = null
);

public record JobHistoryEntryDto(
    long Id,
    string JobName,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    string? ErrorMessage,
    long RowsProcessed,
    long PeakMemoryBytes,
    double CpuTimeSeconds,
    string? ScriptHashAtRunTime,
    bool? HashMatched,
    long RowsQuarantined = 0,
    long RowsWarned = 0,
    string? DataQualityFailures = null,
    string? CheckpointLabel = null,
    bool HasResumeSession = false
);

public record OrchestratorMetricsDto(
    int ActiveJobs,
    int QueuedJobs,
    int MaxJobs,
    int AvailableSlots,
    int ActiveProcesses
);

public record OrchestratorStatusDto(
    string Status,
    double UptimeSeconds,
    int ProcessId,
    DateTime StartedAt,
    string Version
);

public record OrchestratorScriptsDto(
    string Root,
    string[] Files
);

public record CreateJobRequest(
    string Name,
    string ScriptText,
    int Interval,
    string Unit,
    string? AtTime = null,
    int MaxRetries = 0,
    int RetryDelaySeconds = 30,
    string? HashPolicy = "Warn"
);

public record UpdateJobRequest(
    string? ScriptText = null,
    int? Interval = null,
    string? Unit = null,
    string? AtTime = null,
    bool? IsEnabled = null,
    int? MaxRetries = null,
    int? RetryDelaySeconds = null,
    string? HashPolicy = null
);

public record TriggerJobRequest(
    Dictionary<string, string>? Variables = null
);

public record UpdateOrchestratorSettingsRequest(
    string? ApiUrl,
    string? ApiKey
);

public record UpdatePortalBrandingRequest(
    string? DisplayName,
    string? FooterText,
    string? LogoUrl
);
