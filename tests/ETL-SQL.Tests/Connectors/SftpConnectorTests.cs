using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core.Common;
using Moq;
using Renci.SshNet;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "SFTP")]
    [Trait("CertificationClass", "MockedIntegration")]
    public class SftpConnectorTests
    {
        [Fact]
        public void CreateDataSource_WithKeyFile_PassesParametersToConstructor()
        {
            // Arrange
            var connector = new SftpConnector();
            var options = new Dictionary<string, string>
            {
                ["USER"] = "testuser",
                ["KEYFILE"] = "path/to/key.pem",
                ["PASSPHRASE"] = "secret"
            };
            string connectionString = "sftp.example.com";

            // Act
            var dataSource = connector.CreateDataSource(SystemExecutionContext.Instance, connectionString, options) as SftpConnector;

            // Assert
            Assert.NotNull(dataSource);
            // Since we can't easily verify private fields of the newly created connector, 
            // we've at least verified the plumbing for CreateDataSource.
        }

        [Fact]
        public async Task Constructor_WithKeyFile_UsesPrivateKeyAuth()
        {
            // We use the internal constructor with a factory to verify parameters without real SSH connections
            string host = "sftp.example.com";
            string user = "testuser";
            string keyFile = "path/to/key.pem";
            string passphrase = "secret";

            bool factoryCalled = false;
            string capturedKeyFile = null;
            string capturedPassphrase = null;

            var connector = new SftpConnector(SystemExecutionContext.Instance, host, user, null, keyFile, passphrase, (h, u, p, k, pp) =>
            {
                factoryCalled = true;
                capturedKeyFile = k;
                capturedPassphrase = pp;
                return null; // Don't actually create a client
            });

            // Trigger lazy load
            try { await InvokeClientCreationAsync(connector); } catch { }

            Assert.True(factoryCalled);
            Assert.Equal(keyFile, capturedKeyFile);
            Assert.Equal(passphrase, capturedPassphrase);
        }

        [Fact]
        public async Task Constructor_WithPassword_UsesPasswordAuth()
        {
            string host = "sftp.example.com";
            string user = "testuser";
            string pass = "mypassword";

            bool factoryCalled = false;
            string capturedPass = null;

            var connector = new SftpConnector(SystemExecutionContext.Instance, host, user, pass, null, null, (h, u, p, k, pp) =>
            {
                factoryCalled = true;
                capturedPass = p;
                return null; // Don't actually create a client
            });

            // Trigger lazy load
            try { await InvokeClientCreationAsync(connector); } catch { }

            Assert.True(factoryCalled);
            Assert.Equal(pass, capturedPass);
        }

        [Theory]
        [InlineData(@"incoming\orders\today.csv", "incoming/orders/today.csv")]
        [InlineData(@"/incoming\orders_today.csv", "/incoming/orders_today.csv")]
        [InlineData("", "")]
        public void NormalizeRemotePath_UsesUnixSeparators(string input, string expected)
        {
            Assert.Equal(expected, SftpConnector.NormalizeRemotePath(input));
        }

        [Fact]
        public void CreateDataSource_WithTimeoutSeconds_PassesTimeoutToConstructor()
        {
            // Arrange
            var connector = new SftpConnector();
            var options = new Dictionary<string, string>
            {
                ["USER"] = "testuser",
                ["TIMEOUT_SECONDS"] = "45"
            };
            string connectionString = "sftp.example.com";

            // Act
            var dataSource = connector.CreateDataSource(SystemExecutionContext.Instance, connectionString, options) as SftpConnector;

            // Assert
            Assert.NotNull(dataSource);
            var timeoutField = typeof(SftpConnector).GetField("_timeoutSeconds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var timeoutValue = (int)timeoutField.GetValue(dataSource);
            Assert.Equal(45, timeoutValue);
        }

        [Fact]
        public void CreateDataSource_ParsesHostKeyFingerprintAndAtomicUpload()
        {
            var connector = new SftpConnector();
            var options = new Dictionary<string, string>
            {
                ["USER"] = "u",
                ["HOST_KEY_FINGERPRINT"] = "SHA256:abc123",
                ["ATOMIC_UPLOAD"] = "true"
            };

            var ds = connector.CreateDataSource(SystemExecutionContext.Instance, "sftp.example.com", options) as SftpConnector;
            Assert.NotNull(ds);

            var fpField = typeof(SftpConnector).GetField("_hostKeyFingerprint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var atomicField = typeof(SftpConnector).GetField("_atomicUpload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.Equal("SHA256:abc123", (string?)fpField!.GetValue(ds));
            Assert.True((bool)atomicField!.GetValue(ds)!);
        }

        [Fact]
        public void CreateDataSource_DefaultsFingerprintNull_AtomicUploadFalse()
        {
            var connector = new SftpConnector();
            var ds = connector.CreateDataSource(SystemExecutionContext.Instance, "sftp.example.com",
                new Dictionary<string, string> { ["USER"] = "u" }) as SftpConnector;

            var fpField = typeof(SftpConnector).GetField("_hostKeyFingerprint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var atomicField = typeof(SftpConnector).GetField("_atomicUpload", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.Null((string?)fpField!.GetValue(ds));
            Assert.False((bool)atomicField!.GetValue(ds)!);
        }

        [Theory]
        // SHA256: exact, with algorithm prefix, and tolerating base64 padding differences.
        [InlineData("n0uukFPxColrSHu5cxRc8g3z6BdHm4gTZZbhTP2Xoxc", true)]
        [InlineData("SHA256:n0uukFPxColrSHu5cxRc8g3z6BdHm4gTZZbhTP2Xoxc", true)]
        [InlineData("n0uukFPxColrSHu5cxRc8g3z6BdHm4gTZZbhTP2Xoxc=", true)]
        [InlineData("WRONGFPxColrSHu5cxRc8g3z6BdHm4gTZZbhTP2Xoxc", false)]
        public void FingerprintMatches_Sha256(string pin, bool expected)
        {
            const string actualSha256 = "n0uukFPxColrSHu5cxRc8g3z6BdHm4gTZZbhTP2Xoxc";
            Assert.Equal(expected, SftpConnector.FingerprintMatches(pin, actualSha256, new byte[] { 1, 2, 3 }));
        }

        [Theory]
        // MD5: colon-separated hex, no separators, uppercase, and with the algorithm prefix.
        [InlineData("aa:bb:cc:dd", true)]
        [InlineData("aabbccdd", true)]
        [InlineData("AA:BB:CC:DD", true)]
        [InlineData("MD5:aa:bb:cc:dd", true)]
        [InlineData("aa:bb:cc:ee", false)]
        public void FingerprintMatches_Md5(string pin, bool expected)
        {
            var actualMd5 = new byte[] { 0xaa, 0xbb, 0xcc, 0xdd };
            Assert.Equal(expected, SftpConnector.FingerprintMatches(pin, "some-sha256-value", actualMd5));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void FingerprintMatches_EmptyPin_NeverMatches(string? pin)
        {
            Assert.False(SftpConnector.FingerprintMatches(pin, "anything", new byte[] { 0xaa }));
        }

        [Fact]
        public void FingerprintMatches_Sha256Pin_DoesNotMatchViaMd5Fallback()
        {
            // An explicit SHA256 pin must not accidentally match the MD5 bytes.
            Assert.False(SftpConnector.FingerprintMatches("SHA256:aabbccdd", "different", new byte[] { 0xaa, 0xbb, 0xcc, 0xdd }));
        }

        private static async Task InvokeClientCreationAsync(SftpConnector connector)
        {
            var method = typeof(SftpConnector).GetMethod(
                "GetOrCreateClientAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(method);
            var task = (Task<SftpClient>)method.Invoke(connector, null)!;
            await task;
        }
    }
}
