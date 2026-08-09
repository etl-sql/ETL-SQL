using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Quality;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// End-to-end runtime enforcement of @expect column rules: THROW / WARN / QUARANTINE routing,
    /// the __dq_* capture schema (pre-projection input row, reserved replay columns), NULL-skip
    /// semantics, decimal compares, EXISTS IN key sets, PII masking, and the local-path pin.
    /// Numeric assertions use the m suffix — INT/BIGINT store as decimal at runtime.
    /// </summary>
    public class ColumnQualityRuntimeTests
    {
        // ── THROW ──────────────────────────────────────────────────────────

        [Fact]
        public async Task NotNullRule_WithThrow_AbortsStatement()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (NULL, 'b')");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'THROW'; */, Name
                INTO #clean FROM #src;"));

            Assert.Contains("NOT NULL", ex.Message);
            Assert.Contains("Id", ex.Message);
        }

        [Fact]
        public async Task PassingRows_AreUnaffected()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (2, 'b')");

            await Run(eval, @"
                SELECT Id /* @expect: 'NOT NULL, >= 0'; @fail: 'THROW'; */, Name
                INTO #clean FROM #src;");

            Assert.Equal(2, await CountRows(eval, "#clean"));
            Assert.Equal(0, eval.DataQuality.TotalFailures);
        }

        // ── WARN ───────────────────────────────────────────────────────────

        [Fact]
        public async Task WarnRule_PassesRowThrough_AndAggregatesOneDiagnosticPerRulePerColumn()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (-5, 'b'), (-7, 'c')");

            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");

            Assert.Equal(3, await CountRows(eval, "#clean")); // warned rows still reach the target
            Assert.Equal(3, eval.DataQuality.RowsValidated);
            Assert.Equal(2, eval.DataQuality.RowsWarned);

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal("Id", failure.Column);
            Assert.Equal(">= 0", failure.Rule);
            Assert.Equal(FailAction.Warn, failure.Action);
            Assert.Equal(2, failure.Count);
            Assert.Equal(new[] { "-5", "-7" }, failure.Samples);
        }

        [Fact]
        public async Task WarnSamples_AreCappedPerRule()
        {
            var eval = NewEvaluator();
            var values = string.Join(", ", Enumerable.Range(1, 25).Select(i => $"(-{i}, 'x')"));
            await Seed(eval, values);

            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(25, failure.Count);
            Assert.Equal(10, failure.Samples.Count); // count is exact, samples are capped
        }

        [Fact]
        public async Task WarnWithTarget_CapturesRowsWithWarnedStatusAndTargetWritten()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (-5, 'b')");

            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN TO #warn_log WITH (RETENTION = '30 DAYS');");

            Assert.Equal(2, await CountRows(eval, "#clean"));
            var warned = Assert.Single(await ReadRows(eval, "#warn_log"));
            Assert.Equal(DataQualityColumns.WarnedStatus, warned[DataQualityColumns.Status]);
            Assert.Equal(1m, warned[DataQualityColumns.TargetWritten]);
            Assert.Null(warned[DataQualityColumns.OriginRowId]);
            Assert.Equal("-5", warned[DataQualityColumns.Value]?.ToString());
        }

        [Fact]
        public async Task WarnRetention_UsesConnectorSidePrunerWhenAvailable()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (-5, 'b')");
            var target = new RetentionPruningDataSource();
            eval.Connections["#warn_log"] = target;

            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN TO #warn_log WITH (RETENTION = '30 DAYS');");

            Assert.Equal(2, await CountRows(eval, "#clean"));
            Assert.Single(target.WrittenRows);
            Assert.Equal(1, target.PruneCalls);
            Assert.Equal(DataQualityColumns.Timestamp, target.TimestampColumn);
            Assert.Equal(DataQualityColumns.CaptureScope, target.ScopeColumn);
            Assert.Equal(target.WrittenRows[0][DataQualityColumns.CaptureScope], target.ScopeValue);
            Assert.True(target.CutoffUtc < DateTime.UtcNow);
        }

        // ── QUARANTINE ─────────────────────────────────────────────────────

        [Fact]
        public async Task QuarantineRule_DivertsRow_AndCapturesPreProjectionInputColumns()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'keep'), (NULL, 'divert')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            // The failing row left the output entirely.
            Assert.Equal(1, await CountRows(eval, "#clean"));
            Assert.Equal(1, eval.DataQuality.RowsQuarantined);

            var quarantined = Assert.Single(await ReadRows(eval, "#q"));
            // Name is NOT in the projection — capturing the pre-projection input row keeps it.
            Assert.Equal("divert", quarantined["Name"]);
            Assert.Equal("NOT NULL", quarantined[DataQualityColumns.Rule]);
            Assert.Equal("Id", quarantined[DataQualityColumns.Column]);
            Assert.Equal(DataQualityColumns.QuarantinedStatus, quarantined[DataQualityColumns.Status]);
            Assert.NotNull(quarantined[DataQualityColumns.RowId]);
            Assert.Null(quarantined[DataQualityColumns.OriginRowId]); // reserved for v2 replay
            Assert.NotNull(quarantined[DataQualityColumns.Timestamp]);
            Assert.NotNull(quarantined[DataQualityColumns.Reason]);
        }

        [Fact]
        public async Task QuarantineRule_RecordsReplayManifestForLabeledSingleSource()
        {
            var provider = new CapturingMetricsProvider();
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.CurrentScriptPath = @"C:\jobs\nightly.etlsql";
            eval.JobMetrics = provider;
            await Seed(eval, "(1, 'keep'), (NULL, 'divert')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, UPPER(Name) AS CleanName
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            var manifest = Assert.Single(provider.Manifests);
            Assert.Equal("nightly_import", manifest.JobName);
            Assert.Equal(@"C:\jobs\nightly.etlsql", manifest.ScriptPath);
            Assert.Equal("import_rows", manifest.SectionLabel);
            Assert.Equal("#src", manifest.SourceTable);
            Assert.Equal("#q", manifest.QuarantineTarget);
            Assert.True(manifest.IsReplayable);
            Assert.Null(manifest.NonReplayableReason);
            Assert.Equal(new[] { "Id", "Name" }, manifest.InputColumns);
            Assert.False(string.IsNullOrWhiteSpace(manifest.InputSchemaFingerprint));
        }

        [Fact]
        public async Task QuarantineRule_RecordsNonReplayableManifestForJoinSource()
        {
            var provider = new CapturingMetricsProvider();
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.JobMetrics = provider;
            await Seed(eval, "(NULL, 'divert')");

            await Run(eval, @"
                CREATE TABLE #dim (Name VARCHAR(100));
                INSERT INTO #dim (Name) VALUES ('divert');
                import_joined_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                JOIN #dim ON #src.Name = #dim.Name
                ON FAILURE QUARANTINE TO #q;");

            var manifest = Assert.Single(provider.Manifests);
            Assert.False(manifest.IsReplayable);
            Assert.Equal("probe-join", manifest.ReplayMode);
            Assert.Equal("#src", manifest.SourceTable);
            Assert.Equal("#src", manifest.ProbeSourceTable);
            Assert.Equal("#dim", manifest.JoinBuildTable);
            Assert.False(manifest.JoinObservedN1);
            Assert.Contains("N:1", manifest.NonReplayableReason, StringComparison.OrdinalIgnoreCase);

            var quarantined = Assert.Single(await ReadRows(eval, "#q"));
            Assert.Equal("divert", quarantined["Name"]);
            Assert.DoesNotContain("Region", quarantined.GetColumnNames(), StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task JoinOutput_CarriesProbeReplayProvenanceWithoutVisibleColumns()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'match')");

            await Run(eval, @"
                CREATE TABLE #dim (Name VARCHAR(100), Region VARCHAR(10));
                INSERT INTO #dim (Name, Region) VALUES ('match', 'NA');
                SELECT #src.Id, #src.Name, #dim.Region
                INTO #joined FROM #src
                JOIN #dim ON #src.Name = #dim.Name;");

            var row = Assert.Single(await ReadRows(eval, "#joined"));
            Assert.NotNull(row.DataQualityReplayProvenance);
            var provenance = row.DataQualityReplayProvenance!;
            Assert.Equal("#src", provenance.SourceTable);
            Assert.Equal(1m, provenance.SourceRow["Id"]);
            Assert.Equal("match", provenance.SourceRow["Name"]);
            Assert.DoesNotContain(
                row.GetColumnNames(),
                name => name.Contains("ReplayProvenance", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("DataQuality", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task QuarantineRowId_IsDeterministicPerRowContent()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'a'), (NULL, 'a'), (NULL, 'b')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            var ids = (await ReadRows(eval, "#q"))
                .Select(r => r[DataQualityColumns.RowId]?.ToString()).ToList();
            Assert.Equal(3, ids.Count);
            Assert.Equal(ids[0], ids[1]); // identical content → identical identity
            Assert.NotEqual(ids[0], ids[2]);
        }

        [Fact]
        public async Task QuarantineDisposition_AllowsReleaseFromQuarantined()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'divert')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            await Run(eval, @"
                UPDATE #q
                SET Name = 'fixed', __dq_status = 'released'
                WHERE __dq_status = 'quarantined';");

            var row = Assert.Single(await ReadRows(eval, "#q"));
            Assert.Equal("fixed", row["Name"]);
            Assert.Equal(DataQualityColumns.ReleasedStatus, row[DataQualityColumns.Status]);
        }

        [Fact]
        public async Task ReplayQuarantine_ReplaysReleasedRowsFromManifest()
        {
            var provider = new CapturingMetricsProvider();
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.JobMetrics = provider;
            await Seed(eval, "(NULL, 'divert')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");
            await Run(eval, "UPDATE #q SET Id = 10, __dq_status = 'released' WHERE __dq_status = 'quarantined';");

            await Run(eval, "REPLAY QUARANTINE #q;");

            var row = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("nightly_import", row["JobName"]);
            Assert.Equal("import_rows", row["SectionLabel"]);
            Assert.Equal("#src", row["SourceTable"]);
            Assert.Equal(1L, row["ReleasedRows"]);
            Assert.Equal("replayed", row["Status"]);
            Assert.Equal(1, provider.LeasesAcquired);
            Assert.Equal(1, provider.LeasesReleased);

            await Run(eval, "SELECT * FROM #clean WHERE Id = 10;");
            var cleanRow = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal(10m, cleanRow["Id"]);
            Assert.Equal("divert", cleanRow["Name"]);

            await Run(eval, "SELECT __dq_status FROM #q;");
            var quarantineRow = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal(DataQualityColumns.ReplayedStatus, quarantineRow[DataQualityColumns.Status]);
        }

        [Fact]
        public async Task ReplayQuarantine_OnlyMarksTheRowsItActuallyConsumed()
        {
            // A steward can release a row through Portal (a separate job that does not hold the
            // replay lease) while a replay is in flight. That row must not be flipped to
            // 'replayed' — it was never fed through the statement, and marking it done would
            // silently discard the fix.
            var provider = new CapturingMetricsProvider();
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.JobMetrics = provider;
            await Seed(eval, "(NULL, 'first'), (NULL, 'second')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            // Release only the first row; the second stays quarantined for now.
            await Run(eval, "UPDATE #q SET Id = 10, __dq_status = 'released' WHERE Name = 'first';");
            await Run(eval, "REPLAY QUARANTINE #q;");

            // Now the second row is released *after* that replay consumed the first.
            await Run(eval, "UPDATE #q SET Id = 20, __dq_status = 'released' WHERE Name = 'second';");

            await Run(eval, "SELECT Name, __dq_status FROM #q;");
            var rows = eval.LastResult!.Rows.ToDictionary(
                r => r["Name"]?.ToString()!, r => r[DataQualityColumns.Status]?.ToString());

            Assert.Equal(DataQualityColumns.ReplayedStatus, rows["first"]);
            // Still awaiting its own replay — not swept up by the previous one.
            Assert.Equal(DataQualityColumns.ReleasedStatus, rows["second"]);
        }

        [Fact]
        public async Task DryRun_ReportsImpactWithoutEnforcing()
        {
            // The point of a dry run: learn how many rows a candidate rule would take out, without
            // it taking any out.
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (NULL, 'b'), (NULL, 'c')");

            await Run(eval, @"
                SET DATA_QUALITY_DRY_RUN = ON;
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src;");

            // Nothing was diverted or thrown: the load behaves exactly as it would without rules.
            Assert.Equal(3, await CountRows(eval, "#clean"));
            Assert.Equal(0, eval.DataQuality.RowsQuarantined);
            Assert.Equal(0, eval.DataQuality.RowsWarned);

            // ...but the steward gets the impact numbers.
            Assert.Equal(2, eval.DataQuality.RowsDryRunAffected);
            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal("Id", failure.Column);
            Assert.Equal(2, failure.Count);
        }

        [Fact]
        public async Task DryRun_DoesNotRequireRoutingClauses_NorThrow()
        {
            // A QUARANTINE rule without ON FAILURE is a hard error normally; during a dry run the
            // steward has not decided on wiring yet, and a THROW rule must not abort the load.
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'a')");

            await Run(eval, @"
                SET DATA_QUALITY_DRY_RUN = ON;
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'THROW'; */, Name
                INTO #clean FROM #src;");

            Assert.Equal(1, await CountRows(eval, "#clean"));
            Assert.Equal(1, eval.DataQuality.RowsDryRunAffected);
        }

        [Fact]
        public async Task DryRun_TurnedOff_EnforcesNormally()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (NULL, 'b')");

            await Run(eval, @"
                SET DATA_QUALITY_DRY_RUN = ON;
                SET DATA_QUALITY_DRY_RUN = OFF;
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            Assert.Equal(1, await CountRows(eval, "#clean"));
            Assert.Equal(1, eval.DataQuality.RowsQuarantined);
            Assert.Equal(0, eval.DataQuality.RowsDryRunAffected);
        }

        [Fact]
        public async Task ShowDataQualityRules_ListsEachProtectionPerColumn()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL, >= 0'; @fail: 'THROW';
                            @expect_1: 'UNIQUE'; @fail_1: 'QUARANTINE'; */,
                       Name /* @expect: 'NOT NULL'; */
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q
                ON FAILURE WARN;");

            await Run(eval, "SELECT * FROM eng.data_quality_rules;");
            var rows = eval.LastResult!.Rows;

            // One row per individual rule, not per tag: 'NOT NULL, >= 0' reads as two protections.
            Assert.Equal(4, rows.Count);
            Assert.Contains(rows, r => (string?)r["target_column"] == "Id"
                && (string?)r["rule"] == "NOT NULL" && (string?)r["action"] == "THROW");
            Assert.Contains(rows, r => (string?)r["target_column"] == "Id"
                && (string?)r["rule"] == ">= 0" && (string?)r["action"] == "THROW");
            Assert.Contains(rows, r => (string?)r["target_column"] == "Id"
                && (string?)r["rule"] == "UNIQUE" && (string?)r["rule_tag"] == "@expect_1");

            // A rule with no @fail is WARN, and the listing says so rather than leaving it blank.
            Assert.Contains(rows, r => (string?)r["target_column"] == "Name"
                && (string?)r["action"] == "WARN (default)");
        }

        [Fact]
        public async Task ShowDataQualityRules_FiltersByTableAndColumn_AndWritesIntoTemp()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'WARN'; */,
                       Name /* @expect: 'NOT NULL'; @fail: 'WARN'; */
                INTO #clean FROM #src
                ON FAILURE WARN;");

            await Run(eval, "SELECT * FROM eng.data_quality_rules WHERE target_table = '#clean' AND target_column = 'Id';");
            var single = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("Id", single["target_column"]);

            await Run(eval, "SELECT * INTO #rules FROM eng.data_quality_rules;");
            var captured = await ReadRows(eval, "#rules");
            Assert.Equal(2, captured.Count);
            Assert.All(captured, r => Assert.Equal("#clean", r["target_table"]));
        }

        [Fact]
        public async Task ShowDataQualityRules_WithNoRulesRecorded_ReturnsEmptyRatherThanFailing()
        {
            var eval = NewEvaluator();
            await Run(eval, "SELECT * FROM eng.data_quality_rules;");
            Assert.Empty(eval.LastResult!.Rows);
        }

        [Fact]
        public async Task QuarantineEvidence_CannotBeDeletedWhileDispositionIsInFlight()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'divert')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            var quarantined = await Assert.ThrowsAsync<ExecutionException>(() =>
                Run(eval, "DELETE FROM #q;"));
            Assert.Contains("still in flight", quarantined.Message);

            await Run(eval, "UPDATE #q SET __dq_status = 'released' WHERE __dq_status = 'quarantined';");
            var released = await Assert.ThrowsAsync<ExecutionException>(() =>
                Run(eval, "DELETE FROM #q;"));
            Assert.Contains("still in flight", released.Message);

            // A terminal disposition is the steward's explicit sign-off, so it can be removed.
            await Run(eval, "UPDATE #q SET __dq_status = 'discarded' WHERE __dq_status = 'released';");
            await Run(eval, "DELETE FROM #q;");
            Assert.Empty(await ReadRows(eval, "#q"));
        }

        [Fact]
        public async Task FabricatedEvidenceRows_CannotBeInserted()
        {
            // Without this guard a hand-authored 'released' row would be picked up by REPLAY
            // QUARANTINE and injected into the production target as if it had been validated.
            var eval = NewEvaluator();
            await Run(eval, "CREATE TABLE #q2 (Id INT, Name VARCHAR(50), __dq_status VARCHAR(20));");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval,
                "INSERT INTO #q2 (Id, Name, __dq_status) VALUES (1, 'injected', 'released');"));

            Assert.Contains("__dq_status", ex.Message);
            Assert.Contains("evidence column", ex.Message);
        }

        [Fact]
        public async Task FabricatedEvidenceRows_CannotBeInsertedWithoutColumnList()
        {
            var eval = NewEvaluator();
            await Run(eval, "CREATE TABLE #q2 (Id INT, Name VARCHAR(50), __dq_status VARCHAR(20));");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval,
                "INSERT INTO #q2 VALUES (1, 'injected', 'released');"));

            Assert.Contains("__dq_status", ex.Message);
            Assert.Empty(await ReadRows(eval, "#q2"));
        }

        [Fact]
        public async Task ReplayQuarantine_RejectsAnUnresolvedReplayClaim()
        {
            var provider = new CapturingMetricsProvider();
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.JobMetrics = provider;
            await Seed(eval, "(NULL, 'divert')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");
            await Run(eval,
                "UPDATE #q SET __dq_status = 'released' WHERE __dq_status = 'quarantined';");
            await Run(eval,
                "UPDATE #q SET __dq_status = 'replaying' WHERE __dq_status = 'released';");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                Run(eval, "REPLAY QUARANTINE #q;"));

            Assert.Contains("incomplete replay", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await ReadRows(eval, "#clean"));
            Assert.Equal(
                DataQualityColumns.ReplayingStatus,
                Assert.Single(await ReadRows(eval, "#q"))[DataQualityColumns.Status]);
        }

        [Fact]
        public async Task ReplayQuarantine_ReplaysReleasedProbeRowsThroughN1HashJoin()
        {
            var provider = new CapturingMetricsProvider();
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.JobMetrics = provider;
            await Seed(eval, "(NULL, 'divert')");

            await Run(eval, @"
                CREATE TABLE #dim (Name VARCHAR(100), Region VARCHAR(10));
                INSERT INTO #dim (Name, Region) VALUES ('divert', 'NA');
                import_joined_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, #src.Name, #dim.Region
                INTO #clean FROM #src
                INNER HASH JOIN #dim ON #src.Name = #dim.Name
                ON FAILURE QUARANTINE TO #q;");

            var manifest = Assert.Single(provider.Manifests);
            Assert.True(manifest.IsReplayable);
            Assert.Equal("probe-join", manifest.ReplayMode);
            Assert.Equal("#src", manifest.SourceTable);
            Assert.Equal("#src", manifest.ProbeSourceTable);
            Assert.Equal("#dim", manifest.JoinBuildTable);
            Assert.True(manifest.JoinObservedN1);
            Assert.Null(manifest.JoinNonReplayableReason);

            await Run(eval, "UPDATE #q SET Id = 10, __dq_status = 'released' WHERE __dq_status = 'quarantined';");
            await Run(eval, "REPLAY QUARANTINE #q;");

            Assert.Equal("replayed", Assert.Single(eval.LastResult!.Rows)["Status"]);

            await Run(eval, "SELECT Id, Name, Region FROM #clean WHERE Id = 10;");
            var cleanRow = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal(10m, cleanRow["Id"]);
            Assert.Equal("divert", cleanRow["Name"]);
            Assert.Equal("NA", cleanRow["Region"]);
        }

        [Fact]
        public async Task ReplayQuarantine_ReplaysUnmatchedProbeAfterDimensionIsAdded()
        {
            var provider = new CapturingMetricsProvider();
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.JobMetrics = provider;
            await Seed(eval, "(1, 'missing')");

            await Run(eval, @"
                CREATE TABLE #dim (Name VARCHAR(100), Region VARCHAR(10));
                import_joined_rows:
                SELECT #src.Id, #src.Name,
                       #dim.Region /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */
                INTO #clean FROM #src
                LEFT HASH JOIN #dim ON #src.Name = #dim.Name
                ON FAILURE QUARANTINE TO #q;");

            var manifest = Assert.Single(provider.Manifests);
            Assert.True(manifest.IsReplayable);
            Assert.Equal("probe-join", manifest.ReplayMode);
            Assert.Equal("#dim", manifest.JoinBuildTable);
            Assert.True(manifest.JoinObservedN1);

            await Run(eval, "INSERT INTO #dim (Name, Region) VALUES ('missing', 'NA');");
            await Run(eval,
                "UPDATE #q SET __dq_status = 'released' WHERE __dq_status = 'quarantined';");
            await Run(eval, "REPLAY QUARANTINE #q;");

            await Run(eval, "SELECT Id, Name, Region FROM #clean;");
            var cleanRow = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal(1m, cleanRow["Id"]);
            Assert.Equal("missing", cleanRow["Name"]);
            Assert.Equal("NA", cleanRow["Region"]);
        }

        [Fact]
        public async Task ReplayQuarantine_FailsWhenManifestIsMissing()
        {
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.JobMetrics = new CapturingMetricsProvider();

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, "REPLAY QUARANTINE #missing;"));
            Assert.Contains("No quarantine replay manifest", ex.Message);
        }

        [Fact]
        public async Task ReplayQuarantine_FailsWhenManifestIsNonReplayable()
        {
            var provider = new CapturingMetricsProvider();
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.JobMetrics = provider;
            await Seed(eval, "(NULL, 'divert')");
            await Run(eval, @"
                CREATE TABLE #dim (Name VARCHAR(100));
                INSERT INTO #dim (Name) VALUES ('divert');
                import_joined_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                JOIN #dim ON #src.Name = #dim.Name
                ON FAILURE QUARANTINE TO #q;");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, "REPLAY QUARANTINE #q;"));
            Assert.Contains("not replayable", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("N:1", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ReplayQuarantine_FailsWhenReplayLeaseIsHeld()
        {
            var provider = new CapturingMetricsProvider { AllowLeaseAcquire = false };
            var eval = NewEvaluator();
            eval.JobName = "nightly_import";
            eval.JobMetrics = provider;
            await Seed(eval, "(NULL, 'divert')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");
            await Run(eval, "UPDATE #q SET Id = 10, __dq_status = 'released' WHERE __dq_status = 'quarantined';");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, "REPLAY QUARANTINE #q;"));
            Assert.Contains("already being replayed", ex.Message);
            Assert.Equal(1, provider.LeasesAcquired);
            Assert.Equal(0, provider.LeasesReleased);
        }

        [Fact]
        public async Task QuarantineDisposition_RejectsEvidenceColumnEdits()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'divert')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                UPDATE #q
                SET __dq_reason = 'changed'
                WHERE __dq_status = 'quarantined';"));
            Assert.Contains("immutable", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task QuarantineDisposition_RejectsInvalidStatusTransition()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'divert')");
            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                UPDATE #q
                SET __dq_status = 'replayed'
                WHERE __dq_status = 'quarantined';"));
            Assert.Contains("quarantined -> replayed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task WarnDisposition_RejectsStatusEdits()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(-5, 'warned')");
            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN TO #warn_log WITH (RETENTION = '30 DAYS');");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                UPDATE #warn_log
                SET __dq_status = 'released'
                WHERE __dq_status = 'warned';"));
            Assert.Contains("Warn rows", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task QuarantineWithoutClause_FailsLoudly()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'a')");

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(eval, @"
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */ INTO #clean FROM #src;"));
            Assert.Contains("ON FAILURE QUARANTINE", ex.Message);
        }

        // ── Rule semantics ─────────────────────────────────────────────────

        [Fact]
        public async Task NullValues_SkipEveryRuleExceptNotNull()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'a')");

            // A NULL Id must NOT trip the >= 0 rule (SQL CHECK-constraint convention).
            await Run(eval, @"
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");

            Assert.Equal(0, eval.DataQuality.TotalFailures);
            Assert.Equal(1, await CountRows(eval, "#clean"));
        }

        [Fact]
        public async Task MatchesRule_ValidatesProjectedValue_NotSourceValue()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'user@example.com'), (2, 'bad-address')");

            await Run(eval, @"
                SELECT Id, UPPER(Name) AS Email /* @expect: 'MATCHES ^[A-Z0-9._%+-]+@[A-Z0-9.-]+$'; @fail: 'WARN'; */
                INTO #clean FROM #src
                ON FAILURE WARN;");

            // The rule saw the uppercased projected value: the valid address passes the
            // uppercase-only pattern, the invalid one fails.
            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(1, failure.Count);
            Assert.Equal("BAD-ADDRESS", failure.Samples.Single());
        }

        [Fact]
        public async Task InListRule_HonorsCaseSensitivitySetting()
        {
            var insensitive = NewEvaluator();
            await Seed(insensitive, "(1, 'na')");
            await Run(insensitive, @"
                SELECT Id, Name /* @expect: ""IN ('NA','EMEA')""; @fail: 'WARN'; */
                INTO #clean FROM #src ON FAILURE WARN;");
            Assert.Equal(0, insensitive.DataQuality.TotalFailures);

            var sensitive = NewEvaluator();
            await Seed(sensitive, "(1, 'na')");
            await Run(sensitive, @"
                SET CASE_SENSITIVE = ON;
                SELECT Id, Name /* @expect: ""IN ('NA','EMEA')""; @fail: 'WARN'; */
                INTO #clean FROM #src ON FAILURE WARN;");
            Assert.Equal(1, sensitive.DataQuality.TotalFailures);
        }

        [Fact]
        public async Task ExistsInRule_ProbesReferenceTableKeySet()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (99, 'b')");
            await Run(eval, @"
                CREATE TABLE #dim_region (Id INT);
                INSERT INTO #dim_region (Id) VALUES (1), (2);");

            await Run(eval, @"
                SELECT Id /* @expect: 'EXISTS IN #dim_region(Id)'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(1, failure.Count);
            Assert.Equal("99", failure.Samples.Single());
        }

        [Fact]
        public async Task ExprRule_EvaluatesAcrossTheProjectedRow()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #ranges (StartVal INT, EndVal INT);
                INSERT INTO #ranges (StartVal, EndVal) VALUES (1, 5), (9, 3);");

            await Run(eval, @"
                SELECT StartVal /* @expect: 'EXPR StartVal <= EndVal'; @fail: 'WARN'; */, EndVal
                INTO #clean FROM #ranges
                ON FAILURE WARN;");

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(1, failure.Count);
        }

        [Fact]
        public async Task MultipleNumberedRules_BindToTheirOwnActions()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (-5, 'b'), (500, 'c')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN';
                            @expect_1: '<= 120'; @fail_1: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN
                ON FAILURE QUARANTINE TO #q;");

            Assert.Equal(2, await CountRows(eval, "#clean"));  // -5 warned through, 500 diverted
            Assert.Equal(1, await CountRows(eval, "#q"));
            Assert.Equal(1, eval.DataQuality.RowsWarned);
            Assert.Equal(1, eval.DataQuality.RowsQuarantined);
            Assert.Equal(2, eval.DataQuality.Failures.Count);
        }

        // ── PII masking ────────────────────────────────────────────────────

        [Fact]
        public async Task PiiTaggedColumn_MasksSampleValuesInDiagnosticsAndCapture()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'secret-value')");

            await Run(eval, @"
                import_rows:
                SELECT Id, Name /* @pii: true; @expect: 'MATCHES ^ok$'; @fail: 'QUARANTINE'; */
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            var failure = Assert.Single(eval.DataQuality.Failures);
            Assert.Equal(DataQualityReport.PiiMask, failure.Samples.Single());

            // __dq_value is masked too; the raw value survives only in the captured input columns,
            // which inherit the source column's stewardship tags and access controls.
            var quarantined = Assert.Single(await ReadRows(eval, "#q"));
            Assert.Equal(DataQualityReport.PiiMask, quarantined[DataQualityColumns.Value]);
            Assert.DoesNotContain("secret-value", quarantined[DataQualityColumns.Reason]!.ToString());
        }

        // ── UNIQUE (spill-once pre-pass) ───────────────────────────────────

        [Fact]
        public async Task UniqueRule_QuarantinesEveryRowInADuplicatedGroup()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (2, 'b'), (1, 'c')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'UNIQUE'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            // Plain UNIQUE keeps nothing from a duplicated group — both Id=1 rows are diverted.
            var clean = await ReadRows(eval, "#clean");
            Assert.Equal(2m, Assert.Single(clean)["Id"]);
            Assert.Equal(2, await CountRows(eval, "#q"));
            Assert.Equal(2, eval.DataQuality.RowsQuarantined);
        }

        [Fact]
        public async Task UniqueFirstBy_KeepsTheEarliestRowPerKey()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #events (EventId INT, LoadedAt INT, Tag VARCHAR(10));
                INSERT INTO #events (EventId, LoadedAt, Tag)
                VALUES (1, 30, 'late'), (1, 10, 'early'), (2, 5, 'only');");

            await Run(eval, @"
                import_rows:
                SELECT EventId /* @expect: 'UNIQUE_FIRST BY LoadedAt'; @fail: 'QUARANTINE'; */, LoadedAt, Tag
                INTO #clean FROM #events
                ON FAILURE QUARANTINE TO #q;");

            var clean = await ReadRows(eval, "#clean");
            Assert.Equal(2, clean.Count);
            Assert.Contains(clean, r => (string?)r["Tag"] == "early");
            Assert.Contains(clean, r => (string?)r["Tag"] == "only");
            Assert.DoesNotContain(clean, r => (string?)r["Tag"] == "late");

            var quarantined = Assert.Single(await ReadRows(eval, "#q"));
            Assert.Equal("late", quarantined["Tag"]);
        }

        [Fact]
        public async Task UniqueLastBy_KeepsTheLatestRowPerKey()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #events (EventId INT, LoadedAt INT, Tag VARCHAR(10));
                INSERT INTO #events (EventId, LoadedAt, Tag)
                VALUES (1, 30, 'late'), (1, 10, 'early');");

            await Run(eval, @"
                import_rows:
                SELECT EventId /* @expect: 'UNIQUE_LAST BY LoadedAt'; @fail: 'QUARANTINE'; */, LoadedAt, Tag
                INTO #clean FROM #events
                ON FAILURE QUARANTINE TO #q;");

            Assert.Equal("late", Assert.Single(await ReadRows(eval, "#clean"))["Tag"]);
        }

        [Fact]
        public async Task UniqueWith_TreatsTheColumnTupleAsTheKey()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #t (TenantId INT, Region VARCHAR(10), Val INT);
                INSERT INTO #t (TenantId, Region, Val)
                VALUES (1, 'NA', 10), (1, 'EMEA', 20), (1, 'NA', 30);");

            await Run(eval, @"
                import_rows:
                SELECT TenantId /* @expect: 'UNIQUE WITH (TenantId, Region)'; @fail: 'QUARANTINE'; */, Region, Val
                INTO #clean FROM #t
                ON FAILURE QUARANTINE TO #q;");

            // (1,'NA') appears twice → both diverted; (1,'EMEA') is unique → kept.
            Assert.Equal("EMEA", Assert.Single(await ReadRows(eval, "#clean"))["Region"]);
            Assert.Equal(2, await CountRows(eval, "#q"));
        }

        [Fact]
        public async Task UniqueRule_SkipsNullKeys()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(NULL, 'a'), (NULL, 'b'), (1, 'c')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'UNIQUE'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            // NULL is not a value that can duplicate — UNIQUE skips it like every non-NOT NULL rule.
            Assert.Equal(3, await CountRows(eval, "#clean"));
            Assert.Equal(0, await CountRows(eval, "#q"));
        }

        [Fact]
        public async Task UniqueRule_SameTextOnDifferentColumns_IsTrackedIndependently()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #t (A INT, B INT, Tag VARCHAR(10));
                INSERT INTO #t (A, B, Tag)
                VALUES (1, 10, 'a'), (1, 20, 'b'), (2, 20, 'c'), (3, 30, 'd');");

            await Run(eval, @"
                import_rows:
                SELECT
                    A /* @expect: 'UNIQUE'; @fail: 'QUARANTINE'; */,
                    B /* @expect: 'UNIQUE'; @fail: 'QUARANTINE'; */,
                    Tag
                INTO #clean FROM #t
                ON FAILURE QUARANTINE TO #q;");

            var clean = await ReadRows(eval, "#clean");
            Assert.Equal("d", Assert.Single(clean)["Tag"]);
            Assert.Equal(3, await CountRows(eval, "#q"));
            Assert.Equal(3, eval.DataQuality.RowsQuarantined);
        }

        [Fact]
        public async Task UniqueFirst_TieOnOrderKey_IsDeterministicAcrossRuns()
        {
            // Two rows share both the key and the order key; the tiebreak must pick the same
            // survivor every run, not whichever the scan happened to reach first.
            var survivors = new System.Collections.Generic.List<string?>();
            for (int run = 0; run < 3; run++)
            {
                var eval = NewEvaluator();
                await Run(eval, @"
                    CREATE TABLE #events (EventId INT, LoadedAt INT, Tag VARCHAR(10));
                    INSERT INTO #events (EventId, LoadedAt, Tag)
                    VALUES (1, 10, 'zebra'), (1, 10, 'alpha');");

                await Run(eval, @"
                    import_rows:
                    SELECT EventId /* @expect: 'UNIQUE_FIRST BY LoadedAt'; @fail: 'QUARANTINE'; */, LoadedAt, Tag
                    INTO #clean FROM #events
                    ON FAILURE QUARANTINE TO #q;");

                survivors.Add(Assert.Single(await ReadRows(eval, "#clean"))["Tag"]?.ToString());
            }

            Assert.Single(survivors.Distinct());
        }

        [Fact]
        public async Task UniqueFirst_IdenticalDuplicateRows_KeepsOnlyOneRow()
        {
            var eval = NewEvaluator();
            await Run(eval, @"
                CREATE TABLE #events (EventId INT, LoadedAt INT, Tag VARCHAR(10));
                INSERT INTO #events (EventId, LoadedAt, Tag)
                VALUES (1, 10, 'same'), (1, 10, 'same');");

            await Run(eval, @"
                import_rows:
                SELECT EventId /* @expect: 'UNIQUE_FIRST BY LoadedAt'; @fail: 'QUARANTINE'; */, LoadedAt, Tag
                INTO #clean FROM #events
                ON FAILURE QUARANTINE TO #q;");

            Assert.Equal(1, await CountRows(eval, "#clean"));
            Assert.Equal(1, await CountRows(eval, "#q"));
        }

        [Fact]
        public async Task UniqueRule_ReadsTheSourceExactlyOnce()
        {
            // Non-rewindable sources (Kafka, paginated REST) cannot be read twice, and even a
            // rewindable one can observe different data on a second read. The pre-pass and the
            // validation pass must both come from a single materialization.
            var eval = NewEvaluator();
            var counting = new ReadCountingDataSource(
                ("Id", "Name"),
                [(1m, "a"), (2m, "b"), (1m, "c")]);
            eval.Connections["#counted"] = counting;

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'UNIQUE'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #counted
                ON FAILURE QUARANTINE TO #q;");

            Assert.Equal(1, counting.ReadCount);
            Assert.Equal(2, await CountRows(eval, "#q"));
        }

        [Fact]
        public async Task UniqueRule_WithWarnAction_KeepsEveryRowButCountsFailures()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (1, 'b'), (2, 'c')");

            await Run(eval, @"
                SELECT Id /* @expect: 'UNIQUE'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");

            Assert.Equal(3, await CountRows(eval, "#clean")); // WARN never removes rows
            Assert.Equal(2, eval.DataQuality.RowsWarned);
            Assert.Equal(2, Assert.Single(eval.DataQuality.Failures).Count);
        }

        // ── Overhead and the local-path pin ────────────────────────────────

        [Fact]
        public async Task NoRules_LeavesReportEmpty_ZeroOverheadPath()
        {
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (2, 'b')");

            await Run(eval, "SELECT Id, Name INTO #clean FROM #src;");

            Assert.True(eval.DataQuality.IsEmpty);
            Assert.Equal(0, eval.DataQuality.RowsValidated);
        }

        [Fact]
        public async Task RulesAreEnforced_EvenOnAColumnarSelectIntoShape()
        {
            // SELECT <cols> INTO <table> FROM <table> is the native columnar fast path, which
            // bypasses local projection. Rules must pin execution to the row pipeline instead of
            // being silently skipped.
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (NULL, 'b')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            Assert.Equal(1, await CountRows(eval, "#clean"));
            Assert.Equal(1, await CountRows(eval, "#q"));
        }

        // ── Rule tags do not propagate downstream ──────────────────────────

        [Fact]
        public async Task RuleTags_AreNotInheritedByDownstreamColumns()
        {
            // @expect/@fail are enforcement directives bound to the declaring statement, not
            // descriptive metadata. If they were inherited through lineage, every later read of a
            // quality-loaded table would re-validate (and re-quarantine) already-validated rows —
            // and a plain SELECT would fail for lacking an ON FAILURE clause it never declared.
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a'), (NULL, 'b')");

            await Run(eval, @"
                import_rows:
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE QUARANTINE TO #q;");

            // A plain read of the loaded table must succeed and enforce nothing.
            await Run(eval, "SELECT Id, Name FROM #clean;");
            Assert.Single(eval.LastResult!.Rows);

            // Re-loading from it must not quarantine again either.
            long quarantinedAfterLoad = eval.DataQuality.RowsQuarantined;
            await Run(eval, "SELECT Id, Name INTO #copy FROM #clean;");
            Assert.Equal(quarantinedAfterLoad, eval.DataQuality.RowsQuarantined);
        }

        [Fact]
        public async Task RulesStayVisibleWhereDeclared_ButDoNotFlowDownstream()
        {
            // Two things must both hold. Rules ARE steward-facing metadata, so they belong on the
            // lineage entry of the statement that declares them (that visibility is the reason
            // rules are tags at all). They must NOT be inherited onto columns further down the
            // graph, where they would re-fire as enforcement.
            var eval = NewEvaluator();
            await Seed(eval, "(1, 'a')");

            await Run(eval, @"
                SELECT Id /* @pii: true; @owner: CRM; @expect: 'NOT NULL'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN;");
            await Run(eval, "SELECT Id, Name INTO #downstream FROM #clean;");

            var declared = eval.LineageTracker.GetFullLineage()
                .First(e => e.TargetTable == "#clean" && e.TargetColumn == "Id").Metadata;
            Assert.Equal("true", declared["pii"]);
            Assert.True(declared.ContainsKey("expect"));  // steward visibility where declared

            var downstream = eval.LineageTracker.GetFullLineage()
                .First(e => e.TargetTable == "#downstream" && e.TargetColumn == "Id").Metadata;
            Assert.Equal("true", downstream["pii"]);      // descriptive tags still inherit
            Assert.Equal("CRM", downstream["owner"]);
            Assert.False(downstream.ContainsKey("expect")); // enforcement does not
            Assert.False(downstream.ContainsKey("fail"));
        }

        // ── Metrics surfaced on the run result ─────────────────────────────

        [Fact]
        public async Task DataQualityOutcomes_SurfaceOnTheExecutionResult()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var session = Microsoft.Extensions.DependencyInjection.ActivatorUtilities
                .CreateInstance<ETL_SQL.Orchestrator.Execution.ExecutionSession>(provider);

            var result = await session.ExecuteAsync(@"
                CREATE TABLE #src (Id INT, Name VARCHAR(50));
                INSERT INTO #src (Id, Name) VALUES (1, 'a'), (-5, 'b'), (NULL, 'c');
                import_rows:
                SELECT Id /* @expect: '>= 0'; @fail: 'WARN';
                            @expect_1: 'NOT NULL'; @fail_1: 'QUARANTINE'; */, Name
                INTO #clean FROM #src
                ON FAILURE WARN
                ON FAILURE QUARANTINE TO #q;");

            Assert.True(result.Success);
            Assert.Equal(1, result.RowsQuarantined);
            Assert.Equal(1, result.RowsWarned);
            Assert.NotNull(result.DataQualityFailures);
            Assert.Contains("Id:", result.DataQualityFailures);

            // Aggregated warn diagnostics reach the run's diagnostic surface.
            Assert.Contains(result.Diagnostics, d =>
                d.Code == "DATAQUALITY" && d.Message.Contains("Data quality"));
        }

        [Fact]
        public async Task DataQualityThrowFailures_SurfaceOnFailedExecutionResult()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var session = Microsoft.Extensions.DependencyInjection.ActivatorUtilities
                .CreateInstance<ETL_SQL.Orchestrator.Execution.ExecutionSession>(provider);

            var result = await session.ExecuteAsync(@"
                CREATE TABLE #src (Id INT, Name VARCHAR(50));
                INSERT INTO #src (Id, Name) VALUES (NULL, 'bad');
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'THROW'; */, Name
                INTO #clean FROM #src;");

            Assert.False(result.Success);
            Assert.Equal("Id:NOT NULL=1", result.DataQualityFailures);
            Assert.Contains(result.Diagnostics, d =>
                d.Code == "DATAQUALITY" && d.Severity == DiagnosticSeverity.Error);
        }

        // ── Profiling: what the rules cost ─────────────────────────────────

        /// <summary>
        /// `SET PROFILE ON` must report the cost of the rules, not only what they found. The
        /// run-level tallies answer "what failed"; an operator whose load has slowed down and whose
        /// rules are the thing that changed needs "what did this statement spend".
        /// </summary>
        [Fact]
        public async Task Profiling_AttributesDataQualityCost_ToTheStatementCarryingTheRules()
        {
            var eval = NewEvaluator();
            eval.Telemetry.IsProfiling = true;
            await Seed(eval, "(1, 'a'), (NULL, 'b'), (NULL, 'c')");

            await Run(eval, @"
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src ON FAILURE WARN;");

            var ruleStatement = Assert.Single(
                eval.Telemetry.ProfileMetrics, m => m.DataQualityRowsValidated > 0);

            Assert.Equal(3, ruleStatement.DataQualityRowsValidated);
            Assert.Equal(2, ruleStatement.DataQualityRowsWarned);
            Assert.Equal(0, ruleStatement.DataQualityRowsQuarantined);
        }

        /// <summary>
        /// The cost belongs to the statement that carried the rules. Attributing it to the run, or
        /// smearing it across every statement, would make the number useless for finding which load
        /// got slower.
        /// </summary>
        [Fact]
        public async Task Profiling_ReportsZeroDataQualityCost_ForStatementsWithoutRules()
        {
            var eval = NewEvaluator();
            eval.Telemetry.IsProfiling = true;
            await Seed(eval, "(1, 'a'), (2, 'b')");

            await Run(eval, "SELECT Id, Name INTO #plain FROM #src;");

            Assert.All(eval.Telemetry.ProfileMetrics, m =>
            {
                Assert.Equal(0, m.DataQualityRowsValidated);
                Assert.Equal(0, m.DataQualityRowsWarned);
                Assert.Equal(0, m.DataQualityValidationMs);
            });
        }

        /// <summary>
        /// `SET PROFILE OFF` removes the per-row timing work entirely.
        ///
        /// <para>Worth pinning because <c>IsProfiling</c> defaults to <b>true</b> — so the two
        /// timestamp reads per row are the normal case, not the exception, and turning profiling
        /// off is the only way to avoid them. Anyone tightening the row pipeline needs to know the
        /// switch exists and actually works.</para>
        /// </summary>
        [Fact]
        public async Task ValidationTiming_IsNotAccumulated_WhenProfilingIsOff()
        {
            var eval = NewEvaluator();
            eval.Telemetry.IsProfiling = false;
            await Seed(eval, "(1, 'a'), (NULL, 'b')");

            await Run(eval, @"
                SELECT Id /* @expect: 'NOT NULL'; @fail: 'WARN'; */, Name
                INTO #clean FROM #src ON FAILURE WARN;");

            Assert.Equal(0, eval.Telemetry.DataQualityValidationTicks);
            // The rules still ran — this is about the measurement, not the enforcement.
            Assert.Equal(2, eval.DataQuality.RowsValidated);
        }

        [Fact]
        public async Task PassingSynchronousRules_DoNotAllocatePerValidatedRow()
        {
            var eval = NewEvaluator();
            eval.Telemetry.IsProfiling = true;
            var script = new Lexer(@"
                SELECT Id /* @expect: 'NOT NULL, >= 0, IN (1, 2)'; @fail: 'WARN'; */
                FROM #src ON FAILURE WARN;").TokenizeToScript();
            var statement = Assert.IsType<SelectStatement>(Assert.Single(script.Statements));
            var validator = Assert.IsType<ColumnQualityValidator>(
                ColumnQualityValidator.TryCreate(eval, eval.Logger, statement, ["Id"]));
            await validator.InitializeAsync();

            var row = new Row();
            row[0] = 1m;
            for (var i = 0; i < 1_000; i++)
                Assert.True(await validator.TryAcceptRowAsync(row, row));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var allAcceptedSynchronously = true;
            const int measuredRows = 100_000;
            for (var i = 0; i < measuredRows; i++)
            {
                var result = validator.TryAcceptRowAsync(row, row);
                allAcceptedSynchronously &= ReadCompleted(result);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.True(allAcceptedSynchronously);
            Assert.True(allocated <= 4_096,
                $"Synchronous data-quality validation allocated {allocated:N0} bytes for {measuredRows:N0} passing rows.");
        }

        private static bool ReadCompleted(ValueTask<bool> result) =>
            result.IsCompletedSuccessfully && result.Result;

        // ── Harness ────────────────────────────────────────────────────────

        private static Evaluator NewEvaluator() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Task Seed(Evaluator eval, string valuesList) => Run(eval, $@"
            CREATE TABLE #src (Id INT, Name VARCHAR(100));
            INSERT INTO #src (Id, Name) VALUES {valuesList};");

        private static async Task Run(Evaluator eval, string sql) =>
            await eval.Evaluate(new Lexer(sql).TokenizeToScript());

        private static async Task<int> CountRows(Evaluator eval, string table) =>
            (await ReadRows(eval, table)).Count;

        private static async Task<System.Collections.Generic.List<Row>> ReadRows(Evaluator eval, string table)
        {
            var rows = new System.Collections.Generic.List<Row>();
            if (!eval.Connections.TryGetValue(table, out var source)) return rows;
            await foreach (var batch in source.ReadBatches(1000))
                rows.AddRange(batch.Rows);
            return rows;
        }

        private sealed class CapturingMetricsProvider : IJobMetricsProvider
        {
            public List<QuarantineReplayManifest> Manifests { get; } = [];
            public bool AllowLeaseAcquire { get; init; } = true;
            public int LeasesAcquired { get; private set; }
            public int LeasesReleased { get; private set; }
            private readonly Dictionary<string, QuarantineReplayManifest> _manifestsByJobAndTarget =
                new(StringComparer.OrdinalIgnoreCase);

            public Task<IReadOnlyList<JobRunMetrics>> GetRecentRunMetricsAsync(
                string jobName,
                int limit,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<JobRunMetrics>>(Array.Empty<JobRunMetrics>());

            public Task<QuarantineReplayManifest?> GetQuarantineReplayManifestAsync(
                string jobName,
                string quarantineTarget,
                CancellationToken cancellationToken = default)
            {
                _manifestsByJobAndTarget.TryGetValue($"{jobName}:{NormalizeTarget(quarantineTarget)}", out var manifest);
                return Task.FromResult(manifest);
            }

            public Task SaveQuarantineReplayManifestAsync(
                QuarantineReplayManifest manifest,
                CancellationToken cancellationToken = default)
            {
                Manifests.Add(manifest);
                _manifestsByJobAndTarget[$"{manifest.JobName}:{NormalizeTarget(manifest.QuarantineTarget)}"] = manifest;
                return Task.CompletedTask;
            }

            public Task<bool> TryAcquireQuarantineReplayLeaseAsync(
                string jobName,
                string quarantineTarget,
                string owner,
                TimeSpan ttl,
                CancellationToken cancellationToken = default)
            {
                LeasesAcquired++;
                return Task.FromResult(AllowLeaseAcquire);
            }

            public Task ReleaseQuarantineReplayLeaseAsync(
                string jobName,
                string quarantineTarget,
                string owner,
                CancellationToken cancellationToken = default)
            {
                LeasesReleased++;
                return Task.CompletedTask;
            }

            private static string NormalizeTarget(string target) =>
                target.Trim().TrimStart('#').ToLowerInvariant();
        }

        /// <summary>
        /// A read-once source that counts how many times its stream was enumerated, standing in for
        /// a non-rewindable source (Kafka, paginated REST).
        /// </summary>
        private sealed class RetentionPruningDataSource : IDataSource, IDataQualityRetentionPruner
        {
            public List<Row> WrittenRows { get; } = [];
            public int PruneCalls { get; private set; }
            public string? TimestampColumn { get; private set; }
            public DateTime CutoffUtc { get; private set; }
            public string? ScopeColumn { get; private set; }
            public string? ScopeValue { get; private set; }

            public string Path => "retention-pruning";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "RETENTION_TEST";

            public async Task<int> PruneDataQualityRowsAsync(
                string timestampColumn,
                DateTime cutoffUtc,
                string scopeColumn,
                string scopeValue,
                CancellationToken cancellationToken)
            {
                await Task.Yield();
                PruneCalls++;
                TimestampColumn = timestampColumn;
                CutoffUtc = cutoffUtc;
                ScopeColumn = scopeColumn;
                ScopeValue = scopeValue;
                return 0;
            }

            public async IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000)
            {
                var table = new DataTable();
                table.SetColumns(WrittenRows.FirstOrDefault()?.Columns.Keys ?? Enumerable.Empty<string>());
                foreach (var row in WrittenRows)
                    await table.AddRowAsync(row);
                yield return table;
            }

            public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
            {
                if (!append) WrittenRows.Clear();
                await foreach (var batch in batches)
                    WrittenRows.AddRange(batch.Rows);
            }

            public Task<IEnumerable<string>> GetColumnsAsync() =>
                Task.FromResult<IEnumerable<string>>(WrittenRows.FirstOrDefault()?.Columns.Keys ?? Enumerable.Empty<string>());
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class ReadCountingDataSource : IDataSource
        {
            private readonly (string First, string Second) _columns;
            private readonly (object? Id, object? Name)[] _rows;

            public ReadCountingDataSource((string, string) columns, (object?, object?)[] rows)
            {
                _columns = columns;
                _rows = rows;
            }

            public int ReadCount { get; private set; }

            public string Path => "counted";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "COUNTING";

            public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
                ReadBatches(batchSize, System.Threading.CancellationToken.None);

            public async IAsyncEnumerable<DataTable> ReadBatches(
                int batchSize,
                [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken)
            {
                ReadCount++;
                var table = new DataTable();
                table.SetColumns(new[] { _columns.First, _columns.Second });
                foreach (var (id, name) in _rows)
                {
                    var row = table.NewRow();
                    row[_columns.First] = id;
                    row[_columns.Second] = name;
                    await table.AddRowAsync(row);
                }
                yield return table;
            }

            public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) =>
                throw new NotSupportedException();
            public Task<IEnumerable<string>> GetColumnsAsync() =>
                Task.FromResult<IEnumerable<string>>(new[] { _columns.First, _columns.Second });
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
