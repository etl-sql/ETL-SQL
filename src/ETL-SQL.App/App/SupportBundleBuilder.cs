using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.App
{
    /// <summary>
    /// Builds a redacted support archive an administrator can hand to support: system/runtime
    /// configuration, the doctor health snapshot, recent logs, and database file metrics. Every
    /// credential is redacted before anything is written to the archive.
    /// </summary>
    internal static class SupportBundleBuilder
    {
        // Key names whose values are treated as secrets and masked. "version"/"note" suffixes are
        // excluded so non-secret metadata (e.g. AtRestKeyVersion) stays visible for diagnostics.
        private static readonly Regex SecretKeyPattern = new(
            "(password|passwd|pwd|secret|apikey|api_key|token|accountkey|sharedaccesskey|privatekey|clientsecret|connectionstring|atrestkey)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Suffixes that demote a secret-looking key back to non-secret metadata.
        private static readonly Regex SecretKeyExemptPattern = new(
            "(version|note|expiry|expires|enabled|count|limit|window|days|minutes|seconds|policy|provider|path|name|mode)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Embedded credentials inside connection-string-like values (e.g. "...;Password=hunter2;...").
        private static readonly Regex EmbeddedSecretPattern = new(
            "((?:password|pwd|secret|accountkey|sharedaccesskey)\\s*=)([^;]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string RedactedMarker = "***REDACTED***";

        /// <summary>
        /// True when a configuration key names a secret value (and is not an exempt metadata suffix
        /// such as <c>AtRestKeyVersion</c>). Shared so the support-bundle redactor and the backup
        /// config-secret splitter agree on what counts as a secret.
        /// </summary>
        internal static bool IsSecretKey(string key) =>
            SecretKeyPattern.IsMatch(key) && !SecretKeyExemptPattern.IsMatch(key);

        internal static async Task<int> RunAsync(CliContext ctx, ILogger logger)
        {
            var outputPath = ResolveOutputPath(ctx.BundleOutput);
            var staging = Path.Combine(Path.GetTempPath(), $"etl-sql-support-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);

            try
            {
                var config = Program.ServiceProvider.GetService<IConfiguration>();

                await WriteManifestAsync(staging);
                await WriteDoctorSnapshotAsync(staging, logger);
                await WriteRedactedConfigAsync(staging);
                await WriteDatabaseMetricsAsync(staging, config);
                CopyRecentLogs(staging, config);

                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                ZipFile.CreateFromDirectory(staging, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                logger.WriteLine($"Support bundle written to: {Path.GetFullPath(outputPath)}", ConsoleColor.Green);
                logger.WriteLine("All credentials were redacted. Review the archive before sharing.", ConsoleColor.Gray);
                return 0;
            }
            catch (Exception ex)
            {
                logger.WriteLine($"Failed to build support bundle: {ex.Message}", ConsoleColor.Red);
                return 1;
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }

        private static string ResolveOutputPath(string? requested)
        {
            if (!string.IsNullOrWhiteSpace(requested))
                return requested.Trim('"', '\'', ' ');
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(Directory.GetCurrentDirectory(), $"etl-sql-support-{stamp}.zip");
        }

        private static async Task WriteManifestAsync(string staging)
        {
            var manifest = new JsonObject
            {
                ["generatedUtc"] = DateTime.UtcNow.ToString("o"),
                ["tool"] = "etl-sql admin support-bundle",
                ["toolVersion"] = typeof(SupportBundleBuilder).Assembly.GetName().Version?.ToString() ?? "unknown",
                ["operatingSystem"] = Environment.OSVersion.ToString(),
                ["dotnetRuntime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["machineName"] = Environment.MachineName,
                ["baseDirectory"] = AppDomain.CurrentDomain.BaseDirectory,
            };
            await File.WriteAllTextAsync(
                Path.Combine(staging, "manifest.json"),
                manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static async Task WriteDoctorSnapshotAsync(string staging, ILogger logger)
        {
            var doctorCtx = new CliContext { Command = "doctor", IsJsonMode = true, DoctorProfile = "full" };
            var originalOut = Console.Out;
            using var captured = new StringWriter();
            try
            {
                Console.SetOut(captured);
                await EngineRunner.RunDoctor(doctorCtx, logger);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            await File.WriteAllTextAsync(Path.Combine(staging, "doctor-health.json"), captured.ToString());
        }

        private static async Task WriteRedactedConfigAsync(string staging)
        {
            var appSettings = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (!File.Exists(appSettings))
            {
                await File.WriteAllTextAsync(
                    Path.Combine(staging, "config-redacted.json"),
                    "{ \"note\": \"appsettings.json was not found next to the executable.\" }");
                return;
            }

            string redacted;
            try
            {
                var root = JsonNode.Parse(await File.ReadAllTextAsync(appSettings));
                if (root != null) Redact(root);
                redacted = root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
            }
            catch (Exception ex)
            {
                redacted = $"{{ \"note\": \"appsettings.json could not be parsed for redaction: {ex.Message}\" }}";
            }

            await File.WriteAllTextAsync(Path.Combine(staging, "config-redacted.json"), redacted);
        }

        /// <summary>
        /// Test seam: parses <paramref name="json"/>, redacts it, and returns the redacted JSON text.
        /// </summary>
        internal static string RedactConfigJson(string json)
        {
            var root = JsonNode.Parse(json);
            if (root != null) Redact(root);
            return root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        }

        /// <summary>Recursively masks secret-bearing values in a parsed JSON tree, in place.</summary>
        /// <param name="forceMaskStrings">
        /// When true (inside a secret-keyed container such as <c>PreviousSecrets</c> or
        /// <c>ConnectionStrings</c>), every string leaf is masked entirely rather than only its
        /// embedded credential portion.
        /// </param>
        private static void Redact(JsonNode node, bool forceMaskStrings = false)
        {
            switch (node)
            {
                case JsonObject obj:
                    // Snapshot keys to avoid mutating while enumerating.
                    foreach (var key in obj.Select(kvp => kvp.Key).ToList())
                    {
                        var child = obj[key];
                        bool isSecretKey = SecretKeyPattern.IsMatch(key) && !SecretKeyExemptPattern.IsMatch(key);

                        if (child is JsonValue val)
                        {
                            if (val.TryGetValue<string>(out var s))
                            {
                                if (string.IsNullOrEmpty(s))
                                    continue; // empty string carries no secret; keep it visible
                                obj[key] = (isSecretKey || forceMaskStrings) ? RedactedMarker : RedactEmbedded(s);
                            }
                            // Non-string scalars (numbers/bools) are config knobs, never secrets — leave them.
                        }
                        else if (child != null)
                        {
                            // Recurse; a secret-keyed container masks all string leaves beneath it.
                            Redact(child, forceMaskStrings || isSecretKey);
                        }
                    }
                    break;
                case JsonArray arr:
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var item = arr[i];
                        if (item is JsonValue val && val.TryGetValue<string>(out var s))
                        {
                            if (string.IsNullOrEmpty(s)) continue;
                            arr[i] = forceMaskStrings ? RedactedMarker : RedactEmbedded(s);
                        }
                        else if (item != null)
                        {
                            Redact(item, forceMaskStrings);
                        }
                    }
                    break;
            }
        }

        private static string RedactEmbedded(string value) =>
            EmbeddedSecretPattern.Replace(value, m => m.Groups[1].Value + RedactedMarker);

        private static async Task WriteDatabaseMetricsAsync(string staging, IConfiguration? config)
        {
            var files = new JsonArray();
            void Add(string label, string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                var entry = new JsonObject { ["name"] = label, ["path"] = path };
                try
                {
                    var fi = new FileInfo(path);
                    entry["exists"] = fi.Exists;
                    if (fi.Exists)
                    {
                        entry["sizeBytes"] = fi.Length;
                        entry["lastWriteUtc"] = fi.LastWriteTimeUtc.ToString("o");
                    }
                }
                catch (Exception ex) { entry["error"] = ex.Message; }
                files.Add(entry);
            }

            Add("Portal Database", config?["Portal:DatabasePath"]);
            Add("Orchestrator History DB", config?["Orchestrator:HistoryDbPath"]);

            var metrics = new JsonObject { ["databaseFiles"] = files };
            await File.WriteAllTextAsync(
                Path.Combine(staging, "database-metrics.json"),
                metrics.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void CopyRecentLogs(string staging, IConfiguration? config)
        {
            const int maxFilesPerDir = 10;
            var logsDir = Path.Combine(staging, "logs");

            var sources = new[]
            {
                ("app", config?["Logging:AppLog:Directory"] ?? "logs/app"),
                ("scripts", config?["Logging:ScriptLog:Directory"] ?? "logs/scripts"),
            };

            foreach (var (label, dir) in sources)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
                    var recent = new DirectoryInfo(dir)
                        .GetFiles("*", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .Take(maxFilesPerDir)
                        .ToList();
                    if (recent.Count == 0) continue;

                    var dest = Path.Combine(logsDir, label);
                    Directory.CreateDirectory(dest);
                    foreach (var file in recent)
                    {
                        try { file.CopyTo(Path.Combine(dest, file.Name), overwrite: true); }
                        catch { /* skip locked/unreadable log files */ }
                    }
                }
                catch { /* a missing or inaccessible log dir is non-fatal for the bundle */ }
            }
        }
    }
}
