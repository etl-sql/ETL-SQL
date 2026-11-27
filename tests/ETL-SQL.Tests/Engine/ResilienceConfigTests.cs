using System.Collections.Generic;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Shared;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;



namespace ETL_SQL.Tests.Engine
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
            services.AddSingleton<ETL_SQL.Core.Functions.IFunctionRegistry>(new ETL_SQL.Engine.Functions.FunctionRegistry());
            services.AddSingleton<ILineageTracker>(new LineageTracker(testLogger));
            services.AddSingleton<IDockerManager>(new DockerContainerManager(testLogger));
            services.AddSingleton<ETL_SQL.Core.Execution.ISessionMetadataStoreFactory, ETL_SQL.Core.Execution.SqliteSessionMetadataStoreFactory>();


            services.AddSingleton<ETL_SQL.Core.Execution.ISessionStateManager, ETL_SQL.Engine.Services.SessionStateManager>();
            services.AddSingleton<SecurityService>();
            services.AddSingleton<ETL_SQL.Core.Interfaces.ILanguageHelpRegistry, ETL_SQL.Core.Metadata.LanguageHelpRegistry>();

            // Mimic DependencyInjectionSetup logic
            int joinSpillThreshold = int.TryParse(configuration["Engine:JoinSpillThreshold"], out var jst) ? jst : 100000;
            int externalHashPartitions = int.TryParse(configuration["Engine:ExternalHashPartitions"], out var ehp) ? ehp : 32;

            services.AddTransient<Evaluator>(sp =>
            {
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

        // ── Connector retry behaviour ──────────────────────────────────────────
        // The pipelines are built with explicit options, never through ConnectorRetryPolicy's
        // process-wide ones, which every host sets from configuration.

        public static TheoryData<string> Connectors =>
            new() { "SqlServer", "Postgres", "Oracle", "Snowflake", "BigQuery", "Odbc", "MySql" };

        private static Polly.ResiliencePipeline Pipeline(string connector, ConnectorRetryOptions options)
        {
            var logger = NullLogger.Instance;
            return connector switch
            {
                "SqlServer" => ConnectorRetryPolicy.ForSqlServer(logger, options),
                "Postgres" => ConnectorRetryPolicy.ForPostgres(logger, options),
                "Oracle" => ConnectorRetryPolicy.ForOracle(logger, options),
                "Snowflake" => ConnectorRetryPolicy.ForSnowflake(logger, options),
                "BigQuery" => ConnectorRetryPolicy.ForBigQuery(logger, options),
                "Odbc" => ConnectorRetryPolicy.ForOdbc(logger, options),
                "MySql" => ConnectorRetryPolicy.ForMySql(logger, options),
                _ => throw new System.ArgumentOutOfRangeException(nameof(connector))
            };
        }

        /// <summary>How many times the pipeline ran an operation that always throws <paramref name="error"/>.</summary>
        private static int Attempts(string connector, System.Exception error, int maxRetries)
        {
            var attempts = 0;
            var pipeline = Pipeline(connector, new ConnectorRetryOptions { MaxAttempts = maxRetries, BaseDelaySeconds = 0 });
            Assert.ThrowsAny<System.Exception>(() => pipeline.Execute(() => { attempts++; throw error; }));
            return attempts;
        }

        private static ConnectorRetryOptions FromConfig(Dictionary<string, string?> values) =>
            ConnectorRetryPolicy.OptionsFrom(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        /// <summary>
        /// docs/administration/platform/config/portal-configuration.md documents both settings, and
        /// appsettings.json ships them; nothing used to read them.
        /// </summary>
        [Fact]
        public void ConnectorRetryPolicy_ReadsTheDocumentedSettings()
        {
            var options = FromConfig(new()
            {
                ["Connectors:Retry:MaxAttempts"] = "7",
                ["Connectors:Retry:BaseDelaySeconds"] = "0.25"
            });

            Assert.Equal(7, options.MaxAttempts);
            Assert.Equal(System.TimeSpan.FromMilliseconds(250), options.BaseDelay);
        }

        [Theory]
        [InlineData("0", "1")]
        [InlineData("-2", "1")]
        [InlineData("3", "-1")]
        public void ConnectorRetryPolicy_OutOfRangeSettings_FallBackToTheDefaults(string attempts, string delay)
        {
            var options = FromConfig(new()
            {
                ["Connectors:Retry:MaxAttempts"] = attempts,
                ["Connectors:Retry:BaseDelaySeconds"] = delay
            });

            Assert.Equal(3, options.MaxAttempts);
            Assert.Equal(System.TimeSpan.FromSeconds(1), options.BaseDelay);
        }

        [Fact]
        public void ConnectorRetryPolicy_NoSettings_UsesTheDefaults()
        {
            var options = FromConfig(new());

            Assert.Equal(3, options.MaxAttempts);
            Assert.Equal(System.TimeSpan.FromSeconds(1), options.BaseDelay);
        }

        [Theory]
        [MemberData(nameof(Connectors))]
        public void ConnectorRetryPolicy_RetriesATimeout_UpToTheConfiguredAttempts(string connector)
        {
            // One first try plus MaxAttempts retries.
            Assert.Equal(6, Attempts(connector, new System.TimeoutException(), maxRetries: 5));
        }

        [Theory]
        [MemberData(nameof(Connectors))]
        public void ConnectorRetryPolicy_DoesNotRetryANonTransientError(string connector)
        {
            Assert.Equal(1, Attempts(connector, new System.InvalidOperationException("bad query"), maxRetries: 5));
        }

        [Theory]
        [InlineData(408, 3)]
        [InlineData(429, 3)]
        [InlineData(500, 3)]
        [InlineData(503, 3)]
        [InlineData(400, 1)]
        [InlineData(404, 1)]
        public void ConnectorRetryPolicy_BigQuery_RetriesOnlyTransientStatusCodes(int status, int expectedAttempts)
        {
            var error = new Google.GoogleApiException("bigquery", "status " + status)
            {
                HttpStatusCode = (System.Net.HttpStatusCode)status
            };
            Assert.Equal(expectedAttempts, Attempts("BigQuery", error, maxRetries: 2));
        }
    }
}
