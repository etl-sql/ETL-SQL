using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.FuzzTests
{
    public class NoRecMismatchException : Exception
    {
        public NoRecMismatchException(string message) : base(message) { }
    }

    [Trait("Category", "Fuzz")]
    public class ParserFuzzTests : IAsyncLifetime
    {
        private IServiceProvider _serviceProvider = null!;
        private Evaluator _evaluator = null!;
        private string _fuzzLogDir = null!;
        private string _fuzzTableName = null!;
        private int _seed;
        private FuzzDataShape _dataShape = FuzzDataShape.FromSeed(0);
        private bool _strictExec;
        private bool _queryValidByConstruction;
        private bool _queryCorrupted;
        private bool _dumpExec;
        private readonly HashSet<string> _execMessages = new(StringComparer.Ordinal);
        private int _noRecCompared;
        private int _noRecRewriteRejected;

        // Message fragments of ExecutionExceptions that are known-expected engine rejections (not
        // bugs), consulted when strict-exec is enabled. To extend after new surface is fuzzed, run
        // with ETLSQL_FUZZ_DUMP_EXEC=1 and fold new benign messages in as stable fragments.
        private static readonly string[] ExpectedExecutionMessageFragments =
        {
            // Calibrated 2026-07-12 from a 40k-iteration dump (ETLSQL_FUZZ_DUMP_EXEC=1). These are
            // sanitized engine rejections of semantically-invalid generated queries, not bugs.
            // Explicit THROW-originated exceptions are handled structurally (see RunFuzzer), not here.
            "not found or does not support remote file",
            "Connection name evaluated to null",
            "Procedure not found",
            "Unknown function",
            "Unknown source",
            "invalid time format",
            // A grammar walk names random objects (COPY FILE src.Users WITH (src.Users = ...)).
            "It is not a column, a variable, a connection or a function",
        };

        public async Task InitializeAsync()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
            {
                ["Snippets:UserSnippetsPath"] = Path.Combine(Path.GetTempPath(), "etlsql-fuzz-snippets")
            });

            _evaluator = _serviceProvider.GetRequiredService<Evaluator>();

            // 1. Create MOCKDB connection
            var setupQuery = "CREATE CONNECTION src AS MOCKDB();";
            await _evaluator.Evaluate(new Parser(new Lexer(setupQuery).Tokenize(), setupQuery).Parse());

            // 2. Create a randomized schema mutation (fuzz table) to verify dynamic table compile
            _fuzzTableName = "FuzzTable_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var createTableQuery = $"CREATE TABLE src.{_fuzzTableName} (ID INT, Price DECIMAL, Name VARCHAR(50), TotalAmount DECIMAL);";
            await _evaluator.Evaluate(new Parser(new Lexer(createTableQuery).Tokenize(), createTableQuery).Parse());

            // 3. Seed the table with a data shape drawn from the same seed as the grammar walk.
            //
            // Every lane varied the query and held the data constant: three rows, one of them all
            // NULL. That is why the entire columnar/spill layer was unreachable by fuzzing — the
            // defects there were not missed, they could not be executed. Row count, per-column null
            // density and value spread now vary too, and because the shape comes from the seed a
            // reproducer still reproduces.
            _seed = ResolveSeed();
            _dataShape = FuzzDataShape.FromSeed(_seed);

            foreach (var insert in _dataShape.BuildInserts($"src.{_fuzzTableName}"))
                await _evaluator.Evaluate(new Parser(new Lexer(insert).Tokenize(), insert).Parse());

            // 4. Create fuzz logging directory
            _fuzzLogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "fuzz");
            Directory.CreateDirectory(Path.Combine(_fuzzLogDir, "reproducers"));
        }

        public async Task DisposeAsync()
        {
            switch (_serviceProvider)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        /// <summary>
        /// The one place the seed is decided, so the data shape and the grammar walk cannot end up
        /// drawn from different seeds — which would make a reported reproducer not reproduce.
        /// </summary>
        private static int ResolveSeed() =>
            int.TryParse(Environment.GetEnvironmentVariable("ETLSQL_FUZZ_SEED"), out var parsed)
                ? parsed
                : Environment.TickCount;

        [Fact]
        public async Task RunFuzzer()
        {
            string? iterEnv = Environment.GetEnvironmentVariable("ETLSQL_FUZZ_ITERATIONS");
            int iterations = int.TryParse(iterEnv, out var parsedIter) ? parsedIter : 500;
            string? generationAttemptsEnv = Environment.GetEnvironmentVariable("ETLSQL_FUZZ_GENERATION_ATTEMPTS");
            int generationAttempts = int.TryParse(generationAttemptsEnv, out var parsedGenerationAttempts)
                ? Math.Max(1, parsedGenerationAttempts)
                : 10;

            // Seed both RNG streams from an overridable seed and record it, so any failure found in
            // CI is reproducible: rerun with ETLSQL_FUZZ_SEED=<seed>. The seed is echoed to test
            // output and written into every reproducer file.
            // _seed was resolved during InitializeAsync, because the seeded data shape is drawn from
            // it and the table is populated before this runs. Re-resolving here would hand the
            // grammar walk a different seed than the data, and an unseeded rerun a different shape.
            // Strictness depends on where a query came from. A template query is valid by
            // construction against the fuzz table, so any ExecutionException from it is a bug,
            // always. A grammar walk names random objects; with ETLSQL_FUZZ_STRICT_EXEC=1 its
            // un-allowlisted ExecutionExceptions are bugs too (the fixed-seed smoke lane), otherwise
            // only real crashes are. A corrupted query is expected to fail, so only a real crash
            // counts there.
            _strictExec = Environment.GetEnvironmentVariable("ETLSQL_FUZZ_STRICT_EXEC") == "1";
            _dumpExec = Environment.GetEnvironmentVariable("ETLSQL_FUZZ_DUMP_EXEC") == "1";
            Console.WriteLine($"[Fuzzer] seed={_seed} iterations={iterations} generationAttempts={generationAttempts} strictExec={_strictExec} (rerun with ETLSQL_FUZZ_SEED={_seed})");
            Console.WriteLine($"[Fuzzer] data shape: {_dataShape}");

            var tree = DefaultGrammar.Build();
            var generator = new GrammarWalkGenerator(tree, new Random(_seed));

            // Register dynamic schema in the walk generator
            generator.AddCustomSchema(_fuzzTableName, new[] { "ID", "Price", "Name", "TotalAmount" });

            var results = new FuzzResults();
            var rng = new Random(unchecked(_seed * 31 + 17));

            for (int i = 0; i < iterations; i++)
            {
                var tokens = generator.GenerateQuery();
                Script? preParsed = null;
                string query = string.Empty;

                for (int attempt = 1; attempt <= generationAttempts; attempt++)
                {
                    query = string.Join(" ", tokens.Where(t => t.Type != TokenType.EOF).Select(t => t.Value));
                    if (string.IsNullOrWhiteSpace(query)) break;

                    try
                    {
                        preParsed = new Parser(tokens, query).Parse();
                    }
                    catch (Exception ex)
                    {
                        if (IsSevereCrash(ex))
                        {
                            results.ParserCrash.Record(tokens, query, ex);
                            preParsed = null;
                            break;
                        }

                        preParsed = null;
                    }

                    if (preParsed != null &&
                        !preParsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        break;
                    }

                    // A template query is valid by construction; the parser rejecting one is a
                    // parser regression, not a retry.
                    if (generator.LastQueryIsValidByConstruction)
                    {
                        var error = preParsed?.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)?.Message;
                        results.TemplateParserRejected.Record(tokens, query,
                            new InvalidOperationException($"The parser rejected a template query: {error ?? "it threw"}"));
                    }

                    bool grammarAcceptsRejectedCandidate = tree.ValidateSequence(tokens, out _, requireComplete: false);
                    if (grammarAcceptsRejectedCandidate)
                    {
                        results.GrammarAcceptedParserRejected.Increment();
                    }
                    results.ParserDiagnostic.Increment();
                    results.GrammarGeneratedParserRejected.Increment();

                    if (attempt < generationAttempts)
                    {
                        tokens = generator.GenerateQuery();
                    }
                }

                if (preParsed == null ||
                    preParsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    continue;
                }

                bool isCorrupted = false;

                // 5% chance of token mutation/corruption to verify parser robustness
                if (rng.Next(100) < 5)
                {
                    generator.CorruptQuery(tokens);
                    isCorrupted = true;
                }

                query = string.Join(" ", tokens.Where(t => t.Type != TokenType.EOF).Select(t => t.Value));
                if (string.IsNullOrWhiteSpace(query)) continue;

                // --- Parse stage ---
                Script parsed;
                if (isCorrupted)
                {
                    try
                    {
                        parsed = new Parser(tokens, query).Parse();
                    }
                    catch (Exception ex)
                    {
                        // An unhandled exception out of the parser is a parser robustness bug; an expected
                        // syntax failure (common on corrupted input) is just a diagnostic bucket.
                        if (IsSevereCrash(ex)) results.ParserCrash.Record(tokens, query, ex);
                        else results.ParserDiagnostic.Increment();
                        continue;
                    }
                }
                else
                {
                    parsed = preParsed;
                }

                bool parserAccepts = !parsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

                // --- Grammar conformance stage (non-corrupted only) ---
                // Compare the grammar state tree against the production parser in both directions and
                // report them separately, so we can tell whether the grammar is too strict (recall gap
                // that would make suggestions miss valid tokens) or too loose (precision gap that would
                // suggest invalid tokens). The grammar-only walk (requireComplete:false) is used so the
                // tree's own parser-acceptance gate does not mask the comparison.
                if (!isCorrupted)
                {
                    bool grammarAccepts = tree.ValidateSequence(tokens, out var grammarError, requireComplete: false);
                    if (parserAccepts && !grammarAccepts)
                        results.GrammarRejectedParserAccepted.Record(tokens, query,
                            new InvalidOperationException($"The grammar state tree rejected a statement the parser accepts: {grammarError}"));
                    else if (!parserAccepts && grammarAccepts) results.GrammarAcceptedParserRejected.Increment();
                }

                if (!parserAccepts)
                {
                    results.ParserDiagnostic.Increment();
                    // A grammar-generated (non-corrupted) query the parser rejected means the generator
                    // walked a path the parser does not accept — a generator-fidelity/yield signal.
                    if (!isCorrupted) results.GrammarGeneratedParserRejected.Increment();
                    continue;
                }

                // --- Execution stage ---
                // A template query is valid by construction, so any engine rejection of it is a
                // finding. A corrupted query or a grammar walk is expected to be rejected.
                _queryValidByConstruction = generator.LastQueryIsValidByConstruction && !isCorrupted;
                _queryCorrupted = isCorrupted;
                try
                {
                    _evaluator.LastResult = null;
                    await _evaluator.Evaluate(parsed);

                    if (!isCorrupted && parsed.Statements.FirstOrDefault() is SelectStatement selectStmt)
                    {
                        await VerifyNoRECParity(selectStmt);
                    }
                }
                catch (NoRecMismatchException ex)
                {
                    results.DifferentialCorrectness.Record(tokens, query, ex);
                }
                catch (Exception ex)
                {
                    if (_dumpExec && ex is ExecutionException) _execMessages.Add(ex.Message);

                    // A THROW statement raising an ExecutionException is the script doing exactly what
                    // it asked; its message is arbitrary user text, so classify it structurally rather
                    // than trying to allowlist the message.
                    bool expectedThrow = ex is ExecutionException && parsed.Statements.Any(s => s is ThrowStatement);
                    if (!expectedThrow && IsSevereCrash(ex)) results.ExecutionCrash.Record(tokens, query, ex);
                    // Otherwise an expected/sanitized engine error — not a bug (see IsSevereCrash).
                }
            }

            // --- Grammar coverage ---
            int totalStates = tree.GetAllStates().Count;
            int totalTransitions = tree.GetTotalTransitionCount();
            int reachedStates = generator.VisitedStates.Count;
            int reachedTransitions = generator.VisitedTransitions.Count;
            double statePct = totalStates == 0 ? 0 : 100.0 * reachedStates / totalStates;
            double transPct = totalTransitions == 0 ? 0 : 100.0 * reachedTransitions / totalTransitions;

            Console.WriteLine(results.Summary());
            // Print the bug samples now: a failed assertion below may fire first and hide them.
            if (results.BugBuckets.Any(b => b.Count > 0)) Console.WriteLine(results.SampleSummary());
            Console.WriteLine($"[Fuzzer] NoREC: compared={_noRecCompared} rewriteRejected={_noRecRewriteRejected}");
            Console.WriteLine($"[Fuzzer] grammar coverage: states {reachedStates}/{totalStates} ({statePct:F1}%), " +
                              $"transitions {reachedTransitions}/{totalTransitions} ({transPct:F1}%)");

            if (_dumpExec)
            {
                var dumpPath = Path.Combine(_fuzzLogDir, "exec-messages.txt");
                File.WriteAllLines(dumpPath, _execMessages.OrderBy(m => m, StringComparer.Ordinal));
                Console.WriteLine($"[Fuzzer] dumped {_execMessages.Count} distinct ExecutionException message(s) to {dumpPath}");
            }

            // Persist minimal reproducers only for the buckets that represent real bugs.
            bool writeRepros = Environment.GetEnvironmentVariable("ETLSQL_FUZZ_NO_REPRO") != "1";
            if (writeRepros)
            {
                foreach (var bucket in results.BugBuckets)
                {
                    foreach (var sample in bucket.Samples)
                    {
                        WriteReproducer(bucket.Name, sample.Tokens, sample.Query, sample.Exception);
                    }
                }
            }

            // About one generated query in ten is NoREC-eligible, so a run this long that compared
            // none means the eligibility gate has gone vacuous again.
            if (iterations >= 200)
            {
                Assert.True(_noRecCompared > 0,
                    $"NoREC compared no queries in {iterations} iterations (rewriteRejected={_noRecRewriteRejected}); the differential check is not running.");
            }

            int bugCount = results.BugBuckets.Sum(b => b.Count);
            Assert.True(bugCount == 0,
                $"Fuzzer found {bugCount} bug(s). {results.Summary()} {results.SampleSummary()} See logs/fuzz/reproducers/ for minimized SQL.");
        }

        private bool IsSevereCrash(Exception ex)
        {
            if (ex is NoRecMismatchException)
            {
                return true;
            }

            // Always-expected exceptions are never severe. ConnectionException derives from
            // ExecutionException, so it must be matched here — before the generic
            // ExecutionException handling below — to stay benign even under strict-exec.
            if (ex is SyntaxException || ex is ConnectionException || ex is DivideByZeroException || ex is OverflowException)
            {
                return false;
            }

            // ExecutionException is the engine's sanitized wrapper for real failures, so treating
            // it as always-benign hides genuine bugs. With ETLSQL_FUZZ_STRICT_EXEC=1, any
            // ExecutionException whose message is not on the expected-rejection allowlist counts as
            // a bug. Off by default (allowlist not yet calibrated) so CI behaviour is unchanged.
            if (ex is ExecutionException)
            {
                if (_queryValidByConstruction) return true;
                // A corrupted query is expected to fail; only a real crash counts there.
                if (_strictExec && !_queryCorrupted &&
                    !ExpectedExecutionMessageFragments.Any(f =>
                        ex.Message.Contains(f, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
                return false;
            }

            return ex is NullReferenceException ||
                   ex is IndexOutOfRangeException ||
                   ex is InvalidCastException ||
                   ex is ArgumentOutOfRangeException ||
                   ex is KeyNotFoundException;
        }

        private async Task VerifyNoRECParity(SelectStatement selectStmt)
        {
            if (selectStmt.WhereClause == null) return;
            // Every generated query reads src.<table>, so a connection-qualified FROM must stay in
            // scope: excluding it skipped NoREC on 100% of queries.
            // A join stays in: WHERE filters the joined rows, and SUM(CASE WHEN p ...) over the same
            // joined rows must count the same rows.
            if (selectStmt.IntoTable != null ||
                selectStmt.OnFailureActions != null ||
                selectStmt.IsDistinct ||
                selectStmt.TopCount != null ||
                selectStmt.LimitCount != null ||
                selectStmt.Offset != null ||
                selectStmt.ForClause != null ||
                selectStmt.QualifyClause != null ||
                selectStmt.Sample != null ||
                selectStmt.GroupByAll ||
                selectStmt.GroupingSet != null ||
                selectStmt.GroupBy is { Count: > 0 } ||
                selectStmt.HavingClause != null ||
                selectStmt.WindowDefinitions is { Count: > 0 })
            {
                return;
            }

            // 1. Build Query 1 (Optimized COUNT)
            var countCol = new SelectColumn(
                new FunctionCallExpression("COUNT", new List<Expression> { new LiteralExpression(1, TokenType.NUMBER) }),
                "cnt",
                null
            );
            var q1 = selectStmt with { Columns = new List<SelectColumn> { countCol }, OrderBy = null, OrderByAll = false, OrderByAllDescending = false };
            var sql1 = new Script { Statements = new List<Statement> { q1 } }.ToSql();

            // 2. Build Query 2 (NoREC SUM(CASE...))
            var caseExpr = new CaseExpression(
                new List<(Expression, Expression)> { (selectStmt.WhereClause, new LiteralExpression(1, TokenType.NUMBER)) },
                new LiteralExpression(0, TokenType.NUMBER)
            );
            var sumCol = new SelectColumn(
                new FunctionCallExpression("SUM", new List<Expression> { caseExpr }),
                "cnt",
                null
            );
            var q2 = selectStmt with { Columns = new List<SelectColumn> { sumCol }, WhereClause = null, OrderBy = null, OrderByAll = false, OrderByAllDescending = false };
            var sql2 = new Script { Statements = new List<Statement> { q2 } }.ToSql();

            // 3. Evaluate both
            try
            {
                _evaluator.LastResult = null;
                var tokens1 = new Lexer(sql1).Tokenize();
                var parsed1 = new Parser(tokens1, sql1).Parse();
                await _evaluator.Evaluate(parsed1);
                var val1 = Convert.ToInt64(_evaluator.LastResult?.Rows.FirstOrDefault()?[0] ?? 0);

                _evaluator.LastResult = null;
                var tokens2 = new Lexer(sql2).Tokenize();
                var parsed2 = new Parser(tokens2, sql2).Parse();
                await _evaluator.Evaluate(parsed2);
                var val2 = Convert.ToInt64(_evaluator.LastResult?.Rows.FirstOrDefault()?[0] ?? 0);

                if (val1 != val2)
                {
                    throw new NoRecMismatchException($"NoREC correctness mismatch! Optimized: {val1}, Unoptimized: {val2}. Query1: {sql1}, Query2: {sql2}");
                }

                _noRecCompared++;
            }
            catch (Exception ex) when (ex is not NoRecMismatchException && IsExpectedRewriteRejection(ex))
            {
                _noRecRewriteRejected++;
            }
            catch (Exception ex) when (ex is not NoRecMismatchException && !IsSevereCrash(ex))
            {
                // The original query ran, and both rewrites read the same rows with the same
                // predicate. A rewrite that fails anyway is an engine or serializer defect, so it
                // fails the run whether or not strict-exec is on; before, it was counted and dropped.
                throw new NoRecMismatchException(
                    $"NoREC rewrite failed although the original query ran: {ex.GetType().Name}: {ex.Message}. Query1: {sql1}, Query2: {sql2}");
            }
        }

        /// <summary>
        /// A rewrite failure that is not a defect: a connection failure, or an engine rejection on the
        /// expected-rejection allowlist. Arithmetic failures stay defects: the rewrite evaluates the
        /// predicate on exactly the rows the original did.
        /// </summary>
        private static bool IsExpectedRewriteRejection(Exception ex) =>
            ex is ConnectionException ||
            (ex is ExecutionException && ExpectedExecutionMessageFragments.Any(f =>
                ex.Message.Contains(f, StringComparison.OrdinalIgnoreCase)));

        private void WriteReproducer(string bucket, List<Token> originalTokens, string query, Exception ex)
        {
            // Minimize on the original token stream (not a re-lexed string) so synthetic single-token
            // identifiers reproduce faithfully.
            var minimized = QueryMinimizer.Minimize(originalTokens, toks => Reproduces(toks, ex));
            var minimalQuery = QueryMinimizer.Render(minimized);

            // Stable, process-independent name so the same crash overwrites its own repro across runs
            // (string.GetHashCode is randomized per process). Prefixed with the seed to keep distinct
            // crashes from colliding.
            var hash = StableHash(bucket + "\n" + ex.GetType().Name + "\n" + minimalQuery);
            var reproPath = Path.Combine(_fuzzLogDir, "reproducers", $"{_seed}-{bucket}-{hash}.repro.sql");
            var content =
                $"-- Bucket: {bucket}\n" +
                $"-- Exception: {ex.GetType().Name}\n" +
                $"-- Message: {ex.Message}\n" +
                $"-- Seed: {_seed} (rerun with ETLSQL_FUZZ_SEED={_seed})\n" +
                $"-- Data shape: {_dataShape}\n" +
                $"-- Original Query: {query}\n\n{minimalQuery}";
            File.WriteAllText(reproPath, content);
        }

        private bool Reproduces(List<Token> tokens, Exception ex)
        {
            try
            {
                var parsed = new Parser(new List<Token>(tokens), QueryMinimizer.Render(tokens)).Parse();
                _evaluator.LastResult = null;
                _evaluator.Evaluate(parsed).GetAwaiter().GetResult();

                if (ex is NoRecMismatchException && parsed.Statements.FirstOrDefault() is SelectStatement selectStmt)
                {
                    VerifyNoRECParity(selectStmt).GetAwaiter().GetResult();
                }
                return false;
            }
            catch (Exception testEx)
            {
                return IsSevereCrash(testEx) && testEx.GetType() == ex.GetType();
            }
        }

        /// <summary>
        /// A named tally of fuzzer outcomes. Buckets flagged <see cref="Bucket.IsBug"/> fail the test
        /// and get minimized reproducers; the rest are informational signals (parser diagnostics,
        /// grammar recall gaps) that separate suggestion/parser/execution progress from a single count.
        /// </summary>
        private sealed class FuzzResults
        {
            public Bucket ParserCrash { get; } = new("parser-crash", isBug: true);
            public Bucket ExecutionCrash { get; } = new("execution-crash", isBug: true);
            public Bucket DifferentialCorrectness { get; } = new("differential-correctness", isBug: true);
            public Bucket TemplateParserRejected { get; } = new("template-parser-rejected", isBug: true);
            public Bucket ParserDiagnostic { get; } = new("parser-diagnostic");
            public Bucket GrammarGeneratedParserRejected { get; } = new("grammar-generated-parser-rejected");
            // The grammar state tree feeds completion, so a statement the parser accepts and the tree
            // rejects is a suggestion that can never be offered: a defect (SQ09).
            public Bucket GrammarRejectedParserAccepted { get; } = new("grammar-rejected-parser-accepted", isBug: true);
            // The other direction stays a signal. The tree validates prefixes only (wildcard
            // transitions, no accepting states), so it accepts every truncated or semantically
            // invalid candidate the generator retries; most of the 850 or so per 2000 iterations are those.
            public Bucket GrammarAcceptedParserRejected { get; } = new("grammar-accepted-parser-rejected");

            private IEnumerable<Bucket> All => new[]
            {
                ParserCrash, ExecutionCrash, DifferentialCorrectness, TemplateParserRejected,
                ParserDiagnostic, GrammarGeneratedParserRejected,
                GrammarRejectedParserAccepted, GrammarAcceptedParserRejected
            };

            public IEnumerable<Bucket> BugBuckets => All.Where(b => b.IsBug);

            public string Summary() =>
                "[Fuzzer] buckets: " + string.Join(", ", All.Select(b => $"{b.Name}={b.Count}"));

            public string SampleSummary() =>
                "[Fuzzer] samples: " + string.Join(" | ",
                    BugBuckets.SelectMany(b => b.Samples.Select(s => $"{b.Name}: {s.Exception.GetType().Name}: {s.Exception.Message}; SQL={s.Query}")).Take(5));
        }

        private sealed class Bucket
        {
            private const int MaxSamples = 5;
            private readonly List<(List<Token> Tokens, string Query, Exception Exception)> _samples = new();

            public Bucket(string name, bool isBug = false)
            {
                Name = name;
                IsBug = isBug;
            }

            public string Name { get; }
            public bool IsBug { get; }
            public int Count { get; private set; }
            public IReadOnlyList<(List<Token> Tokens, string Query, Exception Exception)> Samples => _samples;

            public void Increment() => Count++;

            public void Record(List<Token> tokens, string query, Exception ex)
            {
                Count++;
                if (_samples.Count < MaxSamples)
                {
                    _samples.Add((new List<Token>(tokens), query, ex));
                }
            }
        }

        private static string StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s)
                {
                    h = (h ^ c) * 16777619;
                }
                return h.ToString("X8");
            }
        }
    }
}
