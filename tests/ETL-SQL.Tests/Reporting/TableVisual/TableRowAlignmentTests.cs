using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.ReportHosting;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting.TableVisual;

/// <summary>
/// TABLE visual defects from the 2026-11-01 review: DEFAULT_SORT reordering rows but not the
/// row-parallel arrays, summaries naming a mapped display column, and culture-dependent numbers.
/// </summary>
public class TableRowAlignmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TableRowAlign-" + Guid.NewGuid().ToString("N"));

    public TableRowAlignmentTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<VisualManifest> BuildAsync(string script, string visual = "T")
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".rptsql");
        File.WriteAllText(path, script);
        await using var service = new DashboardService(path, DashboardTestHelper.CreateMockScopeFactory());
        var manifest = await service.GetManifestAsync();
        return manifest.Visuals.Single(v => v.Name == visual);
    }

    [Fact]
    public async Task DefaultSortKeepsRowStylesWithTheirRows()
    {
        var table = await BuildAsync("""
            SELECT 'North' AS Region, 100 AS Amount INTO #Sales;
            INSERT INTO #Sales (Region, Amount) VALUES ('South', -5);
            CREATE VISUAL T AS TABLE (
                SOURCE = (SELECT Region, Amount FROM #Sales),
                FORMATTING (
                    Amount < 0 THEN '#ef4444',
                    Amount >= 100 THEN '#22c55e'
                ),
                OPTIONS (DEFAULT_SORT = (Amount ASC))
            );
            """);

        Assert.Equal("South", table.Rows[0][0]);
        Assert.Equal(new[] { "#ef4444", "#22c55e" }, table.RowStyles);
    }

    [Fact]
    public async Task DefaultSortKeepsRowDetailKeysWithTheirRows()
    {
        var table = await BuildAsync("""
            SELECT 1 AS ParentId, 'Row1' AS Label INTO #Parents;
            INSERT INTO #Parents VALUES (2, 'Row2');
            CREATE VISUAL T AS TABLE (
                SOURCE = (SELECT * FROM #Parents),
                ROW_DETAIL (TARGET = 'C', MAP (@pid = ParentId)),
                OPTIONS (DEFAULT_SORT = (ParentId DESC))
            );
            SELECT 1 AS ChildId, 1 AS PId INTO #Children;
            CREATE VISUAL C AS TABLE (SOURCE = (SELECT * FROM #Children WHERE PId = @pid), OPTIONS (VISIBLE = OFF));
            """);

        Assert.Equal("2", table.Rows[0][0]);
        Assert.NotNull(table.RowDetailKeys);
        Assert.Equal(2m, Convert.ToDecimal(table.RowDetailKeys![0].Values.Single()));
        Assert.Equal(1m, Convert.ToDecimal(table.RowDetailKeys[1].Values.Single()));
    }

    [Fact]
    public async Task SummaryCanNameTheMappedDisplayColumn()
    {
        var table = await BuildAsync("""
            SELECT 'A' AS Cat, 10 AS Val INTO #D;
            INSERT INTO #D VALUES ('B', 20);
            CREATE VISUAL T AS TABLE (
                SOURCE = #D,
                MAPPINGS (COLUMN Cat AS 'Category', COLUMN Val AS 'Revenue'),
                SUMMARY (SUM(Revenue) AS Total)
            );
            """);

        Assert.NotNull(table.SummaryData);
        var total = Assert.Single(table.SummaryData!.Aggregates);
        Assert.Equal("30", total.Value);
    }

    [Fact]
    public async Task NumbersAreCultureInvariantUnderAGermanLocale()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var table = await BuildAsync("""
                -- Integer literals only: decimal literal parsing is culture-sensitive in the lexer (separate defect).
                SELECT 'A' AS Cat, CAST(975 AS FLOAT) / 100 AS Val INTO #D;
                INSERT INTO #D VALUES ('B', CAST(1050 AS FLOAT) / 100);
                INSERT INTO #D VALUES ('C', CAST(100025 AS FLOAT) / 100);
                CREATE VISUAL T AS TABLE (
                    SOURCE = #D,
                    SUMMARY (SUM(Val) AS Total, MAX(Val) AS Biggest),
                    OPTIONS (DEFAULT_SORT = (Val ASC))
                );
                """);

            Assert.Equal(new[] { "A", "B", "C" }, table.Rows.Select(r => r[0]));
            Assert.Equal("9.75", table.Rows[0][1]);
            Assert.Equal(1020.5m, decimal.Parse(table.SummaryData!.Aggregates[0].Value!, CultureInfo.InvariantCulture));
            Assert.Equal("1000.25", table.SummaryData.Aggregates[1].Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
