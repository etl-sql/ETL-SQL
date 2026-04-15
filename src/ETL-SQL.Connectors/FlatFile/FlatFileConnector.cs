using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
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
        public string Name => "FLATFILE";
        public IReadOnlyList<string> Aliases => new[] { "CSV", "FILE" };

        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("FlatFile Engine 1.0");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

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
            { "PASSWORD", Array.Empty<string>() },
            { "CULTURE", Array.Empty<string>() },
            { "TRIM", new[] { "ON", "OFF" } }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "HEADER", new[] { "ON", "OFF" } },
            { "DELIMITER", new[] { "COMMA", "PIPE", "TAB", "SEMICOLON", "COLON", "TILDE" } },
            { "ROW_DELIMITER", new[] { "LF", "CR", "CRLF", "TILDE", "SEMICOLON", "COLON", "COMMA", "TAB", "PIPE" } },
            { "ENCODING", new[] { "UTF8", "ANSI", "UTF16", "LATIN1", "UNICODE", "UTF32", "ASCII" } },
            { "TEXT_QUALIFIER", new[] { "DOUBLEQUOTE", "SINGLEQUOTE", "DOUBLEQUOTES", "SINGLEQUOTES" } },
            { "NULL_AS", new[] { "NULL", "EMPTY", "BACKSLASH_N" } },
            { "STRICT_SCHEMA", new[] { "ON", "OFF" } },
            { "TRIM", new[] { "ON", "OFF" } }
        };

        public string GetHelp() =>
            "FLATFILE Connector: High-performance delimited text processing.\n" +
            "Options:\n" +
            "  DELIMITER: COMMA | PIPE | TAB | SEMICOLON | COLON | TILDE | <char>\n" +
            "  HEADER: ON (default) | OFF\n" +
            "  ENCODING: UTF8 | ANSI | UTF16 | LATIN1 | UNICODE | UTF32 | ASCII\n" +
            "  CULTURE: Locale for parsing (e.g. 'en-US', 'de-DE')\n" +
            "  TRIM: ON (default) | OFF (Whitespace management)\n" +
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

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
             => CreateDataSource(context, connectionString, options, null);

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options, IEnumerable<ColumnDefinition>? templateSchema)
        {
            if (connectionString.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return new JsonDataSource(context, connectionString, options);
            if (connectionString.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return new XmlDataSource(context, connectionString, options);
            return new FlatFileDataSource(context, connectionString, options, templateSchema);
        }


        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString)
        {
            var path = connectionString.Trim('\'', '\"', ' ', '(', ')');
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(fileName)) fileName = "Table";
            return Task.FromResult<IEnumerable<string>>(new[] { fileName, "FILE" });
        }

        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");

        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
