using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Engine;
using ETL_SQL.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine;

public class SaaSEngineTablesTests
{
    private static Evaluator NewEval() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    [Fact]
    public async Task EngCapabilities_ReturnsExpectedSchema()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"caps_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var oldEnv = Environment.GetEnvironmentVariable("ETLSQL_CAPABILITY_ROOT");

        try
        {
            Environment.SetEnvironmentVariable("ETLSQL_CAPABILITY_ROOT", tempRoot);
            var file1 = Path.Combine(tempRoot, "my_cert.pem");
            var file2 = Path.Combine(tempRoot, "token.key");
            await File.WriteAllTextAsync(file1, "cert-data");
            await File.WriteAllTextAsync(file2, "secret-token");

            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse("SELECT name, size_bytes, is_available FROM eng.capabilities;"));

            Assert.NotNull(eval.LastResult);
            Assert.Contains("name", eval.LastResult!.ColumnNames);
            Assert.Contains("size_bytes", eval.LastResult!.ColumnNames);
            Assert.Contains("is_available", eval.LastResult!.ColumnNames);

            var rows = eval.LastResult.Rows;
            Assert.Equal(2, rows.Count);
            Assert.Equal("my_cert.pem", rows[0]["name"]);
            Assert.Equal("token.key", rows[1]["name"]);
            Assert.True((bool)rows[0]["is_available"]!);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ETLSQL_CAPABILITY_ROOT", oldEnv);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EngTenantContext_DefaultStandalone_ReturnsExpectedRow()
    {
        var eval = NewEval();
        await eval.Evaluate(TestHelpers.Parse("SELECT tenant_id, run_id, is_sandboxed FROM eng.tenant_context;"));

        Assert.NotNull(eval.LastResult);
        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("standalone", row["tenant_id"]);
        Assert.Equal("local", row["run_id"]);
        Assert.False((bool)row["is_sandboxed"]!);
    }

