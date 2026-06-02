using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ETL_SQL.Reporting;
using ETL_SQL.Orchestrator;
using ETL_SQL.ReportHosting;
using ETL_SQL.Core;

// ─────────────────────────────────────────────────────────────────────────────
// ReportPlayer — Phase 9F (multi-report hosting)
//
// Single-report:  dotnet run -- <script.rptsql>
// Multi-report:   dotnet run -- --manifest reports.json
// ─────────────────────────────────────────────────────────────────────────────

string? scriptPath   = null;
string? manifestPath = null;
int?    portArg      = null;
bool    noBrowser    = false;
string  invocationDir = Directory.GetCurrentDirectory(); // capture before ASP.NET Core changes CWD

for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--manifest" || args[i] == "-m") && i + 1 < args.Length)
        manifestPath = args[++i];
    else if ((args[i] == "--port" || args[i] == "-p") && i + 1 < args.Length
             && int.TryParse(args[++i], out int p))
        portArg = p;
    else if (args[i] == "--no-browser")
        noBrowser = true;
    else if (!args[i].StartsWith("-"))
        scriptPath = args[i];
}

// Resolve relative paths against the directory the user ran the command from,
// not the project/assembly directory (which may differ under `dotnet run`).
if (scriptPath   != null && !Path.IsPathRooted(scriptPath))   scriptPath   = Path.GetFullPath(scriptPath,   invocationDir);
if (manifestPath != null && !Path.IsPathRooted(manifestPath)) manifestPath = Path.GetFullPath(manifestPath, invocationDir);

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
        Console.Error.WriteLine("Usage: etl-sql-report <script.rptsql> [--no-browser]");
        Console.Error.WriteLine("       etl-sql-report --manifest reports.json [--no-browser]");
        return;
    }
}

var builder = WebApplication.CreateBuilder(args);

// Resolve port: CLI arg > appsettings > default 0 (ephemeral). Port 0 = OS-assigned dynamic port.
int port = portArg ?? builder.Configuration.GetValue<int>("ReportPlayer:Port", 0);
int executionTimeoutSeconds = builder.Configuration.GetValue<int?>("ReportPlayer:ExecutionTimeoutSeconds")
    ?? builder.Configuration.GetValue<int?>("Portal:Resources:ExecutionTimeoutSeconds")
    ?? 30;
var executionTimeout = TimeSpan.FromSeconds(Math.Max(1, executionTimeoutSeconds));
int idleTimeoutMinutes = builder.Configuration.GetValue<int>("ReportPlayer:IdleTimeoutMinutes", 30);

// We need a ServiceProvider to build the services, but we also want to register them.
// In Phase 9F, these are effectively singletons.
// We'll use a placeholder and then fix it up once the app is built.

if (multiMode)
{
    // For Multi-mode, we register the factory itself.
    builder.Services.AddSingleton<DashboardServiceFactory>(sp => 
        new DashboardServiceFactory(Path.GetFullPath(manifestPath!), sp.GetRequiredService<IServiceScopeFactory>(), executionTimeout));
}
else
{
    // For Single-mode, we register the service.
    builder.Services.AddSingleton<DashboardService>(sp => 
        new DashboardService(Path.GetFullPath(scriptPath!), sp.GetRequiredService<IServiceScopeFactory>(), executionTimeout));
}

// ── Logging via LoggerService ──────────────────────────────────────
var loggerService = new ETL_SQL.Common.LoggerService();
loggerService.InitializeAppLogger(
    builder.Configuration["Logging:AppLog:Directory"] ?? "logs/player",
    int.TryParse(builder.Configuration["Logging:AppLog:RetentionDays"],   out var rd) ? rd : 30,
    int.TryParse(builder.Configuration["Logging:AppLog:FileSizeLimitMb"], out var sl) ? sl : 10);

builder.Services.AddSingleton<ETL_SQL.Common.LoggerService>(loggerService);
builder.Services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);
builder.Services.AddSingleton<ETL_SQL.Common.ILoggerService>(loggerService);

builder.Services.AddEtlSqlEngine(builder.Configuration);

var app = builder.Build();

// Disable browser caching for all responses (static files and API alike) to prevent stale report data.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    ctx.Response.Headers["Pragma"] = "no-cache";
    ctx.Response.Headers["Expires"] = "0";
    await next();
});

