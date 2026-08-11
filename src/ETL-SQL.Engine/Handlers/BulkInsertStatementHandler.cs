using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the BULK INSERT statement, providing high-performance data loading from flat files into target tables.
/// Supports FIELDTERMINATOR, ROWTERMINATOR, FIRSTROW, and batching.
/// </summary>
public class BulkInsertStatementHandler(IConnectorRegistry connectorRegistry, ILogger logger) : IStatementHandler
{
    private readonly IConnectorRegistry _connectorRegistry = connectorRegistry;
    private readonly ILogger _logger = logger;


    public Type SupportedStatementType => typeof(BulkInsertStatement);
    /// <summary>Executes the BULK INSERT statement, resolving the source file and streaming data to the destination.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (BulkInsertStatement)statement;


        // 1. Resolve Target
        var destination = await context.ResolveDataSourceAsync(stmt.TargetTable);
        if (destination == null)
            throw new ExecutionException($"Target table {stmt.TargetTable.TableName} not found.");

        string connName = stmt.TargetTable.ConnectionName ?? stmt.TargetTable.TableName;

        if (stmt.Columns != null && stmt.Columns.Count > 0)
        {
            foreach (var col in stmt.Columns)
            {
                context.LineageTracker.Record(
                    connName,
                    stmt.GetSourceTables(),
                    "BULK INSERT",
                    targetColumn: col,
                    sourceColumns: new List<string> { col },
                    metadata: stmt.Metadata,
                    line: stmt.Line,
                    column: stmt.Column);
            }
        }
        else
        {
            context.LineageTracker.Record(
                connName,
                stmt.GetSourceTables(),
                "BULK INSERT",
                metadata: stmt.Metadata,
                line: stmt.Line,
                column: stmt.Column);
        }

