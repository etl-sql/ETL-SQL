using System.Collections.Concurrent;
using System.Security.Claims;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;

namespace ETL_SQL.Portal.Services;

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
    IMetadataManager metadata,
    PortalConfig? portalConfig = null)
{
    private const string ScopedDesignerUriPrefix = "portal-designer://";
    private static readonly ConcurrentDictionary<string, Lazy<Task<DesignerSchemaResponse>>> ActiveDiscoveries = new(StringComparer.OrdinalIgnoreCase);
    private PortalDesignerLimitsConfig Limits => portalConfig?.DesignerLimits ?? new PortalDesignerLimitsConfig();

    public async Task<DesignerSchemaResponse> GetSchemaAsync(
        string connectionRef,
        ClaimsPrincipal user,
        string? documentUri = null,
        CancellationToken cancellationToken = default)
    {
        var alias = NormalizeConnectionRef(connectionRef);
        var effectiveDocumentUri = ResolveDocumentUri(user, alias, documentUri);
        var key = $"{effectiveDocumentUri}|{alias}";
        var discovery = ActiveDiscoveries.GetOrAdd(key, _ => new Lazy<Task<DesignerSchemaResponse>>(
            () => GetSchemaCoreAsync(alias, user, effectiveDocumentUri, cancellationToken)));

        try
        {
            return await discovery.Value.ConfigureAwait(false);
        }
        finally
        {
            ActiveDiscoveries.TryRemove(key, out _);
        }
    }

    private async Task<DesignerSchemaResponse> GetSchemaCoreAsync(
        string alias,
        ClaimsPrincipal user,
        string effectiveDocumentUri,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, Limits.MaxSchemaDiscoverySeconds)));

        var definition = await catalog.ResolveAsync(alias, BuildIdentity(user), timeout.Token);
        return await RegisterResolvedConnectionAsync(alias, definition, effectiveDocumentUri, timeout.Token);
    }

    public async Task<DesignerSchemaResponse> RegisterResolvedConnectionAsync(
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

        var tables = new ConcurrentBag<DesignerSchemaTableDto>();
        var metadataColumns = new Dictionary<string, IEnumerable<ColumnMetadata>>(StringComparer.OrdinalIgnoreCase);
        await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, target);
        var tableNames = (await source.GetTablesAsync(cancellationToken))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, Limits.MaxSchemaTables))
            .ToList();
        using var columnGate = new SemaphoreSlim(Math.Max(1, Limits.MaxSchemaColumnConcurrency));
        var columnTasks = tableNames.Select(async table =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await columnGate.WaitAsync(cancellationToken);
            try
            {
                var columns = (await GetColumnsAsync(source, table, cancellationToken))
                    .Take(Math.Max(1, Limits.MaxSchemaColumnsPerTable))
                    .ToList();
                lock (metadataColumns)
                {
                    metadataColumns[table] = columns;
                }

                tables.Add(new DesignerSchemaTableDto(
                    table,
                    columns.Select(c => new DesignerSchemaColumnDto(c.Name, c.DataType)).ToList()));
            }
            finally
            {
                columnGate.Release();
            }
        });
        await Task.WhenAll(columnTasks);

        metadata.RegisterDocumentMetadata(
            documentUri,
            alias,
            connector.Name,
            tableNames,
            metadataColumns);

        return new DesignerSchemaResponse(alias, tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static async Task<List<ColumnMetadata>> GetColumnsAsync(
        IDataSource source,
        string table,
        CancellationToken cancellationToken)
    {
        var catalogProvider = source.GetCatalogProvider();
        if (catalogProvider != null)
        {
            try
            {
                var (schema, name) = SplitQualifiedName(table);
                var catalogColumns = await catalogProvider.GetColumnMetadataAsync(schema, name, cancellationToken);
                if (catalogColumns.Count > 0)
                    return catalogColumns.Select(c => new ColumnMetadata(c.ColumnName, c.DataType)).ToList();
            }
            catch
            {
                // Fall back to datasource column discovery below.
            }
        }

        var rawColumns = source is IDatabaseSource db
            ? await db.GetColumnsAsync(table, cancellationToken)
            : await source.GetColumnsAsync(cancellationToken);
        return rawColumns.Select(c => new ColumnMetadata(c, "ANY")).ToList();
    }

    private static (string Schema, string Name) SplitQualifiedName(string qualified)
    {
        var parts = qualified.Split('.');
        return parts.Length > 1 ? (parts[^2], parts[^1]) : ("", qualified);
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
        var alias = NormalizeConnectionRef(connectionRef);
        var suffix = string.IsNullOrWhiteSpace(documentUri)
            ? "default"
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(documentUri))).ToLowerInvariant();
        return $"{ScopedDesignerUriPrefix}u/{Uri.EscapeDataString(userId)}/c/{Uri.EscapeDataString(alias)}/{suffix}";
    }

    public static string ResolveDocumentUri(ClaimsPrincipal user, string connectionRef, string? documentUri = null)
    {
        if (!string.IsNullOrWhiteSpace(documentUri)
            && documentUri.Trim().StartsWith(ScopedDesignerUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return documentUri.Trim();
        }

        return BuildDocumentUri(user, connectionRef, documentUri);
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
