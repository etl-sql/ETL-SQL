using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;
using Moq;
using ETL_SQL.Common;
using ETL_SQL.Services;
using ETL_SQL.Core;

namespace ETL_SQL.Tests.Hardening
{
    public class Batch12SecurityDeepTests
    {
        private readonly Mock<ILogger> _logger = new Mock<ILogger>();
        private readonly SecurityService _security;

        public Batch12SecurityDeepTests()
        {
            _security = new SecurityService(_logger.Object);
        }

        [Theory]
        [InlineData(@"C:\Data\..\Windows\System32\cmd.exe")]
        [InlineData(@"..\..\..\etc\passwd")]
        [InlineData(@"C:\Users\Public\Documents\..\..\..\Windows\explorer.exe")]
        [InlineData(@"/etc/shadow")]
        [InlineData(@"/usr/bin/../../etc/passwd")]
        public void ValidatePath_ShouldBlockTraversalAttempts(string path)
        {
            // Act & Assert
            Assert.Throws<SecurityException>(() => _security.ValidatePath(path));
        }

        [Fact]
        public void ValidatePath_ShouldBlockRootAccess()
        {
            string root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\" : "/";
            Assert.Throws<SecurityException>(() => _security.ValidatePath(root));
        }

        [Fact]
        public void ValidatePath_ShouldDetectSymlinkEscapes()
        {
            // Only runs if we can create a symlink (might require admin on Windows)
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            try
            {
                string linkPath = Path.Combine(tempDir, "evil_link");
                string targetPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                    ? @"C:\Windows" 
                    : "/etc";

                // Attempt to create symlink (might fail if no permission, we skip if so)
                try 
                {
                    Directory.CreateSymbolicLink(linkPath, targetPath);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                {
                    return; // Skip test on restricted environments
                }

                // Act & Assert
                // Even though the link itself is in a 'safe' temp dir, it points to a blocked system dir.
                Assert.Throws<SecurityException>(() => _security.ValidatePath(linkPath));
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Theory]
        [InlineData("2001:db8:85a3::8a2e:370:7334")]
        [InlineData("[::1]")]
        [InlineData("xn--80ak6aa92e.com")] // IDN example
        public void ValidateHost_ShouldHandleDiverseFormats(string host)
        {
            // Arrange
            _security.AllowedHosts.Clear();
            _security.AllowedHosts.Add("*.example.com");
            _security.AllowedHosts.Add("[::1]");

            // Act & Assert
            if (host == "[::1]")
            {
                _security.ValidateHost(host); // Should pass
            }
            else
            {
                Assert.Throws<SecurityException>(() => _security.ValidateHost(host)); // Should block if not in list
            }
        }

        [Fact]
        public void ValidateHost_ShouldAllowLocalhostExplicity()
        {
            _security.AllowedHosts.Clear(); // Strict mode
            _security.ValidateHost("localhost"); // Should pass (hardcoded loopback exception)
            _security.ValidateHost("127.0.0.1"); // Should pass
        }
    }
}
