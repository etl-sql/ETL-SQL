using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.App;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    /// <summary>
    /// The metadata every registered connector publishes to HELP, the connection wizard and the
    /// language server. Option descriptors are built from <c>GetSupportedOptions</c>, so an option
    /// that has predefined values but is not a supported option never reaches the wizard.
    /// </summary>
    [Trait("Connector", "MULTIPLE")]
    [Trait("CertificationClass", "MetadataOnly")]
    public class ConnectorMetadataContractTests
    {
        private static readonly Lazy<Dictionary<string, IConnector>> Connectors = new(() =>
            DependencyInjectionSetup.BuildServiceProvider().GetServices<IConnector>()
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));

        public static TheoryData<string> ConnectorNames()
        {
            var data = new TheoryData<string>();
            foreach (var name in Connectors.Value.Keys.OrderBy(n => n, StringComparer.Ordinal)) data.Add(name);
            return data;
        }

        [Fact]
        public void TheRegistryHasTheBuiltInConnectors()
        {
            foreach (var name in new[] { "ODBC", "POSTGRES", "MYSQL", "ORACLE", "SNOWFLAKE", "API", "EXCEL", "DIRECTORY", "AVRO", "PARQUET" })
                Assert.Contains(name, Connectors.Value.Keys, StringComparer.OrdinalIgnoreCase);
        }

        [Theory]
        [MemberData(nameof(ConnectorNames))]
        public void Help_NamesTheConnector(string name)
        {
            var connector = Connectors.Value[name];
            var help = connector.GetHelp();
            var names = connector.Aliases.Prepend(connector.Name);
            Assert.True(
                names.Any(n => help.Contains(n, StringComparison.OrdinalIgnoreCase)),
                $"{name}: help does not mention {string.Join(" or ", names)}");
        }

        [Theory]
        [MemberData(nameof(ConnectorNames))]
        public void OptionsWithValues_AreSupportedOptions(string name)
        {
            var connector = Connectors.Value[name];
            var supported = new HashSet<string>(connector.GetSupportedOptions().Keys, StringComparer.OrdinalIgnoreCase);
            var unlisted = connector.GetOptionValues()
                .Where(entry => !supported.Contains(entry.Key))
                .Select(entry => entry.Key)
                .ToList();
            Assert.True(unlisted.Count == 0, $"{name}: options with values but not supported: {string.Join(", ", unlisted)}");
        }

        [Theory]
        [MemberData(nameof(ConnectorNames))]
        public void OptionValues_ListAtLeastOneValue(string name)
        {
            var empty = Connectors.Value[name].GetOptionValues()
                .Where(entry => entry.Value == null || entry.Value.Length == 0)
                .Select(entry => entry.Key)
                .ToList();
            Assert.True(empty.Count == 0, $"{name}: options with an empty value list: {string.Join(", ", empty)}");
        }

        [Theory]
        [InlineData("AVRO", "sample.avro")]
        [InlineData("EXCEL", "sample.xlsx")]
        [InlineData("FLATFILE", "sample.csv")]
        [InlineData("JSON", "sample.json")]
        [InlineData("PARQUET", "sample.parquet")]
        [InlineData("XML", "sample.xml")]
        public void FileConnector_EncryptOptionValues_AdvertiseAndAcceptAllKeySources(string name, string fileName)
        {
            var connector = Connectors.Value[name];
            var expected = new[] { "ON", "OFF", "MACHINE", "PASSWORD", "KEYFILE", "PORTAL" };

            Assert.True(connector.GetSupportedOptions().TryGetValue("ENCRYPT", out var supportedValues),
                $"{name}: GetSupportedOptions missing ENCRYPT");
            Assert.True(connector.GetOptionValues().TryGetValue("ENCRYPT", out var advertisedValues),
                $"{name}: GetOptionValues missing ENCRYPT");

            foreach (var mode in expected)
            {
                Assert.Contains(mode, supportedValues!, StringComparer.OrdinalIgnoreCase);
                Assert.Contains(mode, advertisedValues!, StringComparer.OrdinalIgnoreCase);
            }

            var prevPortalKey = Environment.GetEnvironmentVariable(EncryptionOptions.PortalAtRestKeyEnvVar);
            try
            {
                Environment.SetEnvironmentVariable(
                    EncryptionOptions.PortalAtRestKeyEnvVar,
                    Convert.ToBase64String(new byte[32]));

                var path = Path.Combine(Path.GetTempPath(), $"rf02_{Guid.NewGuid():N}_{fileName}");
                foreach (var mode in advertisedValues!)
                {
                    var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ENCRYPT"] = mode
                    };
                    if (mode.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
                        mode.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase))
                    {
                        opts["PASSWORD"] = "rf02-test-password";
                    }
                    else if (mode.Equals("KEYFILE", StringComparison.OrdinalIgnoreCase))
                    {
                        opts["KEYFILE"] = Path.Combine(Path.GetTempPath(), "rf02_dummy.key");
                    }

                    var ds = connector.CreateDataSource(SystemExecutionContext.Instance, path, opts);
                    Assert.NotNull(ds);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(EncryptionOptions.PortalAtRestKeyEnvVar, prevPortalKey);
            }
        }
    }
}
