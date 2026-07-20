using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using ETL_SQL.Orchestrator.Execution;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationRunService(IServiceProvider services, ETL_SQL.Common.ILogger logger)
{
    private const int DefaultRowLimit = 100;
    private const int MaxRowLimit = 1000;
    private const int MaxLineageEntries = 500;
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

        var rowLimit = Math.Clamp(request.RowLimit ?? DefaultRowLimit, 1, MaxRowLimit);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RunTimeout);

        var context = new CliContext
        {
            Command = "run",
            BatchSize = rowLimit,
            IsSilentMode = true,
            SessionId = Guid.NewGuid().ToString("N")
        };

        await using var session = new ExecutionSession(services, context, logger);
        var result = await session.ExecuteAsync(script, timeout.Token, "workstation-editor-run");
        var table = session.LastEvaluator?.LastResult ?? result.ResultsTables.LastOrDefault();
        var diagnostics = ToRunDiagnostics(result);

        if (!result.Success)
        {
            var message = diagnostics.Count > 0
                ? diagnostics[0].Message
                : "Script execution failed.";
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
        var lineageList = session.LastEvaluator?.LineageTracker?.GetFullLineage()?
            .Take(MaxLineageEntries)
            .Select(e => new LineageEntryDto(
                e.TargetTable ?? string.Empty,
                e.TargetColumn ?? string.Empty,
                e.SourceTables != null ? string.Join(", ", e.SourceTables) : string.Empty,
                e.SourceColumns != null ? string.Join(", ", e.SourceColumns) : string.Empty,
                Redact(e.Description),
                e.TransformationKind.ToString(),
                Redact(e.TransformationExpression)))
            .ToList();

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
}

public sealed record RunRequest(string? Script, string? Selection = null, string? DocumentUri = null, int? RowLimit = null);
public sealed record PreviewRequest(string? Script);

public sealed record LineageEntryDto(
    string TargetTable,
    string TargetColumn,
    string SourceTables,
    string SourceColumns,
    string? Description = null,
    string? TransformationKind = null,
    string? TransformationExpression = null);

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
