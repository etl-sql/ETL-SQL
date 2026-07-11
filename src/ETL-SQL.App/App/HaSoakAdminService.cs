using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.App
{
    internal static class HaSoakAdminService
    {
        private static readonly Dictionary<string, string> ScriptByCommand = new(StringComparer.Ordinal)
        {
            ["admin-ha-soak-prepare"] = "New-PostgresHaSoakTopology.ps1",
            ["admin-ha-soak-workload"] = "New-PostgresHaCapacityWorkload.ps1",
            ["admin-ha-soak-runbook"] = "New-HaSoakRunbook.ps1",
            ["admin-ha-soak-evidence"] = "New-HaSoakEvidencePlan.ps1",
            ["admin-ha-soak-large-job-plan"] = "New-HaLargeJobSoakPlan.ps1",
            ["admin-ha-soak-fault-plan"] = "New-HaFaultInjectionPlan.ps1",
            ["admin-ha-soak-metrics"] = "Export-PostgresHaMetricsSnapshot.ps1",
            ["admin-ha-soak-validate"] = "Test-HaSoakEvidence.ps1",
            ["admin-ha-soak-diagnostics"] = "Export-HaSoakDiagnostics.ps1",
        };

        internal static async Task<int> RunAsync(CliContext ctx, ILogger logger)
        {
            if (!ScriptByCommand.TryGetValue(ctx.Command, out var scriptName))
            {
                logger.WriteLine($"Unknown HA soak admin command: {ctx.Command}", ConsoleColor.Red);
                return 1;
            }

            if (RequiresRunRoot(ctx.Command) && string.IsNullOrWhiteSpace(ctx.HaSoakRunRoot))
            {
                logger.WriteLine("--run-root is required for this HA soak command.", ConsoleColor.Red);
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(ctx.HaSoakMode)
                && ctx.HaSoakMode is not ("CiSmoke" or "ManualCertification"))
            {
                logger.WriteLine("--mode must be CiSmoke or ManualCertification.", ConsoleColor.Red);
                return 1;
            }

            var scriptPath = FindScript(scriptName);
            if (scriptPath == null)
            {
                logger.WriteLine(
                    $"Could not find scripts/{scriptName}. Run this command from a source checkout or install the HA soak scripts next to the executable.",
                    ConsoleColor.Red);
                return 1;
            }

            var shell = ResolvePowerShellExecutable();
            var args = BuildScriptArguments(ctx);
            logger.WriteLine($"Running HA soak script: {Path.GetFileName(scriptPath)}", ConsoleColor.Cyan);
            logger.WriteLine($"Script path: {scriptPath}", ConsoleColor.Gray);

            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            foreach (var arg in args)
                startInfo.ArgumentList.Add(arg);

            try
            {
                using var process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        logger.WriteLine(e.Data, ConsoleColor.White);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        logger.WriteLine(e.Data, ConsoleColor.Yellow);
                };

                if (!process.Start())
                {
                    logger.WriteLine("PowerShell process did not start.", ConsoleColor.Red);
                    return 1;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                    logger.WriteLine("HA soak command completed.", ConsoleColor.Green);
                else
                    logger.WriteLine($"HA soak command failed with exit code {process.ExitCode}.", ConsoleColor.Red);
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Failed to run HA soak script: {ex.Message}", ConsoleColor.Red);
                return 1;
            }
        }

        private static bool RequiresRunRoot(string command) => command != "admin-ha-soak-prepare";

        private static string ResolvePowerShellExecutable()
        {
            var configured = Environment.GetEnvironmentVariable("ETL_SQL_POWERSHELL");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : "pwsh";
        }

        private static string? FindScript(string scriptName)
        {
            foreach (var root in CandidateRoots())
            {
                var candidate = Path.Combine(root, "scripts", scriptName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            return null;
        }

        private static IEnumerable<string> CandidateRoots()
        {
            var current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current != null)
            {
                yield return current.FullName;
                current = current.Parent;
            }

            var baseDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (baseDir != null)
            {
                yield return baseDir.FullName;
                baseDir = baseDir.Parent;
            }
        }

        private static List<string> BuildScriptArguments(CliContext ctx)
        {
            var args = new List<string>();

            switch (ctx.Command)
            {
                case "admin-ha-soak-prepare":
                    AddValue(args, "-RunId", ctx.HaSoakRunId);
                    AddValue(args, "-OutputRoot", ctx.HaSoakOutputRoot);
                    AddValue(args, "-ComposeFile", ctx.HaSoakComposeFile);
                    AddValue(args, "-EnvExample", ctx.HaSoakEnvExample);
                    AddValue(args, "-PortalScale", ctx.HaSoakPortalScale);
                    AddValue(args, "-OrchestratorScale", ctx.HaSoakOrchestratorScale);
                    AddValue(args, "-PortalPort", ctx.HaSoakPortalPort);
                    AddValue(args, "-OrchestratorPort", ctx.HaSoakOrchestratorPort);
                    AddValue(args, "-PostgresPort", ctx.HaSoakPostgresPort);
                    AddValue(args, "-ImageTag", ctx.HaSoakImageTag);
                    AddSwitch(args, "-ValidateOnly", ctx.HaSoakValidateOnly);
                    AddSwitch(args, "-Start", ctx.HaSoakStart);
                    AddSwitch(args, "-Pull", ctx.HaSoakPull);
                    AddSwitch(args, "-Force", ctx.HaSoakForce);
                    break;
                case "admin-ha-soak-workload":
                    AddValue(args, "-TopologyRunRoot", ctx.HaSoakRunRoot);
                    AddValue(args, "-OutputPath", ctx.HaSoakOutputPath);
                    AddValue(args, "-AdminPassword", ctx.HaSoakAdminPassword);
                    AddSwitch(args, "-Force", ctx.HaSoakForce);
                    break;
                case "admin-ha-soak-runbook":
                    AddValue(args, "-TopologyRunRoot", ctx.HaSoakRunRoot);
                    AddValue(args, "-SustainedWorkloadPath", ctx.HaSoakSustainedWorkloadPath);
                    AddValue(args, "-Mode", ctx.HaSoakMode);
                    AddValue(args, "-OutputPath", ctx.HaSoakOutputPath);
                    AddSwitch(args, "-Force", ctx.HaSoakForce);
                    break;
                case "admin-ha-soak-evidence":
                    AddValue(args, "-TopologyRunRoot", ctx.HaSoakRunRoot);
                    AddValue(args, "-SustainedWorkloadPath", ctx.HaSoakSustainedWorkloadPath);
                    AddValue(args, "-OutputPath", ctx.HaSoakOutputPath);
                    AddSwitch(args, "-Force", ctx.HaSoakForce);
                    break;
                case "admin-ha-soak-large-job-plan":
                    AddValue(args, "-TopologyRunRoot", ctx.HaSoakRunRoot);
                    AddValue(args, "-Mode", ctx.HaSoakMode);
                    AddValue(args, "-OutputPath", ctx.HaSoakOutputPath);
                    AddSwitch(args, "-Force", ctx.HaSoakForce);
                    break;
                case "admin-ha-soak-fault-plan":
                    AddValue(args, "-TopologyRunRoot", ctx.HaSoakRunRoot);
                    AddValue(args, "-Mode", ctx.HaSoakMode);
                    AddValue(args, "-OutputPath", ctx.HaSoakOutputPath);
                    AddSwitch(args, "-Force", ctx.HaSoakForce);
                    break;
                case "admin-ha-soak-metrics":
                    AddValue(args, "-TopologyRunRoot", ctx.HaSoakRunRoot);
                    AddValue(args, "-OutputPath", ctx.HaSoakOutputPath);
                    AddSwitch(args, "-ValidateOnly", ctx.HaSoakValidateOnly);
                    AddSwitch(args, "-Force", ctx.HaSoakForce);
                    break;
                case "admin-ha-soak-validate":
                    AddValue(args, "-TopologyRunRoot", ctx.HaSoakRunRoot);
                    AddValue(args, "-RequiredGate", ctx.HaSoakRequiredGate);
                    AddValue(args, "-RequiredCommit", ctx.HaSoakRequiredCommit);
                    AddValue(args, "-MarkdownReport", ctx.HaSoakMarkdownReport);
                    AddSwitch(args, "-AllowDirty", ctx.HaSoakAllowDirty);
                    break;
                case "admin-ha-soak-diagnostics":
                    AddValue(args, "-TopologyRunRoot", ctx.HaSoakRunRoot);
                    AddValue(args, "-OutputRoot", ctx.HaSoakOutputRoot);
                    AddValue(args, "-LogTail", ctx.HaSoakLogTail);
                    AddSwitch(args, "-NoDocker", ctx.HaSoakNoDocker);
                    AddSwitch(args, "-Force", ctx.HaSoakForce);
                    break;
            }

            return args;
        }

        private static void AddValue(List<string> args, string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            args.Add(name);
            args.Add(value.Trim('"', '\'', ' '));
        }

        private static void AddValue(List<string> args, string name, int value)
        {
            args.Add(name);
            args.Add(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void AddSwitch(List<string> args, string name, bool enabled)
        {
            if (enabled)
                args.Add(name);
        }
    }
}
