using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class ParameterScannerTests
{
    [Fact]
    public void Scan_FindsVariablesAndParametersThroughNestedAst()
    {
        var expression = new FunctionCallExpression("COALESCE", new List<Expression>
        {
            new VariableExpression("Region"),
            new ParameterExpression("?1", 1)
        });

        var parameters = ParameterScanner.Scan(expression);

        Assert.Contains("Region", parameters);
        Assert.Contains("?1", parameters);
    }
}
