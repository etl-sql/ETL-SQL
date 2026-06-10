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

        // ── USE DATASET — PRIVATE ACL enforcement via threaded caller identity (engine) ──

        [Fact]
        public async Task UseDataset_PrivateWithoutAccess_Denied()
        {
            // 1c: the handler threads the executing user's real CallerContext into the registry,
            // which ACL-gates PRIVATE datasets. A non-owner cannot resolve a PRIVATE dataset by its
            // global name — Lookup returns null and USE surfaces "not found" (existence not leaked).
            var registry = new SingleDatasetRegistry(ownerUserId: 1);
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
            eval.DatasetRegistry      = registry;
            eval.DatasetCallerContext = "UserId=99";   // not the owner

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("USE DATASET &secret;")));
            Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);

            // The handler forwarded the real caller identity — not the old "IsAdmin=true" literal.
            Assert.Equal("UserId=99", registry.LastLookupPermissions);
        }

        [Fact]
        public async Task ShowDatasets_ForwardsCallerContextToRegistry()
        {
            // The SHOW DATASETS handler must list only what the caller may see — it forwards the
            // evaluator's caller context to ListAll rather than spoofing admin.
            var registry = new SingleDatasetRegistry(ownerUserId: 1);

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry      = registry;
            eval.DatasetCallerContext = "UserId=7";

            await eval.Evaluate(Parse("SHOW DATASETS;"));

            Assert.Equal("UserId=7", registry.LastListAllPermissions);
        }

        // ── 1d: refresh/edit gates + serve-stale ──────────────────────────────────

        [Fact]
        public async Task RefreshDataset_NonEditor_Denied()
        {
            // A viewer who can read a dataset cannot REFRESH it (re-materialise) — editor/owner only.
            var registry = new SingleDatasetRegistry(ownerUserId: 1);
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = "&pub", FolderPath = "/f", ParquetFilePath = "pub_1.parquet",
                SourceQuery = "SELECT 1 AS v", AccessLevel = DatasetAccessLevel.Public, LastRefresh = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry      = registry;
            eval.DatasetCallerContext = "UserId=99";   // can read (PUBLIC) but not edit

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("REFRESH DATASET &pub;")));
            Assert.Contains("editor or owner", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CreateOrAlterDataset_NonEditor_Denied()
        {
            // Redefining an existing dataset via CREATE OR ALTER requires editor/owner.
            var registry = new SingleDatasetRegistry(ownerUserId: 1);
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = "&pub", FolderPath = "/f", ParquetFilePath = "pub_1.parquet",
                SourceQuery = "SELECT 1 AS v", AccessLevel = DatasetAccessLevel.Public, LastRefresh = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry      = registry;
            eval.DatasetCallerContext = "UserId=99";

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("CREATE OR ALTER DATASET &pub AS (SELECT 1 AS v FROM t);")));
            Assert.Contains("editor or owner", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UseDataset_StaleCache_ServesCachedSnapshotWithoutReRun()
        {
            // USE never re-materialises: a stale cache is served as-is. Proven by consuming from a
            // second evaluator that has NO seed table — if USE re-ran the source it would fail.
            var root = Path.Combine(Path.GetTempPath(), "etlsql_ds_stale_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            try
            {
                var registry = new SingleDatasetRegistry(ownerUserId: 1, root: root);

                var producer = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                producer.DatasetRegistry      = registry;
                producer.DatasetCallerContext = "IsAdmin=true";
                await producer.Evaluate(Parse(@"
                    CREATE TABLE #seed (v INT);
                    INSERT INTO #seed VALUES (10);
                    INSERT INTO #seed VALUES (20);
                    CREATE DATASET &sales TTL = '1h' AS (SELECT v FROM #seed);"));

                // Force the cache stale.
                registry.Stored("&sales").LastRefresh = DateTime.UtcNow.AddHours(-2);

                // Fresh evaluator (no #seed) consumes the dataset. CREATE DATASET defaults to PRIVATE,
                // so read as admin; the point is that USE serves the stale parquet without re-running.
                var consumer = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                consumer.DatasetRegistry      = registry;
                consumer.DatasetCallerContext = "IsAdmin=true";
                await consumer.Evaluate(Parse("USE DATASET &sales; SELECT COUNT(*) AS n, SUM(v) AS s FROM &sales;"));

                var row = consumer.LastResult!.Rows[0];
                Assert.Equal(2m, Convert.ToDecimal(row["n"]));
                Assert.Equal(30m, Convert.ToDecimal(row["s"]));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task UseDataset_NeverMaterialized_Errors()
        {
            // USE of a registered dataset whose parquet file is absent errors (it does not re-run the
            // source under the consumer's identity).
            var registry = new SingleDatasetRegistry(ownerUserId: 1);
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = "&ghost", FolderPath = "/f", ParquetFilePath = "does_not_exist.parquet",
                SourceQuery = "SELECT 1 AS v", AccessLevel = DatasetAccessLevel.Public, LastRefresh = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry      = registry;
            eval.DatasetCallerContext = "IsAdmin=true";

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("USE DATASET &ghost;")));
            Assert.Contains("materialised", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Minimal IDatasetRegistry holding a single dataset, resolved by name. Mimics the real
        /// registry's ACL gate: PUBLIC always resolves; PRIVATE resolves only for admin or the owner.
        /// Records the last caller-permission string each method received.
        /// </summary>
        private sealed class SingleDatasetRegistry(int ownerUserId, string? root = null) : IDatasetRegistry
        {
            private readonly Dictionary<string, DatasetMetadata> _items = new();
            private int _nextId = 1;

            public string? LastLookupPermissions { get; private set; }
            public string? LastListAllPermissions { get; private set; }

            public DatasetMetadata Stored(string name) => _items[name];

            public Task<int> RegisterOrUpdate(DatasetMetadata metadata)
            {
                if (metadata.Id == 0) metadata.Id = _nextId++;
                _items[metadata.Name] = metadata;
                return Task.FromResult(metadata.Id);
            }

            public Task<DatasetMetadata?> Lookup(string name, string callerPermissions = "")
            {
                LastLookupPermissions = callerPermissions;
                if (!_items.TryGetValue(name, out var m)) return Task.FromResult<DatasetMetadata?>(null);
                if (m.AccessLevel == DatasetAccessLevel.Public || CanRead(callerPermissions))
                    return Task.FromResult<DatasetMetadata?>(m);
                return Task.FromResult<DatasetMetadata?>(null);
            }

            public Task<bool> Exists(string name) => Task.FromResult(_items.ContainsKey(name));
            public Task SetStale(string name) => Task.CompletedTask;

            // Edit access mirrors the real registry: admin or the owner (folder/PUBLIC read does not grant edit).
            public Task<bool> CanEditAsync(string name, string callerPermissions) =>
                Task.FromResult(_items.ContainsKey(name) && CanWrite(callerPermissions));

            public Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions)
            {
                LastListAllPermissions = callerPermissions;
                var visible = _items.Values.Where(m => m.AccessLevel == DatasetAccessLevel.Public || CanRead(callerPermissions));
                return Task.FromResult<IEnumerable<DatasetMetadata>>(visible.ToList());
            }

            public Task Delete(string name) => Task.CompletedTask;

            public string BuildDatasetFilePath(int datasetId, string name)
            {
                var safe = name.TrimStart('&', '#');
                return root is null ? $"{safe}_{datasetId}.parquet" : Path.Combine(root, $"{safe}_{datasetId}.parquet");
            }

            private bool CanRead(string callerPermissions) =>
                callerPermissions == "IsAdmin=true" || callerPermissions == $"UserId={ownerUserId}";

            private bool CanWrite(string callerPermissions) =>
                callerPermissions == "IsAdmin=true" || callerPermissions == $"UserId={ownerUserId}";
        }
    }
}
