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
    public class NoRecMismatchException : Exception
    {
        public NoRecMismatchException(string message) : base(message) { }
    }

    public class ParserFuzzTests : IAsyncLifetime
    {
        private IServiceProvider _serviceProvider = null!;
        private Evaluator _evaluator = null!;
        private string _fuzzLogDir = null!;
        private string _fuzzTableName = null!;

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

            // 3. Seed dynamic table
            var insertQuery = $"INSERT INTO src.{_fuzzTableName} VALUES (1, 10.5, 'Alice', 100.0), (2, 20.0, 'Bob', 250.0), (3, null, null, null);";
            await _evaluator.Evaluate(new Parser(new Lexer(insertQuery).Tokenize(), insertQuery).Parse());

            // 4. Create fuzz logging directory
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
            
            // Register dynamic schema in the walk generator
            generator.AddCustomSchema(_fuzzTableName, new[] { "ID", "Price", "Name", "TotalAmount" });

            int iterations = 500;
            int crashCount = 0;
            var rng = new Random();

            for (int i = 0; i < iterations; i++)
            {
                var tokens = generator.GenerateQuery();
                bool isCorrupted = false;

                // 5% chance of token mutation/corruption to verify parser robustness
                if (rng.Next(100) < 5)
                {
                    generator.CorruptQuery(tokens);
                    isCorrupted = true;
                }

                var query = string.Join(" ", tokens.Where(t => t.Type != TokenType.EOF).Select(t => t.Value));
                if (string.IsNullOrWhiteSpace(query)) continue;

                try
                {
                    var parsed = new Parser(tokens, query).Parse();

                    // If not corrupted, skip standard compilation diagnostics
                    if (!isCorrupted && parsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        continue;
                    }

                    Exception? engineEx = null;

                    try
                    {
                        _evaluator.LastResult = null;
                        await _evaluator.Evaluate(parsed);

                        // If it is a clean SELECT statement, run NoREC correctness verification
                        if (!isCorrupted && parsed.Statements.FirstOrDefault() is SelectStatement selectStmt)
                        {
                            await VerifyNoRECParity(selectStmt);
                        }
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
                catch (Exception ex)
                {
                    // If corrupted, parsing failures are expected.
                    // But if it triggers a severe unhandled crash (like NullReferenceException), log it!
                    if (IsSevereCrash(ex))
                    {
                        crashCount++;
                        HandleCrash(query, ex);
                    }
                }
            }

            Assert.True(crashCount == 0, $"Fuzzer found {crashCount} severe crash/correctness bugs! Check logs/fuzz/reproducers/ for minimal reproducing SQL queries.");
        }

        private bool IsSevereCrash(Exception ex)
        {
            if (ex is NoRecMismatchException)
            {
                return true;
            }

            // Expected exceptions are not severe crashes
            if (ex is SyntaxException || ex is ConnectionException || ex is ExecutionException || ex is DivideByZeroException || ex is OverflowException)
            {
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

            // 1. Build Query 1 (Optimized COUNT)
            var countCol = new SelectColumn(
                new FunctionCallExpression("COUNT", new List<Expression> { new LiteralExpression(1, TokenType.NUMBER) }),
                "cnt",
                null
            );
            var q1 = selectStmt with { Columns = new List<SelectColumn> { countCol } };
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
            var q2 = selectStmt with { Columns = new List<SelectColumn> { sumCol }, WhereClause = null };
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
            }
            catch (Exception ex) when (!(ex is NoRecMismatchException))
            {
                // Ignore general evaluation errors inside generated NoREC queries (expected database exceptions)
            }
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

                    if (ex is NoRecMismatchException && parsed.Statements.FirstOrDefault() is SelectStatement selectStmt)
                    {
                        // Verify if the mismatch still reproduces
                        VerifyNoRECParity(selectStmt).GetAwaiter().GetResult();
                    }
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
