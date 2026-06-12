using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Portal connection storage and folder-tree flattening for Publish.</summary>
    public class PortalTests
    {
        private static string TempPath() =>
            Path.Combine(Path.GetTempPath(), "etlsql_portal_" + Path.GetRandomFileName() + ".json");

        [Fact]
        public void Config_SaveLoad_RoundTrips()
        {
            var original = PortalConfig.ConfigPath;
            PortalConfig.ConfigPath = TempPath();
            try
            {
                new PortalConfig { Url = "http://localhost:5001", Token = "abc", Expiry = DateTime.UtcNow.AddMinutes(30) }.Save();
                var loaded = PortalConfig.Load();
                Assert.Equal("http://localhost:5001", loaded.Url);
                Assert.Equal("abc", loaded.Token);
                Assert.True(loaded.HasValidToken);
            }
            finally { PortalConfig.Clear(); PortalConfig.ConfigPath = original; }
        }

        [Fact]
        public void Config_Clear_RemovesStoredConnection()
        {
            var original = PortalConfig.ConfigPath;
            PortalConfig.ConfigPath = TempPath();
            try
            {
                new PortalConfig { Url = "http://x", Token = "t" }.Save();
                PortalConfig.Clear();
                var loaded = PortalConfig.Load();
                Assert.Null(loaded.Url);
                Assert.Null(loaded.Token);
            }
            finally { PortalConfig.Clear(); PortalConfig.ConfigPath = original; }
        }

        [Theory]
        [InlineData(30, true)]   // token, future expiry
        [InlineData(-1, false)]  // token, past expiry
        public void Config_HasValidToken_RespectsExpiry(int minutesFromNow, bool expected)
        {
            var cfg = new PortalConfig { Token = "t", Expiry = DateTime.UtcNow.AddMinutes(minutesFromNow) };
            Assert.Equal(expected, cfg.HasValidToken);
        }

        [Fact]
        public void Config_NoToken_IsNotValid()
        {
            Assert.False(new PortalConfig { Token = null, Expiry = DateTime.UtcNow.AddHours(1) }.HasValidToken);
        }

        [Fact]
        public void FlattenFolders_ProducesFullPaths()
        {
            using var doc = JsonDocument.Parse(
                "[{\"id\":1,\"name\":\"Sales\",\"children\":[{\"id\":2,\"name\":\"Q4\"}]},{\"id\":3,\"name\":\"HR\"}]");
            var list = new List<(int id, string path)>();
            PortalClient.FlattenFolders(doc.RootElement, "", list);

            Assert.Contains((1, "/Sales"), list);
            Assert.Contains((2, "/Sales/Q4"), list);
            Assert.Contains((3, "/HR"), list);
        }
    }
}
