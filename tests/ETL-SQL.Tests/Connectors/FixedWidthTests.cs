using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "FLATFILE")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class FixedWidthTests
    {
        [Fact]
        public async Task TestFixedWidthWithVarcharLengths()
        {
            string fwFile = "FWTest_Varchar.txt";
            // Widths: ID=5, Name=10, Active=1
            await File.WriteAllTextAsync(fwFile, "00001Chuck     Y\n00002Bob       N");

            try
            {
                var schema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("ID", "VARCHAR(5)", false),
                    new ColumnDefinition("Name", "VARCHAR(10)", false),
                    new ColumnDefinition("Active", "CHAR(1)", false)
                };

                var options = new Dictionary<string, string>
                {
                    { "FORMAT", "FIXED" },
                    { "HEADER", "OFF" }
                };

                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options, schema);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Single(batches);
                Assert.Equal(2, batches[0].Rows.Count);

                var row1 = batches[0].Rows[0];
                Assert.Equal("00001", row1["ID"]?.ToString());
                Assert.Equal("Chuck", row1["Name"]?.ToString());
                Assert.Equal("Y", row1["Active"]?.ToString());

                var row2 = batches[0].Rows[1];
                Assert.Equal("Bob", row2["Name"]?.ToString());
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

        [Fact]
        public async Task TestFixedWidthWithMetadataTags()
        {
            string fwFile = "FWTest_Metadata.txt";
            // Widths: ID=3, Value=5
            await File.WriteAllTextAsync(fwFile, "001ABC  \n002XYZ  ");

            try
            {
                var schema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("ID", "INT", false, null, new Dictionary<string, string> { { "width", "3" } }),
                    new ColumnDefinition("Value", "VARCHAR", false, null, new Dictionary<string, string> { { "width", "5" } })
                };

                var options = new Dictionary<string, string>
                {
                    { "FORMAT", "FIXED" },
                    { "HEADER", "OFF" }
                };

                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options, schema);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Equal("001", batches[0].Rows[0]["ID"]?.ToString());
                Assert.Equal("ABC", batches[0].Rows[0]["Value"]?.ToString());
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

        [Fact]
        public async Task TestFixedWidthWithTrimOff()
        {
            string fwFile = "FWTest_TrimOff.txt";
            await File.WriteAllTextAsync(fwFile, "Chuck     ");

            try
            {
                var schema = new List<ColumnDefinition> { new ColumnDefinition("Name", "VARCHAR(10)", false) };
                var options = new Dictionary<string, string>
                {
                    { "FORMAT", "FIXED" },
                    { "HEADER", "OFF" },
                    { "TRIM", "OFF" }
                };

                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options, schema);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Equal("Chuck     ", batches[0].Rows[0]["Name"]?.ToString());
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

        [Fact]
        public async Task TestFixedWidthWithHeaderAndSkip()
        {
            string fwFile = "FWTest_HeaderSkip.txt";
            // Skip 1, Header 1, Data 1
            await File.WriteAllTextAsync(fwFile, "GARBAGE LINE\nID   NAME \n001  Chuck");

            try
            {
                var schema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("ID", "VARCHAR(5)", false),
                    new ColumnDefinition("NAME", "VARCHAR(6)", false)
                };

                var options = new Dictionary<string, string>
                {
                    { "FORMAT", "FIXED" },
                    { "START_AT", "1" },
                    { "HEADER", "ON" }
                };

                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options, schema);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Single(batches[0].Rows);
                Assert.Equal("001", batches[0].Rows[0]["ID"]?.ToString());
                Assert.Equal("Chuck", batches[0].Rows[0]["NAME"]?.ToString());
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

        [Fact]
        public async Task TestFixedWidthMissingTemplateThrows()
        {
            string fwFile = "FWTest_Fail.txt";
            await File.WriteAllTextAsync(fwFile, "test");
            try
            {
                var options = new Dictionary<string, string> { { "FORMAT", "FIXED" } };
                Assert.Throws<ExecutionException>(() => new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options));
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }
    }
}
