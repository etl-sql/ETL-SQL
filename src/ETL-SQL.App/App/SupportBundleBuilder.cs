using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
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

        private static readonly Regex UrlQueryValuePattern = new(
            @"([?&][^=\s&#?]+)=([^&\s#]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EmailPattern = new(
            @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IpAddressPattern = new(
            @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b",
            RegexOptions.Compiled);

        private static readonly Regex WindowsPathPattern = new(
            @"\b[A-Z]:\\[^\s""'<>|]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex UserPathPattern = new(
            @"/(?:Users|home)/[^\s""'<>|]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TableLikeLinePattern = new(
            @"^\s*(?:[|│].*[|│]|[^,\r\n]+,[^,\r\n]+,[^,\r\n]+).*$",
            RegexOptions.Compiled);

        private const string RedactedMarker = "***REDACTED***";
        private const string RedactedValueMarker = "***REDACTED_VALUE***";
        private const string RedactedPathMarker = "***REDACTED_PATH***";
        private const string RedactedTableRowMarker = "***REDACTED_TABLE_ROW***";

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
                await WriteEnterpriseDiagnosticsAsync(staging);
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
                logger.WriteLine($"Failed to build support bundle: {RedactDiagnosticText(ex.Message)}", ConsoleColor.Red);
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
                ["machineName"] = RedactedValueMarker,
                ["baseDirectory"] = RedactDiagnosticText(AppDomain.CurrentDomain.BaseDirectory),
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

            await File.WriteAllTextAsync(Path.Combine(staging, "doctor-health.json"), RedactDiagnosticText(captured.ToString()));
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
                redacted = $"{{ \"note\": \"appsettings.json could not be parsed for redaction: {RedactDiagnosticText(ex.Message)}\" }}";
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
                var entry = new JsonObject { ["name"] = label, ["path"] = RedactDiagnosticText(path) };
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
                catch (Exception ex) { entry["error"] = RedactDiagnosticText(ex.Message); }
                files.Add(entry);
            }

            Add("Portal Database", config?["Portal:DatabasePath"]);
            Add("Orchestrator History DB", config?["Orchestrator:HistoryDbPath"]);

            var metrics = new JsonObject { ["databaseFiles"] = files };
            await File.WriteAllTextAsync(
                Path.Combine(staging, "database-metrics.json"),
                metrics.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static async Task WriteEnterpriseDiagnosticsAsync(string staging)
        {
            var diagnostics = BuildEnterpriseDiagnostics();
            await File.WriteAllTextAsync(
                Path.Combine(staging, "enterprise-diagnostics.json"),
                diagnostics.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        internal static JsonObject BuildEnterpriseDiagnostics(
            EnterpriseEnrollmentStore? store = null,
            EffectiveEnterprisePolicy? policy = null)
        {
            store ??= new EnterpriseEnrollmentStore();
            policy ??= EnterprisePolicyRuntime.Current;

            var status = store.GetStatus();
            var root = new JsonObject
            {
                ["generatedUtc"] = DateTime.UtcNow.ToString("o"),
                ["enrollment"] = BuildEnrollmentDiagnostics(status),
                ["currentPolicy"] = BuildPolicyDiagnostics(policy),
                ["securityEvents"] = JsonSerializer.SerializeToNode(
                    SecurityEventRuntime.GetDiagnostics(),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!,
                ["localFiles"] = BuildEnterpriseFileDiagnostics(store.Path)
            };
            return root;
        }

        private static JsonObject BuildEnrollmentDiagnostics(EnterpriseEnrollmentStatus status)
        {
            var result = new JsonObject
            {
                ["isEnrolled"] = status.IsEnrolled,
                ["bootstrapPath"] = RedactDiagnosticText(status.Path)
            };

            if (status.Error is not null)
            {
                result["valid"] = false;
                result["error"] = RedactDiagnosticText(status.Error);
                return result;
            }

            if (status.Enrollment is not { } value)
            {
                result["valid"] = !status.IsEnrolled;
                return result;
            }

            result["valid"] = true;
            result["schemaVersion"] = value.SchemaVersion;
            result["enrollmentId"] = value.EnrollmentId;
            result["machineId"] = value.MachineId;
            result["tenantHash"] = Hash(value.Tenant);
            result["policyEndpoint"] = BuildEndpointDiagnostics(value.PolicyEndpoint);
            result["policySigningKeyConfigured"] = true;
            result["policySigningKeyHash"] = Hash(value.PolicySigningPublicKey);
            result["clientCertificateConfigured"] = !string.IsNullOrWhiteSpace(value.ClientCertificateThumbprint);
            result["clientCertificateThumbprintHash"] = HashNullable(value.ClientCertificateThumbprint);
            result["serviceIdentityConfigured"] = !string.IsNullOrWhiteSpace(value.ServiceIdentity);
            result["maxOfflineHours"] = value.MaxOfflineHours;
            result["failClosed"] = value.FailClosed;
            result["enrolledAtUtc"] = value.EnrolledAtUtc.ToString("o");
            return result;
        }

        private static JsonObject BuildPolicyDiagnostics(EffectiveEnterprisePolicy policy)
        {
            var result = new JsonObject
            {
                ["isEnrolled"] = policy.IsEnrolled,
                ["isAvailable"] = policy.IsAvailable,
                ["status"] = policy.Status,
                ["policyVersion"] = policy.PolicyVersion,
                ["policyHash"] = ComputePolicyHash(policy.Document),
                ["source"] = policy.Source,
                ["issuedAtUtc"] = policy.IssuedAtUtc?.ToString("o"),
                ["expiresAtUtc"] = policy.ExpiresAtUtc?.ToString("o"),
                ["loadedAtUtc"] = policy.LoadedAtUtc?.ToString("o"),
                ["warning"] = RedactDiagnosticText(policy.Error)
            };

            var governedKeys = new JsonArray();
            foreach (var key in policy.ConfigurationValues.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
                governedKeys.Add(key);
            result["governedKeys"] = governedKeys;
            return result;
        }

        private static JsonObject BuildEndpointDiagnostics(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return new JsonObject
                {
                    ["configured"] = true,
                    ["validAbsoluteUri"] = false
                };
            }

            return new JsonObject
            {
                ["configured"] = true,
                ["validAbsoluteUri"] = true,
                ["scheme"] = uri.Scheme,
                ["hostHash"] = Hash(uri.IdnHost),
                ["port"] = uri.IsDefaultPort ? null : uri.Port
            };
        }

        private static JsonObject BuildEnterpriseFileDiagnostics(string enrollmentPath)
        {
            var directory = Path.GetDirectoryName(enrollmentPath);
            var cacheDirectory = directory is null ? null : Path.Combine(directory, "cache");
            return new JsonObject
            {
                ["enrollment"] = BuildFileMetric(enrollmentPath),
                ["policyCache"] = cacheDirectory is null ? null : BuildFileMetric(Path.Combine(cacheDirectory, "policy-cache.json")),
                ["securityEventOutbox"] = cacheDirectory is null ? null : BuildFileMetric(Path.Combine(cacheDirectory, "security-events.db"))
            };
        }

        private static JsonObject BuildFileMetric(string path)
        {
            var result = new JsonObject { ["path"] = RedactDiagnosticText(path) };
            try
            {
                var info = new FileInfo(path);
                result["exists"] = info.Exists;
                if (info.Exists)
                {
                    result["sizeBytes"] = info.Length;
                    result["lastWriteUtc"] = info.LastWriteTimeUtc.ToString("o");
                }
            }
            catch (Exception ex)
            {
                result["error"] = RedactDiagnosticText(ex.Message);
            }
            return result;
        }

        private static string? ComputePolicyHash(OrganizationPolicyDocument? document) =>
            document is null ? null : Hash(JsonSerializer.Serialize(document));

        private static string? HashNullable(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : Hash(value);

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

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
                        try
                        {
                            var text = File.ReadAllText(file.FullName);
                            File.WriteAllText(Path.Combine(dest, file.Name), RedactDiagnosticText(text));
                        }
                        catch { /* skip locked/unreadable log files */ }
                    }
                }
                catch { /* a missing or inaccessible log dir is non-fatal for the bundle */ }
            }
        }

        /// <summary>
        /// Redacts diagnostic text before it enters a support bundle. This intentionally masks more
        /// than secret keys: URLs, local paths, host/user identifiers, emails, IPs, and table-shaped
        /// rows can all carry private operational data.
        /// </summary>
        internal static string RedactDiagnosticText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var redacted = SecretRedactor.Redact(text) ?? string.Empty;
            redacted = UrlQueryValuePattern.Replace(redacted, m => $"{m.Groups[1].Value}={RedactedValueMarker}");
            redacted = EmailPattern.Replace(redacted, RedactedValueMarker);
            redacted = IpAddressPattern.Replace(redacted, RedactedValueMarker);
            redacted = WindowsPathPattern.Replace(redacted, RedactedPathMarker);
            redacted = UserPathPattern.Replace(redacted, RedactedPathMarker);

            var lines = redacted.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (LooksLikePrivateTableRow(lines[i]))
                    lines[i] = RedactedTableRowMarker;
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static bool LooksLikePrivateTableRow(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                || line.Contains(" at ", StringComparison.Ordinal)
                || line.Contains("=>", StringComparison.Ordinal))
            {
                return false;
            }

            return TableLikeLinePattern.IsMatch(line);
        }
    }
}
