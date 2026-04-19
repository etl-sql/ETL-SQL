using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Core;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using System.Text;
using System.Security.Cryptography;

namespace ETL_SQL.Tests.Hardening
{
    public class Batch11RefinementTests
    {
        [Fact]
        public void CryptoVersioning_ShouldPrependVersionByte()
        {
            // Arrange
            string plainText = "SensitiveData123";
            string password = "TestPassword!";

            // Act
            string encrypted = CryptoUtils.Encrypt(plainText, password);
            
            // Assert
            Assert.StartsWith("ENC:", encrypted);
            byte[] fullBytes = Convert.FromBase64String(encrypted.Substring(4));
            Assert.Equal(1, fullBytes[0]); // Version 1 prefix
        }

        [Fact]
        public void CryptoVersioning_ShouldDecryptVersionedData()
        {
            // Arrange
            string plainText = "SensitiveData123";
            string password = "TestPassword!";
            string encrypted = CryptoUtils.Encrypt(plainText, password);

            // Act
            string decrypted = CryptoUtils.Decrypt(encrypted, password);

            // Assert
            Assert.Equal(plainText, decrypted);
        }

        [Fact]
        public void ConnectionProvider_ShouldProvideSuggestions()
        {
            // Arrange
            string misspelled = "PPOSTGRES";
            var props = new Dictionary<string, string> { { "HOST", "localhost" } };

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() => ConnectionStringBuilder.Build(misspelled, props));
            Assert.Contains("Did you mean 'POSTGRES'?", ex.Message);
        }

        [Fact]
        public async Task SessionCompression_ShouldSaveWithPrefix()
        {
            // Arrange
            var logger = new Mock<ILogger>();
            var security = new SecurityService(logger.Object);
            
            string sessionId = "test-session-" + Guid.NewGuid();
            string testDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-Tests", sessionId);
            Directory.CreateDirectory(testDir);
            
            try 
            {
                var mgr = new SessionStateManager(logger.Object, security, testDir);
                // We'll test the Compress/Decompress methods via reflection since they are private
                var compressMethod = typeof(SessionStateManager).GetMethod("Compress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var decompressMethod = typeof(SessionStateManager).GetMethod("Decompress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                string original = "Some session state data that should be compressed";

                // Act
                string compressed = (string)compressMethod.Invoke(mgr, new object[] { original });
                string decompressed = (string)decompressMethod.Invoke(mgr, new object[] { compressed });

                // Assert
                Assert.StartsWith("COMP:", compressed);
                Assert.Equal(original, decompressed);
            }
            finally
            {
                if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
            }
        }

        [Fact]
        public void ConnectionStringBuilder_ShouldSupportMockDB()
        {
            // Verify I added MOCKDB to the valid list
            var props = new Dictionary<string, string> { { "PATH", "test" } };
            // Should not throw
            ConnectionStringBuilder.Build("MOCKDB", props);
        }
    }
}
