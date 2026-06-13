using System.Text;
using ETL_SQL.ReportPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// P1.7: generates the portal's declarative configuration as a readable, replayable `.etlsql`
/// bootstrap script in dependency order (groups → users → memberships → folders → ACLs → SMTP →
/// reports → dataset metadata/ACLs → subscriptions → alerts), using logical names rather than
/// database ids. Secrets are never exported: password-bearing statements emit
/// <c>${...}</c> placeholders collected into a requirements header (P1.8), and every resource
/// that cannot be emitted is listed explicitly in the trailing summary — nothing is silently
/// omitted. Runtime/security artifacts (hashes, tokens, sessions, history, audit rows, snapshots,
/// dataset caches) are configuration non-goals and are listed as runtime-only.
/// </summary>
public sealed class ConfigurationExportService(PortalDbContext db)
{
    public sealed record ExportResult(string Script, IReadOnlyList<string> RequiredSecrets,
        IReadOnlyList<string> Skipped, IReadOnlyList<string> Emitted);

    private static readonly string[] RuntimeOnly =
    [
        "password hashes / encrypted credentials (placeholders emitted instead)",
        "refresh tokens, sessions, share links, embed tokens (security capabilities are never configuration)",
        "job execution history, audit rows, report snapshots, cached dataset parquet files",
        "favorites and saved views (personal state)"
    ];

