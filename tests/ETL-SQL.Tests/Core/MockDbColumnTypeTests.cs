using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Core.Services;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// The schema and session explorers render a type per column, and every MOCKDB column read `ANY`
/// because `MetadataManager.GetColumnDetailsAsync` falls back to `new ColumnMetadata(name, "ANY")`
/// when the data source exposes no catalog provider. MOCKDB is the default development loop, so this
/// was the type display most people saw. These assert real declared types arrive instead.
/// </summary>
[Trait("Category", "Connectors")]
public class MockDbColumnTypeTests : System.IDisposable
{
    // MetadataManager's on-disk schema cache defaults to %LOCALAPPDATA%/ETL-SQL/SchemaCache, which is
    // machine-global and keyed by connection string, and it is consulted *before* the catalog
    // provider. An entry written by any earlier run — including runs from before MOCKDB had a
    // provider, when every column was ANY — would be served ahead of the declared types and fail
    // these assertions for reasons that have nothing to do with the code under test.
    private readonly string _cacheDir =
        Path.Combine(Path.GetTempPath(), "etlsql-mockdb-schema-tests", Guid.NewGuid().ToString("N"));

    private MetadataManager NewManager()
    {
        // A private registry, never ConnectorRegistry.Instance — the global is mutable shared state
        // and a documented source of order-dependent connector test failures.
        var registry = new ConnectorRegistry();
        registry.Register(new MockDbConnector());
        var metadata = new MetadataManager(NullLogger.Instance, registry) { SchemaCacheDirectory = _cacheDir };
        metadata.RegisterConnection("m", "MOCKDB", string.Empty);
        return metadata;
    }

    private async Task<string?> TypeOfAsync(string table, string column)
    {
        var columns = await NewManager().GetColumnDetailsAsync("m", table);
        return columns.FirstOrDefault(c => c.Name.Equals(column, System.StringComparison.OrdinalIgnoreCase))?.DataType;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
        }
        catch
        {
            // A leftover temp directory must never fail a test run.
        }
    }

    [Theory]
    // Integer widths that are indistinguishable at runtime: every numeric is a decimal once seeded,
    // so these can only be right because the seeder declares them rather than inferring them.
    [InlineData("Sales", "Quantity", "INT")]
    [InlineData("Sales", "SaleID", "BIGINT")]
    [InlineData("Products", "StockLevel", "INT")]
    [InlineData("Products", "WeightGrams", "BIGINT")]
    [InlineData("AuditTrail", "LogID", "BIGINT")]
    // Temporal and identity types the explorers previously showed as ANY.
    [InlineData("Users", "RegistrationDate", "DATE")]
    [InlineData("Users", "PreciseTime", "DATETIME2")]
    [InlineData("Users", "LastLoginOffset", "DATETIMEOFFSET")]
    [InlineData("Users", "ExternalID", "UNIQUEIDENTIFIER")]
    [InlineData("Sales", "ProcessDuration", "TIME")]
    [InlineData("Employee", "Salary", "DECIMAL(18,2)")]
    public async Task GetColumnDetails_ReturnsDeclaredType(string table, string column, string expected)
    {
        Assert.Equal(expected, await TypeOfAsync(table, column));
    }

    [Fact]
    public async Task GetColumnDetails_NoLongerReportsAnyForMockDb()
    {
        var columns = (await NewManager().GetColumnDetailsAsync("m", "Users")).ToList();

        Assert.NotEmpty(columns);
        Assert.DoesNotContain(columns, c => c.DataType.Equals("ANY", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiskCacheFileName_IncludesTheSchemaVersion_SoStaleEntriesAreBypassed()
    {
        // The on-disk cache is consulted *before* the catalog provider, so an entry written when
        // MOCKDB reported ANY would keep masking real types until it aged out — up to 14 days on an
        // existing workstation. The schema version participates in the cache filename precisely so a
        // correctness change orphans old entries instead of waiting them out. If someone drops the
        // version from the salt, stale schema silently comes back.
        var method = typeof(MetadataManager).GetMethod(
            "GetCacheFileName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var versionField = typeof(MetadataManager).GetField(
            "CacheSchemaVersion",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(versionField);
        var version = (string)versionField!.GetRawConstantValue()!;

        var manager = NewManager();
        var withVersion = (string)method!.Invoke(manager, ["conn", "Server=x;Database=y;"])!;

        // Recompute the same name with the version removed from the salt; it must differ, which is
        // what guarantees a bump orphans the previous generation of cache files.
        using var sha = System.Security.Cryptography.SHA256.Create();
        var unversionedSalt = $"{System.Environment.UserName}|{System.Environment.MachineName}|";
        var unversioned = System.Convert.ToHexString(
            sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(unversionedSalt + "Server=x;Database=y;"))).ToLowerInvariant();

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain(unversioned, withVersion, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // The seeder publishes several tables under more than one name. A qualified spelling must not
    // fall back to ANY just because the declared schema was keyed differently.
    [InlineData("Orders", "SaleID", "BIGINT")]
    [InlineData("Employee_Log", "EmpID", "INT")]
    [InlineData("DemoDb.dbo.Employee", "Salary", "DECIMAL(18,2)")]
    [InlineData("hr.departments", "Budget", "DECIMAL(18,2)")]
    public async Task GetColumnDetails_ResolvesAliasedAndQualifiedTableNames(string table, string column, string expected)
    {
        Assert.Equal(expected, await TypeOfAsync(table, column));
    }
}