// Track last request time so the idle-timeout timer knows when to shut down.
long lastActivityTicks = DateTime.UtcNow.Ticks;
app.Use(async (ctx, next) =>
{
    Interlocked.Exchange(ref lastActivityTicks, DateTime.UtcNow.Ticks);
    await next();
});

var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypes.Mappings[".geojson"] = "application/geo+json";
contentTypes.Mappings[".json"] = "application/json";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });

var noCache = new JsonSerializerOptions { WriteIndented = false };
var webOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

app.MapGet("/third-party-notices", () =>
{
    var noticesPath = FindRepoFile("THIRD-PARTY-NOTICES.md");
    return noticesPath is null
        ? Results.NotFound("THIRD-PARTY-NOTICES.md was not found.")
        : Results.Text(File.ReadAllText(noticesPath), "text/markdown; charset=utf-8");
});

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
            GetDashboardShellHtml(entry?.Name ?? name, entry?.Description, staleBanner, apiBase),
            "text/html");
    });

    app.MapGet("/reports/{name}/api/manifest", async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        
        // Pick up @Parameters from QueryString
        foreach (var q in ctx.Request.Query)
        {
            if (q.Key.StartsWith("@"))
            {
                await svc.SetParameterAsync(q.Key, q.Value.ToString());
            }
        }
        
        return Results.Json(await svc.GetManifestAsync(), noCache);
    });

    app.MapPost("/reports/{name}/api/parameter",
        async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var body = await JsonSerializer.DeserializeAsync<ParameterUpdateRequest>(ctx.Request.Body, webOptions);
        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest("name is required");
        return Results.Json(await svc.SetParameterAsync(body.Name, body.Value ?? ""), noCache);
    });

    app.MapPost("/reports/{name}/api/parameters",
        async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var body = await JsonSerializer.DeserializeAsync<ParameterBatchRequest>(ctx.Request.Body, webOptions);
        if (body == null) return Results.BadRequest("body is required");
        var updates = (body.Params ?? new List<ParameterUpdateRequest>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => (p.Name!, p.Value ?? ""));
        return Results.Json(await svc.SetParametersAsync(updates, body.IsInteraction, body.PageName), noCache);
    });

    app.MapPost("/reports/{name}/api/run-script",
        async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var body = await JsonSerializer.DeserializeAsync<RunScriptRequest>(ctx.Request.Body, webOptions);
        if (body == null || string.IsNullOrEmpty(body.ScriptPath)) return Results.BadRequest("ScriptPath is required");
        var result = await svc.RunScriptAsync(body.ScriptPath, body.Parameters ?? new());
        return Results.Json(new { message = result.Message, refresh = result.Refresh }, noCache);
    });

    app.MapPost("/reports/{name}/api/drill",
        async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var body = await JsonSerializer.DeserializeAsync<DrillRequest>(ctx.Request.Body, webOptions);
        if (body == null || string.IsNullOrWhiteSpace(body.VisualName)) return Results.BadRequest("visualName is required");
        var manifest = body.Direction?.ToUpperInvariant() == "UP"
            ? await svc.DrillUpAsync(body.VisualName, body.TargetDepth)
            : await svc.DrillInAsync(body.VisualName, body.ClickedValue ?? "");
        return manifest is null ? Results.NotFound() : Results.Json(manifest, noCache);
    });

    app.MapPost("/reports/{name}/api/refresh-visuals",
        async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var body = await JsonSerializer.DeserializeAsync<RefreshVisualsRequest>(ctx.Request.Body, webOptions);
        if (body == null || body.Visuals == null || body.Visuals.Count == 0)
            return Results.BadRequest("visuals is required");
        var manifest = await svc.RefreshVisualsAsync(body.Visuals);
        return manifest is null ? Results.NotFound() : Results.Json(manifest, noCache);
    });

    app.MapGet("/reports/{name}/api/refresh", async (string name, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var manifest = await svc.RebuildAsync();
        return Results.Json(new { rebuilt = true, visuals = manifest.Visuals.Count });
    });

    app.MapGet("/reports/{name}/api/export/pdf", async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();
        var manifest = await svc.GetManifestAsync();
        var url = BuildAbsoluteUrl(ctx, "/reports/" + WebUtility.UrlEncode(name));
        var bytes = new ReportPdfExporter().Export(manifest, new PdfExportOptions
        {
            Mode = PdfExportMode.Auto,
            Host = url,
            BrowserPath = builder.Configuration["ReportPlayer:PdfExport:BrowserPath"],
            Warn = message => Console.WriteLine("PDF export: " + message)
        });
        var filename = SanitizeFilename(manifest.Title ?? name) + "_" + DateTime.UtcNow.ToString("yyyyMMdd") + ".pdf";
        return Results.File(bytes, "application/pdf", filename);
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// Single-report routes
// ─────────────────────────────────────────────────────────────────────────────
else
{
    app.MapGet("/api/manifest", async (HttpContext ctx, DashboardService svc) =>
    {
        // Pick up @Parameters from QueryString
        foreach (var q in ctx.Request.Query)
        {
            if (q.Key.StartsWith("@"))
            {
                await svc.SetParameterAsync(q.Key, q.Value.ToString());
            }
        }
        return Results.Json(await svc.GetManifestAsync(), noCache);
    });

    app.MapPost("/api/parameter", async (HttpContext ctx, DashboardService svc) =>
    {
        var body = await JsonSerializer.DeserializeAsync<ParameterUpdateRequest>(ctx.Request.Body, webOptions);
        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest("name is required");
        return Results.Json(await svc.SetParameterAsync(body.Name, body.Value ?? ""), noCache);
    });

    app.MapPost("/api/parameters", async (HttpContext ctx, DashboardService svc) =>
    {
        var body = await JsonSerializer.DeserializeAsync<ParameterBatchRequest>(ctx.Request.Body, webOptions);
        if (body == null) return Results.BadRequest("body is required");
        var updates = (body.Params ?? new List<ParameterUpdateRequest>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => (p.Name!, p.Value ?? ""));
        return Results.Json(await svc.SetParametersAsync(updates, body.IsInteraction, body.PageName), noCache);
    });

    app.MapPost("/api/drill", async (HttpContext ctx, DashboardService svc) =>
    {
        var body = await JsonSerializer.DeserializeAsync<DrillRequest>(ctx.Request.Body, webOptions);
        if (body == null || string.IsNullOrWhiteSpace(body.VisualName)) return Results.BadRequest("visualName is required");
        var manifest = body.Direction?.ToUpperInvariant() == "UP"
            ? await svc.DrillUpAsync(body.VisualName, body.TargetDepth)
            : await svc.DrillInAsync(body.VisualName, body.ClickedValue ?? "");
        return manifest is null ? Results.NotFound() : Results.Json(manifest, noCache);
    });

    app.MapPost("/api/refresh-visuals", async (HttpContext ctx, DashboardService svc) =>
    {
        var body = await JsonSerializer.DeserializeAsync<RefreshVisualsRequest>(ctx.Request.Body, webOptions);
        if (body == null || body.Visuals == null || body.Visuals.Count == 0)
            return Results.BadRequest("visuals is required");
        var manifest = await svc.RefreshVisualsAsync(body.Visuals);
        return manifest is null ? Results.NotFound() : Results.Json(manifest, noCache);
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

    app.MapGet("/api/export/pdf", async (HttpContext ctx, DashboardService svc) =>
    {
        var manifest = await svc.GetManifestAsync();
        var bytes = new ReportPdfExporter().Export(manifest, new PdfExportOptions
        {
            Mode = PdfExportMode.Auto,
            Host = BuildAbsoluteUrl(ctx, "/"),
            BrowserPath = builder.Configuration["ReportPlayer:PdfExport:BrowserPath"],
            Warn = message => Console.WriteLine("PDF export: " + message)
        });
        var filename = SanitizeFilename(manifest.Title ?? Path.GetFileNameWithoutExtension(scriptPath) ?? "report")
            + "_" + DateTime.UtcNow.ToString("yyyyMMdd") + ".pdf";
        return Results.File(bytes, "application/pdf", filename);
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// Custom GeoJSON file route — shared across single and multi-report modes.
// Serves a user-supplied MAP_FILE path safely from the current report's script directory.
// The client sends: GET /maps/custom?path=<url-encoded-relative-path>
// ─────────────────────────────────────────────────────────────────────────────
app.MapGet("/maps/custom", async (HttpContext ctx) =>
{
    var rawPath = ctx.Request.Query["path"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(rawPath))
        return Results.BadRequest("path query parameter is required");

    // Resolve relative paths against the script directory (single-mode) or manifest directory (multi-mode).
    string baseDir = multiMode
        ? ctx.RequestServices.GetRequiredService<DashboardServiceFactory>().ManifestDirectory
        : Path.GetDirectoryName(Path.GetFullPath(scriptPath!)) ?? Directory.GetCurrentDirectory();

    if (Path.IsPathRooted(rawPath))
        return Results.BadRequest("Map path must be relative");

    if (!SafePath.TryResolveWithinRoot(baseDir, rawPath, out var fullPath))
        return Results.Forbid();

    if (!File.Exists(fullPath))
        return Results.NotFound($"Map file not found: {Path.GetFileName(fullPath)}");

    // Only serve GeoJSON files — reject anything else by extension.
    if (!fullPath.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase) &&
        !fullPath.EndsWith(".json",    StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only .geojson / .json files may be served via this route");

    ctx.Response.ContentType = "application/geo+json; charset=utf-8";
    await ctx.Response.SendFileAsync(fullPath);
    return Results.Empty;
});

// Multi-report mode also exposes the same route scoped per-report so that
// report-specific paths (relative to each script dir) can be resolved.
if (multiMode)
{
    app.MapGet("/reports/{name}/maps/custom", async (string name, HttpContext ctx, DashboardServiceFactory fac) =>
    {
        var svc = fac.GetService(name);
        if (svc == null) return Results.NotFound();

        var rawPath = ctx.Request.Query["path"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawPath))
            return Results.BadRequest("path query parameter is required");

        if (Path.IsPathRooted(rawPath))
            return Results.BadRequest("Map path must be relative");

        if (!SafePath.TryResolveWithinRoot(svc.ScriptDirectory, rawPath, out var fullPath))
            return Results.Forbid();

        if (!File.Exists(fullPath))
            return Results.NotFound($"Map file not found: {Path.GetFileName(fullPath)}");

        if (!fullPath.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase) &&
            !fullPath.EndsWith(".json",    StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Only .geojson / .json files may be served via this route");

        ctx.Response.ContentType = "application/geo+json; charset=utf-8";
        await ctx.Response.SendFileAsync(fullPath);
        return Results.Empty;
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// Start
// ─────────────────────────────────────────────────────────────────────────────
app.Urls.Add($"http://127.0.0.1:{port}");
await app.StartAsync();

// When port 0 was requested the OS assigns a free port — resolve the actual URL now.
var boundAddresses = app.Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>()?.Addresses;
var actualUrl = boundAddresses?.FirstOrDefault() ?? $"http://localhost:{port}";

if (multiMode)
{
    var fac = app.Services.GetRequiredService<DashboardServiceFactory>();
    Console.WriteLine($"ReportPlayer: hosting {fac.Reports.Count} report(s) from {Path.GetFileName(manifestPath)}");
    Console.WriteLine($"Catalog: {actualUrl}");
}
else
{
    Console.WriteLine($"ReportPlayer: serving {Path.GetFileName(scriptPath)}");
    Console.WriteLine($"Dashboard: {actualUrl}");
}

// Machine-readable line so callers (e.g. VS Code extension) can parse the actual bound URL.
Console.WriteLine($"REPORT_URL={actualUrl}");

// Idle timeout: each ReportPlayer instance shuts itself down after inactivity so
// orphaned preview servers don't accumulate. Multiple instances are fine — each gets
// its own ephemeral port. Pass --no-browser or set IdleTimeoutMinutes=0 to disable.
if (idleTimeoutMinutes > 0)
{
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    var idleTimeout = TimeSpan.FromMinutes(idleTimeoutMinutes);
    _ = Task.Run(async () =>
    {
        try
        {
            while (!lifetime.ApplicationStopping.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(60), lifetime.ApplicationStopping);
                var idle = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastActivityTicks));
                if (idle >= idleTimeout)
                {
                    Console.WriteLine($"[ReportPlayer] No activity for {idle.TotalMinutes:F0} min — shutting down.");
                    lifetime.StopApplication();
                }
            }
        }
        catch (OperationCanceledException) { }
    });
    Console.WriteLine($"[ReportPlayer] Will auto-shutdown after {idleTimeoutMinutes} min of inactivity.");
}

// Open the browser automatically so the user can immediately see the report.
// Pass --no-browser to suppress (e.g. VS Code extension handles its own webview).
if (!noBrowser)
{
    try
    {
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo { FileName = actualUrl, UseShellExecute = true });
    }
    catch { /* non-critical — user can copy the URL from stdout */ }
}

await app.WaitForShutdownAsync();


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
  .chart-wrapper { width: 100%; height: 400px; }
  .table-wrapper { overflow: auto; max-height: 500px; }
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
  footer a { color: #5470c6; }
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
  .page-grid > div { display: flex; flex-direction: column; min-height: 0; }
  .page-grid .visual-card { flex: 1; display: flex; flex-direction: column; min-height: 0; margin-bottom: 0; }
  .page-grid .chart-wrapper { flex: 1; height: auto; max-width: none; min-height: 0; }
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
    .page-grid { grid-template-areas: none !important; grid-template-columns: 1fr !important;
                 grid-template-rows: none !important; }
    .page-grid > div { grid-area: auto !important; }
    .page-grid .visual-card { flex: none; }
    .chart-wrapper { height: 240px !important; flex: none !important; }
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
        "<title>" + title + "</title>\n" +
        "<link rel=\"stylesheet\" href=\"/report-runtime.css\">\n</head>\n<body>\n" +
        staleBanner + "\n" +
        "<div id=\"root\"></div>\n" +
        ReportPlayerFooter() +
        // Pre-embed manifest; set __IS_WEB__ so interactive controls activate.
        "<script>window.__IS_WEB__ = true; window.__MANIFEST__ = " + manifestJson + ";</script>\n" +
        "<script src=\"/echarts.min.js\"></script>\n" +
        "<script src=\"/report-runtime.js\"></script>\n" +
        "</body>\n</html>";
}

static string BuildAbsoluteUrl(HttpContext ctx, string path)
{
    var scheme = ctx.Request.Scheme;
    var host = ctx.Request.Host.ToString();
    return scheme + "://" + host + path;
}

static string SanitizeFilename(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
        .Trim()
        .Replace(' ', '_');
}

/// <summary>Multi-report dashboard shell — manifest is fetched via API on load.</summary>
static string GetDashboardShellHtml(string reportName, string? description, string staleBanner, string apiBase)
{
    return
        "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n" +
        "<meta charset=\"UTF-8\">\n" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n" +
        "<title>" + reportName + " — ETL-SQL</title>\n" +
        "<title>" + reportName + " — ETL-SQL</title>\n" +
        "<link rel=\"stylesheet\" href=\"/report-runtime.css\">\n</head>\n<body>\n" +
        staleBanner + "\n" +
        "<div id=\"root\"></div>\n" +
        ReportPlayerFooter() +
        "<script>window.__IS_WEB__ = true; window.__API_BASE__ = '" + apiBase + "';</script>\n" +
        "<script src=\"/echarts.min.js\"></script>\n" +
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
        ReportPlayerFooter() +
        "</body>\n</html>";
}

static string ReportPlayerFooter() =>
    "<footer>Powered by ETL-SQL ReportPlayer &middot; <a href=\"/third-party-notices\" target=\"_blank\" rel=\"noopener\">Third-party notices</a></footer>\n";

static string? FindRepoFile(string fileName)
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var dir = new DirectoryInfo(start);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
    }

    return null;
}

// ── DTOs ──────────────────────────────────────────────────────────────────────
public class ParameterUpdateRequest
{
    public string? Name          { get; set; }
    public string? Value         { get; set; }
    public bool    IsInteraction { get; set; }
}

public class ParameterBatchRequest
{
    public List<ParameterUpdateRequest>? Params { get; set; }
    public bool    IsInteraction { get; set; }
    public string? PageName { get; set; }
}

public class RunScriptRequest
{
    public string? ScriptPath { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
}

public class DrillRequest
{
    public string? VisualName   { get; set; }
    public string? Direction    { get; set; }  // "IN" or "UP"
    public string? ClickedValue { get; set; }
    public int     TargetDepth  { get; set; }
}

public class RefreshVisualsRequest
{
    public List<string>? Visuals { get; set; }
}
