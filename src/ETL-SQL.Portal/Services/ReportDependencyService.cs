using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Aggregates a report's dependency view — snapshot, manifest datasets, registered datasets
/// (filtered by the caller's dataset read access), refresh jobs, source tables, and script lineage.
/// Extracted from <c>ReportsController.GetDependencies</c> so the controller only performs report
/// lookup, authorization, and HTTP mapping. Dataset visibility is decided from the caller's identity
/// passed in by the controller (<paramref name="isAdmin"/> / <paramref name="currentUserId"/>).
/// </summary>
public sealed class ReportDependencyService(
    PortalDbContext db,
    ReportScriptInspectionService scriptInspection,
    DatasetTenantScope datasetScope,
    PortalTenantCatalogScope? catalogScope = null)
{
    private IQueryable<ReportSnapshot> ReportSnapshots => catalogScope?.ReportSnapshots ?? db.ReportSnapshots;
    private IQueryable<ReportAlert> ReportAlerts => catalogScope?.ReportAlerts ?? db.ReportAlerts;

    /// <summary>
    /// Build the dependency DTO for an already-loaded <paramref name="report"/> (its <c>Folder</c>
    /// should be included for the report-summary path). <paramref name="isAdmin"/> and
    /// <paramref name="currentUserId"/> come from the authenticated caller and drive dataset ACL
    /// filtering.
    /// </summary>
    public async Task<ReportDependencyDto> BuildAsync(Report report, bool isAdmin, int currentUserId)
    {
        var id = report.Id;

        var snapshot = await ReportSnapshots
            .Where(s => s.ReportId == id)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync();

        var manifestDatasets = await scriptInspection.ReadManifestDatasetsAsync(snapshot);

        List<int> datasetGroupIds = isAdmin
            ? []
            : await db.UserGroups
                .Where(ug => ug.TenantId == datasetScope.TenantId && ug.UserId == currentUserId)
                .Select(ug => ug.GroupId)
                .ToListAsync();

        var registeredDatasets = (await datasetScope.Query(db)
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .Include(d => d.UserAcls)
            .Where(d => d.OwningReportId == id)
            .OrderBy(d => d.FolderPath)
            .ThenBy(d => d.Name)
            .ToListAsync())
            .Where(d => CanReadDataset(d, datasetGroupIds, isAdmin, currentUserId))
            .ToList();

        var datasetDtos = registeredDatasets
            .Select(d => new ReportDependencyDatasetDto(
                d.Id,
                d.Name,
                d.FolderPath,
                d.AccessLevel.ToString(),
                d.RowCount,
                d.LastRefresh,
                d.RefreshInterval,
                scriptInspection.BuildSourceDtos(scriptInspection.ParseSourceTables(d.SourceQuery), "DatasetSource")))
            .ToList();

        var reportJobLinks = await db.ReportJobLinks
            .Where(j => j.ReportId == id)
            .OrderBy(j => j.OrchestratorAlias)
            .ThenBy(j => j.JobName)
            .Select(j => new ReportDependencyRefreshJobDto(
                j.Id,
                j.JobName,
                j.OrchestratorAlias,
                "",
                j.LastRefreshedAt))
            .ToListAsync();

        var jobs = reportJobLinks;

        var alerts = await ReportAlerts
            .AsNoTracking()
            .Include(a => a.Notifications)
            .Where(a => a.ReportId == id)
            .OrderBy(a => a.Name)
            .ToListAsync();
        var alertDtos = alerts
            .Select(a => new ReportDependencyAlertDto(
                a.Id,
                a.Name,
                a.VisualName,
                a.Operator,
                a.Threshold,
                a.IsActive,
                a.LastState,
                a.LastEvaluatedAt,
                a.Notifications
                    .OrderBy(n => n.OrchestratorAlias, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n.NotificationName, StringComparer.OrdinalIgnoreCase)
                    .Select(n => new ReportDependencyAlertNotificationDto(
                        n.OrchestratorAlias,
                        n.NotificationName))
                    .ToList()))
            .ToList();

        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in await scriptInspection.ReadScriptSourceTablesAsync(report.ScriptPath))
            sourceNames.Add(source);
        foreach (var source in registeredDatasets.SelectMany(d => scriptInspection.ParseSourceTables(d.SourceQuery)))
            sourceNames.Add(source);
        var lineageEntries = await scriptInspection.ReadScriptLineageAsync(report.ScriptPath);

        return new ReportDependencyDto(
            new ReportDependencyReportDto(report.Id, report.Name, report.Folder?.Path ?? "", report.ScriptPath),
            snapshot is null ? null : new ReportDependencySnapshotDto(snapshot.Id, snapshot.ManifestPath, snapshot.BuiltAt),
            manifestDatasets,
            datasetDtos,
            jobs,
            alertDtos,
            scriptInspection.BuildSourceDtos(sourceNames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), "ScriptSource"),
            lineageEntries);
    }

    /// <summary>
    /// Reads from grants only. Authorship of the owning report used to short-circuit to true here,
    /// which meant the dependency view kept listing a private dataset to someone whose every grant
    /// had been revoked. The author still sees it — they hold an explicit Owner grant written when
    /// the dataset was registered — but now through a row that deprovisioning can remove.
    /// </summary>
    private static bool CanReadDataset(Dataset dataset, IReadOnlyCollection<int> groupIds, bool isAdmin, int currentUserId)
    {
        if (isAdmin) return true;
        if (dataset.AccessLevel == DatasetAccessLevel.Public) return true;

        return dataset.Acls.Any(a => groupIds.Contains(a.GroupId) && IsReadable(a.Permission))
            || dataset.UserAcls.Any(a => a.UserId == currentUserId && IsReadable(a.Permission));
    }

    private static bool IsReadable(DatasetPermission permission) =>
        permission is DatasetPermission.Viewer
            or DatasetPermission.Refresh
            or DatasetPermission.Editor
            or DatasetPermission.Owner;
}
