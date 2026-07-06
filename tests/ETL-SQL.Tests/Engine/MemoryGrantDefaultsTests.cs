using System;
using System.Collections.Generic;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>Resolution of the RAM-governor ceiling (Engine:TotalMemoryGrantMB).</summary>
    public class MemoryGrantDefaultsTests
    {
        private static IConfiguration Config(string? value)
        {
            var dict = new Dictionary<string, string?>();
            if (value != null) dict["Engine:TotalMemoryGrantMB"] = value;
            return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        }

        [Fact]
        public void PositiveValue_IsAbsoluteCeiling()
        {
            Assert.Equal(2048, DefaultThresholds.TotalMemoryGrantMB(Config("2048")));
        }

        [Fact]
        public void Zero_MeansUnbounded()
        {
            Assert.Equal(0, DefaultThresholds.TotalMemoryGrantMB(Config("0")));
        }

        [Fact]
        public void NegativeOrUnset_ResolvesToAuto()
        {
            var auto = DefaultThresholds.AutoMemoryGrantMB();
            Assert.Equal(auto, DefaultThresholds.TotalMemoryGrantMB(Config("-1")));
            Assert.Equal(auto, DefaultThresholds.TotalMemoryGrantMB(Config(null)));
        }

        [Fact]
        public void Auto_IsFlooredAndWithinPhysicalRam()
        {
            var auto = DefaultThresholds.AutoMemoryGrantMB();
            Assert.True(auto >= 512, $"auto ceiling {auto} MB should be floored at 512 MB");

            long physicalMB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            if (physicalMB > 0)
                Assert.True(auto <= physicalMB, $"auto ceiling {auto} MB should not exceed physical RAM {physicalMB} MB");
        }
    }
}
