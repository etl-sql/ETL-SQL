using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Reporting
{
    public class ReportSqlTests
    {
        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void TestCreateVisual_SubtitleAndSourceNoEquals()
        {
            var sql = @"
                CREATE VISUAL SalesChart AS BAR (
                    TITLE 'Global Sales Report',
                    SUBTITLE 'Q1 2026',
                    SOURCE SELECT Region, Amount FROM #SalesData
                );
            ";

            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (CreateVisualStatement)parser.ParseStatement();

            Assert.Equal("SalesChart", stmt.Name);
            Assert.Equal(VisualType.Bar, stmt.VisualType);
            Assert.Equal("'Global Sales Report'", stmt.Title.ToSql());
            Assert.Equal("'Q1 2026'", stmt.Subtitle.ToSql());
            Assert.NotNull(stmt.Source.InlineSelect);
        }

        [Fact]
        public void TestCreateVisual_SourceParenthesesNoEquals()
        {
            var sql = @"
                CREATE VISUAL SalesChart AS BAR (
                    TITLE = 'Global Sales Report',
                    SOURCE (
                        SELECT Region, SUM(Amount) AS Total FROM #SalesData GROUP BY Region
                    )
                );
            ";

            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (CreateVisualStatement)parser.ParseStatement();

            Assert.Equal("SalesChart", stmt.Name);
            Assert.Equal("'Global Sales Report'", stmt.Title.ToSql());
            Assert.NotNull(stmt.Source.InlineSelect);
            var inlineSelect = Assert.IsType<SelectStatement>(stmt.Source.InlineSelect);
            Assert.Equal("Total", inlineSelect.Columns[1].Alias);
        }

        [Fact]
        public void TestExplainInto_Serialization()
        {
            var sql = "EXPLAIN SELECT * FROM #T INTO #Plan;";
            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (ExplainStatement)parser.ParseStatement();

            Assert.NotNull(stmt.IntoTable);
            Assert.Equal("#Plan", stmt.IntoTable.TableName);

            var serialized = stmt.ToSql();
            Assert.Contains("INTO #Plan", serialized);
        }

        [Fact]
        public void CreateTheme_ParsesNameAndProperties()
        {
            var sql = @"
                CREATE THEME corporate AS (
                    BACKGROUND   = '#1a1a2e',
                    TEXT_COLOR   = '#eee',
                    ACCENT_COLOR = '#4ecca3',
                    COLORS       = '#4ecca3, #e94560, #f5a623'
                );
            ";
            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (CreateThemeStatement)parser.ParseStatement();

            Assert.Equal("corporate", stmt.Name);
            Assert.Equal(ObjectCreationMode.Create, stmt.Mode);
            Assert.Equal("#1a1a2e", stmt.Properties["BACKGROUND"]);
            Assert.Equal("#eee", stmt.Properties["TEXT_COLOR"]);
            Assert.Equal("#4ecca3", stmt.Properties["ACCENT_COLOR"]);
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void CreateTheme_BuildEChartsTheme_MapsProperties()
        {
            var props = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BACKGROUND"] = "#1a1a2e",
                ["TEXT_COLOR"] = "#eeeeee",
                ["ACCENT_COLOR"] = "#4ecca3",
                ["GRID_COLOR"] = "#333333"
            };

            var json = ETL_SQL.Engine.Handlers.CreateThemeStatementHandler.BuildEChartsTheme(props);

            Assert.Equal("#1a1a2e", json["backgroundColor"]?.GetValue<string>());
            Assert.Equal("#eeeeee", json["textStyle"]?["color"]?.GetValue<string>());
            Assert.Equal("#4ecca3", json["color"]?[0]?.GetValue<string>());
            // Axis objects should be present
            Assert.NotNull(json["categoryAxis"]);
        }

        [Fact]
        public void DropTheme_ParsesNameAndObjectType()
        {
            var sql = "DROP THEME corporate;";
            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var stmt = (DropReportObjectStatement)parser.ParseStatement();

            Assert.Equal(ReportObjectType.Theme, stmt.ObjectType);
            Assert.Equal("corporate", stmt.Name);
            Assert.False(stmt.IfExists);
        }

        // ── MAP visual parse tests (cookbook recipe 11 coverage) ─────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void MapVisual_Choropleth_ParsesBasicOptions()
        {
            var sql = @"
                CREATE VISUAL RevenueMap AS MAP (
                    SOURCE   = #state_rev,
                    MAPPINGS (REGION = State, VALUE = Revenue),
                    OPTIONS  (
                        MAP_NAME   = US_STATES,
                        COLOR_LOW  = '#e0f2fe',
                        COLOR_HIGH = '#0369a1'
                    )
                );";
            var tokens = new Lexer(sql).Tokenize();
            var stmt = (CreateVisualStatement)new Parser(tokens, sql).ParseStatement();

            Assert.Equal(VisualType.Map, stmt.VisualType);
            Assert.Contains(stmt.Mappings, m => m.Role == "REGION" && m.Column == "State");
            Assert.Contains(stmt.Mappings, m => m.Role == "VALUE" && m.Column == "Revenue");
            var opts = stmt.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("US_STATES", opts["MAP_NAME"]);
            Assert.Equal("#e0f2fe", opts["COLOR_LOW"]);
            Assert.Equal("#0369a1", opts["COLOR_HIGH"]);
        }

        [Fact]
        public void MapVisual_FipsMatchBy_ParsesCorrectly()
        {
            var sql = @"
                CREATE VISUAL IncidentMap AS MAP (
                    SOURCE   = #incidents,
                    MAPPINGS (REGION = fips_code, VALUE = incident_count),
                    OPTIONS  (
                        MAP_NAME = US_COUNTIES,
                        MATCH_BY = FIPS,
                        COLOR_LOW  = '#fef9c3',
                        COLOR_HIGH = '#b45309'
                    )
                );";
            var tokens = new Lexer(sql).Tokenize();
            var stmt = (CreateVisualStatement)new Parser(tokens, sql).ParseStatement();

            Assert.Equal(VisualType.Map, stmt.VisualType);
            var opts = stmt.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("US_COUNTIES", opts["MAP_NAME"]);
            Assert.Equal("FIPS", opts["MATCH_BY"]);
        }

        [Fact]
        public void MapVisual_PointsMode_ParsesLonLatMappings()
        {
            var sql = @"
                CREATE VISUAL CityMap AS MAP (
                    SOURCE   = #city_rev,
                    MAPPINGS (
                        LON   = longitude,
                        LAT   = latitude,
                        VALUE = Revenue,
                        LABEL = city_name
                    ),
                    OPTIONS  (
                        MAP_NAME = US_STATES,
                        MODE     = POINTS
                    )
                );";
            var tokens = new Lexer(sql).Tokenize();
            var stmt = (CreateVisualStatement)new Parser(tokens, sql).ParseStatement();

            Assert.Equal(VisualType.Map, stmt.VisualType);
            Assert.Contains(stmt.Mappings, m => m.Role == "LON" && m.Column == "longitude");
            Assert.Contains(stmt.Mappings, m => m.Role == "LAT" && m.Column == "latitude");
            Assert.Contains(stmt.Mappings, m => m.Role == "VALUE" && m.Column == "Revenue");
            Assert.Contains(stmt.Mappings, m => m.Role == "LABEL" && m.Column == "city_name");
            var opts = stmt.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("US_STATES", opts["MAP_NAME"]);
            Assert.Equal("POINTS", opts["MODE"]);
        }

        [Fact]
        public void MapVisual_MapFile_ParsesFilePath()
        {
            var sql = @"
                CREATE VISUAL ZipMap AS MAP (
                    SOURCE   = #zip_orders,
                    MAPPINGS (REGION = zip_code, VALUE = Orders),
                    OPTIONS  (
                        MAP_FILE   = 'C:\Reports\Maps\zcta.geojson',
                        MATCH_BY   = NAME,
                        COLOR_LOW  = '#f0fdf4',
                        COLOR_HIGH = '#166534'
                    )
                );";
            var tokens = new Lexer(sql).Tokenize();
            var stmt = (CreateVisualStatement)new Parser(tokens, sql).ParseStatement();

            Assert.Equal(VisualType.Map, stmt.VisualType);
            var opts = stmt.Options.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(@"C:\Reports\Maps\zcta.geojson", opts["MAP_FILE"]);
            Assert.Equal("NAME", opts["MATCH_BY"]);
        }
    }
}
