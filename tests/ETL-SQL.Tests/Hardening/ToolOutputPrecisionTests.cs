using System.Globalization;
using System.Reflection;
using System.Text;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

[Trait("CompatBreak", "0.20")]
public class ToolOutputPrecisionTests
{
    [Theory]
    [InlineData("9007199254740993", "BIGINT")]
    [InlineData("-9007199254740993", "BIGINT")]
    [InlineData("9223372036854775807", "BIGINT")]
    [InlineData("-9223372036854775808", "BIGINT")]
    [InlineData("79228162514264337593543950335", "BIGINT")]
    [InlineData("-79228162514264337593543950335", "BIGINT")]
    [InlineData("1234567890.1234567890123456789", "DECIMAL")]
    [InlineData("0.0000000000000000000000000001", "NUMERIC")]
    [InlineData("9.007199254740993e15", "DECIMAL")]
    public async Task OutputPreservesExactNumbers(string jsonNumber, string type)
    {
        var actual = await ReadOutputAsync(jsonNumber, type);
        Assert.Equal(decimal.Parse(jsonNumber, NumberStyles.Float, CultureInfo.InvariantCulture), Assert.IsType<decimal>(actual));
    }

    [Theory]
    [InlineData("1e100", "DOUBLE")]
    [InlineData("-1e100", "FLOAT")]
    public async Task FloatingSchemaSupportsFloatingRange(string jsonNumber, string type)
    {
        Assert.Equal(double.Parse(jsonNumber, CultureInfo.InvariantCulture), Assert.IsType<double>(await ReadOutputAsync(jsonNumber, type)));
    }

    private static async Task<object?> ReadOutputAsync(string jsonNumber, string type)
    {
        await using var provider = (Microsoft.Extensions.DependencyInjection.ServiceProvider)DependencyInjectionSetup.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<Evaluator>();
        var handler = new ExecuteToolStatementHandler(provider.GetRequiredService<ILogger>());
        var method = typeof(ExecuteToolStatementHandler).GetMethod("StreamOutputAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var input = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("{\"n\":" + jsonNumber + "}\n")));
        var task = (Task<IDataSource?>)method.Invoke(handler,
            [new TableReference("#precision"), input, evaluator,
                new List<ExpectedSchemaColumn> { new() { ColumnName = "n", DataType = type } }, CancellationToken.None])!;
        await using var output = (await task)!;
        var values = new List<object?>();
        await foreach (var batch in output.ReadBatches())
            values.AddRange(batch.Rows.Select(row => row[0]));
        return Assert.Single(values);
    }
}
