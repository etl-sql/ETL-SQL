using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Common;
using ETL_SQL.Orchestrator.Storage;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class LineageCatalogTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SQLiteJobHistoryStore _store;

        public LineageCatalogTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"etlsql-lineage-{Guid.NewGuid():N}.db");
            _store = new SQLiteJobHistoryStore(_dbPath);
        }

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal"); } catch (IOException) { }
            try { if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm"); } catch (IOException) { }
        }

        private static Script ParseScript(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }

        private static LineageEntry MakeEntry(string target, string operation = "INSERT", IEnumerable<string>? sources = null, Dictionary<string, string>? tags = null) =>
            new LineageEntry(target, operation)
            {
                SourceTables = sources?.ToList() ?? new List<string>(),
                Metadata = tags ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

        // ── SaveLineageAsync + GetHistoryForTableAsync ─────────────────────────

        [Fact]
        public async Task SaveAndQuery_ReturnsEntriesForTable()
        {
            await _store.InitializeAsync();
            var runAt = DateTime.UtcNow;

            var entries = new[]
            {
                MakeEntry("Orders", "INSERT", new[] { "Staging" }),
                MakeEntry("Customers", "INSERT")
            };

            await ((ILineageCatalogStore)_store).SaveLineageAsync(entries, "DailyLoad", null, runAt);

            var history = (await ((ILineageCatalogStore)_store).GetHistoryForTableAsync("Orders")).ToList();

            Assert.Single(history);
            Assert.Equal("Orders", history[0].TargetTable);
            Assert.Equal("INSERT", history[0].Operation);
            Assert.Equal("DailyLoad", history[0].JobName);
            Assert.Contains("Staging", history[0].SourceTables);
        }

        [Fact]
        public async Task SaveAndQuery_PersistsColumnTransformAndDescription()
        {
            await _store.InitializeAsync();
            var catalog = (ILineageCatalogStore)_store;

            var entry = new LineageEntry("dataset:sales_snap", "SELECT")
            {
                TargetColumn = "total",
                SourceTables = new List<string> { "Sales" },
                SourceColumns = new List<string> { "Amount" },
                TransformationKind = TransformationKind.Aggregation,
                TransformationExpression = "SUM(Amount)",
                FunctionsApplied = new List<string> { "SUM" },
                DerivedFromDescriptions = "Amount: Sales amounts",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["pii"] = "true",
                    ["d"] = "Sales amounts",
                },
            };

            await catalog.SaveLineageAsync(new[] { entry }, "BuildSnap", null, DateTime.UtcNow);

            var hist = (await catalog.GetHistoryForTableAsync("dataset:sales_snap")).ToList();
            var e = Assert.Single(hist);
            Assert.Equal("total", e.TargetColumn);
            Assert.Equal(new[] { "Amount" }, e.SourceColumns);
            Assert.Equal("Aggregation", e.TransformationKind);
            Assert.Equal("SUM(Amount)", e.TransformationExpression);
            Assert.Equal(new[] { "SUM" }, e.FunctionsApplied);
            Assert.Equal("Amount: Sales amounts", e.DerivedFromDescriptions);
            Assert.Equal("true", e.Tags["pii"]);
        }

        [Fact]
        public async Task GetHistoryForTable_CaseInsensitive()
        {
            await _store.InitializeAsync();
            await ((ILineageCatalogStore)_store).SaveLineageAsync(
                new[] { MakeEntry("Orders") }, "Job1", null, DateTime.UtcNow);

            var result = await ((ILineageCatalogStore)_store).GetHistoryForTableAsync("orders");
            Assert.Single(result);
        }

        [Fact]
        public async Task MultipleRuns_OrderedMostRecentFirst()
        {
            await _store.InitializeAsync();
            var catalog = (ILineageCatalogStore)_store;

            var run1 = DateTime.UtcNow.AddMinutes(-5);
            var run2 = DateTime.UtcNow;

            await catalog.SaveLineageAsync(new[] { MakeEntry("Fact") }, "Job1", null, run1);
            await catalog.SaveLineageAsync(new[] { MakeEntry("Fact") }, "Job2", null, run2);

            var history = (await catalog.GetHistoryForTableAsync("Fact")).ToList();
            Assert.Equal(2, history.Count);
            Assert.Equal("Job2", history[0].JobName);
            Assert.Equal("Job1", history[1].JobName);
        }

        [Fact]
        public async Task Limit_ReturnsAtMostNEntries()
        {
            await _store.InitializeAsync();
            var catalog = (ILineageCatalogStore)_store;

            for (int i = 0; i < 5; i++)
                await catalog.SaveLineageAsync(new[] { MakeEntry("Tbl") }, $"Job{i}", null, DateTime.UtcNow);

            var history = await catalog.GetHistoryForTableAsync("Tbl", limit: 3);
            Assert.Equal(3, history.Count());
        }

        // ── GetHistoryForTagAsync ──────────────────────────────────────────────

        [Fact]
        public async Task SaveAndQuery_ReturnsEntriesByTag()
        {
            await _store.InitializeAsync();
            var catalog = (ILineageCatalogStore)_store;

            var piiEntry = MakeEntry("Customers", tags: new Dictionary<string, string> { ["pii"] = "true" });
            var noTagEntry = MakeEntry("Orders");

            await catalog.SaveLineageAsync(new[] { piiEntry, noTagEntry }, "LoadJob", null, DateTime.UtcNow);

            var results = (await catalog.GetHistoryForTagAsync("pii")).ToList();
            Assert.Single(results);
            Assert.Equal("Customers", results[0].TargetTable);
        }

        [Fact]
        public async Task GetHistoryForTag_WithValue_FiltersCorrectly()
        {
            await _store.InitializeAsync();
            var catalog = (ILineageCatalogStore)_store;

            var piiTrue = MakeEntry("Customers", tags: new Dictionary<string, string> { ["pii"] = "true" });
            var piiFalse = MakeEntry("Audit", tags: new Dictionary<string, string> { ["pii"] = "false" });

            await catalog.SaveLineageAsync(new[] { piiTrue, piiFalse }, null, null, DateTime.UtcNow);

            var results = (await catalog.GetHistoryForTagAsync("pii", tagValue: "true")).ToList();
            Assert.Single(results);
            Assert.Equal("Customers", results[0].TargetTable);
        }

        [Fact]
        public async Task GetHistoryForTag_ReturnsEmptyWhenNoMatch()
        {
            await _store.InitializeAsync();
            await ((ILineageCatalogStore)_store).SaveLineageAsync(
                new[] { MakeEntry("Orders") }, null, null, DateTime.UtcNow);

            var results = await ((ILineageCatalogStore)_store).GetHistoryForTagAsync("pii");
            Assert.Empty(results);
        }

        [Fact]
        public async Task ProtectedData_FindsTruthyAndProtectedClassificationTags()
        {
            await _store.InitializeAsync();
            var catalog = (ILineageCatalogStore)_store;

            await catalog.SaveLineageAsync(
                new[]
                {
                    MakeEntry("Customers", tags: new Dictionary<string, string> { ["pii"] = "true" }),
                    MakeEntry("Claims", tags: new Dictionary<string, string> { ["phi"] = "yes" }),
                    MakeEntry("Payments", tags: new Dictionary<string, string> { ["pci"] = "1" }),
                    MakeEntry("Contracts", tags: new Dictionary<string, string> { ["classification"] = "confidential" }),
                    MakeEntry("Users", tags: new Dictionary<string, string> { ["classification"] = "restricted" }),
                    MakeEntry("Public", tags: new Dictionary<string, string> { ["classification"] = "public" })
                },
                "ProtectedScan",
                null,
                DateTime.UtcNow);

            var protectedRows = LineageProtectedData
                .FromHistory(await catalog.GetRecentLineageAsync())
                .ToList();

            Assert.Equal(5, protectedRows.Count);
            Assert.Contains(protectedRows, e => e.TargetTable == "Customers" && e.ProtectionTags.Contains("@pii=true"));
            Assert.Contains(protectedRows, e => e.TargetTable == "Claims" && e.ProtectionTags.Contains("@phi=true"));
            Assert.Contains(protectedRows, e => e.TargetTable == "Payments" && e.ProtectionTags.Contains("@pci=true"));
            Assert.Contains(protectedRows, e => e.TargetTable == "Contracts" && e.ProtectionTags.Contains("@classification=confidential"));
            Assert.Contains(protectedRows, e => e.TargetTable == "Users" && e.ProtectionTags.Contains("@classification=restricted"));
            Assert.DoesNotContain(protectedRows, e => e.TargetTable == "Public");
        }

        [Fact]
        public async Task GetMissingMetadata_ReturnsLatestTargetsMissingRequiredStewardshipTags()
        {
            await _store.InitializeAsync();
            var catalog = (ILineageCatalogStore)_store;

            await catalog.SaveLineageAsync(
                new[]
                {
                    MakeEntry("Complete", tags: new Dictionary<string, string>
                    {
                        ["owner"] = "sales",
                        ["steward"] = "steward@example.com",
                        ["contact"] = "sales@example.com",
                        ["classification"] = "internal",
                        ["quality"] = "gold"
                    }),
                    MakeEntry("Missing", tags: new Dictionary<string, string>
                    {
                        ["owner"] = "finance",
                        ["classification"] = "restricted"
                    })
                },
                "StewardshipScan",
                "scripts/stewardship.etlsql",
                DateTime.UtcNow);

            var results = (await catalog.GetMissingMetadataAsync(
                StewardshipTagCatalog.RequiredStewardshipTags,
                limit: 10)).ToList();

            var row = Assert.Single(results);
            Assert.Equal("Missing", row.TargetTable);
            Assert.Contains("steward", row.MissingTags);
            Assert.Contains("contact", row.MissingTags);
            Assert.Contains("quality", row.MissingTags);
            Assert.DoesNotContain("owner", row.MissingTags);
            Assert.DoesNotContain("classification", row.MissingTags);
            Assert.Equal("finance", row.PresentTags["owner"]);
            Assert.Equal("StewardshipScan", row.JobName);
        }

        [Fact]
        public async Task GetHistoryForSource_ReturnsExactSourceMatches()
        {
            await _store.InitializeAsync();
            var catalog = (ILineageCatalogStore)_store;

            await catalog.SaveLineageAsync(
                new[]
                {
                    MakeEntry("#stage", sources: new[] { "sales.Orders" }),
                    MakeEntry("#other", sources: new[] { "sales.OrdersArchive" })
                },
                "SourceJob",
                "scripts/source.etlsql",
                DateTime.UtcNow);

            var results = (await catalog.GetHistoryForSourceAsync("sales.Orders")).ToList();

            Assert.Single(results);
            Assert.Equal("#stage", results[0].TargetTable);
        }

        [Fact]
        public async Task GetHistoryForSourceFile_ReturnsEntriesForSourceFileOrScriptPath()
        {
            await _store.InitializeAsync();
            var catalog = (ILineageCatalogStore)_store;
            var sourceEntry = MakeEntry("#stage");
            sourceEntry.SourceFile = "bundle/main.etlsql";

            await catalog.SaveLineageAsync(new[] { sourceEntry }, "BundleJob", "orch://bundle@1/main.etlsql", DateTime.UtcNow);
            await catalog.SaveLineageAsync(new[] { MakeEntry("#script") }, "ScriptJob", "scripts/load.etlsql", DateTime.UtcNow);

            var sourceFileResults = (await catalog.GetHistoryForSourceFileAsync("bundle/main.etlsql")).ToList();
            var scriptPathResults = (await catalog.GetHistoryForSourceFileAsync("scripts/load.etlsql")).ToList();

            Assert.Single(sourceFileResults);
            Assert.Equal("#stage", sourceFileResults[0].TargetTable);
            Assert.Single(scriptPathResults);
            Assert.Equal("#script", scriptPathResults[0].TargetTable);
        }

        // ── Ad-hoc run (null jobName) ─────────────────────────────────────────

        [Fact]
        public async Task AdHocRun_NullJobName_Stored()
        {
            await _store.InitializeAsync();
            await ((ILineageCatalogStore)_store).SaveLineageAsync(
                new[] { MakeEntry("Reports") }, null, null, DateTime.UtcNow);

            var history = (await ((ILineageCatalogStore)_store).GetHistoryForTableAsync("Reports")).ToList();
            Assert.Single(history);
            Assert.Null(history[0].JobName);
        }

        // ── Empty lineage — no rows written ───────────────────────────────────

        [Fact]
        public async Task EmptyEntries_NoRowsWritten()
        {
            await _store.InitializeAsync();
            await ((ILineageCatalogStore)_store).SaveLineageAsync(
                Array.Empty<LineageEntry>(), "NoOp", null, DateTime.UtcNow);

            var result = await ((ILineageCatalogStore)_store).GetHistoryForTableAsync("Anything");
            Assert.Empty(result);
        }

        // ── Idempotent initialisation ─────────────────────────────────────────

        [Fact]
        public async Task InitializeTwice_DoesNotThrow()
        {
            await _store.InitializeAsync();
            await _store.InitializeAsync();
        }

        // ── Parser round-trip: both new statements parse without error ─────────

        [Fact]
        public void Parser_ShowLineageHistoryForTable_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TABLE Orders;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTableStatement>(stmt);
            Assert.Equal("Orders", hist.TableName);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForTable_WithLimit_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TABLE Orders LIMIT 10;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTableStatement>(stmt);
            Assert.Equal("Orders", hist.TableName);
            Assert.Equal(10, hist.Limit);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForTag_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TAG pii;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTagStatement>(stmt);
            Assert.Equal("pii", hist.TagKey);
            Assert.Null(hist.TagValue);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForTag_WithValue_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TAG pii = 'true';");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTagStatement>(stmt);
            Assert.Equal("pii", hist.TagKey);
            Assert.Equal("true", hist.TagValue);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForTag_WithValueAndLimit_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TAG pii = 'true' LIMIT 25;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTagStatement>(stmt);
            Assert.Equal("pii", hist.TagKey);
            Assert.Equal("true", hist.TagValue);
            Assert.Equal(25, hist.Limit);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForMissingTags_WithAtLimitAndInto_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR MISSING TAGS AT ProdOrch LIMIT 25 INTO #missing;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForMissingTagsStatement>(stmt);
            Assert.Equal("ProdOrch", hist.At);
            Assert.Equal(25, hist.Limit);
            Assert.Equal("#missing", hist.IntoTable);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForTable_WithInto_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TABLE Orders INTO #result;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTableStatement>(stmt);
            Assert.Equal("Orders", hist.TableName);
            Assert.Equal("#result", hist.IntoTable);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForTable_WithAt_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TABLE Orders AT ProdOrch;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTableStatement>(stmt);
            Assert.Equal("Orders", hist.TableName);
            Assert.Equal("ProdOrch", hist.At);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForTable_WithAtAndLimitAndInto_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TABLE Orders AT ProdOrch LIMIT 50 INTO #h;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTableStatement>(stmt);
            Assert.Equal("Orders", hist.TableName);
            Assert.Equal("ProdOrch", hist.At);
            Assert.Equal(50, hist.Limit);
            Assert.Equal("#h", hist.IntoTable);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForTag_WithAt_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TAG pii = 'true' AT ProdOrch;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTagStatement>(stmt);
            Assert.Equal("pii", hist.TagKey);
            Assert.Equal("true", hist.TagValue);
            Assert.Equal("ProdOrch", hist.At);
        }

        [Fact]
        public void Parser_ShowLineageHistoryForTag_WithAtAndInto_Parses()
        {
            var script = ParseScript("SHOW LINEAGE HISTORY FOR TAG classification = 'restricted' AT ProdOrch LIMIT 100 INTO #r;");
            var stmt = Assert.Single(script.Statements);
            var hist = Assert.IsType<ShowLineageHistoryForTagStatement>(stmt);
            Assert.Equal("classification", hist.TagKey);
            Assert.Equal("restricted", hist.TagValue);
            Assert.Equal("ProdOrch", hist.At);
            Assert.Equal(100, hist.Limit);
            Assert.Equal("#r", hist.IntoTable);
        }

        [Fact]
        public void Parser_ShowProtectedData_WithLimitAndInto_Parses()
        {
            var script = ParseScript("SHOW PROTECTED DATA LIMIT 25 INTO #protected;");
            var stmt = Assert.Single(script.Statements);
            var protectedData = Assert.IsType<ShowProtectedDataStatement>(stmt);
            Assert.Equal(25, protectedData.Limit);
            Assert.Equal("#protected", protectedData.IntoTable);
        }

        [Fact]
        public void Parser_ShowProtectedData_WithAtLimitAndInto_Parses()
        {
            var script = ParseScript("SHOW PROTECTED DATA AT ProdPortal LIMIT 100 INTO #protected;");
            var stmt = Assert.Single(script.Statements);
            var protectedData = Assert.IsType<ShowProtectedDataStatement>(stmt);
            Assert.Equal("ProdPortal", protectedData.At);
            Assert.Equal(100, protectedData.Limit);
            Assert.Equal("#protected", protectedData.IntoTable);
        }

        [Fact]
        public void Parser_ShowPortalAudit_WithActionLimitAndInto_Parses()
        {
            var script = ParseScript("SHOW PORTAL AUDIT ACTION 'STEWARD_LINEAGE_IMPACT' LIMIT 50 INTO #audit;");
            var stmt = Assert.Single(script.Statements);
            var audit = Assert.IsType<ShowPortalAuditStatement>(stmt);
            Assert.Equal("STEWARD_LINEAGE_IMPACT", audit.Action);
            Assert.Equal(50, audit.Limit);
            Assert.Equal("#audit", audit.IntoTable);
        }
    }
}
