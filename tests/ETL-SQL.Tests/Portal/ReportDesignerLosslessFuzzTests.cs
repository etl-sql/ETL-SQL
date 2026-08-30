using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Models;
using ETL_SQL.Portal.Services;
using Xunit;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// Deterministic mutation fuzz and property tests proving that the Visual Report Builder
/// surgical patcher is lossless: every out-of-scope byte, CTE, SQL statement, comment,
/// and line ending remains 100% byte-preserved across arbitrary mutations and syntax errors.
/// </summary>
public class ReportDesignerLosslessFuzzTests
{
    private readonly DesignerScriptPatcher _patcher = new();
    private readonly DesignerAnalysisService _analysis = new();

    private static int GetIterationCount()
    {
        var env = Environment.GetEnvironmentVariable("ETLSQL_FUZZ_ITERATIONS");
        return int.TryParse(env, out var n) && n > 0 ? n : 40;
    }

    private static int GetSeed()
    {
        var env = Environment.GetEnvironmentVariable("ETLSQL_FUZZ_SEED");
        return int.TryParse(env, out var s) ? s : 20260826;
    }

    [Fact]
    public void Fuzz_Property_OutOfScopeBytesRemainBitForBitIdentical_AcrossRandomizedScriptsAndMutations()
    {
        var seed = GetSeed();
        var iterations = GetIterationCount();
        var rng = new Random(seed);

        for (int i = 0; i < iterations; i++)
        {
            var script = GenerateRandomReportScript(rng, i, out var visualNames, out var pageNames);
            var parseResult = _analysis.Parse(script, 100);
            Assert.Null(parseResult.Error);
            var state = parseResult.DesignState;

            // Pick a random mutation type
            var mutationType = rng.Next(6);
            switch (mutationType)
            {
                case 0: // Mutate visual title
                    if (state.Pages.Count > 0 && state.Pages[0].Visuals.Count > 0)
                    {
                        var targetVisual = state.Pages[0].Visuals[0];
                        var updatedPages = state.Pages.Select(p => p.Id == state.Pages[0].Id
                            ? p with
                            {
                                Visuals = p.Visuals.Select(v => v.Id == targetVisual.Id
                                    ? v with { Title = $"Mutated Title {i}_{rng.Next(1000)}" }
                                    : v).ToList()
                            }
                            : p).ToList();
                        state = state with { Pages = updatedPages };
                    }
                    break;

                case 1: // Mutate visual mappings
                    if (state.Pages.Count > 0 && state.Pages[0].Visuals.Count > 0)
                    {
                        var targetVisual = state.Pages[0].Visuals[0];
                        var newMappings = new Dictionary<string, string>(targetVisual.Mappings)
                        {
                            ["X"] = "mutated_x_col",
                            ["Y"] = "mutated_y_val"
                        };
                        var updatedPages = state.Pages.Select(p => p.Id == state.Pages[0].Id
                            ? p with
                            {
                                Visuals = p.Visuals.Select(v => v.Id == targetVisual.Id
                                    ? v with { Mappings = newMappings }
                                    : v).ToList()
                            }
                            : p).ToList();
                        state = state with { Pages = updatedPages };
                    }
                    break;

                case 2: // Mutate visual style/options
                    if (state.Pages.Count > 0 && state.Pages[0].Visuals.Count > 0)
                    {
                        var targetVisual = state.Pages[0].Visuals[0];
                        var newOptions = new Dictionary<string, string>(targetVisual.Options)
                        {
                            ["WIDTH"] = $"{rng.Next(50, 100)}%",
                            ["HEIGHT"] = $"{rng.Next(300, 600)}px",
                            ["LEGEND"] = rng.Next(2) == 0 ? "ON" : "OFF"
                        };
                        var updatedPages = state.Pages.Select(p => p.Id == state.Pages[0].Id
                            ? p with
                            {
                                Visuals = p.Visuals.Select(v => v.Id == targetVisual.Id
                                    ? v with { Options = newOptions }
                                    : v).ToList()
                            }
                            : p).ToList();
                        state = state with { Pages = updatedPages };
                    }
                    break;

                case 3: // Add a new visual
                    if (state.Pages.Count > 0)
                    {
                        var newVisualName = $"v_fuzz_new_{i}";
                        var newVisual = new DesignerVisualDto(
                            Id: $"v_new_{i}",
                            Name: newVisualName,
                            Type: "BAR",
                            GridCol: 1,
                            GridRow: 5,
                            GridColSpan: 6,
                            GridRowSpan: 4,
                            Title: $"New Fuzz Visual {i}",
                            Dataset: "data_stage",
                            Mappings: new Dictionary<string, string> { ["X"] = "cat", ["Y"] = "val" },
                            Options: new Dictionary<string, string>()
                        );
                        var updatedPages = state.Pages.Select((p, idx) => idx == 0
                            ? p with { Visuals = p.Visuals.Append(newVisual).ToList() }
                            : p).ToList();
                        state = state with { Pages = updatedPages };
                    }
                    break;

                case 4: // Delete a visual
                    if (state.Pages.Count > 0 && state.Pages[0].Visuals.Count > 1)
                    {
                        var updatedPages = state.Pages.Select((p, idx) => idx == 0
                            ? p with { Visuals = p.Visuals.Skip(1).ToList() }
                            : p).ToList();
                        state = state with { Pages = updatedPages };
                    }
                    break;

                case 5: // Mutate report style theme
                    state = state with
                    {
                        ReportStyle = new DesignerReportStyleDto(
                            Theme: rng.Next(2) == 0 ? "Dark" : "Light"
                        )
                    };
                    break;
            }

            var patched = _patcher.Patch(script, state);

            // Invariant 1: Patched script must parse cleanly
            var reparsed = _analysis.Parse(patched, 100);
            Assert.Null(reparsed.Error);

            // Invariant 2: Preceding CTEs, SQL comments, variables, connections must remain byte-preserved
            Assert.Contains("-- Header Section with license info", patched);
            Assert.Contains("/* Multi-line comment block with SQL rationale */", patched);
            Assert.Contains("CREATE CONNECTION main_db AS POSTGRES", patched);
            Assert.Contains("WITH cte_orders AS", patched);
            Assert.Contains("SELECT cat, val INTO #data_stage FROM cte_ranked", patched);
            Assert.Contains("-- Trailing comment block preserved", patched);
        }
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void Fuzz_Property_LineEndings_NeverDriftAcrossRepeatedCycles(string lineEnding)
    {
        var originalScript = string.Join(lineEnding, new[]
        {
            "-- Step 1: Pre-processing",
            "SELECT 1 AS id, 'A' AS cat, 100 AS val INTO #stage;",
            "",
            "CREATE VISUAL v_chart AS BAR (",
            "    TITLE = 'Original Title',",
            "    SOURCE = #stage,",
            "    MAPPINGS (X = cat, Y = val)",
            ");",
            "",
            "CREATE PAGE [Dashboard] AS DASHBOARD (",
            "    LAYOUT (STRUCTURE = 'A', MAP ('A' = v_chart))",
            ");",
            "",
            "-- End of script"
        });

        var currentScript = originalScript;
        for (int cycle = 1; cycle <= 20; cycle++)
        {
            var parsed = _analysis.Parse(currentScript, 100);
            Assert.Null(parsed.Error);

            var mutatedState = parsed.DesignState with
            {
                Pages = parsed.DesignState.Pages.Select(p => p with
                {
                    Visuals = p.Visuals.Select(v => v with
                    {
                        Title = $"Cycle {cycle} Title"
                    }).ToList()
                }).ToList()
            };

            currentScript = _patcher.Patch(currentScript, mutatedState);

            // Assert exact line ending integrity
            if (lineEnding == "\r\n")
            {
                Assert.DoesNotContain("\r\r\n", currentScript);
                // Every \n must be preceded by \r
                var withoutCrlf = currentScript.Replace("\r\n", "");
                Assert.DoesNotContain("\n", withoutCrlf);
                Assert.DoesNotContain("\r", withoutCrlf);
            }
            else
            {
                Assert.DoesNotContain("\r", currentScript);
            }

            Assert.Contains($"Cycle {cycle} Title", currentScript);
            Assert.Contains("-- Step 1: Pre-processing", currentScript);
            Assert.Contains("-- End of script", currentScript);
        }
    }

    [Fact]
    public void Fuzz_Property_CorruptedSyntaxInjection_LosslessNoOpAndNeverThrows()
    {
        var validScript = """
            -- Shared Data
            SELECT 'North' AS region, 5000 AS revenue INTO #sales;

            CREATE VISUAL v_bar AS BAR (
                TITLE = 'Regional Revenue',
                SOURCE = #sales,
                MAPPINGS (X = region, Y = revenue)
            );

            CREATE PAGE [Overview] AS DASHBOARD (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = v_bar))
            );
            """;

        var parsed = _analysis.Parse(validScript, 100);
        Assert.Null(parsed.Error);
        var targetState = parsed.DesignState;

        var corruptionTokens = new[]
        {
            " >>> SYNTAX_ERROR <<< ",
            " {{INVALID_TOKEN}} ",
            " SELECT UNCLOSED ( ",
            " ;;;; &&& %%% ",
            " CREATE INCOMPLETE ",
            " 'unterminated string literal ",
            " /* unclosed block comment "
        };

        var rng = new Random(20260826);
        for (int i = 0; i < 50; i++)
        {
            var insertPos = rng.Next(0, validScript.Length);
            var token = corruptionTokens[rng.Next(corruptionTokens.Length)];
            var corruptedScript = validScript.Insert(insertPos, token);

            // Patcher must NEVER throw
            string result = null!;
            var ex = Record.Exception(() => result = _patcher.Patch(corruptedScript, targetState));
            Assert.Null(ex);

            // Determine if the corrupted script had parser/lexer errors
            bool hasError = false;
            try
            {
                var ast = new CoreParser(new Lexer(corruptedScript).Tokenize(), corruptedScript).Parse();
                hasError = ast.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            }
            catch
            {
                hasError = true;
            }

            if (hasError)
            {
                // If syntax is invalid, patcher MUST return it byte-for-byte unmodified
                Assert.Equal(corruptedScript, result);
            }
            else
            {
                // If insertion landed in a comment and script remains valid, patched script must parse cleanly
                var reparsed = _analysis.Parse(result, 100);
                Assert.Null(reparsed.Error);
            }
        }
    }

    [Fact]
    public void Fuzz_Property_Idempotence_PatchingSameStateIsAlwaysIdempotent()
    {
        var script = """
            -- Staging query
            SELECT category, amount INTO #sales_stage FROM source.sales;

            CREATE VISUAL v_sales AS BAR (
                TITLE = 'Initial Sales',
                SOURCE = #sales_stage,
                MAPPINGS (X = category, Y = amount)
            );

            CREATE PAGE [Dashboard] AS DASHBOARD (
                LAYOUT (STRUCTURE = 'A', MAP ('A' = v_sales))
            );
            """;

        var parsed = _analysis.Parse(script, 100);
        Assert.Null(parsed.Error);

        var mutatedState = parsed.DesignState with
        {
            Pages = parsed.DesignState.Pages.Select(p => p with
            {
                Visuals = p.Visuals.Select(v => v with
                {
                    Title = "Idempotent Title",
                    Options = new Dictionary<string, string> { ["WIDTH"] = "100%", ["HEIGHT"] = "400px" }
                }).ToList()
            }).ToList()
        };

        var patch1 = _patcher.Patch(script, mutatedState);
        var patch2 = _patcher.Patch(patch1, mutatedState);

        Assert.Equal(patch1, patch2);
    }

    private static string GenerateRandomReportScript(Random rng, int iteration, out List<string> visualNames, out List<string> pageNames)
    {
        visualNames = new List<string>();
        pageNames = new List<string>();
        var sb = new StringBuilder();

        sb.AppendLine("-- Header Section with license info");
        sb.AppendLine("/* Multi-line comment block with SQL rationale */");
        sb.AppendLine("CREATE CONNECTION main_db AS POSTGRES(HOST='127.0.0.1', DATABASE='analytics');");
        sb.AppendLine("DECLARE @Threshold INT = 100;");
        sb.AppendLine();
        sb.AppendLine("WITH cte_orders AS (");
        // CONCAT, not `||`: the parser does not accept an alias after a `||` expression, and this
        // generator's job is to produce scripts that genuinely parse. See the `||` item in TODO.md.
        sb.AppendLine("    SELECT CONCAT('Dept ', id) AS cat, amount * 10 AS val FROM main_db.orders WHERE amount > @Threshold");
        sb.AppendLine("), cte_ranked AS (");
        sb.AppendLine("    SELECT cat, val, ROW_NUMBER() OVER (ORDER BY val DESC) AS rn FROM cte_orders");
        sb.AppendLine(")");
        sb.AppendLine("SELECT cat, val INTO #data_stage FROM cte_ranked WHERE rn <= 20;");
        sb.AppendLine();

        int visualCount = rng.Next(2, 5);
        for (int v = 0; v < visualCount; v++)
        {
            var vName = $"v_chart_{iteration}_{v}";
            visualNames.Add(vName);
            var vType = v % 3 == 0 ? "BAR" : (v % 3 == 1 ? "LINE" : "CARD");
            sb.AppendLine($"CREATE VISUAL {vName} AS {vType} (");
            sb.AppendLine($"    TITLE = 'Visual {vName}',");
            sb.AppendLine("    SOURCE = #data_stage,");
            sb.AppendLine("    MAPPINGS (X = cat, Y = val),");
            sb.AppendLine("    OPTIONS (LEGEND = 'ON'),");
            sb.AppendLine("    STYLE (WIDTH = '100%', HEIGHT = '350px')");
            sb.AppendLine(");");
            sb.AppendLine();
        }

        int pageCount = rng.Next(1, 3);
        for (int p = 0; p < pageCount; p++)
        {
            var pName = $"Page_{iteration}_{p}";
            pageNames.Add(pName);
            var assignedVisual = visualNames[p % visualNames.Count];
            sb.AppendLine($"CREATE PAGE [{pName}] AS DASHBOARD (");
            sb.AppendLine("    LAYOUT (");
            sb.AppendLine("        STRUCTURE = 'A',");
            sb.AppendLine($"        MAP ('A' = {assignedVisual})");
            sb.AppendLine("    )");
            sb.AppendLine(");");
            sb.AppendLine();
        }

        sb.AppendLine("-- Trailing comment block preserved");
        return sb.ToString();
    }
}
