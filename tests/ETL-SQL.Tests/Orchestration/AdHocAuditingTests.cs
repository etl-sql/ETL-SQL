using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Orchestrator;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class AdHocAuditingTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly string _tempScriptPath;
        private readonly string _workspacePath;
        private readonly IServiceProvider? _originalServiceProvider;

        public AdHocAuditingTests()
        {
            _testDbPath = $"test_audit_{Guid.NewGuid():N}.db";
            _workspacePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AuditWorkspaces", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspacePath);
            _tempScriptPath = Path.Combine(_workspacePath, "pipeline.etlsql");
            _originalServiceProvider = ETL_SQL.Program.ServiceProvider;
        }

        public void Dispose()
        {
            ETL_SQL.Program.ServiceProvider = _originalServiceProvider;

            try
            {
                if (File.Exists(_testDbPath))
                    File.Delete(_testDbPath);
            }
            catch { }

            try
            {
                if (Directory.Exists(_workspacePath))
                    Directory.Delete(_workspacePath, recursive: true);
            }
            catch { }
        }

        private IServiceProvider CreateTestServiceProvider(bool auditAdHoc, bool lineageEnabled = true)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var configOverrides = new Dictionary<string, string?>
            {
                ["Engine:AuditAdHocRuns"] = auditAdHoc ? "true" : "false",
                ["Engine:LineageEnabled"] = lineageEnabled ? "true" : "false"
            };

            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddInMemoryCollection(configOverrides);
            var configuration = builder.Build();

            services.AddSingleton<IConfiguration>(configuration);

            var loggerService = new ETL_SQL.Common.LoggerService();
            loggerService.InitializeAppLogger("logs/test_app", 1, 10);
            services.AddSingleton<ETL_SQL.Common.LoggerService>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILoggerService>(loggerService);

            services.AddEtlSqlEngine(configuration);

            // Override SQLiteJobHistoryStore to use our test db path
            var store = new SQLiteJobHistoryStore(_testDbPath);

            var toRemove = services.Where(d =>
                d.ServiceType == typeof(SQLiteJobHistoryStore) ||
                d.ServiceType == typeof(IJobHistoryStore) ||
                d.ServiceType == typeof(ILineageCatalogStore) ||
                d.ServiceType == typeof(IBundleStore)
            ).ToList();
            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<SQLiteJobHistoryStore>(store);
            services.AddSingleton<IJobHistoryStore>(store);
            services.AddSingleton<ILineageCatalogStore>(store);
            services.AddSingleton<IBundleStore>(store);

            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task TestAuditAdHocRuns_Enabled_LogsCompletedJob()
        {
            // Arrange
            var provider = CreateTestServiceProvider(auditAdHoc: true);
            Program.ServiceProvider = provider;

            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();

            await File.WriteAllTextAsync(_tempScriptPath, "PRINT 'Hello Audit';");
            var ctx = new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true
            };

            var jobName = Path.GetFileName(_tempScriptPath);

            // Act
            var exitCode = await EngineRunner.Run(ctx);

            // Assert
            Assert.Equal(0, exitCode);
            var history = (await store.GetHistoryAsync(jobName)).ToList();
            Assert.Single(history);
            var entry = history.First();
            Assert.Equal("COMPLETED", entry.Status);
            Assert.NotNull(entry.ScriptHashAtRunTime);
            Assert.StartsWith("sha256:", entry.ScriptHashAtRunTime);
        }

        [Fact]
        public async Task TestAuditAdHocRuns_Disabled_DoesNotLogJob()
        {
            // Arrange
            var provider = CreateTestServiceProvider(auditAdHoc: false);
            Program.ServiceProvider = provider;

            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();

            await File.WriteAllTextAsync(_tempScriptPath, "PRINT 'No Audit';");
            var ctx = new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true
            };

            var jobName = Path.GetFileName(_tempScriptPath);

            // Act
            var exitCode = await EngineRunner.Run(ctx);

            // Assert
            Assert.Equal(0, exitCode);
            var history = (await store.GetHistoryAsync(jobName)).ToList();
            Assert.Empty(history);
        }

        [Fact]
        public async Task TestAuditAdHocRuns_Enabled_LogsFailedJob()
        {
            // Arrange
            var provider = CreateTestServiceProvider(auditAdHoc: true);
            Program.ServiceProvider = provider;

            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();

            await File.WriteAllTextAsync(_tempScriptPath, "THROW 50001, 'Test bad audit error', 1;");
            var ctx = new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true
            };

            var jobName = Path.GetFileName(_tempScriptPath);

            // Act
            var exitCode = await EngineRunner.Run(ctx);

            // Assert
            Assert.Equal(1, exitCode);
            var history = (await store.GetHistoryAsync(jobName)).ToList();
            Assert.Single(history);
            var entry = history.First();
            Assert.Equal("FAILED", entry.Status);
            Assert.Contains("Test bad audit error", entry.ErrorMessage);
        }

        [Fact]
        public async Task TestAuditAdHocRuns_LineagePersisted()
        {
            // Arrange
            var provider = CreateTestServiceProvider(auditAdHoc: true, lineageEnabled: true);
            Program.ServiceProvider = provider;

            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();

            // Script that generates lineage
            var scriptContent =
                "SELECT 1 AS amount INTO #orders; " +
                "CREATE DATASET &daily_sales AS (SELECT amount FROM #orders);";
            await File.WriteAllTextAsync(_tempScriptPath, scriptContent);

            var ctx = new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true
            };

            var jobName = Path.GetFileName(_tempScriptPath);

            // Act
            var exitCode = await EngineRunner.Run(ctx);

            // Assert
            Assert.Equal(0, exitCode);

            // Verify job execution was logged
            var history = (await store.GetHistoryAsync(jobName)).ToList();
            Assert.Single(history);

            // Verify lineage was saved to the store
            var lineageStore = provider.GetRequiredService<ILineageCatalogStore>();
            var lineageHistory = (await lineageStore.GetHistoryForJobAsync(jobName)).ToList();
            Assert.NotEmpty(lineageHistory);
            Assert.Contains(lineageHistory, e =>
                e.TargetTable.Equals("dataset:&daily_sales", StringComparison.OrdinalIgnoreCase) &&
                e.Operation.Equals("CREATE DATASET", StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// One install serves both interactive development and a scheduled task. Turning on the
        /// machine-wide setting so the 02:00 job is recorded must not start recording every
        /// exploratory run the same operator makes.
        /// </summary>
        [Fact]
        public async Task NoRecordSuppressesRecordingEvenWhenTheSettingIsOn()
        {
            var provider = CreateTestServiceProvider(auditAdHoc: true);
            Program.ServiceProvider = provider;

            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();
            await File.WriteAllTextAsync(_tempScriptPath, "PRINT 'exploratory';");

            var exitCode = await EngineRunner.Run(new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true,
                RecordRun = false
            });

            Assert.Equal(0, exitCode);
            Assert.Empty(await store.GetHistoryAsync(Path.GetFileName(_tempScriptPath)));
        }

        /// <summary>The mirror case: record a scheduled run without turning recording on machine-wide.</summary>
        [Fact]
        public async Task RecordForcesRecordingEvenWhenTheSettingIsOff()
        {
            var provider = CreateTestServiceProvider(auditAdHoc: false);
            Program.ServiceProvider = provider;

            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();
            await File.WriteAllTextAsync(_tempScriptPath, "PRINT 'scheduled';");

            var exitCode = await EngineRunner.Run(new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true,
                RecordRun = true
            });

            Assert.Equal(0, exitCode);
            Assert.Single(await store.GetHistoryAsync(Path.GetFileName(_tempScriptPath)));
        }

        [Fact]
        public async Task AbsentFlagLeavesTheConfiguredSettingInCharge()
        {
            var provider = CreateTestServiceProvider(auditAdHoc: true);
            Program.ServiceProvider = provider;

            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();
            await File.WriteAllTextAsync(_tempScriptPath, "PRINT 'default';");

            var exitCode = await EngineRunner.Run(new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true,
                RecordRun = null
            });

            Assert.Equal(0, exitCode);
            Assert.Single(await store.GetHistoryAsync(Path.GetFileName(_tempScriptPath)));
        }

        /// <summary>
        /// Two schedules of the same script otherwise collapse into one history identity, which is
        /// exactly what the triage inbox needs to tell apart.
        /// </summary>
        [Fact]
        public async Task JobNameGivesAnUnattendedRunItsOwnIdentity()
        {
            var provider = CreateTestServiceProvider(auditAdHoc: true);
            Program.ServiceProvider = provider;

            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();
            await File.WriteAllTextAsync(_tempScriptPath, "PRINT 'nightly';");

            var exitCode = await EngineRunner.Run(new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true,
                JobName = "nightly-load-eu"
            });

            Assert.Equal(0, exitCode);
            Assert.Single(await store.GetHistoryAsync("nightly-load-eu"));
            Assert.Empty(await store.GetHistoryAsync(Path.GetFileName(_tempScriptPath)));
        }

        /// <summary>
        /// The run's lineage and its history entry must file under the same name, or the catalog and
        /// the inbox disagree about what ran.
        /// </summary>
        [Fact]
        public async Task LineageIsFiledUnderTheSameJobNameAsTheHistoryEntry()
        {
            var provider = CreateTestServiceProvider(auditAdHoc: true, lineageEnabled: true);
            Program.ServiceProvider = provider;

            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();
            await File.WriteAllTextAsync(_tempScriptPath,
                "SELECT 1 AS amount INTO #orders; CREATE DATASET &daily_sales AS (SELECT amount FROM #orders);");

            var exitCode = await EngineRunner.Run(new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true,
                JobName = "nightly-load-eu"
            });

            Assert.Equal(0, exitCode);
            Assert.Single(await store.GetHistoryAsync("nightly-load-eu"));

            var lineageStore = provider.GetRequiredService<ILineageCatalogStore>();
            Assert.NotEmpty(await lineageStore.GetHistoryForJobAsync("nightly-load-eu"));
            Assert.Empty(await lineageStore.GetHistoryForJobAsync(Path.GetFileName(_tempScriptPath)));
        }

        [Fact]
        public async Task WorkstationAutomation_MissingRequiredMetadataReturnsNonZero()
        {
            var provider = CreateTestServiceProvider(auditAdHoc: false);
            Program.ServiceProvider = provider;
            await WritePolicyAsync();
            await File.WriteAllTextAsync(_tempScriptPath, "SELECT 1 AS CustomerId INTO #customers;");

            var exitCode = await EngineRunner.Run(new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true
            });

            Assert.Equal(1, exitCode);
        }

        [Fact]
        public async Task WorkstationAutomation_ExpectThrowFailureReturnsNonZero()
        {
            var provider = CreateTestServiceProvider(auditAdHoc: false);
            Program.ServiceProvider = provider;
            await WritePolicyAsync();
            await File.WriteAllTextAsync(_tempScriptPath, """
                CREATE TABLE #source (
                  CustomerId INT /* @owner: 'sales'; @steward: 'data-office' */
                );
                INSERT INTO #source (CustomerId) VALUES (NULL);
                SELECT CustomerId EXPECT NOT NULL ON FAILURE THROW /* @owner: 'sales'; @steward: 'data-office'; */
                INTO #clean FROM #source;
                """);

            var exitCode = await EngineRunner.Run(new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(_tempScriptPath),
                IsSilentMode = true
            });

            Assert.Equal(1, exitCode);
        }

        private Task WritePolicyAsync() => File.WriteAllTextAsync(
            Path.Combine(_workspacePath, "etlsql-policy.json"),
            """
            {
              "schemaVersion": "1.0",
              "requiredTags": [
                { "tag": "@owner", "scopes": ["COLUMN"] },
                { "tag": "@steward", "scopes": ["COLUMN"] }
              ]
            }
            """);
    }
}
