using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.App
{
    internal static class HaSoakAdminService
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static readonly Regex SecretEnvPattern = new("^(PG_PASSWORD|PORTAL_JWT_SECRET|PORTAL_DATASET_KEY|ORCH_API_KEY)=", RegexOptions.Compiled);
        private static readonly Regex RunIdPattern = new("^[a-zA-Z0-9][a-zA-Z0-9_.-]*$", RegexOptions.Compiled);

        internal static async Task<int> RunAsync(CliContext ctx, ILogger logger)
        {
            try
            {
                if (RequiresRunRoot(ctx.Command) && string.IsNullOrWhiteSpace(ctx.HaSoakRunRoot))
                    return Fail(logger, "--run-root is required for this HA soak command.");

                if (!string.IsNullOrWhiteSpace(ctx.HaSoakMode) && ctx.HaSoakMode is not ("CiSmoke" or "ManualCertification"))
                    return Fail(logger, "--mode must be CiSmoke or ManualCertification.");

                return ctx.Command switch
                {
                    "admin-ha-soak-prepare" => await PrepareAsync(ctx, logger),
                    "admin-ha-soak-workload" => await WorkloadAsync(ctx, logger),
                    "admin-ha-soak-runbook" => await RunbookAsync(ctx, logger),
                    "admin-ha-soak-evidence" => await EvidenceAsync(ctx, logger),
                    "admin-ha-soak-large-job-plan" => await LargeJobPlanAsync(ctx, logger),
                    "admin-ha-soak-fault-plan" => await FaultPlanAsync(ctx, logger),
                    "admin-ha-soak-metrics" => await MetricsAsync(ctx, logger),
                    "admin-ha-soak-validate" => await ValidateEvidenceAsync(ctx, logger),
                    "admin-ha-soak-diagnostics" => await DiagnosticsAsync(ctx, logger),
                    _ => Fail(logger, $"Unknown HA soak admin command: {ctx.Command}")
                };
            }
            catch (Exception ex)
            {
                logger.WriteLine($"HA soak command failed: {ex.Message}", ConsoleColor.Red);
                return 1;
            }
        }

        private static bool RequiresRunRoot(string command) => command != "admin-ha-soak-prepare";

        private static int Fail(ILogger logger, string message)
        {
            logger.WriteLine(message, ConsoleColor.Red);
            return 1;
        }

        private static Task<int> PrepareAsync(CliContext ctx, ILogger logger)
        {
            AssertPositive(ctx.HaSoakPortalScale, "portal scale");
            AssertPositive(ctx.HaSoakOrchestratorScale, "orchestrator scale");
            AssertPositive(ctx.HaSoakPortalPort, "portal port");
            AssertPositive(ctx.HaSoakOrchestratorPort, "orchestrator port");
            AssertPositive(ctx.HaSoakPostgresPort, "postgres port");

            var composePath = ResolveRepoPath(ctx.HaSoakComposeFile ?? "deploy/docker/docker-compose.ha.yml");
            var examplePath = ResolveRepoPath(ctx.HaSoakEnvExample ?? "deploy/docker/environment-ha.env.example");
            AssertTopologyTemplate(composePath, examplePath);

            if (ctx.HaSoakValidateOnly)
            {
                logger.WriteLine("PostgreSQL HA soak topology template is valid.", ConsoleColor.Green);
                logger.WriteLine($"Compose file: {composePath}", ConsoleColor.Gray);
                logger.WriteLine($"Environment example: {examplePath}", ConsoleColor.Gray);
                return Task.FromResult(0);
            }

            var runId = string.IsNullOrWhiteSpace(ctx.HaSoakRunId)
                ? "ha-soak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                : ctx.HaSoakRunId.Trim();
            if (!RunIdPattern.IsMatch(runId))
                throw new InvalidOperationException("Run id must contain only letters, numbers, dot, underscore, or hyphen, and must not start with punctuation.");

            var outputRoot = ResolveRepoPath(ctx.HaSoakOutputRoot ?? ".ha-soak-runs");
            var runRoot = Path.Combine(outputRoot, runId);
            if (Directory.Exists(runRoot))
            {
                if (!ctx.HaSoakForce)
                    throw new InvalidOperationException($"Run directory already exists: {runRoot}. Use --force to replace generated configuration.");
                var fullRunRoot = Path.GetFullPath(runRoot);
                var fullOutputRoot = Path.GetFullPath(outputRoot);
                if (!fullRunRoot.StartsWith(fullOutputRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Refusing to replace run directory outside output root: {runRoot}");
                Directory.Delete(fullRunRoot, recursive: true);
            }

            var dataRoot = Path.Combine(runRoot, "data");
            foreach (var path in new[]
            {
                runRoot, dataRoot, Path.Combine(dataRoot, "Reports"), Path.Combine(dataRoot, "Snapshots"),
                Path.Combine(dataRoot, "datasets"), Path.Combine(dataRoot, "maps"),
                Path.Combine(dataRoot, "portal-data"), Path.Combine(dataRoot, "logs")
            })
            {
                Directory.CreateDirectory(path);
            }

            var projectSuffix = Regex.Replace(runId.ToLowerInvariant(), "[^a-z0-9-]", "-");
            var envFile = Path.Combine(runRoot, "postgres-ha-soak.env");
            File.WriteAllLines(envFile, new[]
            {
                "ETLSQL_ENV=postgres-ha-soak",
                $"COMPOSE_PROJECT_NAME=etlsql-{projectSuffix}",
                $"ETLSQL_IMAGE_TAG={ctx.HaSoakImageTag ?? "latest"}",
                $"PORT_PORTAL={ctx.HaSoakPortalPort}",
                $"PORT_ORCH={ctx.HaSoakOrchestratorPort}",
                $"PORT_PG={ctx.HaSoakPostgresPort}",
                $"ENV_DATA_ROOT={ToEnvPath(dataRoot)}",
                "PG_USER=etlsql_ha_soak",
                $"PG_PASSWORD={NewBase64Secret(24)}",
                "PG_DB_PORTAL=portal",
                "PG_DB_ORCH=orch",
                $"PORTAL_JWT_SECRET={NewBase64Secret(48)}",
                $"PORTAL_DATASET_KEY={NewBase64Secret(32)}",
                $"ORCH_API_KEY={NewBase64Secret(32)}",
                "PORTAL_ADMIN_USERNAME=admin"
            });

            var composeRelative = RelativeLabel(composePath);
            var envRelative = RelativeLabel(envFile);
            var runRelative = RelativeLabel(runRoot);
            var metadata = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["phase"] = "v0.15.0 Phase 6",
                ["generatedAt"] = DateTime.UtcNow.ToString("o"),
                ["commit"] = GetGitCommit(),
                ["runId"] = runId,
                ["composeFile"] = composeRelative,
                ["envFile"] = envRelative,
                ["dataRoot"] = ToEnvPath(Path.GetFullPath(dataRoot)),
                ["topology"] = new JsonObject
                {
                    ["postgres"] = 1,
                    ["portal"] = ctx.HaSoakPortalScale,
                    ["orchestrator"] = ctx.HaSoakOrchestratorScale,
                    ["loadBalancer"] = 1
                },
                ["ports"] = new JsonObject
                {
                    ["portal"] = ctx.HaSoakPortalPort,
                    ["orchestrator"] = ctx.HaSoakOrchestratorPort,
                    ["postgres"] = ctx.HaSoakPostgresPort
                },
                ["requirements"] = new JsonObject
                {
                    ["portalDatabaseProvider"] = "Postgres",
                    ["orchestratorDatabaseProvider"] = "Postgres",
                    ["sharedArtifactRoot"] = "ENV_DATA_ROOT",
                    ["sharedDataProtectionKeyRing"] = "Portal__Storage__KeyRingPath=/app/data/.portal-keys",
                    ["stickyAffinity"] = "ETLSQL_PORTAL_AFFINITY via deploy/docker/haproxy.cfg",
                    ["orchestratorAuthentication"] = "X-Orchestrator-Key"
                },
                ["commands"] = new JsonObject
                {
                    ["start"] = $"docker compose --env-file \"{envRelative}\" -f \"{composeRelative}\" up -d --scale portal={ctx.HaSoakPortalScale} --scale orchestrator={ctx.HaSoakOrchestratorScale}",
                    ["status"] = $"docker compose --env-file \"{envRelative}\" -f \"{composeRelative}\" ps",
                    ["stop"] = $"docker compose --env-file \"{envRelative}\" -f \"{composeRelative}\" down",
                    ["diagnostics"] = $"etl-sql admin ha-soak diagnostics --run-root \"{runRelative}\"",
                    ["runbook"] = $"etl-sql admin ha-soak runbook --run-root \"{runRelative}\""
                },
                ["secrets"] = "Generated only in envFile; intentionally omitted from metadata."
            };

            var metadataPath = Path.Combine(runRoot, "topology-metadata.json");
            WriteJson(metadataPath, metadata);
            File.WriteAllLines(Path.Combine(runRoot, "README.md"), new[]
            {
                "# PostgreSQL HA Soak Topology Run", "",
                $"Run id: `{runId}`", "",
                "Generated files:", "",
                "- `postgres-ha-soak.env` - local disposable credentials and port/data-root settings. Do not commit this file.",
                "- `topology-metadata.json` - non-secret run metadata for capacity and soak evidence.",
                "- `ha-soak-runbook.md` - optional operator command sequence generated by the runbook command below.",
                "", "Generate operator runbook:", "", "```bash", metadata["commands"]!["runbook"]!.GetValue<string>(), "```",
                "", "Start:", "", "```bash", metadata["commands"]!["start"]!.GetValue<string>(), "```",
                "", "Stop:", "", "```bash", metadata["commands"]!["stop"]!.GetValue<string>(), "```",
                "", "Diagnostics after any failed or completed run:", "", "```bash", metadata["commands"]!["diagnostics"]!.GetValue<string>(), "```"
            });

            if (ctx.HaSoakPull)
                RunDocker(logger, new[] { "compose", "--env-file", envFile, "-f", composePath, "pull" });
            if (ctx.HaSoakStart)
                RunDocker(logger, new[] { "compose", "--env-file", envFile, "-f", composePath, "up", "-d", "--scale", $"portal={ctx.HaSoakPortalScale}", "--scale", $"orchestrator={ctx.HaSoakOrchestratorScale}" });

            logger.WriteLine($"HA soak topology prepared: {Path.GetFullPath(runRoot)}", ConsoleColor.Green);
            logger.WriteLine($"Metadata: {metadataPath}", ConsoleColor.Gray);
            return Task.FromResult(0);
        }

        private static Task<int> WorkloadAsync(CliContext ctx, ILogger logger)
        {
            var runRoot = ResolveExistingDirectory(ctx.HaSoakRunRoot!);
            var envFile = RequireFile(Path.Combine(runRoot, "postgres-ha-soak.env"), "PostgreSQL HA soak env file");
            var metadataPath = RequireFile(Path.Combine(runRoot, "topology-metadata.json"), "PostgreSQL HA soak topology metadata");
            var templatePath = ResolveRepoPath("capacity-results/workloads/postgres-ha-sustained.workload.json");
            var outputPath = string.IsNullOrWhiteSpace(ctx.HaSoakOutputPath)
                ? Path.Combine(runRoot, "postgres-ha-sustained.workload.local.json")
                : Path.GetFullPath(ctx.HaSoakOutputPath.Trim());
            AssertCanWrite(outputPath, ctx.HaSoakForce, "Output workload");

            var env = ReadEnv(envFile);
            var metadata = ReadJsonObject(metadataPath);
            var workload = ReadJsonObject(templatePath);
            RequireEnv(env, "PORT_PORTAL", "PORT_ORCH", "ORCH_API_KEY");

            workload["environment"] ??= new JsonObject();
            var environment = workload["environment"]!.AsObject();
            environment["deploymentMode"] = $"PostgreSQL HA soak topology ({metadata["runId"]?.GetValue<string>()})";
            environment["databaseLocation"] = $"PostgreSQL via {metadata["composeFile"]?.GetValue<string>()}";
            environment["notes"] = $"Materialized from {metadata["envFile"]?.GetValue<string>()}. Generated workload contains the local Orchestrator API key; do not commit it.";
            environment["topologyMetadataPath"] = metadataPath;

            workload["portal"]!["baseUrl"] = $"http://localhost:{env["PORT_PORTAL"]}";
            workload["portal"]!["roles"]!["admin"]!["password"] = ctx.HaSoakAdminPassword ?? "CHANGE_ME";
            workload["orchestrator"]!["baseUrl"] = $"http://localhost:{env["PORT_ORCH"]}";
            workload["orchestrator"]!["apiKey"] = env["ORCH_API_KEY"];
            foreach (var setup in workload["setupRequests"]?.AsArray() ?? new JsonArray())
                if (setup?["baseUrl"] != null) setup["baseUrl"] = $"http://localhost:{env["PORT_ORCH"]}";
            foreach (var cleanup in workload["cleanupRequests"]?.AsArray() ?? new JsonArray())
                if (cleanup?["baseUrl"] != null) cleanup["baseUrl"] = $"http://localhost:{env["PORT_ORCH"]}";

            WriteJson(outputPath, workload);
            logger.WriteLine($"HA sustained workload written: {outputPath}", ConsoleColor.Green);
            return Task.FromResult(0);
        }

        private static Task<int> RunbookAsync(CliContext ctx, ILogger logger)
        {
            var runRoot = ResolveExistingDirectory(ctx.HaSoakRunRoot!);
            var metadataPath = RequireFile(Path.Combine(runRoot, "topology-metadata.json"), "Topology metadata");
            var metadata = ReadJsonObject(metadataPath);
            var mode = ctx.HaSoakMode ?? "CiSmoke";
            var outputPath = string.IsNullOrWhiteSpace(ctx.HaSoakOutputPath) ? Path.Combine(runRoot, "ha-soak-runbook.json") : Path.GetFullPath(ctx.HaSoakOutputPath.Trim());
            AssertCanWrite(outputPath, ctx.HaSoakForce, "Runbook");
            var runId = metadata["runId"]!.GetValue<string>();
            var runLabel = RelativeLabel(runRoot);
            var workload = string.IsNullOrWhiteSpace(ctx.HaSoakSustainedWorkloadPath)
                ? (File.Exists(Path.Combine(runRoot, "postgres-ha-sustained.workload.local.json")) ? RelativeLabel(Path.Combine(runRoot, "postgres-ha-sustained.workload.local.json")) : null)
                : RelativeLabel(ctx.HaSoakSustainedWorkloadPath);
            var sustainedOut = $"certification-results/postgres-ha-soak/{runId}";
            var largeOut = $"certification-results/ha-large-job-soak/{runId}";
            var faultOut = $"certification-results/ha-fault-injection/{runId}";

            var steps = new JsonArray
            {
                Step(1, "Start topology", metadata["commands"]!["start"]!.GetValue<string>(), "topology-metadata.json", "postgres-ha-soak.env"),
                Step(2, "Check topology status", metadata["commands"]!["status"]!.GetValue<string>(), "docker-compose-ps output"),
                Step(3, "Materialize sustained workload", $"etl-sql admin ha-soak workload --run-root \"{runLabel}\" --admin-password PORTAL_ADMIN_PASSWORD --force", "postgres-ha-sustained.workload.local.json"),
                Step(4, "Run sustained service capacity workload", $"node scripts/test-service-capacity.mjs --config \"{workload ?? $"{runLabel}/postgres-ha-sustained.workload.local.json"}\" --out-dir \"{sustainedOut}\"", $"{sustainedOut}/capacity-report.json", $"{sustainedOut}/capacity-report.md"),
                Step(5, "Capture PostgreSQL metrics", $"etl-sql admin ha-soak metrics --run-root \"{runLabel}\" --output \"{sustainedOut}/postgres-ha-metrics.json\" --force", $"{sustainedOut}/postgres-ha-metrics.json", $"{sustainedOut}/postgres-ha-metrics.md"),
                Step(6, "Create large-job soak plan", $"etl-sql admin ha-soak large-job-plan --run-root \"{runLabel}\" --mode {mode} --output \"{largeOut}/ha-large-job-soak-plan.json\" --force", $"{largeOut}/ha-large-job-soak-plan.json", $"{largeOut}/ha-large-job-soak-plan.md"),
                Step(7, "Create fault-injection plan", $"etl-sql admin ha-soak fault-plan --run-root \"{runLabel}\" --mode {mode} --output \"{faultOut}/ha-fault-injection-plan.json\" --force", $"{faultOut}/ha-fault-injection-plan.json", $"{faultOut}/ha-fault-injection-plan.md"),
                Step(8, "Collect diagnostics", $"etl-sql admin ha-soak diagnostics --run-root \"{runLabel}\"", $"{runLabel}/diagnostics/<timestamp>/diagnostic-summary.json", $"{runLabel}/diagnostics/<timestamp>/docker-compose-logs.txt"),
                Step(9, "Stop topology", metadata["commands"]!["stop"]!.GetValue<string>(), "docker compose down output")
            };
            var runbook = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["phase"] = "v0.15.0 Phase 6",
                ["generatedAt"] = DateTime.UtcNow.ToString("o"),
                ["runId"] = runId,
                ["mode"] = mode,
                ["topologyMetadataPath"] = RelativeLabel(metadataPath),
                ["sustainedWorkloadPath"] = workload,
                ["expectedOutputDirectories"] = new JsonObject { ["sustainedLoad"] = sustainedOut, ["largeJobSoak"] = largeOut, ["faultInjection"] = faultOut },
                ["diagnostics"] = new JsonObject
                {
                    ["command"] = $"etl-sql admin ha-soak diagnostics --run-root \"{runLabel}\"",
                    ["defaultOutputRoot"] = $"{runLabel}/diagnostics/<timestamp>",
                    ["expectedArtifacts"] = JsonArrayFrom("diagnostic-summary.json", "postgres-ha-soak.redacted.env", "run-root-inventory.json", "docker-compose-ps.txt", "docker-compose-logs.txt")
                },
                ["steps"] = steps,
                ["nonSecret"] = true
            };
            WriteJson(outputPath, runbook);
            WriteRunbookMarkdown(ChangeExtension(outputPath, ".md"), runbook);
            logger.WriteLine($"HA soak runbook written: {outputPath}", ConsoleColor.Green);
            return Task.FromResult(0);
        }

        private static Task<int> EvidenceAsync(CliContext ctx, ILogger logger)
        {
            var runRoot = ResolveExistingDirectory(ctx.HaSoakRunRoot!);
            var metadataPath = RequireFile(Path.Combine(runRoot, "topology-metadata.json"), "Topology metadata");
            var metadata = ReadJsonObject(metadataPath);
            var runId = metadata["runId"]!.GetValue<string>();
            var outputPath = string.IsNullOrWhiteSpace(ctx.HaSoakOutputPath) ? Path.Combine(runRoot, "ha-soak-evidence-plan.json") : Path.GetFullPath(ctx.HaSoakOutputPath.Trim());
            AssertCanWrite(outputPath, ctx.HaSoakForce, "Evidence plan");
            var workload = string.IsNullOrWhiteSpace(ctx.HaSoakSustainedWorkloadPath) ? null : RelativeLabel(ctx.HaSoakSustainedWorkloadPath);
            var largeManifest = ResolveRepoPath("certification-results/ha-large-job-soak-scenarios.json");
            var faultMatrix = ResolveRepoPath("certification-results/ha-fault-injection-matrix.json");
            var largeCount = ReadJsonObject(largeManifest)["scenarios"]!.AsArray().Count;
            var faultCount = ReadJsonObject(faultMatrix)["faults"]!.AsArray().Count;
            var runLabel = RelativeLabel(runRoot);
            var sustainedOut = $"certification-results/postgres-ha-soak/{runId}";
            var largeOut = $"certification-results/ha-large-job-soak/{runId}";
            var faultOut = $"certification-results/ha-fault-injection/{runId}";

            var plan = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["phase"] = "v0.15.0 Phase 6",
                ["generatedAt"] = DateTime.UtcNow.ToString("o"),
                ["runId"] = runId,
                ["topologyMetadataPath"] = RelativeLabel(metadataPath),
                ["topology"] = metadata["topology"]!.DeepClone(),
                ["diagnostics"] = new JsonObject
                {
                    ["command"] = $"etl-sql admin ha-soak diagnostics --run-root \"{runLabel}\"",
                    ["requiredAfterFailure"] = true,
                    ["expectedArtifacts"] = JsonArrayFrom("diagnostic-summary.json", "postgres-ha-soak.redacted.env", "run-root-inventory.json", "docker-compose-ps.txt", "docker-compose-logs.txt")
                },
                ["operatorRunbook"] = new JsonObject
                {
                    ["command"] = $"etl-sql admin ha-soak runbook --run-root \"{runLabel}\"",
                    ["expectedArtifacts"] = JsonArrayFrom("ha-soak-runbook.json", "ha-soak-runbook.md")
                },
                ["gates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["gateId"] = "sustained-postgres-ha-load",
                        ["state"] = workload == null ? "Missing local workload config" : "Ready",
                        ["input"] = workload,
                        ["expectedOutputDirectory"] = sustainedOut,
                        ["command"] = workload == null ? $"Run etl-sql admin ha-soak workload --run-root \"{runLabel}\" first." : $"node scripts/test-service-capacity.mjs --config \"{workload}\" --out-dir \"{sustainedOut}\"",
                        ["requiredEvidence"] = JsonArrayFrom("capacity-report.json", "capacity-report.md", "postgres-ha-metrics.json", "postgres-ha-metrics.md", "topology-metadata.json", "workload configuration with secrets redacted before check-in")
                    },
                    new JsonObject
                    {
                        ["gateId"] = "concurrent-large-job-soak",
                        ["state"] = "Contract ready",
                        ["input"] = RelativeLabel(largeManifest),
                        ["expectedOutputDirectory"] = largeOut,
                        ["scenarioCount"] = largeCount,
                        ["command"] = $"Run etl-sql admin ha-soak large-job-plan --run-root \"{runLabel}\" --mode CiSmoke before executing the large-job soak runner.",
                        ["requiredEvidence"] = JsonArrayFrom("ha-large-job-soak-plan.json", "ha-large-job-soak-plan.md", "soak-report.json", "soak-report.md", "cleanup-invariant results", "cancellation-phase results")
                    },
                    new JsonObject
                    {
                        ["gateId"] = "fault-injection",
                        ["state"] = "Contract ready",
                        ["input"] = RelativeLabel(faultMatrix),
                        ["expectedOutputDirectory"] = faultOut,
                        ["faultCount"] = faultCount,
                        ["command"] = $"Run etl-sql admin ha-soak fault-plan --run-root \"{runLabel}\" --mode CiSmoke before executing the fault-injection runner.",
                        ["requiredEvidence"] = JsonArrayFrom("ha-fault-injection-plan.json", "ha-fault-injection-plan.md", "fault-report.json", "fault-report.md", "per-fault cleanup invariant results", "redaction proof")
                    }
                },
                ["nonSecret"] = true
            };
            WriteJson(outputPath, plan);
            logger.WriteLine($"HA soak evidence plan written: {outputPath}", ConsoleColor.Green);
            return Task.FromResult(0);
        }

        private static Task<int> LargeJobPlanAsync(CliContext ctx, ILogger logger)
        {
            var runRoot = ResolveExistingDirectory(ctx.HaSoakRunRoot!);
            var metadata = ReadJsonObject(RequireFile(Path.Combine(runRoot, "topology-metadata.json"), "Topology metadata"));
            var manifestPath = ResolveRepoPath("certification-results/ha-large-job-soak-scenarios.json");
            var manifest = ReadJsonObject(manifestPath);
            var outputPath = string.IsNullOrWhiteSpace(ctx.HaSoakOutputPath) ? Path.Combine(runRoot, "ha-large-job-soak-plan.json") : Path.GetFullPath(ctx.HaSoakOutputPath.Trim());
            AssertCanWrite(outputPath, ctx.HaSoakForce, "Large-job soak plan");
            var mode = ctx.HaSoakMode ?? "CiSmoke";
            var duration = mode == "CiSmoke" ? (int)manifest["defaultDuration"]!["ciSmokeMinutes"]! : (int)manifest["defaultDuration"]!["manualCertificationHours"]! * 60;
            var scenarios = new JsonArray();
            foreach (var scenario in manifest["scenarios"]!.AsArray())
            {
                var jobs = scenario!["concurrency"] == null ? 1 : (mode == "CiSmoke" ? (int)scenario["concurrency"]!["ciSmokeJobs"]! : (int)scenario["concurrency"]!["manualCertificationJobs"]!);
                scenarios.Add(new JsonObject
                {
                    ["scenarioId"] = scenario["scenarioId"]!.DeepClone(),
                    ["state"] = "ReadyForRunner",
                    ["sourceState"] = scenario["state"]!.DeepClone(),
                    ["purpose"] = scenario["purpose"]?.DeepClone(),
                    ["concurrentJobs"] = jobs,
                    ["durationMinutes"] = duration,
                    ["workloads"] = scenario["workloads"]?.DeepClone() ?? new JsonArray(),
                    ["cancellationPoint"] = scenario["cancellationPoint"]?.DeepClone(),
                    ["expectedResult"] = scenario["expectedResult"]?.DeepClone(),
                    ["requiredTelemetry"] = scenario["requiredTelemetry"]!.DeepClone(),
                    ["expectedArtifacts"] = JsonArrayFrom($"{scenario["scenarioId"]!.GetValue<string>()}/result.json", $"{scenario["scenarioId"]!.GetValue<string>()}/result.md", $"{scenario["scenarioId"]!.GetValue<string>()}/runner.log")
                });
            }
            var plan = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["phase"] = "v0.15.0 Phase 6",
                ["generatedAt"] = DateTime.UtcNow.ToString("o"),
                ["runId"] = metadata["runId"]!.DeepClone(),
                ["mode"] = mode,
                ["durationMinutes"] = duration,
                ["topologyMetadataPath"] = RelativeLabel(Path.Combine(runRoot, "topology-metadata.json")),
                ["manifestPath"] = RelativeLabel(manifestPath),
                ["expectedOutputDirectory"] = $"certification-results/ha-large-job-soak/{metadata["runId"]!.GetValue<string>()}",
                ["sharedBudgets"] = manifest["sharedBudgets"]!.DeepClone(),
                ["cleanupInvariants"] = manifest["cleanupInvariants"]!.DeepClone(),
                ["scenarios"] = scenarios,
                ["runnerState"] = "PlanOnly",
                ["nonSecret"] = true
            };
            WriteJson(outputPath, plan);
            WriteLargeJobMarkdown(ChangeExtension(outputPath, ".md"), plan);
            logger.WriteLine($"HA large-job soak plan written: {outputPath}", ConsoleColor.Green);
            return Task.FromResult(0);
        }

        private static Task<int> FaultPlanAsync(CliContext ctx, ILogger logger)
        {
            var runRoot = ResolveExistingDirectory(ctx.HaSoakRunRoot!);
            var metadata = ReadJsonObject(RequireFile(Path.Combine(runRoot, "topology-metadata.json"), "Topology metadata"));
            var matrixPath = ResolveRepoPath("certification-results/ha-fault-injection-matrix.json");
            var matrix = ReadJsonObject(matrixPath);
            var outputPath = string.IsNullOrWhiteSpace(ctx.HaSoakOutputPath) ? Path.Combine(runRoot, "ha-fault-injection-plan.json") : Path.GetFullPath(ctx.HaSoakOutputPath.Trim());
            AssertCanWrite(outputPath, ctx.HaSoakForce, "Fault-injection plan");
            var faults = new JsonArray();
            foreach (var fault in matrix["faults"]!.AsArray())
            {
                faults.Add(new JsonObject
                {
                    ["faultId"] = fault!["faultId"]!.DeepClone(),
                    ["state"] = "ReadyForRunner",
                    ["sourceState"] = fault["state"]!.DeepClone(),
                    ["category"] = fault["category"]!.DeepClone(),
                    ["injectionPoint"] = fault["injectionPoint"]!.DeepClone(),
                    ["injectionMethod"] = fault["injectionMethod"]!.DeepClone(),
                    ["expectedResult"] = fault["expectedResult"]!.DeepClone(),
                    ["requiredEvidence"] = fault["requiredEvidence"]!.DeepClone(),
                    ["expectedArtifacts"] = JsonArrayFrom($"{fault["faultId"]!.GetValue<string>()}/fault-result.json", $"{fault["faultId"]!.GetValue<string>()}/fault-result.md", $"{fault["faultId"]!.GetValue<string>()}/runner.log", $"{fault["faultId"]!.GetValue<string>()}/cleanup-invariants.json")
                });
            }
            var categories = faults.Select(f => f!["category"]!.GetValue<string>())
                .GroupBy(x => x)
                .Select(g => new JsonObject { ["category"] = g.Key, ["count"] = g.Count() })
                .Aggregate(new JsonArray(), (a, o) => { a.Add(o); return a; });
            var runLabel = RelativeLabel(runRoot);
            var plan = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["phase"] = "v0.15.0 Phase 6",
                ["generatedAt"] = DateTime.UtcNow.ToString("o"),
                ["runId"] = metadata["runId"]!.DeepClone(),
                ["mode"] = ctx.HaSoakMode ?? "CiSmoke",
                ["topologyMetadataPath"] = RelativeLabel(Path.Combine(runRoot, "topology-metadata.json")),
                ["matrixPath"] = RelativeLabel(matrixPath),
                ["expectedOutputDirectory"] = $"certification-results/ha-fault-injection/{metadata["runId"]!.GetValue<string>()}",
                ["diagnosticsCommand"] = $"etl-sql admin ha-soak diagnostics --run-root \"{runLabel}\"",
                ["runSafety"] = matrix["runSafety"]!.DeepClone(),
                ["commonCleanupInvariants"] = matrix["commonCleanupInvariants"]!.DeepClone(),
                ["categoryCounts"] = categories,
                ["faults"] = faults,
                ["runnerState"] = "PlanOnly",
                ["nonSecret"] = true
            };
            WriteJson(outputPath, plan);
            WriteFaultMarkdown(ChangeExtension(outputPath, ".md"), plan);
            logger.WriteLine($"HA fault-injection plan written: {outputPath}", ConsoleColor.Green);
            return Task.FromResult(0);
        }

        private static Task<int> MetricsAsync(CliContext ctx, ILogger logger)
        {
            var runRoot = ResolveExistingDirectory(ctx.HaSoakRunRoot!);
            var metadata = ReadJsonObject(RequireFile(Path.Combine(runRoot, "topology-metadata.json"), "Topology metadata"));
            var outputPath = string.IsNullOrWhiteSpace(ctx.HaSoakOutputPath) ? Path.Combine(runRoot, "postgres-ha-metrics.json") : Path.GetFullPath(ctx.HaSoakOutputPath.Trim());
            if (ctx.HaSoakValidateOnly)
            {
                logger.WriteLine($"Metrics contract is valid for run {metadata["runId"]?.GetValue<string>()}.", ConsoleColor.Green);
                return Task.FromResult(0);
            }
            AssertCanWrite(outputPath, ctx.HaSoakForce, "Metrics snapshot");
            var metrics = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["phase"] = "v0.15.0 Phase 6",
                ["runId"] = metadata["runId"]!.DeepClone(),
                ["capturedAt"] = DateTime.UtcNow.ToString("o"),
                ["topologyMetadataPath"] = RelativeLabel(Path.Combine(runRoot, "topology-metadata.json")),
                ["note"] = "Native CLI snapshot. Docker collection is best-effort and non-secret.",
                ["dockerPs"] = TryRunDockerText(new[] { "compose", "--env-file", Path.Combine(runRoot, "postgres-ha-soak.env"), "-f", ResolveRepoPath(metadata["composeFile"]!.GetValue<string>()), "ps" }),
                ["nonSecret"] = true
            };
            WriteJson(outputPath, metrics);
            File.WriteAllLines(ChangeExtension(outputPath, ".md"), new[] { "# PostgreSQL HA Metrics Snapshot", "", $"Run id: `{metadata["runId"]!.GetValue<string>()}`", "", "Docker status captured in JSON when Docker was available." });
            logger.WriteLine($"PostgreSQL HA metrics snapshot written: {outputPath}", ConsoleColor.Green);
            return Task.FromResult(0);
        }

        private static Task<int> DiagnosticsAsync(CliContext ctx, ILogger logger)
        {
            var runRoot = ResolveExistingDirectory(ctx.HaSoakRunRoot!);
            var metadataPath = RequireFile(Path.Combine(runRoot, "topology-metadata.json"), "Topology metadata");
            var envFile = RequireFile(Path.Combine(runRoot, "postgres-ha-soak.env"), "PostgreSQL HA soak env file");
            if (ctx.HaSoakLogTail < 1) throw new InvalidOperationException("Log tail must be at least 1.");
            var metadata = ReadJsonObject(metadataPath);
            var outputRoot = string.IsNullOrWhiteSpace(ctx.HaSoakOutputRoot)
                ? Path.Combine(runRoot, "diagnostics", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))
                : Path.GetFullPath(ctx.HaSoakOutputRoot.Trim());
            if (Directory.Exists(outputRoot) && !ctx.HaSoakForce)
                throw new InvalidOperationException($"Diagnostics output already exists: {outputRoot}. Use --force to replace it.");
            Directory.CreateDirectory(outputRoot);

            var redactedEnv = File.ReadAllLines(envFile).Select(RedactEnvLine).ToArray();
            var redactedEnvPath = Path.Combine(outputRoot, "postgres-ha-soak.redacted.env");
            File.WriteAllLines(redactedEnvPath, redactedEnv);
            var metadataCopy = Path.Combine(outputRoot, "topology-metadata.json");
            File.Copy(metadataPath, metadataCopy, overwrite: true);
            var inventoryPath = Path.Combine(outputRoot, "run-root-inventory.json");
            WriteJson(inventoryPath, new JsonObject
            {
                ["runRoot"] = RelativeLabel(runRoot),
                ["dataRoot"] = metadata["dataRoot"]!.DeepClone(),
                ["directories"] = GetDirectoryInventory(runRoot)
            });

            var commandResults = new JsonArray();
            if (ctx.HaSoakNoDocker)
            {
                File.WriteAllText(Path.Combine(outputRoot, "docker-compose-skipped.txt"), "Docker collection skipped by --no-docker.");
            }
            else
            {
                var compose = ResolveRepoPath(metadata["composeFile"]!.GetValue<string>());
                var common = new[] { "compose", "--env-file", envFile, "-f", compose };
                commandResults.Add(RunDiagnosticDocker("compose-ps", Path.Combine(outputRoot, "docker-compose-ps.txt"), common.Concat(new[] { "ps" }).ToArray()));
                commandResults.Add(RunDiagnosticDocker("compose-top", Path.Combine(outputRoot, "docker-compose-top.txt"), common.Concat(new[] { "top" }).ToArray()));
                commandResults.Add(RunDiagnosticDocker("compose-logs", Path.Combine(outputRoot, "docker-compose-logs.txt"), common.Concat(new[] { "logs", "--tail", ctx.HaSoakLogTail.ToString(CultureInfo.InvariantCulture), "--timestamps" }).ToArray()));
            }
            var summaryPath = Path.Combine(outputRoot, "diagnostic-summary.json");
            WriteJson(summaryPath, new JsonObject
            {
                ["schemaVersion"] = 1,
                ["phase"] = "v0.15.0 Phase 6",
                ["runId"] = metadata["runId"]!.DeepClone(),
                ["capturedAt"] = DateTime.UtcNow.ToString("o"),
                ["diagnosticsRoot"] = RelativeLabel(outputRoot),
                ["topologyMetadata"] = RelativeLabel(metadataCopy),
                ["redactedEnvironment"] = RelativeLabel(redactedEnvPath),
                ["runRootInventory"] = RelativeLabel(inventoryPath),
                ["dockerCollection"] = ctx.HaSoakNoDocker ? "Skipped" : "Attempted",
                ["commands"] = commandResults,
                ["nonSecret"] = true
            });
            logger.WriteLine($"HA soak diagnostics written: {outputRoot}", ConsoleColor.Green);
            return Task.FromResult(0);
        }

        private static Task<int> ValidateEvidenceAsync(CliContext ctx, ILogger logger)
        {
            var runRoot = ResolveExistingDirectory(ctx.HaSoakRunRoot!);
            var issues = new List<JsonObject>();
            var checkedArtifacts = new List<string>();
            var metadata = TryReadJsonObject(Path.Combine(runRoot, "topology-metadata.json"), "topology metadata", issues, checkedArtifacts);
            TryReadJsonObject(Path.Combine(runRoot, "ha-soak-evidence-plan.json"), "evidence plan", issues, checkedArtifacts);
            var runId = metadata?["runId"]?.GetValue<string>() ?? Path.GetFileName(runRoot);
            var actualCommit = metadata?["commit"]?.GetValue<string>() ?? "";
            var requiredCommit = string.IsNullOrWhiteSpace(ctx.HaSoakRequiredCommit) ? GetGitCommit() : ctx.HaSoakRequiredCommit.Trim();
            if (string.IsNullOrWhiteSpace(actualCommit))
                AddIssue(issues, "Warning", "missing-commit", "Topology metadata does not include a source commit; regenerate topology metadata before publishing final certification evidence.");
            else if (!string.IsNullOrWhiteSpace(requiredCommit) && actualCommit != requiredCommit)
                AddIssue(issues, "Error", "commit-mismatch", $"Topology metadata commit {actualCommit} does not match required commit {requiredCommit}.");

            if (!ctx.HaSoakAllowDirty && IsGitDirty())
                AddIssue(issues, "Warning", "dirty-worktree", "Worktree is dirty; evidence can still be reviewed but should not be published as final certification evidence.");

            var requiredGate = ctx.HaSoakRequiredGate ?? "Sustained";
            var evidenceRoot = Directory.GetCurrentDirectory();
            if (requiredGate is "Sustained" or "All")
                ValidateSustained(runId, evidenceRoot, issues, checkedArtifacts);
            if (requiredGate is "LargeJob" or "All")
                ValidatePassedReportSet(Path.Combine(evidenceRoot, "certification-results", "ha-large-job-soak", runId), "large-job", "ha-large-job-soak-plan", "soak-report", issues, checkedArtifacts);
            if (requiredGate is "FaultInjection" or "All")
                ValidatePassedReportSet(Path.Combine(evidenceRoot, "certification-results", "ha-fault-injection", runId), "fault-injection", "ha-fault-injection-plan", "fault-report", issues, checkedArtifacts);
            foreach (var artifact in checkedArtifacts)
                CheckRedaction(artifact, issues);

            var status = issues.Any(i => i["level"]?.GetValue<string>() == "Error") ? "Failed" : "Passed";
            var summary = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["phase"] = "v0.15.0 Phase 6",
                ["generatedAt"] = DateTime.UtcNow.ToString("o"),
                ["runId"] = runId,
                ["requiredGate"] = requiredGate,
                ["status"] = status,
                ["checkedArtifactCount"] = checkedArtifacts.Count,
                ["issues"] = new JsonArray(issues.Select(i => i.DeepClone()).ToArray())
            };
            if (!string.IsNullOrWhiteSpace(ctx.HaSoakMarkdownReport))
                WriteValidationMarkdown(ctx.HaSoakMarkdownReport.Trim(), summary);
            logger.WriteLine($"HA soak evidence validation: {status}", status == "Passed" ? ConsoleColor.Green : ConsoleColor.Red);
            foreach (var issue in issues)
                logger.WriteLine($"{issue["level"]}: {issue["message"]}", issue["level"]?.GetValue<string>() == "Error" ? ConsoleColor.Red : ConsoleColor.Yellow);
            return Task.FromResult(status == "Passed" ? 0 : 1);
        }

        private static void ValidateSustained(string runId, string evidenceRoot, List<JsonObject> issues, List<string> checkedArtifacts)
        {
            var dir = Path.Combine(evidenceRoot, "certification-results", "postgres-ha-soak", runId);
            var capacity = TryReadJsonObject(Path.Combine(dir, "capacity-report.json"), "capacity report", issues, checkedArtifacts);
            RequireArtifact(Path.Combine(dir, "capacity-report.md"), "capacity Markdown report", issues, checkedArtifacts);
            TryReadJsonObject(Path.Combine(dir, "postgres-ha-metrics.json"), "PostgreSQL metrics snapshot", issues, checkedArtifacts);
            RequireArtifact(Path.Combine(dir, "postgres-ha-metrics.md"), "PostgreSQL metrics Markdown report", issues, checkedArtifacts);
            if (capacity == null) return;
            foreach (var service in new[] { "portal", "orchestrator" })
            {
                foreach (var step in capacity[service]?.AsArray() ?? new JsonArray())
                {
                    if (step?["passed"]?.GetValue<bool>() != true)
                        AddIssue(issues, "Error", "capacity-breach", $"{service} step at concurrency {step?["concurrency"]} did not pass.");
                    var breaches = step?["breaches"]?.AsArray();
                    if (breaches is { Count: > 0 })
                        AddIssue(issues, "Error", "capacity-breach", $"{service} step at concurrency {step?["concurrency"]} reported breaches.");
                }
            }
        }

        private static void ValidatePassedReportSet(string dir, string kind, string planBase, string reportBase, List<JsonObject> issues, List<string> checkedArtifacts)
        {
            TryReadJsonObject(Path.Combine(dir, $"{planBase}.json"), $"{kind} plan", issues, checkedArtifacts);
            RequireArtifact(Path.Combine(dir, $"{planBase}.md"), $"{kind} plan Markdown", issues, checkedArtifacts);
            var report = TryReadJsonObject(Path.Combine(dir, $"{reportBase}.json"), $"{kind} report", issues, checkedArtifacts);
            RequireArtifact(Path.Combine(dir, $"{reportBase}.md"), $"{kind} Markdown report", issues, checkedArtifacts);
            if (report == null) return;
            if (report["passed"]?.GetValue<bool>() == false)
                AddIssue(issues, "Error", "failed-report", $"{kind} report did not pass.");
            var status = report["status"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(status) && status is not ("Passed" or "Pass" or "Succeeded" or "Success"))
                AddIssue(issues, "Error", "failed-report", $"{kind} report status is {status}.");
        }

        private static JsonObject? TryReadJsonObject(string path, string kind, List<JsonObject> issues, List<string> checkedArtifacts)
        {
            if (!RequireArtifact(path, kind, issues, checkedArtifacts)) return null;
            try { return ReadJsonObject(path); }
            catch (Exception ex)
            {
                AddIssue(issues, "Error", "invalid-json", $"{kind} is not valid JSON: {ex.Message}");
                return null;
            }
        }

        private static bool RequireArtifact(string path, string kind, List<JsonObject> issues, List<string> checkedArtifacts)
        {
            if (!File.Exists(path))
            {
                AddIssue(issues, "Error", "missing-artifact", $"{kind} not found: {path}");
                return false;
            }
            checkedArtifacts.Add(Path.GetFullPath(path));
            return true;
        }

        private static void AddIssue(List<JsonObject> issues, string level, string kind, string message) =>
            issues.Add(new JsonObject { ["level"] = level, ["kind"] = kind, ["message"] = message });

        private static void CheckRedaction(string path, List<JsonObject> issues)
        {
            if (!File.Exists(path)) return;
            var text = File.ReadAllText(path);
            foreach (var pattern in new[]
            {
                "PG_PASSWORD\\s*=\\s*(?!\\*{4,})\\S+",
                "PORTAL_JWT_SECRET\\s*=\\s*(?!\\*{4,})\\S+",
                "PORTAL_DATASET_KEY\\s*=\\s*(?!\\*{4,})\\S+",
                "ORCH_API_KEY\\s*=\\s*(?!\\*{4,})\\S+",
                "\"apiKey\"\\s*:\\s*\"(?!\\*{4,}|CHANGE_ME\")([^\"]+)\"",
                "\"password\"\\s*:\\s*\"(?!\\*{4,}|CHANGE_ME\")([^\"]+)\""
            })
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                {
                    AddIssue(issues, "Error", "secret-leak", $"Potential secret value found in {path}");
                    return;
                }
            }
        }

        private static void WriteValidationMarkdown(string path, JsonObject summary)
        {
            EnsureParent(path);
            var issues = summary["issues"]!.AsArray();
            var lines = new List<string>
            {
                "# HA Soak Evidence Validation", "",
                $"Run id: `{summary["runId"]!.GetValue<string>()}`",
                $"Required gate: `{summary["requiredGate"]!.GetValue<string>()}`",
                $"Status: **{summary["status"]!.GetValue<string>()}**",
                $"Checked artifacts: `{summary["checkedArtifactCount"]!.GetValue<int>()}`",
                "", "## Issues", ""
            };
            if (issues.Count == 0) lines.Add("- None");
            else lines.AddRange(issues.Select(i => $"- **{i!["level"]!.GetValue<string>()} / {i["kind"]!.GetValue<string>()}**: {i["message"]!.GetValue<string>()}"));
            File.WriteAllLines(path, lines);
        }

        private static JsonArray GetDirectoryInventory(string root)
        {
            var result = new JsonArray();
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(p => new FileInfo(p)).ToArray();
                result.Add(new JsonObject
                {
                    ["path"] = RelativeLabel(dir),
                    ["fileCount"] = files.Length,
                    ["totalBytes"] = files.Sum(f => f.Length)
                });
            }
            return result;
        }

        private static JsonObject RunDiagnosticDocker(string name, string outputPath, string[] args)
        {
            var started = DateTime.UtcNow;
            var result = RunProcess("docker", args);
            File.WriteAllText(outputPath, RedactLines(result.Output));
            return new JsonObject
            {
                ["name"] = name,
                ["output"] = RelativeLabel(outputPath),
                ["exitCode"] = result.ExitCode,
                ["startedAt"] = started.ToString("o"),
                ["endedAt"] = DateTime.UtcNow.ToString("o"),
                ["command"] = "docker " + string.Join(' ', args.Select(QuoteIfNeeded))
            };
        }

        private static void RunDocker(ILogger logger, string[] args)
        {
            var result = RunProcess("docker", args);
            if (!string.IsNullOrWhiteSpace(result.Output)) logger.WriteLine(RedactLines(result.Output), ConsoleColor.White);
            if (result.ExitCode != 0) throw new InvalidOperationException($"docker exited with code {result.ExitCode}");
        }

        private static string TryRunDockerText(string[] args)
        {
            try { return RedactLines(RunProcess("docker", args).Output); }
            catch (Exception ex) { return $"Docker collection failed: {ex.Message}"; }
        }

        private static (int ExitCode, string Output) RunProcess(string file, string[] args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = file,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
            foreach (var arg in args) startInfo.ArgumentList.Add(arg);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{file} did not start.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output + error);
        }

        private static void AssertTopologyTemplate(string composePath, string examplePath)
        {
            var compose = File.ReadAllText(RequireFile(composePath, "Compose file"));
            foreach (var token in new[] { "postgres:", "orchestrator:", "portal:", "loadbalancer:", "Portal__Database__Provider=Postgres", "Orchestrator__Database__Provider=Postgres", "Portal__Storage__KeyRingPath=/app/data/.portal-keys", "Portal__Dataset__AtRestKey=${PORTAL_DATASET_KEY}", "Portal__Orchestrator__ApiKey=${ORCH_API_KEY}" })
                if (!compose.Contains(token, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Compose file is missing required PostgreSQL HA soak token: {token}");
            var example = File.ReadAllText(RequireFile(examplePath, "Environment example"));
            foreach (var token in new[] { "COMPOSE_PROJECT_NAME=", "ENV_DATA_ROOT=", "PG_PASSWORD=", "PG_DB_PORTAL=", "PG_DB_ORCH=", "PORTAL_JWT_SECRET=", "PORTAL_DATASET_KEY=", "ORCH_API_KEY=" })
                if (!example.Contains(token, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Environment example is missing required PostgreSQL HA soak token: {token}");
        }

        private static string ResolveRepoPath(string value)
        {
            var trimmed = value.Trim('"', '\'', ' ');
            if (Path.IsPathRooted(trimmed)) return Path.GetFullPath(trimmed);
            foreach (var root in CandidateRoots())
            {
                var candidate = Path.Combine(root, trimmed);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), trimmed));
        }

        private static IEnumerable<string> CandidateRoots()
        {
            for (var current = new DirectoryInfo(Directory.GetCurrentDirectory()); current != null; current = current.Parent)
                yield return current.FullName;
            for (var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); current != null; current = current.Parent)
                yield return current.FullName;
        }

        private static string ResolveExistingDirectory(string value)
        {
            var path = ResolveRepoPath(value);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Directory not found: {path}");
            return path;
        }

        private static string RequireFile(string path, string name)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"{name} not found: {path}", path);
            return path;
        }

        private static JsonObject ReadJsonObject(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        private static void WriteJson(string path, JsonNode node) { EnsureParent(path); File.WriteAllText(path, node.ToJsonString(JsonOptions)); }
        private static string ChangeExtension(string path, string extension) => Path.ChangeExtension(path, extension);
        private static void EnsureParent(string path) { var parent = Path.GetDirectoryName(path); if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent); }
        private static void AssertCanWrite(string path, bool force, string label) { if (File.Exists(path) && !force) throw new InvalidOperationException($"{label} already exists: {path}. Use --force to replace it."); EnsureParent(path); }
        private static void AssertPositive(int value, string name) { if (value < 1) throw new InvalidOperationException($"{name} must be at least 1."); }
        private static string ToEnvPath(string value) => value.Replace('\\', '/');
        private static string NewBase64Secret(int bytes) { var buffer = new byte[bytes]; RandomNumberGenerator.Fill(buffer); return Convert.ToBase64String(buffer); }
        private static JsonArray JsonArrayFrom(params string[] values) { var a = new JsonArray(); foreach (var value in values) a.Add(value); return a; }
        private static string RedactEnvLine(string line) => SecretEnvPattern.IsMatch(line) ? line.Split('=', 2)[0] + "=********" : line;
        private static string RedactLines(string text) => string.Join(Environment.NewLine, text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Select(RedactEnvLine));
        private static string QuoteIfNeeded(string value) => value.Contains(' ') ? "\"" + value + "\"" : value;

        private static string RelativeLabel(string path)
        {
            try { return Path.GetRelativePath(Directory.GetCurrentDirectory(), Path.GetFullPath(path)).Replace('\\', '/').TrimStart('.', '/', '\\'); }
            catch { return path.Replace('\\', '/'); }
        }

        private static Dictionary<string, string> ReadEnv(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
                var index = trimmed.IndexOf('=');
                if (index <= 0) continue;
                result[trimmed[..index]] = trimmed[(index + 1)..];
            }
            return result;
        }

        private static void RequireEnv(Dictionary<string, string> env, params string[] keys)
        {
            foreach (var key in keys)
                if (!env.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"Generated topology env is missing {key}.");
        }

        private static string GetGitCommit()
        {
            try
            {
                var result = RunProcess("git", new[] { "-C", ResolveRepoPath("."), "rev-parse", "HEAD" });
                return result.ExitCode == 0 ? result.Output.Trim() : "";
            }
            catch { return ""; }
        }

        private static bool IsGitDirty()
        {
            try
            {
                var result = RunProcess("git", new[] { "-C", ResolveRepoPath("."), "status", "--porcelain" });
                return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output);
            }
            catch { return false; }
        }

        private static JsonObject Step(int order, string name, string command, params string[] artifacts) =>
            new()
            {
                ["order"] = order,
                ["name"] = name,
                ["command"] = command,
                ["expectedArtifacts"] = JsonArrayFrom(artifacts)
            };

        private static void WriteRunbookMarkdown(string path, JsonObject runbook)
        {
            var lines = new List<string> { "# PostgreSQL HA Soak Operator Runbook", "", $"Run id: `{runbook["runId"]!.GetValue<string>()}`", $"Mode: `{runbook["mode"]!.GetValue<string>()}`", "", "Run the commands below from the repository root. Generated env files contain secrets; do not commit them.", "", "| Step | Purpose | Command | Expected artifacts |", "| ---: | :--- | :--- | :--- |" };
            foreach (var step in runbook["steps"]!.AsArray())
                lines.Add($"| {step!["order"]} | {step["name"]} | `{step["command"]!.GetValue<string>().Replace("|", "\\|")}` | {string.Join("<br>", step["expectedArtifacts"]!.AsArray().Select(a => a!.GetValue<string>()))} |");
            File.WriteAllLines(path, lines);
        }

        private static void WriteLargeJobMarkdown(string path, JsonObject plan)
        {
            var lines = new List<string> { "# HA Large-Job Soak Plan", "", $"Run id: `{plan["runId"]!.GetValue<string>()}`", $"Mode: `{plan["mode"]!.GetValue<string>()}`", $"Duration minutes: `{plan["durationMinutes"]!.GetValue<int>()}`", "", "| Scenario | State | Jobs | Cancellation point | Required telemetry count |", "| :--- | :--- | ---: | :--- | ---: |" };
            foreach (var scenario in plan["scenarios"]!.AsArray())
                lines.Add($"| {scenario!["scenarioId"]} | {scenario["state"]} | {scenario["concurrentJobs"]} | {scenario["cancellationPoint"]} | {scenario["requiredTelemetry"]!.AsArray().Count} |");
            File.WriteAllLines(path, lines);
        }

        private static void WriteFaultMarkdown(string path, JsonObject plan)
        {
            var lines = new List<string> { "# HA Fault-Injection Plan", "", $"Run id: `{plan["runId"]!.GetValue<string>()}`", $"Mode: `{plan["mode"]!.GetValue<string>()}`", $"Fault count: `{plan["faults"]!.AsArray().Count}`", "", "| Fault | Category | Injection point | State | Required evidence count |", "| :--- | :--- | :--- | :--- | ---: |" };
            foreach (var fault in plan["faults"]!.AsArray())
                lines.Add($"| {fault!["faultId"]} | {fault["category"]} | {fault["injectionPoint"]} | {fault["state"]} | {fault["requiredEvidence"]!.AsArray().Count} |");
            File.WriteAllLines(path, lines);
        }
    }
}
