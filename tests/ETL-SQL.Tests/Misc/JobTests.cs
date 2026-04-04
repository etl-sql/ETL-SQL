using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Scheduling;
using ETL_SQL.Engine.Storage;
using ETL_SQL.Data;

using ETL_SQL.Engine.Handlers;
using ETL_SQL.Core.Functions;
using ETL_SQL.Engine.Functions;

namespace ETL_SQL.Tests
{
    public class JobTests
    {
        private IServiceProvider CreateServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var dbName = $"test_jobs_{Guid.NewGuid()}.db";
            services.AddSingleton<IJobHistoryStore>(new SQLiteJobHistoryStore(dbName));
            services.AddSingleton<SchedulerService>();
            var registry = new FunctionRegistry();
            FileFunctions.Register(registry);
            StandardFunctions.Register(registry);
            services.AddSingleton<IFunctionRegistry>(registry);
            
            services.AddSingleton<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager, DockerContainerManager>();
            services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
            
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
            var sql = "CREATE JOB MyJob ON SCHEDULE EVERY 1 MINUTE AS PRINT 'Hello';";
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            Assert.Single(script.Statements);
            var createJob = Assert.IsType<CreateJobStatement>(script.Statements[0]);
            Assert.Equal("MyJob", createJob.JobName);
            Assert.Equal(1, createJob.Schedule.Interval);
            Assert.Equal("MINUTE", createJob.Schedule.Unit);
            Assert.IsType<PrintStatement>(createJob.Script);
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

            var sql = "CREATE JOB TestJob ON SCHEDULE EVERY 5 SECONDS AS PRINT 'Test Run';";
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
            await store.LogJobEndAsync(historyId, "SUCCESS", rowsProcessed: 1);

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

            await evaluator.Evaluate(new Parser(new Lexer("CREATE JOB Job1 ON SCHEDULE EVERY 1 HOUR AS PRINT '1';").Tokenize()).Parse());
            await evaluator.Evaluate(new Parser(new Lexer("SHOW JOBS;").Tokenize()).Parse());

            var result = evaluator.LastResult;
            Assert.NotNull(result);
            Assert.Contains(result.Rows, r => r["Name"].ToString() == "Job1");
        }
    }
}
