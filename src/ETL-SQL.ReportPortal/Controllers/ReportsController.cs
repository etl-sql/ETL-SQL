using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using ETL_SQL.ReportPortal.Services;
using ETL_SQL.Reporting;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly PortalDbContext db;
    private readonly AuditService audit;
    private readonly PortalConfig portalConfig;
    private readonly ILineageCatalogStore lineageCatalog;
    private readonly FolderPermissionService folderPermissions;
    private readonly ReportScriptInspectionService scriptInspection;
    private readonly IDatasetRegistry datasetRegistry;

    public ReportsController(PortalDbContext db, AuditService audit, PortalConfig portalConfig, ILineageCatalogStore lineageCatalog, FolderPermissionService folderPermissions, ReportScriptInspectionService scriptInspection, IDatasetRegistry datasetRegistry)
    {
        this.db = db;
        this.audit = audit;
        this.portalConfig = portalConfig;
        this.lineageCatalog = lineageCatalog;
        this.folderPermissions = folderPermissions;
        this.scriptInspection = scriptInspection;
        this.datasetRegistry = datasetRegistry;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin => User.IsInRole("Admin");

    private Task<FolderPermission?> GetEffectivePermissionAsync(int folderId) =>
        folderPermissions.GetEffectivePermissionAsync(folderId, User);

    private ReportDto ToDto(Report r, ReportSnapshot? snap, bool isFavorite = false)
    {
        bool isStale = false;
        if (snap is not null
            && PortalPathGuard.TryResolveScript(portalConfig, r.ScriptPath, out var resolvedScriptPath)
            && System.IO.File.Exists(resolvedScriptPath))
            isStale = System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath) > snap.BuiltAt;

        bool scriptChanged = false;
        if (r.PublishedScriptHash is not null
            && PortalPathGuard.TryResolveScript(portalConfig, r.ScriptPath, out resolvedScriptPath)
            && System.IO.File.Exists(resolvedScriptPath))
        {
            var currentHash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(System.IO.File.ReadAllBytes(resolvedScriptPath))).ToLowerInvariant();
            scriptChanged = !string.Equals(currentHash, r.PublishedScriptHash, StringComparison.OrdinalIgnoreCase);
        }

        return new ReportDto(
            r.Id, r.FolderId, r.Folder?.Path ?? "",
            r.Name, r.Description,
            r.Owner, r.Contact, r.Tags, r.Category, r.Domain, r.Steward, r.Certification,
            DeserializeMetadata(r.MetadataJson),
            r.ScriptPath,
            r.ScriptLastModified,
            snap is not null,
            snap?.BuiltAt,
            r.LastViewedAt,
            r.LastRefreshStartedAt,
            r.LastRefreshCompletedAt,
            r.LastRefreshStatus,
            r.LastRefreshError,
            r.LastRefreshDurationMs,
            isFavorite,
            isStale,
            scriptChanged);
    }

    private async Task<string> GenerateUniqueShareTokenAsync()
    {
        while (true)
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            var token = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            if (!await db.ReportShareLinks.AnyAsync(l => l.Token == token))
                return token;
        }
    }

    private ReportShareLinkDto ToShareLinkDto(ReportShareLink link)
    {
        var report = link.Report;
        var folderPath = report.Folder?.Path ?? "";
        return new ReportShareLinkDto(
            link.Id,
            link.ReportId,
            report.Name,
            folderPath,
            link.Token,
            $"{Request.Scheme}://{Request.Host}/api/share/{link.Token}",
            link.CreatedBy,
            link.CreatedAt,
            link.ExpiresAt,
            link.RevokedAt);
    }

    private async Task<string> GenerateUniqueEmbedTokenAsync()
    {
        while (true)
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (!await db.ReportEmbedTokens.AnyAsync(t => t.Token == token))
                return token;
        }
    }

    private ReportEmbedTokenDto ToEmbedTokenDto(ReportEmbedToken token)
    {
        var report = token.Report;
        return new ReportEmbedTokenDto(
            token.Id,
            token.ReportId,
            report.Name,
            token.Name,
            token.Token,
            $"{Request.Scheme}://{Request.Host}/api/embed/{token.Token}",
            token.CreatedBy,
            token.CreatedAt,
            token.ExpiresAt,
            token.RevokedAt);
    }

    private static SavedReportViewDto ToSavedViewDto(SavedReportView view) =>
        new(
            view.Id,
            view.ReportId,
            view.Name,
            DeserializeDictionary(view.ParametersJson),
            DeserializeDictionary(view.FiltersJson),
            view.IsDefault,
            view.CreatedAt,
            view.UpdatedAt);

    private static ReportAlertDto ToAlertDto(ReportAlert alert) =>
        new(
            alert.Id,
            alert.ReportId,
            alert.Name,
            alert.VisualName,
            alert.Operator,
            alert.Threshold,
            alert.Recipient,
            alert.SmtpAlias,
            alert.IsActive,
            alert.CreatedAt,
            alert.UpdatedAt,
            alert.LastCheckedAt,
            alert.LastTriggeredAt);

    private async Task ClearDefaultSavedViewsAsync(int reportId)
    {
        var defaults = await db.SavedReportViews
            .Where(v => v.ReportId == reportId && v.UserId == CurrentUserId && v.IsDefault)
            .ToListAsync();
        foreach (var view in defaults)
            view.IsDefault = false;
    }

    private static bool IsSupportedAlertOperator(string op) =>
        op is ">" or ">=" or "<" or "<=" or "=" or "!=";

    private static string? SerializeDictionary(Dictionary<string, string>? values) =>
        values is null || values.Count == 0
            ? null
            : JsonSerializer.Serialize(values.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value));

    private static Dictionary<string, string>? DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── GET /api/folders/{id}/reports ─────────────────────────────────────────

    [HttpGet("folders/{folderId:int}/reports")]
    public async Task<IActionResult> GetByFolder(int folderId)
    {
        var perm = await GetEffectivePermissionAsync(folderId);
        if (perm is null) return Forbid();

        var reports = await db.Reports
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .Where(r => r.FolderId == folderId && !r.IsDeleted)
            .ToListAsync();
        var reportIds = reports.Select(r => r.Id).ToList();
        var favoriteIds = await db.ReportFavorites
            .Where(f => f.UserId == CurrentUserId && reportIds.Contains(f.ReportId))
            .Select(f => f.ReportId)
            .ToHashSetAsync();

        return Ok(reports.Select(r => ToDto(r, r.Snapshots.FirstOrDefault(), favoriteIds.Contains(r.Id))));
    }

    // ── POST /api/reports ─────────────────────────────────────────────────────

    [HttpPost("reports")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> Publish([FromBody] PublishReportRequest req)
    {
        var perm = await GetEffectivePermissionAsync(req.FolderId);
        if (perm is null || perm < FolderPermission.Manage)
            return Forbid();

        if (!await db.Folders.AnyAsync(f => f.Id == req.FolderId))
            return NotFound("Folder not found");

        if (!PortalPathGuard.TryResolveScript(portalConfig, req.ScriptPath, out var resolved))
            return BadRequest(new { error = "Script path must be within the configured ScriptRootPath" });

        var validation = await scriptInspection.ValidateResolvedScriptAsync(resolved);
        if (!validation.IsValid)
            return BadRequest(validation);

        var scriptMetadata = new Dictionary<string, string>(validation.Metadata, StringComparer.OrdinalIgnoreCase);

        var report = new Report
        {
            FolderId            = req.FolderId,
            Name                = req.Name,
            Description         = FirstNonBlank(req.Description, GetMetadata(scriptMetadata, "description", "d")),
            Owner               = FirstNonBlank(req.Owner, GetMetadata(scriptMetadata, "owner")),
            Contact             = FirstNonBlank(req.Contact, GetMetadata(scriptMetadata, "contact")),
            Tags                = FirstNonBlank(req.Tags, GetMetadata(scriptMetadata, "tags")),
            Category            = FirstNonBlank(req.Category, GetMetadata(scriptMetadata, "category")),
            Domain              = FirstNonBlank(req.Domain, GetMetadata(scriptMetadata, "domain")),
            Steward             = FirstNonBlank(req.Steward, GetMetadata(scriptMetadata, "steward")),
            Certification       = FirstNonBlank(req.Certification, GetMetadata(scriptMetadata, "certification", "trusted")),
            MetadataJson        = SerializeMetadata(scriptMetadata),
            ScriptPath          = resolved,
            ScriptLastModified  = validation.LastModified ?? DateTime.UtcNow,
            PublishedScriptHash = validation.Hash,
            CreatedBy           = CurrentUserId,
            CreatedAt           = DateTime.UtcNow,
            UpdatedAt           = DateTime.UtcNow
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "PUBLISH_REPORT", "Report", report.Id.ToString(), report.Name);

        return CreatedAtAction(nameof(GetById), new { id = report.Id }, ToDto(report, null));
    }

    // ── POST /api/reports/validate ───────────────────────────────────────────

    [HttpPost("reports/validate")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> ValidateScript([FromBody] ValidateReportScriptRequest req)
    {
        if (!PortalPathGuard.TryResolveScript(portalConfig, req.ScriptPath, out var resolved))
            return BadRequest(new ReportScriptValidationDto(
                false,
                req.ScriptPath,
                null,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<ReportParameterDto>(),
                ["Script path must be within the configured ScriptRootPath"]));

        var validation = await scriptInspection.ValidateResolvedScriptAsync(resolved);
        return validation.IsValid ? Ok(validation) : BadRequest(validation);
    }

    // ── GET /api/reports/{id} ─────────────────────────────────────────────────

    [HttpGet("reports/{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .Include(r => r.Snapshots.OrderByDescending(s => s.BuiltAt).Take(1))
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        var isFavorite = await db.ReportFavorites.AnyAsync(f => f.UserId == CurrentUserId && f.ReportId == report.Id);
        return Ok(ToDto(report, report.Snapshots.FirstOrDefault(), isFavorite));
    }

    // ── GET /api/reports/{id}/dependencies ───────────────────────────────────

    [HttpGet("reports/{id:int}/dependencies")]
    public async Task<IActionResult> GetDependencies(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        var snapshot = await db.ReportSnapshots
            .Where(s => s.ReportId == id)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync();

        var manifestDatasets = await scriptInspection.ReadManifestDatasetsAsync(snapshot);

        List<int> datasetGroupIds = IsAdmin
            ? []
            : await db.UserGroups
                .Where(ug => ug.UserId == CurrentUserId)
                .Select(ug => ug.GroupId)
                .ToListAsync();

        var registeredDatasets = (await db.Datasets
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .Where(d => d.OwningReportId == id)
            .OrderBy(d => d.FolderPath)
            .ThenBy(d => d.Name)
            .ToListAsync())
            .Where(d => CanReadDataset(d, datasetGroupIds))
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

        var jobs = await db.DatasetJobs
            .Where(j => j.ReportId == id)
            .OrderBy(j => j.OrchestratorJobName)
            .Select(j => new ReportDependencyRefreshJobDto(
                j.Id,
                j.OrchestratorJobName,
                j.RefreshInterval,
                j.LastRefreshedAt))
            .ToListAsync();

        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in await scriptInspection.ReadScriptSourceTablesAsync(report.ScriptPath))
            sourceNames.Add(source);
        foreach (var source in registeredDatasets.SelectMany(d => scriptInspection.ParseSourceTables(d.SourceQuery)))
            sourceNames.Add(source);
        var lineageEntries = await scriptInspection.ReadScriptLineageAsync(report.ScriptPath);

        var dto = new ReportDependencyDto(
            new ReportDependencyReportDto(report.Id, report.Name, report.Folder?.Path ?? "", report.ScriptPath),
            snapshot is null ? null : new ReportDependencySnapshotDto(snapshot.Id, snapshot.ManifestPath, snapshot.BuiltAt),
            manifestDatasets,
            datasetDtos,
            jobs,
            scriptInspection.BuildSourceDtos(sourceNames.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), "ScriptSource"),
            lineageEntries);

        return Ok(dto);
    }

    // ── GET /api/reports/{id}/structure ─────────────────────────────────────

    [HttpGet("reports/{id:int}/structure")]
    public async Task<IActionResult> GetStructure(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        if (!System.IO.File.Exists(resolvedScriptPath))
            return Ok(new DagDto([], []));

        var scriptText = await System.IO.File.ReadAllTextAsync(resolvedScriptPath);
        List<DagNodeDto> nodes = [];
        List<DagEdgeDto> edges = [];

        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new CoreParser(tokens, scriptText).Parse();

            // Pass 1 — collect all tables produced by SELECT INTO and CREATE DATASET
            var producers = new Dictionary<string, (List<string> Sources, bool HasGroupBy)>(StringComparer.OrdinalIgnoreCase);

            foreach (var stmt in script.Statements)
            {
                if (stmt is SelectStatement sel && sel.IntoTable is not null)
                {
                    producers[sel.IntoTable.TableName] = (
                        sel.GetSourceTables().ToList(),
                        sel.GroupBy?.Count > 0 || sel.GroupingSet is not null);
                }
                else if (stmt is CreateDatasetStatement ds)
                {
                    var selQuery = ds.SourceQuery as SelectStatement;
                    producers[ds.TempTableName] = (
                        ds.SourceQuery.GetSourceTables().ToList(),
                        selQuery?.GroupBy?.Count > 0 || selQuery?.GroupingSet is not null);
                }
            }

            // Pass 2 — walk backwards from each visual to find only relevant ancestors
            var relevant = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void WalkAncestors(string table)
            {
                if (!relevant.Add(table)) return;
                if (producers.TryGetValue(table, out var info))
                    foreach (var src in info.Sources) WalkAncestors(src);
            }

            var visuals = script.Statements.OfType<CreateVisualStatement>().ToList();
            var visualPages = BuildVisualPageMap(script);

            foreach (var vis in visuals)
            {
                if (vis.Source.TempTableName is string t)
                    WalkAncestors(t);
                else if (vis.Source.InlineSelect is Statement inl)
                    foreach (var src in inl.GetSourceTables()) WalkAncestors(src);
            }

            // Build nodes — datasets (green), temp/source tables (gray)
            var datasetNames = new HashSet<string>(
                script.Statements.OfType<CreateDatasetStatement>().Select(d => d.TempTableName),
                StringComparer.OrdinalIgnoreCase);

            var addedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string EnsureTableNode(string name)
            {
                var isDataset = datasetNames.Contains(name);
                var nodeId = isDataset ? $"ds:{name}" : $"table:{name}";
                if (addedNodes.Add(nodeId))
                    nodes.Add(new DagNodeDto(nodeId, name, isDataset ? "dataset" : "table", null));
                return nodeId;
            }

            // Add edges for all producer relationships (restricted to relevant ancestors)
            foreach (var kvp in producers)
            {
                var target = kvp.Key;
                if (!relevant.Contains(target)) continue;
                var (srcs, hasGroupBy) = kvp.Value;
                var edgeLabel = hasGroupBy ? "GROUP BY" : "SELECT";
                var targetId = EnsureTableNode(target);
                foreach (var src in srcs)
                {
                    var srcId = EnsureTableNode(src);
                    edges.Add(new DagEdgeDto(srcId, targetId, edgeLabel));
                }
            }

            // Add visual and page nodes, plus dataset→visual edges with axis labels
            foreach (var stmt in script.Statements)
            {
                if (stmt is CreatePageStatement page)
                {
                    var pageId = $"page:{page.Name}";
                    if (addedNodes.Add(pageId))
                        nodes.Add(new DagNodeDto(pageId, page.Name, "page", null));

                    foreach (var visualName in page.SlotMap.Values)
                    {
                        if (!visualPages.ContainsKey(visualName)) continue;
                        var visId = $"vis:{visualName}";
                        edges.Add(new DagEdgeDto(pageId, visId, null));
                    }
                }
                else if (stmt is CreateVisualStatement vis)
                {
                    var visId = $"vis:{vis.Name}";
                    var label = $"{vis.VisualType} · {vis.Name}";
                    visualPages.TryGetValue(vis.Name, out var pages);
                    if (addedNodes.Add(visId))
                        nodes.Add(new DagNodeDto(visId, label, "visual",
                            new
                            {
                                page = pages?.FirstOrDefault(),
                                pages = pages ?? [],
                                visualType = vis.VisualType.ToString(),
                                mappings = vis.Mappings
                                    .Select(m => new { role = m.Role, column = m.Column, display = m.DisplayName })
                                    .ToList(),
                            }));

                    var axisLabel = BuildMappingLabel(vis.Mappings);

                    if (vis.Source.TempTableName is string srcTable)
                    {
                        var srcId = EnsureTableNode(srcTable);
                        edges.Add(new DagEdgeDto(srcId, visId, axisLabel));
                    }
                    else if (vis.Source.InlineSelect is Statement inl)
                    {
                        foreach (var src in inl.GetSourceTables())
                        {
                            var srcId = EnsureTableNode(src);
                            edges.Add(new DagEdgeDto(srcId, visId, axisLabel));
                        }
                    }
                }
            }

            // Enrich table and dataset nodes with column-level lineage for DAG expansion
            var colTracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
            new LineageAnalyzer(colTracker).Analyze(script);
            var allLineage = colTracker.GetFullLineage().ToList();

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node.Type != "table" && node.Type != "dataset") continue;

                var nodeEntries = allLineage
                    .Where(e => e.TargetColumn != null && e.TargetColumn != "*" &&
                                e.TargetTable.Equals(node.Label, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // A bare SELECT * yields only a "*" column with no real lineage —
                // leave the node empty so the dataset bridge can fill it with the
                // upstream dataset's actual columns (pass-through).
                if (nodeEntries.Count == 0) continue;

                var columns = nodeEntries
                    .Select(e => e.TargetColumn!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();

                // Rich per-column lineage: source columns, the transform that
                // produced them, and any inherited description / tags (e.g. pii).
                // Lets the detail panel walk a column back to its origin and show
                // "total = SUM(Amount) ← EDW.Sales.Amount · <description>".
                var columnLineage = nodeEntries
                    .GroupBy(e => e.TargetColumn!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g =>
                    {
                        var e = g.FirstOrDefault(x => x.SourceTables.Count > 0) ?? g.First();
                        var sources = e.SourceTables
                            .Select((t, k) => new { table = t, column = k < e.SourceColumns.Count ? e.SourceColumns[k] : null })
                            .ToList();
                        var tags = e.Metadata
                            .Where(kv => !kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                        return (object)new
                        {
                            sources,
                            transform   = e.TransformationExpression,
                            functions   = e.FunctionsApplied,
                            kind        = e.TransformationKind == TransformationKind.Unknown ? null : e.TransformationKind.ToString(),
                            description = e.DerivedFromDescriptions ?? e.Description,
                            tags        = tags.Count > 0 ? tags : null,
                        };
                    }, StringComparer.OrdinalIgnoreCase);

                nodes[i] = node with { Meta = new { columns, columnLineage } };
            }
        }
        catch (Exception ex)
        {
            return UnprocessableEntity(new { Error = $"Could not parse report script: {ex.Message}" });
        }

        // Best-effort cross-script enrichment: resolve dataset references (built by
        // a separate script) to their column lineage so the detail panel can trace a
        // visual's field back through the dataset to its origin + description.
        try { await BridgeCatalogLineageAsync(nodes, edges); }
        catch { /* never let enrichment fail the structure render */ }
        try { await BridgeDatasetLineageAsync(id, nodes, edges); }
        catch { /* never let enrichment fail the structure render */ }

        return Ok(new DagDto(nodes, edges));

        static string? BuildMappingLabel(List<VisualMapping> mappings)
        {
            var x = mappings.FirstOrDefault(m => m.Role.Equals("XAXIS", StringComparison.OrdinalIgnoreCase))?.Column;
            var y = mappings.FirstOrDefault(m => m.Role.Equals("YAXIS", StringComparison.OrdinalIgnoreCase))?.Column;
            var parts = new List<string>();
            if (x is not null) parts.Add($"X: {x}");
            if (y is not null) parts.Add($"Y: {y}");
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }

        static Dictionary<string, List<string>> BuildVisualPageMap(Script script)
        {
            var visualNames = script.Statements
                .OfType<CreateVisualStatement>()
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var page in script.Statements.OfType<CreatePageStatement>())
            {
                foreach (var target in page.SlotMap.Values)
                {
                    if (!visualNames.Contains(target)) continue;
                    if (!map.TryGetValue(target, out var pages))
                    {
                        pages = [];
                        map[target] = pages;
                    }
                    if (!pages.Contains(page.Name, StringComparer.OrdinalIgnoreCase))
                        pages.Add(page.Name);
                }
            }

            return map;
        }
    }

    private static string NormalizeName(string? s) => (s ?? string.Empty).TrimStart('&', '#');

    // Resolve a registered dataset's column lineage by stitching two sources:
    //  - parsing its stored SourceQuery (column transform + source columns), and
    //  - the persisted lineage catalog from its own build run (inherited
    //    description / tags such as pii — which the SQL text alone cannot supply).
    private (List<string> Columns, Dictionary<string, object> Lineage) ResolveDatasetColumnLineage(
        Dataset ds, IEnumerable<LineageHistoryEntry> persistedEntries)
    {
        var parsed = new Dictionary<string, LineageEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!string.IsNullOrWhiteSpace(ds.SourceQuery))
            {
                var tokens = new Lexer(ds.SourceQuery).Tokenize();
                var script = new CoreParser(tokens, ds.SourceQuery).Parse();
                var tr = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
                new LineageAnalyzer(tr).Analyze(script);
                foreach (var e in tr.GetFullLineage())
                    if (e.TargetColumn != null && !parsed.ContainsKey(e.TargetColumn))
                        parsed[e.TargetColumn] = e;
            }
        }
        catch { /* unparseable source query — fall back to persisted lineage only */ }

        // persistedEntries are pre-fetched by the caller in one batch query, ordered so the
        // "dataset:&name" variant precedes "dataset:name"; first occurrence per column wins.
        var persisted = new Dictionary<string, LineageHistoryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in persistedEntries)
            if (e.TargetColumn != null && !persisted.ContainsKey(e.TargetColumn))
                persisted[e.TargetColumn] = e;

        var columns = new List<string>();
        var lineage = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var col in parsed.Keys.Concat(persisted.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c))
        {
            parsed.TryGetValue(col, out var p);
            persisted.TryGetValue(col, out var h);

            var srcTables = (p?.SourceTables ?? (IReadOnlyList<string>?)h?.SourceTables) ?? new List<string>();
            var srcCols   = (p?.SourceColumns ?? (IReadOnlyList<string>?)h?.SourceColumns) ?? new List<string>();
            var sources = srcTables
                .Select((t, k) => new { table = t, column = k < srcCols.Count ? srcCols[k] : null })
                .ToList();

            string? description = p?.DerivedFromDescriptions
                ?? (h?.Tags != null && h.Tags.TryGetValue("d", out var hd) ? hd : null)
                ?? h?.DerivedFromDescriptions
                ?? (p?.Metadata != null && p.Metadata.TryGetValue("d", out var pd) ? pd : null);

            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (h?.Tags != null)
                foreach (var kv in h.Tags)
                    if (!kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase)) tags[kv.Key] = kv.Value;
            if (p?.Metadata != null)
                foreach (var kv in p.Metadata)
                    if (!kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase) && !tags.ContainsKey(kv.Key)) tags[kv.Key] = kv.Value;

            columns.Add(col);
            lineage[col] = new
            {
                sources,
                transform   = p?.TransformationExpression ?? h?.TransformationExpression,
                functions   = (object?)p?.FunctionsApplied ?? h?.FunctionsApplied,
                kind        = (p != null && p.TransformationKind != TransformationKind.Unknown) ? p.TransformationKind.ToString() : h?.TransformationKind,
                description,
                tags        = tags.Count > 0 ? tags : null,
            };
        }

        return (columns, lineage);
    }

    // Replace dataset-reference nodes' (and their SELECT * consumers') column
    // lineage with the resolved cross-script lineage.
    private async Task BridgeDatasetLineageAsync(int reportId, List<DagNodeDto> nodes, List<DagEdgeDto> edges)
    {
        var reportDatasets = await db.Datasets.Where(d => d.OwningReportId == reportId).ToListAsync();
        if (reportDatasets.Count == 0) return;

        var dsByNorm = reportDatasets
            .GroupBy(d => NormalizeName(d.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Batch all datasets' persisted lineage in one round-trip (was 2 queries per dataset).
        var persistedTargets = dsByNorm.Keys
            .SelectMany(norm => new[] { $"dataset:&{norm}", $"dataset:{norm}" })
            .ToList();
        var persistedByTarget = (await lineageCatalog.GetHistoryForTablesAsync(persistedTargets, 500))
            .GroupBy(e => e.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LineageHistoryEntry>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        var resolved = new Dictionary<string, (List<string> Columns, Dictionary<string, object> Lineage)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in dsByNorm)
        {
            var persistedEntries = new[] { $"dataset:&{kvp.Key}", $"dataset:{kvp.Key}" }
                .SelectMany(t => persistedByTarget.TryGetValue(t, out var l) ? l : Array.Empty<LineageHistoryEntry>());
            var r = ResolveDatasetColumnLineage(kvp.Value, persistedEntries);
            if (r.Columns.Count > 0) resolved[kvp.Key] = r;
        }
        if (resolved.Count == 0) return;

        // 1. Enrich the dataset-reference nodes themselves.
        var datasetRefCols = new Dictionary<string, (List<string> Columns, string Label)>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Type != "table" && node.Type != "dataset") continue;
            if (!resolved.TryGetValue(NormalizeName(node.Label), out var r)) continue;
            nodes[i] = node with { Type = "dataset", Meta = new { columns = r.Columns, columnLineage = r.Lineage } };
            datasetRefCols[node.Id] = (r.Columns, node.Label);
        }
        if (datasetRefCols.Count == 0) return;

        // 2. Propagate to temp tables that SELECT * from a single dataset ref
        //    (e.g. SELECT * INTO #sales FROM &sales_snap) — pass-through columns
        //    pointing back at the dataset so the chain stays connected.
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Type != "table" && node.Type != "dataset") continue;
            if (node.Meta != null) continue;   // already has column lineage from the report script
            var inbound = edges.Where(e => e.Target == node.Id && datasetRefCols.ContainsKey(e.Source)).ToList();
            if (inbound.Count != 1) continue;  // only the unambiguous single-source case
            var (cols, label) = datasetRefCols[inbound[0].Source];
            var passthrough = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cols)
                passthrough[c] = new
                {
                    sources = new[] { new { table = label, column = (string?)c } },
                    transform = (string?)null,
                    functions = (object?)null,
                    kind = "PassThrough",
                    description = (string?)null,
                    tags = (object?)null,
                };
            nodes[i] = node with { Meta = new { columns = cols, columnLineage = passthrough } };
        }
    }

    // Enrich raw source-table nodes from persisted runtime DB_CATALOG lineage.
    // This avoids portal-time DB round-trips while still making SELECT * consumers
    // inspectable after a report has run with catalog import enabled.
    private async Task BridgeCatalogLineageAsync(List<DagNodeDto> nodes, List<DagEdgeDto> edges)
    {
        var resolved = new Dictionary<string, (List<string> Columns, Dictionary<string, object> Lineage, string Label)>(StringComparer.OrdinalIgnoreCase);

        // Fetch persisted catalog lineage for every unresolved table node in one round-trip
        // (was an N+1: one SQLite query per node).
        var tableLabels = nodes
            .Where(n => n.Type == "table" && n.Meta == null)
            .Select(n => n.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (tableLabels.Count == 0) return;

        var historyByTable = (await lineageCatalog.GetHistoryForTablesAsync(tableLabels, 500))
            .GroupBy(e => e.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Type != "table" || node.Meta != null) continue;
            if (!historyByTable.TryGetValue(node.Label, out var nodeHistory)) continue;

            var history = nodeHistory
                .Where(e => e.Operation.Equals("DB_CATALOG", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(e.TargetColumn))
                .GroupBy(e => e.TargetColumn!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(e => e.RunAt).First())
                .OrderBy(e => e.TargetColumn, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (history.Count == 0) continue;

            var columns = history.Select(e => e.TargetColumn!).ToList();
            var lineage = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in history)
            {
                var tags = e.Tags
                    .Where(kv => !kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                var description = e.Tags.TryGetValue("d", out var d)
                    ? d
                    : e.DerivedFromDescriptions;

                lineage[e.TargetColumn!] = new
                {
                    sources = Array.Empty<object>(),
                    transform = (string?)null,
                    functions = (object?)null,
                    kind = "Catalog",
                    description,
                    tags = tags.Count > 0 ? tags : null,
                };
            }

            nodes[i] = node with { Meta = new { columns, columnLineage = lineage } };
            resolved[node.Id] = (columns, lineage, node.Label);
        }

        if (resolved.Count == 0) return;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Type != "table" || node.Meta != null) continue;

            var inbound = edges.Where(e => e.Target == node.Id && resolved.ContainsKey(e.Source)).ToList();
            if (inbound.Count != 1) continue;

            var (cols, _, label) = resolved[inbound[0].Source];
            var passthrough = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cols)
            {
                passthrough[c] = new
                {
                    sources = new[] { new { table = label, column = (string?)c } },
                    transform = (string?)null,
                    functions = (object?)null,
                    kind = "PassThrough",
                    description = (string?)null,
                    tags = (object?)null,
                };
            }

            nodes[i] = node with { Meta = new { columns = cols, columnLineage = passthrough } };
        }
    }

    // ── GET /api/reports/{id}/history ────────────────────────────────────────

    [HttpGet("reports/{id:int}/history")]
    public async Task<IActionResult> GetHistory(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        var snapshots = await db.ReportSnapshots
            .Where(s => s.ReportId == id)
            .OrderByDescending(s => s.BuiltAt)
            .Select(s => new ReportHistorySnapshotDto(
                s.Id,
                s.BuiltAt,
                s.BuiltBy,
                s.ManifestPath,
                s.ScriptHashAtRunTime,
                s.HashMatched,
                s.ParametersJson))
            .ToListAsync();

        var resourceId = id.ToString();
        var changes = await db.AuditLogs
            .Where(a => a.ResourceType == "Report" && a.ResourceId == resourceId)
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new ReportHistoryChangeDto(
                a.Id,
                a.Action,
                a.Timestamp,
                a.UserId,
                a.Detail))
            .ToListAsync();

        var currentHash = await scriptInspection.ReadCurrentScriptHashAsync(report.ScriptPath);
        var scriptChanged = currentHash is not null
            && report.PublishedScriptHash is not null
            && !string.Equals(currentHash, report.PublishedScriptHash, StringComparison.OrdinalIgnoreCase);

        return Ok(new ReportHistoryDto(
            new ReportDependencyReportDto(report.Id, report.Name, report.Folder?.Path ?? "", report.ScriptPath),
            report.PublishedScriptHash,
            currentHash,
            scriptChanged,
            snapshots,
            changes));
    }

    // ── PUT /api/reports/{id} ─────────────────────────────────────────────────

    [HttpPut("reports/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateReportRequest req)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        if (req.Name is not null)        report.Name        = req.Name;
        if (req.Description is not null) report.Description = req.Description;
        if (req.Owner is not null)         report.Owner         = req.Owner;
        if (req.Contact is not null)       report.Contact       = req.Contact;
        if (req.Tags is not null)          report.Tags          = req.Tags;
        if (req.Category is not null)      report.Category      = req.Category;
        if (req.Domain is not null)        report.Domain        = req.Domain;
        if (req.Steward is not null)       report.Steward       = req.Steward;
        if (req.Certification is not null) report.Certification = req.Certification;
        if (req.FolderId.HasValue)
        {
            var targetPerm = await GetEffectivePermissionAsync(req.FolderId.Value);
            if (targetPerm is null || targetPerm < FolderPermission.Manage)
                return Forbid();
            report.FolderId = req.FolderId.Value;
        }

        if (req.ScriptPath is not null)
        {
            var scriptRoot = portalConfig.ScriptRootPath;
            if (string.IsNullOrWhiteSpace(scriptRoot))
                return BadRequest(new { error = "ScriptRootPath is not configured." });

            if (!PortalPathGuard.TryResolveScript(portalConfig, req.ScriptPath, out var resolved))
                return BadRequest(new { error = "Script path must be within the configured ScriptRootPath" });

            if (!System.IO.File.Exists(resolved))
                return BadRequest(new { error = $"Script file not found: {req.ScriptPath}" });

            var validation = await scriptInspection.ValidateResolvedScriptAsync(resolved);
            if (!validation.IsValid)
                return BadRequest(validation);

            report.ScriptPath = resolved;
            report.PublishedScriptHash = validation.Hash;
            report.ScriptLastModified  = validation.LastModified ?? DateTime.UtcNow;
            var scriptMetadata = new Dictionary<string, string>(validation.Metadata, StringComparer.OrdinalIgnoreCase);
            report.MetadataJson = SerializeMetadata(scriptMetadata);
            report.Description   = FirstNonBlank(req.Description, GetMetadata(scriptMetadata, "description", "d"), report.Description);
            report.Owner         = FirstNonBlank(req.Owner, GetMetadata(scriptMetadata, "owner"), report.Owner);
            report.Contact       = FirstNonBlank(req.Contact, GetMetadata(scriptMetadata, "contact"), report.Contact);
            report.Tags          = FirstNonBlank(req.Tags, GetMetadata(scriptMetadata, "tags"), report.Tags);
            report.Category      = FirstNonBlank(req.Category, GetMetadata(scriptMetadata, "category"), report.Category);
            report.Domain        = FirstNonBlank(req.Domain, GetMetadata(scriptMetadata, "domain"), report.Domain);
            report.Steward       = FirstNonBlank(req.Steward, GetMetadata(scriptMetadata, "steward"), report.Steward);
            report.Certification = FirstNonBlank(req.Certification, GetMetadata(scriptMetadata, "certification", "trusted"), report.Certification);
        }

        report.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_REPORT", "Report", id.ToString());

        var isFavorite = await db.ReportFavorites.AnyAsync(f => f.UserId == CurrentUserId && f.ReportId == report.Id);
        return Ok(ToDto(report, null, isFavorite));
    }

    // ── POST /api/reports/{id}/favorite ──────────────────────────────────────

    [HttpPost("reports/{id:int}/favorite")]
    public async Task<IActionResult> AddFavorite(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        var exists = await db.ReportFavorites.AnyAsync(f => f.UserId == CurrentUserId && f.ReportId == id);
        if (!exists)
        {
            db.ReportFavorites.Add(new ReportFavorite { UserId = CurrentUserId, ReportId = id });
            await db.SaveChangesAsync();
            await audit.LogAsync(CurrentUserId, "FAVORITE_REPORT", "Report", id.ToString(), report.Name);
        }

        return NoContent();
    }

    // ── DELETE /api/reports/{id}/favorite ────────────────────────────────────

    [HttpDelete("reports/{id:int}/favorite")]
    public async Task<IActionResult> RemoveFavorite(int id)
    {
        var favorite = await db.ReportFavorites
            .FirstOrDefaultAsync(f => f.UserId == CurrentUserId && f.ReportId == id);
        if (favorite is null) return NoContent();

        db.ReportFavorites.Remove(favorite);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UNFAVORITE_REPORT", "Report", id.ToString());
        return NoContent();
    }

    // ── POST /api/reports/{id}/share-links ──────────────────────────────────

    [HttpPost("reports/{id:int}/share-links")]
    public async Task<IActionResult> CreateShareLink(int id, [FromBody] CreateReportShareLinkRequest? req)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Execute) return Forbid();

        if (req?.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            return BadRequest(new { error = "Share link expiration must be in the future." });

        var link = new ReportShareLink
        {
            ReportId = id,
            CreatedBy = CurrentUserId,
            Token = await GenerateUniqueShareTokenAsync(),
            ExpiresAt = req?.ExpiresAt
        };
        db.ReportShareLinks.Add(link);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_REPORT_SHARE_LINK", "Report", id.ToString(), report.Name);

        link.Report = report;
        return CreatedAtAction(nameof(ResolveShareLink), new { token = link.Token }, ToShareLinkDto(link));
    }

    // ── GET /api/reports/{id}/share-links ───────────────────────────────────

    [HttpGet("reports/{id:int}/share-links")]
    public async Task<IActionResult> GetShareLinks(int id)
    {
        var report = await db.Reports
            .Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || (perm < FolderPermission.Manage && !IsAdmin)) return Forbid();

        var links = await db.ReportShareLinks
            .Include(l => l.Report).ThenInclude(r => r.Folder)
            .Where(l => l.ReportId == id)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        return Ok(links.Select(ToShareLinkDto));
    }

    // ── DELETE /api/reports/{id}/share-links/{token} ────────────────────────

    [HttpDelete("reports/{id:int}/share-links/{token}")]
    public async Task<IActionResult> RevokeShareLink(int id, string token)
    {
        var link = await db.ReportShareLinks
            .Include(l => l.Report)
            .FirstOrDefaultAsync(l => l.ReportId == id && l.Token == token);
        if (link is null) return NoContent();

        var perm = await GetEffectivePermissionAsync(link.Report.FolderId);
        if (perm is null || (perm < FolderPermission.Manage && link.CreatedBy != CurrentUserId)) return Forbid();

        if (link.RevokedAt is null)
        {
            link.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await audit.LogAsync(CurrentUserId, "REVOKE_REPORT_SHARE_LINK", "Report", id.ToString(), token);
        }

        return NoContent();
    }

    // ── GET /api/share/{token} ──────────────────────────────────────────────

    [HttpGet("share/{token}")]
    public async Task<IActionResult> ResolveShareLink(string token)
    {
        var link = await db.ReportShareLinks
            .Include(l => l.Report).ThenInclude(r => r.Folder)
            .FirstOrDefaultAsync(l => l.Token == token);
        if (link is null || link.Report.IsDeleted) return NotFound();
        if (link.RevokedAt is not null) return NotFound();
        if (link.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow) return NotFound();

        var perm = await GetEffectivePermissionAsync(link.Report.FolderId);
        if (perm is null) return Forbid();

        return Ok(new ReportShareResolutionDto(
            link.ReportId,
            link.Report.Name,
            link.Report.Folder.Path,
            $"/reports/{link.ReportId}",
            link.ExpiresAt));
    }

    // ── Embed tokens ────────────────────────────────────────────────────────

    [HttpPost("reports/{id:int}/embed-tokens")]
    public async Task<IActionResult> CreateEmbedToken(int id, [FromBody] CreateReportEmbedTokenRequest? req)
    {
        var report = await db.Reports.Include(r => r.Folder).FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();
        if (req?.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow)
            return BadRequest(new { error = "Embed token expiration must be in the future." });

        var token = new ReportEmbedToken
        {
            ReportId = id,
            CreatedBy = CurrentUserId,
            Name = string.IsNullOrWhiteSpace(req?.Name) ? "Embed token" : req!.Name!,
            Token = await GenerateUniqueEmbedTokenAsync(),
            ExpiresAt = req?.ExpiresAt
        };
        db.ReportEmbedTokens.Add(token);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_REPORT_EMBED_TOKEN", "Report", id.ToString(), report.Name);
        token.Report = report;
        return CreatedAtAction(nameof(ResolveEmbedToken), new { token = token.Token }, ToEmbedTokenDto(token));
    }

    [HttpGet("reports/{id:int}/embed-tokens")]
    public async Task<IActionResult> GetEmbedTokens(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        var tokens = await db.ReportEmbedTokens
            .Include(t => t.Report).ThenInclude(r => r.Folder)
            .Where(t => t.ReportId == id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        return Ok(tokens.Select(ToEmbedTokenDto));
    }

    [HttpDelete("reports/{id:int}/embed-tokens/{token}")]
    public async Task<IActionResult> RevokeEmbedToken(int id, string token)
    {
        var embed = await db.ReportEmbedTokens.Include(t => t.Report).FirstOrDefaultAsync(t => t.ReportId == id && t.Token == token);
        if (embed is null) return NoContent();
        var perm = await GetEffectivePermissionAsync(embed.Report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();
        if (embed.RevokedAt is null)
        {
            embed.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await audit.LogAsync(CurrentUserId, "REVOKE_REPORT_EMBED_TOKEN", "Report", id.ToString(), token);
        }
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("embed/{token}")]
    public async Task<IActionResult> ResolveEmbedToken(string token)
    {
        var embed = await db.ReportEmbedTokens.Include(t => t.Report).ThenInclude(r => r.Folder).FirstOrDefaultAsync(t => t.Token == token);
        if (embed is null || embed.Report.IsDeleted) return NotFound();
        if (embed.RevokedAt is not null) return NotFound();
        if (embed.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow) return NotFound();
        return Ok(new ReportShareResolutionDto(embed.ReportId, embed.Report.Name, embed.Report.Folder.Path, $"/reports/{embed.ReportId}", embed.ExpiresAt));
    }

    // ── Saved parameter/filter views ────────────────────────────────────────

    [HttpGet("reports/{id:int}/saved-views")]
    public async Task<IActionResult> GetSavedViews(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();
        var views = await db.SavedReportViews.Where(v => v.ReportId == id && v.UserId == CurrentUserId).OrderBy(v => v.Name).ToListAsync();
        return Ok(views.Select(ToSavedViewDto));
    }

    [HttpPost("reports/{id:int}/saved-views")]
    public async Task<IActionResult> CreateSavedView(int id, [FromBody] CreateSavedReportViewRequest req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Saved view name is required." });
        if (req.IsDefault) await ClearDefaultSavedViewsAsync(id);

        var view = new SavedReportView
        {
            ReportId = id,
            UserId = CurrentUserId,
            Name = req.Name,
            ParametersJson = SerializeDictionary(req.Parameters),
            FiltersJson = SerializeDictionary(req.Filters),
            IsDefault = req.IsDefault
        };
        db.SavedReportViews.Add(view);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_SAVED_REPORT_VIEW", "Report", id.ToString(), req.Name);
        return CreatedAtAction(nameof(GetSavedViews), new { id }, ToSavedViewDto(view));
    }

    [HttpPut("reports/{id:int}/saved-views/{viewId:int}")]
    public async Task<IActionResult> UpdateSavedView(int id, int viewId, [FromBody] UpdateSavedReportViewRequest req)
    {
        var view = await db.SavedReportViews.FirstOrDefaultAsync(v => v.Id == viewId && v.ReportId == id && v.UserId == CurrentUserId);
        if (view is null) return NotFound();
        if (req.Name is not null) view.Name = req.Name;
        if (req.Parameters is not null) view.ParametersJson = SerializeDictionary(req.Parameters);
        if (req.Filters is not null) view.FiltersJson = SerializeDictionary(req.Filters);
        if (req.IsDefault.HasValue)
        {
            if (req.IsDefault.Value) await ClearDefaultSavedViewsAsync(id);
            view.IsDefault = req.IsDefault.Value;
        }
        view.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_SAVED_REPORT_VIEW", "Report", id.ToString(), view.Name);
        return Ok(ToSavedViewDto(view));
    }

    [HttpDelete("reports/{id:int}/saved-views/{viewId:int}")]
    public async Task<IActionResult> DeleteSavedView(int id, int viewId)
    {
        var view = await db.SavedReportViews.FirstOrDefaultAsync(v => v.Id == viewId && v.ReportId == id && v.UserId == CurrentUserId);
        if (view is null) return NoContent();
        db.SavedReportViews.Remove(view);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DELETE_SAVED_REPORT_VIEW", "Report", id.ToString(), view.Name);
        return NoContent();
    }

    // ── Alerts ───────────────────────────────────────────────────────────────

    [HttpGet("reports/{id:int}/alerts")]
    public async Task<IActionResult> GetAlerts(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();
        var alerts = await db.ReportAlerts.Where(a => a.ReportId == id && (IsAdmin || a.OwnerId == CurrentUserId)).OrderBy(a => a.Name).ToListAsync();
        return Ok(alerts.Select(ToAlertDto));
    }

    [HttpPost("reports/{id:int}/alerts")]
    public async Task<IActionResult> CreateAlert(int id, [FromBody] CreateReportAlertRequest req)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();
        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Execute) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.VisualName))
            return BadRequest(new { error = "Alert name and visualName are required." });
        if (!IsSupportedAlertOperator(req.Operator)) return BadRequest(new { error = "Unsupported alert operator." });

        var alert = new ReportAlert
        {
            ReportId = id,
            OwnerId = CurrentUserId,
            Name = req.Name,
            VisualName = req.VisualName,
            Operator = req.Operator,
            Threshold = req.Threshold,
            Recipient = req.Recipient,
            SmtpAlias = req.SmtpAlias
        };
        db.ReportAlerts.Add(alert);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "CREATE_REPORT_ALERT", "Report", id.ToString(), req.Name);
        return CreatedAtAction(nameof(GetAlerts), new { id }, ToAlertDto(alert));
    }

    [HttpPut("reports/{id:int}/alerts/{alertId:int}")]
    public async Task<IActionResult> UpdateAlert(int id, int alertId, [FromBody] UpdateReportAlertRequest req)
    {
        var alert = await db.ReportAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.ReportId == id);
        if (alert is null) return NotFound();
        if (!IsAdmin && alert.OwnerId != CurrentUserId) return Forbid();
        if (req.Name is not null) alert.Name = req.Name;
        if (req.VisualName is not null) alert.VisualName = req.VisualName;
        if (req.Operator is not null)
        {
            if (!IsSupportedAlertOperator(req.Operator)) return BadRequest(new { error = "Unsupported alert operator." });
            alert.Operator = req.Operator;
        }
        if (req.Threshold.HasValue) alert.Threshold = req.Threshold.Value;
        if (req.Recipient is not null) alert.Recipient = req.Recipient;
        if (req.SmtpAlias is not null) alert.SmtpAlias = req.SmtpAlias;
        if (req.IsActive.HasValue) alert.IsActive = req.IsActive.Value;
        alert.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "UPDATE_REPORT_ALERT", "Report", id.ToString(), alert.Name);
        return Ok(ToAlertDto(alert));
    }

    [HttpDelete("reports/{id:int}/alerts/{alertId:int}")]
    public async Task<IActionResult> DeleteAlert(int id, int alertId)
    {
        var alert = await db.ReportAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.ReportId == id);
        if (alert is null) return NoContent();
        if (!IsAdmin && alert.OwnerId != CurrentUserId) return Forbid();
        db.ReportAlerts.Remove(alert);
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DELETE_REPORT_ALERT", "Report", id.ToString(), alert.Name);
        return NoContent();
    }

    // ── GET /api/reports/{id}/parameters ─────────────────────────────────────

    /// <summary>
    /// Parses the report script and returns metadata for all INPUT-declared parameters.
    /// No script execution occurs. Used by the subscription UI to build parameter forms.
    /// </summary>
    [HttpGet("reports/{id:int}/parameters")]
    public async Task<IActionResult> GetParameters(int id)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null) return Forbid();

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolvedScriptPath))
            return Forbid();

        if (!System.IO.File.Exists(resolvedScriptPath))
            return Ok(Array.Empty<ReportParameterDto>());

        var scriptText = await System.IO.File.ReadAllTextAsync(resolvedScriptPath);
        var tokens     = new Lexer(scriptText).Tokenize();
        var parser     = new CoreParser(tokens, scriptText);
        var script     = parser.Parse();

        var parameters = script.Statements
            .OfType<DeclareStatement>()
            .Where(d => d.IsInput)
            .Select(d => new ReportParameterDto(
                d.VariableName,
                d.DataType,
                d.InitialValue is LiteralExpression lit ? lit.Value?.ToString() : null,
                d.InitialValue is null,
                d.Description))
            .ToList();

        return Ok(parameters);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? GetMetadata(IReadOnlyDictionary<string, string> metadata, params string[] keys)
    {
        foreach (var key in keys)
            if (metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
    }

    private static string? SerializeMetadata(IReadOnlyDictionary<string, string> metadata) =>
        metadata.Count == 0
            ? null
            : JsonSerializer.Serialize(metadata
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value));

    private static IReadOnlyDictionary<string, string> DeserializeMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
            return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool CanReadDataset(Dataset dataset, IReadOnlyCollection<int> groupIds)
    {
        if (IsAdmin) return true;
        if (dataset.AccessLevel == DatasetAccessLevel.Public) return true;
        if (dataset.OwningReport?.CreatedBy == CurrentUserId) return true;

        return dataset.Acls.Any(a =>
            groupIds.Contains(a.GroupId)
            && a.Permission is DatasetPermission.Viewer
                or DatasetPermission.Refresh
                or DatasetPermission.Editor
                or DatasetPermission.Owner);
    }

    // ── DELETE /api/reports/{id} ──────────────────────────────────────────────

    [HttpDelete("reports/{id:int}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] bool cascade = false)
    {
        var report = await db.Reports
            .Include(r => r.Subscriptions.Where(s => s.IsActive))
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        bool hasActive = report.Subscriptions.Any();
        if (hasActive && !cascade)
            return Conflict(new { error = "Report has active subscriptions. Use ?cascade=true." });

        if (cascade)
            foreach (var sub in report.Subscriptions)
                sub.IsActive = false;

        var datasetNames = await db.Datasets
            .Where(d => d.OwningReportId == report.Id)
            .Select(d => d.Name)
            .ToListAsync();
        foreach (var datasetName in datasetNames)
            await datasetRegistry.Delete(datasetName);

        report.IsDeleted = true;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DELETE_REPORT", "Report", id.ToString(), report.Name);
        return NoContent();
    }

    // ── GET /api/reports/{id}/script-content ─────────────────────────────────

    [HttpGet("reports/{id:int}/script-content")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> GetScriptContent(int id)
    {
        var report = await db.Reports.Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolved))
            return Forbid();

        var text = System.IO.File.Exists(resolved)
            ? await System.IO.File.ReadAllTextAsync(resolved)
            : string.Empty;
        return Ok(new ScriptContentResponse(text));
    }

    // ── PUT /api/reports/{id}/script-content ──────────────────────────────────

    [HttpPut("reports/{id:int}/script-content")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> SaveScriptContent(int id, [FromBody] ScriptContentRequest req)
    {
        var report = await db.Reports.Include(r => r.Folder)
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
        if (report is null) return NotFound();

        var perm = await GetEffectivePermissionAsync(report.FolderId);
        if (perm is null || perm < FolderPermission.Manage) return Forbid();

        if (!PortalPathGuard.TryResolveScript(portalConfig, report.ScriptPath, out var resolved))
            return Forbid();

        await System.IO.File.WriteAllTextAsync(resolved, req.ScriptText, System.Text.Encoding.UTF8);

        var hash = "sha256:" + Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(req.ScriptText))).ToLowerInvariant();
        report.PublishedScriptHash = hash;
        report.ScriptLastModified  = DateTime.UtcNow;
        report.UpdatedAt           = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(CurrentUserId, "DESIGNER_SAVE", "Report", id.ToString(), report.Name);
        return NoContent();
    }

    // ── POST /api/scripts/upload ──────────────────────────────────────────────

    [HttpPost("scripts/upload")]
    [Authorize(Roles = "Admin,Publisher")]
    public async Task<IActionResult> UploadScript([FromBody] UploadScriptRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Filename))
            return BadRequest(new { error = "Filename is required." });

        // Reject any path separators — filename only, no subdirectory traversal.
        if (req.Filename.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
            || req.Filename.Contains('/') || req.Filename.Contains('\\'))
            return BadRequest(new { error = "Filename must not contain path separators." });

        if (!req.Filename.EndsWith(".rptsql", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .rptsql files may be uploaded." });

        byte[] content;
        try { content = Convert.FromBase64String(req.ContentBase64); }
        catch { return BadRequest(new { error = "ContentBase64 is not valid base64." }); }

        var root = portalConfig.ScriptRootPath;
        if (string.IsNullOrWhiteSpace(root))
            return StatusCode(503, new { error = "ScriptRootPath is not configured on the portal." });

        Directory.CreateDirectory(root);
        var destination = System.IO.Path.Combine(root, req.Filename);

        await System.IO.File.WriteAllBytesAsync(destination, content);

        var relativePath = System.IO.Path.GetRelativePath(root, destination).Replace('\\', '/');
        return Ok(new UploadScriptResponse(relativePath));
    }

    // ── GET /api/reports/available-scripts ───────────────────────────────────

    [HttpGet("reports/available-scripts")]
    [Authorize(Roles = "Admin,Publisher")]
    public IActionResult GetAvailableScripts()
    {
        var root = portalConfig.ScriptRootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) 
            return Ok(Array.Empty<string>());

        var files = Directory.GetFiles(root, "*.rptsql", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(f => f)
            .ToList();

        return Ok(files);
    }

    // ── GET /api/maps/custom ─────────────────────────────────────────────────

    [HttpGet("maps/custom")]
    public async Task<IActionResult> GetCustomMap([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "Path is required." });

        if (Path.IsPathRooted(path))
            return BadRequest(new { error = "Map path must be relative." });

        var ext = Path.GetExtension(path);
        if (!ext.Equals(".geojson", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Only .json and .geojson map files are supported." });
        }

        if (!PortalPathGuard.TryResolveMap(portalConfig, path, out var resolved))
            return Forbid();

        if (!System.IO.File.Exists(resolved))
            return NotFound(new { error = "Map file not found." });

        var json = await System.IO.File.ReadAllTextAsync(resolved);
        return Content(json, "application/geo+json");
    }

}