    public async Task<ExportResult> GenerateAsync(CancellationToken ct = default)
    {
        var emitted = new List<string>();
        var skipped = new List<string>();
        var secrets = new List<string>();
        var body = new StringBuilder();

        // ── Groups ────────────────────────────────────────────────────────────
        var groups = await db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
        AppendSection(body, "Groups");
        foreach (var g in groups)
        {
            var options = new List<string>();
            if (!string.IsNullOrWhiteSpace(g.Description)) options.Add($"DESCRIPTION = {Q(g.Description)}");
            if (!string.IsNullOrWhiteSpace(g.Provider) && g.Provider != "Local") options.Add($"PROVIDER = {Q(g.Provider)}");
            if (!string.IsNullOrWhiteSpace(g.AdGroup)) options.Add($"AD_GROUP = {Q(g.AdGroup)}");
            body.AppendLine(options.Count == 0
                ? $"    CREATE GROUP {Q(g.Name)};"
                : $"    CREATE GROUP {Q(g.Name)} WITH ({string.Join(", ", options)});");
        }
        emitted.Add($"{groups.Count} group(s)");

        // ── Users ─────────────────────────────────────────────────────────────
        var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(ct);
        var roleByUser = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, r.Name }).ToListAsync(ct);
        AppendSection(body, "Users (passwords are never exported — supply each ${...} secret at import)");
        foreach (var u in users)
        {
            var role = roleByUser.FirstOrDefault(x => x.UserId == u.Id)?.Name ?? "Viewer";
            var isLdap = string.Equals(u.Provider, "LDAP", StringComparison.OrdinalIgnoreCase);
            var options = new List<string> { $"EMAIL = {Q(u.Email ?? $"{u.UserName}@example.invalid")}" };
            if (!isLdap)
            {
                var placeholder = $"PORTAL_USER_{Placeholder(u.UserName!)}_PASSWORD";
                secrets.Add(placeholder);
                options.Add($"PASSWORD = '${{{placeholder}}}'");
            }
            options.Add($"ROLE = {role}");
            if (!string.IsNullOrWhiteSpace(u.FirstName)) options.Add($"FIRST_NAME = {Q(u.FirstName)}");
            if (!string.IsNullOrWhiteSpace(u.LastName)) options.Add($"LAST_NAME = {Q(u.LastName)}");
            if (isLdap) options.Add("PROVIDER = 'LDAP'");
            body.AppendLine($"    CREATE USER {Q(u.UserName!)} WITH ({string.Join(", ", options)});");
            if (!u.IsActive)
                body.AppendLine($"    ALTER USER {Q(u.UserName!)} SET DISABLE;");
        }
        emitted.Add($"{users.Count} user(s)");

        // ── Group memberships ────────────────────────────────────────────────
        var memberships = await (
            from ug in db.UserGroups.AsNoTracking()
            join u in db.Users on ug.UserId equals u.Id
            join g in db.Groups on ug.GroupId equals g.Id
            orderby g.Name, u.UserName
            select new { Username = u.UserName!, Group = g.Name }).ToListAsync(ct);
        AppendSection(body, "Group memberships");
        foreach (var m in memberships)
            body.AppendLine($"    ADD USER {Q(m.Username)} TO GROUP {Q(m.Group)};");
        emitted.Add($"{memberships.Count} group membership(s)");

        // ── Folders (parents before children) ────────────────────────────────
        var folders = await db.Folders.AsNoTracking().OrderBy(f => f.Path).ToListAsync(ct);
        AppendSection(body, "Folders");
        foreach (var f in folders.OrderBy(f => f.Path.Count(c => c == '/')).ThenBy(f => f.Path))
            body.AppendLine($"    CREATE FOLDER {Q(f.Path)};");
        emitted.Add($"{folders.Count} folder(s)");

        // ── Folder ACLs ───────────────────────────────────────────────────────
        var folderAcls = await (
            from a in db.FolderAcls.AsNoTracking()
            join f in db.Folders on a.FolderId equals f.Id
            join g in db.Groups on a.GroupId equals g.Id
            orderby f.Path, g.Name
            select new { f.Path, Group = g.Name, a.Permission }).ToListAsync(ct);
        AppendSection(body, "Folder permissions");
        foreach (var a in folderAcls)
            body.AppendLine($"    GRANT {a.Permission.ToString().ToUpperInvariant()} ON FOLDER {Q(a.Path)} TO GROUP {Q(a.Group)};");
        emitted.Add($"{folderAcls.Count} folder ACL(s)");

        // ── SMTP connections ──────────────────────────────────────────────────
        var smtp = await db.SmtpConnections.AsNoTracking().OrderBy(s => s.Alias).ToListAsync(ct);
        AppendSection(body, "SMTP connections (credentials are never exported)");
        foreach (var s in smtp)
        {
            var options = new List<string> { $"HOST = {Q(s.Host)}", $"PORT = {s.Port}" };
            if (!string.IsNullOrWhiteSpace(s.Username)) options.Add($"USERNAME = {Q(s.Username)}");
            if (!string.IsNullOrEmpty(s.EncryptedPassword))
            {
                var placeholder = $"SMTP_{Placeholder(s.Alias)}_PASSWORD";
                secrets.Add(placeholder);
                options.Add($"PASSWORD = '${{{placeholder}}}'");
            }
            if (!string.IsNullOrWhiteSpace(s.FromAddress)) options.Add($"FROM_ADDRESS = {Q(s.FromAddress)}");
            options.Add($"USE_SSL = {(s.UseSsl ? "TRUE" : "FALSE")}");
            body.AppendLine($"    CREATE SMTP CONNECTION {Q(s.Alias)} WITH ({string.Join(", ", options)});");
        }
        emitted.Add($"{smtp.Count} SMTP connection(s)");

        // ── Reports (publication references — script files travel separately, P1.10) ─
        var reports = await db.Reports.AsNoTracking()
            .Include(r => r.Folder)
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.Folder!.Path).ThenBy(r => r.Name)
            .ToListAsync(ct);
        AppendSection(body, "Reports (copy the referenced .rptsql script files before replay — see the export manifest)");
        foreach (var r in reports)
        {
            var withClause = string.IsNullOrWhiteSpace(r.Description)
                ? ""
                : $" WITH (DESCRIPTION = {Q(r.Description)})";
            body.AppendLine($"    PUBLISH REPORT {Q(r.Name)} FROM {Q(r.ScriptPath)} IN FOLDER {Q(r.Folder!.Path)}{withClause};");
        }
        emitted.Add($"{reports.Count} report publication(s)");

        // ── Dataset metadata + ACLs (datasets materialize when their report runs) ─
        var datasets = await db.Datasets.AsNoTracking()
            .Include(d => d.Acls).ThenInclude(a => a.Group)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);
        AppendSection(body, "Dataset metadata and grants (apply after each dataset first materializes)");
        foreach (var d in datasets)
        {
            if (string.IsNullOrWhiteSpace(d.FolderPath))
            {
                skipped.Add($"dataset '{d.Name}': no folder path — published datasets must be re-published from a portable export (PUBLISH DATASET)");
                continue;
            }
            var sets = new List<string> { $"ACCESS = {Q(d.AccessLevel.ToString())}" };
            if (!string.IsNullOrWhiteSpace(d.Ttl)) sets.Add($"TTL = {Q(d.Ttl)}");
            body.AppendLine($"    ALTER DATASET {Q(d.Name)} IN FOLDER {Q(d.FolderPath)} SET {string.Join(", ", sets)};");
            foreach (var acl in d.Acls.OrderBy(a => a.Group.Name))
                body.AppendLine($"    GRANT {acl.Permission.ToString().ToUpperInvariant()} ON DATASET {Q(d.Name)} IN FOLDER {Q(d.FolderPath)} TO GROUP {Q(acl.Group.Name)};");
        }
        emitted.Add($"{datasets.Count} dataset metadata definition(s)");

        // ── Subscriptions ─────────────────────────────────────────────────────
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Include(s => s.Report).ThenInclude(r => r.Folder)
            .Include(s => s.User)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        AppendSection(body, "Subscriptions");
        foreach (var s in subscriptions)
        {
            var label = s.Name ?? $"subscription {s.Id}";
            if (s.Format is not (SubscriptionFormat.PDF or SubscriptionFormat.CSV))
            {
                skipped.Add($"subscription '{label}': FORMAT {s.Format} has no scripted CREATE SUBSCRIPTION form yet");
                continue;
            }
            if (s.DeliverOnRefresh || string.IsNullOrWhiteSpace(s.Schedule))
            {
                skipped.Add($"subscription '{label}': deliver-on-refresh subscriptions are not yet scriptable");
                continue;
            }
            if (s.Recipients.Contains(';') || s.Recipients.Contains(','))
            {
                skipped.Add($"subscription '{label}': multiple recipients require one CREATE SUBSCRIPTION per recipient");
                continue;
            }

            body.AppendLine($"    CREATE SUBSCRIPTION {Q(label)}");
            body.AppendLine($"        FOR REPORT {Q($"{s.Report.Folder!.Path}/{s.Report.Name}")}");
            body.AppendLine($"        DELIVER TO {Q(s.Recipients)}");
            body.AppendLine($"        SCHEDULE {Q(s.Schedule!)}");
            body.AppendLine($"        FORMAT {s.Format.ToString().ToUpperInvariant()}");
            body.Append($"        AT {s.SmtpAlias}");
            var parameters = DeserializeParameters(s.ParametersJson);
            if (parameters is { Count: > 0 })
            {
                body.AppendLine();
                body.AppendLine("        PARAMETERS (");
                body.AppendLine(string.Join(",\n", parameters.Select(p => $"            @{p.Key} = {Q(p.Value)}")));
                body.Append("        )");
            }
            body.AppendLine(";");
            if (!s.IsActive)
                skipped.Add($"subscription '{label}': exported, but it is currently DISABLED — disable it again after import (ALTER SUBSCRIPTION <id> SET DISABLE)");
        }
        emitted.Add($"{subscriptions.Count} subscription(s) considered");

        // ── Alerts (definition-only metadata, P0.5) ──────────────────────────
        var alerts = await db.ReportAlerts.AsNoTracking()
            .Include(a => a.Report)
            .OrderBy(a => a.Report.Name).ThenBy(a => a.Name)
            .ToListAsync(ct);
        AppendSection(body, "Alerts (definition-only metadata)");
        foreach (var a in alerts)
        {
            body.Append($"    CREATE ALERT {Q(a.Name)} FOR REPORT {Q(a.Report.Name)} WHEN VISUAL {Q(a.VisualName)} {a.Operator} {a.Threshold}");
            if (!string.IsNullOrWhiteSpace(a.Recipient)) body.Append($" DELIVER TO {Q(a.Recipient)}");
            if (!string.IsNullOrWhiteSpace(a.SmtpAlias)) body.Append($" AT {a.SmtpAlias}");
            body.AppendLine(";");
        }
        emitted.Add($"{alerts.Count} alert(s)");

        // ── Scheduled refresh jobs: need an Orchestrator connection alias at import ─
        var refreshJobs = await db.DatasetJobs.AsNoTracking().Include(j => j.Report).ToListAsync(ct);
        foreach (var j in refreshJobs)
            skipped.Add($"refresh job for report '{j.Report.Name}': re-create with CREATE REFRESH JOB FOR REPORT {Q(j.Report.Name)} SCHEDULE '<cron>' AT <orchestrator-alias>");

        skipped.Add("portal settings (JWT/dataset keys, Orchestrator API key/URL, branding): provisioned via configuration files, not script — see the administrators guide");

        return new ExportResult(ComposeScript(body, secrets, emitted, skipped), secrets, skipped, emitted);
    }

    private static string ComposeScript(
        StringBuilder body, List<string> secrets, List<string> emitted, List<string> skipped)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ============================================================================");
        sb.AppendLine("-- ETL-SQL Report Portal configuration bootstrap (EXPORT PORTAL CONFIGURATION)");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:o}   Format: 1");
        sb.AppendLine("-- Replays through an admin REPORTPORTAL connection. Review before executing.");
        sb.AppendLine("-- This script reconstructs configuration only — report scripts, dataset caches,");
        sb.AppendLine("-- and snapshots are content, not configuration (copy/publish them separately).");
        sb.AppendLine("-- ============================================================================");
        if (secrets.Count > 0)
        {
            sb.AppendLine("--");
            sb.AppendLine("-- REQUIRED SECRETS — supply each before executing. Real secret material is never");
            sb.AppendLine("-- exported; every credential below is a ${...} placeholder you must replace with one of:");
            sb.AppendLine("--   • an environment reference:   ENV('NAME')        (no plaintext in this file; preferred)");
            sb.AppendLine("--   • an encrypted literal:       ENC:...            (USE PASSWORD = ... unlocks it at import)");
            sb.AppendLine("--   • a plaintext literal:        '...'              (least preferred — avoid committing)");
            sb.AppendLine("-- An unsubstituted ${...} placeholder is rejected at import before it reaches the portal.");
            sb.AppendLine("-- Placeholders:");
            foreach (var secret in secrets.Distinct().OrderBy(s => s))
                sb.AppendLine($"--   ${{{secret}}}");
        }
        sb.AppendLine();
        sb.AppendLine("-- CREATE CONNECTION portal AS REPORTPORTAL (");
        sb.AppendLine("--     HOST = '<portal-url>', USERNAME = '<admin>', PASSWORD = ENC:...");
        sb.AppendLine("-- );");
        sb.AppendLine();
        sb.AppendLine("EXECUTE portal BEGIN");
        sb.Append(body);
        sb.AppendLine("END;");
        sb.AppendLine();
        sb.AppendLine("-- ── Export summary ──────────────────────────────────────────────────────────");
        sb.AppendLine("-- Emitted:");
        foreach (var line in emitted) sb.AppendLine($"--   {line}");
        sb.AppendLine("-- Skipped / manual follow-up:");
        foreach (var line in skipped.Count == 0 ? ["(none)"] : skipped.Distinct())
            sb.AppendLine($"--   {line}");
        sb.AppendLine("-- Runtime-only (never exported as configuration):");
        foreach (var line in RuntimeOnly) sb.AppendLine($"--   {line}");
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder body, string title)
    {
        if (body.Length > 0) body.AppendLine();
        body.AppendLine($"    -- ── {title} ──");
    }

    private static Dictionary<string, string>? DeserializeParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Single-quoted ETL-SQL string literal with embedded quotes doubled.</summary>
    private static string Q(string value) => $"'{value.Replace("'", "''")}'";

    private static string Placeholder(string value) =>
        new([.. value.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_')]);
}
