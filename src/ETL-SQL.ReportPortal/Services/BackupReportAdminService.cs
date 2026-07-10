using System.Globalization;
using System.Text;
using ETL_SQL.Core.Data;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Native replacement for samples/admin_operations/backup_and_report.etlsql: reads the backup
/// outcome markers that `etl-sql admin backup` records under job-state name 'admin-backup'
/// (last_backup_status / last_backup_at / last_backup_exit_code) and alerts when the last backup
/// failed, is missing, or is older than MaxBackupAgeHours. AlertOnly (default) sends nothing when
/// the last backup is recent and successful.
/// </summary>
public sealed class BackupReportAdminService(
    IServiceScopeFactory scopeFactory,
    PortalConfig config,
    IClusterLockStore lockStore,
    ILogger<BackupReportAdminService> log)
    : AdminDigestServiceBase(scopeFactory, config, lockStore, log)
{
    internal const string BackupJobStateName = "admin-backup";

    public override string ServiceName => "backup-report";

    protected override AdminServiceScheduleConfig Schedule => Config.AdminServices.BackupReport;

    protected override async Task<AdminDigestContent?> BuildAsync(IServiceProvider scope, CancellationToken ct)
    {
        var cfg = Config.AdminServices.BackupReport;
        var jobHistory = scope.GetRequiredService<IJobHistoryStore>();

        var status = await jobHistory.GetJobStateAsync(BackupJobStateName, "last_backup_status");
        var atText = await jobHistory.GetJobStateAsync(BackupJobStateName, "last_backup_at");
        var exitCode = await jobHistory.GetJobStateAsync(BackupJobStateName, "last_backup_exit_code");

        DateTime? lastBackupAt = DateTime.TryParse(
            atText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

        var maxAge = TimeSpan.FromHours(Math.Max(1, cfg.MaxBackupAgeHours));
        var problems = new List<string>();
        if (status == null && lastBackupAt == null)
            problems.Add("No backup outcome has ever been recorded (run 'etl-sql admin backup' on a schedule).");
        else
        {
            if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                problems.Add($"The last backup FAILED (status '{status ?? "unknown"}', exit code {exitCode ?? "?"}).");
            if (lastBackupAt == null)
                problems.Add("The last backup time is unreadable.");
            else if (DateTime.UtcNow - lastBackupAt.Value > maxAge)
                problems.Add($"The last backup is STALE: {lastBackupAt:u} is older than {maxAge.TotalHours:0}h.");
        }

        if (problems.Count == 0 && cfg.AlertOnly)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine(problems.Count == 0
            ? "ETL-SQL backup status: OK."
            : $"ETL-SQL backup status: {problems.Count} problem(s).");
        sb.AppendLine();
        foreach (var problem in problems)
            sb.AppendLine($"  - {problem}");
        if (problems.Count > 0) sb.AppendLine();
        sb.AppendLine($"Last recorded outcome: status={status ?? "none"}, at={atText ?? "never"}, exitCode={exitCode ?? "n/a"}.");

        var subject = problems.Count == 0
            ? "ETL-SQL backup status: OK"
            : $"ETL-SQL backup ALERT: {problems.Count} problem(s)";
        return new AdminDigestContent(subject, sb.ToString(),
            $"Problems={problems.Count}; LastStatus={status ?? "none"}; LastAt={atText ?? "never"}");
    }
}
