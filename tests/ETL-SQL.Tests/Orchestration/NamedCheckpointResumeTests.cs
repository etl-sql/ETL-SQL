using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class NamedCheckpointResumeTests
{
    [Fact]
    public async Task ExecutionSession_ResumesAtSavedLabelAndSkipsEarlierStatements()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), $"etlsql-resume-{Guid.NewGuid():N}");
        var sessionId = $"resume-{Guid.NewGuid():N}";
        Directory.CreateDirectory(sessionRoot);

        try
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
            {
                ["Session:Root"] = sessionRoot,
                ["Logging:AppLog:Directory"] = Path.Combine(sessionRoot, "logs")
            });
            var logger = provider.GetRequiredService<ILogger>();
            var context = new CliContext { SessionId = sessionId };

            await using (var first = new ExecutionSession(provider, context, logger))
            {
                var failed = await first.ExecuteAsync(@"
SET PERSIST ON;
DECLARE @x = 1;
restore_here:
THROW 'planned failure';");
                Assert.False(failed.Success);
            }

            await using (var resumed = new ExecutionSession(provider, context, logger))
            {
                var result = await resumed.ExecuteAsync(@"
DECLARE @x = 0;
SET @x = 999;
restore_here:
IF (@x <> 1) THROW 'pre-checkpoint statements replayed';
PRINT 'resumed';", resume: true);

                Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                Assert.Equal(1L, Convert.ToInt64(resumed.LastEvaluator!.GetVariable("@x")));
                Assert.Equal("restore_here", resumed.LastEvaluator.ResumeLabel);
            }
        }
        finally
        {
            try { Directory.Delete(sessionRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task DedicatedExecutionSession_SavesAndResumesBelowHostTenantRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"etlsql-dedicated-resume-{Guid.NewGuid():N}");
        var defaultRoot = Path.Combine(root, "default-sessions");
        var tenantRoot = Path.Combine(root, "tenant-sessions", "tenant-alpha");
        var sessionId = $"resume-{Guid.NewGuid():N}";
        Directory.CreateDirectory(root);

        try
        {
            var inner = DependencyInjectionSetup.BuildServiceProvider(new Dictionary<string, string?>
            {
                ["Session:Root"] = defaultRoot,
                ["Logging:AppLog:Directory"] = Path.Combine(root, "logs")
            });
            var authority = TenantStorageHostAuthority.FromServerConfiguration(
                TenantContext.FromHostConfiguration("tenant-alpha"),
                tenantRoot,
                Path.Combine(root, "scratch"),
                []);
            var provider = new AuthorityServiceProvider(
                inner,
                new FixedAuthorityProvider(authority));
            var logger = inner.GetRequiredService<ILogger>();
            var context = new CliContext { SessionId = sessionId };

            await using (var first = new ExecutionSession(provider, context, logger))
            {
                var failed = await first.ExecuteAsync("""
                    SET PERSIST ON;
                    DECLARE @x = 7;
                    tenant_restore:
                    THROW 'planned failure';
                    """);
                Assert.False(failed.Success);
            }

            Assert.True(File.Exists(Path.Combine(tenantRoot, sessionId, "metadata.db")));
            Assert.False(File.Exists(Path.Combine(defaultRoot, sessionId, "metadata.db")));

            await using var resumed = new ExecutionSession(provider, context, logger);
            var result = await resumed.ExecuteAsync("""
                DECLARE @x = 0;
                tenant_restore:
                IF (@x <> 7) THROW 'wrong checkpoint root';
                """, resume: true);

            Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Assert.Equal(7L, Convert.ToInt64(resumed.LastEvaluator!.GetVariable("@x")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class FixedAuthorityProvider(TenantStorageHostAuthority authority)
        : ITenantStorageHostAuthorityProvider
    {
        public TenantStorageHostAuthority GetAuthority(TenantContext? persistedContext = null) => authority;
    }

    private sealed class AuthorityServiceProvider(
        IServiceProvider inner,
        ITenantStorageHostAuthorityProvider authority) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ITenantStorageHostAuthorityProvider)
                ? authority
                : inner.GetService(serviceType);
    }
}
