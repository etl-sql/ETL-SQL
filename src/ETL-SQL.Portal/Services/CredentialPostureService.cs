using System.Text.Json;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Secrets and shared connections resolved against each other.
///
/// Both already had good admin pages, and neither could show the failure that actually happens: a
/// connection referencing a secret that was renamed, disabled, or never created. The secrets page
/// shows a healthy list of secrets; the connections page shows a healthy list of connections; the
/// break only exists in the join between them, and it surfaces the first time something runs.
///
/// Also joins the two facts an operator needs before touching either: when a secret was last
/// rotated, and whether a configuration export will demand it at the target.
///
/// <b>No secret value is read.</b> References are matched by name against the store's inventory —
/// resolving them would mean decrypting every secret to render a page.
/// </summary>
public sealed class CredentialPostureService(
    PortalDbContext db,
    ConfigurationExportService exporter)
{
    /// <summary>A secret older than this is flagged. Deliberately advisory: rotation cadence is an
    /// organizational policy, and a wrong number that blocks nothing is better than a false alarm.</summary>
    public const int RotationWarningDays = 365;

    public async Task<CredentialPostureDto> BuildAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var secrets = await db.PortalSecrets
            .AsNoTracking()
            .OrderBy(secret => secret.Name)
            .Select(secret => new
            {
                secret.Name,
                secret.Disabled,
                secret.CreatedAtUtc,
                secret.UpdatedAtUtc
            })
            .ToListAsync(ct);

        var connections = await db.PortalSharedConnections
            .AsNoTracking()
            .Include(connection => connection.Acls)
            .OrderBy(connection => connection.Alias)
            .ToListAsync(ct);

        var live = secrets
            .Where(secret => !secret.Disabled)
            .Select(secret => secret.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var connectionPosture = new List<ConnectionPostureDto>();
        var referencedBy = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var connection in connections)
        {
            var references = ExtractSecretReferences(connection);
            foreach (var reference in references)
            {
                if (!referencedBy.TryGetValue(reference, out var aliases))
                    referencedBy[reference] = aliases = [];
                aliases.Add(connection.Alias);
            }

            var unresolved = references.Where(reference => !live.Contains(reference)).ToArray();
            connectionPosture.Add(new ConnectionPostureDto(
                connection.Alias,
                connection.ConnectorType,
                connection.Disabled,
                references,
                unresolved,
                UsableWithoutGrant: connection.Acls.Count == 0,
                GrantedGroups: connection.Acls.Count,
                connection.LastVerifiedAtUtc,
                connection.LastUsedAtUtc,
                Healthy: !connection.Disabled && unresolved.Length == 0));
        }

        // Which secrets a promotion would demand at the target. Placeholders are emitted by name, so
        // this is the join between "what we hold" and "what the target must be given".
        var requiredForPromotion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var export = await exporter.GenerateAsync(null, ct);
            foreach (var required in export.RequiredSecrets)
                requiredForPromotion.Add(required.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The export is a convenience join here, not the subject: a failure to build it must not
            // take the credential view down with it.
        }

        var secretPosture = secrets
            .Select(secret =>
            {
                var age = (int)Math.Max(0, (now - secret.UpdatedAtUtc).TotalDays);
                var users = referencedBy.TryGetValue(secret.Name, out var aliases)
                    ? aliases.OrderBy(alias => alias, StringComparer.Ordinal).ToArray()
                    : [];
                return new SecretPostureDto(
                    secret.Name,
                    secret.Disabled,
                    secret.CreatedAtUtc,
                    secret.UpdatedAtUtc,
                    age,
                    RotationOverdue: age > RotationWarningDays,
                    users,
                    RequiredForPromotion: requiredForPromotion.Contains(secret.Name)
                        || requiredForPromotion.Any(required =>
                            required.Contains(secret.Name, StringComparison.OrdinalIgnoreCase)),
                    // Nothing references it and no promotion needs it: a candidate for removal, and
                    // worth naming because an unused credential is still a credential.
                    Orphaned: users.Length == 0);
            })
            .ToList();

        var findings = new List<string>();
        var broken = connectionPosture.Where(connection => connection.UnresolvedSecrets.Count > 0).ToList();
        if (broken.Count > 0)
        {
            findings.Add(
                $"{broken.Count} connection(s) reference a secret that is missing or disabled and "
                + $"cannot authenticate: {string.Join(", ", broken.Select(connection => connection.Alias))}.");
        }

        var overdue = secretPosture.Count(secret => secret.RotationOverdue && !secret.Disabled);
        if (overdue > 0)
            findings.Add($"{overdue} secret(s) have not been rotated in over {RotationWarningDays} days.");

        var orphaned = secretPosture.Count(secret => secret.Orphaned && !secret.Disabled);
        if (orphaned > 0)
            findings.Add($"{orphaned} secret(s) are referenced by no connection.");

        return new CredentialPostureDto(secretPosture, connectionPosture, RotationWarningDays, findings);
    }

    /// <summary>
    /// Pulls <c>SECRET:name</c> references out of a connection's options and target, matching how
    /// <see cref="PortalConnectionCatalogService.VerifySecretReferencesAsync"/> finds them — the
    /// posture view must look for references in the same places the resolver does, or it will report
    /// a connection healthy that is not.
    /// </summary>
    private static string[] ExtractSecretReferences(PortalSharedConnection connection)
    {
        var values = new List<string>();
        try
        {
            var options = JsonSerializer.Deserialize<Dictionary<string, string>>(connection.OptionsJson);
            if (options is not null) values.AddRange(options.Values);
        }
        catch (JsonException)
        {
            // Unparseable options carry no discoverable references; the entry still lists.
        }

        if (!string.IsNullOrEmpty(connection.Target))
            values.AddRange(connection.Target.Split(';'));

        return
        [
            .. values
                .Select(value => value.Trim().Trim('\'', '"'))
                .Select(value => value.Contains('=') ? value.Split('=', 2)[1].Trim().Trim('\'', '"') : value)
                .Where(value => value.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase))
                .Select(value => value["SECRET:".Length..].Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Ordinal)
        ];
    }
}
