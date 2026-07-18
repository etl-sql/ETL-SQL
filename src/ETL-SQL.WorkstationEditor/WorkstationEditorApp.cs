using System.Net;
using System.Text.Json;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Common;
using ETL_SQL.Core;
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

        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Response.Headers.Pragma = "no-cache";
            ctx.Response.Headers.Expires = "0";
            await next();
        });

        app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), branch =>
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
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/designer",
                FileProvider = new PhysicalFileProvider(Path.Combine(sharedRoot, "designer"))
            });
        }

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

        return app;
    }

    public static string GetListeningUrl(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        return addresses?.FirstOrDefault() ?? "http://127.0.0.1:0";
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

        return null;
    }
}

public sealed record SaveFileRequest(string? Path, string? Content);
