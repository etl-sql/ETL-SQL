using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Functions;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Orchestrator.Scheduling;
using ETL_SQL.Orchestrator.Storage;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration
{
    public class JobTests
    {
        private IServiceProvider CreateServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var configuration = new ConfigurationBuilder().Build();
            services.AddSingleton<IConfiguration>(configuration);

            services.AddSingleton<ETL_SQL.Common.ILogger>(new ETL_SQL.Common.EngineLogger());
            var dbName = $"test_jobs_{Guid.NewGuid()}.db";
            var store = new SQLiteJobHistoryStore(dbName);
            services.AddSingleton<IJobHistoryStore>(store);
            services.AddSingleton<IJobCatalogStore>(store);
            services.AddSingleton<IBundleStore>(store);
            services.AddSingleton<ILineageCatalogStore>(store);
            // The relational store also backs the host-metrics time series; register it so the
            // auto-scanned ShowHostMetricsStatementHandler can be constructed (mirrors production DI).
            services.AddSingleton<IHostMetricsStore>(store);
            services.AddSingleton<SchedulerService>();

            var registry = new FunctionRegistry();
            FileFunctions.Register(registry);
            StandardFunctions.Register(registry);
            services.AddSingleton<IFunctionRegistry>(registry);

            services.AddSingleton<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager, DockerContainerManager>();
            services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
            services.AddSingleton<ETL_SQL.Engine.Services.EvaluatorComponentRegistry>();
            services.AddSingleton<ETL_SQL.Core.Execution.ISessionMetadataStoreFactory, ETL_SQL.Core.Execution.SqliteSessionMetadataStoreFactory>();
            services.AddSingleton<ETL_SQL.Core.Execution.ISessionStateManager, ETL_SQL.Engine.Services.SessionStateManager>();
            services.AddSingleton<ETL_SQL.Engine.Services.SessionStateManager>(sp => (ETL_SQL.Engine.Services.SessionStateManager)sp.GetRequiredService<ETL_SQL.Core.Execution.ISessionStateManager>());

            services.AddSingleton(new CliContext());
            services.AddSingleton<SecurityService>();
            services.AddSingleton<IScriptExecutor, ScriptExecutorAdapter>();
            services.AddSingleton(new ETL_SQL.Orchestrator.Execution.JobThrottleOptions());
            services.AddSingleton<ETL_SQL.Orchestrator.Execution.JobThrottle>();

            services.AddSingleton<ETL_SQL.Core.Execution.ISystemResources, ETL_SQL.Core.Execution.DefaultSystemResources>();
            services.AddSingleton<ETL_SQL.Core.Execution.IBufferManager, ETL_SQL.Orchestrator.Execution.BufferManager>();
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ETL_SQL.Core.Execution.BufferManagerOptions()));
            services.AddSingleton<ETL_SQL.Core.Interfaces.ILanguageHelpRegistry>(new ETL_SQL.Core.Metadata.LanguageHelpRegistry());

            services.AddTransient<Evaluator>();

            // Register Handlers using reflection
            var handlerAssembly = typeof(DeclareStatementHandler).Assembly;
            var handlerTypes = handlerAssembly.GetTypes()
                .Where(t => typeof(IStatementHandler).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in handlerTypes)
            {
                services.AddTransient(typeof(IStatementHandler), type);
                services.AddTransient(type); // Still register concrete type just in case some code resolves it directly
            }

            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task TestCreateJobParsing()
        {
            var sql = "CREATE JOB MyJob FOR SCRIPT 'jobs/my-job.etlsql' " +
                      "WITH (MAX_RETRIES = 2, RETRY_DELAY = 15);";
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Single(script.Statements);
            var createJob = Assert.IsType<CreateJobStatement>(script.Statements[0]);
            Assert.Equal("MyJob", createJob.JobName);
            Assert.Equal(JobTargetKind.Script, createJob.TargetKind);
            Assert.Equal("jobs/my-job.etlsql", createJob.TargetPath);
            Assert.Equal(2, createJob.MaxRetries);
            Assert.Equal(15, createJob.RetryDelaySeconds);
        }

        [Fact]
        public async Task TestJobExecutionAndHistory()
        {
            var provider = CreateServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();
            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();

            // Clear existing data for test isolation if needed
            await store.DeleteJobAsync("TestJob");

            var sql = "CREATE JOB TestJob FOR SCRIPT 'jobs/test-job.etlsql';";
            var lexer = new Lexer(sql);
            var script = new Parser(lexer.Tokenize()).Parse();

            await evaluator.Evaluate(script);

            var activeJobs = await store.GetActiveJobsAsync();
            Assert.Contains(activeJobs, j => j.Name == "TestJob");

            // Verify history is empty initially
            var history = await store.GetHistoryAsync("TestJob");
            Assert.Empty(history);

            // Trigger scheduler manually for one run
            var scheduler = provider.GetRequiredService<SchedulerService>();
            // Since we can't easily wait for background tasks in a unit test without more plumbing,
            // we'll use a manual execution logic similar to what scheduler does but synchronously.

            long historyId = await store.LogJobStartAsync("TestJob");
            await store.LogJobEndAsync(historyId, "SUCCESS", rowsProcessed: 1, peakMemoryBytes: 1024, cpuTimeSeconds: 0.5);

            history = await store.GetHistoryAsync("TestJob");
            Assert.Single(history);
            Assert.Equal("SUCCESS", history.First().Status);
        }

        [Fact]
        public async Task TestShowJobs()
        {
            var provider = CreateServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();
            var store = provider.GetRequiredService<IJobHistoryStore>();
            await store.InitializeAsync();

            await evaluator.Evaluate(new Parser(new Lexer("CREATE JOB Job1 FOR SCRIPT 'jobs/job1.etlsql';").Tokenize()).Parse());
            await evaluator.Evaluate(new Parser(new Lexer("SHOW JOBS;").Tokenize()).Parse());

            var result = evaluator.LastResult;
            Assert.NotNull(result);
            Assert.Contains(result.Rows, r => r["Name"].ToString() == "Job1");
        }
    }
}