        // 2. Evaluate Options
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var opt in stmt.Options)
        {
            var val = (await context.EvaluateValue(opt.Value, new Row(), decryptSensitive: true))?.ToString() ?? "";
            options[opt.Key] = val;
        }

        // 3. Map Bulk Options to FlatFile Options
        var ffOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\\", "\\");
        }

        if (options.TryGetValue("FIELDTERMINATOR", out var ft)) ffOptions["DELIMITER"] = Unescape(ft);
        if (options.TryGetValue("ROWTERMINATOR", out var rt)) ffOptions["ROW_DELIMITER"] = Unescape(rt);

        int firstRow = 1;
        if (options.TryGetValue("FIRSTROW", out var fr) && int.TryParse(fr, out var parsedFirstRow))
            firstRow = parsedFirstRow;

        // A header is present when the caller skipped a row (the T-SQL idiom, FIRSTROW = 2) or said
        // so outright. Knowing that is what lets the load map by column name below; T-SQL cannot,
        // because it never reads the header, and needs a format file to map by anything but ordinal.
        bool headerPresent = firstRow >= 2
            || (options.TryGetValue("HEADER", out var hdr) && IsTrue(hdr));

        if (headerPresent)
        {
            // Let the reader consume row 1 as names rather than discarding it positionally, so the
            // names reach the mapping step. FIRSTROW counts the header, so anything beyond row 2 is
            // additional data to skip.
            ffOptions["HEADER"] = "TRUE";
            ffOptions["START_AT"] = Math.Max(0, firstRow - 2).ToString();
        }
        else
        {
            ffOptions["HEADER"] = "FALSE";
            ffOptions["START_AT"] = Math.Max(0, firstRow - 1).ToString();
        }

        foreach (var opt in options)
        {
            if (!ffOptions.ContainsKey(opt.Key)) ffOptions[opt.Key] = opt.Value;
        }

        // MAPPING = 'POSITION' forces T-SQL's ordinal pairing for scripts ported from SQL Server,
        // where the header's names are data rather than metadata.
        bool forcePositional = options.TryGetValue("MAPPING", out var mappingMode)
            && mappingMode.Trim().Equals("POSITION", StringComparison.OrdinalIgnoreCase);

        // 4. Create Source via ConnectorRegistry to avoid circular dependency
        var connector = _connectorRegistry.GetConnector("FLATFILE");
        if (connector == null)
            throw new ExecutionException("FLATFILE connector not found.");

        List<ColumnDefinition>? destColumnDefs = null;
        try
        {
            var destCols = await destination.GetColumnsAsync(context.CancellationToken);
            destColumnDefs = destCols.Select(c => new ColumnDefinition(c, "VARCHAR", false)).ToList();
        }
        catch (Exception ex)
        {
            _logger.Debug("[BulkInsertStatementHandler] Could not retrieve destination columns for template schema: {Message}", ex.Message);
        }

        string resolvedPath = context.ResolvePath(stmt.FilePath);
        // Flat-file connectors perform their own file-type validation; the authorizer adds
        // enterprise-root and policy-freshness enforcement at this operation boundary.
        resolvedPath = new FileSystemPolicyAuthorizer(context.SecurityService)
            .Authorize(context, resolvedPath, FileSystemAccessKind.Read, validateFileType: false)
            .CanonicalPath;

        // A missing source is a failed load, not an empty one. The flat-file reader yields no
        // batches when the path does not exist, so without this a typo or an absent daily drop
        // loads zero rows, reports success, and leaves a table that is merely empty rather than
        // wrong — which is the harder kind to notice downstream. T-SQL refuses this too.
        if (!System.IO.File.Exists(resolvedPath))
        {
            throw new ExecutionException(
                $"Bulk insert source file not found: '{stmt.FilePath}' (resolved to '{resolvedPath}'). "
                + "No rows were loaded.");
        }

        // The template schema names columns for a header-less file. When the file has its own header
        // those names are the ones the mapping needs, so the template must not stand in for them.
        var source = connector.CreateDataSource(
            context, resolvedPath, ffOptions, headerPresent ? null : destColumnDefs);

        try
        {
            _logger.Debug("Bulk loading from {FilePath} into {TableName}", stmt.FilePath, stmt.TargetTable.TableName);

            int batchSize = context.EffectiveBatchSize;
            if (options.TryGetValue("BATCHSIZE", out var bs) && int.TryParse(bs, out var bsv))
                batchSize = bsv;

            int maxErrors = 0;
            if (options.TryGetValue("MAXERRORS", out var me) && int.TryParse(me, out var mev))
                maxErrors = mev;

            var batches = context.InterceptProgress(source.ReadBatches(batchSize, context.CancellationToken));


            // Get destination columns for metadata validation
            var destColumns = (await destination.GetColumnsAsync(context.CancellationToken)).ToList();

            // Determine mapping: use explicit columns if provided, otherwise positional mapping to destination columns
            var mapping = stmt.Columns ?? destColumns;

            int count = 0;
            int errorCount = 0;
            int batchIndex = 0;

            await foreach (var batch in batches.WithCancellation(context.CancellationToken))
            {
                batchIndex++;
                // Map columns by position from source to destination
                var mappedBatch = new DataTable();
                mappedBatch.SetColumns(destColumns);

                // Resolve the pairing once per batch, not per row. When the file carries a header and
                // every target column matches one of its names, pair by name: positional pairing is
                // what lets a file whose columns are merely in a different order load transposed,
                // with no error whenever the types happen to be compatible. Any target left
                // unmatched means the header does not describe this table, so fall back to ordinal
                // rather than half-mapping — and say so, because a silent fallback is the same
                // defect wearing a different hat.
                var pairing = ResolvePairing(
                    mapping, batch.ColumnNames, headerPresent && !forcePositional, out var pairedByName);

                if (batchIndex == 1)
                {
                    if (pairedByName)
                        _logger.Debug("Bulk insert into '{Target}' paired {Count} columns by header name.",
                            stmt.TargetTable.TableName, pairing.Count);
                    else if (headerPresent && !forcePositional)
                        _logger.Warning(
                            "Bulk insert into '{Target}': the file's header does not name every target column, "
                            + "so columns were paired by position instead. Header: [{Header}]; target: [{Target2}]. "
                            + "Positional pairing loads a reordered file transposed without error.",
                            stmt.TargetTable.TableName, string.Join(", ", batch.ColumnNames), string.Join(", ", mapping));
                }

                foreach (var sourceRow in batch.Rows)
                {
                    var mappedRow = new Row();
                    foreach (var (target, sourceColumn) in pairing)
                    {
                        mappedRow[target] = NullIfBlank(sourceRow[sourceColumn]);
                    }
                    await mappedBatch.AddRowAsync(mappedRow);
                }

                try
                {
                    if (context.IsWhatIf)
                    {
                        // Dry run: just count
                    }
                    else
                    {
                        // Security Hardening: Block writing data into script files
                        if (!string.IsNullOrEmpty(destination.Path))
                        {
                            context.SecurityService.ValidateWriteAccess(destination.Path);
                        }

                        await destination.WriteBatches(new[] { mappedBatch }.ToAsyncEnumerable(), append: true, cancellationToken: context.CancellationToken);
                    }
                    count += mappedBatch.Rows.Count;
                }
                catch (Exception ex)
                {
                    if (context.IsWhatIf) throw;
                    if (errorCount < maxErrors)
                    {
                        int rowStart = count + 1;
                        int rowEnd = count + mappedBatch.Rows.Count;
                        _logger.Warning("Batch #{BatchIndex} write failed (rows {RowStart}–{RowEnd}, target '{Target}'): {Message}. Bisecting to isolate bad rows (MAXERRORS={MaxErrors}).",
                            batchIndex, rowStart, rowEnd, stmt.TargetTable.TableName, ex.Message, maxErrors);

                        // Bisect the failing batch: try halves recursively until bad rows are isolated.
                        // The full batch already failed above; start bisection directly into halves
                        // to avoid a redundant full-batch retry. Each half is tried as a unit first,
                        // so clean halves succeed in one write. O(N + M·log N) write calls total
                        // vs. O(N) for the old row-by-row loop (M = number of bad rows).
                        var mid = mappedBatch.Rows.Count / 2;
                        var (w1, e1) = await WriteBisect(
                            mappedBatch.Rows.Take(mid).ToList(), destColumns, destination, context.CancellationToken,
                            batchIndex, stmt.TargetTable.TableName, maxErrors, errorCount);
                        var (w2, e2) = await WriteBisect(
                            mappedBatch.Rows.Skip(mid).ToList(), destColumns, destination, context.CancellationToken,
                            batchIndex, stmt.TargetTable.TableName, maxErrors, errorCount + e1);
                        count += w1 + w2;
                        errorCount += e1 + e2;
                    }
                    else
                    {
                        throw new ExecutionException($"Bulk insert into '{stmt.TargetTable.TableName}' failed and MAXERRORS is 0. Error: {ex.Message}", ex);
                    }
                }
            }


            _logger.WriteLine($"Bulk insert completed. {count} rows loaded. {errorCount} errors skipped.");
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    private static bool IsTrue(string value) =>
        value.Trim() is var v
        && (v.Equals("ON", StringComparison.OrdinalIgnoreCase)
            || v.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || v.Equals("1", StringComparison.Ordinal)
            || v.Equals("YES", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pairs each target column with the source column it reads from.
    ///
    /// <para>Name pairing is used only when every target column is named by the header, so the
    /// choice is all-or-nothing: a partial match means the header describes a different table, and
    /// filling some columns by name and the rest by position would be worse than either.</para>
    /// </summary>
    private static List<(string Target, string Source)> ResolvePairing(
        IReadOnlyList<string> targets, IReadOnlyList<string> sourceColumns, bool preferNames, out bool pairedByName)
    {
        pairedByName = false;

        if (preferNames)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sourceColumns) byName.TryAdd(source, source);

            if (targets.All(t => byName.ContainsKey(t)))
            {
                pairedByName = true;
                return targets.Select(t => (t, byName[t])).ToList();
            }
        }

        var positional = new List<(string, string)>();
        for (int i = 0; i < targets.Count && i < sourceColumns.Count; i++)
            positional.Add((targets[i], sourceColumns[i]));
        return positional;
    }

    /// <summary>
    /// An absent value is NULL, not an empty string. This is T-SQL's <c>KEEPNULLS</c> promoted from
    /// opt-in to default: a blank field in an extract means "no value", and leaving it as <c>""</c>
    /// makes it invisible to <c>IS NULL</c> while still failing conversion to any numeric column.
    /// </summary>
    private static object? NullIfBlank(object? value) =>
        value is string s && string.IsNullOrWhiteSpace(s) ? null : value;

    private async Task<(int written, int errors)> WriteBisect(
        IReadOnlyList<Row> rows, IReadOnlyList<string> destColumns, IDataSource destination,
        System.Threading.CancellationToken cancellationToken,
        int batchIndex, string targetTable, int maxErrors, int currentErrors)
    {
        if (rows.Count == 0) return (0, 0);

        var batch = new DataTable();
        batch.SetColumns(destColumns.ToList());
        foreach (var row in rows) await batch.AddRowAsync(row);

        try
        {
            await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable(), append: true, cancellationToken: cancellationToken);
            return (rows.Count, 0);
        }
        catch (Exception ex) when (rows.Count == 1)
        {
            if (currentErrors + 1 > maxErrors)
                throw new ExecutionException(
                    $"Max errors ({maxErrors}) exceeded during bulk insert into '{targetTable}'. Last error: {ex.Message}", ex);
            _logger.Warning("Row in batch #{BatchIndex} failed (target '{Target}'): {Message}",
                batchIndex, targetTable, ex.Message);
            return (0, 1);
        }
        catch
        {
            var mid = rows.Count / 2;
            var (w1, e1) = await WriteBisect(
                rows.Take(mid).ToList(), destColumns, destination, cancellationToken,
                batchIndex, targetTable, maxErrors, currentErrors);
            var (w2, e2) = await WriteBisect(
                rows.Skip(mid).ToList(), destColumns, destination, cancellationToken,
                batchIndex, targetTable, maxErrors, currentErrors + e1);
            return (w1 + w2, e1 + e2);
        }
    }
}

