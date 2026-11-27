using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class PostgresSearchPathTests
{
    private readonly PostgresConnector _connector = new();

    private static Mock<IExecutionContext> CreateContext()
    {
        var context = new Mock<IExecutionContext>();
        context.Setup(c => c.SecurityService).Returns(new SecurityService(NullLogger.Instance));
        context.Setup(c => c.Logger).Returns(NullLogger.Instance);
        context.Setup(c => c.ResolvePath(It.IsAny<string>())).Returns<string>(p => p);
        return context;
    }

    [Theory]
    [InlineData("public", "public")]
    [InlineData("analytics, public", "analytics, public")]
    [InlineData("  app_v2 , _internal , s$1 , public  ", "app_v2, _internal, s$1, public")]
    [InlineData("\"MySchema\", public", "\"MySchema\", public")]
    [InlineData("\"$user\", public", "\"$user\", public")]
    [InlineData("\"my-schema\", \"my\"\"schema\"", "\"my-schema\", \"my\"\"schema\"")]
    [InlineData("\"schema,with,comma\", public", "\"schema,with,comma\", public")]
    public void BuildConnectionString_ValidSearchPath_SetsNpgsqlSearchPath(string raw, string expected)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HOST"] = "localhost",
            ["DATABASE"] = "warehouse",
            ["USER"] = "etl",
            ["PASSWORD"] = "secret",
            ["SEARCH_PATH"] = raw
        };

        var connStr = _connector.BuildConnectionString(props);
        var builder = new NpgsqlConnectionStringBuilder(connStr);

        Assert.Equal(expected, builder.SearchPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("public,")]
    [InlineData(",public")]
    [InlineData("a,,b")]
    [InlineData("\"\"")]
    [InlineData("\"unclosed")]
    [InlineData("\"a\"b")]
    [InlineData("123schema")]
    [InlineData("my-schema")]
    [InlineData("my schema")]
    [InlineData("a.b")]
    [InlineData("public; DROP TABLE users")]
    [InlineData("public' OR '1'='1")]
    [InlineData("\"line1\nline2\"")]
    [InlineData("\"has\0null\"")]
    public void BuildConnectionString_InvalidSearchPath_ThrowsExecutionException(string invalid)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HOST"] = "localhost",
            ["DATABASE"] = "warehouse",
            ["USER"] = "etl",
            ["PASSWORD"] = "secret",
            ["SEARCH_PATH"] = invalid
        };

        Assert.Throws<ExecutionException>(() => _connector.BuildConnectionString(props));
    }

    [Fact]
    public void PostgresDataSource_WithPositionalStringAndSearchPathOption_AppliesAndValidates()
    {
        var ds = new PostgresDataSource(
            CreateContext().Object,
            "Host=localhost;Database=warehouse;Username=etl;Password=secret",
            tableName: null,
            options: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SEARCH_PATH"] = "analytics, \"My-Schema\""
            });

        var builder = new NpgsqlConnectionStringBuilder(ds.ConnectionString);
        Assert.Equal("analytics, \"My-Schema\"", builder.SearchPath);
    }

    [Fact]
    public void PostgresDataSource_WithInvalidSearchPathOption_ThrowsExecutionException()
    {
        Assert.Throws<ExecutionException>(() => new PostgresDataSource(
            CreateContext().Object,
            "Host=localhost;Database=warehouse;Username=etl;Password=secret",
            tableName: null,
            options: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SEARCH_PATH"] = "public; DROP TABLE x"
            }));
    }

    [Fact]
    public async Task CreateConnection_ValidAndInvalidSearchPath_EnforcedAtCreateTime()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        const string okSql =
            "CREATE CONNECTION pg_ok AS POSTGRES(HOST='db.invalid', DATABASE='warehouse', USER='etl', SEARCH_PATH='analytics, \"$user\", public');";
        await evaluator.Evaluate(new Parser(new Lexer(okSql).Tokenize()).Parse());

        Assert.True(((IExecutionContext)evaluator).Connections.TryGetValue("pg_ok", out var conn));
        var pg = Assert.IsType<PostgresDataSource>(conn);
        Assert.Equal("analytics, \"$user\", public", new NpgsqlConnectionStringBuilder(pg.ConnectionString).SearchPath);

        const string badSql =
            "CREATE CONNECTION pg_bad AS POSTGRES(HOST='db.invalid', DATABASE='warehouse', USER='etl', SEARCH_PATH='public; DROP TABLE x');";
        await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(badSql).Tokenize()).Parse()));
    }

    [Fact]
    public void MetadataAndHelp_IncludeSearchPath()
    {
        Assert.Contains("SEARCH_PATH", _connector.GetSupportedOptions().Keys);
        Assert.Contains("SEARCH_PATH", _connector.GetHelp(), StringComparison.Ordinal);
        Assert.True(LanguageMetadata.IsKeyword("SEARCH_PATH"));
    }
}
