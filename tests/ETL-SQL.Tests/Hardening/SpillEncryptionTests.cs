using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Engine;
using ETL_SQL.Core;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
using Moq;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Execution;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Orchestrator.Execution;

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
            
            services.AddSingleton<ISessionStateManager>(sp => {
                return new SessionStateManager(logger.Object, security, config, sessionRoot);
            });
            services.AddSingleton<SessionStateManager>(sp => (SessionStateManager)sp.GetRequiredService<ISessionStateManager>());

            services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
            services.AddTransient<ETL_SQL.Engine.Services.ReportRegistry>();
            services.AddTransient<IReportContext, ETL_SQL.Engine.Services.ReportRegistry>();
            services.AddSingleton<IJobHistoryStore>(new Mock<IJobHistoryStore>().Object);
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
            var manager = new SessionStateManager(logger.Object, security, config);

            var key1 = manager.GetSpillKey("session1");
            var key2 = manager.GetSpillKey("session1");
            var key3 = manager.GetSpillKey("session2");

            Assert.Equal(key1, key2);
            Assert.NotEqual(key1, key3);
            Assert.Equal(32, key1.Length); // SHA256
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
                evaluator.SessionRoot = Path.Combine(sessionRoot, sessionId);
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
                evaluator2.SessionRoot = Path.Combine(sessionRoot, sessionId);

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
    }
}
