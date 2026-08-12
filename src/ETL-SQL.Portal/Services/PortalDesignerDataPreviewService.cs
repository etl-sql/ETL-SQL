using System.Security.Claims;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Models;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Builds governed, bounded Studio row previews without accepting caller-authored connection
/// definitions or arbitrary statements. Catalog sources are first resolved through schema ACLs;
/// temp tables are recreated only from the read-only prefix that materializes the selected table.
/// </summary>
public sealed class PortalDesignerDataPreviewService(
    PortalDesignerSchemaService schemaService,
    PortalDesignerRunService runService)
{
    public async Task<DesignerDataPreviewResponse> PreviewAsync(
        DesignerDataPreviewRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var sourceKind = request.SourceKind?.Trim().ToLowerInvariant();
        return sourceKind switch
        {
            "connection" => await PreviewConnectionAsync(request, user, cancellationToken),
            "temp" => await PreviewTempTableAsync(request, user, cancellationToken),
            _ => throw new ArgumentException("SourceKind must be 'connection' or 'temp'.")
        };
    }

    private async Task<DesignerDataPreviewResponse> PreviewConnectionAsync(
        DesignerDataPreviewRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var alias = PortalDesignerSchemaService.NormalizeConnectionRef(
            request.Connection ?? throw new ArgumentException("A connection is required for source preview."));
        var table = RequireName(request.Table, "table");

        // This is both existence validation and the resource authorization boundary. A caller cannot
        // preview a table through an alias that the shared catalog refuses for their identity.
        var schema = await schemaService.GetSchemaAsync(alias, user, request.DocumentUri, cancellationToken);
        var resolvedTable = schema.Tables.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, table, StringComparison.OrdinalIgnoreCase))?.Name;
        if (resolvedTable is null)
            throw new KeyNotFoundException("Table not found in the authorized connection schema.");

        var script = BuildSourcePreviewScript(alias, resolvedTable);
        var result = await runService.RunAsync(
            new RunDesignerRequest(script, ConnectionRef: alias, DocumentUri: request.DocumentUri),
            user,
            cancellationToken);
        return ToResponse("connection", $"{alias}.{resolvedTable}", result);
    }

    private async Task<DesignerDataPreviewResponse> PreviewTempTableAsync(
        DesignerDataPreviewRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var tempTable = RequireName(request.TempTable, "temp table");
        if (!tempTable.StartsWith('#') || tempTable.Length == 1)
            throw new ArgumentException("A temp-table preview requires a #name target.");
        var script = BuildTempPreviewScript(
            request.Script ?? throw new ArgumentException("The current script is required for temp-table preview."),
            tempTable);

        var result = await runService.RunAsync(
            new RunDesignerRequest(script, ConnectionRef: request.Connection, DocumentUri: request.DocumentUri),
            user,
            cancellationToken);
        return ToResponse("temp", tempTable, result);
    }

    public static string BuildSourcePreviewScript(string connection, string table)
        => $"SELECT * FROM {QuoteIdentifier(connection)}.{QuoteQualifiedIdentifier(table)};";

    public static string BuildTempPreviewScript(string scriptText, string tempTable)
    {
        var parsed = new CoreParser(new Lexer(scriptText).Tokenize(), scriptText).Parse();
        var error = parsed.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (error is not null)
            throw new ArgumentException(error.Message);

        var statements = parsed.Statements.Where(statement => statement is not NoOpStatement).ToList();
        var materializerIndex = statements.FindIndex(statement => statement is SelectStatement select
            && select.IntoTable is not null
            && string.Equals(select.IntoTable.TableName, tempTable, StringComparison.OrdinalIgnoreCase));
        if (materializerIndex < 0)
            throw new KeyNotFoundException("The selected temp table is not materialized by the current script.");

        var builder = new StringBuilder();
        for (var index = 0; index <= materializerIndex; index++)
        {
            var statement = statements[index];
            if (PortalInteractiveRunPolicy.Reject(statement) is { } rejection)
            {
                throw new ArgumentException(
                    $"Temp-table preview cannot execute statement {index + 1}: {rejection}");
            }
            builder.AppendLine(statement.ToSql().Trim().TrimEnd(';') + ";");
        }

        builder.Append("SELECT * FROM ").Append(QuoteIdentifier(tempTable)).AppendLine(";");
        return builder.ToString();
    }

    private static DesignerDataPreviewResponse ToResponse(
        string sourceKind,
        string source,
        RunDesignerResponse result) =>
        new(
            sourceKind,
            source,
            result.Columns,
            result.Rows,
            result.RowCount,
            result.Capped,
            result.ByteCapped,
            result.BytesReturned,
            result.ElapsedMs,
            result.Message);

    private static string RequireName(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"A {label} is required.")
            : value.Trim();

    private static string QuoteQualifiedIdentifier(string value)
        => string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(QuoteIdentifier));

    private static string QuoteIdentifier(string value)
        => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
