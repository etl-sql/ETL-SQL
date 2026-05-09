using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Reporting;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using ETL_SQL.Common;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Hardening
{
    public class StabilityHardeningTests
    {
        [Fact]
        public async Task SnapshotStore_AtomicWrite_DoesNotLeaveTmpFiles()
        {
            var store = new SnapshotStore();
            var manifest = new ReportManifest { BuiltAt = DateTime.UtcNow };
            string path = Path.Combine(Path.GetTempPath(), "test_snap.json");
            
            await store.SaveAsync(manifest, path);
            
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
            
            File.Delete(path);
        }
    }
}
