using System.Linq;
using System.Net;
using System.Text.Json;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;
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
        builder.Services.AddSingleton<ScriptDagProjectionService>();
        builder.Services.AddSingleton<IMetadataManager, MetadataManager>();
        builder.Services.AddSingleton<ILanguageService, GrammarLanguageService>();
        builder.Services.AddSingleton<WorkstationMetadataService>();
        builder.Services.AddSingleton<WorkstationCompletionService>();
        builder.Services.AddSingleton<WorkstationHelpService>();
        builder.Services.AddSingleton<WorkstationFormatService>();
        builder.Services.AddSingleton<WorkstationRunService>();
        builder.Services.AddSingleton<WorkstationPreviewService>();
        builder.Services.AddSingleton<WorkstationDataSampleService>();
        builder.Services.AddSingleton<WorkstationGitService>();
        builder.Services.AddSingleton<StudioHostLifecycleService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<StudioHostLifecycleService>());

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

            // The report runtime (report-runtime.js/css, tabulator, maps) sits beside
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

        var cssRoot = FindPortalCssRoot();
        if (cssRoot is not null)
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/css",
                FileProvider = new PhysicalFileProvider(cssRoot),
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

        app.MapGet("/studio", (WorkstationWorkspace workspace, WorkstationEditorOptions editorOptions) =>
            Results.Content(StudioShell.Html(editorOptions, workspace), "text/html; charset=utf-8"));

        app.MapGet("/api/workspace", (WorkstationWorkspace workspace, WorkstationEditorOptions editorOptions) =>
            Results.Json(new
            {
                root = workspace.Root,
                readOnly = workspace.ReadOnly,
                initialFile = workspace.InitialRelativeFile(editorOptions.InitialFile),
                files = workspace.ListFiles()
            }, JsonOptions));

        app.MapGet("/api/studio/lifecycle", (
            StudioHostLifecycleService lifecycle,
            WorkstationEditorOptions editorOptions) => Results.Json(new
            {
                instanceId = editorOptions.InstanceId,
                workspaceRoot = editorOptions.WorkspaceRoot,
                processId = Environment.ProcessId,
                connectedClients = lifecycle.ConnectedClients,
                activeRuns = lifecycle.ActiveRuns,
                dirtyClients = lifecycle.DirtyClients,
                idleShutdownMinutes = editorOptions.IdleShutdownMinutes
            }, JsonOptions));

        app.MapPost("/api/studio/heartbeat", (StudioHeartbeatRequest request, StudioHostLifecycleService lifecycle) =>
        {
            try
            {
                lifecycle.Heartbeat(request);
                return Results.Json(new { connectedClients = lifecycle.ConnectedClients }, JsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/studio/disconnect", (StudioHeartbeatRequest request, StudioHostLifecycleService lifecycle) =>
        {
            lifecycle.Disconnect(request.ClientId);
            return Results.NoContent();
        });

        app.MapPost("/api/studio/shutdown", (StudioShutdownRequest request, StudioHostLifecycleService lifecycle) =>
        {
            return lifecycle.TryRequestShutdown(request.Force, out var reason)
                ? Results.Accepted(value: new { message = "Studio shutdown requested." })
                : Results.Conflict(new { error = reason });
        });

        app.MapGet("/api/connections", (string? documentUri, IMetadataManager metadata) =>
        {
            var connections = metadata.GetConnections(documentUri).Select(c => new
            {
                alias = c.Name,
                connectorType = c.Type,
                description = $"{c.Type} connection ({c.Name})"
            }).ToList();
            return Results.Json(new { connections }, JsonOptions);
        });

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
                    tableList.Add(new
                    {
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

        app.MapGet("/api/connectors/schema", (string? type, IConnectorRegistry registry) =>
        {
            if (!string.IsNullOrWhiteSpace(type))
            {
                var schema = registry.GetConnectorSchema(type);
                return schema is not null
                    ? Results.Json(schema, JsonOptions)
                    : Results.NotFound(new { error = $"Connector type '{type}' not found." });
            }
            return Results.Json(registry.GetAllConnectorSchemas(), JsonOptions);
        });

        app.MapPost("/api/connectors/parse-string", (ParseConnectionStringRequest request) =>
        {
            var result = ConnectionStringParser.Parse(request.ConnectionString ?? string.Empty, request.HintProvider);
            return Results.Json(result, JsonOptions);
        });

        app.MapPost("/api/connectors/test", async (
            TestConnectionRequest request,
            ConnectionDiagnosticEngine diagnosticEngine,
            IExecutionContext context,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ConnectorType))
                return Results.BadRequest(new { error = "ConnectorType is required for connection testing." });

            try
            {
                var report = await diagnosticEngine.DiagnoseTargetAsync(
                    context,
                    request.Alias ?? "test_connection",
                    request.ConnectorType,
                    request.Target ?? string.Empty,
                    request.Options,
                    request.ProbeTimeoutSeconds > 0 ? request.ProbeTimeoutSeconds : 5,
                    cancellationToken);
                return Results.Json(report, JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
            }
        });

        app.MapGet("/api/files", async (string path, WorkstationWorkspace workspace, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Json(await workspace.ReadFileAsync(path, cancellationToken), JsonOptions);
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

        app.MapGet("/api/files/revision", async (string path, WorkstationWorkspace workspace, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Json(new { path, sourceRevision = await workspace.GetRevisionAsync(path, cancellationToken) }, JsonOptions);
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
                var sourceRevision = await workspace.WriteTextAsync(
                    request.Path ?? string.Empty,
                    request.Content ?? string.Empty,
                    request.BaseRevision,
                    cancellationToken);
                return Results.Json(new { saved = true, path = request.Path, sourceRevision }, JsonOptions);
            }
            catch (WorkspaceSaveConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
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

        app.MapPost("/api/files/rename", (RenameFileRequest request, WorkstationWorkspace workspace) =>
        {
            try
            {
                var renamed = workspace.RenameFile(request.Path ?? string.Empty, request.Name ?? string.Empty);
                return Results.Json(new
                {
                    path = renamed.Path,
                    name = Path.GetFileName(renamed.Path)
                }, JsonOptions);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (WorkspaceRenameConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
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

        app.MapPost("/api/script/dag", (ScriptDagRequest request, ScriptDagProjectionService dag) =>
            Results.Json(dag.Project(request.Script), JsonOptions));

        app.MapPost("/api/complete", async (CompleteRequest request, WorkstationCompletionService completion, CancellationToken cancellationToken) =>
            Results.Json(await completion.CompleteAsync(request, cancellationToken), JsonOptions));

        app.MapPost("/api/hover", (HoverRequest request, WorkstationHelpService help) =>
            Results.Json(help.GetHover(request), JsonOptions));

        app.MapPost("/api/format", (FormatRequest request, WorkstationFormatService formatter) =>
            Results.Json(formatter.Format(request), JsonOptions));

        app.MapGet("/api/formatter/config", (string? documentUri, WorkstationFormatService formatter) =>
            Results.Json(formatter.GetOptions(documentUri), JsonOptions));

        app.MapPost("/api/formatter/config", (ETL_SQL.Core.Formatting.FormatterOptions options, WorkstationFormatService formatter) =>
        {
            try
            {
                var targetPath = formatter.SaveOptions(options);
                return Results.Json(new { saved = true, path = targetPath }, JsonOptions);
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        var designerParsing = new ETL_SQL.Reporting.Authoring.DesignerScriptParsingService();
        var designerGen = new ETL_SQL.Reporting.Authoring.DesignerScriptGenerationService();
        var designerPatcher = new ETL_SQL.Reporting.Authoring.DesignerScriptPatcher(designerGen);
        var designerQueryFilters = new ETL_SQL.Reporting.Authoring.DesignerQueryFilterService();

        app.MapPost("/api/designer/generate", (GenerateDesignerAuthoringRequest request) =>
        {
            var script = !string.IsNullOrWhiteSpace(request.Script)
                ? designerPatcher.Patch(request.Script, request.DesignState)
                : designerGen.Generate(request.DesignState);
            return Results.Json(new { script }, JsonOptions);
        });

        app.MapPost("/api/designer/patch", (PatchDesignerAuthoringRequest request) =>
        {
            var script = designerPatcher.Patch(request.Script, request.DesignState);
            return Results.Json(new { script }, JsonOptions);
        });

        app.MapPost("/api/designer/parse", (ParseDesignerAuthoringRequest request) =>
        {
            var state = designerParsing.Parse(request.Script);
            return Results.Json(new { designState = state, error = (string?)null }, JsonOptions);
        });

        app.MapPost("/api/designer/query-filter", (ApplyDesignerQueryFiltersAuthoringRequest request) =>
        {
            try
            {
                return Results.Json(new
                {
                    source = designerQueryFilters.Apply(request.Source, request.Filters, request.AsVisualSource)
                }, JsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/designer/option-source", (BuildDesignerOptionSourceAuthoringRequest request) =>
        {
            try
            {
                return Results.Json(new
                {
                    source = designerQueryFilters.BuildCategoricalOptionSource(request.Source, request.Column)
                }, JsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/designer/run", async (
            RunRequest request,
            WorkstationRunService runner,
            StudioHostLifecycleService lifecycle,
            CancellationToken cancellationToken) =>
            Results.Json(await RunWithLifecycleAsync(request, runner, lifecycle, cancellationToken), JsonOptions));

        app.MapPost("/api/designer/analyze", async (AnalyzeRequest request, WorkstationAnalysisService analysis) =>
            Results.Json(await analysis.AnalyzeAsync(request), JsonOptions));

        app.MapPost("/api/designer/complete", async (CompleteRequest request, WorkstationCompletionService completion, CancellationToken cancellationToken) =>
            Results.Json(await completion.CompleteAsync(request, cancellationToken), JsonOptions));

        app.MapPost("/api/designer/dag", (ScriptDagRequest request, ScriptDagProjectionService dag) =>
            Results.Json(dag.Project(request.Script), JsonOptions));

        // Studio speaks one route dialect on every host: /api/designer/*. The unprefixed forms above
        // stay for the legacy editor shell, but hover and format had no designer-prefixed alias, so
        // Studio silently lost them on any host that did not serve the unprefixed name.
        app.MapPost("/api/designer/hover", (HoverRequest request, WorkstationHelpService help) =>
            Results.Json(help.GetHover(request), JsonOptions));

        app.MapPost("/api/designer/format", (FormatRequest request, WorkstationFormatService formatter) =>
            Results.Json(formatter.Format(request), JsonOptions));

        // Studio's visual canvas is built on this sample and keeps its palette disabled until one
        // exists, so the desktop host needs it just as much as the Portal does. The service applies
        // the same schema validation and bounded-run governance as the Portal's route.
        app.MapPost("/api/designer/data-sample", async (
            DataSampleRequest request,
            WorkstationDataSampleService sampler,
            IMetadataManager metadata,
            CancellationToken cancellationToken) =>
        {
            try
            {
                EnforceSchemaAccessPolicy(metadata, request.Connection ?? string.Empty, request.DocumentUri);
            }
            catch (System.Security.SecurityException ex)
            {
                return Results.Json(new { error = SecretRedactor.Redact(ex.Message) }, JsonOptions, statusCode: StatusCodes.Status403Forbidden);
            }

            try
            {
                return Results.Json(await sampler.SampleAsync(request, cancellationToken), JsonOptions);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = SecretRedactor.Redact(ex.Message) });
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Connector failures routinely quote the connection string back.
                return Results.BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
            }
        });

        app.MapPost("/api/designer/preview", async (PreviewRequest request, WorkstationPreviewService previewer, CancellationToken cancellationToken) =>
        {
            try
            {
                var manifest = await previewer.BuildPreviewAsync(request.Script ?? string.Empty, cancellationToken);
                return Results.Content(
                    ETL_SQL.Reporting.BrowserDeliveryProjection.Serialize(manifest), "application/json");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
            }
        });

        app.MapPost("/api/run", async (
            RunRequest request,
            WorkstationRunService runner,
            StudioHostLifecycleService lifecycle,
            CancellationToken cancellationToken) =>
            Results.Json(await RunWithLifecycleAsync(request, runner, lifecycle, cancellationToken), JsonOptions));

        app.MapPost("/api/preview", async (PreviewRequest request, WorkstationPreviewService previewer, CancellationToken cancellationToken) =>
        {
            try
            {
                var manifest = await previewer.BuildPreviewAsync(request.Script ?? string.Empty, cancellationToken);
                return Results.Content(
                    ETL_SQL.Reporting.BrowserDeliveryProjection.Serialize(manifest), "application/json");
            }
            catch (Exception ex)
            {
                // BuildPreviewAsync redacts its own failure message, but any other exception
                // reaching here (connector, IO) can carry a connection string.
                return Results.BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
            }
        });

        app.MapGet("/api/git/status", (WorkstationGitService git) =>
            Results.Json(git.GetStatus(), JsonOptions));

        app.MapPost("/api/git/commit", (GitCommitRequest request, WorkstationGitService git) =>
        {
            try
            {
                var result = git.Commit(request);
                return Results.Json(result, JsonOptions);
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/shutdown", (StudioShutdownRequest request, StudioHostLifecycleService lifecycle) =>
            lifecycle.TryRequestShutdown(request.Force, out var reason)
                ? Results.Accepted(value: new { message = "Studio shutdown requested." })
                : Results.Conflict(new { error = reason }));

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
<script src="/runtime/feedback.js"></script>
<script>
  window.__IS_PREVIEW__ = true;
  (function () {
    var runtimeInjected = false;

    function injectRuntimeOnce() {
      if (runtimeInjected) return;
      runtimeInjected = true;
      var rt = document.createElement('script');
      rt.src = '/runtime/report-runtime.js';
      rt.onerror = showError;
      document.body.appendChild(rt);
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

    private static async Task<RunResponse> RunWithLifecycleAsync(
        RunRequest request,
        WorkstationRunService runner,
        StudioHostLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        using var run = lifecycle.BeginRun();
        return await runner.RunAsync(request, cancellationToken);
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

    private static string? FindPortalCssRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "ETL-SQL.Portal", "wwwroot", "css");
            if (Directory.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        var published = Path.Combine(AppContext.BaseDirectory, "wwwroot", "css");
        return Directory.Exists(published) ? published : null;
    }
}

public sealed record SaveFileRequest(string? Path, string? Content, string? BaseRevision = null);
public sealed record RenameFileRequest(string? Path, string? Name);
public sealed record ParseConnectionStringRequest(string? ConnectionString, string? HintProvider);
public sealed record TestConnectionRequest(string? Alias, string? ConnectorType, string? Target, Dictionary<string, string>? Options, int ProbeTimeoutSeconds = 5);
public sealed record GenerateDesignerAuthoringRequest(ETL_SQL.Reporting.Authoring.DesignerAuthoringState DesignState, string? Script = null);
public sealed record PatchDesignerAuthoringRequest(string Script, ETL_SQL.Reporting.Authoring.DesignerAuthoringState DesignState);
public sealed record ParseDesignerAuthoringRequest(string Script);
public sealed record ApplyDesignerQueryFiltersAuthoringRequest(
    string Source,
    List<ETL_SQL.Reporting.Authoring.DesignerQueryFilter> Filters,
    bool AsVisualSource = true);
public sealed record BuildDesignerOptionSourceAuthoringRequest(string Source, string Column);
