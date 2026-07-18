using ETL_SQL.Core.Formatting;

namespace ETL_SQL.WorkstationEditor;

public sealed class WorkstationFormatService(WorkstationWorkspace workspace)
{
    public FormatResponse Format(FormatRequest request)
    {
        var script = request.Script ?? string.Empty;
        if (string.IsNullOrWhiteSpace(script))
            return new FormatResponse(script, []);

        try
        {
            var options = FormatterOptions.LoadFromWorkspace(GetFormatterStartPath(request.DocumentUri))
                ?? new FormatterOptions();
            return new FormatResponse(SqlFormatter.Format(script, options), []);
        }
        catch (Exception ex)
        {
            return new FormatResponse(script, [new FormatDiagnostic(ex.Message)]);
        }
    }

    private string? GetFormatterStartPath(string? documentUri)
    {
        if (string.IsNullOrWhiteSpace(documentUri) ||
            string.Equals(documentUri, "untitled.etlsql", StringComparison.OrdinalIgnoreCase))
        {
            return workspace.Root;
        }

        try
        {
            return workspace.ResolveEditablePath(documentUri);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return workspace.Root;
        }
    }
}

public sealed record FormatRequest(string? Script, string? DocumentUri);

public sealed record FormatResponse(string Script, IReadOnlyList<FormatDiagnostic> Diagnostics);

public sealed record FormatDiagnostic(string Message);
