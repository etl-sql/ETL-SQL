using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Scale;

public sealed class LiveObjectScaleAssessmentTests
{
    public static TheoryData<string, int> ScaleMatrix => new()
    {
        { "connection", 10 }, { "connection", 50 }, { "connection", 100 },
        { "temp", 10 }, { "temp", 50 }, { "temp", 100 },
        { "variable", 10 }, { "variable", 50 }, { "variable", 100 },
        { "visual", 10 }, { "visual", 50 }, { "visual", 100 }
    };

    [Theory]
    [MemberData(nameof(ScaleMatrix))]
    [Trait("Category", "ScaleAssessment")]
    public async Task LiveObjectsSupportDocumentedScaleMatrix(string objectKind, int count)
    {
        await using var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        evaluator.PreviewLimit = 0;

        await evaluator.Evaluate(Parse(BuildScript(objectKind, count)));

        Assert.Equal(count, Count(evaluator, objectKind));
    }

    [Theory]
    [InlineData("connection")]
    [InlineData("temp")]
    [InlineData("variable")]
    [InlineData("visual")]
    public async Task ConfiguredLiveObjectCeilingRejectsTheNextDistinctObject(string objectKind)
    {
        await using var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        evaluator.PreviewLimit = 0;
        SetLimit(evaluator, objectKind, 2);

        var error = await Assert.ThrowsAsync<ExecutionException>(
            () => evaluator.Evaluate(Parse(BuildScript(objectKind, 3))));

        Assert.Contains("limit exceeded (2)", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Count(evaluator, objectKind));
    }

    private static Script Parse(string source) => new Parser(new Lexer(source).Tokenize(), source).Parse();

    private static string BuildScript(string objectKind, int count)
    {
        var source = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            source.AppendLine(objectKind switch
            {
                "connection" => $"CREATE CONNECTION c{i} AS MOCKDB();",
                "temp" => $"CREATE TABLE #t{i} (id INT);",
                "variable" => $"DECLARE @v{i} INT = {i};",
                "visual" => $"CREATE VISUAL v{i} AS CARD (SOURCE = (SELECT {i} AS Value));",
                _ => throw new ArgumentOutOfRangeException(nameof(objectKind))
            });
        }
        return source.ToString();
    }

    private static int Count(Evaluator evaluator, string objectKind) => objectKind switch
    {
        "connection" => evaluator.Connections.Keys.Count(name => !name.StartsWith('#')),
        "temp" => evaluator.Connections.Keys.Count(name => name.StartsWith('#')),
        "variable" => evaluator.CurrentVariables.Count,
        "visual" => evaluator.ReportContext.VisualDefinitions.Count,
        _ => throw new ArgumentOutOfRangeException(nameof(objectKind))
    };

    private static void SetLimit(Evaluator evaluator, string objectKind, int limit)
    {
        switch (objectKind)
        {
            case "connection": evaluator.MaxConnectionsPerScript = limit; break;
            case "temp": evaluator.MaxTempTablesPerScript = limit; break;
            case "variable": evaluator.MaxVariablesPerScript = limit; break;
            case "visual": evaluator.MaxVisualsPerScript = limit; break;
            default: throw new ArgumentOutOfRangeException(nameof(objectKind));
        }
    }
}
