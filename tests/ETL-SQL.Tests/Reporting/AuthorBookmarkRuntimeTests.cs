using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Reporting;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

/// <summary>
/// End-to-end coverage that runs real report scripts through the production Evaluator + ManifestBuilder,
/// and exercises the real bookmark/drop handlers against the real ReportContext.
/// </summary>
public class AuthorBookmarkRuntimeTests
{
    private static Evaluator NewEvaluator()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.RedirectOutput = true;
        evaluator.DisplayExecuteTree = false;
        return evaluator;
    }

    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    [Fact]
    public async Task Handler_RejectsDuplicateBookmarkIdentifier()
    {
        var evaluator = NewEvaluator();
        var handler = new CreateBookmarkStatementHandler(NullLogger.Instance);
        var first = (CreateBookmarkStatement)Parse("CREATE BOOKMARK A AS (PAGE = Main);").Statements[0];
        var second = (CreateBookmarkStatement)Parse("CREATE BOOKMARK A AS (PAGE = Detail);").Statements[0];

        await handler.Execute(first, evaluator);
        var ex = await Assert.ThrowsAsync<ExecutionException>(() => handler.Execute(second, evaluator));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task Handler_RejectsSecondDefaultBookmark()
    {
        var evaluator = NewEvaluator();
        var handler = new CreateBookmarkStatementHandler(NullLogger.Instance);
        var first = (CreateBookmarkStatement)Parse("CREATE BOOKMARK A AS (PAGE = Main, DEFAULT = ON);").Statements[0];
        var second = (CreateBookmarkStatement)Parse("CREATE BOOKMARK B AS (PAGE = Main, DEFAULT = ON);").Statements[0];

        await handler.Execute(first, evaluator);
        var ex = await Assert.ThrowsAsync<ExecutionException>(() => handler.Execute(second, evaluator));
        Assert.Contains("DEFAULT", ex.Message);
    }

    [Fact]
    public async Task Manifest_BuildsTypedEnvelopeFromBookmarks()
    {
        var evaluator = NewEvaluator();
        var script = Parse("""
            DECLARE @region VARCHAR INPUT = 'All';
            DECLARE @year INT INPUT = 2026;
            SELECT 'West' AS region, 2026 AS year INTO #sales;
            CREATE VISUAL DetailTable AS TABLE (SOURCE = (SELECT * FROM #sales));
            CREATE CONTAINER FilterPanel AS BOX (LAYOUT (STRUCTURE = 'A', MAP ('A' = DetailTable)));
            CREATE PAGE Detail AS DASHBOARD (LAYOUT (STRUCTURE = 'A', MAP ('A' = DetailTable)));
            CREATE BOOKMARK WestCoast AS (
                TITLE = 'West Coast',
                PARAMETERS (@region = 'West', @year = 2026),
                PAGE = Detail,
                STATE (FilterPanel.COLLAPSED = ON, DetailTable.VISIBLE = OFF),
                DEFAULT = ON
            );
            """);
        await evaluator.Evaluate(script);
        var manifest = await new ManifestBuilder(evaluator).BuildAsync("bm.rptsql");

        var bm = Assert.Single(manifest.Bookmarks!);
        Assert.Equal("WestCoast", bm.Name);
        Assert.Equal("West Coast", bm.Title);
        Assert.True(bm.IsDefault);
        Assert.Equal("Detail", bm.State.ActivePage);
        Assert.Equal(ReportStateValueKind.String, bm.State.Parameters["@region"].Kind);
        Assert.Equal(ReportStateValueKind.Number, bm.State.Parameters["@year"].Kind);
        Assert.Equal(2026m, bm.State.Parameters["@year"].NumberValue);
        Assert.True(bm.State.Collapsed["FilterPanel"]);
        Assert.False(bm.State.Visible["DetailTable"]);

        // The serialized envelope carries the number as a JSON number, never a quoted string.
        var json = bm.State.ToJson();
        Assert.Contains("\"@year\":2026", json);
        Assert.DoesNotContain("\"@year\":\"2026\"", json);
    }

    [Fact]
    public async Task DropBookmark_RemovesItFromManifest()
    {
        var evaluator = NewEvaluator();
        var script = Parse("""
            CREATE BOOKMARK Temp AS (PAGE = Main);
            DROP BOOKMARK Temp;
            """);
        await evaluator.Evaluate(script);
        var manifest = await new ManifestBuilder(evaluator).BuildAsync("bm.rptsql");
        Assert.True(manifest.Bookmarks == null || manifest.Bookmarks.Count == 0);
    }

    [Fact]
    public async Task DropBookmarkIfExists_OnMissing_DoesNotThrow()
    {
        var evaluator = NewEvaluator();
        var script = Parse("DROP BOOKMARK IF EXISTS NeverCreated;");
        await evaluator.Evaluate(script); // must not throw
    }

    [Fact]
    public void ProductionSample_Parses()
    {
        var path = Path.Combine(FindRepoRoot(), "samples", "08_Reporting", "author_bookmarks.rptsql");
        Assert.True(File.Exists(path), $"Sample not found at {path}");
        var sql = File.ReadAllText(path);
        var script = Parse(sql);
        Assert.Contains(script.Statements, s => s is CreateBookmarkStatement);
        Assert.Contains(script.Statements, s => s is CreateContainerStatement);
    }

    [Fact]
    public async Task ProductionSample_Executes()
    {
        var path = Path.Combine(FindRepoRoot(), "samples", "08_Reporting", "author_bookmarks.rptsql");
        var sql = File.ReadAllText(path);
        var evaluator = NewEvaluator();
        await evaluator.Evaluate(Parse(sql));
        var manifest = await new ManifestBuilder(evaluator).BuildAsync(path);
        Assert.NotNull(manifest.Bookmarks);
        Assert.Equal(3, manifest.Bookmarks!.Count);
        Assert.Single(manifest.Bookmarks!, b => b.IsDefault);
    }

    private static string FindRepoRoot()
    {
        var current = System.AppDomain.CurrentDomain.BaseDirectory;
        while (current != null)
        {
            if (File.Exists(Path.Combine(current, "ETL-SQL.slnx"))) return current;
            current = Path.GetDirectoryName(current);
        }
        throw new DirectoryNotFoundException("Could not locate repository root containing ETL-SQL.slnx.");
    }
}
