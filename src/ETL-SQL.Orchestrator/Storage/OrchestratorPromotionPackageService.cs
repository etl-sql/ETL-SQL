using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;

namespace ETL_SQL.Orchestrator.Storage;

/// <summary>Secret-safe, provider-neutral export/import contract for eligible Orchestrator state.</summary>
public static partial class OrchestratorPromotionPackageService
{
    public const string SchemaVersion = "etl-sql.orchestrator-promotion/v1";

    public sealed record QualityFailureRecord(long SourceRunId, string? TargetTable, string ColumnName,
        string Rule, string Action, long FailureCount, string? Owner);

    public sealed record Package(
        string SchemaVersion,
        DateTimeOffset ExportedUtc,
        IReadOnlyList<JobDefinition> Jobs,
        IReadOnlyList<ScheduleDefinition> Schedules,
        IReadOnlyList<NotificationDefinition> Notifications,
        IReadOnlyList<JobScheduleLink> JobSchedules,
        IReadOnlyList<JobNotificationLink> JobNotifications,
        IReadOnlyList<JobHistoryEntry> QualityHistory,
        IReadOnlyList<QualityFailureRecord> QualityFailures,
        IReadOnlyList<LineageHistoryEntry> LineageAndTags,
        IReadOnlyList<string> RequiredSecretReferences);

