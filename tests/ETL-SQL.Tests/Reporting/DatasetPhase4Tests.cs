using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Phase 4 DATASET tests: ACCESS PUBLIC|PRIVATE syntax, UseBeforeCreateRule lint warning,
    /// and private access violation enforcement in UseDatasetStatementHandler.
    /// </summary>
    public class DatasetPhase4Tests
    {
        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens).Parse();
        }

        // ── Parser — ACCESS clause ────────────────────────────────────────────────

        [Fact]
        public void CreateDataset_AccessPublic_SetsPublicLevel()
        {
            var script = Parse("CREATE DATASET &sales ACCESS PUBLIC AS (SELECT 1 AS v FROM t);");
            var stmt   = Assert.Single(script.Statements);
            var ds     = Assert.IsType<CreateDatasetStatement>(stmt);
            Assert.Equal(DatasetAccessLevel.Public, ds.AccessLevel);
        }

        [Fact]
        public void CreateDataset_AccessPrivate_SetsPrivateLevel()
        {
            var script = Parse("CREATE DATASET &sales ACCESS PRIVATE AS (SELECT 1 AS v FROM t);");
            var stmt   = Assert.Single(script.Statements);
            var ds     = Assert.IsType<CreateDatasetStatement>(stmt);
            Assert.Equal(DatasetAccessLevel.Private, ds.AccessLevel);
        }

        [Fact]
        public void CreateDataset_NoAccessClause_DefaultsToPrivate()
        {
            var script = Parse("CREATE DATASET &sales AS (SELECT 1 AS v FROM t);");
            var stmt   = Assert.Single(script.Statements);
            var ds     = Assert.IsType<CreateDatasetStatement>(stmt);
            Assert.Equal(DatasetAccessLevel.Private, ds.AccessLevel);
        }

        [Fact]
        public void CreateDataset_InvalidAccessValue_ProducesDiagnosticError()
        {
            // Parser uses error recovery: SyntaxException is caught and stored in Diagnostics
            var script = Parse("CREATE DATASET &sales ACCESS RESTRICTED AS (SELECT 1 FROM t);");
            Assert.NotEmpty(script.Diagnostics);
            Assert.Contains(script.Diagnostics, d => d.Message.Contains("PUBLIC or PRIVATE"));
        }

        [Fact]
        public void CreateDataset_AccessClauseWithOtherOptions_ParsesAll()
        {
            var sql    = "CREATE DATASET &sales TTL = '1h' ACCESS PUBLIC ENCRYPT = MACHINE AS (SELECT 1 AS v FROM t);";
            var script = Parse(sql);
            var ds     = Assert.IsType<CreateDatasetStatement>(Assert.Single(script.Statements));
            Assert.Equal(DatasetAccessLevel.Public, ds.AccessLevel);
            Assert.Equal("1h", ds.Ttl);
            Assert.Equal(DatasetEncryptionMode.MachineBound, ds.EncryptionMode);
        }

        // ── UseBeforeCreateRule ───────────────────────────────────────────────────

        [Fact]
        public async Task UseBeforeCreateRule_UseBeforeCreate_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new UseBeforeCreateRule());

            var sql = @"
                USE DATASET &sales;
                CREATE DATASET &sales AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Equal("UseBeforeCreate", results[0].RuleName);
            Assert.Contains("&sales", results[0].Message);
        }

        [Fact]
        public async Task UseBeforeCreateRule_UseAfterCreate_NoWarning()
        {
            var linter = new Linter();
            linter.AddRule(new UseBeforeCreateRule());

            var sql = @"
                CREATE DATASET &sales AS (SELECT 1 AS v FROM t);
                USE DATASET &sales;";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task UseBeforeCreateRule_UseWithoutCreate_NoWarning()
        {
            // USE referencing an external dataset that isn't in this script
            var linter = new Linter();
            linter.AddRule(new UseBeforeCreateRule());

            var sql = "USE DATASET &external;";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            // No warning — we can't know whether &external exists; cross-file analysis is Phase 5+
            Assert.Empty(results);
        }

        [Fact]
        public async Task UseBeforeCreateRule_MultipleDatasets_OnlyFlagsBadOrdering()
        {
            var linter = new Linter();
            linter.AddRule(new UseBeforeCreateRule());

            // &good is fine (create before use), &bad is wrong (use before create)
            var sql = @"
                CREATE DATASET &good AS (SELECT 1 AS v FROM t);
                USE DATASET &bad;
                USE DATASET &good;
                CREATE DATASET &bad AS (SELECT 2 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Contains("&bad", results[0].Message);
        }

        [Fact]
        public async Task UseBeforeCreateRule_AutoDiscoveredByLinterFactory()
        {
            var linter = LinterFactory.CreateWithAllRules();
            var sql = @"
                USE DATASET &sales;
                CREATE DATASET &sales AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Contains(results, r => r.RuleName == "UseBeforeCreate" && r.Severity == LintSeverity.Warning);
        }

        // ── USE DATASET — PRIVATE cross-folder enforcement (engine) ───────────────

        [Fact]
        public async Task UseDataset_PrivateFromDifferentFolder_Throws()
        {
            // Regression for the 1a IDOR window: a PRIVATE dataset must not be resolvable by its
            // global name from a script outside its home folder. (Until 1c threads the real caller
            // identity, UseDatasetStatementHandler enforces this with a folder guard.)
            var registry = new SingleDatasetRegistry();
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name            = "&secret",
                FolderPath      = "/folder-a",
                ParquetFilePath = "secret_1.parquet",
                SourceQuery     = "SELECT 1 AS v",
                AccessLevel     = DatasetAccessLevel.Private,
                LastRefresh     = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry  = registry;
            eval.CurrentScriptPath = Path.Combine("/folder-b", "report.rptsql");

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("USE DATASET &secret;")));
            Assert.Contains("private", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Minimal IDatasetRegistry holding a single dataset, resolved by name.</summary>
        private sealed class SingleDatasetRegistry : IDatasetRegistry
        {
            private readonly Dictionary<string, DatasetMetadata> _items = new();
            private int _nextId = 1;

            public Task<int> RegisterOrUpdate(DatasetMetadata metadata)
            {
                if (metadata.Id == 0) metadata.Id = _nextId++;
                _items[metadata.Name] = metadata;
                return Task.FromResult(metadata.Id);
            }

            public Task<DatasetMetadata?> Lookup(string name, string callerPermissions = "")
                => Task.FromResult(_items.TryGetValue(name, out var m) ? m : null);

            public Task<bool> Exists(string name) => Task.FromResult(_items.ContainsKey(name));
            public Task SetStale(string name) => Task.CompletedTask;
            public Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions)
                => Task.FromResult<IEnumerable<DatasetMetadata>>(_items.Values.ToList());
            public Task Delete(string name) => Task.CompletedTask;
            public string BuildDatasetFilePath(int datasetId, string name) => $"{name}_{datasetId}.parquet";
        }
    }
}
