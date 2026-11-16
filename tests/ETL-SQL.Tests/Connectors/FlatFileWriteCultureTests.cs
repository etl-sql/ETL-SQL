using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// A flat file written on any host holds the same text. The writer formatted each cell with the
/// host culture, so Windows en-US wrote '1/1/2026 2:30:05 PM' and a German host wrote '1,5' into a
/// comma-delimited file, while the reader parsed with the invariant culture (or CULTURE). Cells are
/// now the CAST AS STRING text unless the connection names a CULTURE or DATE_FORMAT.
/// </summary>
public sealed class FlatFileWriteCultureTests
{
    private const string Row = "SELECT CAST('2026-01-01 14:30:05' AS DATETIME) AS AtTime, CAST(1.5 AS FLOAT) AS Ratio, 2.25 AS Amount";

    private static async Task<string> WriteUnder(string culture, string options)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ETL_SQL_FlatCulture_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "out.csv");
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var sql = $"CREATE CONNECTION c AS FLATFILE('{path.Replace("\\", "\\\\")}', HEADER=ON{options}); {Row} INTO c.FILE;";
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
            return (await File.ReadAllLinesAsync(path))[1];
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public async Task CellsAreInvariantByDefault(string culture) =>
        Assert.Equal("2026-01-01 14:30:05,1.5,2.25", await WriteUnder(culture, ""));

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public async Task ACultureOptionFormatsCellsInThatCulture(string culture) =>
        Assert.Equal("01.01.2026 14:30:05;1,5;2,25", await WriteUnder(culture, ", DELIMITER=';', CULTURE='de-DE'"));

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public async Task ADateFormatOptionFormatsDates(string culture) =>
        Assert.Equal("01/01/2026,1.5,2.25", await WriteUnder(culture, ", DATE_FORMAT='dd/MM/yyyy'"));
}
