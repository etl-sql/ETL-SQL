using Xunit;
using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Core.Execution;
using ETL_SQL.Common;
using ETL_SQL.Services;
using ETL_SQL.Engine.Services;
using ETL_SQL.Core.Common;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Tests.Hardening
{
    public class FileOperationPasswordTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _sourceFile;
        private readonly string _destFile;
        private readonly Mock<ILogger> _mockLogger;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IExecutionContext> _mockContext;
        private readonly SecurityService _securityService;

        public FileOperationPasswordTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_FileOpTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            _sourceFile = Path.Combine(_testDir, "source.txt");
            _destFile = Path.Combine(_testDir, "dest.enc");
            File.WriteAllText(_sourceFile, "Confidential Content");

            _mockLogger = new Mock<ILogger> { CallBase = true };
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockContext = new Mock<IExecutionContext>();
            _securityService = new SecurityService(_mockLogger.Object) { IsTestMode = true };

            _mockContext.Setup(c => c.SecurityService).Returns(_securityService);
            _mockContext.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(s => s);
            _mockContext.Setup(c => c.EvaluateValue(It.IsAny<Expression>(), It.IsAny<Row>()))
                .Returns<Expression, Row>((e, r) => Task.FromResult<object?>(e is LiteralExpression le ? le.Value?.ToString() : e?.ToString()?.Trim('\'')));
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
        }

        [Fact]
        public void Parser_SupportsPasswordClause()
        {
            var sql = $"ENCRYPT FILE '{_sourceFile}' TO '{_destFile}' PASSWORD('Secret123') WITH(OVERWRITE=ON);";
            var lexer = new Lexer(sql);
            var parser = new ETL_SQL.Core.Parser.Parser(lexer.Tokenize(), sql);
            var script = parser.Parse();

            var stmt = Assert.IsType<FileOperationStatement>(script.Statements[0]);
            Assert.Equal(FileOpType.Encrypt, stmt.Type);
            Assert.NotNull(stmt.Password);
            Assert.Contains("Secret123", stmt.Password.ToString());
        }

        [Fact]
        public async Task Handler_UsesExplicitPassword()
        {
            var stmt = new FileOperationStatement(
                FileOpType.Encrypt, 
                new LiteralExpression(_sourceFile, TokenType.STRING), 
                new LiteralExpression(_destFile, TokenType.STRING), 
                new LiteralExpression("ON", TokenType.IDENTIFIER),
                new LiteralExpression("ExplicitPass!", TokenType.STRING)
            );

            var handler = new FileOperationStatementHandler(_mockLogger.Object);
            await handler.Execute(stmt, _mockContext.Object);

            Assert.True(File.Exists(_destFile));
            
            string decrypted = Path.Combine(_testDir, "decrypted.txt");
            var decryptStmt = new FileOperationStatement(
                FileOpType.Decrypt,
                new LiteralExpression(_destFile, TokenType.STRING),
                new LiteralExpression(decrypted, TokenType.STRING),
                new LiteralExpression("ON", TokenType.IDENTIFIER),
                new LiteralExpression("ExplicitPass!", TokenType.STRING)
            );
            await handler.Execute(decryptStmt, _mockContext.Object);
            
            Assert.Equal("Confidential Content", File.ReadAllText(decrypted));
        }

        [Fact]
        public async Task ExecutionSession_LogsTelemetry()
        {
            var ctx = new CliContext { SessionId = "TEST-SES" };
            var serviceCollection = new ServiceCollection();
            
            // Register dummy services needed by Evaluator
            serviceCollection.AddSingleton(_mockLogger.Object);
            // Use a real list to avoid NullRef in foreach
            serviceCollection.AddSingleton<IEnumerable<IStatementHandler>>(new List<IStatementHandler>());
            serviceCollection.AddSingleton(new Mock<ETL_SQL.Core.Functions.IFunctionRegistry>().Object);
            serviceCollection.AddSingleton(new Mock<ILineageTracker>().Object);
            serviceCollection.AddSingleton(new Mock<IDockerManager>().Object);
            serviceCollection.AddSingleton(new Mock<IConnectorRegistry>().Object);
            serviceCollection.AddSingleton<ETL_SQL.Core.Execution.ISystemResources, ETL_SQL.Core.Execution.DefaultSystemResources>();
            serviceCollection.AddSingleton<ETL_SQL.Core.Execution.IBufferManager, ETL_SQL.Orchestrator.Execution.BufferManager>();
            serviceCollection.AddSingleton(Microsoft.Extensions.Options.Options.Create(new ETL_SQL.Core.Execution.BufferManagerOptions()));
            serviceCollection.AddSingleton<ETL_SQL.Engine.Services.EvaluatorComponentRegistry>();
            serviceCollection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            
            var realSessionManager = new SessionStateManager(_mockLogger.Object, _securityService, new ConfigurationBuilder().Build());
            serviceCollection.AddSingleton<ISessionStateManager>(realSessionManager);
            serviceCollection.AddSingleton<SessionStateManager>(realSessionManager);
            
            serviceCollection.AddSingleton(_securityService);

            var sp = serviceCollection.BuildServiceProvider();

            // We need to setup Info so it calls Log (Moq default interface methods behavior)
            _mockLogger.Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<Exception>())).Verifiable();

            var session = new ExecutionSession(sp, ctx, _mockLogger.Object);
            var result = await session.ExecuteAsync("PRINT 'Hello';");

            // Verify using Log called by default Info implementation
            _mockLogger.Verify(l => l.Log(LogLevel.Info, It.Is<string>(s => s.Contains("Starting execution session TEST-SES")), null), Times.AtLeastOnce());
            _mockLogger.Verify(l => l.Log(LogLevel.Info, It.Is<string>(s => s.Contains("Execution completed")), null), Times.AtLeastOnce());
        }
    }
}
