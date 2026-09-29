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
    AuditService audit,
    IConnectionCatalogProvider catalog)
{
    private const int TimeoutSeconds = 30;
    private const int OperatorGrantMb = 128;

    /// <param name="parameters">
    /// Answers to the report's <c>INPUT</c> prompts, applied the way <c>--var</c> applies them: the
    /// value is seeded before the script runs, and <c>DECLARE</c> prefers an injected value to its
    /// own initial one. Prompting a reader and then previewing the defaults anyway would be a
    /// preview of a report nobody asked for.
    /// </param>
    public async Task<ReportManifest> BuildPreviewAsync(
        string scriptText,
        string? page,
        bool runEveryPage,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            throw new ArgumentException("Nothing to preview — the script is empty.");

        var identity = await BuildIdentityAsync(user, cancellationToken);
        if (identity is null)
            throw new UnauthorizedAccessException("The current portal user could not be resolved for execution.");

        // Preview runs exactly what a reader would run, so an undeclared shared connection fails here
        // too, with the line that fixes it, instead of previewing and then failing for everyone else.
        await RefuseUndeclaredSharedConnectionsAsync(scriptText, identity, cancellationToken);

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
        ApplyParameters(sessionContext, parameters);

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

        var manifest = await new ManifestBuilder(evaluator).BuildAsync(
            scriptText, runPages: runPages, deferPaginatedPages: !runEveryPage);

        await audit.LogAsync(
            identity.EffectiveUserId,
            "DESIGNER_PREVIEW",
            "Designer",
            null,
            $"Pages={manifest.Pages.Count}; Visuals={manifest.Visuals.Count}");

        return manifest;
    }

    /// <summary>
    /// Refuses a script that reads a shared catalog connection without declaring it.
    ///
    /// <para>This used to declare the alias ahead of the script instead. The preview then worked
    /// while the saved report failed with "Unknown source" for every reader, schedule, and
    /// subscription, because none of those paths adds a declaration. Preview now fails the same way
    /// a run would, and names the line that fixes both.</para>
    ///
    /// <para>Resolved one alias at a time through <see cref="IConnectionCatalogProvider"/> under the
    /// caller's own identity. An alias the catalog does not know, or will not give this caller, is
    /// left to the engine's own error.</para>
    /// </summary>
    private async Task RefuseUndeclaredSharedConnectionsAsync(
        string scriptText,
        ExecutionIdentity identity,
        CancellationToken cancellationToken)
    {
        var declarations = await MissingSharedDeclarationsAsync(scriptText, identity, cancellationToken);
        if (declarations.Count == 0) return;
        throw new InvalidOperationException(
            (declarations.Count == 1
                ? "This report reads a shared connection it does not declare, so it would fail for everyone who runs it. Add this line to the script:"
                : "This report reads shared connections it does not declare, so it would fail for everyone who runs it. Add these lines to the script:")
            + "\n" + string.Join("\n", declarations));
    }

    /// <summary>The <c>CREATE CONNECTION … ('SHARED:…')</c> lines a script needs and lacks.</summary>
    private async Task<IReadOnlyList<string>> MissingSharedDeclarationsAsync(
        string scriptText,
        ExecutionIdentity identity,
        CancellationToken cancellationToken)
    {
        var referenced = ReferencedSharedAliases(scriptText);
        var lines = new List<string>();
        if (referenced.Count == 0) return lines;

        foreach (var alias in referenced)
        {
            var normalized = PortalDesignerSchemaService.NormalizeConnectionRef(alias);
            SharedConnectionDefinition definition;
            try
            {
                definition = await catalog.ResolveAsync(normalized, identity, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                continue;
            }

            // The shape Studio writes (studio-shared-connections.ts), so the fix reads the same.
            lines.Add($"CREATE CONNECTION {normalized} AS {definition.ConnectorType.ToUpperInvariant()}"
                + $"('SHARED:{normalized.Replace("'", "''", StringComparison.Ordinal)}');");
        }

        return lines;
    }

    /// <summary>
    /// The connection aliases a script reads from but does not declare itself.
    ///
    /// <para>Qualified sources only, since an unqualified name is a table on a connection already
    /// in scope. A <c>#temp</c> table and a <c>&amp;dataset</c> are names the engine owns rather
    /// than connections, so neither is ever offered to the catalog.</para>
    /// </summary>
    internal static IReadOnlyList<string> ReferencedSharedAliases(string scriptText)
    {
        Script parsed;
        try
        {
            parsed = new ETL_SQL.Core.Parser.Parser(
                new ETL_SQL.Core.Parser.Lexer(scriptText).Tokenize(), scriptText).Parse();
        }
        catch
        {
            // A script the parser cannot read has no resolvable aliases, and the execution that
            // follows reports the syntax error far better than anything this could add.
            return [];
        }

        var declared = parsed.Statements.OfType<CreateConnectionStatement>()
            .Select(statement => statement.ConnectionName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return parsed.Statements
            .SelectMany(statement => statement.GetSourceTables())
            .Where(source => source.Contains('.', StringComparison.Ordinal))
            .Select(source => source.Split('.', 2)[0].Trim())
            .Where(alias => alias.Length > 0 && alias[0] != '#' && alias[0] != '&')
            .Where(alias => !declared.Contains(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    /// <summary>
    /// Seeds answered prompts onto the session, exactly as <c>--var</c> does: the same parser, the
    /// same <c>@</c>-prefixed keys, and the same precedence — <c>DECLARE</c> prefers an injected
    /// value to its own initial one.
    /// </summary>
    private static void ApplyParameters(CliContext context, IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null) return;
        foreach (var (name, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var key = name.StartsWith('@') ? name : "@" + name;
            context.Variables[key] = ETL_SQL.Core.Common.VariableOverrideValueParser.Parse(value);
        }
    }
}
