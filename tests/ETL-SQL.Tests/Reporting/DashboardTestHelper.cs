using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Linq;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Services;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Execution;
using ETL_SQL.Engine.Services;
using ETL_SQL.Engine.Functions;
using ETL_SQL.ReportBuilder;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.FlatFile;
using ILogger = ETL_SQL.Common.ILogger;

namespace ETL_SQL.Tests.Reporting
{
    public static class DashboardTestHelper
    {
        public static IServiceScopeFactory CreateMockScopeFactory()
        {
            var mockScopeFactory = new Mock<IServiceScopeFactory>();
            var mockScope = new Mock<IServiceScope>();
            var mockProvider = new Mock<IServiceProvider>();
            
            var logger = NullLogger.Instance;
            var security = new SecurityService(logger);
            security.IsTestMode = true;
            var sessions = new SessionStateManager(logger, security, new Mock<IConfiguration>().Object, null);
            
            var services = new ServiceCollection();
            
            // Register only handlers needed for reporting to avoid dependency hell
            var reportingHandlers = new List<Type>
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

            foreach (var type in reportingHandlers)
            {
                services.AddTransient(typeof(IStatementHandler), type);
                services.AddTransient(type);
            }

            services.AddSingleton<ILogger>(logger);
            services.AddSingleton(security);
            var registry = new ConnectorRegistry();
            registry.Register(new MockDbConnector());
            registry.Register(new FlatFileConnector());
            services.AddSingleton<IConnectorRegistry>(registry);
            services.AddSingleton<IFunctionRegistry, FunctionRegistry>();
            services.AddSingleton<ILineageTracker, LineageTracker>();
            services.AddSingleton<IDockerManager>(new Mock<IDockerManager>().Object);
            services.AddSingleton<ISessionStateManager>(sessions);
            services.AddSingleton<ILanguageHelpRegistry, LanguageHelpRegistry>();
            services.AddSingleton<EvaluatorComponentRegistry>();
            
            // DashboardService needs a real ReportContext in the provider if it's going to use it
            services.AddSingleton<IReportContext, ReportRegistry>();
            
            services.AddTransient<Evaluator>();
            
            var provider = services.BuildServiceProvider();
            mockProvider.Setup(x => x.GetService(typeof(Evaluator))).Returns(() => provider.GetRequiredService<Evaluator>());
            mockProvider.Setup(x => x.GetService(typeof(IEnumerable<IStatementHandler>))).Returns(() => provider.GetRequiredService<IEnumerable<IStatementHandler>>());
            mockProvider.Setup(x => x.GetService(It.IsAny<Type>())).Returns((Type t) => provider.GetService(t));
            
            mockScope.Setup(x => x.ServiceProvider).Returns(mockProvider.Object);
            mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);
            
            return mockScopeFactory.Object;
        }
    }
}
