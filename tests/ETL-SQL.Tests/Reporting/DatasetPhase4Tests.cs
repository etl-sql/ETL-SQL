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
            var stmt = Assert.Single(script.Statements);
            var ds = Assert.IsType<CreateDatasetStatement>(stmt);
            Assert.Equal(DatasetAccessLevel.Public, ds.AccessLevel);
        }

        [Fact]
        public void CreateDataset_AccessPrivate_SetsPrivateLevel()
        {
            var script = Parse("CREATE DATASET &sales ACCESS PRIVATE AS (SELECT 1 AS v FROM t);");
            var stmt = Assert.Single(script.Statements);
            var ds = Assert.IsType<CreateDatasetStatement>(stmt);
            Assert.Equal(DatasetAccessLevel.Private, ds.AccessLevel);
        }

        [Fact]
        public void CreateDataset_NoAccessClause_DefaultsToPrivate()
        {
            var script = Parse("CREATE DATASET &sales AS (SELECT 1 AS v FROM t);");
            var stmt = Assert.Single(script.Statements);
            var ds = Assert.IsType<CreateDatasetStatement>(stmt);
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
            var sql = "CREATE DATASET &sales TTL = '1h' ACCESS PUBLIC ENCRYPT = MACHINE AS (SELECT 1 AS v FROM t);";
            var script = Parse(sql);
            var ds = Assert.IsType<CreateDatasetStatement>(Assert.Single(script.Statements));
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
                Name = "&secret",
                FolderPath = "/folder-a",
                ParquetFilePath = "secret_1.parquet",
                SourceQuery = "SELECT 1 AS v",
                AccessLevel = DatasetAccessLevel.Private,
                LastRefresh = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry = registry;
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
            eval.DatasetRegistry = registry;
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
                Name = "&pub",
                FolderPath = "/f",
                ParquetFilePath = "pub_1.parquet",
                SourceQuery = "SELECT 1 AS v",
                AccessLevel = DatasetAccessLevel.Public,
                LastRefresh = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry = registry;
            eval.DatasetCallerContext = "UserId=99";   // can read (PUBLIC) but not edit

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("REFRESH DATASET &pub;")));
            Assert.Contains("refresh, editor, or owner", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task CreateOrAlterDataset_NonEditor_Denied()
        {
            // Redefining an existing dataset via CREATE OR ALTER requires editor/owner.
            var registry = new SingleDatasetRegistry(ownerUserId: 1);
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = "&pub",
                FolderPath = "/f",
                ParquetFilePath = "pub_1.parquet",
                SourceQuery = "SELECT 1 AS v",
                AccessLevel = DatasetAccessLevel.Public,
                LastRefresh = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry = registry;
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
                producer.DatasetRegistry = registry;
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
                consumer.DatasetRegistry = registry;
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
                Name = "&ghost",
                FolderPath = "/f",
                ParquetFilePath = "does_not_exist.parquet",
                SourceQuery = "SELECT 1 AS v",
                AccessLevel = DatasetAccessLevel.Public,
                LastRefresh = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry = registry;
            eval.DatasetCallerContext = "IsAdmin=true";

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("USE DATASET &ghost;")));
            Assert.Contains("materialised", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── 1e: portal at-rest key ────────────────────────────────────────────────

        [Fact]
        public async Task UseDataset_AtRestKey_RoundTrips()
        {
            // With a portal at-rest key set, CREATE encrypts the parquet with it and USE decrypts it
            // with the same key — the cache is portal-bound, not host-bound.
            var root = Path.Combine(Path.GetTempPath(), "etlsql_ds_key_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            try
            {
                var registry = new SingleDatasetRegistry(ownerUserId: 1, root: root);
                const string key = "cG9ydGFsLWF0LXJlc3Qta2V5LTEyMzQ1Ng==";

                var producer = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                producer.DatasetRegistry = registry;
                producer.DatasetCallerContext = "IsAdmin=true";
                producer.DatasetAtRestKey = key;
                await producer.Evaluate(Parse(@"
                    CREATE TABLE #seed (v INT);
                    INSERT INTO #seed VALUES (10);
                    INSERT INTO #seed VALUES (20);
                    CREATE DATASET &enc AS (SELECT v FROM #seed);"));

                var consumer = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                consumer.DatasetRegistry = registry;
                consumer.DatasetCallerContext = "IsAdmin=true";
                consumer.DatasetAtRestKey = key;
                await consumer.Evaluate(Parse("USE DATASET &enc; SELECT COUNT(*) AS n, SUM(v) AS s FROM &enc;"));

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
        public async Task UseDataset_WrongAtRestKey_FailsToDecrypt()
        {
            // A consumer with the wrong at-rest key cannot read the cache (decrypt fails) — the file
            // is gated by the portal key, not host DPAPI.
            var root = Path.Combine(Path.GetTempPath(), "etlsql_ds_wkey_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            try
            {
                var registry = new SingleDatasetRegistry(ownerUserId: 1, root: root);

                var producer = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                producer.DatasetRegistry = registry;
                producer.DatasetCallerContext = "IsAdmin=true";
                producer.DatasetAtRestKey = "cG9ydGFsLWtleS1BLTAwMDAwMDAwMDA=";
                await producer.Evaluate(Parse(@"
                    CREATE TABLE #seed (v INT);
                    INSERT INTO #seed VALUES (1);
                    CREATE DATASET &enc AS (SELECT v FROM #seed);"));

                var consumer = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                consumer.DatasetRegistry = registry;
                consumer.DatasetCallerContext = "IsAdmin=true";
                consumer.DatasetAtRestKey = "cG9ydGFsLWtleS1CLTk5OTk5OTk5OTk=";   // different key

                await Assert.ThrowsAnyAsync<Exception>(
                    () => consumer.Evaluate(Parse("USE DATASET &enc;")));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        // ── 2a: EXPORT DATASET (portable transport-encrypted copy) ────────────────

        [Fact]
        public async Task ExportDataset_PasswordRoundTrips()
        {
            var root = Path.Combine(Path.GetTempPath(), "etlsql_ds_exp_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            var exportPath = Path.Combine(root, "sales_export.parquet").Replace('\\', '/');
            try
            {
                var registry = new SingleDatasetRegistry(ownerUserId: 1, root: root);
                const string atKey = "cG9ydGFsLWF0LXJlc3Qta2V5LWV4cG9ydA==";
                const string transport = "transport-secret-pw";

                var producer = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                producer.DatasetRegistry = registry;
                producer.DatasetCallerContext = "IsAdmin=true";
                producer.DatasetAtRestKey = atKey;
                await producer.Evaluate(Parse($@"
                    CREATE TABLE #seed (v INT);
                    INSERT INTO #seed VALUES (10);
                    INSERT INTO #seed VALUES (20);
                    CREATE DATASET &sales AS (SELECT v FROM #seed);
                    EXPORT DATASET &sales TO '{exportPath}' ENCRYPT = PASSWORD PASSWORD = '{transport}';"));

                Assert.True(File.Exists(exportPath));

                // The export is transport-encrypted (not the at-rest key): read it back via a PARQUET
                // connection with the transport password.
                var reader = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                await reader.Evaluate(Parse(
                    $"CREATE CONNECTION expconn AS PARQUET('{exportPath}', ENCRYPT = 'PASSWORD', PASSWORD = '{transport}'); " +
                    "SELECT COUNT(*) AS n, SUM(v) AS s FROM expconn.FILE;"));

                var row = reader.LastResult!.Rows[0];
                Assert.Equal(2m, Convert.ToDecimal(row["n"]));
                Assert.Equal(30m, Convert.ToDecimal(row["s"]));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ExportDataset_MissingCredential_Errors()
        {
            var registry = new SingleDatasetRegistry(ownerUserId: 1);
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = "&sales",
                FolderPath = "/f",
                ParquetFilePath = "sales_1.parquet",
                SourceQuery = "SELECT 1 AS v",
                AccessLevel = DatasetAccessLevel.Public,
                LastRefresh = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry = registry;
            eval.DatasetCallerContext = "IsAdmin=true";

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("EXPORT DATASET &sales TO 'out.parquet' ENCRYPT = PASSWORD;")));
            Assert.Contains("PASSWORD", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── 2b: EXPORT → PUBLISH round-trip (move between portals) ─────────────────

        [Fact]
        public async Task ExportThenPublish_AcrossPortals_RoundTrips()
        {
            var rootA = Path.Combine(Path.GetTempPath(), "etlsql_ds_pa_" + Guid.NewGuid().ToString("N")[..8]);
            var rootB = Path.Combine(Path.GetTempPath(), "etlsql_ds_pb_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(rootA);
            Directory.CreateDirectory(rootB);
            var exportPath = Path.Combine(rootA, "sales_export.parquet").Replace('\\', '/');
            try
            {
                const string transport = "move-me-secret";

                // Portal A: create with at-rest key A, then export with a transport credential.
                var registryA = new SingleDatasetRegistry(ownerUserId: 1, root: rootA);
                var portalA = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                portalA.DatasetRegistry = registryA;
                portalA.DatasetCallerContext = "IsAdmin=true";
                portalA.DatasetAtRestKey = "cG9ydGFsLUEtYXQtcmVzdC1rZXktMDAw";
                await portalA.Evaluate(Parse($@"
                    CREATE TABLE #seed (v INT);
                    INSERT INTO #seed VALUES (10);
                    INSERT INTO #seed VALUES (20);
                    CREATE DATASET &sales AS (SELECT v FROM #seed);
                    EXPORT DATASET &sales TO '{exportPath}' ENCRYPT = PASSWORD PASSWORD = '{transport}';"));

                // Portal B: a DIFFERENT at-rest key. Publish the portable file, then consume it.
                var registryB = new SingleDatasetRegistry(ownerUserId: 1, root: rootB);
                var portalB = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                portalB.DatasetRegistry = registryB;
                portalB.DatasetCallerContext = "IsAdmin=true";
                portalB.DatasetAtRestKey = "cG9ydGFsLUItYXQtcmVzdC1rZXktOTk5";
                await portalB.Evaluate(Parse(
                    $"PUBLISH DATASET FROM '{exportPath}' AS &imported INTO '/imports' ACCESS PUBLIC ENCRYPT = PASSWORD PASSWORD = '{transport}'; " +
                    "USE DATASET &imported; SELECT COUNT(*) AS n, SUM(v) AS s FROM &imported;"));

                var row = portalB.LastResult!.Rows[0];
                Assert.Equal(2m, Convert.ToDecimal(row["n"]));
                Assert.Equal(30m, Convert.ToDecimal(row["s"]));
                Assert.Equal(1, registryB.Stored("&imported").CreatedBy);
                Assert.Equal((null, "&imported", "/imports", true), registryB.LastPublishAudit);

                // The published copy is re-encrypted with portal B's at-rest key, not the transport
                // credential: reading it with the transport password must fail.
                var published = registryB.Stored("&imported").ParquetFilePath;
                Assert.True(File.Exists(published));
                var reader = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                await Assert.ThrowsAnyAsync<Exception>(() => reader.Evaluate(Parse(
                    $"CREATE CONNECTION badc AS PARQUET('{published.Replace('\\', '/')}', ENCRYPT = 'PASSWORD', PASSWORD = '{transport}'); " +
                    "SELECT COUNT(*) AS n FROM badc.FILE;")));
            }
            finally
            {
                try { Directory.Delete(rootA, recursive: true); } catch { }
                try { Directory.Delete(rootB, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ExportPublish_DoesNotPersistAtRestOrTransportSecrets()
        {
            var rootA = Path.Combine(Path.GetTempPath(), "etlsql_ds_secret_a_" + Guid.NewGuid().ToString("N")[..8]);
            var rootB = Path.Combine(Path.GetTempPath(), "etlsql_ds_secret_b_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(rootA);
            Directory.CreateDirectory(rootB);
            var exportPath = Path.Combine(rootA, "portable-export.parquet").Replace('\\', '/');
            const string atRestA = "AT_REST_SECRET_MARKER_ALPHA_0123456789";
            const string atRestB = "AT_REST_SECRET_MARKER_BRAVO_0123456789";
            const string transport = "TRANSPORT_SECRET_MARKER_0123456789";

            try
            {
                var registryA = new SingleDatasetRegistry(ownerUserId: 1, root: rootA);
                var portalA = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                portalA.DatasetRegistry = registryA;
                portalA.DatasetCallerContext = "UserId=1";
                portalA.DatasetAtRestKey = atRestA;
                await portalA.Evaluate(Parse($@"
                    CREATE DATASET &secret_source AS (SELECT 7 AS v);
                    EXPORT DATASET &secret_source TO '{exportPath}'
                        ENCRYPT = PASSWORD PASSWORD = '{transport}';"));

                var registryB = new SingleDatasetRegistry(ownerUserId: 2, root: rootB);
                var portalB = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                portalB.DatasetRegistry = registryB;
                portalB.DatasetCallerContext = "UserId=2";
                portalB.DatasetAtRestKey = atRestB;
                await portalB.Evaluate(Parse(
                    $"PUBLISH DATASET FROM '{exportPath}' AS &secret_import INTO '/imports' " +
                    $"ENCRYPT = PASSWORD PASSWORD = '{transport}';"));

                foreach (var metadata in new[]
                {
                    registryA.Stored("&secret_source"),
                    registryB.Stored("&secret_import")
                })
                {
                    var persisted = string.Join("|",
                        metadata.Name,
                        metadata.FolderPath,
                        metadata.ParquetFilePath,
                        metadata.SourceQuery,
                        metadata.ColumnSchema,
                        metadata.RefreshInterval,
                        metadata.Ttl,
                        metadata.AtRestKeyVersion);
                    Assert.DoesNotContain(atRestA, persisted, StringComparison.Ordinal);
                    Assert.DoesNotContain(atRestB, persisted, StringComparison.Ordinal);
                    Assert.DoesNotContain(transport, persisted, StringComparison.Ordinal);
                }

                foreach (var file in Directory.EnumerateFiles(rootA).Concat(Directory.EnumerateFiles(rootB)))
                {
                    Assert.DoesNotContain(atRestA, Path.GetFileName(file), StringComparison.Ordinal);
                    Assert.DoesNotContain(atRestB, Path.GetFileName(file), StringComparison.Ordinal);
                    Assert.DoesNotContain(transport, Path.GetFileName(file), StringComparison.Ordinal);
                    var bytes = await File.ReadAllBytesAsync(file);
                    Assert.False(ContainsUtf8(bytes, atRestA), $"{file} contains the source at-rest key.");
                    Assert.False(ContainsUtf8(bytes, atRestB), $"{file} contains the destination at-rest key.");
                    Assert.False(ContainsUtf8(bytes, transport), $"{file} contains the transport password.");
                }

                var wrong = await Assert.ThrowsAnyAsync<Exception>(() => portalB.Evaluate(Parse(
                    $"PUBLISH DATASET FROM '{exportPath}' AS &secret_failed INTO '/imports' " +
                    $"ENCRYPT = PASSWORD PASSWORD = '{transport}-wrong';")));
                Assert.DoesNotContain(transport, wrong.ToString(), StringComparison.Ordinal);
                Assert.False(await registryB.Exists("&secret_failed"));
            }
            finally
            {
                try { Directory.Delete(rootA, recursive: true); } catch { }
                try { Directory.Delete(rootB, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task PublishDataset_DuplicateName_Rejected()
        {
            var registry = new SingleDatasetRegistry(ownerUserId: 1);
            await registry.RegisterOrUpdate(new DatasetMetadata
            {
                Name = "&taken",
                FolderPath = "/f",
                ParquetFilePath = "taken_1.parquet",
                SourceQuery = "SELECT 1 AS v",
                AccessLevel = DatasetAccessLevel.Public,
                LastRefresh = DateTime.UtcNow
            });

            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            eval.DatasetRegistry = registry;
            eval.DatasetCallerContext = "IsAdmin=true";

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("PUBLISH DATASET FROM 'whatever.parquet' AS &taken ENCRYPT = PASSWORD PASSWORD = 'p';")));
            Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task PublishDataset_UnauthorizedFolder_DeniedBeforeAllocationAndAudited()
        {
            var root = Path.Combine(Path.GetTempPath(), "etlsql_ds_pubdeny_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            var sourcePath = Path.Combine(root, "portable.parquet");
            await File.WriteAllTextAsync(sourcePath, "not read because authorization fails");
            try
            {
                var registry = new SingleDatasetRegistry(ownerUserId: 1, root: root)
                {
                    AllowPublish = false
                };
                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                eval.DatasetRegistry = registry;
                eval.DatasetCallerContext = "UserId=7";

                var ex = await Assert.ThrowsAsync<ExecutionException>(() => eval.Evaluate(Parse(
                    $"PUBLISH DATASET FROM '{sourcePath.Replace('\\', '/')}' AS &denied INTO '/restricted' " +
                    "ENCRYPT = PASSWORD PASSWORD = 'transport-secret';")));

                Assert.Contains("Manage permission", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0, registry.RegisterCalls);
                Assert.Equal((7, "&denied", "/restricted", false), registry.LastPublishAudit);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task PublishDataset_WrongPassword_RollsBackRowAndPartialFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "etlsql_ds_pubrollback_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            var sourcePath = Path.Combine(root, "portable.parquet");
            await File.WriteAllTextAsync(sourcePath, "not encrypted with the supplied password");
            try
            {
                var registry = new SingleDatasetRegistry(ownerUserId: 1, root: root);
                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                eval.DatasetRegistry = registry;
                eval.DatasetCallerContext = "IsAdmin=true";
                eval.DatasetAtRestKey = "cG9ydGFsLWF0LXJlc3Qta2V5";

                await Assert.ThrowsAnyAsync<Exception>(() => eval.Evaluate(Parse(
                    $"PUBLISH DATASET FROM '{sourcePath.Replace('\\', '/')}' AS &retryable INTO '/imports' " +
                    "ENCRYPT = PASSWORD PASSWORD = 'wrong-password';")));

                Assert.False(await registry.Exists("&retryable"));
                Assert.Equal(1, registry.DeleteCalls);
                Assert.Empty(Directory.GetFiles(root, "retryable_*.parquet"));
                Assert.Empty(Directory.GetFiles(root, ".*.tmp-*.parquet"));
                Assert.Equal((null, "&retryable", "/imports", false), registry.LastPublishAudit);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task RefreshDataset_RegistryFailure_RestoresPreviousCache()
        {
            var root = Path.Combine(Path.GetTempPath(), "etlsql_ds_refreshrollback_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            try
            {
                const string atRestKey = "cG9ydGFsLXJlZnJlc2gtcm9sbGJhY2sta2V5";
                var registry = new SingleDatasetRegistry(ownerUserId: 1, root: root);
                var producer = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                producer.DatasetRegistry = registry;
                producer.DatasetCallerContext = "IsAdmin=true";
                producer.DatasetAtRestKey = atRestKey;
                await producer.Evaluate(Parse("CREATE DATASET &stable AS (SELECT 1 AS v);"));

                registry.Stored("&stable").SourceQuery = "SELECT 2 AS v";
                registry.FailNextRegister = true;

                var refresh = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                refresh.DatasetRegistry = registry;
                refresh.DatasetCallerContext = "IsAdmin=true";
                refresh.DatasetAtRestKey = atRestKey;
                await Assert.ThrowsAnyAsync<Exception>(
                    () => refresh.Evaluate(Parse("REFRESH DATASET &stable;")));

                var cachePath = registry.Stored("&stable").ParquetFilePath.Replace('\\', '/');
                var reader = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                await reader.Evaluate(Parse(
                    $"CREATE CONNECTION stable_cache AS PARQUET('{cachePath}', ENCRYPT = 'PASSWORD', PASSWORD = '{atRestKey}'); " +
                    "SELECT v FROM stable_cache.FILE;"));

                Assert.Equal(1m, Convert.ToDecimal(reader.LastResult!.Rows[0]["v"]));
                Assert.Empty(Directory.GetFiles(root, ".*.tmp-*.parquet"));
                Assert.Empty(Directory.GetFiles(root, ".*.bak-*.parquet"));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
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
            public bool AllowPublish { get; init; } = true;
            public int RegisterCalls { get; private set; }
            public int DeleteCalls { get; private set; }
            public bool FailNextRegister { get; set; }
            public (int? UserId, string DatasetName, string FolderPath, bool Succeeded)? LastPublishAudit { get; private set; }

            public DatasetMetadata Stored(string name) => _items[name];

            public Task<int> RegisterOrUpdate(DatasetMetadata metadata)
            {
                RegisterCalls++;
                if (FailNextRegister)
                {
                    FailNextRegister = false;
                    throw new InvalidOperationException("Injected registry failure.");
                }
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

            public Task<bool> CanRefreshAsync(string name, string callerPermissions) =>
                Task.FromResult(_items.ContainsKey(name) && CanWrite(callerPermissions));

            public Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions)
            {
                LastListAllPermissions = callerPermissions;
                var visible = _items.Values.Where(m => m.AccessLevel == DatasetAccessLevel.Public || CanRead(callerPermissions));
                return Task.FromResult<IEnumerable<DatasetMetadata>>(visible.ToList());
            }

            public Task Delete(string name)
            {
                DeleteCalls++;
                _items.Remove(name);
                return Task.CompletedTask;
            }

            public Task<DatasetPublishTarget?> AuthorizePublishAsync(
                string targetFolderPath,
                string callerPermissions)
            {
                return Task.FromResult<DatasetPublishTarget?>(
                    AllowPublish && !string.IsNullOrWhiteSpace(targetFolderPath)
                        ? new DatasetPublishTarget(1, targetFolderPath, ownerUserId)
                        : null);
            }

            public Task AuditPublishAsync(
                int? userId,
                string datasetName,
                string targetFolderPath,
                bool succeeded,
                string? failureReason = null)
            {
                LastPublishAudit = (userId, datasetName, targetFolderPath, succeeded);
                return Task.CompletedTask;
            }

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

        private static bool ContainsUtf8(byte[] bytes, string marker)
        {
            var needle = System.Text.Encoding.UTF8.GetBytes(marker);
            return bytes.AsSpan().IndexOf(needle) >= 0;
        }
    }
}
