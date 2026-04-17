using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ETL_SQL.ReportBuilder;

using ETL_SQL.ReportPlayer;

// ─────────────────────────────────────────────────────────────────────────────
// ReportPlayer — Phase 9F (multi-report hosting)
//
// Single-report:  dotnet run -- <script.rptsql>
// Multi-report:   dotnet run -- --manifest reports.json
// ─────────────────────────────────────────────────────────────────────────────

string? scriptPath   = null;
string? manifestPath = null;

for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--manifest" || args[i] == "-m") && i + 1 < args.Length)
        manifestPath = args[++i];
    else if (!args[i].StartsWith("-"))
        scriptPath = args[i];
}

bool multiMode = manifestPath != null;

if (multiMode)
{
    if (!File.Exists(manifestPath!))
    {
        Console.Error.WriteLine($"error: manifest not found: {manifestPath}");
        return;
    }
}
else
{
    if (scriptPath == null || !File.Exists(scriptPath))
    {
        Console.Error.WriteLine("Usage: etl-sql-report <script.rptsql>");
        Console.Error.WriteLine("       etl-sql-report --manifest reports.json");
        return;
    }
}

var builder = WebApplication.CreateBuilder(args);

DashboardService?        singleSvc = null;
DashboardServiceFactory? factory   = null;

if (multiMode)
{
    factory = new DashboardServiceFactory(Path.GetFullPath(manifestPath!));
    builder.Services.AddSingleton(factory);
}
else
{
    singleSvc = new DashboardService(Path.GetFullPath(scriptPath!));
    builder.Services.AddSingleton(singleSvc);
}

var app = builder.Build();
app.UseStaticFiles();

var noCache = new JsonSerializerOptions { WriteIndented = false };

