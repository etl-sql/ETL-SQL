using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.BigQuery;
using ETL_SQL.Connectors.Mongodb;
using ETL_SQL.Connectors.Neo4j;
using ETL_SQL.Connectors.Odbc;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.Snowflake;
using ETL_SQL.Connectors.Sqlite;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Services;
using Google.Cloud.BigQuery.V2;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// Regression tests for the database connector findings of the section 6 bug and security review.
/// None of these needs a live database: each one stops at a guard, a parser, or a local provider
/// failure that happens before any network I/O.
/// </summary>
[Trait("Category", "Connectors")]
public sealed class DatabaseConnectorReviewFixTests
{
    private static Mock<IExecutionContext> Context(bool whatIf = false, SecurityService? security = null)
    {
        var context = new Mock<IExecutionContext>();
        context.Setup(c => c.SecurityService).Returns(security ?? new SecurityService(NullLogger.Instance));
        context.Setup(c => c.Logger).Returns(NullLogger.Instance);
        context.Setup(c => c.IsWhatIf).Returns(whatIf);
        context.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
        return context;
    }

    private static async IAsyncEnumerable<DataTable> OneRow()
    {
        var table = new DataTable();
        table.SetColumns(["id"]);
        await table.AddRowAsync(new Row { ["id"] = 1m });
        yield return table;
    }

    // ── SQLite: a writable database passes the script-immutability write guard ──

    [Fact]
    public void Sqlite_WritableDatabaseWithAScriptExtension_IsRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.etlsql");

