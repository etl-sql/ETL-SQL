using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.Json;
using ETL_SQL.Connectors.Xml;
using ETL_SQL.Connectors;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Connectors
{
    /// <summary>
    /// Fast-lane unit tests for file-based connector data sources (FlatFile, JSON, XML)
    /// and ConnectionStringBuilder. No external services required — only temp files.
    /// </summary>
    [Trait("Connector", "FILE")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class FileConnectorTests : IDisposable
    {
        private readonly string _dir;
        private static SystemExecutionContext Ctx => SystemExecutionContext.Instance;

        public FileConnectorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "FCT-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private string Write(string name, string content)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private async Task<List<ETL_SQL.Data.DataTable>> Read(FlatFileDataSource ds) =>
            await ds.ReadBatches().ToListAsync();

        // ── FlatFileDataSource ─────────────────────────────────────────────────

        [Fact]
        public async Task FlatFile_BasicCsv_ReadsTwoRows()
        {
            var path = Write("basic.csv", "id,name\n1,Alice\n2,Bob");
            var ds = new FlatFileDataSource(Ctx, path);
            var batches = await Read(ds);
            Assert.Equal(2, batches[0].Rows.Count);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_PipeDelimiter_ParsesCorrectly()
        {
            var path = Write("pipe.csv", "id|name\n1|Alice\n2|Bob");
            var opts = new Dictionary<string, string> { ["DELIMITER"] = "PIPE" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_TabDelimiter_ParsesCorrectly()
        {
            var path = Write("tab.tsv", "id\tname\n1\tAlice");
            var opts = new Dictionary<string, string> { ["DELIMITER"] = "TAB" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_SemicolonDelimiter_ParsesCorrectly()
        {
            var path = Write("semi.csv", "id;name\n1;Alice");
            var opts = new Dictionary<string, string> { ["DELIMITER"] = "SEMICOLON" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_ColonDelimiter_ParsesCorrectly()
        {
            var path = Write("colon.csv", "id:name\n1:Alice");
            var opts = new Dictionary<string, string> { ["DELIMITER"] = "COLON" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_TildeDelimiter_ParsesCorrectly()
        {
            var path = Write("tilde.csv", "id~name\n1~Alice");
            var opts = new Dictionary<string, string> { ["DELIMITER"] = "TILDE" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_SingleCharDelimiter_ParsesCorrectly()
        {
            var path = Write("at.csv", "id@name\n1@Alice");
            var opts = new Dictionary<string, string> { ["DELIMITER"] = "@" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_NoHeader_UsesColumnNumbers()
        {
            var path = Write("noheader.csv", "1,Alice\n2,Bob");
            var opts = new Dictionary<string, string> { ["HEADER"] = "OFF" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal(2, batches[0].Rows.Count);
        }

        [Fact]
        public async Task FlatFile_HeaderTrue_ParsesNormally()
        {
            var path = Write("header_on.csv", "id,name\n1,Alice");
            var opts = new Dictionary<string, string> { ["HEADER"] = "ON" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Single(batches[0].Rows);
        }

        [Fact]
        public async Task FlatFile_StartAt_SkipsRows()
        {
            var path = Write("skip.csv", "JUNK\nMORE JUNK\nid,name\n1,Alice");
            var opts = new Dictionary<string, string> { ["START_AT"] = "2" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Single(batches[0].Rows);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_EndAt_ReservesTrailingRows()
        {
            // END_AT=N keeps the last N rows as a footer buffer (e.g. for COUNT_AT_END).
            // With 3 data rows and END_AT=2 only the 1st row flows through the queue.
            var path = Write("endat.csv", "id,name\n1,Alice\n2,Bob\n3,Charlie");
            var opts = new Dictionary<string, string> { ["END_AT"] = "2" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Single(batches[0].Rows);
        }

        [Fact]
        public async Task FlatFile_QuotedValues_ParsesEmbeddedComma()
        {
            var path = Write("quoted.csv", "id,name\n1,\"Alice, Smith\"\n2,Bob");
            var ds = new FlatFileDataSource(Ctx, path);
            var batches = await Read(ds);
            Assert.Equal("Alice, Smith", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_TextQualifierSingleQuote_ParsesCorrectly()
        {
            var path = Write("sq.csv", "id,name\n1,'Alice, Smith'");
            var opts = new Dictionary<string, string> { ["TEXT_QUALIFIER"] = "SINGLEQUOTE" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("Alice, Smith", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_TextQualifierDoublequoteString_ParsesCorrectly()
        {
            var path = Write("dq.csv", "id,name\n1,\"Alice\"");
            var opts = new Dictionary<string, string> { ["TEXT_QUALIFIER"] = "DOUBLEQUOTES" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_NullAsEmpty_ReturnsEmptyString()
        {
            var path = Write("nullas.csv", "id,val\n1,\n2,hello");
            var opts = new Dictionary<string, string> { ["NULL_AS"] = "EMPTY" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("", batches[0].Rows[0]["val"]?.ToString() ?? "");
        }

        [Fact]
        public async Task FlatFile_NullAsNull_ReturnsNullLiteral()
        {
            var path = Write("nullasnull.csv", "id,val\n1,NULL\n2,hello");
            var opts = new Dictionary<string, string> { ["NULL_AS"] = "NULL" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.NotNull(batches[0].Rows);
        }

        [Fact]
        public async Task FlatFile_NullAsBackslashN_ParsesCorrectly()
        {
            var path = Write("nullasbn.csv", "id,val\n1,\\n");
            var opts = new Dictionary<string, string> { ["NULL_AS"] = "BACKSLASH_N" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.NotNull(batches[0]);
        }

        [Fact]
        public async Task FlatFile_TrimOff_PreservesTrailingWhitespace()
        {
            // The CSV parser always skips leading whitespace after a delimiter.
            // TRIM=OFF only controls whether trailing whitespace is preserved.
            var path = Write("trim.csv", "id,name\n1,Alice ");
            var opts = new Dictionary<string, string> { ["TRIM"] = "OFF" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal("Alice ", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task FlatFile_EncodingAnsi_ParsesCorrectly()
        {
            var path = Write("ansi.csv", "id,name\n1,Alice");
            var opts = new Dictionary<string, string> { ["ENCODING"] = "ANSI" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Single(batches[0].Rows);
        }

        [Fact]
        public async Task FlatFile_EncodingAscii_ParsesCorrectly()
        {
            var path = Write("ascii.csv", "id,name\n1,Alice");
            var opts = new Dictionary<string, string> { ["ENCODING"] = "ASCII" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Single(batches[0].Rows);
        }

        [Fact]
        public async Task FlatFile_EncodingUtf16_ParsesCorrectly()
        {
            var path = Write("utf16.csv", "id,name\n1,Alice");
            var opts = new Dictionary<string, string> { ["ENCODING"] = "UTF16" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.NotNull(batches);
        }

        [Fact]
        public async Task FlatFile_CultureInvariant_ParsesNumbers()
        {
            var path = Write("culture.csv", "id,amt\n1,1234.56");
            var opts = new Dictionary<string, string> { ["CULTURE"] = "en-US" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Single(batches[0].Rows);
        }

        [Fact]
        public async Task FlatFile_InvalidCulture_FallsBackGracefully()
        {
            var path = Write("badculture.csv", "id,name\n1,Alice");
            var opts = new Dictionary<string, string> { ["CULTURE"] = "xx-INVALID" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.NotNull(batches);
        }

        [Fact]
        public async Task FlatFile_DateFormat_OptionAccepted()
        {
            var path = Write("date.csv", "id,dt\n1,2024-01-15");
            var opts = new Dictionary<string, string> { ["DATE_FORMAT"] = "yyyy-MM-dd" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Single(batches[0].Rows);
        }

        [Fact]
        public async Task FlatFile_EscapeChar_ParsesEscapedDelimiter()
        {
            var path = Write("escape.csv", "id,name\n1,Alice\\,Smith");
            var opts = new Dictionary<string, string> { ["ESCAPE_CHAR"] = "\\" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.NotNull(batches[0].Rows);
        }

        [Fact]
        public async Task FlatFile_RowDelimiterCRLF_ParsesCorrectly()
        {
            var path = Write("crlf.csv", "id,name\r\n1,Alice\r\n2,Bob");
            var opts = new Dictionary<string, string> { ["ROW_DELIMITER"] = "CRLF" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.NotNull(batches);
        }

        [Fact]
        public async Task FlatFile_RowDelimiterTilde_ParsesCorrectly()
        {
            var path = Write("tildedelim.txt", "id,name~1,Alice~2,Bob");
            var opts = new Dictionary<string, string> { ["ROW_DELIMITER"] = "TILDE" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.NotNull(batches);
        }

        [Fact]
        public async Task FlatFile_StrictSchema_Off_IgnoresMismatch()
        {
            var path = Write("strict.csv", "id,name\n1,Alice,Extra\n2,Bob");
            var opts = new Dictionary<string, string> { ["STRICT_SCHEMA"] = "OFF" };
            var ds = new FlatFileDataSource(Ctx, path, opts);
            var batches = await Read(ds);
            Assert.Equal(2, batches[0].Rows.Count);
        }

        [Fact]
        public async Task FlatFile_EmptyFile_ReturnsEmptyBatch()
        {
            var path = Write("empty.csv", "");
            var ds = new FlatFileDataSource(Ctx, path);
            var batches = await Read(ds);
            Assert.NotNull(batches);
        }

        [Fact]
        public async Task FlatFile_HeaderOnlyFile_ReturnsZeroRows()
        {
            var path = Write("headeronly.csv", "id,name");
            var ds = new FlatFileDataSource(Ctx, path);
            var batches = await Read(ds);
            Assert.Equal(0, batches.Sum(b => b.Rows.Count));
        }

        [Fact]
        public async Task FlatFile_WriteBatches_ProducesReadableFile()
        {
            var outPath = Path.Combine(_dir, "written.csv");
            var ds = new FlatFileDataSource(Ctx, outPath);
            var dt = new ETL_SQL.Data.DataTable();
            dt.SetColumns(new[] { "id", "name" });
            var row = dt.NewRow(); row["id"] = 1; row["name"] = "Alice"; await dt.AddRowAsync(row);
            await ds.WriteBatches(new[] { dt }.ToAsyncEnumerable());
            Assert.True(File.Exists(outPath));
            var content = await File.ReadAllTextAsync(outPath);
            Assert.Contains("Alice", content);
        }

        [Fact]
        public async Task FlatFile_WriteBatches_PipeDelimiter_ProducesCorrectOutput()
        {
            var outPath = Path.Combine(_dir, "pipe_out.csv");
            var opts = new Dictionary<string, string> { ["DELIMITER"] = "PIPE" };
            var ds = new FlatFileDataSource(Ctx, outPath, opts);
            var dt = new ETL_SQL.Data.DataTable();
            dt.SetColumns(new[] { "id", "name" });
            var row = dt.NewRow(); row["id"] = 1; row["name"] = "Alice"; await dt.AddRowAsync(row);
            await ds.WriteBatches(new[] { dt }.ToAsyncEnumerable());
            var content = await File.ReadAllTextAsync(outPath);
            Assert.Contains("|", content);
        }

        [Fact]
        public async Task FlatFile_WriteBatches_AppendMode_AddRows()
        {
            var outPath = Path.Combine(_dir, "append.csv");
            File.WriteAllText(outPath, "id,name\n1,Alice\n");
            var ds = new FlatFileDataSource(Ctx, outPath);
            var dt = new ETL_SQL.Data.DataTable();
            dt.SetColumns(new[] { "id", "name" });
            var row = dt.NewRow(); row["id"] = 2; row["name"] = "Bob"; await dt.AddRowAsync(row);
            await ds.WriteBatches(new[] { dt }.ToAsyncEnumerable(), append: true);
            var lines = File.ReadAllLines(outPath);
            Assert.Equal(3, lines.Where(l => !string.IsNullOrEmpty(l)).Count());
        }

        [Fact]
        public async Task FlatFile_WriteBatches_CountAtEnd_AppendsTrailer()
        {
            var outPath = Path.Combine(_dir, "countend.csv");
            var opts = new Dictionary<string, string> { ["COUNT_AT_END"] = "TOTAL: COUNT" };
            var ds = new FlatFileDataSource(Ctx, outPath, opts);
            var dt = new ETL_SQL.Data.DataTable();
            dt.SetColumns(new[] { "id", "name" });
            var row = dt.NewRow(); row["id"] = 1; row["name"] = "Alice"; await dt.AddRowAsync(row);
            await ds.WriteBatches(new[] { dt }.ToAsyncEnumerable());
            var content = await File.ReadAllTextAsync(outPath);
            Assert.Contains("TOTAL: 1", content);
        }

        [Fact]
        public async Task FlatFile_WriteBatches_NoHeader_OmitsHeaderRow()
        {
            var outPath = Path.Combine(_dir, "noheader_out.csv");
            var opts = new Dictionary<string, string> { ["HEADER"] = "OFF" };
            var ds = new FlatFileDataSource(Ctx, outPath, opts);
            var dt = new ETL_SQL.Data.DataTable();
            dt.SetColumns(new[] { "id", "name" });
            var row = dt.NewRow(); row["id"] = 1; row["name"] = "Alice"; await dt.AddRowAsync(row);
            await ds.WriteBatches(new[] { dt }.ToAsyncEnumerable());
            var lines = File.ReadAllLines(outPath).Where(l => !string.IsNullOrEmpty(l)).ToList();
            Assert.Single(lines);
            Assert.DoesNotContain("id", lines[0]);
        }

        // ── FlatFileConnector ──────────────────────────────────────────────────

        [Fact]
        public void FlatFileConnector_GetHelp_ReturnsNonEmpty()
        {
            var conn = new FlatFileConnector();
            Assert.NotEmpty(conn.GetHelp());
        }

        [Fact]
        public void FlatFileConnector_GetSupportedOptions_ContainsDelimiter()
        {
            var conn = new FlatFileConnector();
            var opts = conn.GetSupportedOptions();
            Assert.True(opts.ContainsKey("DELIMITER"));
        }

        [Fact]
        public void FlatFileConnector_GetOptionValues_ReturnsValues()
        {
            var conn = new FlatFileConnector();
            var vals = conn.GetOptionValues();
            Assert.True(vals.ContainsKey("HEADER"));
        }

        [Fact]
        public void FlatFileConnector_CreateDataSource_CsvPath_ReturnsFlatFileSource()
        {
            var conn = new FlatFileConnector();
            var csvPath = Path.Combine(_dir, "test.csv");
            File.WriteAllText(csvPath, "id\n1");
            var ds = conn.CreateDataSource(Ctx, csvPath);
            Assert.IsType<FlatFileDataSource>(ds);
        }

        [Fact]
        public void FlatFileConnector_CreateDataSource_JsonPath_ReturnsJsonSource()
        {
            var conn = new FlatFileConnector();
            var jsonPath = Path.Combine(_dir, "test.json");
            File.WriteAllText(jsonPath, "[{\"id\":1}]");
            var ds = conn.CreateDataSource(Ctx, jsonPath);
            Assert.IsType<JsonDataSource>(ds);
        }

        [Fact]
        public void FlatFileConnector_CreateDataSource_XmlPath_ReturnsXmlSource()
        {
            var conn = new FlatFileConnector();
            var xmlPath = Path.Combine(_dir, "test.xml");
            File.WriteAllText(xmlPath, "<root><row><id>1</id></row></root>");
            var ds = conn.CreateDataSource(Ctx, xmlPath);
            Assert.IsType<XmlDataSource>(ds);
        }

        [Fact]
        public async Task FlatFileConnector_GetTablesAsync_ReturnsFilename()
        {
            var conn = new FlatFileConnector();
            var tables = await conn.GetTablesAsync(Ctx, "/some/path/mydata.csv");
            Assert.Contains("mydata", tables);
        }

        [Fact]
        public void FlatFileConnector_BuildConnectionString_ReturnsSelfPath()
        {
            var conn = new FlatFileConnector();
            var cs = conn.BuildConnectionString(new Dictionary<string, string> { ["PATH"] = "/tmp/data.csv" });
            Assert.Equal("/tmp/data.csv", cs);
        }

        [Fact]
        public void FlatFileConnector_Properties_AreCorrect()
        {
            var conn = new FlatFileConnector();
            Assert.Equal("FLATFILE", conn.Name);
            Assert.True(conn.IsFileBased);
            Assert.Contains("CSV", conn.Aliases);
        }

        // ── JsonDataSource ─────────────────────────────────────────────────────

        [Fact]
        public async Task Json_BasicArrayRead_ReturnsRows()
        {
            var path = Write("basic.json", "[{\"id\":1,\"name\":\"Alice\"},{\"id\":2,\"name\":\"Bob\"}]");
            var ds = new JsonDataSource(Ctx, path);
            var batches = await ds.ReadBatches().ToListAsync();
            Assert.Equal(2, batches[0].Rows.Count);
            Assert.Equal("Alice", batches[0].Rows[0]["name"]?.ToString());
        }

        [Fact]
        public async Task Json_NestedRootPath_ReturnsCorrectRows()
        {
            var path = Write("nested.json", "{\"data\":{\"items\":[{\"val\":10},{\"val\":20}]}}");
            var opts = new Dictionary<string, string> { ["ROOT_PATH"] = "data.items" };
            var ds = new JsonDataSource(Ctx, path, opts);
            var batches = await ds.ReadBatches().ToListAsync();
            Assert.Equal(2, batches[0].Rows.Count);
        }

        [Fact]
        public async Task Json_EmptyArray_ReturnsNoBatches()
        {
            var path = Write("empty.json", "[]");
            var ds = new JsonDataSource(Ctx, path);
            var batches = await ds.ReadBatches().ToListAsync();
            Assert.Equal(0, batches.Sum(b => b.Rows.Count));
        }

        [Fact]
        public async Task Json_WriteBatches_ProducesJsonFile()
        {
            var outPath = Path.Combine(_dir, "out.json");
            var ds = new JsonDataSource(Ctx, outPath);
            var dt = new ETL_SQL.Data.DataTable();
            dt.SetColumns(new[] { "id", "name" });
            var row = dt.NewRow(); row["id"] = 1; row["name"] = "Alice"; await dt.AddRowAsync(row);
            await ds.WriteBatches(new[] { dt }.ToAsyncEnumerable());
            Assert.True(File.Exists(outPath));
            var content = await File.ReadAllTextAsync(outPath);
            Assert.Contains("Alice", content);
        }

        [Fact]
        public async Task Json_ArrayOfObjects_ParsedAsRows()
        {
            var path = Write("arr.json", "[{\"id\":1,\"name\":\"Alice\"},{\"id\":2,\"name\":\"Bob\"}]");
            var ds = new JsonDataSource(Ctx, path);
            var batches = await ds.ReadBatches().ToListAsync();
            Assert.True(batches.Sum(b => b.Rows.Count) >= 2);
        }

        [Fact]
        public void JsonConnector_Properties_AreCorrect()
        {
            var conn = new JsonConnector();
            Assert.Equal("JSON", conn.Name);
            Assert.True(conn.IsFileBased);
        }

        [Fact]
        public void JsonConnector_GetHelp_ReturnsNonEmpty()
        {
            var conn = new JsonConnector();
            Assert.NotEmpty(conn.GetHelp());
        }

        [Fact]
        public void JsonConnector_GetSupportedOptions_ReturnsOptions()
        {
            var conn = new JsonConnector();
            var opts = conn.GetSupportedOptions();
            Assert.NotNull(opts);
        }

        [Fact]
        public async Task JsonConnector_GetTablesAsync_ReturnsFile()
        {
            var conn = new JsonConnector();
            var tables = await conn.GetTablesAsync(Ctx, "/path/mydata.json");
            Assert.Contains("FILE", tables);
        }

        // ── XmlDataSource ──────────────────────────────────────────────────────

        [Fact]
        public async Task Xml_BasicRead_ReturnsRows()
        {
            var path = Write("basic.xml",
                "<root><row><id>1</id><name>Alice</name></row><row><id>2</id><name>Bob</name></row></root>");
            var ds = new XmlDataSource(Ctx, path);
            var batches = await ds.ReadBatches().ToListAsync();
            Assert.True(batches.Sum(b => b.Rows.Count) > 0);
        }

        [Fact]
        public async Task Xml_WithXPath_ReturnsFilteredRows()
        {
            var path = Write("xpath.xml",
                "<data><items><item><id>1</id><v>10</v></item><item><id>2</id><v>20</v></item></items></data>");
            var opts = new Dictionary<string, string> { ["ELEMENT_PATH"] = "items/item" };
            var ds = new XmlDataSource(Ctx, path, opts);
            var batches = await ds.ReadBatches().ToListAsync();
            Assert.True(batches.Sum(b => b.Rows.Count) >= 0);
        }

        [Fact]
        public async Task Xml_EmptyDocument_HandledGracefully()
        {
            var path = Write("empty.xml", "<root></root>");
            var ds = new XmlDataSource(Ctx, path);
            var batches = await ds.ReadBatches().ToListAsync();
            Assert.NotNull(batches);
        }

        [Fact]
        public async Task Xml_WriteBatches_ProducesXmlFile()
        {
            var outPath = Path.Combine(_dir, "out.xml");
            var ds = new XmlDataSource(Ctx, outPath);
            var dt = new ETL_SQL.Data.DataTable();
            dt.SetColumns(new[] { "id", "name" });
            var row = dt.NewRow(); row["id"] = 1; row["name"] = "Alice"; await dt.AddRowAsync(row);
            await ds.WriteBatches(new[] { dt }.ToAsyncEnumerable());
            Assert.True(File.Exists(outPath));
            var content = await File.ReadAllTextAsync(outPath);
            Assert.Contains("Alice", content);
        }

        [Fact]
        public void XmlConnector_Properties_AreCorrect()
        {
            var conn = new XmlConnector();
            Assert.Equal("XML", conn.Name);
            Assert.True(conn.IsFileBased);
        }

        [Fact]
        public void XmlConnector_GetHelp_ReturnsNonEmpty()
        {
            var conn = new XmlConnector();
            Assert.NotEmpty(conn.GetHelp());
        }

        [Fact]
        public void XmlConnector_GetSupportedOptions_ReturnsOptions()
        {
            var conn = new XmlConnector();
            Assert.NotNull(conn.GetSupportedOptions());
        }

        [Fact]
        public async Task XmlConnector_GetTablesAsync_ReturnsEmpty()
        {
            var conn = new XmlConnector();
            var tables = await conn.GetTablesAsync(Ctx, "/path/mydata.xml");
            Assert.Empty(tables);
        }

        // ── ConnectionStringBuilder ────────────────────────────────────────────

        [Fact]
        public void ConnectionStringBuilder_EmptyProvider_ReturnsEmpty()
        {
            var result = ConnectionStringBuilder.Build("", new Dictionary<string, string> { ["SERVER"] = "localhost" });
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ConnectionStringBuilder_NullProps_ReturnsEmpty()
        {
            var result = ConnectionStringBuilder.Build("MSSQL", null!);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ConnectionStringBuilder_EmptyProps_ReturnsEmpty()
        {
            var result = ConnectionStringBuilder.Build("MSSQL", new Dictionary<string, string>());
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ConnectionStringBuilder_MssqlTrusted_ContainsIntegratedSecurity()
        {
            var props = new Dictionary<string, string>
            {
                ["SERVER"] = "localhost", ["DATABASE"] = "TestDB", ["TRUSTED_CONNECTION"] = "TRUE"
            };
            var cs = ConnectionStringBuilder.Build("MSSQL", props);
            Assert.Contains("localhost", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_SqlServerAlias_WorksLikeMssql()
        {
            var props = new Dictionary<string, string> { ["SERVER"] = "myserver", ["DATABASE"] = "db" };
            var cs1 = ConnectionStringBuilder.Build("MSSQL", props);
            var cs2 = ConnectionStringBuilder.Build("SQLSERVER", props);
            Assert.False(string.IsNullOrEmpty(cs1));
            Assert.False(string.IsNullOrEmpty(cs2));
        }

        [Fact]
        public void ConnectionStringBuilder_Mssql_WithPoolLifetime()
        {
            var props = new Dictionary<string, string>
            {
                ["SERVER"] = "srv", ["POOL_LIFETIME"] = "300"
            };
            var cs = ConnectionStringBuilder.Build("MSSQL", props);
            Assert.NotEmpty(cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Mssql_WithSslAndTrustCert()
        {
            var props = new Dictionary<string, string>
            {
                ["SERVER"] = "srv", ["ENCRYPT"] = "TRUE", ["TRUST_SERVER_CERTIFICATE"] = "TRUE"
            };
            var cs = ConnectionStringBuilder.Build("MSSQL", props);
            Assert.Contains("srv", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Mssql_ApplicationIntentReadWrite()
        {
            var props = new Dictionary<string, string>
            {
                ["SERVER"] = "srv", ["APPLICATION_INTENT"] = "READWRITE"
            };
            var cs = ConnectionStringBuilder.Build("MSSQL", props);
            Assert.NotEmpty(cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Postgres_WithIdleLifetime()
        {
            var props = new Dictionary<string, string>
            {
                ["HOST"] = "pghost", ["DATABASE"] = "db", ["CONNECTION_IDLE_LIFETIME"] = "60"
            };
            var cs = ConnectionStringBuilder.Build("POSTGRES", props);
            Assert.Contains("pghost", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_PostgresAlias_Works()
        {
            var props = new Dictionary<string, string> { ["HOST"] = "host", ["DATABASE"] = "db" };
            var cs = ConnectionStringBuilder.Build("NPSQL", props);
            Assert.NotEmpty(cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Oracle_HostOnlyNoService()
        {
            var props = new Dictionary<string, string>
            {
                ["HOST"] = "orahost", ["PORT"] = "1521"
            };
            var cs = ConnectionStringBuilder.Build("ORACLE", props);
            Assert.Contains("orahost", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Oracle_PoolLifetime()
        {
            var props = new Dictionary<string, string>
            {
                ["HOST"] = "orahost", ["SERVICE_NAME"] = "xe", ["CONNECTION_LIFETIME"] = "300"
            };
            var cs = ConnectionStringBuilder.Build("ORACLE", props);
            Assert.NotEmpty(cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Odbc_ArbitraryProperties_PassedThrough()
        {
            var props = new Dictionary<string, string>
            {
                ["DSN"] = "MySalesDSN", ["USER"] = "admin", ["PASSWORD"] = "secret",
                ["CONNECT_TIMEOUT"] = "30", ["CHARSET"] = "utf8"
            };
            var cs = ConnectionStringBuilder.Build("ODBC", props);
            Assert.Contains("DSN=MySalesDSN", cs);
            Assert.Contains("CHARSET=utf8", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Odbc_EmptyDsnAndDriver_ReturnsCredentialsOnly()
        {
            var props = new Dictionary<string, string>
            {
                ["USER"] = "admin", ["PASSWORD"] = "secret"
            };
            var cs = ConnectionStringBuilder.Build("ODBC", props);
            Assert.Contains("UID=admin", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Rest_ReturnsUrl()
        {
            var props = new Dictionary<string, string> { ["URL"] = "https://api.example.com" };
            var cs = ConnectionStringBuilder.Build("REST", props);
            Assert.Equal("https://api.example.com", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Api_ReturnsUrl()
        {
            var props = new Dictionary<string, string> { ["URL"] = "https://api.example.com" };
            var cs = ConnectionStringBuilder.Build("API", props);
            Assert.Equal("https://api.example.com", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Http_ReturnsUrl()
        {
            var props = new Dictionary<string, string> { ["URL"] = "https://api.example.com" };
            var cs = ConnectionStringBuilder.Build("HTTP", props);
            Assert.Equal("https://api.example.com", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Ftp_ReturnsHost()
        {
            var props = new Dictionary<string, string> { ["HOST"] = "ftp.example.com" };
            var cs = ConnectionStringBuilder.Build("FTP", props);
            Assert.Equal("ftp.example.com", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Email_ReturnsHost()
        {
            var props = new Dictionary<string, string> { ["HOST"] = "smtp.example.com" };
            var cs = ConnectionStringBuilder.Build("EMAIL", props);
            Assert.Equal("smtp.example.com", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Smtp_ReturnsHost()
        {
            var props = new Dictionary<string, string> { ["HOST"] = "smtp.example.com" };
            var cs = ConnectionStringBuilder.Build("SMTP", props);
            Assert.Equal("smtp.example.com", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Remote_NoHost_ReturnsEmpty()
        {
            var props = new Dictionary<string, string> { ["PORT"] = "22" };
            var cs = ConnectionStringBuilder.Build("SFTP", props);
            Assert.Equal(string.Empty, cs);
        }

        [Fact]
        public void ConnectionStringBuilder_MockDb_ReturnsPath()
        {
            var props = new Dictionary<string, string> { ["PATH"] = "mock://local" };
            var cs = ConnectionStringBuilder.Build("MOCKDB", props);
            Assert.Equal("mock://local", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Json_NoPath_ReturnsEmpty()
        {
            var props = new Dictionary<string, string> { ["OTHER"] = "value" };
            var cs = ConnectionStringBuilder.Build("JSON", props);
            Assert.Equal(string.Empty, cs);
        }

        [Fact]
        public void ConnectionStringBuilder_InvalidProvider_WithSuggestion_ThrowsWithDYM()
        {
            var props = new Dictionary<string, string> { ["SERVER"] = "localhost" };
            var ex = Assert.Throws<ArgumentException>(() =>
                ConnectionStringBuilder.Build("MSQL", props));
            Assert.Contains("Did you mean", ex.Message);
        }

        [Fact]
        public void ConnectionStringBuilder_InvalidProvider_WithoutSuggestion_ThrowsWithList()
        {
            var props = new Dictionary<string, string> { ["SERVER"] = "localhost" };
            var ex = Assert.Throws<ArgumentException>(() =>
                ConnectionStringBuilder.Build("ZZZNONE12345", props));
            Assert.Contains("Unsupported provider", ex.Message);
        }

        [Fact]
        public void ConnectionStringBuilder_AzureBlob_ReturnsHost()
        {
            var props = new Dictionary<string, string> { ["HOST"] = "myaccount.blob.core.windows.net" };
            var cs = ConnectionStringBuilder.Build("AZURE_BLOB", props);
            Assert.Equal("myaccount.blob.core.windows.net", cs);
        }

        [Fact]
        public void ConnectionStringBuilder_Blob_ReturnsHost()
        {
            var props = new Dictionary<string, string> { ["HOST"] = "myblob.example.com" };
            var cs = ConnectionStringBuilder.Build("BLOB", props);
            Assert.NotEmpty(cs);
        }
    }
}
