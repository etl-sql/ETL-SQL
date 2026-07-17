using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
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
            SecurityEventRuntime.ConfigureLocalOutboxFactory(new SqliteSecurityEventOutboxFactory());
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

        [Fact]
        public void SupportBundle_DiagnosticRedaction_StripsQueryParamsPathsAndPersonalData()
        {
            var text = """
            GET https://portal.local/report?id=42&customer=Acme
            C:\Users\alice\Documents\private.etlsql
            /home/bob/data/customer.csv
            owner alice@example.com from 192.168.1.50
            """;

            var redacted = SupportBundleBuilder.RedactDiagnosticText(text);

            Assert.DoesNotContain("42", redacted);
            Assert.DoesNotContain("Acme", redacted);
            Assert.DoesNotContain("alice", redacted);
            Assert.DoesNotContain("bob", redacted);
            Assert.DoesNotContain("192.168.1.50", redacted);
            Assert.Contains("***REDACTED_PATH***", redacted);
            Assert.Contains("***REDACTED_VALUE***", redacted);
        }

        [Fact]
        public void SupportBundle_DiagnosticRedaction_StripsPrivateTableRows()
        {
            var text = """
            Id,Name,Email
            1,Alice,alice@example.com
            | Account | Balance | Owner |
            | 123 | 1000 | Bob |
            System.InvalidOperationException: keep stack trace context
            """;

            var redacted = SupportBundleBuilder.RedactDiagnosticText(text);

            Assert.DoesNotContain("Alice", redacted);
            Assert.DoesNotContain("Balance", redacted);
            Assert.Contains("***REDACTED_TABLE_ROW***", redacted);
            Assert.Contains("System.InvalidOperationException", redacted);
        }

        [Fact]
        public void SupportBundle_EnterpriseDiagnostics_ExposeMetadataWithoutTrustMaterialOrPayload()
        {
            const string tenant = "corp-production";
            const string thumbprint = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
            const string serviceIdentity = "DOMAIN\\etl-service";
            using var key = RSA.Create(2048);
            var publicKey = key.ExportSubjectPublicKeyInfoPem();
            var enrollmentPath = Path.Combine(_baseDir, "Enterprise", "enrollment.json");
            var store = new EnterpriseEnrollmentStore(
                enrollmentPath,
                new NoopEnrollmentValidator(),
                new NoopEnrollmentProtector());
            var document = new EnterpriseEnrollmentDocument
            {
                Tenant = tenant,
                PolicyEndpoint = "https://policy.example.com/api/policy-authority",
                PolicySigningPublicKey = publicKey,
                ClientCertificateThumbprint = thumbprint,
                ServiceIdentity = serviceIdentity
            };
            store.Enroll(document);

            var policyDocument = new OrganizationPolicyDocument
            {
                Execution = new ExecutionPolicySection { MaxStringResultSize = 4096 },
                SecurityEvents = new SecurityEventPolicySection
                {
                    CollectorEndpoint = "https://siem.example.com/events"
                }
            };
            var policy = new EffectiveEnterprisePolicy(
                true,
                true,
                "Live",
                "2026.07.13.1",
                "unit-test",
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddHours(1),
                DateTimeOffset.UtcNow,
                policyDocument,
                EnterprisePolicyConfiguration.Flatten(policyDocument.ToPolicyValues()));

            var diagnostics = SupportBundleBuilder.BuildEnterpriseDiagnostics(store, policy);
            var json = diagnostics.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            Assert.DoesNotContain(tenant, json);
            Assert.DoesNotContain("policy.example.com", json);
            Assert.DoesNotContain(publicKey, json);
            Assert.DoesNotContain(thumbprint, json);
            Assert.DoesNotContain(serviceIdentity, json);
            Assert.DoesNotContain("siem.example.com", json);
            Assert.Contains("tenantHash", json);
            Assert.Contains("policySigningKeyHash", json);
            Assert.Contains("clientCertificateThumbprintHash", json);
            Assert.Contains("policyHash", json);
            Assert.Equal("1.0", (string?)diagnostics["enrollment"]!["schemaVersion"]);
            Assert.Equal("2026.07.13.1", (string?)diagnostics["currentPolicy"]!["policyVersion"]);
            Assert.Contains("SecurityEvents:CollectorEndpoint",
                diagnostics["currentPolicy"]!["governedKeys"]!.AsArray().Select(x => (string?)x));
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

        private sealed class NoopEnrollmentValidator : IEnterpriseEnrollmentProtectionValidator
        {
            public void Validate(string enrollmentPath)
            {
            }
        }

        private sealed class NoopEnrollmentProtector : IEnterpriseEnrollmentProtector
        {
            public void ProtectDirectory(string directory, string? serviceIdentity)
            {
            }

            public void ProtectCacheDirectory(string directory, string? serviceIdentity)
            {
            }

            public void ProtectFile(string file, string? serviceIdentity)
            {
            }
        }
    }
}
