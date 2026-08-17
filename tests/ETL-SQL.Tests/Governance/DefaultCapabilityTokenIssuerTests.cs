using System;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;
using ETL_SQL.Engine.Governance;
using Xunit;

namespace ETL_SQL.Tests.Governance
{
    public class DefaultCapabilityTokenIssuerTests
    {
        [Fact]
        public void IssueToken_ShouldReturnValidToken()
        {
            // Arrange
            var issuer = new DefaultCapabilityTokenIssuer();
            var payload = new CapabilityToken
            {
                TenantId = "tenant-123",
                Environment = "prod",
                OperationId = "op-1",
                Actor = "user-1",
                RunAttempt = "run-1",
                PolicyVersion = "v1",
                ToolDigest = "digest-123",
                MaxMemoryBytes = 1024,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
                Nonce = Guid.NewGuid().ToString("N")
            };

            // Act
            var tokenString = issuer.IssueToken(payload);

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(tokenString));
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(tokenString));
            Assert.Contains("tenant-123", decoded);
        }

        [Fact]
        public void TryValidateToken_ShouldReturnTrueForValidToken()
        {
            // Arrange
            var issuer = new DefaultCapabilityTokenIssuer();
            var payload = new CapabilityToken
            {
                TenantId = "tenant-123",
                Environment = "prod",
                OperationId = "op-1",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
            };
            var tokenString = issuer.IssueToken(payload);

            // Act
            var result = issuer.TryValidateToken(tokenString, out var parsed);

            // Assert
            Assert.True(result);
            Assert.NotNull(parsed);
            Assert.Equal("tenant-123", parsed.TenantId);
        }

        [Fact]
        public void TryValidateToken_ShouldReturnFalseForExpiredToken()
        {
            // Arrange
            var issuer = new DefaultCapabilityTokenIssuer();
            var payload = new CapabilityToken
            {
                TenantId = "tenant-123",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            var tokenString = issuer.IssueToken(payload);

            // Act
            var result = issuer.TryValidateToken(tokenString, out var parsed);

            // Assert
            Assert.False(result);
            Assert.Null(parsed);
        }

        [Fact]
        public void TryValidateToken_ShouldReturnFalseForInvalidBase64()
        {
            // Arrange
            var issuer = new DefaultCapabilityTokenIssuer();

            // Act
            var result = issuer.TryValidateToken("invalid-token-string", out var parsed);

            // Assert
            Assert.False(result);
            Assert.Null(parsed);
        }
    }
}
