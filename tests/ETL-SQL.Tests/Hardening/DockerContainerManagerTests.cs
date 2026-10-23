using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using Xunit;
using static ETL_SQL.Core.DockerContainerManager;

namespace ETL_SQL.Tests.Hardening;

/// <summary>
/// Container image and credential handling that runs without Docker.
/// </summary>
public class DockerContainerManagerTests
{
    [Theory]
    [InlineData("mcr.microsoft.com/mssql/server:2022-latest", DatabaseImage.MsSql)]
    [InlineData("postgres:15-alpine", DatabaseImage.Postgres)]
    [InlineData("registry.local:5000/library/postgres:16", DatabaseImage.Postgres)]
    [InlineData("mysql:8", DatabaseImage.MySql)]
    [InlineData("mariadb", DatabaseImage.MySql)]
    [InlineData("gvenzl/oracle-free:latest", DatabaseImage.Oracle)]
    [InlineData("postgres@sha256:0123", DatabaseImage.Postgres)]
    public void ImageKind_AcceptsTheOfficialRepositories(string image, DatabaseImage expected) =>
        Assert.Equal(expected, ImageKind(image));

    [Theory]
    [InlineData("evil/postgres-miner")]
    [InlineData("someone/mssql-tools")]
    [InlineData("redis:7")]
    [InlineData("postgres-exporter:mssql")]
    public void ImageKind_RefusesAnImageThatOnlyContainsADatabaseName(string image) =>
        Assert.Throws<ExecutionException>(() => ImageKind(image));

    [Fact]
    public void Reattach_ReadsTheCredentialsTheContainerWasCreatedWith()
    {
        var credentials = CredentialsFromEnvironment("postgres:16", null,
            ["PATH=/usr/bin", "POSTGRES_USER=etluser", "POSTGRES_PASSWORD=Et1!abc=def", "POSTGRES_DB=etldb"]);

        Assert.Equal(new DockerCredentials("etluser", "Et1!abc=def", "etldb"), credentials);
    }

    [Fact]
    public void Reattach_WithoutRecoverableCredentials_ReturnsNull() =>
        Assert.Null(CredentialsFromEnvironment("mcr.microsoft.com/mssql/server:2022-latest", null, ["PATH=/usr/bin"]));
}
