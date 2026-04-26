using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportBuilder;

namespace ETL_SQL.ReportPortal.Controllers;

[ApiController]
[Route("api/reports/{id:int}/export")]
[Authorize]
public class ExportController(
    PortalDbContext  db) : ControllerBase
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
        var userId   = CurrentUserId;
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
    private async Task<(ReportManifest? manifest, string? error)> LoadManifestAsync(int reportId)
    {
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId && !r.IsDeleted);
        if (report is null) return (null, "Report not found");

        var snapshot = await db.ReportSnapshots
            .Where(s => s.ReportId == reportId)
            .OrderByDescending(s => s.BuiltAt)
            .FirstOrDefaultAsync();

        if (snapshot is null || !System.IO.File.Exists(snapshot.ManifestPath))
            return (null, "No snapshot available");

        var store    = new SnapshotStore();
        var manifest = await store.LoadAsync(snapshot.ManifestPath);
        return manifest is null ? (null, "Failed to load snapshot") : (manifest, null);
    }

    // ── 4.1  GET /api/reports/{id}/export/csv?visual=<name> ──────────────────
    [HttpGet("csv")]
    public async Task<IActionResult> ExportCsv(int id, [FromQuery] string? visual)
    {
        if (!await CanReadAsync(id)) return Forbid();

        var (manifest, err) = await LoadManifestAsync(id);
        if (manifest is null) return NotFound(new { error = err });

        // Choose visuals: specific one, or all TABLE visuals if none specified
        var visuals = string.IsNullOrWhiteSpace(visual)
            ? manifest.Visuals
                .Where(v => string.Equals(v.VisualType, "TABLE", StringComparison.OrdinalIgnoreCase)
                         && v.Error is null && v.Columns.Count > 0)
                .ToList()
            : manifest.Visuals
                .Where(v => string.Equals(v.Name, visual, StringComparison.OrdinalIgnoreCase))
                .Take(1)
                .ToList();

        if (visuals.Count == 0)
            return NotFound(new { error = "No exportable visuals found" });

        var sb   = new StringBuilder();
        bool first = true;

        foreach (var v in visuals)
        {
            if (!first) sb.AppendLine().AppendLine();
            first = false;

            if (visuals.Count > 1)
                sb.AppendLine(CsvField(v.Name));

            // Header row
            sb.AppendLine(string.Join(",", v.Columns.Select(CsvField)));

            // Data rows
            foreach (var row in v.Rows)
                sb.AppendLine(string.Join(",", v.Columns.Select((_, ci) =>
                    CsvField(ci < row.Count ? row[ci] : null))));
        }

        var reportName = manifest.Title ?? System.IO.Path.GetFileNameWithoutExtension(manifest.Source);
        var filename   = $"{SanitizeFilename(reportName)}_{DateTime.UtcNow:yyyyMMdd}.csv";

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
                    "text/csv; charset=utf-8",
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
            var now    = DateTime.UtcNow;
            var bucket = _pdfBucket.GetOrAdd(userId, _ => (0, now));

            if ((now - bucket.WindowStart).TotalMinutes >= 1)
                bucket = (0, now);

            if (bucket.Count >= PdfRateLimit)
                return StatusCode(429, new { error = "PDF export rate limit reached. Try again in a minute." });

            _pdfBucket[userId] = (bucket.Count + 1, bucket.WindowStart);
        }

        var (manifest, err) = await LoadManifestAsync(id);
        if (manifest is null) return NotFound(new { error = err });

        byte[] pdfBytes;
        try
        {
            var exporter = new PdfExporter();
            pdfBytes = exporter.Export(manifest);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"PDF generation failed: {ex.Message}" });
        }

        var reportName = manifest.Title ?? System.IO.Path.GetFileNameWithoutExtension(manifest.Source);
        var filename   = $"{SanitizeFilename(reportName)}_{DateTime.UtcNow:yyyyMMdd}.pdf";

        return File(pdfBytes, "application/pdf", filename);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string CsvField(string? value)
    {
        if (value is null) return string.Empty;
        // RFC 4180: quote if contains comma, quote, or newline
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
            .Trim().Replace(' ', '_');
    }
}
