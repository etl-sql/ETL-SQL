using System.Collections.Generic;
using System.Text.Json;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public class ResultFormatterStreamingTests
{
    [Fact]
    public async Task FormatJsonAsync_StreamsRowsAcrossBatches()
    {
        var json = await ResultFormatter.FormatJsonAsync(
            CreateBatches(3, 2),
            ForMode.PATH,
            "Payload",
            includeNulls: false,
            withoutArrayWrapper: false);

        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("Payload");
        Assert.Equal(6, rows.GetArrayLength());
        Assert.Equal(1, rows[0].GetProperty("Id").GetInt32());
        Assert.Equal(6, rows[5].GetProperty("Id").GetInt32());
    }

    [Fact]
    public async Task FormatXmlAsync_StreamsRowsAcrossBatches()
    {
        var xml = await ResultFormatter.FormatXmlAsync(
            CreateBatches(2, 2),
            ForMode.RAW,
            "Payload",
            includeNulls: false,
            useElements: false);

        Assert.Contains("<Payload>", xml);
        Assert.Equal(4, CountOccurrences(xml, "<row "));
        Assert.Contains("Id=\"4\"", xml);
    }

    private static async IAsyncEnumerable<DataTable> CreateBatches(int batchCount, int rowsPerBatch)
    {
        var id = 1;
        for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            var table = new DataTable();
            table.SetColumns(new[] { "Id", "Name" });
            for (var rowIndex = 0; rowIndex < rowsPerBatch; rowIndex++)
            {
                var row = table.NewRow();
                row["Id"] = id;
                row["Name"] = $"Name {id}";
                await table.AddRowAsync(row);
                id++;
            }

            yield return table;
        }
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
