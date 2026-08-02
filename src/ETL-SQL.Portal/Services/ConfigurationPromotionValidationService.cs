using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>Validates a Portal bootstrap against target logical state without executing it.</summary>
public sealed partial class ConfigurationPromotionValidationService(PortalDbContext db)
{
    public sealed record Finding(string Code, string Severity, string Resource, string Message);
    public sealed record Result(IReadOnlyList<Finding> Findings, IReadOnlyList<string> AppliedBindings)
    {
        public bool IsValid => Findings.All(f => f.Severity != "Error");
    }

    public async Task<Result> ValidateAsync(string script, IReadOnlyDictionary<string, string>? bindings,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new([new("PV001", "Error", "script", "Configuration bootstrap is empty.")], []);
        if (RawCredentialRegex().IsMatch(script))
            return new([new("PV002", "Error", "script", "Raw credential material is not accepted for validation; use placeholders, ENV, ENC, or SECRET references.")], []);

        var rebound = script;
        var applied = new List<string>();
        foreach (var binding in (bindings ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(binding.Key) || string.IsNullOrWhiteSpace(binding.Value))
                continue;
            if (rebound.Contains(binding.Key, StringComparison.Ordinal))
            {
                rebound = rebound.Replace(binding.Key, binding.Value, StringComparison.Ordinal);
                applied.Add(binding.Key);
            }
        }

        // Password placeholders are intentionally not target bindings and no secret value is needed
        // to validate catalog identities. Substitute a reference-shaped sentinel only in memory.
        rebound = PlaceholderRegex().Replace(rebound, "SECRET:promotion-validation");
        var parsed = new Parser(new Lexer(rebound).Tokenize(), rebound).Parse();
        var findings = parsed.Diagnostics
            .Where(d => string.Equals(d.Severity.ToString(), "Error", StringComparison.Ordinal))
            .Select(d => new Finding("PV003", "Error", $"line:{d.Line}", d.Message))
            .ToList();
        if (findings.Count > 0) return new(findings, applied);

        var statements = Flatten(parsed.Statements).ToArray();
        AddDuplicateFindings(statements, findings);
        foreach (var statement in statements)
        {
            ct.ThrowIfCancellationRequested();
            switch (statement)
            {
                case CreatePortalGroupStatement group:
                    var existingGroup = await db.Groups.AsNoTracking().SingleOrDefaultAsync(g => g.Name == group.Name, ct);
                    if (existingGroup is not null &&
                        (!Same(existingGroup.Description, group.Description)
                         || !Same(existingGroup.Provider, group.Provider ?? "Local")
                         || !Same(existingGroup.AdGroup, group.AdGroup)))
                        Collision(findings, "group", group.Name);
                    break;
                case CreatePortalUserStatement user:
                    var existingUser = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserName == user.Username, ct);
                    if (existingUser is not null)
                    {
                        var role = await (from ur in db.UserRoles
                                          join r in db.Roles on ur.RoleId equals r.Id
                                          where ur.UserId == existingUser.Id
                                          select r.Name).FirstOrDefaultAsync(ct);
                        if (!Same(existingUser.Email, user.Email) || !Same(role, user.Role)
                            || !Same(existingUser.Provider, user.Provider ?? "Local"))
                            Collision(findings, "user", user.Username);
                    }
                    break;
                case CreatePortalFolderStatement folder:
                    var existingFolder = await db.Folders.AsNoTracking().SingleOrDefaultAsync(f => f.Path == folder.Path, ct);
                    if (existingFolder is not null && folder.CatalogOwner is not null)
                    {
                        var owner = await db.Users.AsNoTracking().Where(u => u.Id == existingFolder.OwnerId)
                            .Select(u => u.UserName).SingleOrDefaultAsync(ct);
                        if (!Same(owner, folder.CatalogOwner)) Collision(findings, "folder", folder.Path);
                    }
                    break;
                case CreateConnectionStatement connection:
                    var existingConnection = await db.PortalSharedConnections.AsNoTracking()
                        .SingleOrDefaultAsync(c => c.Alias == connection.name, ct);
                    if (existingConnection is not null)
                    {
                        var desired = connection.options?.ToDictionary(pair => pair.Key,
                            pair => Literal(pair.Value), StringComparer.OrdinalIgnoreCase)
                            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var current = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(existingConnection.OptionsJson)
                            ?? new Dictionary<string, string>();
                        if (!Same(existingConnection.ConnectorType, connection.type) || !DictionaryEqual(current, desired))
                            Collision(findings, "connection", connection.name);
                    }
                    break;
                case PublishPortalReportStatement report:
                    var existingReport = await db.Reports.AsNoTracking().Include(r => r.Folder)
                        .SingleOrDefaultAsync(r => !r.IsDeleted && r.Name == report.ReportName && r.Folder.Path == report.FolderPath, ct);
                    if (existingReport is not null)
                    {
                        var owner = await db.Users.AsNoTracking().Where(u => u.Id == existingReport.CreatedBy)
                            .Select(u => u.UserName).SingleOrDefaultAsync(ct);
                        if (!Same(existingReport.ScriptPath, report.ScriptPath)
                            || !Same(existingReport.Description, report.Description)
                            || (report.CatalogOwner is not null && !Same(owner, report.CatalogOwner)))
                            Collision(findings, "report", $"{report.FolderPath}/{report.ReportName}");
                    }
                    break;
            }
        }
        foreach (var unused in (bindings ?? new Dictionary<string, string>()).Keys.Except(applied, StringComparer.OrdinalIgnoreCase))
            findings.Add(new("PV005", "Warning", $"binding:{unused}", "Binding did not match the bootstrap."));
        return new(findings.OrderBy(f => f.Code).ThenBy(f => f.Resource, StringComparer.OrdinalIgnoreCase).ToArray(), applied);
    }

    private static IEnumerable<Statement> Flatten(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is ExecuteRemoteBlockStatement block)
            {
                foreach (var nested in Flatten(block.Body.Statements)) yield return nested;
            }
            else if (statement is ExecutePushdownStatement push && !string.IsNullOrWhiteSpace(push.SqlText))
            {
                var parsed = new Parser(new Lexer(push.SqlText).Tokenize(), push.SqlText).Parse();
                foreach (var nested in Flatten(parsed.Statements)) yield return nested;
            }
            else yield return statement;
        }
    }

    private static void AddDuplicateFindings(IEnumerable<Statement> statements, List<Finding> findings)
    {
        var identities = statements.Select(statement => statement switch
        {
            CreatePortalGroupStatement s => $"group:{s.Name}",
            CreatePortalUserStatement s => $"user:{s.Username}",
            CreatePortalFolderStatement s => $"folder:{s.Path}",
            CreateConnectionStatement s => $"connection:{s.name}",
            PublishPortalReportStatement s => $"report:{s.FolderPath}/{s.ReportName}",
            _ => null
        }).Where(value => value is not null).Cast<string>();
        foreach (var duplicate in identities.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            findings.Add(new("PV004", "Error", duplicate.Key, "Bootstrap contains a duplicate logical identity."));
    }

    private static void Collision(List<Finding> findings, string kind, string identity) =>
        findings.Add(new("PV006", "Error", $"{kind}:{identity}", "Target logical identity exists with different configuration."));
    private static bool Same(string? left, string? right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
    private static string Literal(Expression expression) => expression is LiteralExpression literal ? literal.Value?.ToString() ?? "" : expression.ToSql();
    private static bool DictionaryEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && Same(pair.Value, value));

    [GeneratedRegex(@"\$\{[A-Za-z0-9_]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
    [GeneratedRegex(@"\b(?:PASSWORD|API_KEY|TOKEN|CLIENT_SECRET|PRIVATE_KEY)\s*=\s*'(?!SECRET:|ENC:|ENV\()[^'$][^']*'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RawCredentialRegex();
}
