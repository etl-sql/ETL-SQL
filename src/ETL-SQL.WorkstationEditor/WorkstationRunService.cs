using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Orchestrator.Execution;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationRunService(IServiceProvider services, ETL_SQL.Common.ILogger logger)
{
    private const int DefaultRowLimit = 100;
    private const int MaxRowLimit = 1000;
    private const int MaxLineageEntries = 500;
    private const int OperatorGrantMb = 128;
    private const long SessionCeilingBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);

    public async Task<RunResponse> RunAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        var script = string.IsNullOrWhiteSpace(request.Selection)
            ? request.Script ?? string.Empty
            : request.Selection!;

        if (string.IsNullOrWhiteSpace(script))
        {
            return RunResponse.Failed("RUN_EMPTY", "Select or type a script before running.");
        }

        // Destructive statements need an explicit confirmation. The engine's MutationGuardrailPolicy
        // only fires for enterprise-enrolled processes, which a standalone workstation is not.
        if (!request.ConfirmDestructive)
        {
            var destructive = WorkstationRunGuard.FindDestructiveStatements(script);
            if (destructive.Count > 0)
            {
                return RunResponse.Failed(
                    "RUN_DESTRUCTIVE",
                    "This run would destroy persistent data: " + string.Join("; ", destructive) +
                    ". Re-run and confirm if that is intended.");
            }
        }

        var rowLimit = Math.Clamp(request.RowLimit ?? DefaultRowLimit, 1, MaxRowLimit);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RunTimeout);

        // Bound the run's memory the same way the Portal bounds an interactive run, so a careless
        // local query cannot take the machine down.
        var governedScript =
            $"SET OPERATOR_MEMORY_GRANT = {OperatorGrantMb};\n" +
            $"SET MAX_SESSION_SIZE = {SessionCeilingBytes};\n" +
            script;

        var context = new CliContext
        {
            Command = "run",
            BatchSize = rowLimit,
            IsSilentMode = true,
            SessionId = Guid.NewGuid().ToString("N")
        };
        ApplyParameters(context, request.Parameters);

        // Preview-as is the only identity this host injects. Without one a workstation run carries
        // no identity at all, so row-level security is fail-closed — HAS_GROUP is false and
        // @@CURRENT_USER is null — and an RLS-guarded report shows nothing here with no explanation.
        // Naming an audience is what lets the author see the predicate actually work.
        var identity = request.PreviewAs is { } preview
            ? ETL_SQL.Core.Governance.ExecutionIdentity.Preview(
                preview.Label, preview.Groups, preview.Roles, realUser: "workstation", tenantId: null)
            : null;

        await using var session = new ExecutionSession(services, context, logger);
        var result = await session.ExecuteAsync(
            governedScript, timeout.Token, "workstation-editor-run", executionIdentity: identity);
        var table = session.LastEvaluator?.LastResult ?? result.ResultsTables.LastOrDefault();
        var diagnostics = ToRunDiagnostics(result);

        var historyUri = request.DocumentUri ?? "(untitled)";

        if (!result.Success)
        {
            var message = diagnostics.Count > 0
                ? diagnostics[0].Message
                : "Script execution failed.";
            AppendHistory(historyUri, script, success: false, result.ExecutionTimeMs, rowCount: 0);
            return new RunResponse(
                false,
                [],
                [],
                0,
                false,
                result.ExecutionTimeMs,
                message,
                diagnostics,
                result.Messages.Select(m => m.Message).ToList(),
                result.ExecutionTree?.ToSnapshot());
        }

        var (columns, rows, rowCount, capped) = ToRows(table, rowLimit);
        var successMessage = capped
            ? $"Showing first {rows.Count} rows; result was capped."
            : $"Returned {rows.Count} row{(rows.Count == 1 ? string.Empty : "s")}.";

        // TransformationExpression and Description carry script text verbatim, so they go through
        // the same redaction as error messages before reaching the browser — the workspace
        // security model requires that no resolved secret leaves the process in a response.
        // Capped because GetFullLineage() is unbounded while the result grid is capped at RowLimit.
        var connections = session.LastEvaluator?.Connections
            ?? new Dictionary<string, IDataSource>(StringComparer.OrdinalIgnoreCase);

        var lineageList = session.LastEvaluator?.LineageTracker?.GetFullLineage()?
            .Take(MaxLineageEntries)
            .Select(e => new LineageEntryDto(
                e.TargetTable ?? string.Empty,
                e.TargetColumn ?? string.Empty,
                e.SourceTables != null ? string.Join(", ", e.SourceTables) : string.Empty,
                e.SourceColumns != null ? string.Join(", ", e.SourceColumns) : string.Empty,
                Redact(e.Description),
                e.TransformationKind.ToString(),
                Redact(e.TransformationExpression),
                BuildSourceLabels(e.SourceTables, connections)))
            .ToList();

        AppendHistory(historyUri, script, success: true, result.ExecutionTimeMs, rowCount);

        return new RunResponse(
            true,
            columns,
            rows,
            rowCount,
            capped,
            result.ExecutionTimeMs,
            successMessage,
            diagnostics,
            result.Messages.Select(m => m.Message).ToList(),
            result.ExecutionTree?.ToSnapshot(),
            lineageList);
    }

    private static string? Redact(string? value) =>
        string.IsNullOrEmpty(value) ? value : SecretRedactor.Redact(value);

    /// <summary>
    /// Builds a list of human-readable physical source identifiers, one per source table name.
    /// For FLATFILE sources this is "CSV: &lt;path&gt;"; for SQL sources it is
    /// "&lt;TYPE&gt;: &lt;database/path&gt;". Connection strings, passwords, and ENC: values
    /// are never included — only the connector type and the credential-free Path property.
    /// Returns null when no connections can be resolved.
    /// </summary>
    private static IReadOnlyList<string>? BuildSourceLabels(
        IReadOnlyList<string>? sourceTables,
        IDictionary<string, IDataSource> connections)
    {
        if (sourceTables == null || sourceTables.Count == 0) return null;

        var labels = new List<string>(sourceTables.Count);
        bool anyResolved = false;
        foreach (var src in sourceTables)
        {
            // Source table names may be qualified: "pats.FILE" -> connection name is "pats"
            var connAlias = src.Split('.')[0];
            if (connections.TryGetValue(connAlias, out var ds))
            {
                anyResolved = true;
                var type = ds.ConnectorType?.ToUpperInvariant() ?? "SOURCE";
                var path = ds.Path;
                labels.Add(string.IsNullOrEmpty(path)
                    ? type
                    : $"{type}: {path}");
            }
            else
            {
                labels.Add(src); // keep the alias as fallback
            }
        }
        return anyResolved ? labels : null;
    }

    /// <summary>
    /// Appends a one-line JSON record of the run to a local history file.
    /// </summary>
    /// <remarks>
    /// Local-only accountability: what ran, when, against which document, and the outcome. The
    /// script text is redacted and truncated — the point is a trail, not a copy of the work. Never
    /// throws: losing a history line must not fail a run the user asked for.
    /// </remarks>
    private void AppendHistory(string documentUri, string script, bool success, long elapsedMs, int rowCount)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ETL-SQL", "workstation-editor");
            Directory.CreateDirectory(root);

            var summary = SecretRedactor.Redact(script.Trim().Replace("\r", " ").Replace("\n", " ")) ?? string.Empty;
            if (summary.Length > 400) summary = summary[..400] + "…";

            var line = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTime.UtcNow.ToString("O"),
                documentUri,
                success,
                elapsedMs,
                rowCount,
                script = summary,
            });

            File.AppendAllText(Path.Combine(root, "run-history.jsonl"), line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            logger.Warning("Could not append workstation run history: {Message}", ex.Message);
        }
    }

    private static IReadOnlyList<RunDiagnostic> ToRunDiagnostics(ExecutionResult result)
    {
        var diagnostics = new List<RunDiagnostic>();

        diagnostics.AddRange(result.Diagnostics.Select(d => new RunDiagnostic(
            d.Line,
            d.Column,
            d.Severity.ToString(),
            d.Message,
            "Parser")));

        diagnostics.AddRange(result.LintResults.Select(d => new RunDiagnostic(
            d.LineNumber,
            d.ColumnNumber,
            d.Severity.ToString(),
            d.Message,
            d.Code ?? d.RuleName)));

        return diagnostics;
    }

    private static (IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int RowCount, bool Capped)
        ToRows(DataTable? table, int rowLimit)
    {
        if (table is null)
            return ([], [], 0, false);

        var columns = table.ColumnNames;
        var rows = table.Rows
            .Take(rowLimit)
            .Select(row => columns.ToDictionary<string, string, object?>(
                column => column,
                column => NormalizeValue(row[column]),
                StringComparer.OrdinalIgnoreCase))
            .Cast<IReadOnlyDictionary<string, object?>>()
            .ToList();
        var rowCount = table.TotalRowsMatched > 0 ? table.TotalRowsMatched : table.Rows.Count;
        var capped = table.IsCapped || table.Rows.Count > rows.Count || table.TotalRowsMatched > rows.Count;

        return (columns, rows, rowCount, capped);
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        null => null,
        DateTime dateTime => dateTime.ToString("O"),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
        _ => value
    };

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

