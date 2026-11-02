using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// Each EXPORT REPORT builds its report from its own .rptsql only: report objects from an earlier
/// export must not leak into a later one, and the calling script's own report state survives.
/// </summary>
public class ExportReportScopeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "etl-export-scope-" + Guid.NewGuid().ToString("N"));

    public ExportReportScopeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Q(string name) => Path.Combine(_dir, name).Replace("'", "''");

    [Fact]
    public async Task SecondExportDoesNotCarryTheFirstReportsObjects()
    {
        File.WriteAllText(Path.Combine(_dir, "first.rptsql"), """
            SET REPORT TITLE = 'First Report';
            SELECT 1 AS A INTO #First;
            CREATE VISUAL FirstOnlyVisual AS TABLE (TITLE 'First Only Visual', SOURCE = (SELECT * FROM #First));
            """);
        File.WriteAllText(Path.Combine(_dir, "second.rptsql"), """
            SELECT 2 AS B INTO #Second;
            CREATE VISUAL SecondVisual AS TABLE (TITLE 'Second Visual', SOURCE = (SELECT * FROM #Second));
            """);

        var sql = $"""
            SET REPORT TITLE = 'Outer Title';
            EXPORT REPORT '{Q("first.rptsql")}' FORMAT MARKDOWN TO '{Q("first.md")}';
            EXPORT REPORT '{Q("second.rptsql")}' FORMAT MARKDOWN TO '{Q("second.md")}';
            """;

        await using var provider = DashboardTestHelper.BuildScopeProvider();
        var evaluator = provider.GetRequiredService<Evaluator>();
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());

        var first = File.ReadAllText(Path.Combine(_dir, "first.md"));
        var second = File.ReadAllText(Path.Combine(_dir, "second.md"));
        Assert.Contains("First Only Visual", first);
        Assert.DoesNotContain("Outer Title", first);
        Assert.Contains("Second Visual", second);
        Assert.DoesNotContain("First Only Visual", second);
        Assert.DoesNotContain("First Report", second);

        Assert.Equal("Outer Title", evaluator.ReportContext.ReportTitle);
        Assert.False(evaluator.ReportContext.VisualDefinitions.ContainsKey("FirstOnlyVisual"));
    }
}
