using System.Net;
using System.Linq;
using System.Text.Json;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Services;
using ETL_SQL.Orchestrator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.WorkstationEditor;

public static class WorkstationEditorApp
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WebApplication Create(string[] args, WorkstationEditorOptions options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, options.Port));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Session:Root"] = Path.Combine(Path.GetTempPath(), "etl-sql-workstation-editor", options.SessionToken),
                ["Governance:Secrets:Provider"] = "Environment"
            })
            .Build();

        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddSingleton<ETL_SQL.Common.ILogger>(NullLogger.Instance);
        builder.Services.AddEtlSqlEngine(configuration);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new WorkstationWorkspace(options.WorkspaceRoot, options.ReadOnly));
        builder.Services.AddSingleton<WorkstationAnalysisService>();
        builder.Services.AddSingleton<IMetadataManager, MetadataManager>();
        builder.Services.AddSingleton<ILanguageService, GrammarLanguageService>();
        builder.Services.AddSingleton<WorkstationMetadataService>();
        builder.Services.AddSingleton<WorkstationCompletionService>();
        builder.Services.AddSingleton<WorkstationHelpService>();
        builder.Services.AddSingleton<WorkstationFormatService>();
        builder.Services.AddSingleton<WorkstationRunService>();
        builder.Services.AddSingleton<WorkstationPreviewService>();

        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Response.Headers.Pragma = "no-cache";
            ctx.Response.Headers.Expires = "0";
            await next();
        });

        // The shell is gated as well as /api. It embeds the session token so the page can call the
        // API, so serving it unauthenticated would hand the token to anything that can reach the
        // loopback port and defeat the gate entirely. The printed URL already carries ?token=.
        // Static assets under /designer stay open: they are public JS/CSS with no session data, and
        // `<script src>` / module imports cannot send the header.
        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/api")
                   || ctx.Request.Path.Equals("/", StringComparison.Ordinal),
            branch =>
            {
                branch.Use(async (ctx, next) =>
                {
                    if (!IsAuthorized(ctx, options.SessionToken))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await ctx.Response.WriteAsJsonAsync(new { error = "A valid editor session token is required." }, JsonOptions);
                        return;
                    }

                    await next();
                });
            });

        var sharedRoot = FindSharedRuntimeRoot();
        if (sharedRoot is not null)
        {
            var designerSubdir = Path.Combine(sharedRoot, "designer");
            if (Directory.Exists(designerSubdir))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    RequestPath = "/designer",
                    FileProvider = new PhysicalFileProvider(designerSubdir),
                    OnPrepareResponse = ctx =>
                    {
                        ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                        ctx.Context.Response.Headers.Pragma = "no-cache";
                        ctx.Context.Response.Headers.Expires = "0";
                    }
                });
            }

            // The report runtime (report-runtime.js/css, echarts, tabulator, maps) sits beside
            // designer/ rather than inside it, and the preview iframe needs it. Mount it on its own
            // path instead of layering a second provider over /designer: overlapping mounts at one
            // path make it ambiguous which directory answers a request, and would silently publish
            // anything later added beside designer/ under the same URL space.
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/runtime",
                FileProvider = new PhysicalFileProvider(sharedRoot),
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                    ctx.Context.Response.Headers.Pragma = "no-cache";
                    ctx.Context.Response.Headers.Expires = "0";
                }
            });
        }

        app.MapGet("/favicon.ico", () => Results.NoContent());

        app.MapGet("/", (WorkstationWorkspace workspace, WorkstationEditorOptions editorOptions) =>
            Results.Content(EditorShell.Html(editorOptions, workspace), "text/html; charset=utf-8"));

        app.MapGet("/api/workspace", (WorkstationWorkspace workspace, WorkstationEditorOptions editorOptions) =>
            Results.Json(new
            {
                root = workspace.Root,
                readOnly = workspace.ReadOnly,
                initialFile = workspace.InitialRelativeFile(editorOptions.InitialFile),
                files = workspace.ListFiles()
            }, JsonOptions));

        app.MapGet("/api/session/metadata", async (string? documentUri, IMetadataManager metadata, CancellationToken cancellationToken) =>
        {
            var connections = metadata.GetConnections(documentUri).Select(c => c.Name).ToList();
            var tempTables = await metadata.GetTempTablesAsync(documentUri);
            var tempTableDtos = new List<object>();
            foreach (var t in tempTables)
            {
                // Temp-table lookups are keyed by document, not connection — the connection
                // name is ignored for '#' tables (see MetadataManager.GetColumnDetailsAsync).
                var cols = (await metadata.GetColumnDetailsAsync(string.Empty, t, documentUri))
                    .Select(c => new { name = c.Name, type = c.DataType })
                    .ToList();
                tempTableDtos.Add(new { name = t, columns = cols });
            }
            return Results.Json(new
            {
                connections,
                variables = new List<object>(),
                tempTables = tempTableDtos
            }, JsonOptions);
        });

        app.MapGet("/api/designer/schema", async (string connection, string? documentUri, IMetadataManager metadata, CancellationToken cancellationToken) =>
        {
            // Re-check egress policy on every request, before anything is served. The schema cache is
            // warm and connector-free: once an entry is cached, reads never touch the connector that
            // would normally enforce this, so a host blocked after the cache warmed would keep having
            // its table and column names completed. Checking here — not at cache-fill — is what makes
            // tightening policy take effect immediately.
            try
            {
                EnforceSchemaAccessPolicy(metadata, connection, documentUri);
            }
            catch (System.Security.SecurityException ex)
            {
                return Results.Json(new { error = SecretRedactor.Redact(ex.Message) }, JsonOptions, statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                var tables = await metadata.GetTablesAsync(connection, documentUri);
                var tableList = new List<object>();
                foreach (var table in tables)
                {
                    var columns = await metadata.GetColumnDetailsAsync(connection, table, documentUri);
                    tableList.Add(new {
                        name = table,
                        columns = columns.Select(c => new { name = c.Name, type = c.DataType }).ToList()
                    });
                }
                return Results.Json(new { connection, tables = tableList }, JsonOptions);
            }
            catch (Exception ex)
            {
                // Connector failures routinely quote the connection string back.
                return Results.BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
            }
        });

        app.MapGet("/api/files", async (string path, WorkstationWorkspace workspace, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Json(new { path, content = await workspace.ReadTextAsync(path, cancellationToken) }, JsonOptions);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPut("/api/files", async (SaveFileRequest request, WorkstationWorkspace workspace, CancellationToken cancellationToken) =>
        {
            try
            {
                await workspace.WriteTextAsync(request.Path ?? string.Empty, request.Content ?? string.Empty, cancellationToken);
                return Results.Json(new { saved = true, path = request.Path }, JsonOptions);
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/analyze", async (AnalyzeRequest request, WorkstationAnalysisService analysis) =>
            Results.Json(await analysis.AnalyzeAsync(request), JsonOptions));

        app.MapPost("/api/complete", async (CompleteRequest request, WorkstationCompletionService completion, CancellationToken cancellationToken) =>
            Results.Json(await completion.CompleteAsync(request, cancellationToken), JsonOptions));

        app.MapPost("/api/hover", (HoverRequest request, WorkstationHelpService help) =>
            Results.Json(help.GetHover(request), JsonOptions));

        app.MapPost("/api/format", (FormatRequest request, WorkstationFormatService formatter) =>
            Results.Json(formatter.Format(request), JsonOptions));

        app.MapPost("/api/run", async (RunRequest request, WorkstationRunService runner, CancellationToken cancellationToken) =>
            Results.Json(await runner.RunAsync(request, cancellationToken), JsonOptions));

        app.MapPost("/api/preview", async (PreviewRequest request, WorkstationPreviewService previewer, CancellationToken cancellationToken) =>
        {
            try
            {
                var manifest = await previewer.BuildPreviewAsync(request.Script ?? string.Empty, cancellationToken);
                return Results.Json(manifest, JsonOptions);
            }
            catch (Exception ex)
            {
                // BuildPreviewAsync redacts its own failure message, but any other exception
                // reaching here (connector, IO) can carry a connection string.
                return Results.BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
            }
        });

        app.MapPost("/api/shutdown", (IHostApplicationLifetime lifetime) =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                lifetime.StopApplication();
            });
            return Results.Ok(new { message = "Shutting down workstation editor..." });
        });

        app.MapGet("/designer-preview.html", () => Results.Content("""
<!DOCTYPE html>
<html>
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="/runtime/report-runtime.css">
<style>
  html, body { margin: 0; height: 100%; }
  #root { min-height: 100%; }
  .preview-placeholder {
    display: flex; align-items: center; justify-content: center;
    height: 100%; padding: 24px; box-sizing: border-box;
    font-family: system-ui, -apple-system, sans-serif; font-size: 13px; color: #64748b;
    text-align: center;
  }
</style>
<script>
  window.__IS_PREVIEW__ = true;
  (function () {
    var runtimeInjected = false;

    function injectRuntimeOnce() {
      if (runtimeInjected) return;
      runtimeInjected = true;
      var echarts = document.createElement('script');
      echarts.src = '/runtime/echarts.min.js';
      echarts.onload = function () {
        var rt = document.createElement('script');
        rt.src = '/runtime/report-runtime.js';
        document.body.appendChild(rt);
      };
      echarts.onerror = showError;
      document.body.appendChild(echarts);
    }

    function showError() {
      var root = document.getElementById('root');
      if (root) root.innerHTML = '<div class="preview-placeholder">Failed to load the preview runtime.</div>';
    }

    window.addEventListener('message', function (e) {
      var data = e.data;
      if (!data || data.type !== 'reportManifest') return;
      window.__MANIFEST__ = data.manifest;
      if (data.dark) document.body.classList.add('theme-dark');
      else document.body.classList.remove('theme-dark');
      if (runtimeInjected) {
        if (window.__reportRuntimeRender__) window.__reportRuntimeRender__(data.manifest);
      } else {
        injectRuntimeOnce();
      }
    });

    function announceReady() {
      (window.parent || window).postMessage({ type: 'previewReady' }, '*');
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', announceReady);
    else announceReady();
  })();
</script>
</head>
<body style="margin:0">
<div id="root"><div class="preview-placeholder">Run a preview to render the report here.</div></div>
</body>
</html>
""", "text/html; charset=utf-8"));

        return app;
    }

    public static string GetListeningUrl(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        return addresses?.FirstOrDefault() ?? "http://127.0.0.1:0";
    }

    /// <summary>
    /// Applies the local egress guardrail to the connection behind a schema request. Throws
    /// <see cref="System.Security.SecurityException"/> when the host is not permitted.
    ///
    /// Resolution is best-effort by design: a connection the editor cannot resolve, or a connector
    /// that cannot report a host (file and in-memory connectors such as MOCKDB), has no host to
    /// validate and is left to the normal read path, which returns nothing for an unknown
    /// connection. Only a resolvable host is checked, and it is checked every time.
    /// </summary>
    internal static void EnforceSchemaAccessPolicy(IMetadataManager metadata, string connectionName, string? documentUri)
    {
        if (string.IsNullOrWhiteSpace(connectionName)) return;

        var host = metadata.GetConnectionHost(connectionName, documentUri);
        if (string.IsNullOrWhiteSpace(host)) return;

        ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(SystemExecutionContext.Instance, host);
    }

    private static bool IsAuthorized(HttpContext context, string expectedToken)
    {
        if (context.Request.Headers.TryGetValue("X-ETLSQL-EDITOR-TOKEN", out var headerToken) &&
            string.Equals(headerToken.ToString(), expectedToken, StringComparison.Ordinal))
        {
            return true;
        }

        return context.Request.Query.TryGetValue("token", out var queryToken) &&
            string.Equals(queryToken.ToString(), expectedToken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Locates the browser runtime assets (<c>designer/</c>, report runtime, maps).
    /// </summary>
    /// <remarks>
    /// Prefers the canonical <c>Resources/Shared</c> folder when running from a checkout, so
    /// editing a shared asset shows up on reload without re-running <c>sync-assets</c>. A
    /// published install has no repo tree, so it falls back to the <c>wwwroot</c> copies that
    /// <c>sync-assets</c> writes and the Web SDK includes in the publish output.
    /// </remarks>
    private static string? FindSharedRuntimeRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "ETL-SQL.ReportRuntime", "Resources", "Shared");
            if (Directory.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        var published = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        return Directory.Exists(Path.Combine(published, "designer")) ? published : null;
    }
}

public sealed record SaveFileRequest(string? Path, string? Content);
