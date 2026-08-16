using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ETL_SQL.Core.Data;

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
    string? ModifiedBy = null,
    string? DisplayName = null,
    string? Description = null,
    string? Options = null,
    JobTargetKind JobType = JobTargetKind.Script,
    string? TargetPath = null,
    JobId Id = default
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
    string? ScriptText = null,
    int? Interval = null,
    string? Unit = null,
    string? AtTime = null,
    int? MaxRetries = 0,
    int? RetryDelaySeconds = 30,
    string? HashPolicy = "Warn",
    string? JobType = null,
    string? TargetPath = null,
    string? DisplayName = null,
    string? Description = null,
    Dictionary<string, string>? Options = null,
    string? Mode = null
);

public record UpdateJobRequest(
    string? ScriptText = null,
    int? Interval = null,
    string? Unit = null,
    string? AtTime = null,
    bool? IsEnabled = null,
    int? MaxRetries = null,
    int? RetryDelaySeconds = null,
    string? HashPolicy = null,
    string? TargetPath = null,
    string? DisplayName = null,
    string? Description = null,
    Dictionary<string, string>? Options = null
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

public record JobStateEntryDto(
    string JobName,
    string StateKey,
    string? StateValue,
    DateTime UpdatedAt
);

public record SetJobStateRequest(
    string? Value
);

public record ScheduleDefinitionDto(
    string Name,
    string Cron,
    string TimeZone,
    bool IsEnabled = true,
    string? DisplayName = null,
    string? Description = null,
    string? Options = null,
    string? CreatedBy = null,
    string? ModifiedBy = null,
    long Version = 1,
    string? TenantId = null,
    ScheduleId Id = default
);

public record CreateScheduleRequest(
    string Name,
    string Cron,
    string? TimeZone = null,
    bool IsEnabled = true,
    string? DisplayName = null,
    string? Description = null,
    Dictionary<string, string>? Options = null
);

public record UpdateScheduleRequest(
    string? Cron = null,
    string? TimeZone = null,
    bool? IsEnabled = null,
    string? DisplayName = null,
    string? Description = null,
    Dictionary<string, string>? Options = null
);

public record NotificationDefinitionDto(
    string Name,
    string ConnectionName,
    string? Recipient = null,
    bool IsEnabled = true,
    string? DisplayName = null,
    string? Description = null,
    string? Options = null,
    string? CreatedBy = null,
    string? ModifiedBy = null,
    long Version = 1,
    string? TenantId = null,
    NotificationId Id = default
);

public record CreateNotificationRequest(
    string Name,
    string ConnectionName,
    string? Recipient = null,
    bool IsEnabled = true,
    string? DisplayName = null,
    string? Description = null,
    Dictionary<string, string>? Options = null
);

public record UpdateNotificationRequest(
    string? ConnectionName = null,
    string? Recipient = null,
    bool? IsEnabled = null,
    string? DisplayName = null,
    string? Description = null,
    Dictionary<string, string>? Options = null
);

public record JobScheduleLinkDto(
    JobId JobId,
    ScheduleId ScheduleId,
    DateTime? LastRun = null,
    DateTime? NextRun = null,
    string? JobName = null,
    string? ScheduleName = null
);

public record JobNotificationLinkDto(
    JobId JobId,
    NotificationId NotificationId,
    string Trigger,
    string? JobName = null,
    string? NotificationName = null
);

public record LinkJobNotificationRequest(
    string Trigger
);

public record JobDependencyNodeDto(
    string Id,
    string Name,
    string? DisplayName,
    bool IsEnabled,
    DateTime? LastRun,
    DateTime? NextRun,
    bool IsCurrent
);

public record JobDependencyEdgeDto(
    string From,
    string To,
    string Type,
    string Detail
);

public record JobDependencyChainDto(
    string JobName,
    List<JobDependencyNodeDto> Nodes,
    List<JobDependencyEdgeDto> Edges,
    List<string> Upstream,
    List<string> Downstream,
    List<string> SharedSchedules
);