        Assert.Throws<SecurityException>(() =>
            new SqliteDataSource(Context().Object, $"Data Source={path}"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Sqlite_ReadOnlyDatabase_IsNotAWrite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.etlsql");

        var source = new SqliteDataSource(Context().Object, $"Data Source={path};Mode=ReadOnly");

        Assert.Contains(path, source.ConnectionString);
    }

    // ── SET WHAT_IF ON: destructive data-source calls do not reach the database ──

    public static TheoryData<string> WhatIfSources => new() { "MSSQL", "POSTGRES", "ORACLE", "ODBC" };

    private static IDataSource CreateUnreachable(string kind, IExecutionContext context) => kind switch
    {
        // Every target is a closed local port; reaching the provider would throw.
        "MSSQL" => new SqlServerDataSource(context, "Server=127.0.0.1,1;Database=x;Connect Timeout=1", "t"),
        "POSTGRES" => new PostgresDataSource(context, "Host=127.0.0.1;Port=1;Database=x;Timeout=1", "t"),
        "ORACLE" => new OracleDataSource(context, "Data Source=127.0.0.1:1/x;User Id=u;Password=p;Connection Timeout=1", "t"),
        "ODBC" => new OdbcDataSource(context, "Driver={No Such ETLSQL Driver};Server=127.0.0.1", "t"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [MemberData(nameof(WhatIfSources))]
    public async Task WhatIf_WriteBatchesWithoutAppend_DoesNotTruncateOrWrite(string kind)
    {
        var source = CreateUnreachable(kind, Context(whatIf: true).Object);

        await source.WriteBatches(OneRow(), append: false);
    }

    [Theory]
    [MemberData(nameof(WhatIfSources))]
    public async Task WhatIf_TruncateAsync_DoesNotReachTheDatabase(string kind)
    {
        var source = CreateUnreachable(kind, Context(whatIf: true).Object);

        var truncate = source.GetType().GetMethod("TruncateAsync", Type.EmptyTypes)!;
        await (Task)truncate.Invoke(source, null)!;
    }

    [Fact]
    public async Task WithoutWhatIf_TruncateAsync_StillReachesTheProvider()
    {
        var source = (SqlServerDataSource)CreateUnreachable("MSSQL", Context().Object);

        await Assert.ThrowsAsync<ExecutionException>(() => source.TruncateAsync());
    }

    // ── Snowflake: provider failures are sanitized ExecutionExceptions ──

    [Fact]
    public async Task Snowflake_ReadBatchesOpenFailure_IsAnExecutionException()
    {
        var source = new SnowflakeDataSource(Context().Object, "user=alice;password=s3cr3t;", "t", null);

        var error = await Assert.ThrowsAsync<ExecutionException>(async () =>
        {
            await foreach (var _ in source.ReadBatches(10, CancellationToken.None)) { }
        });
        Assert.DoesNotContain("s3cr3t", error.Message);
    }

    [Fact]
    public async Task Snowflake_BeginTransactionOpenFailure_IsAnExecutionException()
    {
        var source = new SnowflakeDataSource(Context().Object, "user=alice;password=s3cr3t;", "t", null);

        await Assert.ThrowsAsync<ExecutionException>(() => source.BeginTransactionAsync());
        await source.CommitAsync(); // no transaction was left half-open
    }

    [Fact]
    public void Snowflake_PrivateKeyFileOnlyInOptions_IsAddedToTheConnectionString()
    {
        var keyDir = Path.Combine(Path.GetTempPath(), $"etlsql-sf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDir);
        var key = Path.Combine(keyDir, "rsa_key.p8");
        File.WriteAllText(key, "-----BEGIN PRIVATE KEY-----");
        try
        {
            var source = new SnowflakeDataSource(Context().Object, "account=a;user=alice;authenticator=snowflake_jwt", "t",
                new Dictionary<string, string> { ["PRIVATE_KEY_FILE"] = key });

            Assert.Contains($"private_key_file={key}", source.ConnectionString, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(keyDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("account=a;private_key_file=old", "account=a;private_key_file=new")]
    [InlineData("account=a", "account=a;private_key_file=new;")]
    [InlineData("account=a;", "account=a;private_key_file=new;")]
    public void Snowflake_ReplaceConnectionStringValue_SetsOrAppends(string input, string expected)
    {
        Assert.Equal(expected, SnowflakeDataSource.ReplaceConnectionStringValue(input, "private_key_file", "new"));
    }

    // ── Oracle / SQL Server / Postgres connectors wrap provider failures; Oracle quoting ──

    [Fact]
    public async Task OracleConnector_GetVersionWithAMalformedString_IsAnExecutionException()
    {
        await Assert.ThrowsAsync<ExecutionException>(() =>
            new OracleConnector().GetVersionAsync(Context().Object, "invalid"));
    }

    [Fact]
    public async Task OracleConnector_GetProceduresWithAMalformedString_IsAnExecutionException()
    {
        await Assert.ThrowsAsync<ExecutionException>(() =>
            new OracleConnector().GetProceduresAsync(Context().Object, "invalid"));
    }

    [Fact]
    public async Task SqlServerConnector_GetProceduresWithAMalformedString_IsAnExecutionException()
    {
        await Assert.ThrowsAsync<ExecutionException>(() =>
            new SqlServerConnector().GetProceduresAsync(Context().Object, "invalid"));
    }

    [Fact]
    public async Task PostgresConnector_GetProceduresWithAMalformedString_IsAnExecutionException()
    {
        await Assert.ThrowsAsync<ExecutionException>(() =>
            new PostgresConnector().GetProceduresAsync(Context().Object, "invalid"));
    }

    [Theory]
    [InlineData("\"MyTable\"", "\"MyTable\"")]
    [InlineData("mytable", "\"MYTABLE\"")]
    [InlineData("hr.\"Mixed Case\"", "\"HR\".\"Mixed Case\"")]
    [InlineData("\"a\"\"b\"", "\"a\"\"b\"")]
    public void OracleQuoteIdentifier_KeepsQuotedNamesAndUppercasesBareOnes(string input, string expected)
    {
        Assert.Equal(expected, OracleDataSource.QuoteIdentifier(input));
    }

    // ── BigQuery: decimal parameters beyond NUMERIC scale bind as BIGNUMERIC ──

    [Fact]
    public void BigQuery_DecimalWithinNumericScale_BindsAsNumeric()
    {
        var (type, value) = BigQueryDataSource.InferParameter(12.5m);

        Assert.Equal(BigQueryDbType.Numeric, type);
        Assert.Equal(BigQueryNumeric.Parse("12.5"), value);
    }

    [Fact]
    public void BigQuery_DecimalBeyondNumericScale_BindsAsBigNumericInsteadOfThrowing()
    {
        var (type, value) = BigQueryDataSource.InferParameter(1.0123456789m);

        Assert.Equal(BigQueryDbType.BigNumeric, type);
        Assert.Equal(BigQueryBigNumeric.Parse("1.0123456789"), value);
    }

    // ── MongoDB: every seed host is validated; TIMEOUT_SECONDS reaches the driver ──

    [Fact]
    public void Mongodb_GetHosts_ReturnsEverySeedHost()
    {
        var hosts = new MongodbConnector().GetHosts("mongodb://allowed.example.com:27017,blocked.example.net:27017/db");

        Assert.Equal(["allowed.example.com", "blocked.example.net"], hosts);
    }

    [Fact]
    public void Mongodb_CreateDataSource_BlockedSecondHost_IsRefused()
    {
        var security = new SecurityService(NullLogger.Instance) { IsTestMode = false };
        security.AllowedHosts.Clear();
        security.AllowedHosts.Add("allowed.example.com");

        Assert.ThrowsAny<SecurityException>(() => new MongodbConnector().CreateDataSource(
            Context(security: security).Object,
            "mongodb://allowed.example.com:27017,blocked.example.net:27017/db"));
    }

    [Fact]
    public void Mongodb_CreateDataSource_AllAllowedHosts_IsAccepted()
    {
        var security = new SecurityService(NullLogger.Instance) { IsTestMode = false };
        security.AllowedHosts.Clear();
        security.AllowedHosts.Add("a.example.com");
        security.AllowedHosts.Add("b.example.com");

        var source = new MongodbConnector().CreateDataSource(
            Context(security: security).Object, "mongodb://a.example.com:27017,b.example.com:27017/db");

        Assert.NotNull(source);
    }

    [Fact]
    public void Mongodb_TimeoutSeconds_IsAppliedToServerSelectionAndConnect()
    {
        var source = new MongodbDataSource(Context().Object, "mongodb://localhost:27017/db", "db", "c",
            new Dictionary<string, string> { ["TIMEOUT_SECONDS"] = "7" });

        var settings = source.BuildClientSettings();

        Assert.Equal(TimeSpan.FromSeconds(7), settings.ServerSelectionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(7), settings.ConnectTimeout);
    }

    [Fact]
    public async Task Mongodb_WriteBatchesWithAMalformedUrl_IsAnExecutionException()
    {
        var source = new MongodbDataSource(Context().Object, "not-a-mongodb-url", "db", "c");

        await Assert.ThrowsAsync<ExecutionException>(() => source.WriteBatches(OneRow(), append: true));
    }

    // ── Neo4j: engine integers stay integers; ? inside literals is text ──

    [Fact]
    public void Neo4j_EngineIntegerDecimal_IsStoredAsAnInteger()
    {
        Assert.Equal(42L, Neo4jDataSource.NormalizePropertyValue(42m, "id"));
        Assert.Equal(9007199254740993L, Neo4jDataSource.NormalizePropertyValue(9007199254740993m, "id"));
        Assert.Equal(1.5d, Neo4jDataSource.NormalizePropertyValue(1.5m, "ratio"));
    }

    [Fact]
    public void Neo4j_QuestionMarkInsideAStringLiteral_IsNotAParameter()
    {
        var bound = new Dictionary<string, object?>();

        var cypher = Neo4jDataSource.RewritePositionalParameters(
            "MATCH (n) WHERE n.title = 'what?' AND n.id = ? RETURN n // really?", [5m], bound);

        Assert.Equal("MATCH (n) WHERE n.title = 'what?' AND n.id = $p1 RETURN n // really?", cypher);
        Assert.Equal(5m, bound["p1"]);
    }

    [Fact]
    public void Neo4j_QuestionMarkWithoutParameters_InLiteralLeavesTheQueryUntouched()
    {
        var bound = new Dictionary<string, object?>();

        var cypher = Neo4jDataSource.RewritePositionalParameters("RETURN 'why?' AS q", null, bound);

        Assert.Equal("RETURN 'why?' AS q", cypher);
        Assert.Empty(bound);
    }

    [Fact]
    public void Neo4j_IndexedPlaceholders_BindByIndexIncludingTwoDigits()
    {
        var values = Enumerable.Range(1, 10).Select(i => (object?)(decimal)i).ToList();
        var bound = new Dictionary<string, object?>();

        var cypher = Neo4jDataSource.RewritePositionalParameters("RETURN ?10 AS a, ?1 AS b", values, bound);

        Assert.Equal("RETURN $p10 AS a, $p1 AS b", cypher);
        Assert.Equal(10m, bound["p10"]);
        Assert.Equal(1m, bound["p1"]);
    }

    // ── ODBC: compiler @pN markers become positional ? markers ──

    [Fact]
    public void Odbc_CompilerParameters_BecomePositionalMarkersInOrderOfUse()
    {
        var (sql, values) = OdbcDataSource.BindPositionally(
            "SELECT * FROM t WHERE a = @p1 AND b = @p0 AND c = '@p0' AND d = @p10x",
            ["zero", "one"]);

        Assert.Equal("SELECT * FROM t WHERE a = ? AND b = ? AND c = '@p0' AND d = @p10x", sql);
        Assert.Equal(["one", "zero"], values);
    }

    [Fact]
    public void Odbc_QuestionMarkSql_KeepsItsParametersAsGiven()
    {
        var (sql, values) = OdbcDataSource.BindPositionally("SELECT * FROM t WHERE a = ? AND b = ?", [1m, 2m]);

        Assert.Equal("SELECT * FROM t WHERE a = ? AND b = ?", sql);
        Assert.Equal([1m, 2m], values);
    }

    [Fact]
    public void Odbc_SelectBaseTables_DropsViewsAndSystemTables()
    {
        var schema = new System.Data.DataTable();
        schema.Columns.Add("TABLE_NAME");
        schema.Columns.Add("TABLE_TYPE");
        schema.Rows.Add("orders", "TABLE");
        schema.Rows.Add("order_totals", "VIEW");
        schema.Rows.Add("sysobjects", "SYSTEM TABLE");
        schema.Rows.Add("customers", "BASE TABLE");

        Assert.Equal(["orders", "customers"], OdbcDataSource.SelectBaseTables(schema));
    }

    // ── Data-warehouse default timeout comes from configuration ──

    private static Mock<IExecutionContext> ContextWithConfig(string? defaultSeconds)
    {
        var values = new Dictionary<string, string?>();
        if (defaultSeconds != null) values["Connectors:DataWarehouse:DefaultCommandTimeoutSeconds"] = defaultSeconds;
        var configuration = Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(new Microsoft.Extensions.Configuration.ConfigurationBuilder(), values).Build();
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(Microsoft.Extensions.Configuration.IConfiguration))).Returns(configuration);
        var context = Context();
        context.Setup(c => c.ServiceProvider).Returns(services.Object);
        return context;
    }

    [Theory]
    [InlineData("600", 600)]
    [InlineData(null, 1800)]
    [InlineData("0", 1800)]
    [InlineData("not-a-number", 1800)]
    public void DataWarehouseDefaultTimeout_ReadsTheConfigurationKey(string? configured, int expected)
    {
        Assert.Equal(expected, ETL_SQL.Connectors.Shared.DataWarehouseTimeouts.DefaultSeconds(ContextWithConfig(configured).Object));
    }

    [Fact]
    public void DataWarehouseDefaultTimeout_WithoutServices_IsTheBuiltInDefault()
    {
        Assert.Equal(1800, ETL_SQL.Connectors.Shared.DataWarehouseTimeouts.DefaultSeconds(Context().Object));
        Assert.Equal(1800, ETL_SQL.Connectors.Shared.DataWarehouseTimeouts.DefaultSeconds(null));
    }
}
