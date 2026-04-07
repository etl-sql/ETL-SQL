using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Connectors.Json;
using ETL_SQL.Connectors.Xml;

namespace ETL_SQL.Connectors.FlatFile
{
    /// <summary>
    /// Connector for delimited text files (CSV, TSV, Fixed-width, Pipe-delimited).
    /// </summary>
    public class FlatFileConnector : IConnector
    {
        /// <summary>Returns the canonical name of the connector.</summary>
        public string Name => "FLATFILE";
        
        /// <summary>Returns synonymous names for this connector (CSV, FILE).</summary>
        public IReadOnlyList<string> Aliases => new[] { "CSV", "FILE" };

        /// <summary>Retrieves the version information for the FlatFile connector.</summary>
        public Task<string> GetVersionAsync(string connectionString) => Task.FromResult("FlatFile Connector 1.0");

        /// <summary>Returns supported SQL functions (none for FlatFile).</summary>
        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns supported SQL keywords (none for FlatFile).</summary>
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Returns supported connection string options (DELIMITER, HEADER, ENCODING, etc.).</summary>
        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "HEADER", new[] { "ON", "OFF" } },
            { "DELIMITER", new[] { "COMMA", "PIPE", "TAB", "SEMICOLON", "COLON", "TILDE" } },
            { "ROW_DELIMITER", new[] { "LF", "CR", "CRLF", "TILDE", "SEMICOLON", "COLON", "COMMA", "TAB", "PIPE" } },
            { "ENCODING", new[] { "UTF8", "ANSI" } },
            { "TEXT_QUALIFIER", new[] { "DOUBLEQUOTE", "SINGLEQUOTE", "DOUBLEQUOTES", "SINGLEQUOTES" } },
            { "ESCAPE_CHAR", Array.Empty<string>() },
            { "NULL_AS", new[] { "NULL", "EMPTY", "BACKSLASH_N" } },
            { "DATE_FORMAT", Array.Empty<string>() },
            { "STRICT_SCHEMA", new[] { "ON", "OFF" } },
            { "START_AT", Array.Empty<string>() },
            { "END_AT", Array.Empty<string>() },
            { "COUNT_AT_END", new[] { "ON", "OFF" } },
            { "COMPRESS", new[] { "ON", "OFF" } },
            { "ENCRYPT", new[] { "ON", "OFF" } },
            { "PASSWORD", Array.Empty<string>() }
        };

        /// <summary>Returns a map of option keys to their current selected values from the UI/prompt.</summary>
        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "HEADER", new[] { "ON", "OFF" } },
            { "DELIMITER", new[] { "COMMA", "PIPE", "TAB", "SEMICOLON", "COLON", "TILDE" } },
            { "ROW_DELIMITER", new[] { "LF", "CR", "CRLF", "TILDE", "SEMICOLON", "COLON", "COMMA", "TAB", "PIPE" } },
            { "ENCODING", new[] { "UTF8", "ANSI", "UTF16", "LATIN1", "UNICODE" } },
            { "TEXT_QUALIFIER", new[] { "DOUBLEQUOTE", "SINGLEQUOTE", "DOUBLEQUOTES", "SINGLEQUOTES" } },
            { "NULL_AS", new[] { "NULL", "EMPTY", "BACKSLASH_N" } },
            { "STRICT_SCHEMA", new[] { "ON", "OFF" } }
        };

        /// <summary>Returns a human-readable help string for the FlatFile connector.</summary>
        public string GetHelp() =>
            "FLATFILE Connector: High-performance delimited text processing.\n" +
            "Options:\n" +
            "  DELIMITER: COMMA | PIPE | TAB | SEMICOLON | COLON | TILDE | <char>\n" +
            "  HEADER: ON (default) | OFF\n" +
            "  ENCODING: UTF8 | ANSI | UTF16 | LATIN1 | UNICODE\n" +
            "  TEXT_QUALIFIER: DOUBLEQUOTE | SINGLEQUOTE\n" +
            "  ESCAPE_CHAR: Character used to escape delimiters within fields (e.g. '\\')\n" +
            "  ROW_DELIMITER: LF | CR | CRLF\n" +
            "  NULL_AS: NULL | EMPTY | BACKSLASH_N\n" +
            "  DATE_FORMAT: Date parsing format string (e.g. 'yyyy-MM-dd')\n" +
            "  STRICT_SCHEMA: ON | OFF (Enforces column counts)\n" +
            "  START_AT: <n> (Start reading at line n)\n" +
            "  END_AT: <n> (Stop reading at line n)\n" +
            "  COUNT_AT_END: ON (Validate row count at trailer)\n" +
            "  COMPRESS: ON | OFF (Transparent GZip support)\n" +
            "  ENCRYPT: ON | OFF (AES encryption for the file)\n" +
            "  PASSWORD: Password for encryption/decryption";

        /// <summary>Creates a new FlatFile, JSON, or XML data source based on the file extension.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null)
             => CreateDataSource(connectionString, options, null);

        /// <summary>Creates a new FlatFile, JSON, or XML data source with an optional template schema.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options, IEnumerable<ColumnDefinition>? templateSchema)
        {
            if (connectionString.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return new JsonDataSource(connectionString, options);
            if (connectionString.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return new XmlDataSource(connectionString, options);
            return new FlatFileDataSource(connectionString, options, templateSchema);
        }

        /// <summary>Returns the logical table name from the file system path.</summary>
        public Task<IEnumerable<string>> GetTablesAsync(string connectionString)
        {
            var path = connectionString.Trim('\'', '\"', ' ', '(', ')');
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(fileName)) fileName = "Table";
            return Task.FromResult<IEnumerable<string>>(new[] { fileName });
        }

        /// <summary>Returns a list of logical views (none for FlatFile).</summary>
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        
        /// <summary>Discovers columns for the specified file.</summary>
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName)
        {
            var ds = new FlatFileDataSource(connectionString);
            return await ds.GetColumnsAsync();
        }

        /// <summary>Returns a list of procedures/functions (none for FlatFile).</summary>
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        /// <summary>Builds a flat file path from named properties.</summary>
        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
