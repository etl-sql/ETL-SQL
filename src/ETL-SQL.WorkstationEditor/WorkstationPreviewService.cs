using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Security;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Reporting;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationPreviewService(IServiceProvider services, ETL_SQL.Common.ILogger logger)
{
    internal const string PreviewSourceName = "preview.rptsql";
    private const int TimeoutSeconds = 30;
    private const int OperatorGrantMb = 128;
    private const long SessionCeilingBytes = 256L * 1024 * 1024;

    /// <param name="runEveryPage">
    /// True for an export: a paginated page's visuals are otherwise built without data, because on
    /// screen they wait for the reader to answer their prompts and press Run.
    /// </param>
    /// <param name="parameters">
    /// Answers to the report's <c>INPUT</c> prompts, seeded the way <c>--var</c> seeds them.
    /// </param>
    public async Task<ReportManifest> BuildPreviewAsync(
        string scriptText,
        CancellationToken cancellationToken = default,
        bool runEveryPage = false,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            throw new ArgumentException("Nothing to preview — the script is empty.");

        // A preview executes the whole script. Run stops for confirmation before destroying
        // persistent data; a preview has no confirmation step, so it refuses instead.
        var destructive = WorkstationRunGuard.FindDestructiveStatements(scriptText);
        if (destructive.Count > 0)
        {
            throw new InvalidOperationException(
                "Preview does not run statements that destroy persistent data: " + string.Join("; ", destructive) +
                ". Run the script with confirmation first, or move this work to a #temp table.");
        }

        var script =
            $"SET OPERATOR_MEMORY_GRANT = {OperatorGrantMb};\n" +
            $"SET MAX_SESSION_SIZE = {SessionCeilingBytes};\n" +
            scriptText;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var sessionContext = new CliContext
        {
            Command = "build",
            IsSilentMode = true,
            SessionId = Guid.NewGuid().ToString("N")
        };
        ApplyParameters(sessionContext, parameters);

        // Disposed once the manifest is built; the builder still reads the session's evaluator.
        await using var session = new ExecutionSession(services, sessionContext, logger);
        var result = await session.ExecuteAsync(script, timeout.Token, "workstation-preview");

        if (!result.Success)
        {
            var message = result.Diagnostics.Count > 0
                ? string.Join("; ", result.Diagnostics.Select(d => d.Message))
                : "Preview build failed.";
            throw new InvalidOperationException(SecretRedactor.Redact(message));
        }

        var evaluator = session.LastEvaluator
            ?? throw new InvalidOperationException("Preview produced no report context.");

        // scriptSource names the report (PDF title fallback, Markdown "Source:"); every file-based
        // host passes a path. An unsaved buffer has none, so name it after the preview.
        return await new ManifestBuilder(evaluator).BuildAsync(PreviewSourceName, deferPaginatedPages: !runEveryPage);
    }

    /// <summary>
    /// Seeds answered prompts onto the session, exactly as <c>--var</c> does: the same parser, the
    /// same <c>@</c>-prefixed keys, and the same precedence — <c>DECLARE</c> prefers an injected
    /// value to its own initial one.
    /// </summary>
    private static void ApplyParameters(CliContext context, IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null) return;
        foreach (var (name, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var key = name.StartsWith('@') ? name : "@" + name;
            context.Variables[key] = ETL_SQL.Core.Common.VariableOverrideValueParser.Parse(value);
        }
    }
}
