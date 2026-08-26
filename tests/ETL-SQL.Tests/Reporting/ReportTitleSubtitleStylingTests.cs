using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.ReportHosting;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    public class ReportTitleSubtitleStylingTests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }

        [Fact]
        public void ParseCreateVisual_TitleAndSubtitleBlocks_ParsesTypographyAndColors()
        {
            const string sql = @"
CREATE VISUAL SalesChart AS BAR (
    SOURCE = #sales,
    TITLE (
        TEXT = 'Revenue by Region',
        COLOR = '#dc2626',
        SIZE = '18px',
        WEIGHT = BOLD,
        ALIGN = CENTER
    ),
    SUBTITLE (
        TEXT = 'USD in thousands',
        COLOR = '#64748b',
        SIZE = '12px',
        ALIGN = CENTER
    )
);
";
            var script = Parse(sql);
            Assert.Empty(script.Diagnostics);
            var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

            Assert.NotNull(stmt.TitleDefinition);
            Assert.Equal("'Revenue by Region'", stmt.TitleDefinition!.Text?.ToSql());
            Assert.Equal("#dc2626", stmt.TitleDefinition.Color);
            Assert.Equal("18px", stmt.TitleDefinition.Size);
            Assert.Equal("BOLD", stmt.TitleDefinition.Weight);
            Assert.Equal("CENTER", stmt.TitleDefinition.Align);

            Assert.NotNull(stmt.SubtitleDefinition);
            Assert.Equal("'USD in thousands'", stmt.SubtitleDefinition!.Text?.ToSql());
            Assert.Equal("#64748b", stmt.SubtitleDefinition.Color);
            Assert.Equal("12px", stmt.SubtitleDefinition.Size);
            Assert.Equal("CENTER", stmt.SubtitleDefinition.Align);

            // Verify AST Serializer Round-Trip
            var serialized = stmt.ToSql();
            var reparsed = Parse(serialized).Statements.OfType<CreateVisualStatement>().Single();
            Assert.NotNull(reparsed.TitleDefinition);
            Assert.Equal(stmt.TitleDefinition.Color, reparsed.TitleDefinition!.Color);
            Assert.Equal(stmt.TitleDefinition.Size, reparsed.TitleDefinition.Size);
            Assert.Equal(stmt.TitleDefinition.Weight, reparsed.TitleDefinition.Weight);
            Assert.Equal(stmt.TitleDefinition.Align, reparsed.TitleDefinition.Align);

            Assert.NotNull(reparsed.SubtitleDefinition);
            Assert.Equal(stmt.SubtitleDefinition.Color, reparsed.SubtitleDefinition!.Color);
            Assert.Equal(stmt.SubtitleDefinition.Size, reparsed.SubtitleDefinition.Size);
            Assert.Equal(stmt.SubtitleDefinition.Align, reparsed.SubtitleDefinition.Align);
        }

        [Fact]
        public void ParseCreateVisual_TitleMarkdownAndSimpleSyntax_Preserved()
        {
            const string sql = @"
CREATE VISUAL SimpleVisual AS TABLE (
    SOURCE = #data,
    TITLE = 'Plain Title',
    SUBTITLE = ('# Markdown Subtitle')
);
";
            var script = Parse(sql);
            Assert.Empty(script.Diagnostics);
            var stmt = script.Statements.OfType<CreateVisualStatement>().Single();

            Assert.Equal("'Plain Title'", stmt.Title?.ToSql());
            Assert.False(stmt.TitleIsMarkdown);

            Assert.Equal("'# Markdown Subtitle'", stmt.Subtitle?.ToSql());
            Assert.True(stmt.SubtitleIsMarkdown);

            var serialized = stmt.ToSql();
            var reparsed = Parse(serialized).Statements.OfType<CreateVisualStatement>().Single();
            Assert.Equal(stmt.Title?.ToSql(), reparsed.Title?.ToSql());
            Assert.Equal(stmt.Subtitle?.ToSql(), reparsed.Subtitle?.ToSql());
            Assert.Equal(stmt.SubtitleIsMarkdown, reparsed.SubtitleIsMarkdown);
        }

        [Fact]
        public void ParseCreatePageAndContainer_TitleAndSubtitleBlocks_Parsed()
        {
            const string sql = @"
CREATE CONTAINER FilterPanel AS BOX (
    TITLE (
        TEXT = 'Filters',
        COLOR = '#334155',
        SIZE = '16px',
        ALIGN = LEFT
    ),
    LAYOUT ( STRUCTURE = '[A]' )
);

CREATE PAGE Dashboard AS DASHBOARD (
    TITLE (
        TEXT = 'Executive Dashboard',
        COLOR = '#0f172a',
        SIZE = '24px',
        WEIGHT = BOLD,
        ALIGN = CENTER
    ),
    SUBTITLE (
        TEXT = 'Q1 2026',
        COLOR = '#64748b'
    ),
    STRUCTURE = 'A',
    MAP('A' = FilterPanel)
);
";
            var script = Parse(sql);
            Assert.Empty(script.Diagnostics);

            var cStmt = script.Statements.OfType<CreateContainerStatement>().Single();
            Assert.NotNull(cStmt.TitleDefinition);
            Assert.Equal("'Filters'", cStmt.TitleDefinition!.Text?.ToSql());
            Assert.Equal("#334155", cStmt.TitleDefinition.Color);
            Assert.Equal("16px", cStmt.TitleDefinition.Size);
            Assert.Equal("LEFT", cStmt.TitleDefinition.Align);

            var pStmt = script.Statements.OfType<CreatePageStatement>().Single();
            Assert.NotNull(pStmt.TitleDefinition);
            Assert.Equal("'Executive Dashboard'", pStmt.TitleDefinition!.Text?.ToSql());
            Assert.Equal("#0f172a", pStmt.TitleDefinition.Color);
            Assert.Equal("24px", pStmt.TitleDefinition.Size);
            Assert.Equal("BOLD", pStmt.TitleDefinition.Weight);
            Assert.Equal("CENTER", pStmt.TitleDefinition.Align);

            Assert.NotNull(pStmt.SubtitleDefinition);
            Assert.Equal("'Q1 2026'", pStmt.SubtitleDefinition!.Text?.ToSql());
            Assert.Equal("#64748b", pStmt.SubtitleDefinition.Color);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task EndToEnd_TitleTypographyAndStyles_PropagateToManifest()
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"title_style_test_{Guid.NewGuid()}.rptsql");
            File.WriteAllText(scriptPath, @"
SELECT 'North' AS Region, 1500 AS Sales INTO #sales;

CREATE STYLE CorpTheme AS (
    TITLE_COLOR = '#000000',
    TITLE_SIZE = '14px',
    SUBTITLE_COLOR = '#94a3b8'
);

CREATE VISUAL RevenueChart AS BAR (
    STYLE = CorpTheme,
    SOURCE = (SELECT * FROM #sales),
    TITLE (
        TEXT = 'Regional Revenue',
        COLOR = '#dc2626',
        SIZE = '18px',
        WEIGHT = BOLD,
        ALIGN = CENTER
    ),
    SUBTITLE (
        TEXT = 'USD thousands',
        SIZE = '11px'
    ),
    MAPPINGS (X = Region, Y = Sales)
);

CREATE CONTAINER SummaryBox AS BOX (
    TITLE (
        TEXT = 'Summary Box',
        COLOR = '#334155',
        SIZE = '16px',
        ALIGN = LEFT
    ),
    LAYOUT (
        STRUCTURE = '[A]',
        MAP('A' = RevenueChart)
    )
);

CREATE PAGE MainDashboard AS DASHBOARD (
    TITLE (
        TEXT = 'Main Dashboard',
        COLOR = '#1e293b',
        SIZE = '24px',
        ALIGN = CENTER
    ),
    SUBTITLE (
        TEXT = 'Fiscal 2026',
        COLOR = '#64748b'
    ),
    STRUCTURE = 'A',
    MAP('A' = SummaryBox)
);
");

            try
            {
                await using var service = new DashboardService(scriptPath, DashboardTestHelper.CreateMockScopeFactory());
                var manifest = await service.GetManifestAsync();

                // Verify Visual
                var visual = manifest.Visuals.Single(v => v.Name == "RevenueChart");
                Assert.Equal("Regional Revenue", visual.Options["title"]);
                Assert.Equal("USD thousands", visual.Options["subtitle"]);
                Assert.NotNull(visual.Styles);
                Assert.Equal("#dc2626", visual.Styles!["TITLE_COLOR"]); // Overridden by TITLE (...)
                Assert.Equal("18px", visual.Styles["TITLE_SIZE"]);      // Overridden by TITLE (...)
                Assert.Equal("BOLD", visual.Styles["TITLE_WEIGHT"]);
                Assert.Equal("CENTER", visual.Styles["TITLE_ALIGN"]);
                Assert.Equal("#94a3b8", visual.Styles["SUBTITLE_COLOR"]); // Inherited from CorpTheme
                Assert.Equal("11px", visual.Styles["SUBTITLE_SIZE"]);     // Overridden by SUBTITLE (...)

                // Verify Container
                var container = manifest.Containers?.Single(c => c.Name == "SummaryBox");
                Assert.NotNull(container);
                Assert.Equal("Summary Box", container!.Title);
                Assert.NotNull(container.Styles);
                Assert.Equal("#334155", container.Styles!["TITLE_COLOR"]);
                Assert.Equal("16px", container.Styles["TITLE_SIZE"]);
                Assert.Equal("LEFT", container.Styles["TITLE_ALIGN"]);

                // Verify Page
                var page = manifest.Pages.Single(p => p.Name == "MainDashboard");
                Assert.Equal("Main Dashboard", page.Title);
                Assert.Equal("Fiscal 2026", page.Subtitle);
                Assert.NotNull(page.Styles);
                Assert.Equal("#1e293b", page.Styles!["TITLE_COLOR"]);
                Assert.Equal("24px", page.Styles["TITLE_SIZE"]);
                Assert.Equal("CENTER", page.Styles["TITLE_ALIGN"]);
                Assert.Equal("#64748b", page.Styles["SUBTITLE_COLOR"]);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
        }
    }
}
