using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// High-scale aggregation engine that spills to disk (partitioned files) when data exceeds memory thresholds.
    /// </summary>
    public class ExternalAggregateEngine
    {
        private readonly IExecutionContext _context;
        private readonly AggregateEngine _inMemoryEngine;
        private readonly ILogger _logger;
        private readonly string _tempDir;
        private const int PARTITION_COUNT = 32;

        public ExternalAggregateEngine(IExecutionContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
            _inMemoryEngine = new AggregateEngine(context, logger);
            _tempDir = Path.Combine(Path.GetTempPath(), "ETL-SQL", "AggSpill", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        /// <summary>Applies aggregation by partitioning the stream into disk files and processing each partition sequentially.</summary>
        public async Task<List<Row>> ApplyAggregationExternal(IAsyncEnumerable<Row> inputStream, List<Expression>? groupBy, List<SelectColumn> finalColumns, List<string> colNames, Expression? havingClause = null)
        {
            try
            {
                // 1. Partition Phase
                var partitionPaths = await PartitionStream(inputStream, groupBy);

                var finalResults = new List<Row>();

                // 2. Aggregate Phase (one partition at a time)
                foreach (var path in partitionPaths)
                {
                    if (!File.Exists(path)) continue;

                    var rows = await ReadPartition(path);
                    if (rows.Count > 0)
                    {
                        var partResults = await _inMemoryEngine.ApplyAggregation(rows, groupBy, finalColumns, colNames, havingClause);
                        finalResults.AddRange(partResults);
                    }
                    File.Delete(path);
                }

                // Handle global aggregation if no rows were found but aggregates exist
                if (finalResults.Count == 0 && finalColumns.Any(c => _inMemoryEngine.IsAggregate(c.Expression)) && (groupBy == null || groupBy.Count == 0))
                {
                    return await _inMemoryEngine.ApplyAggregation(new List<Row>(), groupBy, finalColumns, colNames, havingClause);
                }

                return finalResults;
            }
            finally
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            }
        }

        private async Task<string[]> PartitionStream(IAsyncEnumerable<Row> stream, List<Expression>? groupBy)
        {
            var paths = new string[PARTITION_COUNT];
            var writers = new StreamWriter[PARTITION_COUNT];

            for (int i = 0; i < PARTITION_COUNT; i++)
            {
                paths[i] = Path.Combine(_tempDir, $"agg_{i}.tmp");
                writers[i] = new StreamWriter(paths[i]);
            }

            try
            {
                await foreach (var row in stream)
                {
                    int pIdx = 0;
                    if (groupBy != null && groupBy.Count > 0)
                    {
                        int hash = 17;
                        foreach (var g in groupBy)
                        {
                            var val = await _context.EvaluateValue(g, row);
                            hash = hash * 31 + (val?.GetHashCode() ?? 0);
                        }
                        pIdx = Math.Abs(hash % PARTITION_COUNT);
                    }

                    var json = System.Text.Json.JsonSerializer.Serialize(row.Columns);
                    var bytes = System.Text.Encoding.UTF8.GetByteCount(json) + 2; // + newline
                    _context.TotalSpilledBytes += bytes;
                    await writers[pIdx].WriteLineAsync(json);
                }
            }
            finally
            {
                int usedPartitions = 0;
                foreach (var w in writers) 
                { 
                    if (w.BaseStream.Length > 0) usedPartitions++;
                    w.Flush(); w.Close(); 
                }
                _context.PartitionsCount += usedPartitions;
            }

            return paths;
        }

        private async Task<List<Row>> ReadPartition(string path)
        {
            var rows = new List<Row>();
            using var reader = new StreamReader(path);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var cols = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(line);
                if (cols != null) 
                {
                    var row = new Row();
                    foreach (var kvp in cols) row[kvp.Key] = kvp.Value;
                    rows.Add(row);
                }
            }
            return rows;
        }
    }
}
