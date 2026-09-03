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
                files = workspace.ListFiles(),
                folders = workspace.ListFolders()
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
            catch (WorkspaceEntryConflictException ex)
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

        app.MapPost("/api/workspace/folders", (CreateWorkspaceFolderRequest request, WorkstationWorkspace workspace) =>
        {
            try
            {
                return Results.Json(workspace.CreateFolder(request.Path ?? string.Empty), JsonOptions);
            }
            catch (WorkspaceEntryConflictException ex)
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

        app.MapPost("/api/workspace/rename", (RenameWorkspaceEntryRequest request, WorkstationWorkspace workspace) =>
        {
            try
            {
                return Results.Json(workspace.RenameEntry(
                    request.Path ?? string.Empty,
                    request.Name ?? string.Empty,
                    request.IsDirectory), JsonOptions);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (WorkspaceEntryConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or DirectoryNotFoundException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/workspace/move", (MoveWorkspaceFileRequest request, WorkstationWorkspace workspace) =>
        {
            try
            {
                return Results.Json(workspace.MoveFile(
                    request.Path ?? string.Empty,
                    request.DestinationFolder ?? string.Empty), JsonOptions);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (WorkspaceEntryConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or DirectoryNotFoundException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/workspace/delete", (DeleteWorkspaceEntryRequest request, WorkstationWorkspace workspace) =>
        {
            try
            {
                workspace.DeleteEntry(request.Path ?? string.Empty, request.IsDirectory);
                return Results.NoContent();
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or DirectoryNotFoundException)
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
        var pipelineTasks = new ETL_SQL.Analysis.Services.PipelineTaskAuthoringService();
        var pipelineScope = new ETL_SQL.Analysis.Services.ScriptScopeService();
        var pipelineRunPlans = new ETL_SQL.Analysis.Services.PipelineRunPlanService();
        var dataModel = new ETL_SQL.Analysis.Services.ScriptDataModelService();
        var governance = new ETL_SQL.Analysis.Services.ScriptGovernanceService();
        var qualityRules = new ETL_SQL.Analysis.Services.ScriptQualityRuleService();
        var datasetLifecycle = new ETL_SQL.Analysis.Services.ScriptDatasetLifecycleService();
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

        // The editable half of the pipeline canvas — same contract as the Portal's, so one shared
        // Studio module drives both hosts. A refusal is a 200 with applied:false and the reason.
        app.MapPost("/api/designer/pipeline-task", (PipelineTaskAuthoringRequest request) =>
        {
            var script = request.Script ?? string.Empty;
            var result = (request.Op ?? string.Empty).ToLowerInvariant() switch
            {
                "add" => pipelineTasks.Add(script, new ETL_SQL.Analysis.Services.PipelineTaskDraft(
                    request.Id ?? string.Empty,
                    Enum.TryParse<ETL_SQL.Analysis.Services.PipelineTaskKind>(request.Kind, ignoreCase: true, out var kind)
                        ? kind
                        : ETL_SQL.Analysis.Services.PipelineTaskKind.Execution,
                    request.Connection,
                    request.Body,
                    request.Source,
                    request.Target,
                    request.Condition,
                    request.Message,
                    request.Recipient,
                    request.Sender,
                    request.Subject,
                    request.After,
                    request.Variable,
                    request.Collection)),
                "update" => pipelineTasks.Update(
                    script, request.Id ?? string.Empty, request.NewId, request.Connection, request.Body,
                    request.Variable, request.Collection),
                "move" => pipelineTasks.Move(script, request.Id ?? string.Empty, request.After),
                "nest" => pipelineTasks.Nest(script, request.Id ?? string.Empty, request.After),
                // An edge condition that the host does not recognise is refused rather than quietly
                // written as plain precedence: it decides which control flow goes into the file.
                "connect" => Enum.TryParse<ETL_SQL.Analysis.Services.PipelineEdgeCondition>(
                        string.IsNullOrWhiteSpace(request.Edge) ? "Always" : request.Edge,
                        ignoreCase: true,
                        out var edge)
                    ? pipelineTasks.Connect(script, request.After ?? string.Empty, request.Id ?? string.Empty, edge, request.Expression)
                    : ETL_SQL.Analysis.Services.PipelineEditResult.Refused(script, $"Unknown edge condition '{request.Edge}'."),
                "disconnect" => pipelineTasks.Disconnect(script, request.After ?? string.Empty, request.Id ?? string.Empty),
                "remove" => pipelineTasks.Remove(script, request.Id ?? string.Empty),
                "read" => ETL_SQL.Analysis.Services.PipelineEditResult.Ok(script),
                _ => ETL_SQL.Analysis.Services.PipelineEditResult.Refused(script, $"Unknown pipeline task operation '{request.Op}'."),
            };

            return Results.Json(new
            {
                applied = result.Applied,
                script = result.Script,
                error = result.Error,
                tasks = pipelineTasks.Read(result.Script)
                    .Select(task => new
                    {
                        id = task.Id,
                        kind = task.Kind.ToString().ToLowerInvariant(),
                        connection = task.Connection,
                        body = task.Body,
                        line = task.Line,
                        dependsOn = task.DependsOn.Select(dependency => new
                        {
                            id = dependency.Id,
                            condition = dependency.Condition.ToString().ToLowerInvariant(),
                            expression = dependency.Expression,
                        }).ToList(),
                        guarded = task.Guarded,
                        container = task.Container,
                        variable = task.Variable,
                        collection = task.Collection,
                    })
                    .ToList(),
            }, JsonOptions);
        });

        // What a selected task can see from where it sits. Positional, not script-wide: a variable
        // declared below a task is not one it can read, and a #temp created below it does not exist
        // yet, so a flat list of every name in the file would be telling the author they can use
        // things that are not there.
        app.MapPost("/api/designer/pipeline-scope", (PipelineScopeAuthoringRequest request) =>
        {
            // A canvas points at a task; a script editor points with a caret. Same question and the
            // same positional rule, so one route answers both.
            var scope = request.Line is > 0 && string.IsNullOrWhiteSpace(request.Id)
                ? pipelineScope.AtLine(request.Script, request.Line.Value)
                : pipelineScope.At(request.Script, request.Id);
            return Results.Json(new
            {
                resolved = scope.Resolved,
                error = scope.Error,
                variables = scope.Variables,
                tempTables = scope.TempTables,
                statementText = scope.StatementText,
                prefixScript = scope.PrefixScript,
                statementLine = scope.StatementLine,
            }, JsonOptions);
        });

        // What running to a selected task would execute, and what that would cost. Like the Portal's,
        // this route runs nothing: the canvas shows the effects, gets an answer, and then hands the
        // slice to /api/designer/run, which still applies this host's destructive-statement guard.
        app.MapPost("/api/designer/pipeline-run-plan", (PipelineRunPlanAuthoringRequest request) =>
        {
            var plan = pipelineRunPlans.To(request.Script, request.Id);
            return Results.Json(new
            {
                resolved = plan.Resolved,
                error = plan.Error,
                script = plan.Script,
                included = plan.Included,
                skipped = plan.Skipped,
                effects = plan.Effects.Select(effect => new
                {
                    taskId = effect.TaskId,
                    action = effect.Action,
                    target = effect.Target,
                    line = effect.Line,
                }),
            }, JsonOptions);
        });

        // The entity/relationship shape of a script. Same two passes as the Portal's: project from
        // the script alone, ask the connectors about the tables that projection named, then project
        // again with what they said. Doing it the other way round would turn opening the diagram into
        // a schema crawl over every database the workspace can reach.
        app.MapPost("/api/designer/data-model", async (
            DataModelAuthoringRequest request, IMetadataManager metadata, CancellationToken cancellationToken) =>
        {
            var model = dataModel.Project(request.Script);
            if (model.Parsed)
            {
                var evidence = await ETL_SQL.Analysis.Services.DataModelSchemaEvidenceReader.ReadAsync(
                    metadata, model, request.DocumentUri, cancellationToken);
                if (!evidence.IsEmpty) model = dataModel.Project(request.Script, evidence);
            }

            return Results.Json(new
            {
                parsed = model.Parsed,
                error = model.Error,
                // Said out loud because the view has to distinguish "these tables declare no keys"
                // from "nobody asked a database". Both leave every cardinality unknown; only one of
                // them is a fact about the data.
                hasSchemaEvidence = model.HasSchemaEvidence,
                entities = model.Entities.Select(entity => new
                {
                    id = entity.Id,
                    name = entity.Name,
                    kind = entity.Kind,
                    connection = entity.Connection,
                    line = entity.Line,
                    detail = entity.Detail,
                    columns = entity.Columns.Select(column => new
                    {
                        name = column.Name,
                        type = column.Type,
                        isKey = column.IsKey,
                    }),
                }),
                relationships = model.Relationships.Select(relationship => new
                {
                    id = relationship.Id,
                    from = relationship.From,
                    to = relationship.To,
                    kind = relationship.Kind,
                    cardinality = relationship.Cardinality,
                    evidence = relationship.Evidence,
                    fromColumn = relationship.FromColumn,
                    toColumn = relationship.ToColumn,
                    joinType = relationship.JoinType,
                    line = relationship.Line,
                }),
            }, JsonOptions);
        });

        // The governance metadata a script carries, and the one place Studio writes it. A write is a
        // span edit on the author's own bytes; a refusal comes back as an ordinary answer with its
        // reason, because a panel that redraws unchanged looks exactly like one that applied it.
        app.MapPost("/api/designer/governance", (GovernanceAuthoringRequest request) =>
        {
            var script = request.Script ?? string.Empty;
            var applied = false;
            string? writeError = null;

            switch ((request.Op ?? "read").ToLowerInvariant())
            {
                case "write":
                    {
                        var edit = governance.Write(script, request.ScopeId ?? string.Empty, request.Tags ?? []);
                        (applied, writeError, script) = (edit.Applied, edit.Error, edit.Script);
                        break;
                    }
                case "rule":
                    {
                        var edit = request.Remove
                            ? qualityRules.RemoveRule(script, request.ScopeId ?? string.Empty, request.Index ?? -1)
                            : qualityRules.SetRule(script, request.ScopeId ?? string.Empty, request.Index ?? -1, request.Rule, request.Action);
                        (applied, writeError, script) = (edit.Applied, edit.Error, edit.Script);
                        break;
                    }
                case "routing":
                    {
                        var edit = qualityRules.SetRouting(
                            script, request.StatementId ?? string.Empty, request.Action ?? string.Empty,
                            request.Target, request.Retention, request.Handling, request.Remove);
                        (applied, writeError, script) = (edit.Applied, edit.Error, edit.Script);
                        break;
                    }
                case "dataset-access":
                    {
                        var edit = datasetLifecycle.SetAccess(script, request.Dataset ?? string.Empty, request.Access ?? string.Empty);
                        (applied, writeError, script) = (edit.Applied, edit.Error, edit.Script);
                        break;
                    }
                case "dataset-ttl":
                    {
                        var edit = datasetLifecycle.SetTtl(script, request.Dataset ?? string.Empty, request.Ttl);
                        (applied, writeError, script) = (edit.Applied, edit.Error, edit.Script);
                        break;
                    }
                case "dataset-step":
                    {
                        var edit = datasetLifecycle.AddLifecycleStatement(
                            script, request.Dataset ?? string.Empty, request.Action ?? string.Empty,
                            request.Path, request.Encryption, request.Secret, request.Folder, request.Access);
                        (applied, writeError, script) = (edit.Applied, edit.Error, edit.Script);
                        break;
                    }
            }

            return Results.Json(
                ETL_SQL.Analysis.Services.DesignerGovernanceMapper.Response(
                    governance.Read(script), script, applied, writeError,
                    qualityRules.Read(script),
                    // The desktop host has no steward queue: it is a Portal view over persisted
                    // quarantine evidence, and this host persists none. Null, so the panel says
                    // where the queue lives rather than offering a link that goes nowhere.
                    stewardQueueUrl: null,
                    datasetLifecycle.Read(script),
                    // No dataset registry here either: a workstation's datasets are files on this
                    // machine, so there is nobody to share one with.
                    datasetRegistryUrl: null),
                JsonOptions);
        });

        // The audiences a run can be previewed as. This host has no directory of its own — a
        // workstation is one person's — so it defines no groups or roles and the author names the
        // audience their predicates test. Said out loud rather than returned as an empty list, which
        // would read as "this tenant has no groups".
        app.MapGet("/api/designer/preview-as", () => Results.Json(new
        {
            supported = true,
            groups = Array.Empty<string>(),
            roles = Array.Empty<string>(),
            note = "This host has no directory, so type the group and role names your predicates check. "
                + "Without a preview a workstation run carries no identity at all, which is why an "
                + "RLS-guarded report shows nothing here.",
        }, JsonOptions));

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
                var manifest = await previewer.BuildPreviewAsync(
                    request.Script ?? string.Empty, cancellationToken, request.RunEveryPage, request.Parameters);
                return Results.Content(
                    ETL_SQL.Reporting.BrowserDeliveryProjection.Serialize(manifest), "application/json");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = SecretRedactor.Redact(ex.Message) });
            }
        });

        // The same route the Portal serves, so Studio's Export step is one action on both hosts
        // rather than a button that works in one place and 404s in the other.
        app.MapPost("/api/designer/preview/pdf", async (
            PreviewRequest request,
            WorkstationPreviewService previewer,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // Every page is run: an exported page that was still waiting for its prompts would
                // be a blank sheet in the file.
                var manifest = await previewer.BuildPreviewAsync(
                    request.Script ?? string.Empty, cancellationToken, runEveryPage: true, request.Parameters);
                var pdf = await new ETL_SQL.Reporting.ReportPdfExporter().ExportAsync(
                    manifest,
                    new ETL_SQL.Reporting.PdfExportOptions
                    {
                        Mode = ETL_SQL.Reporting.PdfExportMode.Static,
                        Host = null,
                    },
                    cancellationToken);
                return Results.File(pdf, "application/pdf", "preview.pdf");
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

        app.MapGet("/api/git/history", (string? path, int? limit, WorkstationGitService git) =>
        {
            try
            {
                return Results.Json(git.GetHistory(path, limit ?? 20), JsonOptions);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        });

        app.MapPost("/api/git/diff", (GitDiffRequest request, WorkstationGitService git) =>
        {
            try
            {
                return Results.Json(git.GetDiff(request), JsonOptions);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

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
public sealed record CreateWorkspaceFolderRequest(string? Path);
public sealed record RenameWorkspaceEntryRequest(string? Path, string? Name, bool IsDirectory);
public sealed record MoveWorkspaceFileRequest(string? Path, string? DestinationFolder);
public sealed record DeleteWorkspaceEntryRequest(string? Path, bool IsDirectory);
public sealed record ParseConnectionStringRequest(string? ConnectionString, string? HintProvider);
public sealed record TestConnectionRequest(string? Alias, string? ConnectorType, string? Target, Dictionary<string, string>? Options, int ProbeTimeoutSeconds = 5);
public sealed record GenerateDesignerAuthoringRequest(ETL_SQL.Reporting.Authoring.DesignerAuthoringState DesignState, string? Script = null);
public sealed record PatchDesignerAuthoringRequest(string Script, ETL_SQL.Reporting.Authoring.DesignerAuthoringState DesignState);
public sealed record ParseDesignerAuthoringRequest(string Script);
public sealed record PipelineTaskAuthoringRequest(
    string? Script,
    string? Op,
    string? Id = null,
    string? NewId = null,
    string? Kind = null,
    string? Connection = null,
    string? Body = null,
    string? Source = null,
    string? Target = null,
    string? Condition = null,
    string? Message = null,
    string? Recipient = null,
    string? Sender = null,
    string? Subject = null,
    string? After = null,
    string? Edge = null,
    string? Expression = null,
    string? Variable = null,
    string? Collection = null);
public sealed record PipelineScopeAuthoringRequest(string? Script, string? Id, int? Line = null);

public sealed record DataModelAuthoringRequest(string? Script, string? DocumentUri = null);

/// <param name="Op">read | write. A read never touches the script.</param>
/// <param name="Tags">A null value removes the tag, which is a different edit from setting it empty.</param>
public sealed record GovernanceAuthoringRequest(
    string? Script,
    string? Op = null,
    string? ScopeId = null,
    Dictionary<string, string?>? Tags = null,
    int? Index = null,
    string? Rule = null,
    string? Action = null,
    string? StatementId = null,
    string? Target = null,
    string? Retention = null,
    string? Handling = null,
    bool Remove = false,
    string? Dataset = null,
    string? Access = null,
    string? Ttl = null,
    string? Path = null,
    string? Encryption = null,
    string? Secret = null,
    string? Folder = null);

/// <summary>Which task to plan a run up to. Planning never executes anything.</summary>
public sealed record PipelineRunPlanAuthoringRequest(string? Script, string? Id);
public sealed record ApplyDesignerQueryFiltersAuthoringRequest(
    string Source,
    List<ETL_SQL.Reporting.Authoring.DesignerQueryFilter> Filters,
    bool AsVisualSource = true);
public sealed record BuildDesignerOptionSourceAuthoringRequest(string Source, string Column);
