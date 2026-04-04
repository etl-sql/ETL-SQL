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
        /// <summary>Returns the canonical name of the connector.</summary>
        public string Name => "SMTP";
        
        /// <summary>Returns synonymous names for this connector.</summary>
        public IReadOnlyList<string> Aliases => new[] { "EMAIL" };

        /// <summary>Retrieves the version of the underlying MailKit library.</summary>
        public Task<string> GetVersionAsync(string connectionString) => Task.FromResult("MailKit 4.15.1");

        /// <summary>Returns supported SQL functions (none for SMTP).</summary>
        public HashSet<string> GetSupportedFunctions() => new();

        /// <summary>Returns supported SQL keywords (none for SMTP).</summary>
        public HashSet<string> GetSupportedKeywords() => new();

        /// <summary>Returns supported connection string options for SMTP.</summary>
        public Dictionary<string, string[]> GetSupportedOptions() => new()
        {
            { "HOST", Array.Empty<string>() },
            { "PORT", Array.Empty<string>() },
            { "USERNAME", Array.Empty<string>() },
            { "PASSWORD", Array.Empty<string>() },
            { "USE_SSL", new[] { "TRUE", "FALSE" } },
            { "DEFAULT_FROM", Array.Empty<string>() }
        };

        /// <summary>Returns a map of option keys to their available values.</summary>
        public Dictionary<string, string[]> GetOptionValues() => new()
        {
            { "USE_SSL", new[] { "TRUE", "FALSE" } }
        };

        /// <summary>Returns a human-readable help string for the SMTP connector.</summary>
        public string GetHelp()
        {
            return @"SMTP Connector
Options:
  HOST: SMTP server hostname
  PORT: SMTP server port (default: 25 or 587)
  USERNAME: SMTP username
  PASSWORD: SMTP password
  USE_SSL: TRUE/FALSE (default: FALSE)
  DEFAULT_FROM: Default sender address if not specified in SEND_EMAIL";
        }

        /// <summary>Creates a new instance of the SMTP data source.</summary>
        public IDataSource CreateDataSource(string connectionString, Dictionary<string, string>? options = null)
        {
            return new SmtpDataSource(options ?? new Dictionary<string, string>());
        }

        /// <summary>Returns a list of logical tables (none for SMTP).</summary>
        public Task<IEnumerable<string>> GetTablesAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        /// <summary>Returns a list of logical views (none for SMTP).</summary>
        public Task<IEnumerable<string>> GetViewsAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());

        /// <summary>Returns the virtual column names recognized for email sending.</summary>
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionString, string tableName) => Task.FromResult(new[] { "To", "Cc", "Bcc", "Subject", "Body", "Attachments" }.AsEnumerable());

        /// <summary>Returns a list of procedures/functions (none for SMTP).</summary>
        public Task<IEnumerable<string>> GetProceduresAsync(string connectionString) => Task.FromResult(Enumerable.Empty<string>());
    }
}
