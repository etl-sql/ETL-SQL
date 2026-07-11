using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Connectors.MockDb;
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
        private SqliteConnection _sqliteConnection = null!;
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

            // Create MOCKDB connection
            var setupQuery = "CREATE CONNECTION src AS MOCKDB();";
            await _evaluator.Evaluate(new Parser(new Lexer(setupQuery).Tokenize(), setupQuery).Parse());

            // Initialize SQLite reference DB
            _sqliteConnection = new SqliteConnection("Data Source=:memory:");
            await _sqliteConnection.OpenAsync();

            // Register custom functions in SQLite to align dialects
            _sqliteConnection.CreateFunction("GETDATE", () => DateTime.UtcNow);
            _sqliteConnection.CreateFunction("ISNULL", (object? a, object? b) => a ?? b);
            _sqliteConnection.CreateFunction("CONCAT", (object? a, object? b) => $"{a}{b}");

            // Seed both MOCKDB and SQLite with the exact same mock datasets
            var seeder = new MockDataSeeder();
            var tables = new Dictionary<string, DataTable>();
            await seeder.SeedDataAsync(tables, new Random(42));

            foreach (var pair in tables)
            {
                var dt = pair.Value;
                var name = pair.Key;
                var cols = dt.ColumnNames;

                // Build CREATE TABLE query
                var firstRow = dt.Rows.FirstOrDefault();
                var createColumns = new List<string>();
                foreach (var col in cols)
                {
                    var val = firstRow?[col];
                    var t = val switch
                    {
                        null => "TEXT",
                        int or long or short or byte => "INTEGER",
                        double or float or decimal => "REAL",
                        bool => "INTEGER",
                        _ => "TEXT"
                    };
                    createColumns.Add($"[{col}] {t}");
                }

                var createSql = $"CREATE TABLE [{name}] ({string.Join(", ", createColumns)});";
                using (var createCmd = _sqliteConnection.CreateCommand())
                {
                    createCmd.CommandText = createSql;
                    await createCmd.ExecuteNonQueryAsync();
                }

                // Seed SQLite records
                foreach (var row in dt.Rows)
                {
                    var colList = string.Join(", ", cols.Select(c => $"[{c}]"));
                    var paramList = string.Join(", ", cols.Select(c => $"@{c}"));
                    var insertSql = $"INSERT INTO [{name}] ({colList}) VALUES ({paramList});";

                    using (var insCmd = _sqliteConnection.CreateCommand())
                    {
                        insCmd.CommandText = insertSql;
                        foreach (var col in cols)
                        {
                            var val = row[col];
                            if (val is Guid g) val = g.ToString();
                            else if (val is DateTimeOffset dto) val = dto.ToString("o");
                            else if (val is TimeSpan ts) val = ts.ToString();

                            insCmd.Parameters.AddWithValue($"@{col}", val ?? DBNull.Value);
                        }
                        await insCmd.ExecuteNonQueryAsync();
                    }
                }
            }

            // Create logging root for reproducing cases
            _fuzzLogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "fuzz");
            Directory.CreateDirectory(Path.Combine(_fuzzLogDir, "reproducers"));
            Directory.CreateDirectory(Path.Combine(_fuzzLogDir, "correctness"));
        }

        public async Task DisposeAsync()
        {
            if (_sqliteConnection != null)
            {
                await _sqliteConnection.CloseAsync();
                await _sqliteConnection.DisposeAsync();
            }
        }

        [Fact]
        public async Task RunFuzzer()
        {
            var tree = DefaultGrammar.Build();
            var generator = new GrammarWalkGenerator(tree, new Random());
            int iterations = 100;
            int crashCount = 0;
            int correctnessCount = 0;

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
                        continue;
                    }

                    DataTable? engineResult = null;
                    Exception? engineEx = null;

                    try
                    {
                        _evaluator.LastResult = null;
                        await _evaluator.Evaluate(parsed);
                        engineResult = _evaluator.LastResult;
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
                        continue;
                    }

                    if (engineResult != null)
                    {
                        var sqliteQuery = query.Replace("src.", "");
                        DataTable? sqliteResult = null;
                        Exception? sqliteEx = null;

                        try
                        {
                            sqliteResult = await RunSqliteQuery(sqliteQuery);
                        }
                        catch (Exception ex)
                        {
                            sqliteEx = ex;
                        }

                        if (sqliteEx == null && sqliteResult != null)
                        {
                            var match = CompareResults(engineResult, sqliteResult, out var mismatchReason);
                            if (!match)
                            {
                                correctnessCount++;
                                HandleCorrectnessMismatch(query, engineResult, sqliteResult, mismatchReason);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore transient parsing or walk failures
                }
            }

            Assert.True(crashCount == 0, $"Fuzzer found {crashCount} severe crash bugs! Check logs/fuzz/reproducers/ for minimal reproducing SQL queries.");
            Assert.True(correctnessCount == 0, $"Fuzzer found {correctnessCount} correctness mismatch bugs! Check logs/fuzz/correctness/ for details.");
        }

        private bool IsSevereCrash(Exception ex)
        {
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

        private void HandleCorrectnessMismatch(string query, DataTable engineRes, DataTable sqliteRes, string reason)
        {
            var hash = query.GetHashCode().ToString("X");
            var path = Path.Combine(_fuzzLogDir, "correctness", $"{hash}.correctness.txt");
            var content = $"Mismatch Reason: {reason}\nQuery: {query}\n\nETL-SQL Result Row Count: {engineRes.Rows.Count}\nSQLite Result Row Count: {sqliteRes.Rows.Count}";
            File.WriteAllText(path, content);
        }

        private async Task<DataTable> RunSqliteQuery(string sql)
        {
            using var cmd = _sqliteConnection.CreateCommand();
            cmd.CommandText = sql;
            using var reader = await cmd.ExecuteReaderAsync();

            var dt = new DataTable();
            var cols = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                cols.Add(reader.GetName(i));
            }
            dt.SetColumns(cols);

            while (await reader.ReadAsync())
            {
                var row = new Row(dt.Schema);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var val = reader.GetValue(i);
                    row[name] = val == DBNull.Value ? null : val;
                }
                await dt.AddRowAsync(row);
            }
            return dt;
        }

        private bool CompareResults(DataTable engine, DataTable sqlite, out string mismatchReason)
        {
            mismatchReason = "";
            if (engine.Rows.Count != sqlite.Rows.Count)
            {
                mismatchReason = $"Row count mismatch. Engine={engine.Rows.Count}, SQLite={sqlite.Rows.Count}";
                return false;
            }

            if (engine.ColumnNames.Count != sqlite.ColumnNames.Count)
            {
                mismatchReason = $"Column count mismatch. Engine={engine.ColumnNames.Count}, SQLite={sqlite.ColumnNames.Count}";
                return false;
            }

            var engineSorted = engine.Rows.Select(r => string.Join("|", engine.ColumnNames.Select(c => r[c]?.ToString() ?? "NULL"))).OrderBy(x => x).ToList();
            var sqliteSorted = sqlite.Rows.Select(r => string.Join("|", sqlite.ColumnNames.Select(c => r[c]?.ToString() ?? "NULL"))).OrderBy(x => x).ToList();

            for (int i = 0; i < engineSorted.Count; i++)
            {
                if (engineSorted[i] != sqliteSorted[i])
                {
                    mismatchReason = $"Row {i} content mismatch. Engine={engineSorted[i]}, SQLite={sqliteSorted[i]}";
                    return false;
                }
            }

            return true;
        }
    }
}
