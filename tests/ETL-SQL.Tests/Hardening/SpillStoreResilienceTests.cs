using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Spill;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.App;
using ETL_SQL.Common;

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
    }
}
