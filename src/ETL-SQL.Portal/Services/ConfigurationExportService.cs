using System.Text;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

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
        IReadOnlyList<string> Skipped, IReadOnlyList<string> Emitted,
        IReadOnlyList<ContentManifestItem> ContentManifest);

    /// <summary>A content artifact the bootstrap does NOT reconstruct (report script files,
    /// datasets) that must be copied or re-published separately — see P1.10.</summary>
    public sealed record ContentManifestItem(string Kind, string Logical, string? Source, string Action);

    private static readonly string[] RuntimeOnly =
    [
        "password hashes / encrypted credentials (placeholders emitted instead)",
        "refresh tokens, sessions, share links, embed tokens (security capabilities are never configuration)",
        "job execution history, audit rows, report snapshots, cached dataset parquet files",
        "policy-signing certificate thumbprints and private keys (host configuration / OS key store only)",
        "favorites and saved views (personal state)"
    ];

    public Task<ExportResult> GenerateAsync(CancellationToken ct = default) =>
        GenerateAsync(null, ct);

    public async Task<ExportResult> GenerateAsync(
        string? targetOrchestratorAlias,
        CancellationToken ct = default)
    {
        var emitted = new List<string>();
        var skipped = new List<string>();
        var secrets = new List<string>();
        var manifest = new List<ContentManifestItem>();
        var body = new StringBuilder();

        // ── Groups ────────────────────────────────────────────────────────────
        var groupCount = 0;
        AppendSection(body, "Groups");
        await foreach (var g in db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new { g.Name, g.Description, g.Provider, g.AdGroup })
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            groupCount++;
            var options = new List<string>();
            if (!string.IsNullOrWhiteSpace(g.Description)) options.Add($"DESCRIPTION = {Q(g.Description)}");
            if (!string.IsNullOrWhiteSpace(g.Provider) && g.Provider != "Local") options.Add($"PROVIDER = {Q(g.Provider)}");
            if (!string.IsNullOrWhiteSpace(g.AdGroup)) options.Add($"AD_GROUP = {Q(g.AdGroup)}");
            body.AppendLine(options.Count == 0
                ? $"    CREATE GROUP {Q(g.Name)};"
                : $"    CREATE GROUP {Q(g.Name)} WITH ({string.Join(", ", options)});");
        }
        emitted.Add($"{groupCount} group(s)");

        // ── Users ─────────────────────────────────────────────────────────────
        var roleByUser = (await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, r.Name }).ToListAsync(ct))
            .GroupBy(role => role.UserId)
            .ToDictionary(group => group.Key, group => group.First().Name);
        var userCount = 0;
        AppendSection(body, "Users (passwords are never exported — supply each ${...} secret at import)");
        await foreach (var u in db.Users.AsNoTracking()
            .OrderBy(u => u.UserName)
            .Select(u => new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.Provider,
                u.FirstName,
                u.LastName,
                u.IsActive
            })
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            userCount++;
            var role = roleByUser.GetValueOrDefault(u.Id) ?? "Viewer";
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
        emitted.Add($"{userCount} user(s)");

        // ── Group memberships ────────────────────────────────────────────────
        var membershipCount = 0;
        var memberships = (
            from ug in db.UserGroups.AsNoTracking()
            join u in db.Users on ug.UserId equals u.Id
            join g in db.Groups on ug.GroupId equals g.Id
            orderby g.Name, u.UserName
            select new { Username = u.UserName!, Group = g.Name }).AsAsyncEnumerable();
        AppendSection(body, "Group memberships");
        await foreach (var m in memberships.WithCancellation(ct))
        {
            membershipCount++;
            body.AppendLine($"    ADD USER {Q(m.Username)} TO GROUP {Q(m.Group)};");
        }
        emitted.Add($"{membershipCount} group membership(s)");

        // ── Folders (parents before children) ────────────────────────────────
        var folderCount = 0;
        AppendSection(body, "Folders");
        await foreach (var path in db.Folders.AsNoTracking()
            .OrderBy(f => f.Path)
            .Select(f => f.Path)
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            folderCount++;
            body.AppendLine($"    CREATE FOLDER {Q(path)};");
        }
        emitted.Add($"{folderCount} folder(s)");

        // ── Folder ACLs ───────────────────────────────────────────────────────
        var folderAclCount = 0;
        var folderAcls = (
            from a in db.FolderAcls.AsNoTracking()
            join f in db.Folders on a.FolderId equals f.Id
            join g in db.Groups on a.GroupId equals g.Id
            orderby f.Path, g.Name
            select new { f.Path, Group = g.Name, a.Permission }).AsAsyncEnumerable();
        AppendSection(body, "Folder permissions");
        await foreach (var a in folderAcls.WithCancellation(ct))
        {
            folderAclCount++;
            body.AppendLine($"    GRANT {a.Permission.ToString().ToUpperInvariant()} ON FOLDER {Q(a.Path)} TO GROUP {Q(a.Group)};");
        }
        emitted.Add($"{folderAclCount} folder ACL(s)");

        // ── SMTP connections ──────────────────────────────────────────────────
        var smtpCount = 0;
        AppendSection(body, "SMTP connections (credentials are never exported)");
        await foreach (var s in db.SmtpConnections.AsNoTracking()
            .OrderBy(s => s.Alias)
            .Select(s => new
            {
                s.Alias,
                s.Host,
                s.Port,
                s.Username,
                s.EncryptedPassword,
                s.FromAddress,
                s.UseSsl
            })
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            smtpCount++;
            var options = new List<string> { $"HOST = {Q(s.Host)}", $"PORT = {s.Port}" };
            if (!string.IsNullOrWhiteSpace(s.Username)) options.Add($"USERNAME = {Q(s.Username)}");
            if (!string.IsNullOrEmpty(s.EncryptedPassword))
            {
                var placeholder = $"SMTP_{Placeholder(s.Alias)}_PASSWORD";
                secrets.Add(placeholder);
                options.Add($"PASSWORD = '${{{placeholder}}}'");
            }
            if (!string.IsNullOrWhiteSpace(s.FromAddress)) options.Add($"DEFAULT_FROM = {Q(s.FromAddress)}");
            options.Add($"USE_SSL = {(s.UseSsl ? "TRUE" : "FALSE")}");
            body.AppendLine($"    CREATE CONNECTION {s.Alias} AS SMTP({string.Join(", ", options)});");
        }
        emitted.Add($"{smtpCount} SMTP connection(s)");

        // ── Reports (publication references — script files travel separately, P1.10) ─
        var reportCount = 0;
        var reports = db.Reports.AsNoTracking()
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.Folder!.Path).ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Name,
                r.Description,
                r.ScriptPath,
                FolderPath = r.Folder!.Path
            })
            .AsAsyncEnumerable();
        AppendSection(body, "Reports (copy the referenced .rptsql script files before replay — see the export manifest)");
        await foreach (var r in reports.WithCancellation(ct))
        {
            reportCount++;
            var withClause = string.IsNullOrWhiteSpace(r.Description)
                ? ""
                : $" WITH (DESCRIPTION = {Q(r.Description)})";
            body.AppendLine($"    PUBLISH REPORT {Q(r.Name)} FROM {Q(r.ScriptPath)} IN FOLDER {Q(r.FolderPath)}{withClause};");
            // The PUBLISH statement references a .rptsql path that must already exist at the target.
            manifest.Add(new ContentManifestItem(
                "ReportScript", $"{r.FolderPath}/{r.Name}", r.ScriptPath,
                "Copy this .rptsql file into the target portal's script root before replay."));
        }
        emitted.Add($"{reportCount} report publication(s)");

        // ── Dataset metadata + ACLs (datasets materialize when their report runs) ─
        var datasetCount = 0;
        var datasetAclsByDataset = (await (
            from acl in db.DatasetAcls.AsNoTracking()
            join g in db.Groups on acl.GroupId equals g.Id
            orderby g.Name
            select new { acl.DatasetId, Group = g.Name, acl.Permission }).ToListAsync(ct))
            .GroupBy(acl => acl.DatasetId)
            .ToDictionary(group => group.Key, group => group.OrderBy(acl => acl.Group).ToList());
        var datasets = db.Datasets.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.FolderPath,
                d.AccessLevel,
                d.Ttl
            })
            .AsAsyncEnumerable();
        AppendSection(body, "Dataset metadata and grants (apply after each dataset first materializes)");
        await foreach (var d in datasets.WithCancellation(ct))
        {
            datasetCount++;
            // A dataset's cached parquet is content (and at-rest-encrypted), never configuration:
            // it must be re-materialized by running its producing report, or re-published from a
            // portable EXPORT DATASET file.
            manifest.Add(new ContentManifestItem(
                "Dataset", d.Name, null,
                "Re-materialize by running its producing report, or PUBLISH DATASET from a portable export; this script only restores its metadata/grants."));

            if (string.IsNullOrWhiteSpace(d.FolderPath))
            {
                skipped.Add($"dataset '{d.Name}': no folder path — published datasets must be re-published from a portable export (PUBLISH DATASET)");
                continue;
            }
            var sets = new List<string> { $"ACCESS = {Q(d.AccessLevel.ToString())}" };
            if (!string.IsNullOrWhiteSpace(d.Ttl)) sets.Add($"TTL = {Q(d.Ttl)}");
            body.AppendLine($"    ALTER DATASET {Q(d.Name)} IN FOLDER {Q(d.FolderPath)} SET {string.Join(", ", sets)};");
            foreach (var acl in datasetAclsByDataset.GetValueOrDefault(d.Id) ?? [])
                body.AppendLine($"    GRANT {acl.Permission.ToString().ToUpperInvariant()} ON DATASET {Q(d.Name)} IN FOLDER {Q(d.FolderPath)} TO GROUP {Q(acl.Group)};");
        }
        emitted.Add($"{datasetCount} dataset metadata definition(s)");

        // ── Subscriptions ─────────────────────────────────────────────────────
        var subscriptionCount = 0;
        var subscriptions = db.Subscriptions.AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Format,
                s.DeliverOnRefresh,
                s.Schedule,
                s.Recipients,
                s.SmtpAlias,
                s.ParametersJson,
                s.IsActive,
                ReportName = s.Report.Name,
                FolderPath = s.Report.Folder.Path
            })
            .AsAsyncEnumerable();
        AppendSection(body, "Subscriptions");
        await foreach (var s in subscriptions.WithCancellation(ct))
        {
            subscriptionCount++;
            var label = s.Name ?? $"subscription {s.Id}";
            if (s.Format is not (SubscriptionFormat.PDF or SubscriptionFormat.CSV))
            {
                skipped.Add($"subscription '{label}': FORMAT {s.Format} has no scripted CREATE SUBSCRIPTION form yet");
                continue;
            }
            if (!s.DeliverOnRefresh && string.IsNullOrWhiteSpace(s.Schedule))
            {
                skipped.Add($"subscription '{label}': no schedule was stored");
                continue;
            }
            var recipients = SplitRecipients(s.Recipients);
            if (recipients.Count == 0)
            {
                skipped.Add($"subscription '{label}': no valid recipient was stored");
                continue;
            }

            foreach (var recipient in recipients)
            {
                var exportedName = recipients.Count == 1 ? label : $"{label} [{recipient}]";
                body.AppendLine($"    CREATE SUBSCRIPTION {Q(exportedName)}");
                body.AppendLine($"        FOR REPORT {Q($"{s.FolderPath}/{s.ReportName}")}");
                body.AppendLine($"        DELIVER TO {Q(recipient)}");
                if (s.DeliverOnRefresh)
                    body.AppendLine("        ON REFRESH");
                else
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
                if (!s.IsActive) body.Append(" DISABLE");
                body.AppendLine(";");
            }
        }
        emitted.Add($"{subscriptionCount} subscription(s) considered");

        // ── Alerts (definition-only metadata, P0.5) ──────────────────────────
        var alertCount = 0;
        var alerts = db.ReportAlerts.AsNoTracking()
            .OrderBy(a => a.Report.Name).ThenBy(a => a.Name)
            .Select(a => new
            {
                a.Name,
                a.VisualName,
                a.Operator,
                a.Threshold,
                a.Recipient,
                a.SmtpAlias,
                a.IsActive,
                ReportName = a.Report.Name,
                FolderPath = a.Report.Folder.Path
            })
            .AsAsyncEnumerable();
        AppendSection(body, "Alerts (definition-only metadata)");
        await foreach (var a in alerts.WithCancellation(ct))
        {
            alertCount++;
            var reportPath = $"{a.FolderPath}/{a.ReportName}";
            body.Append($"    CREATE ALERT {Q(a.Name)} FOR REPORT {Q(reportPath)} WHEN VISUAL {Q(a.VisualName)} {a.Operator} {a.Threshold}");
            if (!string.IsNullOrWhiteSpace(a.Recipient)) body.Append($" DELIVER TO {Q(a.Recipient)}");
            if (!string.IsNullOrWhiteSpace(a.SmtpAlias)) body.Append($" AT {a.SmtpAlias}");
            if (!a.IsActive) body.Append(" DISABLE");
            body.AppendLine(";");
        }
        emitted.Add($"{alertCount} alert(s)");

        // ── Scheduled refresh jobs ────────────────────────────────────────────
        var refreshJobCount = 0;
        var refreshJobs = db.DatasetJobs.AsNoTracking()
            .OrderBy(j => j.Report.Folder!.Path).ThenBy(j => j.Report.Name)
            .Select(j => new
            {
                j.RefreshInterval,
                ReportName = j.Report.Name,
                FolderPath = j.Report.Folder.Path
            })
            .AsAsyncEnumerable();
        AppendSection(body, "Scheduled report refresh jobs");
        await foreach (var j in refreshJobs.WithCancellation(ct))
        {
            if (string.IsNullOrWhiteSpace(targetOrchestratorAlias))
            {
                skipped.Add(
                    $"refresh job for report '{j.ReportName}': export again with a target Orchestrator alias");
                continue;
            }
            refreshJobCount++;
            body.AppendLine(
                $"    CREATE REFRESH JOB FOR REPORT {Q($"{j.FolderPath}/{j.ReportName}")} " +
                $"SCHEDULE {Q(j.RefreshInterval)} AT {targetOrchestratorAlias};");
        }
        emitted.Add($"{refreshJobCount} refresh job(s)");

        skipped.Add("portal settings (JWT/dataset keys, Orchestrator API key/URL, branding): provisioned via configuration files, not script — see the administrators guide");

        return new ExportResult(
            ComposeScript(body, secrets, emitted, skipped, manifest), secrets, skipped, emitted, manifest);
    }

    private static string ComposeScript(
        StringBuilder body, List<string> secrets, List<string> emitted, List<string> skipped,
        List<ContentManifestItem> manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ============================================================================");
        sb.AppendLine("-- ETL-SQL Portal configuration bootstrap (EXPORT PORTAL CONFIGURATION)");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:o}   Format: 1");
        sb.AppendLine("-- Replays through an admin PORTAL connection. Review before executing.");
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
        sb.AppendLine("-- CREATE CONNECTION portal AS PORTAL (");
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

        AppendContentManifest(sb, manifest);
        return sb.ToString();
    }

    /// <summary>Companion content manifest + recovery runbook (P1.10): this bootstrap reconstructs
    /// configuration only — the report scripts and datasets it references are content that must be
    /// copied or re-published separately, and exact-state recovery still uses the database/file
    /// backups.</summary>
    private static void AppendContentManifest(StringBuilder sb, List<ContentManifestItem> manifest)
    {
        sb.AppendLine();
        sb.AppendLine("-- ── Companion content manifest & recovery runbook ───────────────────────────");
        sb.AppendLine("-- This script reconstructs CONFIGURATION only. There are three recovery paths:");
        sb.AppendLine("--   1. Configuration (this script): the auditable clean-start path — replay against a");
        sb.AppendLine("--      fresh portal, supplying secrets, after the content below is in place.");
        sb.AppendLine("--   2. Content (this manifest): report .rptsql scripts and datasets — copy or");
        sb.AppendLine("--      re-publish them separately; the bootstrap only references and grants them.");
        sb.AppendLine("--   3. Exact-state disaster recovery: restore the portal and Orchestrator");
        sb.AppendLine("--      database/file backups — that path is unaffected by this export.");

        var scripts = manifest.Where(m => m.Kind == "ReportScript").ToList();
        sb.AppendLine($"-- Report scripts to copy into the target script root ({scripts.Count}):");
        foreach (var item in scripts.OrderBy(m => m.Logical))
            sb.AppendLine($"--   {item.Logical}  <=  {item.Source}");

        var contentDatasets = manifest.Where(m => m.Kind == "Dataset").ToList();
        sb.AppendLine($"-- Datasets to re-materialize or re-publish ({contentDatasets.Count}):");
        foreach (var item in contentDatasets.OrderBy(m => m.Logical))
            sb.AppendLine($"--   {item.Logical}");
        if (scripts.Count == 0 && contentDatasets.Count == 0)
            sb.AppendLine("--   (no report scripts or datasets — configuration-only portal)");
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

    private static List<string> SplitRecipients(string recipients) =>
        recipients.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Single-quoted ETL-SQL string literal with embedded quotes doubled.</summary>
    private static string Q(string value) => $"'{value.Replace("'", "''")}'";

    private static string Placeholder(string value) =>
        new([.. value.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_')]);
}
