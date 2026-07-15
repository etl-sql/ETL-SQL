using System.Security.Claims;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;

namespace ETL_SQL.ReportPortal.Services;

public sealed record DesignerSchemaColumnDto(string Name, string Type);
public sealed record DesignerSchemaTableDto(string Name, IReadOnlyList<DesignerSchemaColumnDto> Columns);
public sealed record DesignerSchemaResponse(string Connection, IReadOnlyList<DesignerSchemaTableDto> Tables);

/// <summary>
/// Resolves Portal shared-connection schemas under the caller's identity and caches only
/// non-secret table/column metadata through the shared metadata manager.
/// </summary>
public sealed class PortalDesignerSchemaService(
    IConnectionCatalogProvider catalog,
    ISecretProvider secrets,
    IConnectorRegistry connectors,
    IMetadataManager metadata)
{
    public async Task<DesignerSchemaResponse> GetSchemaAsync(
        string connectionRef,
        ClaimsPrincipal user,
        string? documentUri = null,
        CancellationToken cancellationToken = default)
    {
        var alias = NormalizeConnectionRef(connectionRef);
        var definition = await catalog.ResolveAsync(alias, BuildIdentity(user), cancellationToken);
        var effectiveDocumentUri = BuildDocumentUri(user, alias, documentUri);
        await RegisterResolvedConnectionAsync(alias, definition, effectiveDocumentUri, cancellationToken);

        var tables = new List<DesignerSchemaTableDto>();
        foreach (var table in (await metadata.GetTablesAsync(alias, effectiveDocumentUri)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var columns = (await metadata.GetColumnDetailsAsync(alias, table, effectiveDocumentUri))
                .Select(c => new DesignerSchemaColumnDto(c.Name, c.DataType))
                .ToList();
            tables.Add(new DesignerSchemaTableDto(table, columns));
        }

        return new DesignerSchemaResponse(alias, tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public async Task RegisterResolvedConnectionAsync(
        string alias,
        SharedConnectionDefinition definition,
        string documentUri,
        CancellationToken cancellationToken = default)
    {
        var options = await ResolveSecretReferencesAsync(definition.Options, cancellationToken);
        var target = await ResolveSecretReferenceAsync(definition.Target, cancellationToken) ?? string.Empty;
        var connector = connectors.GetConnector(definition.ConnectorType)
            ?? throw new InvalidOperationException($"Connection type '{definition.ConnectorType}' is not registered.");

        if (string.IsNullOrEmpty(target) && options.Count > 0)
            target = connector.BuildConnectionString(options);

        metadata.RegisterDocumentConnection(documentUri, alias, connector.Name, target);
    }

    public static string NormalizeConnectionRef(string connectionRef)
    {
        if (string.IsNullOrWhiteSpace(connectionRef))
            throw new ArgumentException("A connection reference is required.", nameof(connectionRef));

        var trimmed = connectionRef.Trim();
        return trimmed.StartsWith("SHARED:", StringComparison.OrdinalIgnoreCase)
            ? trimmed["SHARED:".Length..].Trim()
            : trimmed;
    }

    public static ExecutionIdentity BuildIdentity(ClaimsPrincipal user)
    {
        var userIdText = user.FindFirstValue(ClaimTypes.NameIdentifier);
        int? userId = int.TryParse(userIdText, out var parsedUserId) ? parsedUserId : null;
        var name = user.FindFirstValue(ClaimTypes.Name)
            ?? user.Identity?.Name
            ?? userIdText
            ?? "(unknown)";
        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return new ExecutionIdentity
        {
            EffectiveUser = name,
            EffectiveUserId = userId,
            RealUser = name,
            IsAdmin = user.IsInRole("Admin"),
            Roles = roles
        };
    }

    public static string BuildDocumentUri(ClaimsPrincipal user, string connectionRef, string? documentUri = null)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.Identity?.Name
            ?? "anonymous";
        var suffix = string.IsNullOrWhiteSpace(documentUri)
            ? "default"
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(documentUri))).ToLowerInvariant();
        return $"portal-designer://u/{Uri.EscapeDataString(userId)}/c/{Uri.EscapeDataString(connectionRef)}/{suffix}";
    }

    private async Task<Dictionary<string, string>> ResolveSecretReferencesAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
            resolved[key] = await ResolveSecretReferenceAsync(value, cancellationToken) ?? string.Empty;
        return resolved;
    }

    private async Task<string?> ResolveSecretReferenceAsync(string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase))
            return value;

        var name = value["SECRET:".Length..].Trim();
        return (await secrets.ResolveAsync(name, cancellationToken)).Value;
    }
}
