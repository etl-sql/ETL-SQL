using System.IO.Compression;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("CompatBreak", "0.20.0")]
[Trait("Connector", "EXCEL")]
public sealed class ExcelStreamingRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedSheetDoesNotMaterializeUnreadableUnselectedSheet(bool explicitSheet)
    {
        using var workbook = new Workbook(corruptSecondSheet: true);
        var options = new Dictionary<string, string>();
        if (explicitSheet) options["SHEET"] = "Data";
        await using var source = new ExcelDataSource(SystemExecutionContext.Instance, workbook.Path, options);
        Assert.Equal(new[] { "label", "value" }, await source.GetColumnsAsync());
        var batches = await source.ReadBatches(1).ToListAsync();
        Assert.Equal(2, batches.Count);
        Assert.All(batches, batch => Assert.Single(batch.Rows));
        Assert.Equal(new[] { "One", "Two" }, batches.SelectMany(batch => batch.Rows).Select(row => row["label"]));
    }

    [Theory]
    [InlineData("ON", "A1:B3", "label", "value")]
    [InlineData("OFF", "A2:B3", "Column1", "Column2")]
    public async Task RangeHeadersAndBatchBoundariesPreserveRows(string header, string range, string label, string value)
    {
        using var workbook = new Workbook();
        var options = new Dictionary<string, string> { ["HEADER"] = header, ["RANGE"] = range };
        await using var source = new ExcelDataSource(SystemExecutionContext.Instance, workbook.Path, options);
        var batches = await source.ReadBatches(1).ToListAsync();
        Assert.Equal(2, batches.Count);
        Assert.Equal(new[] { label, value }, batches[0].ColumnNames);
        Assert.Equal(new[] { "One", "Two" }, batches.SelectMany(batch => batch.Rows).Select(row => row[label]));
        Assert.Equal(new[] { "1", "2" }, batches.SelectMany(batch => batch.Rows).Select(row => row[value]?.ToString()));
    }

    [Theory]
    [InlineData("Other", 1)]
    [InlineData("Missing", 0)]
    public async Task SheetSelectionDoesNotFallBackToAnotherSheet(string sheet, int count)
    {
        using var workbook = new Workbook();
        await using var source = new ExcelDataSource(SystemExecutionContext.Instance, workbook.Path,
            new Dictionary<string, string> { ["SHEET"] = sheet });
        var batches = await source.ReadBatches(1).ToListAsync();
        Assert.Equal(count, batches.Count);
        if (count > 0) Assert.Equal("Other", batches[0].Rows[0]["label"]);
    }

    [Fact]
    public async Task CancellationBetweenBatchesRemainsCancellation()
    {
        using var workbook = new Workbook();
        using var cancellation = new CancellationTokenSource();
        await using var source = new ExcelDataSource(SystemExecutionContext.Instance, workbook.Path);
        await using var enumerator = source.ReadBatches(1, cancellation.Token).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Theory]
    [InlineData("ON", "A1:B5", 2)]
    [InlineData("OFF", "A4:B5", 1)]
    public async Task TrailingEmptyRowsDoNotChangeUsedRange(string header, string range, int count)
    {
        using var workbook = new Workbook(trailingEmptyRows: true);
        await using var source = new ExcelDataSource(SystemExecutionContext.Instance, workbook.Path,
            new Dictionary<string, string> { ["HEADER"] = header, ["RANGE"] = range });
        var batches = await source.ReadBatches(1).ToListAsync();
        Assert.Equal(count, batches.Sum(batch => batch.Rows.Count));
        if (header == "OFF") Assert.Equal("Two", batches[0].Rows[0]["Column1"]);
    }

    private sealed class Workbook : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "etlsql-streaming-" + Guid.NewGuid().ToString("N") + ".xlsx");

        public Workbook(bool corruptSecondSheet = false, bool trailingEmptyRows = false)
        {
            using var archive = ZipFile.Open(Path, ZipArchiveMode.Create);
            Add("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Add("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Add("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Data" sheetId="1" r:id="rId1"/><sheet name="Other" sheetId="2" r:id="rId2"/></sheets>
                </workbook>
                """);
            Add("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
                </Relationships>
                """);
            Add("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="A1:B3"/><sheetData>
                  <row r="1"><c r="A1" t="inlineStr"><is><t>label</t></is></c><c r="B1" t="inlineStr"><is><t>value</t></is></c></row>
                  <row r="2"><c r="A2" t="inlineStr"><is><t>One</t></is></c><c r="B2"><v>1</v></c></row>
                  <row r="3"><c r="A3" t="inlineStr"><is><t>Two</t></is></c><c r="B3"><v>2</v></c></row>
                </sheetData></worksheet>
                """.Replace("</sheetData>", trailingEmptyRows ? "<row r=\"4\"/><row r=\"5\"/></sheetData>" : "</sheetData>"));
            Add("xl/worksheets/sheet2.xml", corruptSecondSheet ? "<invalid" : """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="A1:A2"/><sheetData>
                  <row r="1"><c r="A1" t="inlineStr"><is><t>label</t></is></c></row>
                  <row r="2"><c r="A2" t="inlineStr"><is><t>Other</t></is></c></row>
                </sheetData></worksheet>
                """);

            void Add(string name, string xml)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(xml);
            }
        }

        public void Dispose() => File.Delete(Path);
    }
}
