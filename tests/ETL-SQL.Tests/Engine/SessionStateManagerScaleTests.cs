using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public class SessionStateManagerScaleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "etl-sql-session-scale-" + Guid.NewGuid());

    [Fact]
    public void GetSessions_SkipsRecursiveSizeByDefault()
    {
        var sessionDir = Path.Combine(_root, "session-a");
        var spillDir = Path.Combine(sessionDir, "spill", "nested");
        Directory.CreateDirectory(spillDir);
        File.WriteAllText(Path.Combine(sessionDir, "metadata.db"), "db");
        File.WriteAllText(Path.Combine(spillDir, "chunk.arrow"), new string('x', 1024));

        var manager = CreateManager();

        var summary = Assert.Single(manager.GetSessions());
        Assert.False(summary.IsSizeCalculated);
        Assert.Equal(0, summary.TotalSizeBytes);
        Assert.Null(summary.SizeMB);

        var measured = Assert.Single(manager.GetSessions(includeSize: true));
        Assert.True(measured.IsSizeCalculated);
        Assert.True(measured.TotalSizeBytes >= 1026);
        Assert.NotNull(measured.SizeMB);
    }

    private SessionStateManager CreateManager()
    {
        var logger = NullLogger.Instance;
        var security = new SecurityService(logger) { IsTestMode = true };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("Session:PersistentSessionTTLHours", "24") })
            .Build();

        return new SessionStateManager(logger, security, config, _root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
