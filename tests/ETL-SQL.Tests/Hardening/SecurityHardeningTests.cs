using System;
using System.IO;
using System.Runtime.InteropServices;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class SecurityHardeningTests
    {
        private readonly Mock<ILogger> _logger = new Mock<ILogger>();
        private readonly SecurityService _security;

        public SecurityHardeningTests()
        {
            _security = new SecurityService(_logger.Object);
            _security.IsTestMode = false; // Force host/path validation logic to run
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

        [Theory]
        [InlineData(@"/mnt/c/Users/data/sales.csv")]
        [InlineData(@"/mnt/data/analytics/sales.parquet")]
        [InlineData(@"C:\Projects\lib\helpers\data.csv")]
        public void ValidatePath_ShouldAllowLegitimateMountedAndLibPaths(string path)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && path.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
            {
                return; // Skip Windows drive syntax on Linux
            }
            _security.ValidatePath(path);
        }

        [Theory]
        [InlineData(@"/mnt/c/Windows/System32/cmd.exe")]
        [InlineData(@"/mnt/c/Users/chuck/.ssh/id_rsa")]
        [InlineData(@"/boot/vmlinuz")]
        [InlineData(@"/sys/class/net")]
        public void ValidatePath_ShouldStillBlockSystemDirectoriesUnderMounts(string path)
        {
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

        [Theory]
        [InlineData("/proc")]
        [InlineData("/proc/self/environ")]
        [InlineData("/proc/cpuinfo")]
        [InlineData(@"C:\proc\self\environ")]
        [InlineData(@"C:\proc\cpuinfo")]
        public void ValidatePath_ShouldBlockProcDirectory(string path)
        {
            Assert.Throws<SecurityException>(() => _security.ValidatePath(path));
        }

        [Theory]
        [InlineData("169.254.169.254")]
        [InlineData("fd00:ec2::254")]
        [InlineData("[fd00:ec2::254]")]
        [InlineData("metadata.google.internal")]
        public void ValidateHost_ShouldBlockCloudMetadataByDefault(string host)
        {
            // Even with wildcard AllowedHosts, IMDS and cloud metadata must be blocked
            _security.AllowedHosts.Clear();
            _security.AllowedHosts.Add("*");

            Assert.Throws<SecurityException>(() => _security.ValidateHost(host));
        }

        [Fact]
        public void StorageCapability_EnforcesPathProtectionModeDefined()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tenant-defined-{Guid.NewGuid():N}");
            var outside = Path.Combine(Path.GetTempPath(), $"tenant-outside-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);

            try
            {
                var capability = ETL_SQL.Core.Multitenancy.TenantStorageCapability.FromServerAuthority(
                    ETL_SQL.Core.Multitenancy.TenantContext.FromHostConfiguration("tenant-defined-test"),
                    "run-101",
                    [("workspace", root, ETL_SQL.Core.Multitenancy.TenantStorageAccess.All)]);

                var context = new ETL_SQL.Core.Common.SystemExecutionContext();
                context.StorageCapability = capability;

                Assert.Equal(PathProtectionMode.Defined, context.SecurityService.ProtectionMode);
                Assert.Contains(root, context.SecurityService.ApprovedSafeZones);

                // Inside the grant root is permitted
                context.SecurityService.ValidatePath(Path.Combine(root, "allowed.csv"));

                // Outside the grant root must be blocked in Defined mode
                Assert.Throws<SecurityException>(() =>
                    context.SecurityService.ValidatePath(Path.Combine(outside, "forbidden.csv")));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
                try { Directory.Delete(outside, true); } catch { }
            }
        }

        [Fact]
        public void EnvironmentSecretAccess_DoesNotTouchProcAndRespectsAllowlist()
        {
            var testVarName = $"ETLSQL_TEST_SECRET_{Guid.NewGuid():N}";
            var testSecretValue = "ultra-secret-token-value";

            try
            {
                Environment.SetEnvironmentVariable(testVarName, testSecretValue);

                // Denied when not in AllowedEnvVars
                Assert.Throws<SecurityException>(() => _security.ValidateEnvVar(testVarName));

                // Allowed when added to AllowedEnvVars
                _security.AllowedEnvVars.Add(testVarName);
                _security.ValidateEnvVar(testVarName);

                // Value is retrieved successfully via in-memory Environment API
                Assert.Equal(testSecretValue, Environment.GetEnvironmentVariable(testVarName));
            }
            finally
            {
                Environment.SetEnvironmentVariable(testVarName, null);
            }
        }
    }
}
