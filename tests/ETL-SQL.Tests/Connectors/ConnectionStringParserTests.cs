using System.Collections.Generic;
using ETL_SQL.Connectors;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

public class ConnectionStringParserTests
{
    [Fact]
    public void Parse_StandardAdoNetConnectionString_NormalizesKeysAndExtractsSecret()
    {
        var raw = "Server=192.168.1.50,1433;Database=FinanceDW;User Id=db_admin;Password=SuperSecretPass!#;TrustServerCertificate=True;Connect Timeout=45;";
        var result = ConnectionStringParser.Parse(raw, "MSSQL");

        Assert.Equal("MSSQL", result.DetectedProvider);
        Assert.Equal("192.168.1.50", result.Options["SERVER"]);
        Assert.Equal("1433", result.Options["PORT"]);
        Assert.Equal("FinanceDW", result.Options["DATABASE"]);
        Assert.Equal("db_admin", result.Options["USER"]);
        Assert.Equal("ON", result.Options["TRUST_SERVER_CERTIFICATE"]);
        Assert.Equal("45", result.Options["CONNECT_TIMEOUT"]);
        Assert.Equal("SuperSecretPass!#", result.ExtractedCredential);
        Assert.Equal("MSSQL_FINANCEDW_PW", result.SuggestedSecretKey);
    }

    [Fact]
    public void Parse_PostgresUri_ExtractsHostPortDbUserAndSecret()
    {
        var raw = "postgres://etl_user:P%40ssw0rd!@postgres-dw.corp.internal:5432/reporting_db?sslmode=require";
        var result = ConnectionStringParser.Parse(raw);

        Assert.Equal("POSTGRES", result.DetectedProvider);
        Assert.Equal("postgres-dw.corp.internal", result.Options["SERVER"]);
        Assert.Equal("5432", result.Options["PORT"]);
        Assert.Equal("reporting_db", result.Options["DATABASE"]);
        Assert.Equal("etl_user", result.Options["USER"]);
        Assert.Equal("P@ssw0rd!", result.ExtractedCredential);
        Assert.Equal("require", result.Options["SSLMODE"]);
        Assert.Equal("POSTGRES_REPORTING_DB_PW", result.SuggestedSecretKey);
    }

    [Fact]
    public void Parse_WindowsTrustedConnection_DetectsSspiAndSetsTrustedConnection()
    {
        var raw = "Data Source=sql-cluster;Initial Catalog=HR;Integrated Security=SSPI;MultiSubnetFailover=True;";
        var result = ConnectionStringParser.Parse(raw, "MSSQL");

        Assert.Equal("sql-cluster", result.Options["SERVER"]);
        Assert.Equal("HR", result.Options["DATABASE"]);
        Assert.Equal("ON", result.Options["TRUSTED_CONNECTION"]);
        Assert.Equal("ON", result.Options["MULTI_SUBNET_FAILOVER"]);
        Assert.Null(result.ExtractedCredential);
    }

    [Fact]
    public void Parse_SftpUri_ParsesHostUserAndPort()
    {
        var raw = "sftp://sftp_svc:VaultSecretKey99@sftp.partner.com:2222/inbound/orders";
        var result = ConnectionStringParser.Parse(raw);

        Assert.Equal("SFTP", result.DetectedProvider);
        Assert.Equal("sftp.partner.com", result.Options["HOST"]);
        Assert.Equal("2222", result.Options["PORT"]);
        Assert.Equal("sftp_svc", result.Options["USER"]);
        Assert.Equal("VaultSecretKey99", result.ExtractedCredential);
    }
}
