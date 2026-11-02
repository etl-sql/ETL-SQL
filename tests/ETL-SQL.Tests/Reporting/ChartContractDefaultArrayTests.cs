using System.Collections.Immutable;
using System.IO;
using ETL_SQL.Reporting.Semantics;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class ChartContractDefaultArrayTests
{
    [Fact]
    public void ColumnWithDefaultValuesFailsAsInvalidData()
    {
        var column = new ChartColumn("c", ChartValueKind.Decimal, DataSemanticKind.Quantitative, default, default);

        var error = Assert.Throws<InvalidDataException>(() => column.Validate(1));
        Assert.Contains("'c'", error.Message);
    }

    [Fact]
    public void DataSetWithDefaultColumnsFailsAsInvalidData()
    {
        var data = ChartDataSet.Create("d", 0, default(ImmutableArray<ChartColumn>));

        Assert.Throws<InvalidDataException>(() => data.Validate());
    }
}
