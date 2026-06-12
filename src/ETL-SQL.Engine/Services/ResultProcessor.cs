using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services
{
    public class ResultProcessor(ILogger logger)
    {
        private readonly ILogger _logger = logger;

        public async Task<DataTable> ProcessStream(IAsyncEnumerable<DataTable> batches, IExecutionContext context, bool isForClause = false)
        {
            var result = new DataTable();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool isFirst = true;
            long totalRows = 0;
            bool capped = false;

            await foreach (var batch in batches)
            {
                if (result.ColumnNames.Count == 0) result.SetColumns(batch.ColumnNames);

                bool shouldStop = false;
                foreach (var r in batch.Rows)
                {
                    if (result.Rows.Count >= context.MaxLastResultRows)
                    {
                        if (!capped)
                        {
                            capped = true;
                            result.IsCapped = true;
                            _logger.Debug("[SELECT] Result buffer reached {MaxLastResultRows} rows. Stopping consumption to prevent memory exhaustion.", context.MaxLastResultRows);
                        }

                        if (!context.RedirectOutput)
                        {
                            shouldStop = true;
                            break;
                        }
                    }

                    totalRows++;
                    if (result.Rows.Count < context.MaxLastResultRows)
                    {
                        await result.AddRowAsync(r);
                    }
                }

                if (!context.RedirectOutput)
                {
                    if (isForClause)
                    {
                        foreach (var r in batch.Rows) _logger.WriteLine(r[0]?.ToString() ?? "");
                    }
                    else
                    {
                        ResultFormatter.PrintBatch(batch, isFirst);
                        isFirst = false;
                    }
                }

                if (shouldStop) break;
            }

            sw.Stop();
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;
            result.TotalRowsMatched = (int)Math.Min(totalRows, int.MaxValue);
            context.Telemetry.RowsProcessed += totalRows;

            context.LastResult = result;
            context.LastResultSets.Add(result);
            context.OnResultSet?.Invoke(result);

            return result;
        }
    }
}

