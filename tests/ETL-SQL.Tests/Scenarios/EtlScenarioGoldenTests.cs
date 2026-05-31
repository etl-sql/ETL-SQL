using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Tests.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Scenarios
{
    public class EtlScenarioGoldenTests
    {
        public static IEnumerable<object[]> ScenarioDirectories()
        {
            var root = Path.Combine(FindRepoRoot(), "tests", "etl_scenarios");
            foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(Path.GetFileName))
            {
                yield return new object[] { dir };
            }
        }

        [Theory]
        [MemberData(nameof(ScenarioDirectories))]
        public async Task Scenario_MatchesGoldenExpectations(string scenarioDirectory)
        {
            var scriptPath = Path.Combine(scenarioDirectory, "script.etlsql");
            var expectedPath = Path.Combine(scenarioDirectory, "expected.json");
            var scriptText = await File.ReadAllTextAsync(scriptPath);
            var expected = JsonSerializer.Deserialize<ScenarioExpectation>(
                await File.ReadAllTextAsync(expectedPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ScenarioExpectation();

            var script = TestHelpers.Parse(scriptText);
            Assert.Empty(script.Diagnostics.Select(d => d.Message));

            if (expected.Failure != null)
            {
                await AssertExpectedFailure(script, expected.Failure);
                return;
            }

            if (expected.StaticLineage.Count > 0)
            {
                AssertStaticLineage(script, expected);
            }

            if (expected.RuntimeQueries.Count > 0)
            {
                await AssertRuntimeQueries(script, expected);
            }
        }

        private static async Task AssertExpectedFailure(Script script, FailureExpectation expected)
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var exception = await Assert.ThrowsAsync<ExecutionException>(() => evaluator.Evaluate(script));

            if (!string.IsNullOrWhiteSpace(expected.MessageContains))
            {
                Assert.Contains(expected.MessageContains, exception.Message, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void AssertStaticLineage(Script script, ScenarioExpectation expected)
        {
            var tracker = new LineageTracker(NullLogger.Instance);
            foreach (var seed in expected.SeedLineage)
            {
                tracker.Record(
                    seed.Table,
                    seed.SourceTables ?? Enumerable.Empty<string>(),
                    seed.Operation ?? "SEED",
                    seed.Column,
                    seed.SourceColumns,
                    seed.Metadata);
            }

            new LineageAnalyzer(tracker).Analyze(script);

            foreach (var expectedLineage in expected.StaticLineage)
            {
                var entries = tracker.GetFullLineage()
                    .Where(e => Matches(e.TargetTable, expectedLineage.TargetTable))
                    .Where(e => expectedLineage.TargetColumn == null || Matches(e.TargetColumn, expectedLineage.TargetColumn))
                    .Where(e => expectedLineage.Operation == null || Matches(e.Operation, expectedLineage.Operation))
                    .ToList();

                Assert.NotEmpty(entries);
                var entry = entries.FirstOrDefault(e =>
                    ContainsAll(e.SourceTables, expectedLineage.SourceTables) &&
                    ContainsAll(e.SourceColumns, expectedLineage.SourceColumns) &&
                    ContainsAll(e.Metadata, expectedLineage.Metadata));

                Assert.True(entry != null, $"No lineage entry matched expected lineage for {expectedLineage.TargetTable}.{expectedLineage.TargetColumn}.");
            }
        }

        private static async Task AssertRuntimeQueries(Script script, ScenarioExpectation expected)
        {
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await evaluator.Evaluate(script);

            foreach (var query in expected.RuntimeQueries)
            {
                var queryScript = TestHelpers.Parse(query.Sql);
                Assert.Single(queryScript.Statements);
                var result = await evaluator.ExecuteQuery(queryScript.Statements[0]).FirstAsync();
                AssertRows(result, query.Rows);
            }
        }

        private static void AssertRows(DataTable actual, List<Dictionary<string, JsonElement>> expectedRows)
        {
            Assert.Equal(expectedRows.Count, actual.Rows.Count);

            for (var i = 0; i < expectedRows.Count; i++)
            {
                foreach (var expectedColumn in expectedRows[i])
                {
                    Assert.True(actual.Rows[i].Columns.ContainsKey(expectedColumn.Key), $"Missing column '{expectedColumn.Key}'.");
                    var actualText = Convert.ToString(actual.Rows[i][expectedColumn.Key], CultureInfo.InvariantCulture);
                    Assert.Equal(ToComparableString(expectedColumn.Value), actualText);
                }
            }
        }

        private static string? ToComparableString(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => value.ToString()
            };
        }

        private static bool ContainsAll(IEnumerable<string> actual, IReadOnlyList<string>? expected)
        {
            if (expected == null || expected.Count == 0) return true;
            var actualSet = new HashSet<string>(actual, StringComparer.OrdinalIgnoreCase);
            return expected.All(actualSet.Contains);
        }

        private static bool ContainsAll(Dictionary<string, string> actual, Dictionary<string, string>? expected)
        {
            if (expected == null || expected.Count == 0) return true;
            return expected.All(kv =>
                actual.TryGetValue(kv.Key, out var actualValue) &&
                string.Equals(actualValue, kv.Value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool Matches(string? actual, string expected) =>
            string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ETL-SQL.slnx")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate ETL-SQL.slnx from test output directory.");
        }

        private sealed class ScenarioExpectation
        {
            public FailureExpectation? Failure { get; set; }
            public List<SeedLineageExpectation> SeedLineage { get; set; } = new();
            public List<LineageExpectation> StaticLineage { get; set; } = new();
            public List<RuntimeQueryExpectation> RuntimeQueries { get; set; } = new();
        }

        private sealed class FailureExpectation
        {
            public string? MessageContains { get; set; }
        }

        private sealed class SeedLineageExpectation
        {
            public string Table { get; set; } = "";
            public string? Column { get; set; }
            public string? Operation { get; set; }
            public List<string>? SourceTables { get; set; }
            public List<string>? SourceColumns { get; set; }
            public Dictionary<string, string>? Metadata { get; set; }
        }

        private sealed class LineageExpectation
        {
            public string TargetTable { get; set; } = "";
            public string? TargetColumn { get; set; }
            public string? Operation { get; set; }
            public List<string>? SourceTables { get; set; }
            public List<string>? SourceColumns { get; set; }
            public Dictionary<string, string>? Metadata { get; set; }
        }

        private sealed class RuntimeQueryExpectation
        {
            public string Sql { get; set; } = "";
            public List<Dictionary<string, JsonElement>> Rows { get; set; } = new();
        }
    }
}
