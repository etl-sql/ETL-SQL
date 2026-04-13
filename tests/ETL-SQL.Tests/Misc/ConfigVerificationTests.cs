using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Xunit;
using ETL_SQL.Services;

namespace ETL_SQL.Tests.Misc
{
    public class ConfigVerificationTests
    {
        [Fact]
        public void SecurityService_RespectsConfigOverrides()
        {
            // 1. Arrange: Create a configuration with custom limits
            var inMemoryConfig = new Dictionary<string, string> {
                {"Security:MaxFileOperationsPerScript", "500"},
                {"Security:MaxRecursiveNestingDepth", "20"}
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemoryConfig)
                .Build();

            // 2. Act: Initialize SecurityService manually mirroring DependencyInjectionSetup
            var securityService = new SecurityService();
            securityService.MaxFileOperations = int.TryParse(configuration["Security:MaxFileOperationsPerScript"], out var mfo) ? mfo : SecurityService.DefaultMaxFileOperations;
            securityService.MaxRecursiveDepth = int.TryParse(configuration["Security:MaxRecursiveNestingDepth"], out var mrd) ? mrd : SecurityService.DefaultMaxRecursiveDepth;

            // 3. Assert
            Assert.Equal(500, securityService.MaxFileOperations);
            Assert.Equal(20, securityService.MaxRecursiveDepth);
            
            // Verify logic reflects these values
            // 501 should fail, 500 should pass (if not in safe zone and not allowed large count)
            var ex = Assert.Throws<SecurityException>(() => securityService.CheckRunawayProtection(501, 0, false, false, "C:\\external\\path.csv"));
            Assert.Contains("500", ex.Message);
        }
    }
}
