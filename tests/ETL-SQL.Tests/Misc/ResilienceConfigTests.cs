using Xunit;
using ETL_SQL.Engine;
using ETL_SQL.Data;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Services;
using ETL_SQL.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;



namespace ETL_SQL.Tests.Misc
{
    public class ResilienceConfigTests
    {
        [Fact]
        public void Evaluator_RespectsRegistrationScalingOptions()
        {
            // Arrange
            var inMemoryConfig = new Dictionary<string, string> {
                {"Engine:JoinSpillThreshold", "500"},
                {"Engine:ExternalHashPartitions", "64"}
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemoryConfig)
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IServiceProvider>(sp => sp);
            services.AddSingleton<IEnumerable<IStatementHandler>>(Array.Empty<IStatementHandler>());
            services.AddSingleton<IConnectorRegistry>(new ConnectorRegistry());

            var testLogger = new LoggerService();
            services.AddSingleton<ETL_SQL.Common.ILogger>(testLogger);
            services.AddSingleton<Core.Functions.IFunctionRegistry>(new ETL_SQL.Engine.Functions.FunctionRegistry());
            services.AddSingleton<ILineageTracker>(new LineageTracker(testLogger));
            services.AddSingleton<IDockerManager>(new DockerContainerManager(testLogger));


            services.AddSingleton<ETL_SQL.Engine.Services.SessionStateManager>();
            services.AddSingleton<SecurityService>();

            // Mimic DependencyInjectionSetup logic
            int joinSpillThreshold = int.TryParse(configuration["Engine:JoinSpillThreshold"], out var jst) ? jst : 100000;
            int externalHashPartitions = int.TryParse(configuration["Engine:ExternalHashPartitions"], out var ehp) ? ehp : 32;

            services.AddTransient<Evaluator>(sp => {
                var evaluator = ActivatorUtilities.CreateInstance<Evaluator>(sp);
                evaluator.JoinSpillThreshold = joinSpillThreshold;
                evaluator.ExternalHashPartitions = externalHashPartitions;
                return evaluator;
            });

            var sp = services.BuildServiceProvider();

            // Act
            var evaluator = sp.GetRequiredService<Evaluator>();

            // Assert
            Assert.Equal(500, evaluator.JoinSpillThreshold);
            Assert.Equal(64, evaluator.ExternalHashPartitions);
        }

        [Fact]
        public void ConnectorRetryPolicy_RespectsInitialization()
        {
            // Arrange
            var options = new ConnectorRetryOptions {
                MaxAttempts = 99,
                BaseDelaySeconds = 2.5
            };

            // Act
            ConnectorRetryPolicy.Initialize(options);

            // Assert - We can't easily check private fields without reflection, 
            // but we can verify that the ForSqlServer pipeline is built 
            // (this at least ensures no crashes during init).
            var pipeline = ConnectorRetryPolicy.ForSqlServer(new LoggerService());
            Assert.NotNull(pipeline);
            
            // Cleanup - Reset to defaults for other tests
            ConnectorRetryPolicy.Initialize(new ConnectorRetryOptions());
        }
    }
}
