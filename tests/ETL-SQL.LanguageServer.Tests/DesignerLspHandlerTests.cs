using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.LSP;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.LanguageServer.Tests
{
    public class DesignerLspHandlerTests
    {
        [Theory]
        [InlineData("BAR", "\"X\": \"category\", \"Y\": \"amount\"")]
        [InlineData("LINE", "\"X\": \"category\", \"Y\": \"amount\"")]
        [InlineData("SCATTER", "\"X\": \"category\", \"Y\": \"amount\"")]
        [InlineData("PIE", "\"LABEL\": \"category\", \"VALUE\": \"amount\"")]
        [InlineData("DONUT", "\"LABEL\": \"category\", \"VALUE\": \"amount\"")]
        [InlineData("COMBO", "\"X\": \"category\", \"Y\": \"amount\", \"Y2\": \"rate\"")]
        public async Task Generate_Phase3MigratedVisuals_PreservesNamedSyntax(string visualType, string mappings)
        {
            var handler = new DesignerLspHandler(NullLogger<DesignerLspHandler>.Instance);
            var state = $$"""
                {
                  "pages": [{
                    "id": "p1", "name": "Overview", "mode": "Dashboard",
                    "visuals": [{
                      "id": "v1", "name": "chart", "type": "{{visualType}}",
                      "gridCol": 1, "gridRow": 1, "gridColSpan": 12, "gridRowSpan": 4,
                      "title": "Representative", "dataset": "stage",
                      "mappings": { {{mappings}} }, "options": {}
                    }]
                  }],
                  "datasets": []
                }
                """;

            var response = await handler.Handle(new DesignerGenerateParams { designStateJson = state }, CancellationToken.None);

            Assert.Contains($"CREATE VISUAL chart AS {visualType}", response.script);
            Assert.Contains("CREATE PAGE [Overview] AS DASHBOARD", response.script);
        }

        [Fact]
        public async Task Generate_NamelessVisuals_UsesIdFallback()
        {
            var handler = new DesignerLspHandler(NullLogger<DesignerLspHandler>.Instance);
            var designStateJson = @"{
                ""pages"": [
                    {
                        ""id"": ""p1"",
                        ""name"": ""Overview"",
                        ""mode"": ""Dashboard"",
                        ""visuals"": [
                            {
                                ""id"": ""salesBar"",
                                ""type"": ""BAR"",
                                ""title"": ""Sales Bar"",
                                ""gridCol"": 1,
                                ""gridRow"": 1,
                                ""gridColSpan"": 8,
                                ""gridRowSpan"": 6,
                                ""mappings"": { ""X"": ""Date"", ""Y"": ""total"" }
                            },
                            {
                                ""id"": ""kpiRev"",
                                ""type"": ""CARD"",
                                ""title"": ""Revenue"",
                                ""gridCol"": 9,
                                ""gridRow"": 1,
                                ""gridColSpan"": 4,
                                ""gridRowSpan"": 3,
                                ""mappings"": { ""VALUE"": ""total"" }
                            }
                        ]
                    }
                ],
                ""datasets"": []
            }";

            var response = await handler.Handle(new DesignerGenerateParams { designStateJson = designStateJson }, CancellationToken.None);

            Assert.NotNull(response.script);
            // Verify slot names and visual names fallback to IDs
            Assert.Contains("CREATE VISUAL salesBar AS BAR", response.script);
            Assert.Contains("CREATE VISUAL kpiRev AS CARD", response.script);
            Assert.Contains("salesBar", response.script);
            Assert.Contains("kpiRev", response.script);
        }

        [Fact]
        public async Task Generate_WithOriginalScript_UsesSurgicalPatcher()
        {
            const string script = """
                -- LSP must preserve this preparation byte-for-byte
                WITH prepared AS (
                    SELECT category, amount FROM source.orders
                )
                SELECT category, amount INTO #stage FROM prepared;

                CREATE VISUAL chart AS BAR (
                    TITLE = 'Before',
                    SOURCE = &data,
                    MAPPINGS (
                        -- author-owned mapping comment
                        X = category, Y = amount
                    )
                );

                CREATE PAGE [Dashboard] AS DASHBOARD (
                    LAYOUT (STRUCTURE = 'A', MAP ('A' = chart))
                );
                """;
            const string stateJson = """
                {
                  "pages": [{
                    "id": "p1", "name": "Dashboard", "mode": "Dashboard",
                    "visuals": [{
                      "id": "v1", "name": "chart", "type": "BAR",
                      "gridCol": 1, "gridRow": 1, "gridColSpan": 12, "gridRowSpan": 4,
                      "title": "After", "dataset": "data",
                      "mappings": { "X": "category", "Y": "amount" }, "options": {}
                    }]
                  }],
                  "datasets": []
                }
                """;
            var handler = new DesignerLspHandler(NullLogger<DesignerLspHandler>.Instance);

            var response = await handler.Handle(new DesignerGenerateParams
            {
                designStateJson = stateJson,
                script = script
            }, CancellationToken.None);

            Assert.StartsWith(script[..script.IndexOf("CREATE VISUAL", System.StringComparison.Ordinal)], response.script);
            Assert.Contains("TITLE = 'After'", response.script);
            Assert.Contains("-- author-owned mapping comment", response.script);
            Assert.DoesNotContain("-- Generated by ETL-SQL Report Designer", response.script);
        }

        [Fact]
        public async Task Generate_WithInvalidIntermediateScript_ReturnsOriginal()
        {
            const string invalid = "CREATE VISUAL chart AS BAR (SOURCE = &data, MAPPINGS (X = ));";
            const string stateJson = """
                { "pages": [], "datasets": [] }
                """;
            var handler = new DesignerLspHandler(NullLogger<DesignerLspHandler>.Instance);

            var response = await handler.Handle(new DesignerGenerateParams
            {
                designStateJson = stateJson,
                script = invalid
            }, CancellationToken.None);

            Assert.Equal(invalid, response.script);
        }
    }
}
