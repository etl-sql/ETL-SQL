using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.CliCommands
{
    public class HaSoakAdminServiceTests : IDisposable
    {
        private readonly string _outputRoot;
        private readonly string _runId;
        private readonly string _evidenceRoot;

        public HaSoakAdminServiceTests()
        {
            _runId = "native-ha-test-" + Guid.NewGuid().ToString("N");
            _outputRoot = Path.Combine(Path.GetTempPath(), "etl-ha-soak-tests-" + Guid.NewGuid().ToString("N"));
            _evidenceRoot = Path.Combine(Directory.GetCurrentDirectory(), "certification-results");
        }

        public void Dispose()
        {
            TryDelete(_outputRoot);
            TryDelete(Path.Combine(_evidenceRoot, "postgres-ha-soak", _runId));
            TryDelete(Path.Combine(_evidenceRoot, "ha-large-job-soak", _runId));
            TryDelete(Path.Combine(_evidenceRoot, "ha-fault-injection", _runId));
        }

        [Fact]
        public async Task NativeCommandsGenerateOperatorArtifactsWithoutPowershellReferences()
        {
            var logger = new CapturingLogger();
            var runRoot = Path.Combine(_outputRoot, _runId);

            var prepareExit = await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-prepare",
                HaSoakRunId = _runId,
                HaSoakOutputRoot = _outputRoot,
                HaSoakPortalPort = 6600,
                HaSoakOrchestratorPort = 6601,
                HaSoakPostgresPort = 6632,
                HaSoakForce = true
            }, logger);

            Assert.Equal(0, prepareExit);
            Assert.True(File.Exists(Path.Combine(runRoot, "topology-metadata.json")));
            Assert.True(File.Exists(Path.Combine(runRoot, "postgres-ha-soak.env")));
            Assert.True(File.Exists(Path.Combine(runRoot, "README.md")));
            var envText = File.ReadAllText(Path.Combine(runRoot, "postgres-ha-soak.env"));
            var generatedAdminPassword = ReadEnvValue(envText, "PORTAL_ADMIN_PASSWORD");
            Assert.False(string.IsNullOrWhiteSpace(generatedAdminPassword));
            Assert.Equal("false", ReadEnvValue(envText, "PORTAL_ADMIN_MUST_CHANGE_PASSWORD"));

            var metadataText = File.ReadAllText(Path.Combine(runRoot, "topology-metadata.json"));
            var metadata = JsonNode.Parse(metadataText)!.AsObject();
            Assert.Equal(_runId, (string?)metadata["runId"]);
            Assert.Equal(6600, (int?)metadata["ports"]!["portal"]);
            Assert.Contains("etl-sql admin ha-soak diagnostics", metadataText);
            Assert.DoesNotContain("PG_PASSWORD=", metadataText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ORCH_API_KEY=", metadataText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PORTAL_ADMIN_PASSWORD=", metadataText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".ps1", metadataText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Test-GateF", metadataText, StringComparison.OrdinalIgnoreCase);

            var workloadExit = await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-workload",
                HaSoakRunRoot = runRoot,
                HaSoakForce = true
            }, logger);

            Assert.Equal(0, workloadExit);
            var workloadPath = Path.Combine(runRoot, "postgres-ha-sustained.workload.local.json");
            var workloadText = File.ReadAllText(workloadPath);
            var workload = JsonNode.Parse(workloadText)!.AsObject();
            Assert.Equal("http://localhost:6600", (string?)workload["portal"]!["baseUrl"]);
            Assert.Equal("http://localhost:6601", (string?)workload["orchestrator"]!["baseUrl"]);
            Assert.Equal(generatedAdminPassword, (string?)workload["portal"]!["roles"]!["admin"]!["password"]);
            Assert.False(string.IsNullOrWhiteSpace((string?)workload["orchestrator"]!["apiKey"]));

            Assert.Equal(0, await RunAsync(new CliContext { Command = "admin-ha-soak-runbook", HaSoakRunRoot = runRoot, HaSoakForce = true }, logger));
            Assert.Equal(0, await RunAsync(new CliContext { Command = "admin-ha-soak-evidence", HaSoakRunRoot = runRoot, HaSoakSustainedWorkloadPath = workloadPath, HaSoakForce = true }, logger));
            Assert.Equal(0, await RunAsync(new CliContext { Command = "admin-ha-soak-large-job-plan", HaSoakRunRoot = runRoot, HaSoakForce = true }, logger));
            Assert.Equal(0, await RunAsync(new CliContext { Command = "admin-ha-soak-fault-plan", HaSoakRunRoot = runRoot, HaSoakForce = true }, logger));
            Assert.Equal(0, await RunAsync(new CliContext { Command = "admin-ha-soak-metrics", HaSoakRunRoot = runRoot, HaSoakValidateOnly = true }, logger));

            var diagnosticsRoot = Path.Combine(runRoot, "diagnostics", "test");
            Assert.Equal(0, await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-diagnostics",
                HaSoakRunRoot = runRoot,
                HaSoakOutputRoot = diagnosticsRoot,
                HaSoakNoDocker = true,
                HaSoakForce = true
            }, logger));

            var generatedOperatorText = string.Join(
                Environment.NewLine,
                File.ReadAllText(Path.Combine(runRoot, "ha-soak-runbook.json")),
                File.ReadAllText(Path.Combine(runRoot, "ha-soak-runbook.md")),
                File.ReadAllText(Path.Combine(runRoot, "ha-soak-evidence-plan.json")),
                File.ReadAllText(Path.Combine(runRoot, "ha-large-job-soak-plan.json")),
                File.ReadAllText(Path.Combine(runRoot, "ha-fault-injection-plan.json")));
            Assert.Contains("etl-sql admin ha-soak", generatedOperatorText);
            Assert.DoesNotContain(".ps1", generatedOperatorText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Test-GateF", generatedOperatorText, StringComparison.OrdinalIgnoreCase);

            var redactedEnv = File.ReadAllText(Path.Combine(diagnosticsRoot, "postgres-ha-soak.redacted.env"));
            Assert.Contains("PG_PASSWORD=********", redactedEnv);
            Assert.Contains("ORCH_API_KEY=********", redactedEnv);
            Assert.Contains("PORTAL_ADMIN_PASSWORD=********", redactedEnv);
            Assert.Contains("PORTAL_ADMIN_MUST_CHANGE_PASSWORD=false", redactedEnv);
            Assert.DoesNotContain("ORCH_API_KEY=", File.ReadAllText(Path.Combine(diagnosticsRoot, "topology-metadata.json")), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(generatedAdminPassword, redactedEnv);
        }

        [Fact]
        public async Task EvidenceValidationFailsUntilRequiredSustainedArtifactsExist()
        {
            var logger = new CapturingLogger();
            var runRoot = Path.Combine(_outputRoot, _runId);

            Assert.Equal(0, await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-prepare",
                HaSoakRunId = _runId,
                HaSoakOutputRoot = _outputRoot,
                HaSoakForce = true
            }, logger));
            Assert.Equal(0, await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-evidence",
                HaSoakRunRoot = runRoot,
                HaSoakForce = true
            }, logger));

            var reportPath = Path.Combine(_outputRoot, "validation-before.md");
            var missingExit = await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-validate",
                HaSoakRunRoot = runRoot,
                HaSoakRequiredGate = "Sustained",
                HaSoakAllowDirty = true,
                HaSoakMarkdownReport = reportPath
            }, logger);

            Assert.Equal(1, missingExit);
            Assert.Contains("missing-artifact", File.ReadAllText(reportPath));

            SeedSustainedEvidence();
            var passedReportPath = Path.Combine(_outputRoot, "validation-after.md");
            var passedExit = await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-validate",
                HaSoakRunRoot = runRoot,
                HaSoakRequiredGate = "Sustained",
                HaSoakAllowDirty = true,
                HaSoakMarkdownReport = passedReportPath
            }, logger);

            Assert.Equal(0, passedExit);
            Assert.Contains("Status: **Passed**", File.ReadAllText(passedReportPath));
        }

        [Fact]
        public async Task LargeJobRunProducesEvidenceAcceptedByValidator()
        {
            var logger = new CapturingLogger();
            var runRoot = Path.Combine(_outputRoot, _runId);

            Assert.Equal(0, await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-prepare",
                HaSoakRunId = _runId,
                HaSoakOutputRoot = _outputRoot,
                HaSoakForce = true
            }, logger));
            Assert.Equal(0, await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-large-job-plan",
                HaSoakRunRoot = runRoot,
                HaSoakForce = true
            }, logger));
            Assert.Equal(0, await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-evidence",
                HaSoakRunRoot = runRoot,
                HaSoakForce = true
            }, logger));

            var runExit = await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-large-job-run",
                HaSoakRunRoot = runRoot,
                HaSoakDurationSeconds = 1,
                HaSoakForce = true
            }, logger);

            Assert.Equal(0, runExit);
            var outputRoot = Path.Combine(_evidenceRoot, "ha-large-job-soak", _runId);
            Assert.True(File.Exists(Path.Combine(outputRoot, "soak-report.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "soak-report.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "MixedScanSpillSortJoinAggregate_Concurrent", "runner.log")));

            var report = JsonNode.Parse(File.ReadAllText(Path.Combine(outputRoot, "soak-report.json")))!.AsObject();
            Assert.True((bool?)report["passed"]);
            Assert.Equal("NativeBoundedLargeJobCiSmoke", (string?)report["runnerKind"]);

            var validationReport = Path.Combine(_outputRoot, "large-job-validation.md");
            var validationExit = await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-validate",
                HaSoakRunRoot = runRoot,
                HaSoakRequiredGate = "LargeJob",
                HaSoakAllowDirty = true,
                HaSoakMarkdownReport = validationReport
            }, logger);

            Assert.True(validationExit == 0, File.ReadAllText(validationReport));
            Assert.Contains("Status: **Passed**", File.ReadAllText(validationReport));
        }

        [Fact]
        public async Task FaultRunProducesEvidenceAcceptedByValidator()
        {
            var logger = new CapturingLogger();
            var runRoot = Path.Combine(_outputRoot, _runId);

            Assert.Equal(0, await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-prepare",
                HaSoakRunId = _runId,
                HaSoakOutputRoot = _outputRoot,
                HaSoakForce = true
            }, logger));
            Assert.Equal(0, await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-fault-plan",
                HaSoakRunRoot = runRoot,
                HaSoakForce = true
            }, logger));
            Assert.Equal(0, await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-evidence",
                HaSoakRunRoot = runRoot,
                HaSoakForce = true
            }, logger));

            var runExit = await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-fault-run",
                HaSoakRunRoot = runRoot,
                HaSoakForce = true
            }, logger);

            Assert.Equal(0, runExit);
            var outputRoot = Path.Combine(_evidenceRoot, "ha-fault-injection", _runId);
            Assert.True(File.Exists(Path.Combine(outputRoot, "fault-report.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "fault-report.md")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "DiskFullDuringExtentWrite", "runner.log")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "DiskFullDuringExtentWrite", "cleanup-invariants.json")));

            var report = JsonNode.Parse(File.ReadAllText(Path.Combine(outputRoot, "fault-report.json")))!.AsObject();
            Assert.True((bool?)report["passed"]);
            Assert.Equal("NativeBoundedFaultInjectionCiSmoke", (string?)report["runnerKind"]);

            var validationReport = Path.Combine(_outputRoot, "fault-validation.md");
            var validationExit = await RunAsync(new CliContext
            {
                Command = "admin-ha-soak-validate",
                HaSoakRunRoot = runRoot,
                HaSoakRequiredGate = "FaultInjection",
                HaSoakAllowDirty = true,
                HaSoakMarkdownReport = validationReport
            }, logger);

            Assert.True(validationExit == 0, File.ReadAllText(validationReport));
            Assert.Contains("Status: **Passed**", File.ReadAllText(validationReport));
        }

        private void SeedSustainedEvidence()
        {
            var dir = Path.Combine(_evidenceRoot, "postgres-ha-soak", _runId);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "capacity-report.json"), """
            {
              "portal": [
                { "concurrency": 1, "passed": true, "breaches": [] }
              ],
              "orchestrator": [
                { "concurrency": 1, "passed": true, "breaches": [] }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(dir, "capacity-report.md"), "# Capacity Report");
            File.WriteAllText(Path.Combine(dir, "postgres-ha-metrics.json"), """{ "schemaVersion": 1, "nonSecret": true }""");
            File.WriteAllText(Path.Combine(dir, "postgres-ha-metrics.md"), "# PostgreSQL HA Metrics");
        }

        private static Task<int> RunAsync(CliContext ctx, ILogger logger) =>
            HaSoakAdminService.RunAsync(ctx, logger);

        private static string? ReadEnvValue(string text, string key)
        {
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    return line[(key.Length + 1)..];
            return null;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only; test assertions cover generated artifacts before disposal.
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public List<string> Messages { get; } = new();

            public string? SessionId { get; set; }
            public bool IsDebugEnabled => true;
            public bool IsVerboseEnabled => true;
            public bool IsVerbose { get; set; }
            public bool SuppressConsole { get; set; }
            public bool IsJsonMode { get; set; }
            public event Action<string, string?, ConsoleColor>? OnMessage;

            public void Log(LogLevel level, string message, Exception? ex = null)
            {
                Messages.Add(message);
                OnMessage?.Invoke(message, null, ConsoleColor.White);
            }
        }
    }
}
