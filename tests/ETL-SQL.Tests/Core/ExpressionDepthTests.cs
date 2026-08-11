using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    /// <summary>
    /// Expression depth must not be bounded by the stack.
    ///
    /// <para>Every lane varies the query and holds the data constant; none varied expression
    /// <i>depth</i>, and a defect lived there. <c>AstSerializer.FormatBinary</c> recursed once per
    /// operand — three frames per term — so a <c>WHERE</c> with about fifty conjuncts overflowed the
    /// stack. It surfaced only in <c>select5.test</c>'s fifty-way join, whose forty-nine equality
    /// predicates are the longest boolean chain anywhere in the repository.</para>
    ///
    /// <para>The consequence was worse than a wrong answer <i>and</i> worse than an ordinary crash:
    /// every top-level script is serialized in full to hash it for the execution-policy snapshot
    /// before any statement runs, so the process died at governance capture with no message, no
    /// diagnostic, and nothing catchable — a stack overflow cannot be caught.</para>
    ///
    /// <para>These run in milliseconds and need no corpus. The counts are far past anything the SLT
    /// files contain, because the point is to leave headroom rather than to sit at the old limit.</para>
    /// </summary>
    public class ExpressionDepthTests
    {
        [Theory]
        [InlineData(50)]    // the length that actually crashed
        [InlineData(500)]
        [InlineData(2000)]
        public void ALongConjunctionSerializes(int terms)
        {
            var sql = "SELECT a FROM t WHERE "
                + string.Join(" AND ", Enumerable.Range(0, terms).Select(i => $"a{i} = {i}")) + ";";

            var serialized = Parse(sql).Statements.Single().ToSql();

            Assert.Contains($"a{terms - 1} = {terms - 1}", serialized);
        }

        [Theory]
        [InlineData(500)]
        public void ALongDisjunctionSerializes(int terms)
        {
            var sql = "SELECT a FROM t WHERE "
                + string.Join(" OR ", Enumerable.Range(0, terms).Select(i => $"a = {i}")) + ";";

            var serialized = Parse(sql).Statements.Single().ToSql();

            Assert.Contains($"a = {terms - 1}", serialized);
        }

        [Theory]
        [InlineData(500)]
        public void ALongArithmeticChainSerializes(int terms)
        {
            var sql = "SELECT " + string.Join(" + ", Enumerable.Range(0, terms)) + " AS total FROM t;";

            var serialized = Parse(sql).Statements.Single().ToSql();

            Assert.Contains($"{terms - 1}", serialized);
        }

        /// <summary>
        /// Right-deep nesting reaches the serializer by the other operand, and unlike a chain it
        /// also recurses the parser — explicit parentheses nest, where <c>a AND b AND c</c> is
        /// parsed by a loop and never increments the depth counter at all. That asymmetry is the
        /// whole defect: the parser's deliberate ceiling of 100 made deep nesting safe, and a
        /// 49-term chain sailed past it into a serializer with no ceiling of any kind.
        /// </summary>
        [Theory]
        [InlineData(5)]
        [InlineData(25)]
        [InlineData(50)]
        [InlineData(90)]  // inside the ceiling of 100; the predicate itself adds a level or two
        public void RightDeepNestingSerializes(int depth)
        {
            var script = Parse(RightDeepPredicate(depth));

            Assert.True(script.Statements.Count > 0,
                $"{depth} levels is within the supported ceiling but produced no statements. "
                + "Diagnostics: "
                + (script.Diagnostics.Count == 0
                    ? "none — the statement vanished without even a diagnostic."
                    : string.Join(" | ", script.Diagnostics.Select(d => $"[{d.Code}] {d.Message}"))));
            Assert.Contains("x = 0", script.Statements.Single().ToSql());
        }

        /// <summary>
        /// Past the ceiling the parser must say so. This is the behaviour the serializer lacked:
        /// refusing a script with a located diagnostic is survivable, and killing the process is
        /// not.
        /// </summary>
        [Fact]
        public void NestingPastTheCeilingIsRejectedWithADiagnostic()
        {
            var script = Parse(RightDeepPredicate(200));

            Assert.Contains(script.Diagnostics,
                d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("nesting"));
        }

        private static string RightDeepPredicate(int depth) =>
            "SELECT a FROM t WHERE "
            + string.Concat(Enumerable.Repeat("(x = 1 AND ", depth))
            + "x = 0"
            + new string(')', depth) + ";";

        /// <summary>
        /// The serialized form feeds the execution-policy script hash, so the flattening must be
        /// byte-identical to what recursion produced — a formatting change here silently
        /// invalidates every recorded hash.
        /// </summary>
        [Fact]
        public void FlatteningPreservesTheExactParenthesization()
        {
            var serialized = Parse("SELECT a FROM t WHERE a = 1 AND b = 2 AND c = 3;")
                .Statements.Single().ToSql();

            Assert.Contains("(((a = 1) AND (b = 2)) AND (c = 3))", serialized);
        }

        /// <summary>
        /// End to end, because serializing is only half of it: the statement also has to run. This
        /// is the shape that killed the process — governance capture serializes the whole script
        /// before the first statement executes.
        /// </summary>
        [Fact]
        public async Task ALongConjunctionExecutes()
        {
            const int terms = 500;
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            await Run(eval, "CREATE TABLE #t (a INT); INSERT INTO #t (a) VALUES (1), (2);");

            var predicate = string.Join(" AND ", Enumerable.Range(0, terms).Select(_ => "a > 0"));
            await Run(eval, $"SELECT a FROM #t WHERE {predicate};");

            Assert.True(eval.LastResult?.Rows.Count == 2,
                $"A {terms}-term predicate chain returned "
                + $"{eval.LastResult?.Rows.Count.ToString() ?? "no result"} rows; both rows satisfy it.");
        }

        /// <summary>
        /// Every lint rule, against a long chain.
        ///
        /// <para>Two recursive tree walks were found and fixed by the tests above; 38 source files
        /// mention <c>BinaryExpression</c>, so fixing the two instances does not close the class.
        /// Auditing them by hand is the wrong instrument — reflecting over the rules drives whatever
        /// exists, including rules added later, and a walk that still recurses takes the process
        /// down rather than failing an assertion. A crashed run is the signal here.</para>
        /// </summary>
        [Fact]
        public async Task EveryLintRule_SurvivesALongConjunction()
        {
            const int terms = 500;
            var sql = "SELECT a FROM t WHERE "
                + string.Join(" AND ", Enumerable.Range(0, terms).Select(i => $"a{i} = {i}")) + ";";

            var linter = new Linter();
            var rules = typeof(ILintRule).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ILintRule).IsAssignableFrom(t))
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .ToList();

            Assert.True(rules.Count > 0, "Found no lint rules to drive; the reflection filter is wrong.");

            foreach (var rule in rules)
                linter.AddRule((ILintRule)Activator.CreateInstance(rule)!);

            var results = await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext());

            Assert.True(results != null, $"Linting a {terms}-term chain with {rules.Count} rules produced nothing.");
        }

        /// <summary>
        /// EXPLAIN builds its own plan tree over the same expressions, by a different walk.
        /// </summary>
        [Fact]
        public async Task Explain_SurvivesALongConjunction()
        {
            const int terms = 500;
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

            await Run(eval, "CREATE TABLE #t (a INT); INSERT INTO #t (a) VALUES (1);");

            var predicate = string.Join(" AND ", Enumerable.Range(0, terms).Select(_ => "a > 0"));
            await Run(eval, $"EXPLAIN SELECT a FROM #t WHERE {predicate};");
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

        private static Task Run(Evaluator eval, string sql) =>
            eval.Evaluate(new Lexer(sql).TokenizeToScript());
    }
}
