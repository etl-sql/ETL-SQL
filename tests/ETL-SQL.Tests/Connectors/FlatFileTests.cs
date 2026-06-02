using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Connectors.FlatFile;
using Spectre.Console;
using ETL_SQL.Common;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "FLATFILE")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class FlatFileTests
    {
        [Fact]
        public async Task TestBasicCsvRead()
        {
            string csvFile = "FileTest_Basic.csv";
            await File.WriteAllTextAsync(csvFile, "id,name\n1,Alpha\n2,Beta");

            try
            {
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Single(batches);
                Assert.Equal(2, batches[0].Rows.Count);
                Assert.Equal("Alpha", batches[0].Rows[0]["name"]?.ToString());
            }
            finally { if (File.Exists(csvFile)) File.Delete(csvFile); }
        }

        [Fact]
        public async Task TestCsvCustomDelimiter()
        {
            string csvFile = "FileTest_Pipe.csv";
            await File.WriteAllTextAsync(csvFile, "id|name\n1|Alpha\n2|Beta");

            try
            {
                var options = new Dictionary<string, string> { { "DELIMITER", "PIPE" } };
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, options);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Equal("Alpha", batches[0].Rows[0]["name"]?.ToString());
            }
            finally { if (File.Exists(csvFile)) File.Delete(csvFile); }
        }

        [Fact]
        public async Task TestCsvQuotedValues()
        {
            string csvFile = "FileTest_Quote.csv";
            await File.WriteAllTextAsync(csvFile, "id,name\n1,\"Alpha, One\"\n2,Beta");

            try
            {
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Equal("Alpha, One", batches[0].Rows[0]["name"]?.ToString());
            }
            finally { if (File.Exists(csvFile)) File.Delete(csvFile); }
        }

        [Fact]
        public async Task TestCsvSkipRows()
        {
            string csvFile = "FileTest_Skip.csv";
            await File.WriteAllTextAsync(csvFile, "JUNK\nMORE JUNK\nid,name\n1,Alpha");

            try
            {
                var options = new Dictionary<string, string> { { "START_AT", "2" } };
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, options);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Single(batches[0].Rows);
                Assert.Equal("Alpha", batches[0].Rows[0]["name"]?.ToString());
            }
            finally { if (File.Exists(csvFile)) File.Delete(csvFile); }
        }

        [Fact]
        public async Task TestCsvCountAtEnd()
        {
            string csvFile = "FileTest_Count.csv";
            await File.WriteAllTextAsync(csvFile, "id,name\n1,Alpha\n2,Beta\nTotal Rows: 2");

            try
            {
                var options = new Dictionary<string, string> { { "COUNT_AT_END", "Total Rows: COUNT" } };
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, options);
                var batches = await ds.ReadBatches().ToListAsync();

                int rowCount = batches.Sum(b => b.Rows.Count);
                Assert.Equal(2, rowCount);

                foreach(var batch in batches)
                {
                    Assert.DoesNotContain(batch.Rows, r => r.Columns.Values.Any(v => v?.ToString()?.Contains("Total Rows") == true));
                }
            }
            finally { if (File.Exists(csvFile)) File.Delete(csvFile); }
        }

        [Theory]
        [InlineData("LF", "LF", "id|name\n1|Alpha\n2|Beta")]
        [InlineData("CR", "CR", "id|name\r1|Alpha\r2|Beta")]
        [InlineData("CRLF", "CRLF", "id|name\r\n1|Alpha\r\n2|Beta")]
        [InlineData("TILDE", "TILDE", "id|name~1|Alpha~2|Beta")]
        [InlineData("SEMICOLON", "SEMICOLON", "id|name;1|Alpha;2|Beta")]
        [InlineData("COLON", "COLON", "id|name:1|Alpha:2|Beta")]
        [InlineData("COMMA", "COMMA", "id|name,1|Alpha,2|Beta")]
        [InlineData("TAB", "TAB", "id|name\t1|Alpha\t2|Beta")]
        [InlineData("PIPE", "PIPE", "id,name|1,Alpha|2,Beta")]
        public async Task TestCsvRowDelimiter(string name, string delim, string content)
        {
            string csvFile = $"FileTest_RowDelim_{name}.csv";
            await File.WriteAllTextAsync(csvFile, content);
            try
            {
                var options = new Dictionary<string, string> 
                { 
                    { "DELIMITER", name == "PIPE" ? "COMMA" : "PIPE" },
                    { "ROW_DELIMITER", delim }
                };
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, options);
                var batches = await ds.ReadBatches().ToListAsync();
                Assert.NotEmpty(batches);
                Assert.Equal(2, batches[0].Rows.Count);
            }
            finally { if (File.Exists(csvFile)) File.Delete(csvFile); }
        }

        [Fact]
        public async Task TestCsvTextQualifier()
        {
            string csvFile = "FileTest_Qualifier.csv";
            
            try
            {
                // Test Single Quote
                await File.WriteAllTextAsync(csvFile, "id,name\n1,'Alpha, One'\n2,Beta");
                var ds1 = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, new Dictionary<string, string> { { "TEXT_QUALIFIER", "SINGLEQUOTE" } });
                var b1 = await ds1.ReadBatches().ToListAsync();
                Assert.Equal("Alpha, One", b1[0].Rows[0]["name"]?.ToString());

                // Test Double Quote (explicit)
                await File.WriteAllTextAsync(csvFile, "id,name\n1,\"Alpha, One\"\n2,Beta");
                var ds2 = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, new Dictionary<string, string> { { "TEXT_QUALIFIER", "DOUBLEQUOTE" } });
                var b2 = await ds2.ReadBatches().ToListAsync();
                Assert.Equal("Alpha, One", b2[0].Rows[0]["name"]?.ToString());
            }
            finally { if (File.Exists(csvFile)) File.Delete(csvFile); }
        }

        [Fact]
        public async Task TestCsvExpandedDelimiters()
        {
            string csvFile = "FileTest_Expanded.csv";

            try
            {
                // Test semicolon delimiter
                await File.WriteAllTextAsync(csvFile, "id;name\n1;Alpha\n2;Beta");
                var options1 = new Dictionary<string, string> { { "DELIMITER", "SEMICOLON" } };
                var ds1 = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, options1);
                var b1 = await ds1.ReadBatches().ToListAsync();
                Assert.Equal("Alpha", b1[0].Rows[0]["name"]?.ToString());

                // Test colon delimiter
                await File.WriteAllTextAsync(csvFile, "id:name\n1:Alpha\n2:Beta");
                var options2 = new Dictionary<string, string> { { "DELIMITER", "COLON" } };
                var ds2 = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, options2);
                var b2 = await ds2.ReadBatches().ToListAsync();
                Assert.Equal("Alpha", b2[0].Rows[0]["name"]?.ToString());

                // Test tilde delimiter
                await File.WriteAllTextAsync(csvFile, "id~name\n1~Alpha\n2~Beta");
                var options3 = new Dictionary<string, string> { { "DELIMITER", "TILDE" } };
                var ds3 = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, options3);
                var b3 = await ds3.ReadBatches().ToListAsync();
                Assert.Equal("Alpha", b3[0].Rows[0]["name"]?.ToString());

                // Test tab delimiter
                await File.WriteAllTextAsync(csvFile, "id\tname\n1\tAlpha\n2\tBeta");
                var options4 = new Dictionary<string, string> { { "DELIMITER", "TAB" } };
                var ds4 = new FlatFileDataSource(SystemExecutionContext.Instance, csvFile, options4);
                var b4 = await ds4.ReadBatches().ToListAsync();
                Assert.Equal("Alpha", b4[0].Rows[0]["name"]?.ToString());
            }
            finally { if (File.Exists(csvFile)) File.Delete(csvFile); }
        }
    }
}
