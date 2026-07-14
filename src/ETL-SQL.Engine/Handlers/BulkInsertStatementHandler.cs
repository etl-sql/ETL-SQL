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
        if (options.TryGetValue("FIRSTROW", out var fr) && int.TryParse(fr, out var firstRow))
        {
            ffOptions["START_AT"] = Math.Max(0, firstRow - 1).ToString();
        }

        // For BULK INSERT, we handle headers manually via positional mapping or FIRSTROW.
        // Setting HEADER to FALSE prevents FlatFileDataSource from skipping an extra row.
        ffOptions["HEADER"] = "FALSE";

        foreach (var opt in options)
        {
            if (!ffOptions.ContainsKey(opt.Key)) ffOptions[opt.Key] = opt.Value;
        }

        // 4. Create Source via ConnectorRegistry to avoid circular dependency
        var connector = _connectorRegistry.GetConnector("FLATFILE");
        if (connector == null)
            throw new ExecutionException("FLATFILE connector not found.");

        List<ColumnDefinition>? destColumnDefs = null;
        try
        {
            var destCols = await destination.GetColumnsAsync();
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
        var source = connector.CreateDataSource(context, resolvedPath, ffOptions, destColumnDefs);

        try
        {
            _logger.Debug("Bulk loading from {FilePath} into {TableName}", stmt.FilePath, stmt.TargetTable.TableName);

            int batchSize = context.EffectiveBatchSize;
            if (options.TryGetValue("BATCHSIZE", out var bs) && int.TryParse(bs, out var bsv))
                batchSize = bsv;

            int maxErrors = 0;
            if (options.TryGetValue("MAXERRORS", out var me) && int.TryParse(me, out var mev))
                maxErrors = mev;

            var batches = context.InterceptProgress(source.ReadBatches(batchSize));


            // Get destination columns for metadata validation
            var destColumns = (await destination.GetColumnsAsync()).ToList();

            // Determine mapping: use explicit columns if provided, otherwise positional mapping to destination columns
            var mapping = stmt.Columns ?? destColumns;

            int count = 0;
            int errorCount = 0;
            int batchIndex = 0;

            await foreach (var batch in batches)
            {
                batchIndex++;
                // Map columns by position from source to destination
                var mappedBatch = new DataTable();
                mappedBatch.SetColumns(destColumns);

                foreach (var sourceRow in batch.Rows)
                {
                    var mappedRow = new Row();
                    // Source data is always positional in the flat file.
                    // We map the i-th source column to the i-th entry in our 'mapping' list.
                    for (int i = 0; i < mapping.Count && i < batch.ColumnNames.Count; i++)
                    {
                        mappedRow[mapping[i]] = sourceRow[batch.ColumnNames[i]];
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

                        await destination.WriteBatches(new[] { mappedBatch }.ToAsyncEnumerable(), append: true);
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
                            mappedBatch.Rows.Take(mid).ToList(), destColumns, destination,
                            batchIndex, stmt.TargetTable.TableName, maxErrors, errorCount);
                        var (w2, e2) = await WriteBisect(
                            mappedBatch.Rows.Skip(mid).ToList(), destColumns, destination,
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

    private async Task<(int written, int errors)> WriteBisect(
        IReadOnlyList<Row> rows, IReadOnlyList<string> destColumns, IDataSource destination,
        int batchIndex, string targetTable, int maxErrors, int currentErrors)
    {
        if (rows.Count == 0) return (0, 0);

        var batch = new DataTable();
        batch.SetColumns(destColumns.ToList());
        foreach (var row in rows) await batch.AddRowAsync(row);

        try
        {
            await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable(), append: true);
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
                rows.Take(mid).ToList(), destColumns, destination,
                batchIndex, targetTable, maxErrors, currentErrors);
            var (w2, e2) = await WriteBisect(
                rows.Skip(mid).ToList(), destColumns, destination,
                batchIndex, targetTable, maxErrors, currentErrors + e1);
            return (w1 + w2, e1 + e2);
        }
    }
}

