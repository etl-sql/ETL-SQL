using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.LSP;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ETL_SQL.LanguageServer.Tests
{
    public class DesignerLspHandlerTests
    {
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
    }
}