// ─────────────────────────────────────────────────────────────────────────────
// Multi-report routes
// ─────────────────────────────────────────────────────────────────────────────
if (multiMode)
{
    // GET / — catalog page
    app.MapGet("/", (DashboardServiceFactory fac) =>
        Results.Content(GetCatalogHtml(fac.Reports), "text/html"));

    // GET /reports/{name} — dashboard shell (runtime fetches manifest via API)
    app.MapGet("/reports/{name}", async (string name, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound($"Report '{name}' not found.");
        var entry = fac.Reports.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        var staleBanner = svc.IsStale(TimeSpan.FromHours(24))
            ? "<div class=\"stale-banner\">⚠ Snapshot may be stale — " +
              "run <code>etl-sql-report refresh</code> to update.</div>"
            : "";
        var apiBase = "/reports/" + WebUtility.UrlEncode(name) + "/api";
        return Results.Content(
            GetDashboardHtml(entry?.Name ?? name, entry?.Description, staleBanner, apiBase),
            "text/html");
    });

    app.MapGet("/reports/{name}/api/manifest", async (string name, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        return Results.Json(await svc.GetManifestAsync(), noCache);
    });

    app.MapPost("/reports/{name}/api/parameter",
        async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var body = await JsonSerializer.DeserializeAsync<ParameterUpdateRequest>(ctx.Request.Body);
        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest("name is required");
        return Results.Json(await svc.SetParameterAsync(body.Name, body.Value ?? ""), noCache);
    });

    app.MapPost("/reports/{name}/api/parameters",
        async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var body = await JsonSerializer.DeserializeAsync<ParameterBatchRequest>(ctx.Request.Body);
        if (body?.Params == null || body.Params.Count == 0)
            return Results.BadRequest("params array is required");
        var updates = body.Params
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => (p.Name!, p.Value ?? ""));
        return Results.Json(await svc.SetParametersAsync(updates), noCache);
    });

    app.MapGet("/reports/{name}/api/refresh", async (string name, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var manifest = await svc.RebuildAsync();
        return Results.Json(new { rebuilt = true, visuals = manifest.Visuals.Count });
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// Single-report routes
// ─────────────────────────────────────────────────────────────────────────────
else
{
    app.MapGet("/api/manifest", async (DashboardService svc) =>
        Results.Json(await svc.GetManifestAsync(), noCache));

    app.MapPost("/api/parameter", async (HttpContext ctx, DashboardService svc) =>
    {
        var body = await JsonSerializer.DeserializeAsync<ParameterUpdateRequest>(ctx.Request.Body);
        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest("name is required");
        return Results.Json(await svc.SetParameterAsync(body.Name, body.Value ?? ""), noCache);
    });

    app.MapPost("/api/parameters", async (HttpContext ctx, DashboardService svc) =>
    {
        var body = await JsonSerializer.DeserializeAsync<ParameterBatchRequest>(ctx.Request.Body);
        if (body?.Params == null || body.Params.Count == 0)
            return Results.BadRequest("params array is required");
        var updates = body.Params
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => (p.Name!, p.Value ?? ""));
        return Results.Json(await svc.SetParametersAsync(updates), noCache);
    });

    app.MapGet("/", async (DashboardService svc) =>
    {
        var manifest = await svc.GetManifestAsync();
        var staleBanner = svc.IsStale(TimeSpan.FromHours(24))
            ? "<div class=\"stale-banner\">⚠ Snapshot may be stale — " +
              "run <code>etl-sql-report refresh</code> to update.</div>"
            : "";
        return Results.Content(
            GetDashboardHtml(manifest, staleBanner),
            "text/html");
    });

    app.MapGet("/api/refresh", async (DashboardService svc) =>
    {
        var manifest = await svc.RebuildAsync();
        return Results.Json(new { rebuilt = true, visuals = manifest.Visuals.Count });
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// Start
// ─────────────────────────────────────────────────────────────────────────────
int port = builder.Configuration.GetValue<int>("ReportPlayer:Port", 5200);

if (multiMode)
{
    Console.WriteLine($"ReportPlayer: hosting {factory!.Reports.Count} report(s) from {Path.GetFileName(manifestPath)}");
    Console.WriteLine($"Catalog: http://localhost:{port}");
}
else
{
    Console.WriteLine($"ReportPlayer: serving {Path.GetFileName(scriptPath)}");
    Console.WriteLine($"Dashboard: http://localhost:{port}");
}

app.Urls.Add($"http://localhost:{port}");
app.Run();


// ── HTML builders ─────────────────────────────────────────────────────────────

// Shared CSS block (used by both catalog and dashboard pages)
const string SharedCss = @"
  * { box-sizing: border-box; }
  body { font-family: -apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;
         margin: 0; padding: 16px; background: #f5f5f5; color: #222; }
  h1   { margin-bottom: 4px; font-size: 1.4em; }
  a    { color: #5470c6; }
  .report-desc { color: #555; margin-top: 2px; margin-bottom: 16px; }
  h2   { border-bottom: 2px solid #ccc; padding-bottom: 4px; margin-top: 32px; }
  h3   { margin-bottom: 8px; }
  .visual-card  { background: #fff; border: 1px solid #ddd; border-radius: 6px;
                  padding: 16px; margin-bottom: 24px; }
  .chart-wrapper { width: 100%; max-width: 640px; height: 400px; }
  .table-wrapper { overflow-x: auto; }
  table { border-collapse: collapse; width: 100%; }
  th, td { border: 1px solid #ddd; padding: 4px 8px; text-align: left; }
  th { background: #f0f0f0; }
  .card-value  { display: flex; flex-direction: column; gap: 4px; }
  .card-label  { font-size: 0.85em; color: #888; }
  .card-number { font-size: 2em; font-weight: bold; }
  .slicer-wrapper select { font-size: 1em; padding: 4px 8px; }
  .stale-banner { background: #fff3cd; border: 1px solid #ffc107;
                  border-radius: 4px; padding: 8px 12px; margin-bottom: 16px;
                  font-size: 0.9em; }
  footer { margin-top: 32px; color: #888; font-size: 0.8em; }
  .text-visual { line-height: 1.6; }
  .no-data { color: #999; font-style: italic; }
  .error   { color: #c00; }
  .error-card { border: 1px solid #f5c6cb; border-radius: 4px; padding: 6px 10px; background: #fff5f5; }
  .error-card summary { cursor: pointer; color: #c00; font-weight: 600; }
  .error-card pre { font-size: 0.8em; color: #555; white-space: pre-wrap; margin: 6px 0 0; }
  .clickable tbody tr:hover { background: #f0f4ff; }
  .clickable tbody tr { cursor: pointer; }
  .nav-bar { display: flex; gap: 8px; margin-bottom: 24px; flex-wrap: wrap; }
  .nav-tab, .nav-btn { padding: 6px 16px; border: 1px solid #ccc; border-radius: 4px; cursor: pointer; background: #f0f0f0; font-size: 0.9em; }
  .nav-tab.active, .nav-btn.active { background: #5470c6; color: white; border-color: #5470c6; }
  .nav-link { color: #5470c6; cursor: pointer; font-size: 0.9em; }
  .nav-link.active { font-weight: bold; text-decoration: underline; }
  .nav-sep { color: #ccc; }
  .page { display: none; }
  .page.active { display: block; }
  .page-grid { display: grid; gap: 16px; }
  .container-box { display: flex; flex-direction: column; gap: 12px; }
  .container-scroll { display: flex; flex-direction: column; gap: 12px; overflow-y: auto; }
  .filter-wrapper { display: flex; flex-direction: column; gap: 6px; }
  .filter-wrapper input[type=date], .filter-wrapper input[type=range],
  .filter-wrapper input[type=search], .filter-wrapper select[multiple] {
    font-size: 1em; padding: 4px 8px; border: 1px solid #ccc; border-radius: 4px;
    background: #fff; width: 100%; max-width: 320px; }
  .filter-wrapper select[multiple] { height: 120px; }
  .filter-wrapper .range-value { font-size: 0.9em; color: #555; }
  .filter-wrapper .filter-apply { margin-top: 4px; padding: 4px 14px; border: 1px solid #5470c6;
    border-radius: 4px; background: #5470c6; color: white; cursor: pointer; font-size: 0.9em; }
  .filter-wrapper .filter-apply:hover { background: #3a56a8; }
  @media (max-width: 768px) {
    .page-grid { grid-template-areas: none !important; grid-template-columns: 1fr !important; }
    .page-grid > div { grid-area: auto !important; }
    .chart-wrapper { height: 240px !important; }
    .nav-bar { flex-direction: column; }
    .visual-card { width: auto !important; }
  }
";

/// <summary>Single-report dashboard with manifest pre-embedded for fast initial load.</summary>
static string GetDashboardHtml(ReportManifest manifest, string staleBanner)
{
    var manifestJson = JsonSerializer.Serialize(manifest,
        new JsonSerializerOptions { WriteIndented = false })
        .Replace("<", "\\u003c");

    var title = manifest.Title ?? "ETL-SQL Report Dashboard";
    return
        "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n" +
        "<meta charset=\"UTF-8\">\n" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n" +
        "<title>" + title + "</title>\n" +
        "<style>" + SharedCss + "</style>\n</head>\n<body>\n" +
        "<h1>" + title + "</h1>\n" +
        (manifest.Description != null ? "<p class=\"report-desc\">" + manifest.Description + "</p>\n" : "") +
        staleBanner + "\n" +
        "<div id=\"root\"></div>\n" +
        "<footer>Powered by ETL-SQL ReportPlayer</footer>\n\n" +
        // Pre-embed manifest; set __IS_WEB__ so interactive controls activate.
        "<script>window.__IS_WEB__ = true; window.__MANIFEST__ = " + manifestJson + ";</script>\n" +
        "<script src=\"https://cdn.jsdelivr.net/npm/echarts@5/dist/echarts.min.js\"></script>\n" +
        "<script src=\"/report-runtime.js\"></script>\n" +
        "</body>\n</html>";
}

/// <summary>Multi-report dashboard shell — manifest is fetched via API on load.</summary>
static string GetDashboardHtml(string reportName, string? description, string staleBanner, string apiBase)
{
    return
        "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n" +
        "<meta charset=\"UTF-8\">\n" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n" +
        "<title>" + reportName + " — ETL-SQL</title>\n" +
        "<style>" + SharedCss + "</style>\n</head>\n<body>\n" +
        "<p><a href=\"/\">&larr; All reports</a></p>\n" +
        "<h1>" + reportName + "</h1>\n" +
        (description != null ? "<p class=\"report-desc\">" + description + "</p>\n" : "") +
        staleBanner + "\n" +
        "<div id=\"root\"></div>\n" +
        "<footer>Powered by ETL-SQL ReportPlayer</footer>\n\n" +
        "<script>window.__IS_WEB__ = true; window.__API_BASE__ = '" + apiBase + "';</script>\n" +
        "<script src=\"https://cdn.jsdelivr.net/npm/echarts@5/dist/echarts.min.js\"></script>\n" +
        "<script src=\"/report-runtime.js\"></script>\n" +
        "</body>\n</html>";
}

/// <summary>Catalog page listing all hosted reports.</summary>
static string GetCatalogHtml(IReadOnlyList<ReportEntry> reports)
{
    const string catalogCss = @"
  .catalog-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
                  gap: 16px; margin-top: 24px; }
  .catalog-card { background: #fff; border: 1px solid #ddd; border-radius: 6px;
                  padding: 20px; text-decoration: none; color: inherit; display: block;
                  transition: border-color 0.15s, box-shadow 0.15s; }
  .catalog-card:hover { border-color: #5470c6; box-shadow: 0 2px 8px rgba(84,112,198,0.18); }
  .catalog-card h2 { margin: 0 0 6px; font-size: 1.1em; color: #5470c6; }
  .catalog-card p  { margin: 0; font-size: 0.85em; color: #888; }
  .catalog-empty   { color: #999; font-style: italic; margin-top: 24px; }
";
    var cards = new System.Text.StringBuilder();
    if (reports.Count == 0)
    {
        cards.Append("<p class=\"catalog-empty\">No reports configured. Add entries to reports.json.</p>");
    }
    else
    {
        cards.Append("<div class=\"catalog-grid\">");
        foreach (var r in reports)
        {
            var href  = "/reports/" + WebUtility.UrlEncode(r.Name);
            var desc  = r.Description != null ? "<p>" + r.Description + "</p>" : "";
            cards.Append($"<a href=\"{href}\" class=\"catalog-card\"><h2>{r.Name}</h2>{desc}</a>");
        }
        cards.Append("</div>");
    }

    return
        "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n" +
        "<meta charset=\"UTF-8\">\n" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n" +
        "<title>ETL-SQL Reports</title>\n" +
        "<style>" + SharedCss + catalogCss + "</style>\n</head>\n<body>\n" +
        "<h1>ETL-SQL Reports</h1>\n" +
        $"<p class=\"report-desc\">{reports.Count} report{(reports.Count == 1 ? "" : "s")} available.</p>\n" +
        cards +
        "<footer>Powered by ETL-SQL ReportPlayer</footer>\n" +
        "</body>\n</html>";
}

// ── DTOs ──────────────────────────────────────────────────────────────────────
public class ParameterUpdateRequest
{
    public string? Name  { get; set; }
    public string? Value { get; set; }
}

public class ParameterBatchRequest
{
    public List<ParameterUpdateRequest>? Params { get; set; }
}
