using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.CliCommands
{
    /// <summary>
    /// Phase 1 (System Diagnostics) coverage for the Operator Tooling release:
    /// support-bundle credential redaction and `init` scaffolding behavior.
    /// </summary>
    public class OperatorToolingTests : IDisposable
    {
        private readonly string _baseDir;

        public OperatorToolingTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "etlsql_operator_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, true); } catch { }
        }

        // ── support-bundle redaction ──────────────────────────────────────────

        [Fact]
        public void SupportBundle_Redaction_MasksSecretsButKeepsConfigKnobs()
        {
            const string secret = "SuperSecretValue-DO-NOT-LEAK";
            var json = $$"""
            {
              "Portal": {
                "Jwt": { "Secret": "{{secret}}", "PreviousSecrets": [ "{{secret}}-old" ], "ExpiryMinutes": 60 },
                "Dataset": {
                  "AtRestKey": "{{secret}}-atrest",
                  "AtRestKeyVersion": "v1",
                  "PreviousAtRestKeys": { "v0": "{{secret}}-prev" },
                  "AllowPlaintextSecrets": false
                },
                "RateLimit": { "AnonymousTokenPermitLimit": 60 }
              },
              "Orchestrator": { "ApiKey": "{{secret}}-api" },
              "ConnectionStrings": { "Default": "Server=db;User Id=sa;Password={{secret}}-conn;" }
            }
            """;

            var redacted = SupportBundleBuilder.RedactConfigJson(json);

            // No form of the seeded secret may survive anywhere in the output.
            Assert.DoesNotContain(secret, redacted);

            var root = JsonNode.Parse(redacted)!.AsObject();
            // Secrets masked
            Assert.Equal("***REDACTED***", (string?)root["Portal"]!["Jwt"]!["Secret"]);
            Assert.Equal("***REDACTED***", (string?)root["Portal"]!["Jwt"]!["PreviousSecrets"]!.AsArray()[0]);
            Assert.Equal("***REDACTED***", (string?)root["Portal"]!["Dataset"]!["AtRestKey"]);
            Assert.Equal("***REDACTED***", (string?)root["Portal"]!["Dataset"]!["PreviousAtRestKeys"]!["v0"]);
            Assert.Equal("***REDACTED***", (string?)root["Orchestrator"]!["ApiKey"]);
            Assert.Equal("***REDACTED***", (string?)root["ConnectionStrings"]!["Default"]);

            // Non-secret config knobs remain visible for diagnostics.
            Assert.Equal("v1", (string?)root["Portal"]!["Dataset"]!["AtRestKeyVersion"]);
            Assert.False((bool)root["Portal"]!["Dataset"]!["AllowPlaintextSecrets"]!);
            Assert.Equal(60, (int)root["Portal"]!["Jwt"]!["ExpiryMinutes"]!);
            Assert.Equal(60, (int)root["Portal"]!["RateLimit"]!["AnonymousTokenPermitLimit"]!);
        }

        [Fact]
        public void SupportBundle_Redaction_KeepsEmptySecretStringsAndMasksEmbeddedCredentials()
        {
            var json = """
            { "A": { "Password": "" }, "B": { "Url": "https://h/x?pwd=topsecret&page=1" } }
            """;

            var redacted = SupportBundleBuilder.RedactConfigJson(json);
            var root = JsonNode.Parse(redacted)!.AsObject();

            // An empty secret is harmless and kept as-is.
            Assert.Equal("", (string?)root["A"]!["Password"]);
            // An embedded credential inside a non-secret-keyed value is still masked.
            Assert.DoesNotContain("topsecret", redacted);
            Assert.Contains("pwd=***REDACTED***", (string?)root["B"]!["Url"]);
        }

        // ── init scaffolding ──────────────────────────────────────────────────

        [Fact]
        public async Task Init_CreatesStarterFilesWithGeneratedJwtSecret()
        {
            var ctx = new CliContext { Command = "init", InitDirectory = _baseDir };

            var exit = await InitScaffolder.RunAsync(ctx, NullLogger.Instance);

            Assert.Equal(0, exit);
            var configPath = Path.Combine(_baseDir, "appsettings.json");
            var scriptPath = Path.Combine(_baseDir, "hello.etlsql");
            Assert.True(File.Exists(configPath));
            Assert.True(File.Exists(scriptPath));

            var config = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
            var jwt = (string?)config["Portal"]!["Jwt"]!["Secret"];
            Assert.False(string.IsNullOrWhiteSpace(jwt));
        }

        [Fact]
        public async Task Init_IsIdempotent_DoesNotOverwriteWithoutForce()
        {
            var ctx = new CliContext { Command = "init", InitDirectory = _baseDir };
            await InitScaffolder.RunAsync(ctx, NullLogger.Instance);

            var configPath = Path.Combine(_baseDir, "appsettings.json");
            var firstContent = File.ReadAllText(configPath);

            // Second run without --force must leave existing files untouched.
            var exit = await InitScaffolder.RunAsync(ctx, NullLogger.Instance);

            Assert.Equal(0, exit);
            Assert.Equal(firstContent, File.ReadAllText(configPath));
        }

        [Fact]
        public async Task Init_Force_RegeneratesFiles()
        {
            var ctx = new CliContext { Command = "init", InitDirectory = _baseDir };
            await InitScaffolder.RunAsync(ctx, NullLogger.Instance);
            var configPath = Path.Combine(_baseDir, "appsettings.json");
            var firstSecret = (string?)JsonNode.Parse(File.ReadAllText(configPath))!["Portal"]!["Jwt"]!["Secret"];

            var forceCtx = new CliContext { Command = "init", InitDirectory = _baseDir, InitForce = true };
            await InitScaffolder.RunAsync(forceCtx, NullLogger.Instance);
            var secondSecret = (string?)JsonNode.Parse(File.ReadAllText(configPath))!["Portal"]!["Jwt"]!["Secret"];

            // A regenerated config gets a fresh JWT secret.
            Assert.NotEqual(firstSecret, secondSecret);
        }
    }
}
