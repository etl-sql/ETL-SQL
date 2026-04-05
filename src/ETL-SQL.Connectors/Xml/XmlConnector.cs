using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Xml
{
    /// <summary>
    /// Connector for XML data files.
    /// Supports XPath-based data extraction.
    /// </summary>
    public class XmlConnector : IConnector
    {
        /// <summary>Returns the canonical name of the connector.</summary>
        public string Name => "XML";
        
        /// <summary>Returns synonymous names for this connector.</summary>
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        /// <summary>Retrieves the version information for the XML connector.</summary>
        public Task<string> GetVersionAsync(string connectionString) => Task.FromResult("XML Connector 1.0");

        /// <summary>Returns supported SQL functions (none for XML).</summary>
        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns supported SQL keywords (none for XML).</summary>
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns supported connection string options for XML (ROOT_PATH, COMPRESS, etc.).</summary>
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "ROOT_PATH", Array.Empty<string>() },
            { "COMPRESS", new[] { "ON", "OFF" } },
            { "ENCRYPT", new[] { "ON", "OFF" } },
            { "PASSWORD", Array.Empty<string>() }
        };

        /// <summary>Returns a map of option keys to their current selected values.</summary>
        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns a human-readable help string for the XML connector.</summary>
        public string GetHelp() =>
            "XML Connector: Connects to XML files.\n" +
            "Options:\n" +
            "  ROOT_PATH: XPath to the repeating element (e.g. '/Catalog/Book')\n" +
            "  COMPRESS: ON | OFF (Transparent GZip support)\n" +
            "  ENCRYPT: ON | OFF (AES encryption for the file)\n" +
            "  PASSWORD: Password for encryption/decryption";

        /// <summary>Creates a new XML data source instance.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null)
        {
            return new XmlDataSource(connectionString, options);
        }

        /// <summary>Returns a list of logical tables (none for XML files).</summary>
        public Task<IEnumerable<string>> GetTablesAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Returns a list of logical views (none for XML).</summary>
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Returns a list of columns for the specifies XML file.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName)
        {
            var ds = new XmlDataSource(connectionString);
            return await ds.GetColumnsAsync();
        }

        /// <summary>Returns a list of procedures/functions (none for XML).</summary>
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        /// <summary>Builds an XML file path from named properties.</summary>
        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
