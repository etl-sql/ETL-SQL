using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Spill;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class SpillStoreResilienceTests
    {
        private static Evaluator NewEvaluator()
        {
            return DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        }

        private async Task RunSpillStoreFeatureMatrix(bool encrypt, bool compress)
        {
            var e = NewEvaluator();
            e.SpillEncryptionEnabled = encrypt;
            e.SpillCompressionEnabled = compress;

            using var store = new SpillStore(e);

            var schema = new TableSchema(new[] { "Id", "Val" });
            var writer = await store.CreateWriterAsync("test_matrix");

            // Write 10k rows
            for (int i = 0; i < 10000; i++)
            {
                var r = new Row(schema);
                r["Id"] = i;
                r["Val"] = "Data_" + i;
                await writer.WriteRowAsync(r);
            }
            await writer.DisposeAsync();

            var reader = await store.CreateReaderAsync("test_matrix");

            int count = 0;
            await foreach (var row in reader.AsEnumerableAsync())
            {
                Assert.Equal(count, Convert.ToInt32(row["Id"]));
                Assert.Equal("Data_" + count, row["Val"]?.ToString());
                count++;
            }
            await reader.DisposeAsync();

            Assert.Equal(10000, count);
        }

        [Fact]
        public async Task SpillStore_FeatureMatrix_EncryptOnly()
        {
            await RunSpillStoreFeatureMatrix(encrypt: true, compress: false);
        }

        [Fact]
        public async Task SpillStore_FeatureMatrix_CompressOnly()
        {
            await RunSpillStoreFeatureMatrix(encrypt: false, compress: true);
        }

        [Fact]
        public async Task SpillStore_FeatureMatrix_EncryptAndCompress()
        {
            await RunSpillStoreFeatureMatrix(encrypt: true, compress: true);
        }

        [Fact]
        public async Task SpillStore_FeatureMatrix_Neither()
        {
            await RunSpillStoreFeatureMatrix(encrypt: false, compress: false);
        }

        [Fact]
        public async Task SpillStore_DataIntegrity_RoundTrip()
        {
            var e = NewEvaluator();
            e.SpillEncryptionEnabled = true;
            e.SpillCompressionEnabled = true;

            using var store = new SpillStore(e);

            var schema = new TableSchema(new[] { "IntCol", "DecCol", "StrCol", "DateCol", "NullCol" });
            var writer = await store.CreateWriterAsync("integ_test");

            var baseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            for (int i = 0; i < 100000; i++) // 100k row scale test
            {
                var r = new Row(schema);
                r["IntCol"] = i;
                r["DecCol"] = i + 0.5m;
                r["StrCol"] = "Str_" + i;
                r["DateCol"] = baseDate.AddMinutes(i);
                r["NullCol"] = null;

                await writer.WriteRowAsync(r);
            }
            await writer.DisposeAsync();

            var reader = await store.CreateReaderAsync("integ_test");

            int count = 0;
            await foreach (var row in reader.AsEnumerableAsync())
            {
                Assert.Equal(count, Convert.ToInt32(row["IntCol"]));
                Assert.Equal(count + 0.5m, Convert.ToDecimal(row["DecCol"]));
                Assert.Equal("Str_" + count, row["StrCol"]?.ToString());
                Assert.Equal(baseDate.AddMinutes(count), Convert.ToDateTime(row["DateCol"]));
                Assert.Null(row["NullCol"]);
                count++;
            }
            await reader.DisposeAsync();

            Assert.Equal(100000, count);
        }

        [Fact]
        public async Task ArrowLogicalSchema_PreservesNumericAndDateLookingStrings()
        {
            var e = NewEvaluator();
            e.SpillEncryptionEnabled = false;
            e.SpillCompressionEnabled = false;
            using var store = new SpillStore(e);
            await using (var writer = await store.CreateWriterAsync("logical_strings"))
            {
                await writer.WriteRowAsync(new Row
                {
                    ["NumericText"] = "00123",
                    ["DecimalText"] = "12.50",
                    ["DateText"] = "2026-07-01T12:34:56Z"
                });
            }

            await using var reader = await store.CreateReaderAsync("logical_strings");
            var row = await reader.ReadRowAsync();

            Assert.NotNull(row);
            Assert.IsType<string>(row!["NumericText"]);
            Assert.Equal("00123", row["NumericText"]);
            Assert.IsType<string>(row["DecimalText"]);
            Assert.Equal("12.50", row["DecimalText"]);
            Assert.IsType<string>(row["DateText"]);
            Assert.Equal("2026-07-01T12:34:56Z", row["DateText"]);
        }

        [Fact]
        public async Task ArrowWriter_PreservesStableDynamicColumnsOnSchemaBackedRows()
        {
            var e = NewEvaluator();
            e.SpillEncryptionEnabled = false;
            e.SpillCompressionEnabled = false;
            using var store = new SpillStore(e);
            var schema = new TableSchema(new[] { "Id", "Value" });

            await using (var writer = await store.CreateWriterAsync("dynamic_markers"))
            {
                for (int i = 0; i < 100; i++)
                {
                    var row = new Row(schema);
                    row["Id"] = i;
                    row["Value"] = $"value-{i}";
                    row["__PARTITION"] = i % 4;
                    await writer.WriteRowAsync(row);
                }
            }

            await using var reader = await store.CreateReaderAsync("dynamic_markers");
            int expected = 0;
            await foreach (var row in reader.AsEnumerableAsync())
            {
                Assert.Equal(expected, Convert.ToInt32(row["Id"]));
                Assert.Equal($"value-{expected}", row["Value"]);
                Assert.Equal(expected % 4, Convert.ToInt32(row["__PARTITION"]));
                expected++;
            }
            Assert.Equal(100, expected);
        }

        [Fact]
        public async Task SpillLatencyMetrics_AreCollectedOnlyForAdaptiveExecution()
        {
            var e = NewEvaluator();
            e.SpillEncryptionEnabled = false;
            e.SpillCompressionEnabled = false;
            using var store = new SpillStore(e);

            await using (var writer = await store.CreateWriterAsync("static_metrics"))
                await writer.WriteRowAsync(new Row { ["Id"] = 1 });
            Assert.Equal(0, e.AdaptiveMetrics.SpillWriteLatencyMsPerMB);

            e.AdaptiveExecutionEnabled = true;
            await using (var writer = await store.CreateWriterAsync("adaptive_metrics"))
                await writer.WriteRowAsync(new Row { ["Id"] = 2 });
            Assert.True(e.AdaptiveMetrics.SpillWriteLatencyMsPerMB > 0);
        }

        [Fact]
        public async Task ArrowRowSpillPreservesDateTimeOffset()
        {
            var e = NewEvaluator();
            e.SpillEncryptionEnabled = false;
            e.SpillCompressionEnabled = false;
            using var store = new SpillStore(e);
            var expected = new DateTimeOffset(2026, 7, 2, 10, 5, 51, TimeSpan.FromHours(-5));
            await using (var writer = await store.CreateWriterAsync("offset_row"))
                await writer.WriteRowAsync(new Row { ["OccurredAt"] = expected });

            await using var reader = await store.CreateReaderAsync("offset_row");
            var row = await reader.ReadRowAsync();

            Assert.Equal(expected, Assert.IsType<DateTimeOffset>(row!["OccurredAt"]));
            Assert.Equal(expected.Offset, ((DateTimeOffset)row["OccurredAt"]!).Offset);
        }

        [Fact]
        public async Task ArrowReader_ExposesTypedColumnBatches()
        {
            var e = NewEvaluator();
            e.SpillEncryptionEnabled = false;
            e.SpillCompressionEnabled = false;
            using var store = new SpillStore(e);
            var timestamp = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
            await using (var writer = await store.CreateWriterAsync("column_batch"))
            {
                await writer.WriteRowAsync(new Row
                {
                    ["Id"] = 7,
                    ["Amount"] = 12.5m,
                    ["Enabled"] = true,
                    ["At"] = timestamp,
                    ["Label"] = "00123"
                });
                await writer.WriteRowAsync(new Row
                {
                    ["Id"] = 8,
                    ["Amount"] = null,
                    ["Enabled"] = false,
                    ["At"] = timestamp.AddMinutes(1),
                    ["Label"] = null
                });
            }

            await using var reader = await store.CreateReaderAsync("column_batch");
            var columnar = Assert.IsAssignableFrom<IColumnarSpillReader>(reader);
            var batches = new List<ETL_SQL.Core.Data.ColumnBatch>();
            await foreach (var batch in columnar.AsColumnBatchesAsync()) batches.Add(batch);

            var result = Assert.Single(batches);
            try
            {
                Assert.Equal(2, result.RowCount);
                Assert.Equal("Integer", result.Schema.Fields[result.Schema.GetOrdinal("Id")].LogicalType);
                Assert.Equal(7L, result.GetColumn<long>("Id").Values.Span[0]);
                Assert.Equal(8L, result.GetColumn<long>("Id").Values.Span[1]);
                Assert.Equal(12.5m, result.GetColumn<decimal>("Amount").Values.Span[0]);
                Assert.True(result.GetColumn<decimal>("Amount").IsNull(1));
                Assert.True(result.GetColumn<bool>("Enabled").Values.Span[0]);
                Assert.Equal(timestamp, result.GetColumn<DateTime>("At").Values.Span[0]);
                Assert.Equal("00123", result.GetUtf8Column("Label").GetBoxedValue(0));
                Assert.True(result.GetUtf8Column("Label").IsNull(1));
            }
            finally
            {
                foreach (var batch in batches) batch.Dispose();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadRowAsync());
        }

        [Fact]
        public async Task ArrowWriter_AcceptsTypedColumnBatches()
        {
            var e = NewEvaluator();
            e.SpillEncryptionEnabled = false;
            e.SpillCompressionEnabled = false;
            using var store = new SpillStore(e);
            var amount = ETL_SQL.Core.Data.ColumnBuffer<decimal>.Rent(2);
            amount.Values.Span[0] = 4.25m;
            amount.SetNull(1);
            var instant = new DateTimeOffset(2026, 7, 2, 13, 14, 15, TimeSpan.FromHours(5.5));
            using var batch = new ETL_SQL.Core.Data.ColumnBatch(
                new ETL_SQL.Core.Data.ColumnBatchSchema(new[]
                {
                    new ETL_SQL.Core.Data.ColumnBatchField("Id", typeof(long), "Integer"),
                    new ETL_SQL.Core.Data.ColumnBatchField("Amount", typeof(decimal), "Decimal"),
                    new ETL_SQL.Core.Data.ColumnBatchField("Flag", typeof(bool), "Boolean"),
                    new ETL_SQL.Core.Data.ColumnBatchField("Label", typeof(string), "String"),
                    new ETL_SQL.Core.Data.ColumnBatchField("OccurredAt", typeof(DateTimeOffset), "DATETIMEOFFSET")
                }),
                new ETL_SQL.Core.Data.IColumnBuffer[]
                {
                    new ETL_SQL.Core.Data.ColumnBuffer<long>(new long[] { 10, 11 }, 2),
                    amount,
                    new ETL_SQL.Core.Data.ColumnBuffer<bool>(new bool[] { true, false }, 2),
                    ETL_SQL.Core.Data.Utf8ColumnBuffer.FromStrings(new string?[] { "001", null }),
                    new ETL_SQL.Core.Data.ColumnBuffer<DateTimeOffset>(new[] { instant, instant.AddHours(1) }, 2)
                },
                2);
            await using (var writer = await store.CreateWriterAsync("native_write"))
            {
                var columnarWriter = Assert.IsAssignableFrom<IColumnarSpillWriter>(writer);
                await columnarWriter.WriteBatchAsync(batch);
            }

            await using var reader = await store.CreateReaderAsync("native_write");
            var columnarReader = Assert.IsAssignableFrom<IColumnarSpillReader>(reader);
            var readBatches = new List<ETL_SQL.Core.Data.ColumnBatch>();
            await foreach (var readBatch in columnarReader.AsColumnBatchesAsync()) readBatches.Add(readBatch);
            var result = Assert.Single(readBatches);
            try
            {
                Assert.Equal(10L, result.GetColumn<long>("Id").Values.Span[0]);
                Assert.Equal(4.25m, result.GetColumn<decimal>("Amount").Values.Span[0]);
                Assert.True(result.GetColumn<decimal>("Amount").IsNull(1));
                Assert.True(result.GetColumn<bool>("Flag").Values.Span[0]);
                Assert.Equal("001", result.GetUtf8Column("Label").GetBoxedValue(0));
                Assert.True(result.GetUtf8Column("Label").IsNull(1));
                Assert.Equal(instant, result.GetColumn<DateTimeOffset>("OccurredAt").Values.Span[0]);
                Assert.Equal(instant.Offset, result.GetColumn<DateTimeOffset>("OccurredAt").Values.Span[0].Offset);
            }
            finally
            {
                foreach (var readBatch in readBatches) readBatch.Dispose();
            }
        }
    }
}
