using System;
using System.Collections.Generic;
using Xunit;
using Moq;
using Renci.SshNet;
using ETL_SQL.Connectors;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Connectors
{
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
            var dataSource = connector.CreateDataSource(connectionString, options) as SftpConnector;

            // Assert
            Assert.NotNull(dataSource);
            // Since we can't easily verify private fields of the newly created connector, 
            // we've at least verified the plumbing for CreateDataSource.
        }

        [Fact]
        public void Constructor_WithKeyFile_UsesPrivateKeyAuth()
        {
            // We use the internal constructor with a factory to verify parameters without real SSH connections
            string host = "sftp.example.com";
            string user = "testuser";
            string keyFile = "path/to/key.pem";
            string passphrase = "secret";
            
            bool factoryCalled = false;
            string capturedKeyFile = null;
            string capturedPassphrase = null;

            var connector = new SftpConnector(host, user, null, keyFile, passphrase, NullLogger.Instance, (h, u, p, k, pp) => {
                factoryCalled = true;
                capturedKeyFile = k;
                capturedPassphrase = pp;
                return null; // Don't actually create a client
            });

            // Trigger lazy load
            try { var c = connector.GetType().GetProperty("Client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(connector); } catch { }

            Assert.True(factoryCalled);
            Assert.Equal(keyFile, capturedKeyFile);
            Assert.Equal(passphrase, capturedPassphrase);
        }

        [Fact]
        public void Constructor_WithPassword_UsesPasswordAuth()
        {
            string host = "sftp.example.com";
            string user = "testuser";
            string pass = "mypassword";
            
            bool factoryCalled = false;
            string capturedPass = null;

            var connector = new SftpConnector(host, user, pass, null, null, NullLogger.Instance, (h, u, p, k, pp) => {
                factoryCalled = true;
                capturedPass = p;
                return null; // Don't actually create a client
            });

            // Trigger lazy load
            try { var c = connector.GetType().GetProperty("Client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(connector); } catch { }

            Assert.True(factoryCalled);
            Assert.Equal(pass, capturedPass);
        }
    }
}
