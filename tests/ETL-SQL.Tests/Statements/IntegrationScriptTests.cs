using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// Runs the self-checking scripts in tests/Integration. Each one THROWs when a check fails. No
/// runner picked them up, so OutputClauseTests.etlsql sat red over the MERGE $action defect
/// without anyone seeing it.
/// </summary>
public sealed class IntegrationScriptTests
{
    [Theory]
    [InlineData("DateDiffPrecisionTests.etlsql")]
    [InlineData("OutputClauseTests.etlsql")]
    [InlineData("StandardFunctionTests.etlsql")]
    public async Task SelfCheckingScriptPasses(string fileName)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(IntegrationDirectory(), fileName));
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
    }

    [Fact]
    public void EveryIntegrationScriptIsListed()
    {
        var listed = new[] { "DateDiffPrecisionTests.etlsql", "OutputClauseTests.etlsql", "StandardFunctionTests.etlsql" };
        var present = Array.ConvertAll(Directory.GetFiles(IntegrationDirectory(), "*.etlsql"), Path.GetFileName);
        Array.Sort(present, StringComparer.Ordinal);
        Assert.Equal(listed, present);
    }

    private static string IntegrationDirectory([CallerFilePath] string sourceFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "tests", "Integration");
    }
}
