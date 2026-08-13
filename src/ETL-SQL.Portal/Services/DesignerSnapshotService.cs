using System.Security.Claims;
using ETL_SQL.Core.Storage;
using ETL_SQL.Portal.Data;
using ETL_SQL.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// A snapshot package shaped for the Report Designer canvas: sample rows per visual plus the
/// metadata the canvas badges. Row arrays are positional, matching what the runtime stores.
/// </summary>
public sealed record DesignerSnapshotPackage(
    string ReportName,
    DateTime BuiltAt,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string?>>> SampleRows,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Columns,
    DesignerSnapshotMetadata Metadata);

/// <summary>Badged on the canvas so a designer can see what they are looking at.</summary>
public sealed record DesignerSnapshotMetadata(bool IsSampled, bool RlsEnforced, int TotalRows, int ReturnedRows);

/// <summary>
/// Loads the last successfully compiled <c>.etlsnap</c> package for a report so the designer can lay
/// visuals out against real historical data instead of wireframe placeholders, without touching a
/// production database.
///
/// Row-level security is satisfied structurally rather than by filtering here.
/// <c>ExecutionJobService</c> refuses to persist a shared snapshot for an identity-sensitive report —
/// if the script references identity, or the run was impersonated, no <c>ReportSnapshot</c> row is
/// written and the report stays per-viewer execution only. Any snapshot that exists therefore cannot
/// vary by identity, so the folder-permission gate below is sufficient and an identity-sensitive
/// report simply has no snapshot to show.
/// </summary>
public sealed class DesignerSnapshotService(
    PortalDbContext db,
    PortalConfig portalConfig,
    FolderPermissionService folderPermissions,
    IArtifactStorage artifacts,
    SnapshotPackageService snapshotPackages,
    DatasetTenantScope? datasetScope = null,
    PortalTenantCatalogScope? catalogScope = null)
{
    private readonly DatasetTenantScope _datasetScope = datasetScope ?? new DatasetTenantScope(portalConfig);
    private PortalTenantCatalogScope CatalogScope => catalogScope ?? new PortalTenantCatalogScope(db, _datasetScope);
    /// <summary>
    /// Rows kept per visual. The canvas draws thumbnails a few hundred pixels tall, so more than this
    /// changes nothing visible while making the payload and the browser's job worse.
    /// </summary>
    public const int MaxRowsPerVisual = 500;

    public enum SnapshotOutcome { Ok, ReportNotFound, Forbidden, NoSnapshot }

    public sealed record Result(SnapshotOutcome Outcome, DesignerSnapshotPackage? Package);

    public async Task<Result> LoadForDesignerAsync(int reportId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var report = await CatalogScope.Reports.FirstOrDefaultAsync(r => r.Id == reportId && !r.IsDeleted, ct);
        if (report is null) return new Result(SnapshotOutcome.ReportNotFound, null);

        // Same gate the snapshot view path uses: folder permission, then path containment on both the
        // script and the snapshot key, so a stored path cannot point outside the configured roots.
        var permission = await folderPermissions.GetEffectiveReportPermissionAsync(report, user);
        if (permission is null) return new Result(SnapshotOutcome.Forbidden, null);

        if (!PortalPathGuard.TryResolveScript(
                portalConfig, _datasetScope.TenantId, report.ScriptPath, out _))
            return new Result(SnapshotOutcome.Forbidden, null);

        var snapshot = await CatalogScope.ReportSnapshots
            .Where(s => s.ReportId == reportId)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync(ct);
        if (snapshot is null) return new Result(SnapshotOutcome.NoSnapshot, null);

        var manifestKey = PortalPathGuard.ToSnapshotKey(portalConfig, snapshot.ManifestPath);
        if (manifestKey is null) return new Result(SnapshotOutcome.Forbidden, null);

        if (!await artifacts.ExistsAsync(ArtifactArea.Snapshots, manifestKey))
            return new Result(SnapshotOutcome.NoSnapshot, null);

        var manifest = await snapshotPackages.LoadAsync(
            manifestKey, ct, _datasetScope.TenantId);
        if (manifest is null) return new Result(SnapshotOutcome.NoSnapshot, null);

        var sampleRows = new Dictionary<string, IReadOnlyList<IReadOnlyList<string?>>>(StringComparer.OrdinalIgnoreCase);
        var columns = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var totalRows = 0;
        var returnedRows = 0;

        for (var i = 0; i < manifest.Visuals.Count; i++)
        {
            var visual = manifest.Visuals[i];

            // Keyed by visual name, not dataset: the manifest records visuals and datasets but never
            // links them, so the visual is the only identity both sides share. The canvas resolves a
            // visual's own name before falling back to its dataset.
            var key = string.IsNullOrWhiteSpace(visual.Name) ? $"visual{i}" : visual.Name;
            if (sampleRows.ContainsKey(key)) continue;

            var visualRows = await snapshotPackages.LoadRowsAsync(
                manifestKey, i, ct, _datasetScope.TenantId);

            // Rows live inline on the visual for small tables and are offloaded for large ones, so
            // fall back to the inline copy when there is no separate row artifact.
            var rows = visualRows?.Rows ?? visual.Rows;
            var cols = visualRows?.Columns ?? visual.Columns;
            if (rows is null || rows.Count == 0) continue;

            totalRows += visualRows?.RowCount ?? rows.Count;

            var capped = rows.Count > MaxRowsPerVisual
                ? rows.Take(MaxRowsPerVisual).ToList()
                : rows;
            returnedRows += capped.Count;

            sampleRows[key] = capped.Select(r => (IReadOnlyList<string?>)r).ToList();
            if (cols is { Count: > 0 }) columns[key] = cols;
        }

        if (sampleRows.Count == 0) return new Result(SnapshotOutcome.NoSnapshot, null);

        return new Result(SnapshotOutcome.Ok, new DesignerSnapshotPackage(
            report.Name,
            snapshot.BuiltAt,
            sampleRows,
            columns,
            new DesignerSnapshotMetadata(
                IsSampled: returnedRows < totalRows,
                // A shared snapshot only exists for a report whose output does not vary by identity,
                // so what the badge conveys is "this data is identity-independent", not "rows were
                // filtered for you".
                RlsEnforced: false,
                TotalRows: totalRows,
                ReturnedRows: returnedRows)));
    }
}
