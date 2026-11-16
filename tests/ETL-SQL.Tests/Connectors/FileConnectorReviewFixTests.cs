using System.Text;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.Json;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.Xml;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using MiniExcelLibs;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    /// <summary>
    /// Regression tests for the file connector findings of the 2026-11-01 section 6 review:
    /// appends that overwrote the file, type inference from the first row only, ENCODING ignored
    /// on one side of the round trip, and metadata that answered for the wrong table.
    /// </summary>
    [Trait("Connector", "FILE")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public sealed class FileConnectorReviewFixTests : IDisposable
    {
        private readonly string _dir;
        private static SystemExecutionContext Ctx => SystemExecutionContext.Instance;

        public FileConnectorReviewFixTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "FCRF-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private string PathOf(string name) => Path.Combine(_dir, name);

        private static async IAsyncEnumerable<DataTable> Batches(params DataTable[] tables)
        {
            foreach (var t in tables) yield return t;
            await Task.CompletedTask;
        }

        private static async Task<DataTable> Table(string[] columns, params object?[][] rows)
        {
            var table = new DataTable();
            table.SetColumns(columns);
            foreach (var values in rows)
            {
                var row = new Row();
                for (var i = 0; i < columns.Length; i++) row[columns[i]] = values[i];
                await table.AddRowAsync(row);
            }
            return table;
        }

        private static async Task<List<Row>> ReadAll(IDataSource source)
        {
            var rows = new List<Row>();
            await foreach (var batch in source.ReadBatches()) rows.AddRange(batch.Rows);
            return rows;
        }

        // ---- FlatFile ----

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task FlatFile_AppendToCompressedOrEncryptedFile_KeepsTheExistingRows(bool encrypt, bool compress)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (encrypt) { options["ENCRYPT"] = "ON"; options["PASSWORD"] = "AppendPass123!"; }
            if (compress) options["COMPRESS"] = "ON";
            var path = PathOf("append.csv");

            var first = new FlatFileDataSource(Ctx, path, options);
            await first.WriteBatches(Batches(await Table(new[] { "id", "name" }, new object?[] { 1m, "Alice" })), append: false);

            var second = new FlatFileDataSource(Ctx, path, options);
            await second.WriteBatches(Batches(await Table(new[] { "id", "name" }, new object?[] { 2m, "Bob" })), append: true);

            var rows = await ReadAll(new FlatFileDataSource(Ctx, path, options));
            Assert.Equal(new[] { "Alice", "Bob" }, rows.Select(r => r["name"]?.ToString()).ToArray());
        }

        // ---- Parquet ----

        [Fact]
        public async Task Parquet_FirstValueNull_StillKeepsTheDecimalColumn()
        {
            var path = PathOf("nullfirst.parquet");
            var table = await Table(new[] { "amount" }, new object?[] { null }, new object?[] { 12.34m });

            await new ParquetDataSource(Ctx, path).WriteBatches(Batches(table));

            var rows = await ReadAll(new ParquetDataSource(Ctx, path));
            Assert.Null(rows[0]["amount"]);
            Assert.Equal(12.34m, Convert.ToDecimal(rows[1]["amount"]));
        }

        [Fact]
        public void Parquet_InferColumnType_WidensAcrossRows()
        {
            var rows = new List<Row>
            {
                new Row { ["v"] = null },
                new Row { ["v"] = 1L },
                new Row { ["v"] = 2.5m }
            };
            Assert.Equal(typeof(decimal), ParquetDataSource.InferColumnType(rows, "v"));
        }

        [Fact]
        public async Task Parquet_NonNullableInt32Column_IsRead()
        {
            var path = PathOf("int32.parquet");
            var schema = new Parquet.Schema.ParquetSchema(new Parquet.Schema.DataField<int>("n"));
            using (var stream = File.Create(path))
            await using (var writer = await Parquet.ParquetWriter.CreateAsync(schema, stream))
            using (var group = writer.CreateRowGroup())
            {
                await group.WriteAsync(schema.DataFields[0], new ReadOnlyMemory<int>(new[] { 7, 8 }), null, null, CancellationToken.None);
            }

            var rows = await ReadAll(new ParquetDataSource(Ctx, path));
            Assert.Equal(new[] { 7m, 8m }, rows.Select(r => Convert.ToDecimal(r["n"])).ToArray());
        }

        [Fact]
        public async Task Parquet_Append_KeepsTheExistingRowGroups()
        {
            var path = PathOf("append.parquet");
            await new ParquetDataSource(Ctx, path).WriteBatches(
                Batches(await Table(new[] { "id", "name" }, new object?[] { 1m, "Alice" })), append: false);

            await new ParquetDataSource(Ctx, path).WriteBatches(
                Batches(await Table(new[] { "id", "name" }, new object?[] { 2m, "Bob" })), append: true);

            var rows = await ReadAll(new ParquetDataSource(Ctx, path));
            Assert.Equal(new[] { "Alice", "Bob" }, rows.Select(r => r["name"]?.ToString()).ToArray());
            Assert.Equal(new[] { 1m, 2m }, rows.Select(r => Convert.ToDecimal(r["id"])).ToArray());
        }

        [Fact]
        public async Task Parquet_AppendWithAColumnTheFileLacks_IsRefused()
        {
            var path = PathOf("mismatch.parquet");
            await new ParquetDataSource(Ctx, path).WriteBatches(
                Batches(await Table(new[] { "id" }, new object?[] { 1m })), append: false);

            var mismatched = await Table(new[] { "id", "extra" }, new object?[] { 2m, "x" });
            await Assert.ThrowsAsync<ExecutionException>(() => new ParquetDataSource(Ctx, path).WriteBatches(
                Batches(mismatched), append: true));

            var rows = await ReadAll(new ParquetDataSource(Ctx, path));
            Assert.Single(rows);
        }

        [Fact]
        public async Task Parquet_Truncate_OfAScriptFile_IsRefused()
        {
            var path = PathOf("evil.etlsql");
            File.WriteAllText(path, "SELECT 1;");
            var context = new Moq.Mock<ETL_SQL.Core.IExecutionContext>();
            context.Setup(c => c.SecurityService).Returns(new ETL_SQL.Services.SecurityService(NullLogger.Instance));
            context.Setup(c => c.Logger).Returns(NullLogger.Instance);
            context.Setup(c => c.AllowUnknownFileTypes).Returns(true);
            context.Setup(c => c.ResolvePath(Moq.It.IsAny<string>())).Returns<string>(p => p);

            var ds = new ParquetDataSource(context.Object, path);

            await Assert.ThrowsAsync<ETL_SQL.Services.SecurityException>(() => ds.TruncateAsync());
            Assert.Equal("SELECT 1;", File.ReadAllText(path));
        }

        // ---- Avro ----

        [Fact]
        public async Task Avro_Schema_EngineIntegerIsLongAndLeadingNullDoesNotDecideTheType()
        {
            var table = await Table(new[] { "id", "score" },
                new object?[] { 1m, null },
                new object?[] { 2m, 3.5m });

            var schema = AvroDataSource.GenerateSchemaJson(table);

            Assert.Contains("{\"name\": \"id\", \"type\": [\"null\", \"long\"]", schema);
            Assert.Contains("{\"name\": \"score\", \"type\": [\"null\", \"double\"]", schema);
        }

        [Fact]
        public async Task Avro_Append_KeepsTheExistingRows()
        {
            var path = PathOf("append.avro");
            await new AvroDataSource(Ctx, path).WriteBatches(
                Batches(await Table(new[] { "id", "name" }, new object?[] { 1m, "Alice" })), append: false);

            await new AvroDataSource(Ctx, path).WriteBatches(
                Batches(await Table(new[] { "id", "name" }, new object?[] { 2m, "Bob" })), append: true);

            var rows = await ReadAll(new AvroDataSource(Ctx, path));
            Assert.Equal(new[] { "Alice", "Bob" }, rows.Select(r => r["name"]?.ToString()).ToArray());
            Assert.Equal(new[] { 1m, 2m }, rows.Select(r => Convert.ToDecimal(r["id"])).ToArray());
        }

        // ---- Excel ----

        [Fact]
        public async Task Excel_GetColumnsOfAnotherSheet_AnswersForThatSheet()
        {
            var path = PathOf("book.xlsx");
            var sheets = new Dictionary<string, object>
            {
                ["People"] = new[] { new Dictionary<string, object> { ["id"] = 1, ["name"] = "Alice" } },
                ["Orders"] = new[] { new Dictionary<string, object> { ["order_id"] = 10, ["total"] = 5 } }
            };
            MiniExcel.SaveAs(path, sheets);

            var ds = new ExcelDataSource(Ctx, path, new Dictionary<string, string> { ["SHEET"] = "People" });

            Assert.Equal(new[] { "id", "name" }, (await ds.GetColumnsAsync("People")).ToArray());
            Assert.Equal(new[] { "order_id", "total" }, (await ds.GetColumnsAsync("Orders")).ToArray());
        }

        // ---- JSON ----

        [Fact]
        public async Task Json_Append_KeepsTheExistingArrayElements()
        {
            var path = PathOf("append.json");
            await new JsonDataSource(Ctx, path).WriteBatches(
                Batches(await Table(new[] { "id", "name" }, new object?[] { 1m, "Alice" })), append: false);

            await new JsonDataSource(Ctx, path).WriteBatches(
                Batches(await Table(new[] { "id", "name" }, new object?[] { 2m, "Bob" })), append: true);

            var rows = await ReadAll(new JsonDataSource(Ctx, path));
            Assert.Equal(new[] { "Alice", "Bob" }, rows.Select(r => r["name"]?.ToString()).ToArray());
        }

        [Fact]
        public async Task Json_Latin1File_IsDecodedWithTheGivenEncoding()
        {
            var path = PathOf("latin1.json");
            File.WriteAllBytes(path, Encoding.Latin1.GetBytes("[{\"name\":\"José\"}]"));

            var rows = await ReadAll(new JsonDataSource(Ctx, path, new Dictionary<string, string> { ["ENCODING"] = "LATIN1" }));

            Assert.Equal("José", rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task Json_Latin1Write_RoundTripsThroughTheSameEncoding()
        {
            var path = PathOf("latin1-out.json");
            var options = new Dictionary<string, string> { ["ENCODING"] = "LATIN1" };
            await new JsonDataSource(Ctx, path, options).WriteBatches(
                Batches(await Table(new[] { "name" }, new object?[] { "José" })));

            // The serializer escapes non-ASCII, so the bytes are ASCII either way; the read side must agree.
            Assert.DoesNotContain((byte)0xC3, File.ReadAllBytes(path));
            var rows = await ReadAll(new JsonDataSource(Ctx, path, options));
            Assert.Equal("José", rows[0]["name"]?.ToString());
        }

        // ---- XML ----

        [Fact]
        public async Task Xml_Append_KeepsTheExistingRows()
        {
            var path = PathOf("append.xml");
            await new XmlDataSource(Ctx, path).WriteBatches(
                Batches(await Table(new[] { "id", "name" }, new object?[] { 1m, "Alice" })), append: false);

            await new XmlDataSource(Ctx, path).WriteBatches(
                Batches(await Table(new[] { "id", "name" }, new object?[] { 2m, "Bob" })), append: true);

            var rows = await ReadAll(new XmlDataSource(Ctx, path));
            Assert.Equal(new[] { "Alice", "Bob" }, rows.Select(r => r["name"]?.ToString()).ToArray());
        }

        [Fact]
        public async Task Xml_Latin1Write_RoundTripsThroughTheSameEncoding()
        {
            var path = PathOf("latin1.xml");
            var options = new Dictionary<string, string> { ["ENCODING"] = "LATIN1" };
            await new XmlDataSource(Ctx, path, options).WriteBatches(
                Batches(await Table(new[] { "name" }, new object?[] { "José" })));

            Assert.Contains((byte)0xE9, File.ReadAllBytes(path));
            var rows = await ReadAll(new XmlDataSource(Ctx, path, options));
            Assert.Equal("José", rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task XmlConnector_GetTables_MatchesTheDataSource()
        {
            var path = PathOf("tables.xml");
            File.WriteAllText(path, "<ROOT><ROW><id>1</id></ROW></ROOT>");

            var fromConnector = await new XmlConnector().GetTablesAsync(Ctx, path);
            var fromSource = await new XmlDataSource(Ctx, path).GetTablesAsync();

            Assert.Equal(fromSource.ToArray(), fromConnector.ToArray());
        }
    }
}
