using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Engine.Spill;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class SpillEncryptionTests
    {
        private IServiceProvider CreateServiceProvider(string sessionRoot)
        {
            var services = new ServiceCollection();

            var config = new ConfigurationBuilder().Build();
            services.AddSingleton<IConfiguration>(config);

            var logger = new Mock<ILogger>();
            services.AddSingleton<ILogger>(logger.Object);

            var security = new SecurityService(logger.Object);
            security.IsTestMode = true;
            services.AddSingleton<SecurityService>(security);

            var registry = new ETL_SQL.Engine.Functions.FunctionRegistry();
            ETL_SQL.Engine.Functions.StandardFunctions.Register(registry);
            services.AddSingleton<global::ETL_SQL.Core.Functions.IFunctionRegistry>(registry);

            services.AddSingleton<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager>(new Mock<IDockerManager>().Object);

            services.AddSingleton<ISessionStateManager>(sp =>
            {
                return new SessionStateManager(logger.Object, security, config, new SqliteSessionMetadataStoreFactory(), sessionRoot);
            });
            services.AddSingleton<SessionStateManager>(sp => (SessionStateManager)sp.GetRequiredService<ISessionStateManager>());

            services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
            services.AddTransient<ETL_SQL.Engine.Services.ReportRegistry>();
            services.AddTransient<IReportContext, ETL_SQL.Engine.Services.ReportRegistry>();
            services.AddSingleton<IJobHistoryStore>(new Mock<IJobHistoryStore>().Object);
            services.AddSingleton<IHostMetricsStore>(new Mock<IHostMetricsStore>().Object);
            var lineageCatalog = new Mock<ILineageCatalogStore>();
            lineageCatalog
                .Setup(s => s.GetHistoryForTableAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(Enumerable.Empty<LineageHistoryEntry>());
            lineageCatalog
                .Setup(s => s.GetHistoryForTagAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()))
                .ReturnsAsync(Enumerable.Empty<LineageHistoryEntry>());
            lineageCatalog
                .Setup(s => s.GetHistoryForJobAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(Enumerable.Empty<LineageHistoryEntry>());
            lineageCatalog
                .Setup(s => s.GetHistoryForSourceAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(Enumerable.Empty<LineageHistoryEntry>());
            lineageCatalog
                .Setup(s => s.GetHistoryForSourceFileAsync(It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync(Enumerable.Empty<LineageHistoryEntry>());
            services.AddSingleton(lineageCatalog.Object);
            services.AddSingleton<IBundleStore>(new Mock<IBundleStore>().Object);
            services.AddSingleton<ETL_SQL.Engine.Services.EvaluatorComponentRegistry>();
            services.AddSingleton<ETL_SQL.Core.Interfaces.ILanguageHelpRegistry, ETL_SQL.Core.Metadata.LanguageHelpRegistry>();

            services.AddSingleton<ISystemResources, DefaultSystemResources>();
            var bufferOptions = Options.Create(new BufferManagerOptions());
            var bufferLogger = new Mock<Microsoft.Extensions.Logging.ILogger<BufferManager>>();
            services.AddSingleton<IBufferManager>(sp => new BufferManager(bufferOptions, bufferLogger.Object, sp.GetRequiredService<ISystemResources>()));

            // Register Handlers
            var handlerAssembly = typeof(DeclareStatementHandler).Assembly;
            var handlerTypes = handlerAssembly.GetTypes()
                .Where(t => typeof(IStatementHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
            foreach (var type in handlerTypes)
            {
                services.AddTransient(typeof(IStatementHandler), type);
                services.AddTransient(type);
            }

            services.AddTransient<Evaluator>(sp => ActivatorUtilities.CreateInstance<Evaluator>(sp));
            services.AddTransient<IExecutionContext>(sp => sp.GetRequiredService<Evaluator>());

            return services.BuildServiceProvider();
        }

        [Fact]
        public void GetSpillKey_ShouldBeDeterministic()
        {
            var logger = new Mock<ILogger>();
            var security = new SecurityService(logger.Object);
            var config = new ConfigurationBuilder().Build();
            var manager = new SessionStateManager(logger.Object, security, config, new SqliteSessionMetadataStoreFactory());

            var key1 = manager.GetSpillKey("session1");
            var key2 = manager.GetSpillKey("session1");
            var key3 = manager.GetSpillKey("session2");

            Assert.Equal(key1, key2);
            Assert.NotEqual(key1, key3);
            Assert.Equal(32, key1.Length); // SHA256
        }

        [Fact]
        public void ClearSession_RejectsSessionIdPathTraversal()
        {
            var sessionRoot = Path.Combine(Path.GetTempPath(), "ETL-SQL-SessionRoot", Guid.NewGuid().ToString("N"));
            var sibling = sessionRoot + "-sibling";
            Directory.CreateDirectory(sessionRoot);
            Directory.CreateDirectory(sibling);
            File.WriteAllText(Path.Combine(sibling, "metadata.db"), "");

            try
            {
                var logger = new Mock<ILogger>();
                var security = new SecurityService(logger.Object) { IsTestMode = true };
                var config = new ConfigurationBuilder().Build();
                var manager = new SessionStateManager(logger.Object, security, config, new SqliteSessionMetadataStoreFactory(), sessionRoot);
                var maliciousId = Path.Combine("..", Path.GetFileName(sibling));

                Assert.Throws<ExecutionException>(() => manager.ClearSession(maliciousId));
                Assert.True(Directory.Exists(sibling));
            }
            finally
            {
                try { if (Directory.Exists(sessionRoot)) Directory.Delete(sessionRoot, true); } catch (IOException) { }
                try { if (Directory.Exists(sibling)) Directory.Delete(sibling, true); } catch (IOException) { }
            }
        }

        [Fact]
        public async Task SpillEncryption_ShouldPersistAcrossSessions()
        {
            string sessionRoot = Path.Combine(Path.GetTempPath(), "ETL-SQL-SpillTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionRoot);

            try
            {
                var sp = CreateServiceProvider(sessionRoot);
                var manager = sp.GetRequiredService<SessionStateManager>();
                var security = sp.GetRequiredService<SecurityService>();
                security.IsTestMode = true; // Crucial for tests

                var evaluator = sp.GetRequiredService<Evaluator>();
                string sessionId = evaluator.SessionId;
                // SessionRoot is the root that *contains* session directories, not one session's
                // own directory — SessionStateManager and SqliteSessionMetadataStore both append the
                // session id to it, and every production caller passes a parent
                // (EngineRunner uses sessionManager.SessionRoot; the Orchestrator and DashboardService
                // use storageAuthority.CheckpointRoot). Passing an already-scoped path here made the
                // save land at <root>/<id>/<id> while the load looked at <root>/<id>, so LoadSession
                // returned null and this test had been failing on the release branch ever since.
                evaluator.SessionRoot = sessionRoot;
                evaluator.IsPersistentSession = true;
                evaluator.TempTableSpillThresholdRows = 1;

                // 1. Create data and force spill
                Console.WriteLine("DEBUG: Create table");
                await evaluator.EvaluateStatement(new Parser(new Lexer("CREATE TABLE #test (id INT, name TEXT)").Tokenize(), "").ParseStatement());

                Console.WriteLine("DEBUG: Insert row 1");
                await evaluator.EvaluateStatement(new Parser(new Lexer("INSERT INTO #test (id, name) VALUES (1, 'item 1')").Tokenize(), "").ParseStatement());

                Console.WriteLine("DEBUG: Insert row 2 (triggers spill)");
                await evaluator.EvaluateStatement(new Parser(new Lexer("INSERT INTO #test (id, name) VALUES (2, 'item 2')").Tokenize(), "").ParseStatement());

                var dataSource = (InMemoryDataSource)evaluator.Connections["#test"];
                await dataSource.FlushToSpillAsync();

                // Verify spill files exist
                string spillPath = Path.Combine(sessionRoot, sessionId, "spill");
                Assert.True(Directory.Exists(spillPath));
                var spillFiles = Directory.GetFiles(spillPath);
                Assert.NotEmpty(spillFiles);

                // 2. Save session
                Console.WriteLine("DEBUG: Save session");
                await manager.SaveSession(sessionId, evaluator);

                // 3. Rehydrate in new evaluator
                Console.WriteLine("DEBUG: Rehydrate");
                var sp2 = CreateServiceProvider(sessionRoot);
                var evaluator2 = sp2.GetRequiredService<Evaluator>();
                evaluator2.IsPersistentSession = true;
                evaluator2.SessionId = sessionId;
                evaluator2.SessionRoot = sessionRoot;

                var state = await manager.LoadSession(sessionId);
                await evaluator2.LoadSessionState(state);

                // 4. Verify data
                Console.WriteLine("DEBUG: Select");
                var results = new List<DataTable>();
                var selectStmt = (SelectStatement)new Parser(new Lexer("SELECT * FROM #test").Tokenize(), "").ParseStatement();
                await foreach (var batch in evaluator2.EvaluateSelect(selectStmt)) results.Add(batch);

                Assert.Equal(2, results.Sum(r => r.Rows.Count));
                Console.WriteLine($"DEBUG: Verified {results.Sum(r => r.Rows.Count)} rows");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(sessionRoot))
                {
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            Directory.Delete(sessionRoot, true);
                            break;
                        }
                        catch (IOException)
                        {
                            if (i == 4) Console.WriteLine("WARN: Failed to delete session root after 5 attempts.");
                            await Task.Delay(200);
                        }
                    }
                }
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SpillStore_EmptyOrNullPersistentKey_FailsClosed(bool useNull)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "ETL-SQL-SpillEmptyKey", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var sessionManager = new Mock<ISessionStateManager>();
                sessionManager.Setup(m => m.SessionRoot).Returns(tempRoot);
                sessionManager.Setup(m => m.GetSpillKey(It.IsAny<string>()))
                    .Returns(useNull ? null! : Array.Empty<byte>());

                var ctx = new Mock<IExecutionContext>();
                ctx.SetupGet(c => c.IsPersistentSession).Returns(true);
                ctx.SetupGet(c => c.SessionId).Returns("sess_empty_key");
                ctx.SetupGet(c => c.SessionRoot).Returns(tempRoot);
                ctx.SetupGet(c => c.SessionStateManager).Returns(sessionManager.Object);
                ctx.SetupGet(c => c.SpillEncryptionEnabled).Returns(true);
                ctx.SetupGet(c => c.Logger).Returns(new Mock<ILogger>().Object);

                using var store = new SpillStore(ctx.Object);
                var ex = await Assert.ThrowsAsync<ExecutionException>(() => store.CreateWriterAsync("chunk1"));
                Assert.Contains("missing or empty", ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }

        [Fact]
        public async Task SetSpillEncryptionOff_BlockedByDeploymentAndEnterprisePolicy()
        {
            var prevPolicy = EnterprisePolicyRuntime.Current;
            try
            {
                var logger = new Mock<ILogger>();
                var offStmt = new SetSpillOptionStatement(SpillOptionType.Encryption, false);

                // 1. Standalone without policy allows SET SPILL_ENCRYPTION OFF.
                var standaloneCtx = new Mock<IExecutionContext>();
                standaloneCtx.SetupProperty(c => c.SpillEncryptionEnabled, true);
                standaloneCtx.SetupProperty(c => c.SpillCompressionEnabled, true);
                standaloneCtx.SetupGet(c => c.ExecutionPolicy).Returns(
                    ExecutionPolicySnapshot.Capture(EffectiveEnterprisePolicy.Standalone, "dev", ScriptExecutionMode.Batch, "h"));

                var standaloneHandler = new SetSpillOptionStatementHandler(logger.Object, new ConfigurationBuilder().Build());
                await standaloneHandler.Execute(offStmt, standaloneCtx.Object);
                Assert.False(standaloneCtx.Object.SpillEncryptionEnabled);

                // 2. Local deployment config Security:RequireSpillEncryption = true blocks SET SPILL_ENCRYPTION OFF.
                var strictConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Security:RequireSpillEncryption"] = "true"
                    })
                    .Build();
                var configProtectedCtx = new Mock<IExecutionContext>();
                configProtectedCtx.SetupProperty(c => c.SpillEncryptionEnabled, true);
                configProtectedCtx.SetupGet(c => c.ExecutionPolicy).Returns(
                    ExecutionPolicySnapshot.Capture(EffectiveEnterprisePolicy.Standalone, "dev", ScriptExecutionMode.Batch, "h"));

                var strictHandler = new SetSpillOptionStatementHandler(logger.Object, strictConfig);
                await Assert.ThrowsAsync<ExecutionException>(() => strictHandler.Execute(offStmt, configProtectedCtx.Object));
                Assert.True(configProtectedCtx.Object.SpillEncryptionEnabled);

                // 3. Enrolled enterprise policy with RequireSpillEncryption = true blocks SET SPILL_ENCRYPTION OFF.
                var doc = new OrganizationPolicyDocument
                {
                    MutationGuardrails = new MutationGuardrailPolicySection { RequireSpillEncryption = true }
                };
                var enrolled = new EffectiveEnterprisePolicy(
                    true, true, "Live", "v1", "h",
                    System.DateTimeOffset.UtcNow.AddMinutes(-1),
                    System.DateTimeOffset.UtcNow.AddHours(1),
                    System.DateTimeOffset.UtcNow,
                    doc,
                    EnterprisePolicyConfiguration.Flatten(doc.ToPolicyValues()));
                EnterprisePolicyRuntime.SetCurrent(enrolled);

                var enrolledCtx = new Mock<IExecutionContext>();
                enrolledCtx.SetupProperty(c => c.SpillEncryptionEnabled, true);
                enrolledCtx.SetupGet(c => c.ExecutionPolicy).Returns(
                    ExecutionPolicySnapshot.Capture(enrolled, "alice", ScriptExecutionMode.Batch, "h"));

                await Assert.ThrowsAsync<OperationPolicyDeniedException>(() => standaloneHandler.Execute(offStmt, enrolledCtx.Object));
                Assert.True(enrolledCtx.Object.SpillEncryptionEnabled);
            }
            finally
            {
                EnterprisePolicyRuntime.SetCurrent(prevPolicy);
            }
        }

        [Fact]
        public async Task SpillStore_ContendedWriteSlots_WakeAndCompleteWithoutErrors()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "ETL-SQL-SpillSlots", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var sessionManager = new NullSessionStateManager();
                var ctx = new Mock<IExecutionContext>();
                ctx.SetupGet(c => c.IsPersistentSession).Returns(false);
                ctx.SetupGet(c => c.SessionId).Returns("sess_slots");
                ctx.SetupGet(c => c.SessionStateManager).Returns(sessionManager);
                ctx.SetupGet(c => c.SpillEncryptionEnabled).Returns(false);
                ctx.SetupGet(c => c.SpillCompressionEnabled).Returns(false);
                ctx.SetupGet(c => c.SpillFormat).Returns("Json");
                ctx.SetupGet(c => c.EffectiveSpillWriteConcurrency).Returns(1);
                ctx.SetupGet(c => c.AdaptiveMetrics).Returns(new ETL_SQL.Core.Adaptive.AdaptiveRuntimeMetrics());
                ctx.SetupGet(c => c.Logger).Returns(new Mock<ILogger>().Object);
                ctx.SetupGet(c => c.CancellationToken).Returns(System.Threading.CancellationToken.None);

                using var store = new SpillStore(ctx.Object);
                var table = new DataTable();
                table.SetColumns(["id"]);
                var row = table.NewRow();
                row["id"] = 1m;
                var tasks = Enumerable.Range(0, 8).Select(async i =>
                {
                    await using var writer = await store.CreateWriterAsync($"c_{i}");
                    await writer.WriteRowAsync(row);
                });

                await Task.WhenAll(tasks);
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }
}
