using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;
using ETL_SQL.Orchestrator;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Data;
using ETL_SQL.App;
using System.Collections.Generic;

namespace ETL_SQL.Tests.Integration
{
    public class AdHocAuditingTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly string _tempScriptPath;

        public AdHocAuditingTests()
        {
            _testDbPath = $"test_audit_{Guid.NewGuid():N}.db";
            _tempScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"temp_script_{Guid.NewGuid():N}.etlsql");
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath))
                    File.Delete(_testDbPath);
            }
            catch { }

            try
            {
                if (File.Exists(_tempScriptPath))
                    File.Delete(_tempScriptPath);
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
    }
}
