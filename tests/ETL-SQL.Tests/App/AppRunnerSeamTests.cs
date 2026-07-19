using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.CliCommands
{
    public sealed class AppRunnerSeamTests : IDisposable
    {
        private readonly string _root;

        public AppRunnerSeamTests()
        {
            SecurityEventRuntime.ConfigureLocalOutboxFactory(new SqliteSecurityEventOutboxFactory());
            _root = Path.Combine(Path.GetTempPath(), "etlsql_app_runner_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Fact]
        public async Task EnterpriseEnroll_UsesInjectedStoreAndElevationCheck()
        {
            var store = Store("enrollment.json");
            var logger = new CapturingLogger();
            var keyPath = Path.Combine(_root, "policy-signing.pem");
            using var key = RSA.Create(2048);
            await File.WriteAllTextAsync(keyPath, key.ExportSubjectPublicKeyInfoPem());

            var exit = await EnterpriseEnrollmentManager.RunAsync(
                new CliContext
                {
                    Command = "enterprise-enroll",
                    EnterpriseTenant = "prod",
                    EnterprisePolicyEndpoint = "https://policy.example.com",
                    EnterpriseSigningKeyPath = keyPath,
                    EnterpriseClientCertificateThumbprint = "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
                    EnterpriseServiceIdentity = "svc-etl",
                    EnterpriseMaxOfflineHours = 12
                },
                logger,
                store,
                requireElevation: () => { });

            Assert.Equal(0, exit);
            var status = store.GetStatus();
            Assert.True(status.IsEnrolled);
            Assert.Equal("prod", status.Enrollment!.Tenant);
            Assert.Contains("Machine enrolled in enterprise policy", logger.Text);
        }

        [Fact]
        public async Task EnterpriseStatus_ReturnsTwoWhenPolicyCannotBeLoaded()
        {
            var store = Store("status-enrollment.json");
            using var key = RSA.Create(2048);
            store.Enroll(new EnterpriseEnrollmentDocument
            {
                Tenant = "prod",
                PolicyEndpoint = "https://policy.example.com",
                PolicySigningPublicKey = key.ExportSubjectPublicKeyInfoPem()
            });

            var logger = new CapturingLogger();
            var exit = await EnterpriseEnrollmentManager.RunAsync(
                new CliContext { Command = "enterprise-status" },
                logger,
                store,
                initializePolicy: _ => throw new InvalidOperationException("policy unavailable"));

            Assert.Equal(2, exit);
            Assert.Contains("Enterprise enrollment: active", logger.Text);
            Assert.Contains("policy unavailable", logger.Text);
        }

        [Fact]
        public async Task EnterpriseUnenroll_RequiresConfirmation()
        {
            var logger = new CapturingLogger();
            var exit = await EnterpriseEnrollmentManager.RunAsync(
                new CliContext { Command = "enterprise-unenroll" },
                logger,
                Store("unenroll.json"),
                requireElevation: () => { });

            Assert.Equal(1, exit);
            Assert.Contains("Unenrollment requires --yes", logger.Text);
        }

        [Fact]
        public async Task DatabaseMigrationRun_RejectsUnsupportedDirectionBeforeConfigAccess()
        {
            var logger = new CapturingLogger();
            var config = new ConfigurationBuilder().Build();

            var exit = await DatabaseMigrationService.RunAsync(
                new CliContext { MigrateFrom = "postgres", MigrateTo = "sqlite" },
                logger,
                config,
                _root);

            Assert.Equal(1, exit);
            Assert.Contains("Unsupported migration direction", logger.Text);
        }

        [Fact]
        public async Task DatabaseMigrationRun_FailsClosedWhenPostgresTargetMissing()
        {
            var portalDb = Path.Combine(_root, "portal.db");
            await using (var conn = new SqliteConnection($"Data Source={portalDb}"))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE Widgets (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL); INSERT INTO Widgets VALUES (1, 'a');";
                await cmd.ExecuteNonQueryAsync();
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Portal:DatabasePath"] = portalDb,
                    ["Orchestrator:HistoryDbPath"] = Path.Combine(_root, "missing-orch.db")
                })
                .Build();
            var logger = new CapturingLogger();

            var exit = await DatabaseMigrationService.RunAsync(
                new CliContext { MigrateDryRun = true },
                logger,
                config,
                _root);

            Assert.Equal(1, exit);
            Assert.Contains("target PostgreSQL ConnectionString is not configured", logger.Text);
        }

        [Fact]
        public async Task WarmJobRunner_ReturnsRedactedMissingScriptError()
        {
            var services = new ServiceCollection().BuildServiceProvider();
            var missing = Path.Combine(_root, "secret-token-123.etlsql");

            var ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                WarmJobRunner.ExecuteRequestAsync(
                    services,
                    new WarmRunnerRequest("req-1", missing, null, null, 0, 0)));

            Assert.Equal(missing, ex.FileName);
            Assert.Contains("Runner script file not found", ex.Message);
        }

        private EnterpriseEnrollmentStore Store(string name) => new(
            Path.Combine(_root, name),
            new NoopEnrollmentValidator(),
            new NoopEnrollmentProtector());

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<string> _messages = [];
            public string Text => string.Join(Environment.NewLine, _messages);
            public string? SessionId { get; set; }
            public bool IsDebugEnabled => true;
            public bool IsVerboseEnabled => true;
            public bool IsVerbose { get; set; }
            public bool SuppressConsole { get; set; }
            public bool IsJsonMode { get; set; }
            public event Action<string, string?, ConsoleColor>? OnMessage;

            public void Log(LogLevel level, string message, Exception? ex = null)
            {
                _messages.Add(message);
                if (ex is not null)
                    _messages.Add(ex.Message);
                OnMessage?.Invoke(message, null, ConsoleColor.White);
            }
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
