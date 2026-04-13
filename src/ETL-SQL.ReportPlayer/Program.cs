using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ETL_SQL.ReportBuilder;

using ETL_SQL.ReportPlayer;

// ─────────────────────────────────────────────────────────────────────────────
// ReportPlayer — Phase 9D
//
// Usage:  etl-sql-report serve <script.rptsql>   (from the CLI)
//         dotnet run -- serve <script.rptsql>      (direct)
//
// Starts a Kestrel web server and opens the report dashboard in the browser.
// ─────────────────────────────────────────────────────────────────────────────

var scriptPath = args.Length > 0 ? args[0] : null;
if (scriptPath == null || !File.Exists(scriptPath))
{
    Console.Error.WriteLine("Usage: etl-sql-report serve <script.rptsql>");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Register DashboardService as a singleton — one script, one state
builder.Services.AddSingleton(new DashboardService(Path.GetFullPath(scriptPath)));

var app = builder.Build();

// ── Serve static report-runtime.js from embedded resources (or media dir) ────
app.UseStaticFiles();

// ── GET /api/manifest — return the full current manifest ─────────────────────
app.MapGet("/api/manifest", async (DashboardService svc) =>
{
    var manifest = await svc.GetManifestAsync();
    return Results.Json(manifest, new JsonSerializerOptions { WriteIndented = false });
});

// ── POST /api/parameter — update a parameter and return refreshed manifest ───
app.MapPost("/api/parameter", async (HttpContext ctx, DashboardService svc) =>
{
    var body = await JsonSerializer.DeserializeAsync<ParameterUpdateRequest>(ctx.Request.Body);
    if (body == null || string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest("name is required");

    var manifest = await svc.SetParameterAsync(body.Name, body.Value ?? "");
    return Results.Json(manifest, new JsonSerializerOptions { WriteIndented = false });
});

// ── GET / — serve the dashboard HTML shell ────────────────────────────────────
app.MapGet("/", async (DashboardService svc) =>
{
    var manifest = await svc.GetManifestAsync();
    var isStale  = svc.IsStale(TimeSpan.FromHours(24));

    // Staleness banner injected if manifest is older than TTL
    var staleBanner = isStale
        ? "<div class=\"stale-banner\">⚠ Snapshot may be stale — run <code>etl-sql-report refresh</code> to update.</div>"
        : "";

    var html = GetDashboardHtml(manifest, staleBanner);
    return Results.Content(html, "text/html");
});

// ── GET /api/refresh — force a full rebuild ───────────────────────────────────
app.MapGet("/api/refresh", async (DashboardService svc) =>
{
    var manifest = await svc.RebuildAsync();
    return Results.Json(new { rebuilt = true, visuals = manifest.Visuals.Count });
});

// ── Configuration ─────────────────────────────────────────────────────────────
int port = builder.Configuration.GetValue<int>("ReportPlayer:Port", 5200);

Console.WriteLine($"ReportPlayer: serving {Path.GetFileName(scriptPath)}");
Console.WriteLine($"Dashboard: http://localhost:{port}");

app.Urls.Add($"http://localhost:{port}");
app.Run();


// ── Helpers ───────────────────────────────────────────────────────────────────

static string GetDashboardHtml(ReportManifest manifest, string staleBanner)
{
    var manifestJson = JsonSerializer.Serialize(manifest,
        new JsonSerializerOptions { WriteIndented = false })
        .Replace("<", "\\u003c");

    const string css = @"
  * { box-sizing: border-box; }
  body { font-family: -apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;
         margin: 0; padding: 16px; background: #f5f5f5; color: #222; }
  h1   { margin-bottom: 4px; font-size: 1.4em; }
  h2   { border-bottom: 2px solid #ccc; padding-bottom: 4px; margin-top: 32px; }
  h3   { margin-bottom: 8px; }
  .visual-card  { background: #fff; border: 1px solid #ddd; border-radius: 6px;
                  padding: 16px; margin-bottom: 24px; }
  .chart-wrapper { max-width: 640px; }
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
  .no-data { color: #999; font-style: italic; }
  .error   { color: #c00; }
";
    return
        "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n" +
        "<meta charset=\"UTF-8\">\n" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n" +
        "<title>ETL-SQL Report Dashboard</title>\n" +
        "<style>" + css + "</style>\n</head>\n<body>\n" +
        "<h1>ETL-SQL Report Dashboard</h1>\n" +
        staleBanner + "\n" +
        "<div id=\"root\"></div>\n" +
        "<footer>Powered by ETL-SQL ReportPlayer</footer>\n\n" +
        "<script>window.__MANIFEST__ = " + manifestJson + ";</script>\n" +
        "<script src=\"https://cdn.jsdelivr.net/npm/chart.js@4/dist/chart.umd.min.js\"></script>\n" +
        "<script src=\"/report-runtime.js\"></script>\n" +
        "</body>\n</html>";
}

// ── DTOs ──────────────────────────────────────────────────────────────────────
public class ParameterUpdateRequest
{
    public string? Name  { get; set; }
    public string? Value { get; set; }
}