    [Fact]
    public async Task EngTenantContext_WithStorageCapability_ReturnsBoundTenant()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"tenant_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var capability = TenantStorageCapability.FromServerAuthority(
                TenantContext.FromHostConfiguration("tenant-acme"),
                "run-1234",
                new[] { ("workspace", tempRoot, TenantStorageAccess.All) });

            var eval = NewEval();
            eval.StorageCapability = capability;

            await eval.Evaluate(TestHelpers.Parse("SELECT tenant_id, run_id, is_sandboxed, storage_grants_count FROM eng.tenant_context;"));

            Assert.NotNull(eval.LastResult);
            var row = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("tenant-acme", row["tenant_id"]);
            Assert.Equal("run-1234", row["run_id"]);
            Assert.True((bool)row["is_sandboxed"]!);
            Assert.Equal(1, row["storage_grants_count"]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CurrentTenant_And_IsSandbox_Functions_EvaluateAccurately()
    {
        var eval = NewEval();
        await eval.Evaluate(TestHelpers.Parse("SELECT CURRENT_TENANT() AS tenant, TENANT_ID() AS tenant_alias, IS_SANDBOX() AS sandbox;"));

        Assert.NotNull(eval.LastResult);
        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("standalone", row["tenant"]);
        Assert.Equal("standalone", row["tenant_alias"]);
        Assert.False((bool)row["sandbox"]!);

        // Now with tenant capability
        var tempRoot = Path.Combine(Path.GetTempPath(), $"tenant_fn_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            eval.StorageCapability = TenantStorageCapability.FromServerAuthority(
                TenantContext.FromHostConfiguration("tenant-beta"),
                "run-5678",
                new[] { ("workspace", tempRoot, TenantStorageAccess.All) });

            await eval.Evaluate(TestHelpers.Parse("SELECT CURRENT_TENANT() AS tenant, IS_SANDBOX() AS sandbox;"));
            Assert.NotNull(eval.LastResult);
            var boundRow = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("tenant-beta", boundRow["tenant"]);
            Assert.True((bool)boundRow["sandbox"]!);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EngEffectivePermissions_Default_ReturnsSystemAdmin()
    {
        var eval = NewEval();
        await eval.Evaluate(TestHelpers.Parse("SELECT principal_key, role, can_create, can_mutate, can_execute, source FROM eng.effective_permissions;"));

        Assert.NotNull(eval.LastResult);
        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("system", row["principal_key"]);
        Assert.Equal("Admin", row["role"]);
        Assert.True((bool)row["can_create"]!);
        Assert.True((bool)row["can_mutate"]!);
        Assert.True((bool)row["can_execute"]!);
        Assert.Equal("ENGINE", row["source"]);
    }

    [Fact]
    public async Task EngEffectivePermissions_WithIdentity_ReturnsEffectiveRolesAndGroups()
    {
        var eval = NewEval();
        eval.ExecutionIdentity = new ExecutionIdentity
        {
            EffectiveUser = "alice@contoso.com",
            RealUser = "alice@contoso.com",
            IsAdmin = false,
            TenantId = "tenant-xyz",
            Roles = new[] { "OrchestratorManager" },
            Groups = new[] { "DataEngineering", "Finance" }
        };

        await eval.Evaluate(TestHelpers.Parse("SELECT principal_key, role, group_id, scope, can_create FROM eng.effective_permissions;"));

        Assert.NotNull(eval.LastResult);
        var rows = eval.LastResult!.Rows;
        Assert.Equal(3, rows.Count); // 1 role + 2 groups

        var roleRow = rows.First(r => (string)r["role"]! == "OrchestratorManager");
        Assert.Equal("alice@contoso.com", roleRow["principal_key"]);
        Assert.Equal("tenant-xyz", roleRow["scope"]);
        Assert.True((bool)roleRow["can_create"]!);

        var groupRows = rows.Where(r => !string.IsNullOrEmpty((string)r["group_id"]!)).ToList();
        Assert.Equal(2, groupRows.Count);
        Assert.Contains(groupRows, r => (string)r["group_id"]! == "DataEngineering");
        Assert.Contains(groupRows, r => (string)r["group_id"]! == "Finance");
    }

    [Fact]
    public async Task EngEffectivePermissions_ViewerOnly_DisallowsCreateAndMutate()
    {
        var eval = NewEval();
        eval.ExecutionIdentity = new ExecutionIdentity
        {
            EffectiveUser = "bob@contoso.com",
            RealUser = "bob@contoso.com",
            IsAdmin = false,
            TenantId = "tenant-abc",
            Roles = new[] { "OrchestratorViewer" },
            Groups = Array.Empty<string>()
        };

        await eval.Evaluate(TestHelpers.Parse("SELECT role, can_create, can_mutate, can_execute FROM eng.effective_permissions;"));

        Assert.NotNull(eval.LastResult);
        var row = Assert.Single(eval.LastResult!.Rows);
        Assert.Equal("OrchestratorViewer", row["role"]);
        Assert.False((bool)row["can_create"]!);
        Assert.False((bool)row["can_mutate"]!);
        Assert.False((bool)row["can_execute"]!);
    }

    [Fact]
    public async Task RemoteOrchestrator_EngEffectivePermissions_ParsesResponse()
    {
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                PrincipalKey = "svc-account",
                ActorIdentity = "Service Account",
                Role = "OrchestratorManager",
                GroupId = "",
                Scope = "tenant-prod",
                CanCreate = true,
                CanMutate = true,
                CanExecute = true,
                Source = "ORCHESTRATOR"
            }
        });

        var handler = new MockHttpMessageHandler(payload);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var ds = new OrchestratorDataSource(http, "secret-key", NullLogger.Instance);

        var catalogDs = ds.WithTable("eng.effective_permissions");
        var batches = await catalogDs.ReadBatches().ToListAsync();

        Assert.Single(batches);
        var row = Assert.Single(batches[0].Rows);
        Assert.Equal("svc-account", row["principal_key"]);
        Assert.Equal("OrchestratorManager", row["role"]);
        Assert.True((bool)row["can_create"]!);
        Assert.Equal("ORCHESTRATOR", row["source"]);
    }

    private sealed class MockHttpMessageHandler(string jsonResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
