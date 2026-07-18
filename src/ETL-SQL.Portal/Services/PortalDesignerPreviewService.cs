using System.Security.Claims;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Security;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Portal.Data;
using ETL_SQL.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Compiles the current designer script into a self-contained <see cref="ReportManifest"/>
/// for the WYSIWYG preview pane. This is the server-side equivalent of the VS Code preview's
/// <c>ETL-SQL-Report build --format json</c>: the report script is fully evaluated under the
/// logged-in user's execution identity and each visual's data is materialised into the manifest,
/// so the browser can render it statically with report-runtime.js — no live session or serve.
/// No database transaction is held during evaluation.
/// </summary>
public sealed class PortalDesignerPreviewService(
    IServiceProvider services,
    ETL_SQL.Common.ILogger logger,
    PortalDbContext db,
    PortalConfig portalConfig,
    AuditService audit)
{
    private const int TimeoutSeconds = 30;
    private const int OperatorGrantMb = 128;

    public async Task<ReportManifest> BuildPreviewAsync(
        string scriptText,
        string? page,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            throw new ArgumentException("Nothing to preview — the script is empty.");

        var identity = await BuildIdentityAsync(user, cancellationToken);
        if (identity is null)
            throw new UnauthorizedAccessException("The current portal user could not be resolved for execution.");

        // Bound the preview: cap operator memory and let the linked timeout stop a runaway build.
        var script = $"SET OPERATOR_MEMORY_GRANT = {OperatorGrantMb};\n" + scriptText;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var sessionContext = new CliContext
        {
            Command = "build",
            IsSilentMode = true,
            SessionId = Guid.NewGuid().ToString("N")
        };

        var session = new ExecutionSession(services, sessionContext, logger);
        var result = await session.ExecuteAsync(script, timeout.Token, "portal-designer-preview", executionIdentity: identity);

        if (!result.Success)
        {
            var message = result.Diagnostics.Count > 0
                ? string.Join("; ", result.Diagnostics.Select(d => d.Message))
                : "Preview build failed.";
            throw new InvalidOperationException(SecretRedactor.Redact(message));
        }

        var evaluator = session.LastEvaluator
            ?? throw new InvalidOperationException("Preview produced no report context.");

        IReadOnlySet<string>? runPages = string.IsNullOrWhiteSpace(page)
            ? null
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { page! };

        var manifest = await new ManifestBuilder(evaluator).BuildAsync(scriptText, runPages: runPages);

        await audit.LogAsync(
            identity.EffectiveUserId,
            "DESIGNER_PREVIEW",
            "Designer",
            null,
            $"Pages={manifest.Pages.Count}; Visuals={manifest.Visuals.Count}");

        return manifest;
    }

    // Mirrors PortalDesignerRunService.BuildIdentityAsync: resolve the portal user's roles/groups so
    // row-level security and governance apply to the preview exactly as they do to an ad hoc run.
    private async Task<ExecutionIdentity?> BuildIdentityAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var claimIdentity = PortalDesignerSchemaService.BuildIdentity(user);
        if (claimIdentity.EffectiveUserId is not int userId)
            return claimIdentity;

        var portalUser = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (portalUser is null) return null;

        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           where ur.UserId == userId && r.Name != null
                           select r.Name!).ToListAsync(ct);
        var groups = await (from ug in db.UserGroups
                            join g in db.Groups on ug.GroupId equals g.Id
                            where ug.UserId == userId
                            select g.Name).ToListAsync(ct);

        var name = portalUser.UserName ?? claimIdentity.EffectiveUser ?? userId.ToString();
        return claimIdentity with
        {
            EffectiveUser = name,
            RealUser = name,
            IsAdmin = roles.Contains("Admin", StringComparer.OrdinalIgnoreCase) || user.IsInRole("Admin"),
            AdminBypassesRowLevelSecurity = portalConfig.Security.AdminBypassRowLevelSecurity,
            Roles = roles,
            Groups = groups
        };
    }
}
