using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed class LineageImpactService(
    PortalDbContext db,
    ILineageCatalogStore lineageCatalog,
    IJobHistoryStore jobs,
    DatasetPermissionService datasetPermissions)
{
    public async Task<LineageImpactDto> AnalyzeAsync(
        string kind,
        string name,
        string? column,
        string direction,
        int depth,
        int limit,
        bool isAdmin,
        int currentUserId,
        CancellationToken cancellationToken = default)
    {
        kind = NormalizeKind(kind);
        direction = NormalizeDirection(direction);
        name = name.Trim();
        depth = Math.Clamp(depth, 1, 8);
        limit = Math.Clamp(limit, 1, 500);

        var seedReportIds = await FindSeedReportIdsAsync(kind, name, isAdmin, currentUserId, cancellationToken);
        var recent = (await lineageCatalog.GetRecentLineageAsync(Math.Max(limit * 50, 2000))).ToList();
        var graph = BuildGraph(recent);
        var seeds = BuildSeeds(kind, name, column, recent, seedReportIds);
        var nodes = seeds
            .SelectMany(seed => Traverse(graph, seed, direction, depth))
            .ToHashSet();
        var related = recent
            .Where(e => nodes.Contains(NodeKey.Table(e.TargetTable)) || e.SourceTables.Any(s => nodes.Contains(NodeKey.Table(s))))
            .Take(limit)
            .ToList();

        var reportIds = related
            .Select(e => TryParseReportId(e.JobName))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();
        reportIds.UnionWith(seedReportIds);

        var reports = await VisibleReports(isAdmin, currentUserId)
            .AsNoTracking()
            .Include(r => r.Folder)
            .Where(r => reportIds.Contains(r.Id)
                || (kind == "report" && r.Name == name)
                || (kind == "owner" && r.Owner == name)
                || (kind == "steward" && r.Steward == name))
            .ToListAsync(cancellationToken);

        var reportIdSet = reports.Select(r => r.Id).ToHashSet();
        var datasetCandidates = await db.Datasets
            .AsNoTracking()
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .Include(d => d.UserAcls)
            .Where(d => (d.OwningReportId.HasValue && reportIdSet.Contains(d.OwningReportId.Value))
                || (kind == "dataset" && d.Name == name))
            .ToListAsync(cancellationToken);
        var datasetPermissionMap = await datasetPermissions.GetEffectivePermissionsAsync(datasetCandidates, currentUserId, isAdmin);
        var datasets = datasetCandidates
            .Where(d => DatasetPermissionService.CanView(datasetPermissionMap[d.Id]))
            .ToList();

        var subscriptions = await db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Report)
            .Where(s => reportIdSet.Contains(s.ReportId))
            .ToListAsync(cancellationToken);

        var alerts = await db.ReportAlerts
            .AsNoTracking()
            .Include(a => a.Report)
            .Include(a => a.Notifications)
            .Where(a => reportIdSet.Contains(a.ReportId))
            .ToListAsync(cancellationToken);

        var scheduledJobs = await SafeJobsAsync();
        var relatedJobNames = related.Select(e => e.JobName).Where(j => !string.IsNullOrWhiteSpace(j)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var jobItems = scheduledJobs
            .Where(j => relatedJobNames.Contains(j.Name) || (kind == "job" && j.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Select(j => new LineageImpactItemDto("Job", j.Name, j.Script, j.LastRun, null))
            .ToList();

        var tableItems = nodes
            .Where(n => n.Kind == "table")
            .Select(n => new LineageImpactItemDto("Table", n.Name, null, LastSeen(related, n.Name), related.Count(e => IsRelatedToTable(e, n.Name))))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

        var columns = related
            .Where(e => !string.IsNullOrWhiteSpace(e.TargetColumn))
            .Select(e => $"{e.TargetTable}.{e.TargetColumn}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(v => new LineageImpactItemDto("Column", v, null, null, null))
            .ToList();

        var reportItems = reports
            .Select(r => new LineageImpactItemDto("Report", r.Name, r.Folder.Path, r.UpdatedAt, null))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var datasetItems = datasets
            .Select(d => new LineageImpactItemDto("Dataset", d.Name, d.FolderPath, d.LastRefresh, null))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var subscriptionItems = subscriptions
            .Select(s => new LineageImpactItemDto("Subscription", $"Subscription #{s.Id}", s.Report?.Name, s.LastTriggeredAt, null))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var alertItems = alerts
            .Select(a => new LineageImpactItemDto(
                "Alert",
                a.Name,
                AlertDetail(a),
                a.LastEvaluatedAt ?? a.LastTriggeredAt ?? a.UpdatedAt,
                a.Notifications.Count))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var stewardItems = related
            .SelectMany(e => PickTags(e.Tags, "owner", "steward"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new LineageImpactItemDto(v.StartsWith("owner:", StringComparison.OrdinalIgnoreCase) ? "Owner" : "Steward", v.Split(':', 2)[1], null, null, null))
            .ToList();

        return new LineageImpactDto(
            new LineageImpactRequestDto(kind, name, column, direction, depth, limit),
            new LineageImpactSummaryDto(tableItems.Count, columns.Count, reportItems.Count, datasetItems.Count, subscriptionItems.Count, jobItems.Count, alertItems.Count, stewardItems.Count),
            tableItems,
            columns,
            reportItems,
            datasetItems,
            subscriptionItems,
            jobItems,
            alertItems,
            stewardItems);
    }

    private IQueryable<Report> VisibleReports(bool isAdmin, int userId)
    {
        if (isAdmin) return db.Reports.Where(r => !r.IsDeleted);
        return db.Reports.Where(r => !r.IsDeleted && db.FolderAcls.Any(a =>
            a.FolderId == r.FolderId
            && a.Permission >= FolderPermission.Read
            && db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId)));
    }

    private async Task<IReadOnlyList<JobDefinition>> SafeJobsAsync()
    {
        try { return (await jobs.GetAllJobsAsync()).ToList(); }
        catch { return []; }
    }

    private async Task<HashSet<int>> FindSeedReportIdsAsync(string kind, string name, bool isAdmin, int currentUserId, CancellationToken cancellationToken)
    {
        if (kind == "report")
        {
            return (await VisibleReports(isAdmin, currentUserId)
                    .AsNoTracking()
                    .Include(r => r.Folder)
                    .Where(r => r.Name == name || (r.Folder.Path + "/" + r.Name) == name)
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        if (kind == "dataset")
        {
            return (await db.Datasets
                    .AsNoTracking()
                    .Where(d => d.Name == name && d.OwningReportId.HasValue)
                    .Join(VisibleReports(isAdmin, currentUserId).AsNoTracking(),
                        d => d.OwningReportId!.Value,
                        r => r.Id,
                        (_, r) => r.Id)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        if (kind == "subscription" && TryParseSubscriptionId(name) is int subscriptionId)
        {
            return (await db.Subscriptions
                    .AsNoTracking()
                    .Where(s => s.Id == subscriptionId)
                    .Join(VisibleReports(isAdmin, currentUserId).AsNoTracking(),
                        s => s.ReportId,
                        r => r.Id,
                        (_, r) => r.Id)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        return [];
    }

    private static Dictionary<NodeKey, HashSet<NodeKey>> BuildGraph(IEnumerable<LineageHistoryEntry> entries)
    {
        var graph = new Dictionary<NodeKey, HashSet<NodeKey>>();
        foreach (var e in entries)
        {
            var target = NodeKey.Table(e.TargetTable);
            _ = graph.TryAdd(target, []);
            foreach (var source in e.SourceTables)
            {
                var src = NodeKey.Table(source);
                _ = graph.TryAdd(src, []);
                graph[src].Add(target);
            }
        }
        return graph;
    }

    private static HashSet<NodeKey> Traverse(Dictionary<NodeKey, HashSet<NodeKey>> graph, NodeKey seed, string direction, int depth)
    {
        var reverse = graph
            .SelectMany(kvp => kvp.Value.Select(target => (source: kvp.Key, target)))
            .GroupBy(e => e.target)
            .ToDictionary(g => g.Key, g => g.Select(e => e.source).ToHashSet());

        var visited = new HashSet<NodeKey> { seed };
        var frontier = new Queue<(NodeKey Node, int Depth)>();
        frontier.Enqueue((seed, 0));

        while (frontier.Count > 0)
        {
            var (node, d) = frontier.Dequeue();
            if (d >= depth) continue;
            IEnumerable<NodeKey> next = direction switch
            {
                "upstream" => reverse.GetValueOrDefault(node) ?? [],
                "downstream" => graph.GetValueOrDefault(node) ?? [],
                _ => (graph.GetValueOrDefault(node) ?? []).Concat(reverse.GetValueOrDefault(node) ?? [])
            };
            foreach (var n in next)
            {
                if (visited.Add(n)) frontier.Enqueue((n, d + 1));
            }
        }

        return visited;
    }

    private static IReadOnlyList<NodeKey> BuildSeeds(string kind, string name, string? column, IEnumerable<LineageHistoryEntry> entries, IReadOnlySet<int> seedReportIds)
    {
        var seeds = kind switch
        {
            "column" => [NodeKey.Table(name.Contains('.') ? name[..name.LastIndexOf('.')] : name)],
            "job" => entries
                .Where(e => string.Equals(e.JobName, name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(TargetAndSources)
                .Distinct()
                .ToList(),
            "script" => entries
                .Where(e => string.Equals(e.ScriptPath, name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(TargetAndSources)
                .Distinct()
                .ToList(),
            "report" or "dataset" or "subscription" => entries
                .Where(e => TryParseReportId(e.JobName) is int reportId && seedReportIds.Contains(reportId))
                .SelectMany(TargetAndSources)
                .Distinct()
                .ToList(),
            "owner" or "steward" => entries
                .Where(e => e.Tags.TryGetValue(kind, out var value) && value.Equals(name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(TargetAndSources)
                .Distinct()
                .ToList(),
            _ => [NodeKey.Table(name)]
        };
        return seeds.Count == 0 ? [NodeKey.Table(name)] : seeds;
    }

    private static IEnumerable<NodeKey> TargetAndSources(LineageHistoryEntry entry)
    {
        yield return NodeKey.Table(entry.TargetTable);
        foreach (var source in entry.SourceTables)
            yield return NodeKey.Table(source);
    }

    private static string NormalizeKind(string kind) =>
        kind.Trim().ToLowerInvariant() is var k && k is "table" or "column" or "job" or "script" or "dataset" or "report" or "subscription" or "owner" or "steward" ? k : "table";

    private static string NormalizeDirection(string direction) =>
        direction.Trim().ToLowerInvariant() is var d && d is "upstream" or "downstream" or "both" ? d : "downstream";

    private static int? TryParseReportId(string? jobName)
    {
        if (string.IsNullOrWhiteSpace(jobName) || !jobName.StartsWith("report:", StringComparison.OrdinalIgnoreCase))
            return null;
        var nextColon = jobName.IndexOf(':', "report:".Length);
        var idText = nextColon < 0 ? jobName["report:".Length..] : jobName["report:".Length..nextColon];
        return int.TryParse(idText, out var id) ? id : null;
    }

    private static int? TryParseSubscriptionId(string name)
    {
        name = name.Trim();
        if (name.StartsWith("Subscription #", StringComparison.OrdinalIgnoreCase))
            name = name["Subscription #".Length..];
        return int.TryParse(name, out var id) ? id : null;
    }

    private static DateTime? LastSeen(IEnumerable<LineageHistoryEntry> entries, string table) =>
        entries.Where(e => IsRelatedToTable(e, table)).Select(e => (DateTime?)e.RunAt).Max();

    private static bool IsRelatedToTable(LineageHistoryEntry e, string table) =>
        e.TargetTable.Equals(table, StringComparison.OrdinalIgnoreCase)
        || e.SourceTables.Any(s => s.Equals(table, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> PickTags(IReadOnlyDictionary<string, string> tags, params string[] keys)
    {
        foreach (var key in keys)
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                yield return $"{key}:{value}";
    }

    private static string AlertDetail(ReportAlert alert)
    {
        var notifications = alert.Notifications
            .OrderBy(n => n.OrchestratorAlias, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.NotificationName, StringComparer.OrdinalIgnoreCase)
            .Select(n => $"{n.OrchestratorAlias}.{n.NotificationName}")
            .ToList();
        var baseDetail = $"{alert.Report.Name} → {alert.VisualName} {alert.Operator} {alert.Threshold}";
        return notifications.Count == 0
            ? baseDetail
            : $"{baseDetail}; notifications: {string.Join(", ", notifications)}";
    }

    private readonly record struct NodeKey(string Kind, string Name)
    {
        public static NodeKey Table(string name) => new("table", name);
    }
}
