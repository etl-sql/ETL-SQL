using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.App;
using ETL_SQL.Data;
using ETL_SQL.Engine;

namespace ETL_SQL.FuzzTests
{
    public class ParserFuzzTests : IAsyncLifetime
    {
        private IServiceProvider _serviceProvider = null!;
        private Evaluator _evaluator = null!;
        private string _fuzzLogDir = null!;

        public async Task InitializeAsync()
        {
            // Setup DI container with temp user snippets path
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
            {
                ["Snippets:UserSnippetsPath"] = Path.Combine(Path.GetTempPath(), "etlsql-fuzz-snippets")
            });

            _evaluator = _serviceProvider.GetRequiredService<Evaluator>();

            // Create MOCKDB connection - this automatically seeds high-fidelity mock tables out-of-the-box!
            var setupQuery = "CREATE CONNECTION src AS MOCKDB();";
            await _evaluator.Evaluate(new Parser(new Lexer(setupQuery).Tokenize(), setupQuery).Parse());

            // Create fuzz logging directory
            _fuzzLogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "fuzz");
            Directory.CreateDirectory(Path.Combine(_fuzzLogDir, "reproducers"));
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public async Task RunFuzzer()
        {
            var tree = DefaultGrammar.Build();
            var generator = new GrammarWalkGenerator(tree, new Random());
            int iterations = 500; // Increased iterations for much deeper engine coverage
            int crashCount = 0;

            for (int i = 0; i < iterations; i++)
            {
                var tokens = generator.GenerateQuery();
                var query = string.Join(" ", tokens.Where(t => t.Type != TokenType.EOF).Select(t => t.Value));
                if (string.IsNullOrWhiteSpace(query)) continue;

                try
                {
                    var parsed = new Parser(tokens, query).Parse();
                    if (parsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        // Generated query was syntactically invalid - skip
                        continue;
                    }

                    Exception? engineEx = null;

                    try
                    {
                        _evaluator.LastResult = null;
                        await _evaluator.Evaluate(parsed);
                    }
                    catch (Exception ex)
                    {
                        engineEx = ex;
                    }

                    if (engineEx != null)
                    {
                        if (IsSevereCrash(engineEx))
                        {
                            crashCount++;
                            HandleCrash(query, engineEx);
                        }
                    }
                }
                catch
                {
                    // Ignore transient parsing or walk failures
                }
            }

            Assert.True(crashCount == 0, $"Fuzzer found {crashCount} severe crash bugs! Check logs/fuzz/reproducers/ for minimal reproducing SQL queries.");
        }

        private bool IsSevereCrash(Exception ex)
        {
            // Expected database errors/type mismatch errors are not crashes
            if (ex is SyntaxException || ex is ConnectionException || ex is ExecutionException || ex is DivideByZeroException || ex is OverflowException)
            {
                return false;
            }

            // Severe C# execution/optimizer faults
            return ex is NullReferenceException ||
                   ex is IndexOutOfRangeException ||
                   ex is InvalidCastException ||
                   ex is ArgumentOutOfRangeException ||
                   ex is KeyNotFoundException;
        }

        private void HandleCrash(string query, Exception ex)
        {
            var minimalQuery = QueryMinimizer.Minimize(query, q =>
            {
                try
                {
                    var tokens = new Lexer(q).Tokenize();
                    var parsed = new Parser(tokens, q).Parse();
                    var task = _evaluator.Evaluate(parsed);
                    task.GetAwaiter().GetResult();
                    return false;
                }
                catch (Exception testEx)
                {
                    return IsSevereCrash(testEx) && testEx.GetType() == ex.GetType();
                }
            });

            var hash = ex.StackTrace?.GetHashCode().ToString("X") ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            var reproPath = Path.Combine(_fuzzLogDir, "reproducers", $"{hash}.repro.sql");
            var content = $"-- Exception: {ex.GetType().Name}\n-- Message: {ex.Message}\n-- Original Query: {query}\n\n{minimalQuery}";
            File.WriteAllText(reproPath, content);
        }
    }
}
