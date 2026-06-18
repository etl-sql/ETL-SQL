using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Storage;
using ETL_SQL.Reporting;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/reports/{id:int}/export")]
[Authorize]
public class ExportController(
    PortalDbContext db,
    PortalConfig portalConfig,
    AuditService audit,
    IArtifactStorage artifacts) : ControllerBase
{
    // ── Per-user PDF rate limit (tokens per minute) ────────────────────────────
    private static readonly ConcurrentDictionary<int, (int Count, DateTime WindowStart)> _pdfBucket = new();
    private const int PdfRateLimit = 5;

    private int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin => User.IsInRole("Admin");

    // ── Permission check ───────────────────────────────────────────────────────
    private async Task<bool> CanReadAsync(int reportId)
    {
        if (IsAdmin) return true;
        var userId = CurrentUserId;
        var groupIds = await db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId && !r.IsDeleted);
        if (report is null) return false;
        return await db.FolderAcls
            .AnyAsync(a => a.FolderId == report.FolderId && groupIds.Contains(a.GroupId));
    }

    // ── Load manifest from latest snapshot ────────────────────────────────────
    private async Task<(ReportManifest? manifest, string? error, bool forbidden)> LoadManifestAsync(int reportId)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId && !r.IsDeleted);
        if (report is null) return (null, "Report not found", false);

        var snapshot = await db.ReportSnapshots
            .Where(s => s.ReportId == reportId)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync();

        if (snapshot is null)
            return (null, "No snapshot available", false);

        var manifestKey = PortalPathGuard.ToSnapshotKey(portalConfig, snapshot.ManifestPath);
        if (manifestKey is null)
            return (null, "Snapshot path is outside the configured snapshot directory", true);

        if (!await artifacts.ExistsAsync(ArtifactArea.Snapshots, manifestKey))
            return (null, "No snapshot available", false);

        var json = await artifacts.ReadAllTextAsync(ArtifactArea.Snapshots, manifestKey);
        var manifest = JsonSerializer.Deserialize<ReportManifest>(json);
        return manifest is null ? (null, "Failed to load snapshot", false) : (manifest, null, false);
    }

    // ── 4.1  GET /api/reports/{id}/export/csv?visual=<name> ──────────────────
    [HttpGet("csv")]
    public async Task<IActionResult> ExportCsv(int id, [FromQuery] string? visual)
    {
        if (!await CanReadAsync(id)) return Forbid();

        var (manifest, err, forbidden) = await LoadManifestAsync(id);
        if (forbidden) return Forbid();
        if (manifest is null) return NotFound(new { error = err });

        var renderer = new CsvRenderer();
        var visuals = renderer.SelectExportVisuals(manifest, visual);

        if (visuals.Count == 0)
            return NotFound(new { error = "No exportable visuals found" });

        var csv = renderer.Render(manifest, visual, includeVisualNamesWhenMultiple: true);

        var reportName = manifest.Title ?? System.IO.Path.GetFileNameWithoutExtension(manifest.Source);
        var filename = $"{SanitizeFilename(reportName)}_{DateTime.UtcNow:yyyyMMdd}.csv";

        await audit.LogAsync(CurrentUserId, "EXPORT_CSV", "Report", id.ToString(), visual);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),
                    "text/csv; charset=utf-8",
                    filename);
    }

    // ── 4.1b GET /api/reports/{id}/export/xlsx?visual=<name> ──────────────────
    [HttpGet("xlsx")]
    public async Task<IActionResult> ExportXlsx(int id, [FromQuery] string? visual)
    {
        if (!await CanReadAsync(id)) return Forbid();

        var (manifest, err, forbidden) = await LoadManifestAsync(id);
        if (forbidden) return Forbid();
        if (manifest is null) return NotFound(new { error = err });

        var visuals = new CsvRenderer().SelectExportVisuals(manifest, visual);
        if (visuals.Count == 0)
            return NotFound(new { error = "No exportable visuals found" });

        var bytes = await new XlsxExporter().ExportAsync(visuals);

        var reportName = manifest.Title ?? System.IO.Path.GetFileNameWithoutExtension(manifest.Source);
        var filename = $"{SanitizeFilename(reportName)}_{DateTime.UtcNow:yyyyMMdd}.xlsx";

        await audit.LogAsync(CurrentUserId, "EXPORT_XLSX", "Report", id.ToString(), visual);
        return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    filename);
    }

    // ── 4.2  GET /api/reports/{id}/export/pdf ─────────────────────────────────
    [HttpGet("pdf")]
    public async Task<IActionResult> ExportPdf(int id)
    {
        if (!await CanReadAsync(id)) return Forbid();

        // Rate limit: 5 PDFs per user per minute
        var userId = CurrentUserId;
        if (!IsAdmin)
        {
            var now = DateTime.UtcNow;
            var bucket = _pdfBucket.GetOrAdd(userId, _ => (0, now));

            if ((now - bucket.WindowStart).TotalMinutes >= 1)
                bucket = (0, now);

            if (bucket.Count >= PdfRateLimit)
                return StatusCode(429, new { error = "PDF export rate limit reached. Try again in a minute." });

            _pdfBucket[userId] = (bucket.Count + 1, bucket.WindowStart);
        }

        var (manifest, err, forbidden) = await LoadManifestAsync(id);
        if (forbidden) return Forbid();
        if (manifest is null) return NotFound(new { error = err });

        byte[] pdfBytes;
        try
        {
            // Render the already-loaded manifest entirely server-side (charts via ECharts SSR).
            // We deliberately do NOT use the browser/high-fidelity path here: it would navigate a
            // headless browser to the live portal and forward the caller's Authorization header via
            // CDP Network.setExtraHTTPHeaders, which has no URL filter — leaking the viewer's bearer
            // token to every sub-resource the report references (e.g. an attacker-controlled image
            // URL embedded in report content). Static rendering needs no token and no host round-trip.
            var exporter = new ReportPdfExporter();
            pdfBytes = exporter.Export(manifest, new PdfExportOptions
            {
                Mode = PdfExportMode.Static,
                Warn = message => { }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"PDF generation failed: {ex.Message}" });
        }

        var reportName = manifest.Title ?? System.IO.Path.GetFileNameWithoutExtension(manifest.Source);
        var filename = $"{SanitizeFilename(reportName)}_{DateTime.UtcNow:yyyyMMdd}.pdf";

        await audit.LogAsync(CurrentUserId, "EXPORT_PDF", "Report", id.ToString());
        return File(pdfBytes, "application/pdf", filename);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string SanitizeFilename(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
            .Trim().Replace(' ', '_');
    }
}
