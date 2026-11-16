using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using ETL_SQL.Common;
using ETL_SQL.Data;

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

            // Every broker is a destination the client may connect to, not only the first.
            EnforceBrokerHosts(context, connectionString);

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
            "  BOOTSTRAP_SERVERS: Broker hosts list (e.g. localhost:9092)\n" +
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

            EnforceBrokerHosts(context, connStr);

            return new KafkaDataSource(context, connStr, topic ?? "default-topic", options);
        }

        private static void EnforceBrokerHosts(IExecutionContext context, string? servers)
        {
            foreach (var host in BrokerHosts(servers))
                ETL_SQL.Core.Governance.ConnectorPolicyAuthorizer.EnforceEnterpriseHost(context, host);
        }

        /// <summary>
        /// The host of every broker in a <c>BOOTSTRAP_SERVERS</c> list (<c>host:port,host:port</c>).
        /// A bracketed IPv6 literal (<c>[::1]:9092</c>) keeps its colons.
        /// </summary>
        internal static IReadOnlyList<string> BrokerHosts(string? servers)
        {
            if (string.IsNullOrWhiteSpace(servers)) return Array.Empty<string>();
            var hosts = new List<string>();
            foreach (var raw in servers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string host;
                if (raw.StartsWith('['))
                {
                    var close = raw.IndexOf(']');
                    host = close > 0 ? raw[1..close] : raw.TrimStart('[');
                }
                else
                {
                    var colon = raw.IndexOf(':');
                    host = colon >= 0 ? raw[..colon] : raw;
                }

                host = host.Trim();
                if (host.Length > 0) hosts.Add(host);
            }
            return hosts;
        }

        public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetTablesAsync instead.");
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => throw new NotSupportedException("Use IDataSource.GetViewsAsync instead.");
        public Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName) => throw new NotSupportedException("Use IDataSource.GetColumnsAsync instead.");
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public string BuildConnectionString(Dictionary<string, string> properties)
        {
            return properties.GetValueOrDefault("BOOTSTRAP_SERVERS", "");
        }

        public string? GetHost(string connectionString, Dictionary<string, string>? options = null)
        {
            string connStr = connectionString;
            if (string.IsNullOrEmpty(connStr) && options != null)
            {
                connStr = BuildConnectionString(options);
            }
            // IConnector.GetHost reports one host, so the engine's pre-create check sees only the
            // first broker; CreateDataSource and GetVersionAsync enforce every broker themselves.
            var hosts = BrokerHosts(connStr);
            return hosts.Count > 0 ? hosts[0] : null;
        }
    }
}
