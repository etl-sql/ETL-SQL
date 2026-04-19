using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Xunit;
using Moq;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Functions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Security
{
    public class Batch2HardeningTests
    {
        private Mock<ILogger> _logger = new();
        private Mock<IConnectorRegistry> _connectors = new();
        private Mock<IServiceProvider> _services = new();
        private SecurityService _security;

        public Batch2HardeningTests()
        {
            _security = new SecurityService(_logger.Object);
            _security.IsTestMode = true;
        }

        [Fact]
        public void ValidatePath_ShouldResolveSymlinks()
        {
            // This test requires OS support for symlinks, so we mock/simulate or check environment
            var tempDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-Symlink-Test-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            var safeZone = Path.Combine(tempDir, "Safe");
            Directory.CreateDirectory(safeZone);
            
            var targetDir = Path.Combine(tempDir, "Secret");
            Directory.CreateDirectory(targetDir);
            File.WriteAllText(Path.Combine(targetDir, "data.txt"), "secret data");

            var symlinkPath = Path.Combine(safeZone, "LinkToSecret");

            try
            {
                // Create a symlink: LinkToSecret -> Secret (Cross-platform .NET API)
                Directory.CreateSymbolicLink(symlinkPath, targetDir);
            }
            catch (IOException ex) when (ex.Message.Contains("privilege"))
            {
                // Skip if test doesn't have privileges to create symlinks (common on Windows)
                _logger.Object.Warning("Skipping symlink test: No privilege to create symlinks on this machine.");
                return;
            }
            catch (UnauthorizedAccessException) { return; }

            var filePathViaLink = Path.Combine(symlinkPath, "data.txt");
            
            // Trigger ResolvePath via Evaluator (which calls SecurityService.ValidatePath)
            var registry = new Mock<ETL_SQL.Core.Functions.IFunctionRegistry>();
            var tracker = new Mock<ILineageTracker>();
            var docker = new Mock<IDockerManager>();
            var sessions = new Mock<SessionStateManager>(_logger.Object, _security, null);
            
            var handlers = new List<IStatementHandler>();
            var evaluator = new Evaluator(handlers, _services.Object, registry.Object, tracker.Object, docker.Object, _connectors.Object, sessions.Object, _security, _logger.Object);

            // Act
            var resolved = evaluator.ResolvePath(filePathViaLink);

            // Assert
            Assert.Contains("Secret", resolved); 
            Assert.DoesNotContain("LinkToSecret", resolved);
        }

        [Fact]
        public async Task Evaluator_ShouldRollbackTransactionsOnFailure()
        {
            var registry = new Mock<ETL_SQL.Core.Functions.IFunctionRegistry>();
            var tracker = new Mock<ILineageTracker>();
            var docker = new Mock<IDockerManager>();
            var sessions = new Mock<SessionStateManager>(_logger.Object, _security, null);
            
            var handlers = new List<IStatementHandler>();
            var evaluator = new Evaluator(handlers, _services.Object, registry.Object, tracker.Object, docker.Object, _connectors.Object, sessions.Object, _security, _logger.Object);

            // 1. Begin a transaction manually
            await evaluator.BeginTransaction();
            Assert.Equal(1, evaluator.TranCount);

            // 2. Create a script that will fail (Division by zero)
            // Note: We need the SelectStatementHandler registered
            var selectHandler = new SelectStatementHandler(_logger.Object);
            handlers.Add(selectHandler);
            
            var sql = "SELECT 1/0";
            var tokens = new ETL_SQL.Core.Parser.Lexer(sql).Tokenize();
            var script = new ETL_SQL.Core.Parser.Parser(tokens, sql).Parse();
            
            // 3. Evaluate - should throw
            await Assert.ThrowsAnyAsync<Exception>(() => evaluator.Evaluate(script));

            // 4. Verify tran count is 0 (Emergency Rollback)
            Assert.Equal(0, evaluator.TranCount);
            _logger.Verify(l => l.Warning(It.Is<string>(s => s.Contains("emergency rollback")), It.IsAny<object[]>()), Times.Once);
        }

    }
}
