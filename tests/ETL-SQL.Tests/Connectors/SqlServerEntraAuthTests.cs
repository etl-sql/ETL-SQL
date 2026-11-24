using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class SqlServerEntraAuthTests
{
    private readonly SqlServerConnector _connector = new();

    private static Dictionary<string, string> BaseProps(params (string Key, string Value)[] extra)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SERVER"] = "myserver.database.windows.net",
            ["DATABASE"] = "Analytics"
        };
        foreach (var (k, v) in extra)
            props[k] = v;
        return props;
    }

    [Fact]
    public void AuthMode_DefaultAndExplicitSql_MapsUserAndPassword()
    {
        var csDefault = _connector.BuildConnectionString(BaseProps(("USER", "etl"), ("PASSWORD", "s3cr3t")));
        var b1 = new SqlConnectionStringBuilder(csDefault);
        Assert.Equal("etl", b1.UserID);
        Assert.Equal("s3cr3t", b1.Password);
        Assert.False(b1.IntegratedSecurity);
        Assert.Equal(SqlAuthenticationMethod.NotSpecified, b1.Authentication);

        var csExplicit = _connector.BuildConnectionString(BaseProps(("AUTH_MODE", "SQL"), ("USER", "etl"), ("PASSWORD", "s3cr3t")));
        var b2 = new SqlConnectionStringBuilder(csExplicit);
        Assert.Equal("etl", b2.UserID);
        Assert.Equal("s3cr3t", b2.Password);
        Assert.False(b2.IntegratedSecurity);
        Assert.Equal(SqlAuthenticationMethod.NotSpecified, b2.Authentication);
    }

    [Fact]
    public void AuthMode_WindowsAndTrustedConnection_MapsIntegratedSecurity()
    {
        var csWindows = _connector.BuildConnectionString(BaseProps(("AUTH_MODE", "WINDOWS")));
        var b1 = new SqlConnectionStringBuilder(csWindows);
        Assert.True(b1.IntegratedSecurity);

        var csBoth = _connector.BuildConnectionString(BaseProps(("AUTH_MODE", "WINDOWS"), ("TRUSTED_CONNECTION", "TRUE")));
        var b2 = new SqlConnectionStringBuilder(csBoth);
        Assert.True(b2.IntegratedSecurity);

        var csLegacy = _connector.BuildConnectionString(BaseProps(("TRUSTED_CONNECTION", "TRUE")));
        var b3 = new SqlConnectionStringBuilder(csLegacy);
        Assert.True(b3.IntegratedSecurity);
    }

    [Fact]
    public void AuthMode_EntraDefault_MapsActiveDirectoryDefault()
    {
        var cs = _connector.BuildConnectionString(BaseProps(("AUTH_MODE", "ENTRA_DEFAULT")));
        var b = new SqlConnectionStringBuilder(cs);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryDefault, b.Authentication);
        Assert.False(b.IntegratedSecurity);
        Assert.Equal(string.Empty, b.UserID);
        Assert.Equal(string.Empty, b.Password);
    }

    [Fact]
    public void AuthMode_EntraManagedIdentity_SystemAndUserAssigned()
    {
        var csSystem = _connector.BuildConnectionString(BaseProps(("AUTH_MODE", "ENTRA_MANAGED_IDENTITY")));
        var b1 = new SqlConnectionStringBuilder(csSystem);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity, b1.Authentication);
        Assert.Equal(string.Empty, b1.UserID);

        var csUser = _connector.BuildConnectionString(BaseProps(
            ("AUTH_MODE", "ENTRA_MANAGED_IDENTITY"),
            ("CLIENT_ID", "11111111-2222-3333-4444-555555555555")));
        var b2 = new SqlConnectionStringBuilder(csUser);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity, b2.Authentication);
        Assert.Equal("11111111-2222-3333-4444-555555555555", b2.UserID);
    }

    [Fact]
    public void AuthMode_EntraServicePrincipal_MapsActiveDirectoryServicePrincipal()
    {
        var cs = _connector.BuildConnectionString(BaseProps(
            ("AUTH_MODE", "ENTRA_SERVICE_PRINCIPAL"),
            ("CLIENT_ID", "11111111-2222-3333-4444-555555555555"),
            ("PASSWORD", "sp-secret-value")));
        var b = new SqlConnectionStringBuilder(cs);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryServicePrincipal, b.Authentication);
        Assert.Equal("11111111-2222-3333-4444-555555555555", b.UserID);
        Assert.Equal("sp-secret-value", b.Password);
        Assert.False(b.IntegratedSecurity);
    }

    public static TheoryData<string, (string Key, string Value)[]> InvalidCombinations => new()
    {
        // SQL mismatches
        { "SQL + TRUSTED_CONNECTION=TRUE", [("AUTH_MODE", "SQL"), ("TRUSTED_CONNECTION", "TRUE")] },
        { "SQL + CLIENT_ID", [("AUTH_MODE", "SQL"), ("CLIENT_ID", "cid")] },
        { "Default SQL + CLIENT_ID", [("CLIENT_ID", "cid")] },
        // WINDOWS mismatches
        { "WINDOWS + TRUSTED_CONNECTION=FALSE", [("AUTH_MODE", "WINDOWS"), ("TRUSTED_CONNECTION", "FALSE")] },
        { "WINDOWS + USER", [("AUTH_MODE", "WINDOWS"), ("USER", "sa")] },
        { "WINDOWS + PASSWORD", [("AUTH_MODE", "WINDOWS"), ("PASSWORD", "super-secret-xyz")] },
        { "WINDOWS + CLIENT_ID", [("AUTH_MODE", "WINDOWS"), ("CLIENT_ID", "cid")] },
        { "TRUSTED_CONNECTION=TRUE + PASSWORD", [("TRUSTED_CONNECTION", "TRUE"), ("PASSWORD", "super-secret-xyz")] },
        // ENTRA_DEFAULT mismatches
        { "ENTRA_DEFAULT + TRUSTED_CONNECTION=TRUE", [("AUTH_MODE", "ENTRA_DEFAULT"), ("TRUSTED_CONNECTION", "TRUE")] },
        { "ENTRA_DEFAULT + TRUSTED_CONNECTION=FALSE", [("AUTH_MODE", "ENTRA_DEFAULT"), ("TRUSTED_CONNECTION", "FALSE")] },
        { "ENTRA_DEFAULT + USER", [("AUTH_MODE", "ENTRA_DEFAULT"), ("USER", "sa")] },
        { "ENTRA_DEFAULT + PASSWORD", [("AUTH_MODE", "ENTRA_DEFAULT"), ("PASSWORD", "super-secret-xyz")] },
        { "ENTRA_DEFAULT + CLIENT_ID", [("AUTH_MODE", "ENTRA_DEFAULT"), ("CLIENT_ID", "cid")] },
        // ENTRA_MANAGED_IDENTITY mismatches
        { "ENTRA_MANAGED_IDENTITY + TRUSTED_CONNECTION", [("AUTH_MODE", "ENTRA_MANAGED_IDENTITY"), ("TRUSTED_CONNECTION", "TRUE")] },
        { "ENTRA_MANAGED_IDENTITY + USER", [("AUTH_MODE", "ENTRA_MANAGED_IDENTITY"), ("USER", "sa")] },
        { "ENTRA_MANAGED_IDENTITY + PASSWORD", [("AUTH_MODE", "ENTRA_MANAGED_IDENTITY"), ("PASSWORD", "super-secret-xyz")] },
        // ENTRA_SERVICE_PRINCIPAL mismatches
        { "ENTRA_SERVICE_PRINCIPAL + TRUSTED_CONNECTION", [("AUTH_MODE", "ENTRA_SERVICE_PRINCIPAL"), ("TRUSTED_CONNECTION", "TRUE"), ("CLIENT_ID", "cid"), ("PASSWORD", "super-secret-xyz")] },
        { "ENTRA_SERVICE_PRINCIPAL + USER", [("AUTH_MODE", "ENTRA_SERVICE_PRINCIPAL"), ("USER", "sa"), ("CLIENT_ID", "cid"), ("PASSWORD", "super-secret-xyz")] },
        { "ENTRA_SERVICE_PRINCIPAL missing CLIENT_ID", [("AUTH_MODE", "ENTRA_SERVICE_PRINCIPAL"), ("PASSWORD", "super-secret-xyz")] },
        { "ENTRA_SERVICE_PRINCIPAL missing PASSWORD", [("AUTH_MODE", "ENTRA_SERVICE_PRINCIPAL"), ("CLIENT_ID", "cid")] },
        // Unknown / empty AUTH_MODE
        { "Empty AUTH_MODE", [("AUTH_MODE", "   ")] },
        { "Unknown AUTH_MODE", [("AUTH_MODE", "ACCESS_TOKEN")] },
    };

    [Theory]
    [MemberData(nameof(InvalidCombinations))]
    public void BuildConnectionString_InvalidModeOrOptionMismatch_ThrowsWithoutLeakingSecret(
        string _, (string Key, string Value)[] options)
    {
        var ex = Assert.Throws<ExecutionException>(() => _connector.BuildConnectionString(BaseProps(options)));
        Assert.DoesNotContain("super-secret-xyz", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateConnection_EntraModesAndMismatches_ValidatedAtCreateTime()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

        const string okSql =
            "CREATE CONNECTION az_default AS MSSQL(SERVER='myserver.database.windows.net', DATABASE='Analytics', AUTH_MODE='ENTRA_DEFAULT', ENCRYPT=TRUE);";
        await evaluator.Evaluate(new Parser(new Lexer(okSql).Tokenize()).Parse());
        var ds = Assert.IsType<SqlServerDataSource>(((IExecutionContext)evaluator).Connections["az_default"]);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryDefault, new SqlConnectionStringBuilder(ds.ConnectionString).Authentication);

        const string badSql =
            "CREATE CONNECTION az_bad AS MSSQL(SERVER='myserver.database.windows.net', DATABASE='Analytics', AUTH_MODE='ENTRA_SERVICE_PRINCIPAL', CLIENT_ID='cid');";
        await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(badSql).Tokenize()).Parse()));
    }

    [Fact]
    public void SecretResolutionAndMetadata_CoverEntraOptions_AndExcludeAccessToken()
    {
        // PASSWORD and CLIENT_ID are SECRET:-resolvable on MSSQL and its aliases.
        foreach (var alias in new[] { "MSSQL", "SQLSERVER", "SQL" })
        {
            Assert.True(SecretResolvableFields.IsResolvable("PASSWORD", alias));
            Assert.True(SecretResolvableFields.IsResolvable("CLIENT_ID", alias));
        }
        Assert.True(SecretResolvableFields.IsCredential("PASSWORD"));
        Assert.False(SecretResolvableFields.IsCredential("CLIENT_ID"));

        var supported = _connector.GetSupportedOptions();
        Assert.Contains("AUTH_MODE", supported.Keys);
        Assert.Contains("CLIENT_ID", supported.Keys);
        Assert.DoesNotContain("ACCESS_TOKEN", supported.Keys);

        var help = _connector.GetHelp();
        Assert.Contains("AUTH_MODE", help, StringComparison.Ordinal);
        Assert.Contains("CLIENT_ID", help, StringComparison.Ordinal);

        Assert.True(LanguageMetadata.IsKeyword("AUTH_MODE"));
        Assert.True(LanguageMetadata.IsKeyword("CLIENT_ID"));
        // SqlClient takes the tenant from the server; a TENANT_ID option would be accepted and ignored.
        Assert.DoesNotContain("TENANT_ID", supported.Keys);
    }
}
