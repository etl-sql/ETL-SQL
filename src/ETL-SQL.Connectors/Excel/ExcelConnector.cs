using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Connectors.FlatFile;

namespace ETL_SQL.Connectors.Excel
{
    public class ExcelConnector : IConnector
    {
        public string Name => "EXCEL";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null) => Task.FromResult("Excel Connector 1.0");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "SHEET", Array.Empty<string>() },
            { "HEADER", new[] { "ON", "OFF" } },
            { "RANGE", Array.Empty<string>() },
            { "COMPRESS", new[] { "ON", "OFF" } },
            { "ENCRYPT", new[] { "ON", "OFF" } },
            { "PASSWORD", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "HEADER", new[] { "ON", "OFF" } },
            { "COMPRESS", new[] { "ON", "OFF" } },
            { "ENCRYPT", new[] { "ON", "OFF" } }
        };

        public string GetHelp() =>
            "EXCEL Connector: Connects to Excel workbooks (.xlsx, .xls, .xlsb).\n" +
            "Options:\n" +
            "  SHEET: Name of the sheet to read (default: first sheet)\n" +
            "  HEADER: ON | OFF (treat first row as header, default ON)\n" +
            "  RANGE: Cell range to read (e.g. 'A1:D100')\n" +
            "  COMPRESS: ON | OFF (GZip compress the output file)\n" +
            "  ENCRYPT: ON | OFF (AES encryption for the file)\n" +
            "  PASSWORD: Password for encryption/decryption";

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null) 
            => new ExcelDataSource(connectionString, options, logger);

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public async Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null)
        {
            var ds = new ExcelDataSource(connectionString, null, logger);
            return await ds.GetColumnsAsync();
        }
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());

        /// <summary>Builds an Excel file path from named properties.</summary>
        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);
    }
}
