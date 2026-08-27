using System.Linq;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

public class ConnectorSchemaDescriptorTests
{
    [Fact]
    public void SqlServerConnector_ExposesRichSchemaDescriptor()
    {
        IConnector connector = new SqlServerConnector();
        var schema = connector.GetSchemaDescriptor();

        Assert.Equal("MSSQL", schema.ConnectorType);
        Assert.False(schema.IsFileBased);
        Assert.False(schema.IsDataWarehouse);
        Assert.Equal(30, schema.CommandTimeoutSeconds);
        Assert.NotEmpty(schema.Options);

        var serverOpt = schema.Options.FirstOrDefault(o => o.Name == "SERVER");
        Assert.NotNull(serverOpt);
        Assert.Equal(ConnectorOptionType.String, serverOpt.Type);
        Assert.True(serverOpt.IsMandatory);
        Assert.Equal("Basic", serverOpt.Category);

        var dbOpt = schema.Options.FirstOrDefault(o => o.Name == "DATABASE");
        Assert.NotNull(dbOpt);
        Assert.Equal(ConnectorOptionType.String, dbOpt.Type);
        Assert.True(dbOpt.IsMandatory);

        var pwdOpt = schema.Options.FirstOrDefault(o => o.Name == "PASSWORD");
        Assert.NotNull(pwdOpt);
        Assert.Equal(ConnectorOptionType.SecretReference, pwdOpt.Type);
        Assert.Equal("Auth", pwdOpt.Category);
        Assert.Equal("Credentials", pwdOpt.MutuallyExclusiveGroup);

        var trustedOpt = schema.Options.FirstOrDefault(o => o.Name == "TRUSTED_CONNECTION");
        Assert.NotNull(trustedOpt);
        Assert.Equal(ConnectorOptionType.Boolean, trustedOpt.Type);
        Assert.Equal("Credentials", trustedOpt.MutuallyExclusiveGroup);

        var intentOpt = schema.Options.FirstOrDefault(o => o.Name == "APPLICATION_INTENT");
        Assert.NotNull(intentOpt);
        Assert.Equal(ConnectorOptionType.Enum, intentOpt.Type);
        Assert.Contains("READONLY", intentOpt.AllowedValues!);
        Assert.Contains("READWRITE", intentOpt.AllowedValues!);

        var portOpt = schema.Options.FirstOrDefault(o => o.Name == "PORT");
        Assert.NotNull(portOpt);

        var timeoutOpt = schema.Options.FirstOrDefault(o => o.Name == "TIMEOUT_SECONDS");
        Assert.NotNull(timeoutOpt);
        Assert.Equal(ConnectorOptionType.Number, timeoutOpt.Type);
        Assert.Equal("Tuning", timeoutOpt.Category);
    }

    [Fact]
    public void SqlServerConnector_BuildConnectionString_IncludesPort()
    {
        var connector = new SqlServerConnector();
        var cs = connector.BuildConnectionString(new System.Collections.Generic.Dictionary<string, string>
        {
            { "SERVER", "localhost" },
            { "PORT", "1433" },
            { "DATABASE", "master" },
            { "USER", "sa" },
            { "PASSWORD", "secret" }
        });

        Assert.Contains("Data Source=localhost,1433", cs);
        Assert.Contains("Initial Catalog=master", cs);
        Assert.Contains("User ID=sa", cs);
    }

    [Fact]
    public void FlatFileConnector_ExposesFileBasedSchemaDescriptor()
    {
        IConnector connector = new FlatFileConnector();
        var schema = connector.GetSchemaDescriptor();

        Assert.Equal("FLATFILE", schema.ConnectorType);
        Assert.True(schema.IsFileBased);
        Assert.False(schema.IsDataWarehouse);

        var pathOpt = schema.Options.FirstOrDefault(o => o.Name == "PATH");
        Assert.NotNull(pathOpt);
        Assert.Equal(ConnectorOptionType.FilePath, pathOpt.Type);
        Assert.True(pathOpt.IsMandatory);
        Assert.Equal("Basic", pathOpt.Category);

        var headerOpt = schema.Options.FirstOrDefault(o => o.Name == "HEADER");
        Assert.NotNull(headerOpt);
        Assert.Equal(ConnectorOptionType.Boolean, headerOpt.Type);
        Assert.Equal("ON", headerOpt.DefaultValue);

        var delimOpt = schema.Options.FirstOrDefault(o => o.Name == "DELIMITER");
        Assert.NotNull(delimOpt);
        Assert.Equal(",", delimOpt.DefaultValue);
    }

    [Fact]
    public void ConnectorRegistry_GetAllConnectorSchemas_ReturnsAllRegistered()
    {
        var registry = new ConnectorRegistry();
        registry.Register(new SqlServerConnector());
        registry.Register(new FlatFileConnector());

        var all = registry.GetAllConnectorSchemas().ToList();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.ConnectorType == "MSSQL");
        Assert.Contains(all, s => s.ConnectorType == "FLATFILE");

        var mssql = registry.GetConnectorSchema("MSSQL");
        Assert.NotNull(mssql);
        Assert.Equal("MSSQL", mssql.ConnectorType);

        // Alias resolution
        var sqlserver = registry.GetConnectorSchema("SQLSERVER");
        Assert.NotNull(sqlserver);
        Assert.Equal("MSSQL", sqlserver.ConnectorType);
    }
}
