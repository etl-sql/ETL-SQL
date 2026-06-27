using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

/// <summary>Hybrid projected-row DISTINCT with bounded hash-partition state.</summary>
internal sealed class ExternalDistinctEngine
{
    private readonly IExecutionContext _context;

    public ExternalDistinctEngine(IExecutionContext context) => _context = context;

    public async IAsyncEnumerable<Row> ApplyAsync(IAsyncEnumerable<Row> source)
    {
        var threshold = Math.Max(1, _context.JoinSpillThreshold);
        var prefix = new List<Row>(Math.Min(threshold, 4096));
        await using var enumerator = source.GetAsyncEnumerator();
        while (prefix.Count < threshold && await enumerator.MoveNextAsync())
            prefix.Add(enumerator.Current);

        if (prefix.Count < threshold)
        {
            var seen = new HashSet<CompoundKey>();
            foreach (var row in prefix)
                if (seen.Add(Key(row))) yield return row;
            yield break;
        }

        var partitionCount = Math.Max(1, _context.ExternalHashPartitions);
        var names = new string[partitionCount];
        var writers = new ISpillWriter[partitionCount];
        var operationId = Guid.NewGuid().ToString("N");
        try
        {
            for (var i = 0; i < partitionCount; i++)
            {
                names[i] = $"distinct_{operationId}_{i}.tmp";
                writers[i] = await _context.SpillStore.CreateWriterAsync(names[i]);
            }

            foreach (var row in prefix)
                await WritePartitioned(row, writers);
            while (await enumerator.MoveNextAsync())
                await WritePartitioned(enumerator.Current, writers);

            foreach (var writer in writers)
                await writer.DisposeAsync();
            Array.Clear(writers);

            _context.Telemetry.PartitionsCount += partitionCount;
            foreach (var name in names)
            {
                var seen = new HashSet<CompoundKey>();
                await using var reader = await _context.SpillStore.CreateReaderAsync(name);
                await foreach (var row in reader.AsEnumerableAsync())
                    if (seen.Add(Key(row))) yield return row;
            }
        }
        finally
        {
            foreach (var writer in writers)
                if (writer != null) await writer.DisposeAsync();
            foreach (var name in names)
                if (name != null) _context.SpillStore.DeleteChunk(name);
        }
    }

    private static CompoundKey Key(Row row)
    {
        var names = row.GetColumnNames().ToArray();
        var values = new object?[names.Length];
        for (var i = 0; i < values.Length; i++) values[i] = row[names[i]];
        return new CompoundKey(values);
    }

    private static async Task WritePartitioned(Row row, ISpillWriter[] writers)
    {
        var partition = (Key(row).GetHashCode() & 0x7fffffff) % writers.Length;
        await writers[partition].WriteRowAsync(row);
    }
}
