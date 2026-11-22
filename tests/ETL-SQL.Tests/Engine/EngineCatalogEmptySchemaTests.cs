using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// An engine catalog view that has no rows must still report its columns, so
/// SELECT * on a fresh session returns the documented schema instead of a
/// column-less result.
/// </summary>
public class EngineCatalogEmptySchemaTests
{
    public static TheoryData<string> CatalogViews()
    {
        var data = new TheoryData<string>();
        foreach (var name in EngineCatalog.Tables.Where(EngineCatalog.TableColumns.ContainsKey))
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogViews))]
    public async Task SelectStar_OnFreshSession_ReturnsPublishedColumns(string view)
    {
        var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await eval.Evaluate(new Parser(new Lexer($"SELECT * FROM eng.{view};").Tokenize()).Parse());

        var expected = EngineCatalog.TableColumns[view].Select(c => c.Name).ToArray();
        Assert.Equal(expected, eval.LastResult!.ColumnNames);
    }
}
