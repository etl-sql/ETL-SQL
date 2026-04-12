using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
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
                var val = await context.EvaluateValue(opt.Value, new Row());
                options[opt.Key] = val?.ToString() ?? "";
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

            var source = connector.CreateDataSource(stmt.FilePath, ffOptions);

            try
            {
                _logger.Debug("Bulk loading from {FilePath} into {TableName}", stmt.FilePath, stmt.TargetTable.TableName);
                
                int batchSize = 10000;
                if (options.TryGetValue("BATCHSIZE", out var bs) && int.TryParse(bs, out var bsv))
                    batchSize = bsv;

                int maxErrors = 0;
                if (options.TryGetValue("MAXERRORS", out var me) && int.TryParse(me, out var mev))
                    maxErrors = mev;

                var batches = source.ReadBatches(batchSize);
                
                batches = context.InterceptProgress(batches);

                // Get destination columns for metadata validation
                var destColumns = (await destination.GetColumnsAsync()).ToList();
                
                // Determine mapping: use explicit columns if provided, otherwise positional mapping to destination columns
                var mapping = stmt.Columns ?? destColumns;

                int count = 0;
                int errorCount = 0;

                await foreach (var batch in batches)
                {
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
                            
                            await destination.WriteBatches(new[] { mappedBatch }.ToAsyncEnumerable());
                        }
                        count += mappedBatch.Rows.Count;
                    }
                    catch (Exception ex)
                    {
                        if (context.IsWhatIf) throw; // Should not happen in dry run really, but keep consistency
                        if (maxErrors > 0 || errorCount < maxErrors)
                        {
                            _logger.WriteLine($"[WARNING] Batch write failed: {ex.Message}. Retrying row-by-row up to MAXERRORS={maxErrors}.");
                            
                            // Fallback: Try writing each row individually
                            foreach (var row in mappedBatch.Rows)
                            {
                                try
                                {
                                    var singleRowBatch = new DataTable();
                                    singleRowBatch.SetColumns(destColumns);
                                    await singleRowBatch.AddRowAsync(row);
                                    await destination.WriteBatches(new[] { singleRowBatch }.ToAsyncEnumerable());
                                    count++;
                                }
                                catch (Exception rowEx)
                                {
                                    errorCount++;
                                    _logger.WriteLine($"[ERROR] Row failed: {rowEx.Message}");
                                    if (errorCount > maxErrors)
                                    {
                                        throw new ExecutionException($"Max errors ({maxErrors}) exceeded during bulk insert. Last error: {rowEx.Message}", rowEx);
                                    }
                                }
                            }
                        }
                        else
                        {
                            throw new ExecutionException($"Bulk insert failed and MAXERRORS is 0. Error: {ex.Message}", ex);
                        }
                    }
                }
                
                context.RowsProcessed += count;
                _logger.WriteLine($"Bulk insert completed. {count} rows loaded. {errorCount} errors skipped.");
            }
            finally
            {
                await source.DisposeAsync();
            }
        }
    }
}
