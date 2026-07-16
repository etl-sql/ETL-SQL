using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.ReportBuilder;
using ETL_SQL.Reporting;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ILogger = ETL_SQL.Common.ILogger;

namespace ETL_SQL.Tests.Reporting
{
    public static class DashboardTestHelper
    {
        // Handler types registered for every scope — kept as a shared list since it never changes.
        private static readonly List<Type> ReportingHandlers = new()
        {
            typeof(DeclareStatementHandler),
            typeof(SetVariableStatementHandler),
            typeof(SelectStatementHandler),
            typeof(InsertStatementHandler),
            typeof(ExecutePushdownStatementHandler),
            typeof(CreateTableStatementHandler),
            typeof(CreateConnectionStatementHandler),
            typeof(CreateVisualStatementHandler),
            typeof(CreatePageStatementHandler),
            typeof(CreateDatasetStatementHandler),
            typeof(CreateContainerStatementHandler),
            typeof(CreateNavigationStatementHandler),
            typeof(CreateButtonStatementHandler),
            typeof(CreateStyleStatementHandler),
            typeof(CreateThemeStatementHandler),
            typeof(SetReportMetadataStatementHandler),
            typeof(ExportReportStatementHandler)
        };

        /// <summary>
        /// Builds a fresh <see cref="ServiceProvider"/> for one DI scope.
        /// Each call produces an independent provider so singletons (EvaluatorComponentRegistry,
        /// ReportRegistry, FunctionRegistry, …) are never shared across scopes or evaluations.
        /// </summary>
        private static ServiceProvider BuildScopeProvider()
        {
            var logger = NullLogger.Instance;
            var security = new SecurityService(logger) { IsTestMode = true };
            var sessions = new SessionStateManager(logger, security, new Mock<IConfiguration>().Object, new SqliteSessionMetadataStoreFactory(), null);

            var services = new ServiceCollection();

            foreach (var type in ReportingHandlers)
            {
                services.AddTransient(typeof(IStatementHandler), type);
                services.AddTransient(type);
            }

            var connRegistry = new ConnectorRegistry();
            connRegistry.Register(new MockDbConnector());
            connRegistry.Register(new FlatFileConnector());

            services.AddSingleton<ILogger>(logger);
            services.AddSingleton(security);
            services.AddSingleton<IConnectorRegistry>(connRegistry);
            services.AddSingleton<IFunctionRegistry, FunctionRegistry>();
            services.AddSingleton<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager>(new Mock<IDockerManager>().Object);
            services.AddSingleton<ISessionStateManager>(sessions);
            services.AddSingleton<ILanguageHelpRegistry, LanguageHelpRegistry>();
            // Fresh per-scope singletons — this is the key isolation guarantee:
            // EvaluatorComponentRegistry.Initialize() is only ever called by one Evaluator per provider.
            services.AddSingleton<EvaluatorComponentRegistry>();
            services.AddSingleton<IReportContext, ReportRegistry>();
            services.AddTransient<Evaluator>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Returns a scope factory whose <c>CreateScope()</c> produces a brand-new
        /// <see cref="ServiceProvider"/> on every call, matching real ASP.NET Core scoping
        /// semantics and preventing singleton state from leaking between evaluations.
        /// </summary>
        public static IServiceScopeFactory CreateMockScopeFactory()
        {
            var mockScopeFactory = new Mock<IServiceScopeFactory>();

            mockScopeFactory
                .Setup(x => x.CreateScope())
                .Returns(() =>
                {
                    var provider = BuildScopeProvider();
                    var mockScope = new Mock<IServiceScope>();
                    mockScope.As<IAsyncDisposable>()
                        .Setup(s => s.DisposeAsync())
                        .Returns(new ValueTask(Task.CompletedTask));

                    mockScope.Setup(s => s.ServiceProvider).Returns(provider);
                    mockScope.Setup(s => s.Dispose()).Callback(() => provider.Dispose());
                    return mockScope.Object;
                });

            return mockScopeFactory.Object;
        }
    }
}
