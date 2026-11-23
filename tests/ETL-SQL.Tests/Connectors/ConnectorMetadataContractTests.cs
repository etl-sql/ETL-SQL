using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.App;
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
    }
}
