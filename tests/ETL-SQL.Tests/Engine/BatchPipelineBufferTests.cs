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
    }
}