public sealed record RunRequest(
    string? Script,
    string? Selection = null,
    string? DocumentUri = null,
    int? RowLimit = null,
    /// <summary>Set once the user has acknowledged a destructive-statement warning.</summary>
    bool ConfirmDestructive = false,
    /// <summary>Answers to the script's INPUT prompts, keyed by name with or without '@'.</summary>
    Dictionary<string, string>? Parameters = null,
    /// <summary>The audience to evaluate row-level-security predicates as, or null to run as nobody.</summary>
    PreviewAsAuthoringRequest? PreviewAs = null);

/// <param name="Label">What <c>@@CURRENT_USER</c> answers. A description of an audience, not a person.</param>
public sealed record PreviewAsAuthoringRequest(
    string? Label = null,
    List<string>? Groups = null,
    List<string>? Roles = null);
/// <param name="Parameters">Answers to the report's INPUT prompts, keyed by name with or without '@'.</param>
/// <param name="RunEveryPage">True to build the finished document rather than a deferred screen.</param>
public sealed record PreviewRequest(
    string? Script,
    Dictionary<string, string>? Parameters = null,
    bool RunEveryPage = false);

public sealed record LineageEntryDto(
    string TargetTable,
    string TargetColumn,
    string SourceTables,
    string SourceColumns,
    string? Description = null,
    string? TransformationKind = null,
    string? TransformationExpression = null,
    /// <summary>
    /// Human-readable physical identifiers for each source table, e.g.
    /// "FLATFILE: C:\tmp\patients.csv" or "MSSQL: hospital". Never contains
    /// raw connection strings or ENC: values. Null when none could be resolved.
    /// </summary>
    IReadOnlyList<string>? SourceLabels = null);

public sealed record RunResponse(
    bool Success,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowCount,
    bool Capped,
    long ElapsedMs,
    string Message,
    IReadOnlyList<RunDiagnostic> Diagnostics,
    IReadOnlyList<string> Messages,
    /// <summary>Hierarchical execution-tree snapshot that drives the editor's Pipeline (DAG) tab.</summary>
    object? Pipeline = null,
    IReadOnlyList<LineageEntryDto>? Lineage = null)
{
    public static RunResponse Failed(string code, string message) =>
        new(false, [], [], 0, false, 0, message, [new RunDiagnostic(0, 0, "Error", message, code)], []);
}

public sealed record RunDiagnostic(int Line, int Column, string Severity, string Message, string? Code = null);
