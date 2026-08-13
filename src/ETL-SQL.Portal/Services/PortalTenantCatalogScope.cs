using ETL_SQL.Portal.Data;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Provider-side relational catalog partition. Folder ownership and report authorship are bound to
/// immutable tenant identifiers, so every derived surface can join to these roots without trusting a
/// caller-supplied tenant or loading foreign rows into memory.
/// </summary>
public sealed class PortalTenantCatalogScope(
    PortalDbContext db,
    DatasetTenantScope tenantScope)
{
    public string TenantId => tenantScope.TenantId;

    public IQueryable<Folder> Folders => db.Folders.Where(folder => folder.TenantId == TenantId);

    public IQueryable<Report> Reports => db.Reports.Where(report => report.TenantId == TenantId);

    public IQueryable<FolderAcl> FolderAcls => db.FolderAcls.Where(acl =>
        Folders.Any(folder => folder.Id == acl.FolderId));

    public IQueryable<ReportAcl> ReportAcls => db.ReportAcls.Where(acl =>
        Reports.Any(report => report.Id == acl.ReportId));

    public IQueryable<ReportFavorite> ReportFavorites => db.ReportFavorites.Where(favorite =>
        Reports.Any(report => report.Id == favorite.ReportId));

    public IQueryable<ReportAccessRequest> ReportAccessRequests => db.ReportAccessRequests.Where(request =>
        Reports.Any(report => report.Id == request.ReportId));

    public IQueryable<ReportShareLink> ReportShareLinks => db.ReportShareLinks.Where(link =>
        Reports.Any(report => report.Id == link.ReportId));

    public IQueryable<ReportEmbedToken> ReportEmbedTokens => db.ReportEmbedTokens.Where(token =>
        Reports.Any(report => report.Id == token.ReportId));

    public IQueryable<SavedReportView> SavedReportViews => db.SavedReportViews.Where(view =>
        Reports.Any(report => report.Id == view.ReportId));

    public IQueryable<ReportAlert> ReportAlerts => db.ReportAlerts.Where(alert =>
        Reports.Any(report => report.Id == alert.ReportId));

    public IQueryable<ReportSnapshot> ReportSnapshots => db.ReportSnapshots.Where(snapshot =>
        Reports.Any(report => report.Id == snapshot.ReportId));

    public IQueryable<Subscription> Subscriptions => db.Subscriptions.Where(subscription =>
        Reports.Any(report => report.Id == subscription.ReportId));

    public IQueryable<PortalExecutionJob> ExecutionJobs => db.PortalExecutionJobs.Where(job =>
        Reports.Any(report => report.Id == job.ReportId));
}
