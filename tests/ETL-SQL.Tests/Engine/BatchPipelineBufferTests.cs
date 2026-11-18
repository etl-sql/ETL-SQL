using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class BatchPipelineBufferTests
    {
        [Fact]
        public async Task TestBufferPipelining()
        {
            var helper = new BatchPipelineHelper();

            async IAsyncEnumerable<DataTable> SampleSource()
            {
                for (int i = 0; i < 3; i++)
                {
                    await Task.Delay(10);
                    var dt = new DataTable();
                    dt.SetColumns(new[] { "ID" });
                    var row = dt.NewRow();
                    row[0] = i;
                    await dt.AddRowAsync(row);
                    yield return dt;
                }
            }

            var buffered = helper.Buffer(SampleSource());

            int count = 0;
            await foreach (var item in buffered)
            {
                Assert.Single(item.Rows);
                Assert.Equal(count, item.Rows[0][0]);
                count++;
            }

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task BufferSplitsOversizedBatchByTargetBytes()
        {
            var helper = new BatchPipelineHelper();
            const int rowCount = 10;
            const long targetBatchBytes = 8_192;

            var input = new DataTable();
            input.SetColumns(["Id", "Payload"]);
            for (int i = 0; i < rowCount; i++)
            {
                var r = input.NewRow();
                r["Id"] = i;
                r["Payload"] = new string((char)('A' + (i % 26)), 2_000);
                await input.AddRowAsync(r);
            }

            async IAsyncEnumerable<DataTable> SingleLargeBatch()
            {
                yield return input;
                await Task.CompletedTask;
            }

            var outputBatches = new List<DataTable>();
            await foreach (var b in helper.Buffer(SingleLargeBatch(), maxQueuedBytes: 16_384, targetBatchBytes: targetBatchBytes))
                outputBatches.Add(b);

            Assert.True(outputBatches.Count > 1, $"Expected oversized batch split into > 1 sub-batch, got {outputBatches.Count}.");
            var seenIds = new List<int>();
            foreach (var b in outputBatches)
            {
                long batchBytes = b.Rows.Sum(r => r.EstimateHeapBytes());
                if (b.Rows.Count > 1)
                    Assert.True(batchBytes <= targetBatchBytes, $"Sub-batch bytes {batchBytes} exceeded {targetBatchBytes}.");
                foreach (var r in b.Rows)
                    seenIds.Add(Convert.ToInt32(r["Id"]));
            }
            Assert.Equal(Enumerable.Range(0, rowCount), seenIds);
        }

        [Fact]
        public async Task BufferEnforcesByteBudgetBackpressureOnFastWideProducer()
        {
            var helper = new BatchPipelineHelper();
            const int totalBatches = 8;
            const long maxQueuedBytes = 6_000;
            const long targetBatchBytes = 4_000;

            int produced = 0;
            var thirdGenerated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async IAsyncEnumerable<DataTable> WideProducer()
            {
                for (int i = 0; i < totalBatches; i++)
                {
                    var dt = new DataTable();
                    dt.SetColumns(["Id", "Payload"]);
                    var r = dt.NewRow();
                    r["Id"] = i;
                    r["Payload"] = new string((char)('M' + (i % 10)), 2_000); // ~4,100 estimated heap bytes per batch
                    await dt.AddRowAsync(r);
                    Interlocked.Increment(ref produced);
                    if (i == 2) thirdGenerated.TrySetResult();
                    yield return dt;
                }
            }

            var buffered = helper.Buffer(WideProducer(), maxQueuedBytes: maxQueuedBytes, targetBatchBytes: targetBatchBytes);
            await using var enumerator = buffered.GetAsyncEnumerator();

            // Consume batch 0, then pause; byte backpressure stops producer after 1 queued batch + 1 waiting item.
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(0, enumerator.Current.Rows[0]["Id"]);

            await thirdGenerated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(80); // flaky-delay-ok: negative check; a correct producer never passes 3, the delay only gives a broken one time to overrun
            Assert.True(Volatile.Read(ref produced) <= 3,
                $"Producer should block under byte backpressure before queuing 4+ batches, but produced {Volatile.Read(ref produced)} batches.");

            var remainingIds = new List<int>();
            while (await enumerator.MoveNextAsync())
                remainingIds.Add(Convert.ToInt32(enumerator.Current.Rows[0]["Id"]));

            Assert.Equal(Enumerable.Range(1, totalBatches - 1), remainingIds);
            Assert.Equal(totalBatches, Volatile.Read(ref produced));
        }
    }
}
