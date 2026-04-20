using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Common;
using Moq;

namespace ETL_SQL.Tests.Hardening
{
    public class SshKeyManagementTests
    {
        private readonly Mock<ILogger> _logger = new Mock<ILogger>();

        [Fact]
        public async Task CreateSshKeyPair_ShouldGenerateValidRsaKeys()
        {
            // Arrange
            var handler = new CreateSshKeyPairStatementHandler(_logger.Object);
            string tempPath = Path.Combine(Path.GetTempPath(), "ETL-SQL-SSH", Guid.NewGuid().ToString());
            
            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns(tempPath);
            mockContext.Setup(c => c.EvaluateValue(It.IsAny<Expression>(), It.IsAny<Row>()))
                .ReturnsAsync((Expression e, Row r) => e is LiteralExpression l ? l.Value : null);

            var stmt = new CreateSshKeyPairStatement(
                new LiteralExpression(tempPath, TokenType.STRING),
                new LiteralExpression(2048, TokenType.NUMBER),
                new LiteralExpression("RSA", TokenType.STRING)
            );

            try
            {
                // Act
                await handler.Execute(stmt, mockContext.Object);

                // Assert
                string privateKeyFile = Path.Combine(tempPath, "id_rsa");
                string publicKeyFile = Path.Combine(tempPath, "id_rsa.pub");

                Assert.True(File.Exists(privateKeyFile));
                Assert.True(File.Exists(publicKeyFile));

                string privContent = await File.ReadAllTextAsync(privateKeyFile);
                string pubContent = await File.ReadAllTextAsync(publicKeyFile);

                Assert.Contains("BEGIN PRIVATE KEY", privContent);
                Assert.Contains("BEGIN PUBLIC KEY", pubContent);
            }
            finally
            {
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
            }
        }

        [Fact]
        public async Task CreateSshKeyPair_ShouldSupportPassphrase()
        {
            // Arrange
            var handler = new CreateSshKeyPairStatementHandler(_logger.Object);
            string tempPath = Path.Combine(Path.GetTempPath(), "ETL-SQL-SSH-Pass", Guid.NewGuid().ToString());
            
            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns(tempPath);
            mockContext.Setup(c => c.EvaluateValue(It.IsAny<Expression>(), It.IsAny<Row>()))
                .ReturnsAsync((Expression e, Row r) => {
                    if (e is LiteralExpression l) return l.Value;
                    return null;
                });

            var stmt = new CreateSshKeyPairStatement(
                new LiteralExpression(tempPath, TokenType.STRING),
                new LiteralExpression(2048, TokenType.NUMBER),
                new LiteralExpression("RSA", TokenType.STRING),
                new LiteralExpression("test_passphrase", TokenType.STRING)
            );

            try
            {
                // Act
                await handler.Execute(stmt, mockContext.Object);

                // Assert
                string privateKeyFile = Path.Combine(tempPath, "id_rsa");
                Assert.True(File.Exists(privateKeyFile));

                string privContent = await File.ReadAllTextAsync(privateKeyFile);
                Assert.Contains("BEGIN ENCRYPTED PRIVATE KEY", privContent);
            }
            finally
            {
                if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true);
            }
        }
    }
}
