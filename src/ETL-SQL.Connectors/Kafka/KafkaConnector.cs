using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using ETL_SQL.Data;
using ETL_SQL.Common;

namespace ETL_SQL.Connectors.Kafka
{
    public class KafkaConnector : IConnector
    {
        public string Name => "KAFKA";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public bool IsFileBased => false;

        public async Task<string> GetVersionAsync(IExecutionContext context, string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                return "Apache Kafka Connector v1.0 (Offline - No servers specified)";
            }

            var host = GetHost(connectionString);
            if (!string.IsNullOrEmpty(host))
            {
                context.SecurityService.ValidateHost(host);
            }

            try
            {
                var config = new AdminClientConfig
                {
                    BootstrapServers = connectionString
                };

                using var adminClient = new AdminClientBuilder(config).Build();
                var metadata = await Task.Run(() => adminClient.GetMetadata(TimeSpan.FromSeconds(5)));
                
                return $"Apache Kafka Connector v1.0 (Connected - Brokers: {metadata.Brokers.Count}, Topics: {metadata.Topics.Count})";
            }
            catch (Exception ex)
            {
                throw Shared.ConnectorExceptionWrapper.Wrap("Kafka", ex);
            }
        }

        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();

        public string GetHelp() =>
            "Kafka Connector: Publishes to or consumes from Apache Kafka topics.\n" +
            "Options:\n" +
            "  BOOTSTRAP_SERVERS / SERVERS: Broker hosts list (e.g. localhost:9092)\n" +
            "  TOPIC: Topic name (required)\n" +
            "  GROUP_ID: Consumer group ID (default: etl-sql-group)\n" +
            "  AUTO_OFFSET_RESET: Offset start ('Earliest' or 'Latest')\n" +
            "  TIMEOUT_MS: Maximum wait time in milliseconds during read loops (default: 5000)\n" +
            "  MAX_MESSAGES: Maximum message count to consume in a batch (default: 1000)\n" +
            "  SASL_USERNAME: Credentials user name\n" +
            "  SASL_PASSWORD: Credentials password\n" +
            "  SASL_MECHANISM: Plain, ScramSha256, ScramSha512 (default: Plain)\n" +
            "  SECURITY_PROTOCOL: Plaintext, SaslPlaintext, SaslSsl, Ssl (default: Plaintext)\n";

        public Dictionary<string, string[]> GetSupportedOptions() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "BOOTSTRAP_SERVERS", Array.Empty<string>() },
            { "SERVERS", Array.Empty<string>() },
            { "TOPIC", Array.Empty<string>() },
            { "GROUP_ID", Array.Empty<string>() },
            { "AUTO_OFFSET_RESET", new[] { "Earliest", "Latest" } },
            { "TIMEOUT_MS", Array.Empty<string>() },
            { "MAX_MESSAGES", Array.Empty<string>() },
            { "SASL_USERNAME", Array.Empty<string>() },
            { "SASL_PASSWORD", Array.Empty<string>() },
            { "SASL_MECHANISM", new[] { "Plain", "ScramSha256", "ScramSha512" } },
            { "SECURITY_PROTOCOL", new[] { "Plaintext", "SaslPlaintext", "SaslSsl", "Ssl" } }
        };

        public Dictionary<string, string[]> GetOptionValues() => new(StringComparer.OrdinalIgnoreCase)
        {
            { "AUTO_OFFSET_RESET", new[] { "Earliest", "Latest" } },
            { "SASL_MECHANISM", new[] { "Plain", "ScramSha256", "ScramSha512" } },
            { "SECURITY_PROTOCOL", new[] { "Plaintext", "SaslPlaintext", "SaslSsl", "Ssl" } }
        };

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            string? topic = null;
            if (options != null)
            {
                options.TryGetValue("TOPIC", out topic);
            }

            // Egress Security Hardening: Validate bootstrap server hosts
            string connStr = connectionString;
            if (string.IsNullOrEmpty(connStr) && options != null)
            {
                connStr = BuildConnectionString(options);
            }

            if (!string.IsNullOrEmpty(connStr))
            {
                var servers = connStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var server in servers)
                {
                    var parts = server.Split(':');
                    var host = parts[0].Trim();
                    if (!string.IsNullOrEmpty(host))
                    {
                        context.SecurityService.ValidateHost(host);
                    }
                }
            }

            return new KafkaDataSource(context, connectionString, topic ?? "default-topic", options);
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            return properties.GetValueOrDefault("BOOTSTRAP_SERVERS", properties.GetValueOrDefault("SERVERS", ""));
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            string connStr = connectionString;
            if (string.IsNullOrEmpty(connStr) && options != null)
            {
                connStr = BuildConnectionString(options);
            }
            if (string.IsNullOrEmpty(connStr)) return null;
            var servers = connStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (servers.Length > 0)
            {
                var parts = servers[0].Split(':');
                return parts[0].Trim();
            }
            return null;
        }
    }
}
