using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Security;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.WorkstationEditor;

/// <summary>
/// Serves the design-time data sample that Studio's visual canvas is built on.
/// </summary>
/// <remarks>
/// <para>Studio disables its entire visual palette until a sample exists, so without this route the
/// desktop canvas can never be used at all — the Portal had the route and the desktop did not.</para>
/// <para>The sample is not a privileged path: it validates the table against the connection's real
/// schema and then runs an ordinary bounded <c>SELECT</c> through <see cref="WorkstationRunService"/>,
/// inheriting its row cap, timeout, memory grant, destructive-statement guard, and secret redaction.
/// Identifier quoting is shared with the Portal so both hosts sample identically.</para>
/// </remarks>
public sealed class WorkstationDataSampleService(
    IMetadataManager metadata,
    WorkstationMetadataService metadataRegistration,
    WorkstationRunService runService)
{
    /// <summary>Rows to sample. Matches the Portal's design-time sample budget.</summary>
    public const int SampleRowLimit = 250;

    public async Task<DataSampleResponse> SampleAsync(DataSampleRequest request, CancellationToken cancellationToken = default)
    {
        var sourceKind = request.SourceKind?.Trim().ToLowerInvariant();
        if (sourceKind == "dataset")
            return await SampleDatasetAsync(request, cancellationToken);
        if (sourceKind is not (null or "connection"))
            throw new ArgumentException("SourceKind must be 'connection' or 'dataset' on the desktop host.");

        var connection = Require(request.Connection, "connection");
        var table = Require(request.Table, "table");

        // A script owns its own connections on this host, and registration is explicit. Sampling
        // must not depend on an earlier analyze request having happened to register them, so
        // register from the caller's script when one is supplied.
        if (!string.IsNullOrWhiteSpace(request.Script) && !string.IsNullOrWhiteSpace(request.DocumentUri))
        {
            try
            {
                await metadataRegistration.RegisterScriptMetadataAsync(request.Script!, request.DocumentUri!);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A script that does not parse yet is an ordinary editing state; fall back to
                // whatever metadata is already registered for the document.
            }
        }

        // Existence validation and the authorization boundary in one step: a table the connection's
        // schema does not list cannot be sampled, and the schema read itself is policy-checked.
        var tables = await metadata.GetTablesAsync(connection, request.DocumentUri);
        var resolved = tables.FirstOrDefault(candidate =>
            string.Equals(candidate, table, StringComparison.OrdinalIgnoreCase));
        if (resolved is null)
            throw new KeyNotFoundException("Table not found in the connection schema.");

        // A script owns its own connections here — unlike the Portal, there is no catalog to resolve
        // an alias against — so the sample must carry the alias's CREATE CONNECTION with it.
        var script = PrependConnectionDefinitions(request.Script, connection)
            + BuildSampleScript(connection, resolved);
        var result = await runService.RunAsync(
            new RunRequest(script, DocumentUri: request.DocumentUri, RowLimit: SampleRowLimit),
            cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException(SecretRedactor.Redact(result.Message));

        return new DataSampleResponse(
            "connection",
            $"{connection}.{resolved}",
            result.Columns,
            result.Rows,
            result.RowCount,
            result.Capped,
            false,
            result.ElapsedMs,
            result.Message);
    }

    private async Task<DataSampleResponse> SampleDatasetAsync(
        DataSampleRequest request,
        CancellationToken cancellationToken)
    {
        var datasetName = Require(request.Dataset, "dataset");
        var scriptText = request.Script ?? throw new ArgumentException("The current script is required for dataset preview.");
        var parsed = new CoreParser(new Lexer(scriptText).Tokenize(), scriptText).Parse();
        var error = parsed.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (error is not null)
            throw new ArgumentException(error.Message);

        var dataset = parsed.Statements.OfType<CreateDatasetStatement>().FirstOrDefault(candidate =>
            string.Equals(candidate.TempTableName.TrimStart('&'), datasetName.TrimStart('&'), StringComparison.OrdinalIgnoreCase));
        if (dataset is null)
            throw new KeyNotFoundException("The selected dataset is not defined by the current script.");
        if (dataset.SourceQuery is not SelectStatement { IntoTable: null }
            && (dataset.SourceQuery is not SetOperationStatement setOperation || !ReadOnlyQueryPolicy.IsReadOnly(setOperation)))
            throw new ArgumentException("Dataset preview accepts only a read-only query.");

        await metadataRegistration.RegisterScriptMetadataAsync(scriptText, request.DocumentUri ?? "studio");
        var aliases = dataset.SourceQuery.GetSourceTables()
            .Where(source => source.Contains('.', StringComparison.Ordinal))
            .Select(source => source.Split('.', 2)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var definitions = parsed.Statements.OfType<CreateConnectionStatement>()
            .Where(statement => aliases.Contains(statement.name))
            .Select(AstSerializer.Format);
        var previewScript = string.Join(Environment.NewLine, definitions.Append(dataset.SourceQuery.ToSql().Trim().TrimEnd(';') + ";"));
        var result = await runService.RunAsync(
            new RunRequest(previewScript, DocumentUri: request.DocumentUri, RowLimit: SampleRowLimit),
            cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException(SecretRedactor.Redact(result.Message));

        return new DataSampleResponse(
            "dataset",
            datasetName,
            result.Columns,
            result.Rows,
            result.RowCount,
            result.Capped,
            false,
            result.ElapsedMs,
            result.Message);
    }

    private static string PrependConnectionDefinitions(string? scriptText, string connection)
    {
        if (string.IsNullOrWhiteSpace(scriptText))
            return string.Empty;

        try
        {
            var parsed = new CoreParser(new Lexer(scriptText).Tokenize(), scriptText).Parse();
            var definitions = parsed.Statements
                .OfType<CreateConnectionStatement>()
                .Where(statement => string.Equals(statement.name, connection, StringComparison.OrdinalIgnoreCase))
                .Select(AstSerializer.Format)
                .ToList();

            return definitions.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, definitions) + Environment.NewLine;
        }
        catch
        {
            // Mid-edit scripts routinely fail to parse; sampling then relies on already-registered
            // metadata rather than failing outright.
            return string.Empty;
        }
    }

    public static string BuildSampleScript(string connection, string table)
        => $"SELECT * FROM {QuoteIdentifier(connection)}.{QuoteQualifiedIdentifier(table)};";

    private static string Require(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"A {label} is required.")
            : value.Trim();

    private static string QuoteQualifiedIdentifier(string value)
        => string.Join('.', value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(QuoteIdentifier));

    private static string QuoteIdentifier(string value)
    {
        var trimmed = value.Trim().Trim('[', ']');
        if (trimmed.Length == 0)
            throw new ArgumentException("Identifier cannot be empty.");
        if (trimmed.Contains(']', StringComparison.Ordinal))
            throw new ArgumentException("Identifier contains an invalid character.");
        return $"[{trimmed}]";
    }
}

public sealed record DataSampleRequest(
    string? SourceKind,
    string? Connection,
    string? Table,
    string? DocumentUri = null,
    /// <summary>Current editor text, so the connection can be registered without a prior analyze.</summary>
    string? Script = null,
    string? Dataset = null);

public sealed record DataSampleResponse(
    string SourceKind,
    string Source,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowCount,
    bool Capped,
    bool ByteCapped,
    long ElapsedMs,
    string Message);