    public sealed record ImportResult(int Jobs, int Schedules, int Notifications, int JobSchedules,
        int JobNotifications, int QualityRuns, int QualityFailures, int LineageEntries);
    public sealed record ValidationFinding(string Code, string Severity, string Resource, string Message);
    public sealed record ValidationResult(IReadOnlyList<ValidationFinding> Findings)
    {
        public bool IsValid => Findings.All(f => !string.Equals(f.Severity, "Error", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<Package> ExportAsync(
        IJobHistoryStore history,
        IJobCatalogStore catalog,
        ILineageCatalogStore lineage,
        int historyLimit = 10_000,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var jobs = (await history.GetAllJobsAsync()).OrderBy(j => j.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var schedules = (await catalog.GetSchedulesAsync(historyLimit)).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var notifications = (await catalog.GetNotificationsAsync(historyLimit)).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var jobSchedules = (await catalog.GetJobSchedulesAsync()).OrderBy(l => l.JobName, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.ScheduleName, StringComparer.OrdinalIgnoreCase).ToArray();
        var jobNotifications = (await catalog.GetJobNotificationsAsync()).OrderBy(l => l.JobName, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.NotificationName, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Trigger).ToArray();
        var runs = (await history.GetHistoryAsync(limit: historyLimit)).Where(r => r.EndTime.HasValue).OrderBy(r => r.StartTime).ThenBy(r => r.Id).ToArray();
        var failures = (await history.GetDataQualityFailuresAsync(historyLimit))
            .Select(f => new QualityFailureRecord(f.RunId, f.TargetTable, f.ColumnName, f.Rule, f.Action, f.FailureCount, f.Owner))
            .OrderBy(f => f.SourceRunId).ThenBy(f => f.ColumnName, StringComparer.OrdinalIgnoreCase).ToArray();
        var lineageRows = (await lineage.GetRecentLineageAsync(historyLimit)).OrderBy(l => l.RunAt).ThenBy(l => l.Id).ToArray();

        var inspectable = jobs.SelectMany(j => new[] { j.Script, j.TargetPath, j.Options })
            .Concat(notifications.SelectMany(n => new[] { n.ConnectionName, n.Recipient, n.Options }))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        if (inspectable.Any(value => RawCredentialRegex().IsMatch(value)))
            throw new InvalidOperationException("Orchestrator promotion export refused a raw credential literal; replace it with SECRET:name.");
        var requiredSecrets = inspectable.SelectMany(value => SecretReferenceRegex().Matches(value).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

        return new(SchemaVersion, DateTimeOffset.UtcNow, jobs, schedules, notifications,
            jobSchedules, jobNotifications, runs, failures, lineageRows, requiredSecrets);
    }

    public static async Task<ImportResult> ImportAsync(
        Package package,
        IJobHistoryStore history,
        IJobCatalogStore catalog,
        ILineageCatalogStore lineage,
        IReadOnlyDictionary<string, string>? bindings = null,
        CancellationToken ct = default)
    {
        if (!string.Equals(package.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported Orchestrator promotion schema '{package.SchemaVersion}'.");
        var map = bindings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Bind(string value) => map.TryGetValue(value, out var replacement) ? replacement : value;

        var validation = await ValidateAsync(package, history, catalog, map, ct);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                "Promotion import validation failed: " + string.Join("; ", validation.Findings
                    .Where(f => f.Severity == "Error").Select(f => $"{f.Resource}: {f.Message}")));

        // Identity is deployment-local: the package's ids belong to the source and are used only to
        // correlate its own links below. The target assigns its own on insert, so Id is cleared here.
        foreach (var schedule in package.Schedules)
        {
            ct.ThrowIfCancellationRequested();
            var desired = schedule with { Version = 1, Id = ScheduleId.None };
            var existing = await catalog.GetScheduleAsync(schedule.TenantId, schedule.Name);
            if (existing is null)
                await catalog.SaveScheduleAsync(desired);
        }
        foreach (var notification in package.Notifications)
        {
            var desired = notification with
            {
                ConnectionName = Bind(notification.ConnectionName),
                Version = 1,
                Id = NotificationId.None
            };
            var existing = await catalog.GetNotificationAsync(notification.TenantId, notification.Name);
            if (existing is null)
                await catalog.SaveNotificationAsync(desired);
        }
        foreach (var job in package.Jobs)
        {
            var desired = job with
            {
                TargetPath = job.TargetPath is null ? null : Bind(job.TargetPath),
                LastRun = null,
                NextRun = null,
                IsEnabled = false,
                Version = 1,
                Id = JobId.None
            };
            var existing = await history.GetJobAsync(job.TenantId, job.Name);
            if (existing is null)
                await history.SaveJobAsync(desired);
        }

        // Links are re-resolved against the target. The package's link rows carry source ids, which
        // are matched back to the package's own definitions — unambiguous, unlike a bare name — and
        // each definition is then looked up in the target within its own tenant.
        foreach (var link in package.JobSchedules)
        {
            var sourceJob = package.Jobs.FirstOrDefault(j => j.Id == link.JobId);
            var sourceSchedule = package.Schedules.FirstOrDefault(x => x.Id == link.ScheduleId);
            if (sourceJob is null || sourceSchedule is null) continue;
            var targetJob = await history.GetJobAsync(sourceJob.TenantId, sourceJob.Name);
            var targetSchedule = await catalog.GetScheduleAsync(sourceSchedule.TenantId, sourceSchedule.Name);
            if (targetJob is null || !targetJob.Id.IsAssigned
                || targetSchedule is null || !targetSchedule.Id.IsAssigned) continue;
            await catalog.AddJobScheduleAsync(targetJob.Id, targetSchedule.Id, link.NextRun);
        }
        foreach (var link in package.JobNotifications)
        {
            var sourceJob = package.Jobs.FirstOrDefault(j => j.Id == link.JobId);
            var sourceNotification = package.Notifications.FirstOrDefault(n => n.Id == link.NotificationId);
            if (sourceJob is null || sourceNotification is null) continue;
            var targetJob = await history.GetJobAsync(sourceJob.TenantId, sourceJob.Name);
            var targetNotification =
                await catalog.GetNotificationAsync(sourceNotification.TenantId, sourceNotification.Name);
            if (targetJob is null || !targetJob.Id.IsAssigned
                || targetNotification is null || !targetNotification.Id.IsAssigned) continue;
            await catalog.AddJobNotificationAsync(targetJob.Id, targetNotification.Id, link.Trigger);
        }

        var runIds = new Dictionary<long, long>();
        foreach (var run in package.QualityHistory)
            runIds[run.Id] = await history.ImportJobHistoryAsync(run);
        foreach (var failure in package.QualityFailures)
        {
            if (!runIds.TryGetValue(failure.SourceRunId, out var targetRun)) continue;
            await history.SaveJobDataQualityFailuresAsync(targetRun,
            [
                new DataQualityRuleFailureMetric(
                    failure.TargetTable, failure.ColumnName, failure.Rule, failure.Action,
                    failure.FailureCount, failure.Owner)
            ]);
        }

        var existingLineage = (await lineage.GetRecentLineageAsync(10_000)).Select(LineageKey).ToHashSet(StringComparer.Ordinal);
        var importedLineage = 0;
        foreach (var row in package.LineageAndTags)
        {
            var key = LineageKey(row);
            if (!existingLineage.Add(key)) continue;
            var entry = new LineageEntry(row.TargetTable, row.Operation)
            {
                TargetColumn = row.TargetColumn,
                SourceTables = row.SourceTables.ToList(),
                SourceColumns = row.SourceColumns?.ToList() ?? [],
                Metadata = new Dictionary<string, string>(row.Tags, StringComparer.OrdinalIgnoreCase),
                SourceFile = row.SourceFile,
                Line = row.Line,
                TransformationKind = Enum.TryParse<TransformationKind>(row.TransformationKind, out var kind) ? kind : TransformationKind.Unknown,
                TransformationExpression = row.TransformationExpression,
                FunctionsApplied = row.FunctionsApplied,
                DerivedFromDescriptions = row.DerivedFromDescriptions
            };
            await lineage.SaveLineageAsync([entry], row.JobName, row.ScriptPath is null ? null : Bind(row.ScriptPath), row.RunAt);
            importedLineage++;
        }

        return new(package.Jobs.Count, package.Schedules.Count, package.Notifications.Count,
            package.JobSchedules.Count, package.JobNotifications.Count, package.QualityHistory.Count,
            package.QualityFailures.Count, importedLineage);
    }

    public static async Task<ValidationResult> ValidateAsync(
        Package package,
        IJobHistoryStore history,
        IJobCatalogStore catalog,
        IReadOnlyDictionary<string, string>? bindings = null,
        CancellationToken ct = default)
    {
        var findings = new List<ValidationFinding>();
        if (!string.Equals(package.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            findings.Add(new("OP001", "Error", "package", $"Unsupported schema '{package.SchemaVersion}'."));
            return new(findings);
        }
        var map = bindings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Bind(string value) => map.TryGetValue(value, out var replacement) ? replacement : value;

        AddDuplicateFindings(package.Jobs.Select(j => j.Name), "job", findings);
        AddDuplicateFindings(package.Schedules.Select(s => s.Name), "schedule", findings);
        AddDuplicateFindings(package.Notifications.Select(n => n.Name), "notification", findings);

        foreach (var schedule in package.Schedules)
        {
            ct.ThrowIfCancellationRequested();
            var desired = schedule with { Version = 1, Id = ScheduleId.None };
            var existing = await catalog.GetScheduleAsync(schedule.TenantId, schedule.Name);
            if (existing is not null && existing with { Version = 1, Id = ScheduleId.None } != desired)
                findings.Add(new("OP003", "Error", $"schedule:{schedule.Name}", "A different target schedule already uses this name."));
        }
        foreach (var notification in package.Notifications)
        {
            var desired = notification with
            {
                ConnectionName = Bind(notification.ConnectionName),
                Version = 1,
                Id = NotificationId.None
            };
            var existing = await catalog.GetNotificationAsync(notification.TenantId, notification.Name);
            if (existing is not null && existing with { Version = 1, Id = NotificationId.None } != desired)
                findings.Add(new("OP003", "Error", $"notification:{notification.Name}", "A different target notification already uses this name."));
        }
        foreach (var job in package.Jobs)
        {
            var desired = job with
            {
                TargetPath = job.TargetPath is null ? null : Bind(job.TargetPath),
                LastRun = null,
                NextRun = null,
                IsEnabled = false,
                Version = 1,
                Id = JobId.None
            };
            var existing = await history.GetJobAsync(job.TenantId, job.Name);
            if (existing is not null
                && existing with { LastRun = null, NextRun = null, Version = 1, Id = JobId.None } != desired)
                findings.Add(new("OP003", "Error", $"job:{job.Name}", "A different target job already uses this name."));
        }

        // Availability is keyed by tenant and name together. A name alone would let one tenant's job
        // satisfy another tenant's link and import a cross-tenant attachment.
        static string Key(string? tenantId, string name) =>
            (string.IsNullOrWhiteSpace(tenantId) ? string.Empty : tenantId) + "\u0000" + name.ToLowerInvariant();

        var availableJobs = package.Jobs.Select(j => Key(j.TenantId, j.Name)).ToHashSet(StringComparer.Ordinal);
        availableJobs.UnionWith((await history.GetAllJobsAsync()).Select(j => Key(j.TenantId, j.Name)));
        var availableSchedules = package.Schedules.Select(x => Key(x.TenantId, x.Name)).ToHashSet(StringComparer.Ordinal);
        availableSchedules.UnionWith((await catalog.GetSchedulesAsync()).Select(x => Key(x.TenantId, x.Name)));
        var availableNotifications = package.Notifications.Select(n => Key(n.TenantId, n.Name)).ToHashSet(StringComparer.Ordinal);
        availableNotifications.UnionWith((await catalog.GetNotificationsAsync()).Select(n => Key(n.TenantId, n.Name)));
        foreach (var link in package.JobSchedules)
        {
            var sourceJob = package.Jobs.FirstOrDefault(j => j.Id == link.JobId);
            var sourceSchedule = package.Schedules.FirstOrDefault(x => x.Id == link.ScheduleId);
            var label = $"job-schedule:{link.JobName ?? link.JobId.ToString()}/{link.ScheduleName ?? link.ScheduleId.ToString()}";
            if (sourceJob is null || sourceSchedule is null)
            {
                findings.Add(new("OP004", "Error", label, "The link does not reference objects carried in this package."));
                continue;
            }
            if (!availableJobs.Contains(Key(sourceJob.TenantId, sourceJob.Name))
                || !availableSchedules.Contains(Key(sourceSchedule.TenantId, sourceSchedule.Name)))
                findings.Add(new("OP004", "Error", label, "The referenced job or schedule is absent."));
        }
        foreach (var link in package.JobNotifications)
        {
            var sourceJob = package.Jobs.FirstOrDefault(j => j.Id == link.JobId);
            var sourceNotification = package.Notifications.FirstOrDefault(n => n.Id == link.NotificationId);
            var label = $"job-notification:{link.JobName ?? link.JobId.ToString()}/{link.NotificationName ?? link.NotificationId.ToString()}";
            if (sourceJob is null || sourceNotification is null)
            {
                findings.Add(new("OP004", "Error", label, "The link does not reference objects carried in this package."));
                continue;
            }
            if (!availableJobs.Contains(Key(sourceJob.TenantId, sourceJob.Name))
                || !availableNotifications.Contains(Key(sourceNotification.TenantId, sourceNotification.Name)))
                findings.Add(new("OP004", "Error", label, "The referenced job or notification is absent."));
        }

        var inspectable = package.Jobs.SelectMany(j => new[] { j.Script, j.TargetPath, j.Options })
            .Concat(package.Notifications.SelectMany(n => new[] { n.ConnectionName, n.Recipient, n.Options }))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>();
        if (inspectable.Any(value => RawCredentialRegex().IsMatch(value)))
            findings.Add(new("OP005", "Error", "package", "Raw credential material is not importable; use SECRET:name."));

        return new(findings.OrderBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Resource, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static async Task WriteAsync(Package package, Stream destination, CancellationToken ct = default) =>
        await JsonSerializer.SerializeAsync(destination, package, JsonOptions, ct);

    public static async Task<Package> ReadAsync(Stream source, CancellationToken ct = default) =>
        await JsonSerializer.DeserializeAsync<Package>(source, JsonOptions, ct)
        ?? throw new InvalidDataException("The Orchestrator promotion package is empty or invalid.");

    private static string LineageKey(LineageHistoryEntry row) => string.Join("\u001f",
        row.RunAt.ToUniversalTime().ToString("O"), row.JobName, row.ScriptPath, row.TargetTable,
        row.TargetColumn, row.Operation, row.SourceFile, row.Line.ToString());

    private static void AddDuplicateFindings(IEnumerable<string> names, string kind, List<ValidationFinding> findings)
    {
        foreach (var duplicate in names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            findings.Add(new("OP002", "Error", $"{kind}:{duplicate.Key}", "The package contains a duplicate logical identity."));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [GeneratedRegex(@"\bSECRET:([A-Za-z0-9._-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretReferenceRegex();
    [GeneratedRegex(@"\b(?:PASSWORD|API_KEY|TOKEN|CLIENT_SECRET|PRIVATE_KEY)\s*=\s*'(?!SECRET:|ENC:)[^']+'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RawCredentialRegex();
}
