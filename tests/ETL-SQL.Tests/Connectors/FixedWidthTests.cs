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
        public async Task TestFixedWidthWithIntegerPrecision()
        {
            string fwFile = "FWTest_IntPrecision.txt";
            // ID: INT(5) => width = 6 (5 digits + 1 sign). Val: VARCHAR(10) => width = 10. Total line length = 16.
            await File.WriteAllTextAsync(fwFile, "-12345Chuck     \n 12345Bob       ");

            try
            {
                var schema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("ID", "INT(5)", false),
                    new ColumnDefinition("Val", "VARCHAR(10)", false)
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
                Assert.Equal("-12345", row1["ID"]?.ToString());
                Assert.Equal("Chuck", row1["Val"]?.ToString());

                var row2 = batches[0].Rows[1];
                Assert.Equal("12345", row2["ID"]?.ToString());
                Assert.Equal("Bob", row2["Val"]?.ToString());

                // Test write/export
                string outFile = "FWTest_IntPrecision_Out.txt";
                try
                {
                    var outDs = new FlatFileDataSource(SystemExecutionContext.Instance, outFile, options, schema);
                    await outDs.WriteBatches(batches.ToAsyncEnumerable());

                    var lines = await File.ReadAllLinesAsync(outFile);
                    Assert.Equal(2, lines.Length);
                    Assert.Equal("-12345Chuck     ", lines[0]);
                    Assert.Equal("12345 Bob       ", lines[1]);
                }
                finally { if (File.Exists(outFile)) File.Delete(outFile); }
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

        [Fact]
        public async Task TestFixedWidthIntegerOverflow()
        {
            var schema = new List<ColumnDefinition>
            {
                new ColumnDefinition("ID", "INT(5)", false)
            };

            var options = new Dictionary<string, string>
            {
                { "FORMAT", "FIXED" },
                { "HEADER", "OFF" }
            };

            var batch = new DataTable();
            batch.SetColumns(new[] { "ID" });

            // 6 digits is overflow for INT(5) (ignoring negative sign)
            var row = batch.NewRow();
            row["ID"] = "-123456";
            await batch.AddRowAsync(row);

            string outFile = "FWTest_IntOverflow_Out.txt";
            try
            {
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, outFile, options, schema);
                var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(new[] { batch }.ToAsyncEnumerable()));
                Assert.Contains("exceeds the declared INT(5) field width", ex.Message);
            }
            finally { if (File.Exists(outFile)) File.Delete(outFile); }
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

        // ── INT(N,+) / INT(N,-) sign constraints ──────────────────────────────

        [Theory]
        [InlineData("INT(5,+)")]
        [InlineData("INT(5,-)")]
        [InlineData("INT(5)")]
        [InlineData("DECIMAL(10,2)")]   // a scale still parses; sign and scale share the slot
        public void ParserAcceptsSignConstraintInColumnType(string declaredType)
        {
            var sql = $"CREATE TABLE #Layout ( Value {declaredType} );";
            var script = new ETL_SQL.Core.Parser.Parser(
                new ETL_SQL.Core.Parser.Lexer(sql).Tokenize(), sql).Parse();

            var create = Assert.IsType<CreateTableStatement>(
                script.Statements.Single(s => s is not NoOpStatement));
            Assert.Equal(declaredType, create.Columns.Single().DataType);
        }

        [Fact]
        public void ParserRejectsAnEmptySignConstraint()
        {
            // Parse() reports syntax errors as diagnostics rather than throwing, so the contract
            // to assert is that INT(5,) does not parse silently into a bogus type string.
            var sql = "CREATE TABLE #Layout ( Value INT(5,) );";
            var script = new ETL_SQL.Core.Parser.Parser(
                new ETL_SQL.Core.Parser.Lexer(sql).Tokenize(), sql).Parse();

            Assert.Contains(script.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }

        // A bare INT(N) reserves an extra slot for the sign, so INT(5) is six characters
        // wide. INT(5,+) is positive-only and therefore needs no sign slot: five characters.

        [Fact]
        public async Task PositiveOnlyIntegerDropsTheSignSlotFromTheWidth()
        {
            string fwFile = "FWTest_SignPositive.txt";
            // ID is INT(5,+) => 5 chars, Name follows immediately at offset 5.
            await File.WriteAllTextAsync(fwFile, "00042Chuck     \n00007Bob       ");

            try
            {
                var schema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("ID", "INT(5,+)", false),
                    new ColumnDefinition("Name", "VARCHAR(10)", false)
                };
                var options = new Dictionary<string, string> { { "FORMAT", "FIXED" }, { "HEADER", "OFF" } };

                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options, schema);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Equal("00042", batches[0].Rows[0]["ID"]?.ToString());
                Assert.Equal("Chuck", batches[0].Rows[0]["Name"]?.ToString());
                Assert.Equal("Bob", batches[0].Rows[1]["Name"]?.ToString());
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

        [Fact]
        public async Task SignedIntegerKeepsTheSignSlot()
        {
            string fwFile = "FWTest_SignAny.txt";
            // ID is INT(5) => 6 chars (5 digits + sign), Name starts at offset 6.
            await File.WriteAllTextAsync(fwFile, "-00042Chuck     ");

            try
            {
                var schema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("ID", "INT(5)", false),
                    new ColumnDefinition("Name", "VARCHAR(10)", false)
                };
                var options = new Dictionary<string, string> { { "FORMAT", "FIXED" }, { "HEADER", "OFF" } };

                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options, schema);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Equal("-00042", batches[0].Rows[0]["ID"]?.ToString());
                Assert.Equal("Chuck", batches[0].Rows[0]["Name"]?.ToString());
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

        [Fact]
        public async Task WritingANegativeValueToAPositiveOnlyColumnFails()
        {
            string fwFile = "FWTest_SignReject.txt";
            try
            {
                var schema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("ID", "INT(5,+)", false)
                };
                var options = new Dictionary<string, string> { { "FORMAT", "FIXED" }, { "HEADER", "OFF" } };

                var table = new DataTable();
                table.SetColumns(new[] { "ID" });
                var row = new Row();
                row["ID"] = "-42";
                await table.AddRowAsync(row);

                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options, schema);
                var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
                    await ds.WriteBatches(new[] { table }.ToAsyncEnumerable()));

                Assert.Contains("sign constraint", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

        [Fact]
        public async Task WritingAPositiveValueToANegativeOnlyColumnFails()
        {
            string fwFile = "FWTest_SignRejectNeg.txt";
            try
            {
                var schema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("ID", "INT(5,-)", false)
                };
                var options = new Dictionary<string, string> { { "FORMAT", "FIXED" }, { "HEADER", "OFF" } };

                var table = new DataTable();
                table.SetColumns(new[] { "ID" });
                var row = new Row();
                row["ID"] = "42";
                await table.AddRowAsync(row);

                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options, schema);
                var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
                    await ds.WriteBatches(new[] { table }.ToAsyncEnumerable()));

                Assert.Contains("sign constraint", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

        [Fact]
        public async Task PositiveOnlyColumnAcceptsAPositiveValue()
        {
            string fwFile = "FWTest_SignAccept.txt";
            try
            {
                var schema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("ID", "INT(5,+)", false)
                };
                var options = new Dictionary<string, string> { { "FORMAT", "FIXED" }, { "HEADER", "OFF" } };

                var table = new DataTable();
                table.SetColumns(new[] { "ID" });
                var row = new Row();
                row["ID"] = "42";
                await table.AddRowAsync(row);

                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, fwFile, options, schema);
                await ds.WriteBatches(new[] { table }.ToAsyncEnumerable());

                var written = await File.ReadAllTextAsync(fwFile);
                Assert.Contains("42", written);
            }
            finally { if (File.Exists(fwFile)) File.Delete(fwFile); }
        }

    }
}
