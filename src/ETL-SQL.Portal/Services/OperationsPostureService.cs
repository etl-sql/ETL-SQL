using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Recovery and host-identity posture for an operator, read-only.
///
/// Everything reported here is <em>evidence</em>, never an action: backup custody, the restore
/// itself, and host enrolment stay outside the running Portal because they own material and
/// bootstrap authority the Portal deliberately does not have. What the Portal can do is notice that
/// the evidence is missing, stale, or inconsistent — and say what to do about it, since a finding
/// with no remedy just moves the problem.
/// </summary>
public sealed class OperationsPostureService(
    PortalDbContext db,
    PortalConfig config,
    IJobHistoryStore jobHistory,
    TimeProvider clock)
{
    internal const string BackupJobStateName = "admin-backup";
    internal const string RestoreJobStateName = "admin-restore";

    /// <summary>A drill older than this is reported as stale evidence.</summary>
    internal const int RestoreDrillMaxAgeDays = 90;

    /// <summary>A certificate inside this window is reported as expiring before it bites.</summary>
    internal const int CertificateExpiryWarningDays = 30;

    public async Task<OperationsPostureDto> BuildAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return new OperationsPostureDto(
            await BuildBackupAsync(now.UtcDateTime),
            await BuildRestoreDrillAsync(now.UtcDateTime),
            await BuildHostEnrollmentAsync(now, ct));
    }

    private async Task<BackupPostureDto> BuildBackupAsync(DateTime now)
    {
        var status = await jobHistory.GetJobStateAsync(BackupJobStateName, "last_backup_status");
        var atText = await jobHistory.GetJobStateAsync(BackupJobStateName, "last_backup_at");
        var exitCode = await jobHistory.GetJobStateAsync(BackupJobStateName, "last_backup_exit_code");
        var lastBackup = ParseUtc(atText);

        var maxAgeHours = Math.Max(1, config.AdminServices.BackupReport.MaxBackupAgeHours);
        var ageHours = lastBackup is DateTime taken ? (int)Math.Max(0, (now - taken).TotalHours) : (int?)null;
        var succeeded = string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);
        var fresh = succeeded && ageHours is int age && age <= maxAgeHours;

        var findings = new List<string>();
        if (status is null && lastBackup is null)
            findings.Add("No backup has ever been recorded.");
        else
        {
            if (!succeeded) findings.Add($"The last backup did not succeed (status '{status ?? "unknown"}').");
            if (lastBackup is null) findings.Add("The last backup time is unreadable.");
            else if (ageHours > maxAgeHours)
                findings.Add($"The last backup is {ageHours}h old, beyond the {maxAgeHours}h freshness policy.");
        }

        return new BackupPostureDto(
            EverRun: status is not null || lastBackup is not null,
            status, lastBackup, ageHours, exitCode, fresh, maxAgeHours, findings,
            Remediation: findings.Count == 0
                ? "No action needed."
                : "Run 'etl-sql admin backup' on the host, on a schedule. Backup custody stays outside "
                  + "the Portal, so this reports the recorded outcome rather than taking one.");
    }

    private async Task<RestoreDrillPostureDto> BuildRestoreDrillAsync(DateTime now)
    {
        var mode = await jobHistory.GetJobStateAsync(RestoreJobStateName, "last_restore_mode");
        var status = await jobHistory.GetJobStateAsync(RestoreJobStateName, "last_restore_status");
        var atText = await jobHistory.GetJobStateAsync(RestoreJobStateName, "last_restore_at");
        var problemsText = await jobHistory.GetJobStateAsync(RestoreJobStateName, "last_restore_problems");
        var lastDrill = ParseUtc(atText);
        var ageDays = lastDrill is DateTime drilled ? (int)Math.Max(0, (now - drilled).TotalDays) : (int?)null;

        var findings = new List<string>();
        if (lastDrill is null)
        {
            // The failure mode worth naming: a backup that has never been read back is untested.
            findings.Add("No restore or validation has ever been recorded — no archive has been proven readable.");
        }
        else
        {
            if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                findings.Add($"The last restore drill did not succeed (status '{status ?? "unknown"}').");
            if (ageDays > RestoreDrillMaxAgeDays)
                findings.Add($"The last restore drill was {ageDays} days ago, beyond {RestoreDrillMaxAgeDays} days.");
        }

        return new RestoreDrillPostureDto(
            EverRun: lastDrill is not null || status is not null,
            mode, status, lastDrill, ageDays,
            int.TryParse(problemsText, out var problems) ? problems : null,
            findings,
            Remediation: findings.Count == 0
                ? "No action needed."
                : "Run 'etl-sql admin restore --validate --report' against the most recent archive. "
                  + "The restore itself stays outside the Portal; only its outcome is recorded here.");
    }

    private async Task<HostEnrollmentPostureDto> BuildHostEnrollmentAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var status = new EnterpriseEnrollmentStore().GetStatus();
        var enrollment = status.Enrollment;

        var machine = enrollment is null
            ? null
            : await db.Set<PolicyMachineEntity>()
                .AsNoTracking()
                .FirstOrDefaultAsync(entity => entity.MachineId == enrollment.MachineId, ct);

        var certificate = BuildCertificatePosture(enrollment?.ClientCertificateThumbprint, machine, now);

        var findings = new List<string>();
        if (enrollment is null)
        {
            findings.Add("This host is not enrolled.");
        }
        else
        {
            if (machine is null)
            {
                // Each side looks fine alone; only comparing them shows the host cannot be governed.
                findings.Add("The host is enrolled but its machine id is not registered in the Portal.");
            }
            else
            {
                if (machine.Revoked) findings.Add("The Portal registration for this machine is revoked.");
                if (!string.Equals(machine.Tenant, enrollment.Tenant, StringComparison.OrdinalIgnoreCase))
                    findings.Add($"Tenant mismatch: host '{enrollment.Tenant}', Portal '{machine.Tenant}'.");
                if (!string.IsNullOrWhiteSpace(machine.EnrollmentId)
                    && !string.Equals(machine.EnrollmentId, enrollment.EnrollmentId, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add("Enrollment id mismatch — the identity may have been reassigned or copied.");
                }
            }

            if (certificate.ThumbprintMatches == false)
                findings.Add("The host's client certificate is not the one the Portal expects.");
            if (certificate.Expired)
                findings.Add("The host's client certificate has expired.");
            else if (certificate.ExpiringSoon)
                findings.Add($"The host's client certificate expires in {certificate.DaysUntilExpiry} day(s).");
        }

        return new HostEnrollmentPostureDto(
            HostEnrolled: enrollment is not null,
            enrollment?.MachineId,
            enrollment?.Tenant,
            Environment.GetEnvironmentVariable("ETLSQL_ENV") ?? "default",
            RegisteredInPortal: machine is not null,
            Revoked: machine?.Revoked ?? false,
            machine?.RegisteredAtUtc,
            machine?.LastSeenAtUtc,
            Consistent: enrollment is not null && machine is not null && findings.Count == 0,
            certificate,
            findings,
            Remediation: findings.Count == 0
                ? "No action needed."
                : "Enrollment and unenrollment run on the host ('etl-sql enterprise enroll' / 'status' / "
                  + "'unenroll'): they own an OS-protected bootstrap that is deliberately outside "
                  + "lower-authority Portal configuration. Register or revoke the machine from Policy "
                  + "Authority; renew the certificate on the host.");
    }

    private HostCertificatePostureDto BuildCertificatePosture(
        string? hostThumbprint, PolicyMachineEntity? machine, DateTimeOffset now)
    {
        var expected = machine?.ClientCertificateThumbprint;
        var hostHas = !string.IsNullOrWhiteSpace(hostThumbprint);
        var portalExpects = !string.IsNullOrWhiteSpace(expected);

        bool? matches = hostHas && portalExpects
            ? string.Equals(Normalize(hostThumbprint), Normalize(expected), StringComparison.OrdinalIgnoreCase)
            : null;

        var notAfter = TryGetCertificateExpiry(hostThumbprint);
        var daysUntil = notAfter is DateTimeOffset expiry ? (int)Math.Floor((expiry - now).TotalDays) : (int?)null;

        return new HostCertificatePostureDto(
            hostHas, portalExpects, matches, notAfter, daysUntil,
            Expired: daysUntil is int expired && expired < 0,
            ExpiringSoon: daysUntil is int soon && soon >= 0 && soon <= CertificateExpiryWarningDays);
    }

    private static string Normalize(string? thumbprint) =>
        (thumbprint ?? "").Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>
    /// Reads the certificate's expiry from the local store. Absence is reported as unknown rather
    /// than surfaced as a store error — a diagnostic must not fail because a diagnostic failed.
    /// </summary>
    private static DateTimeOffset? TryGetCertificateExpiry(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) return null;
        var normalized = Normalize(thumbprint);

        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly);
                foreach (var certificate in store.Certificates)
                {
                    using (certificate)
                    {
                        if (string.Equals(certificate.Thumbprint, normalized, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                certificate.GetCertHashString(HashAlgorithmName.SHA256), normalized,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                        }
                    }
                }
            }
            catch
            {
                // Absent or unreadable store: reported as unknown expiry.
            }
        }

        return null;
    }

    private static DateTime? ParseUtc(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
