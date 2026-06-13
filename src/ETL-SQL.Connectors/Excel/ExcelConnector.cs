using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Data;

namespace ETL_SQL.Connectors.Excel
{
    public class ExcelConnector : IConnector
    {
        public string Name => "EXCEL";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public bool IsFileBased => true;


        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("Excel Engine 1.0");

        public HashSet<string> GetSupportedFunctions() => new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "PATH", Array.Empty<string>() },
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
            "  COMPRESS: ON | OFF (read a ZIP-compressed workbook input)\n" +
            "  ENCRYPT: ON | OFF (AES encryption for the file)\n" +
            "  PASSWORD: Password for encryption/decryption";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
            => new ExcelDataSource(context, connectionString, options);

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        /// <summary>Builds an Excel file path from named properties.</summary>
        public string BuildConnectionString(Dictionary<string, string> properties) =>
            ConnectionStringBuilder.Build(Name, properties);
    }
}
