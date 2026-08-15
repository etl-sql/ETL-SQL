using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Quality;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Every <c>@expect</c> rule must be able to <b>fail</b>.
    ///
    /// <para>The suite is thorough at "does this rule catch bad data" and was thin at "is this rule
    /// wired up at all" — and those look identical from the outside, because a rule that never runs
    /// reports exactly what clean data reports. Three defects in one session had that shape: a
    /// composite rule naming an unprojected column skipped every row, <c>CASTABLE AS</c> with an
    /// unknown type accepted everything, and both per-row rule switches ended in a default that
    /// returned "passed".</para>
    ///
    /// <para>The reflection test is the part that keeps this honest: a new <see cref="ColumnRule"/>
    /// record cannot be added without a case here, so it cannot ship silently unenforced. Same shape
    /// as <c>EngineSubsystemCoverageTests</c>.</para>
    /// </summary>
    public class ColumnRuleCatalogPropertyTests
    {
        /// <param name="Setup">Extra tables the rule references, if any.</param>
        /// <param name="Column">Which seeded column carries the rule.</param>
        /// <param name="Values">Rows for <c>#src(Id INT, Name VARCHAR(100))</c>, at least one of
        /// which must violate the rule.</param>
        private sealed record RuleCase(string Expect, string Column, string Values, string Setup = "");

        private static readonly Dictionary<Type, RuleCase> Cases = new()
        {
            [typeof(NotNullRule)] = new("NOT NULL", "Id", "(NULL, 'a')"),
            [typeof(NotBlankRule)] = new("NOT BLANK", "Name", "(1, '   ')"),
            [typeof(LengthRule)] = new("LENGTH BETWEEN 5 AND 10", "Name", "(1, 'abc')"),
            [typeof(MatchesRule)] = new("MATCHES ^v[0-9]+$", "Name", "(1, 'nope')"),
            [typeof(InListRule)] = new("IN ('a','b')", "Name", "(1, 'z')"),
            [typeof(ComparisonRule)] = new(">= 0", "Id", "(-1, 'a')"),
            [typeof(CastableRule)] = new("CASTABLE AS DATE", "Name", "(1, 'not a date')"),
            [typeof(BetweenRule)] = new("BETWEEN 1 AND 10", "Id", "(99, 'a')"),
            [typeof(ExprRule)] = new("EXPR Id >= 0", "Id", "(-1, 'a')"),
            [typeof(UniqueRule)] = new("UNIQUE", "Id", "(1, 'a'), (1, 'b')"),
            [typeof(ExistsInRule)] = new(
                "EXISTS IN #dim(Id)", "Id", "(99, 'a')",
                Setup: "CREATE TABLE #dim (Id INT); INSERT INTO #dim (Id) VALUES (1);"),
            [typeof(AndRule)] = new("NOT NULL AND > 0", "Id", "(-5, 'a')"),
            [typeof(OrRule)] = new("= 1 OR = 2", "Id", "(3, 'a')"),
        };

        [Fact]
        public void EveryColumnRuleType_HasACase()
        {
            var declared = typeof(ColumnRule).Assembly
                .GetTypes()
                .Where(t => t.IsSealed && !t.IsAbstract && typeof(ColumnRule).IsAssignableFrom(t))
                .ToList();

            var uncovered = declared.Except(Cases.Keys).Select(t => t.Name).OrderBy(n => n).ToList();

            Assert.True(uncovered.Count == 0,
                "These @expect rules have no case proving they can fail:\n  "
                + string.Join("\n  ", uncovered)
                + "\n\nA rule that never fires reports what clean data reports, so an unenforced one "
                + "is invisible rather than wrong. Add a row that violates it.");

            var stale = Cases.Keys.Except(declared).Select(t => t.Name).OrderBy(n => n).ToList();
            Assert.True(stale.Count == 0, "Cases for rules that no longer exist: " + string.Join(", ", stale));
        }

        [Theory]
        [MemberData(nameof(RuleNames))]
        public async Task EveryRule_FailsOnAViolatingRow(string ruleTypeName)
        {
            var ruleCase = Cases.Single(entry => entry.Key.Name == ruleTypeName).Value;
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            await Run(eval, $@"
                CREATE TABLE #src (Id INT, Name VARCHAR(100));
                INSERT INTO #src (Id, Name) VALUES {ruleCase.Values};
                {ruleCase.Setup}");

            var tagged = new[] { "Id", "Name" }
                .Select(c => c == ruleCase.Column
                    ? $"{c} /* @expect: \"{ruleCase.Expect}\"; @fail: 'WARN'; */"
                    : c);

            await Run(eval, $@"
                SELECT {string.Join(", ", tagged)}
                INTO #clean FROM #src
                ON FAILURE WARN;");

            Assert.True(eval.DataQuality.TotalFailures > 0,
                $"{ruleTypeName} recorded no failure against a row that violates \"{ruleCase.Expect}\". "
                + "Either the rule is not reaching the row pipeline, or it is passing everything — "
                + "both of which look exactly like clean data from outside.");
        }

        public static TheoryData<string> RuleNames()
        {
            var data = new TheoryData<string>();
            foreach (var type in Cases.Keys.OrderBy(t => t.Name, StringComparer.Ordinal))
                data.Add(type.Name);
            return data;
        }

        private static Task Run(Evaluator eval, string sql) =>
            eval.Evaluate(new Lexer(sql).TokenizeToScript());
    }
}
