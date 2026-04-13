using ETL_SQL.Common;
using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Connectors.Email
{
    /// <summary>
    /// Connector for sending emails via SMTP.
    /// </summary>
    public class SmtpConnector : IConnector
    {
        public string Name => "SMTP";
        public IReadOnlyList<string> Aliases => new[] { "EMAIL" };

        public Task<string> GetVersionAsync(string connectionString, ILogger? logger = null) => Task.FromResult("MailKit 4.15.1");

        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();

        public Dictionary<string, string[]> GetSupportedOptions() => new()
        {
            { "HOST", Array.Empty<string>() },
            { "PORT", Array.Empty<string>() },
            { "USERNAME", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "USE_SSL", new[] { "TRUE", "FALSE" } },
            { "DEFAULT_FROM", Array.Empty<string>() }
        };

        public Dictionary<string, string[]> GetOptionValues() => new()
        {
            { "USE_SSL", new[] { "TRUE", "FALSE" } }
        };

        public string GetHelp() => "SMTP Connector: Integrated email notification service.";

        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null, ILogger? logger = null)
        {
            return new SmtpDataSource(options ?? new Dictionary<string, string>(), logger);
        }

        public Task<IEnumerable<string>> GetTablesAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());

        public Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName, ILogger? logger = null) => 
            Task.FromResult(new[] { "To", "Cc", "Bcc", "Subject", "Body", "Attachments" }.AsEnumerable());

        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString, ILogger? logger = null) => Task.FromResult(Enumerable.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties) => 
            ConnectionStringBuilder.Build(Name, properties);

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            if (options != null && options.TryGetValue("HOST", out var host)) return host;
            return null;
        }
    }
}
