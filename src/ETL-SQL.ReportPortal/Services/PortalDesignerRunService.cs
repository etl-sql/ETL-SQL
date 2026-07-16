using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Security;
using ETL_SQL.Data;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using Microsoft.EntityFrameworkCore;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.ReportPortal.Services;

/// <summary>
/// Executes portal-designer ad hoc previews under the logged-in user's execution identity.
/// The endpoint intentionally accepts only one read-only SELECT/set-query statement.
/// </summary>
public sealed class PortalDesignerRunService(
    IServiceProvider services,
    ETL_SQL.Common.ILogger logger,
    IConnectionCatalogProvider catalog,
    PortalDbContext db,
    PortalConfig portalConfig,
    AuditService audit)
{
    private const int RowCap = 100;
    private const int TimeoutSeconds = 15;
    private const int OperatorGrantMb = 64;

    public async Task<RunDesignerResponse> RunAsync(
        RunDesignerRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var selectedText = string.IsNullOrWhiteSpace(request.Selection) ? request.Script : request.Selection!;
        var statement = ParseSingleReadOnlyStatement(selectedText);
        var identity = await BuildIdentityAsync(user, cancellationToken);
        if (identity is null)
            throw new UnauthorizedAccessException("The current portal user could not be resolved for execution.");

        var script = await BuildExecutionScriptAsync(statement, request.ConnectionRef, identity, cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var sessionContext = new CliContext
        {
            Command = "run",
            BatchSize = RowCap,
            IsSilentMode = true,
            SessionId = Guid.NewGuid().ToString("N")
        };

        var start = DateTime.UtcNow;
        var session = new ExecutionSession(services, sessionContext, logger);
        var result = await session.ExecuteAsync(script, timeout.Token, "portal-designer-run", executionIdentity: identity);
        var elapsedMs = (long)(DateTime.UtcNow - start).TotalMilliseconds;
        var table = session.LastEvaluator?.LastResult;
        var queryFingerprint = FingerprintQuery(statement.ToSql());

        await audit.LogAsync(
            identity.EffectiveUserId,
            "AD_HOC_RUN",
            "Designer",
            NormalizeResourceId(request.ConnectionRef),
            $"Connection={NormalizeResourceId(request.ConnectionRef) ?? "(none)"}; Statement={statement.GetType().Name}; QueryHash={queryFingerprint}; Rows={table?.Rows.Count ?? 0}; ElapsedMs={elapsedMs}");

        if (!result.Success)
        {
            var message = result.Diagnostics.Count > 0
                ? string.Join("; ", result.Diagnostics.Select(d => d.Message))
                : "Designer run failed.";
            throw new InvalidOperationException(SecretRedactor.Redact(message));
        }

        table ??= new DataTable();
        return ToResponse(table, elapsedMs);
    }

    private static Statement ParseSingleReadOnlyStatement(string scriptText)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            throw new ArgumentException("Select a query to run.");

        var tokens = new Lexer(scriptText).Tokenize();
        var script = new CoreParser(tokens, scriptText).Parse();
        var error = script.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        if (error != null)
            throw new ArgumentException(error.Message);

        var statements = script.Statements.Where(s => s is not NoOpStatement).ToList();
        if (statements.Count != 1)
            throw new ArgumentException("Run selection accepts exactly one SELECT statement.");

        var statement = statements[0];
        if (ReadOnlyQueryPolicy.IsReadOnly(statement))
            return statement;

        throw new ArgumentException("Run selection is limited to read-only SELECT queries.");
    }

    private async Task<string> BuildExecutionScriptAsync(
        Statement statement,
        string? connectionRef,
        ExecutionIdentity identity,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SET MAX_LAST_RESULT_ROWS = {RowCap};");
        builder.AppendLine($"SET OPERATOR_MEMORY_GRANT = {OperatorGrantMb};");

        if (!string.IsNullOrWhiteSpace(connectionRef))
        {
            var alias = PortalDesignerSchemaService.NormalizeConnectionRef(connectionRef);
            var definition = await catalog.ResolveAsync(alias, identity, cancellationToken);
            builder.Append("CREATE CONNECTION ")
                .Append(QuoteIdentifier(alias))
                .Append(" AS ")
                .Append(definition.ConnectorType)
                .Append("('SHARED:")
                .Append(alias.Replace("'", "''", StringComparison.Ordinal))
                .AppendLine("');");
        }

        builder.AppendLine(statement.ToSql().Trim().TrimEnd(';') + ";");
        return builder.ToString();
    }

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

    private static RunDesignerResponse ToResponse(DataTable table, long elapsedMs)
    {
        var columns = table.ColumnNames;
        var rows = table.Rows
            .Take(RowCap)
            .Select(row => columns.ToDictionary<string, string, object?>(
                column => column,
                column => row[column],
                StringComparer.OrdinalIgnoreCase))
            .Cast<IReadOnlyDictionary<string, object?>>()
            .ToList();

        var capped = table.IsCapped || table.Rows.Count >= RowCap || table.TotalRowsMatched > RowCap;
        var rowCount = table.TotalRowsMatched > 0 ? table.TotalRowsMatched : table.Rows.Count;
        var message = capped
            ? $"Showing first {RowCap} rows; result was capped."
            : $"Returned {rows.Count} row{(rows.Count == 1 ? string.Empty : "s")}.";
        return new RunDesignerResponse(columns, rows, rowCount, capped, elapsedMs, message);
    }

    private static string? NormalizeResourceId(string? connectionRef)
        => string.IsNullOrWhiteSpace(connectionRef)
            ? null
            : PortalDesignerSchemaService.NormalizeConnectionRef(connectionRef);

    private static string QuoteIdentifier(string value)
        => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string FingerprintQuery(string query)
    {
        var bytes = Encoding.UTF8.GetBytes(query.Trim());
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
