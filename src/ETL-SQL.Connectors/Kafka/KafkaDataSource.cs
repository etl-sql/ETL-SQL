using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Connectors.Shared;

namespace ETL_SQL.Connectors.Kafka
{
    public class KafkaDataSource : IDataSource
    {
        private readonly string _bootstrapServers;
        private readonly string _topic;
        private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger _logger;
        private readonly IExecutionContext? _context;
        
        private IConsumer<string, string>? _consumer;
        private IProducer<string, string>? _producer;

        public KafkaDataSource(IExecutionContext context, string bootstrapServers, string topic, Dictionary<string, string>? options = null, IConsumer<string, string>? consumer = null, IProducer<string, string>? producer = null)
        {
            _context = context;
            _logger = context.Logger;
            _bootstrapServers = bootstrapServers;
            _topic = topic;
            _consumer = consumer;
            _producer = producer;

            if (options != null)
            {
                foreach (var kv in options)
                {
                    _options[kv.Key] = kv.Value;
                }
            }
        }

        public string Path => _topic;
        public Dictionary<string, string>? Options => _options;
        public string ConnectorType => "KAFKA";

        private IConsumer<string, string> GetConsumer(ConsumerConfig config)
        {
            if (_consumer != null) return _consumer;
            _consumer = new ConsumerBuilder<string, string>(config).Build();
            return _consumer;
        }

        private IProducer<string, string> GetProducer(ProducerConfig config)
        {
            if (_producer != null) return _producer;
            _producer = new ProducerBuilder<string, string>(config).Build();
            return _producer;
        }

        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) =>
            ConnectorExceptionWrapper.WrapAsync(ReadBatchesCore(batchSize), "Kafka", ShouldWrapProviderException);

        private async IAsyncEnumerable<DataTable> ReadBatchesCore(int batchSize)
        {
            var config = GetConsumerConfig();
            var consumer = GetConsumer(config);

            try
            {
                consumer.Subscribe(_topic);
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Kafka", ex);
            }

            var columns = new[] { "Partition", "Offset", "Key", "Value", "Timestamp", "Headers" };
            var table = new DataTable();
            table.SetColumns(columns);

            int maxMessages = _options.TryGetValue("MAX_MESSAGES", out var m) && int.TryParse(m, out var maxVal) ? maxVal : 1000;
            int timeoutMs = _options.TryGetValue("TIMEOUT_MS", out var t) && int.TryParse(t, out var timeoutVal) ? timeoutVal : 5000;

            int messageCount = 0;
            var startTime = DateTime.UtcNow;

            while (messageCount < maxMessages && (DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    // Poll consumer
                    result = consumer.Consume(TimeSpan.FromMilliseconds(100));
                }
                catch (Exception ex) when (ShouldWrapProviderException(ex))
                {
                    throw ConnectorExceptionWrapper.Wrap("Kafka", ex);
                }

                if (result == null || result.IsPartitionEOF)
                {
                    if ((DateTime.UtcNow - startTime).TotalMilliseconds > 500 && table.Rows.Count > 0)
                    {
                        break;
                    }
                    if (result == null)
                    {
                        await Task.Delay(50);
                    }
                    continue;
                }

                string headersJson = "[]";
                if (result.Message.Headers != null && result.Message.Headers.Count > 0)
                {
                    var headerList = result.Message.Headers
                        .Select(h => new { Key = h.Key, Value = System.Text.Encoding.UTF8.GetString(h.GetValueBytes()) });
                    headersJson = System.Text.Json.JsonSerializer.Serialize(headerList);
                }

                var row = table.NewRow();
                row["Partition"] = result.Partition.Value;
                row["Offset"] = result.Offset.Value;
                row["Key"] = result.Message.Key;
                row["Value"] = result.Message.Value;
                row["Timestamp"] = result.Message.Timestamp.UtcDateTime;
                row["Headers"] = headersJson;

                await table.AddRowAsync(row);
                messageCount++;

                if (table.Rows.Count >= batchSize)
                {
                    yield return table;
                    table = new DataTable();
                    table.SetColumns(columns);
                }
            }

            if (table.Rows.Count > 0)
            {
                yield return table;
            }
        }

        public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false)
        {
            if (_context != null && _context.IsWhatIf) return;

            var config = GetProducerConfig();
            var producer = GetProducer(config);

            try
            {
                await foreach (var batch in batches)
                {
                    foreach (var row in batch.Rows)
                    {
                        string? key = null;
                        string value;

                        if (batch.ColumnNames.Contains("Key"))
                        {
                            key = row["Key"]?.ToString();
                        }

                        if (batch.ColumnNames.Contains("Value"))
                        {
                            value = row["Value"]?.ToString() ?? "";
                        }
                        else
                        {
                            var rowDict = batch.ColumnNames.ToDictionary(c => c, c => row[c]);
                            value = System.Text.Json.JsonSerializer.Serialize(rowDict);
                        }

                        var message = new Message<string, string>
                        {
                            Key = key!,
                            Value = value
                        };

                        await producer.ProduceAsync(_topic, message);
                    }
                }
            }
            catch (Exception ex) when (ShouldWrapProviderException(ex))
            {
                throw ConnectorExceptionWrapper.Wrap("Kafka", ex);
            }
        }

        private ConsumerConfig GetConsumerConfig()
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = _bootstrapServers,
                GroupId = _options.GetValueOrDefault("GROUP_ID", "etl-sql-group"),
                AutoOffsetReset = _options.GetValueOrDefault("AUTO_OFFSET_RESET", "Earliest").Equals("Latest", StringComparison.OrdinalIgnoreCase) 
                    ? AutoOffsetReset.Latest 
                    : AutoOffsetReset.Earliest,
                EnableAutoCommit = true
            };

            ApplySaslConfig(config);
            return config;
        }

        private ProducerConfig GetProducerConfig()
        {
            var config = new ProducerConfig
            {
                BootstrapServers = _bootstrapServers
            };

            ApplySaslConfig(config);
            return config;
        }

        private void ApplySaslConfig(ClientConfig config)
        {
            string username = _options.GetValueOrDefault("SASL_USERNAME", "");
            string password = _options.GetValueOrDefault("SASL_PASSWORD", "");

            if (password.StartsWith("ENC:") && _context != null)
            {
                password = _context.DecryptValue(password) ?? "";
            }

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                config.SaslUsername = username;
                config.SaslPassword = password;
                
                string mechanism = _options.GetValueOrDefault("SASL_MECHANISM", "Plain");
                config.SaslMechanism = mechanism.Equals("ScramSha512", StringComparison.OrdinalIgnoreCase) 
                    ? SaslMechanism.ScramSha512 
                    : mechanism.Equals("ScramSha256", StringComparison.OrdinalIgnoreCase)
                    ? SaslMechanism.ScramSha256
                    : SaslMechanism.Plain;

                string protocol = _options.GetValueOrDefault("SECURITY_PROTOCOL", "SaslPlaintext");
                config.SecurityProtocol = protocol.Equals("SaslSsl", StringComparison.OrdinalIgnoreCase)
                    ? SecurityProtocol.SaslSsl
                    : protocol.Equals("Ssl", StringComparison.OrdinalIgnoreCase)
                    ? SecurityProtocol.Ssl
                    : protocol.Equals("Plaintext", StringComparison.OrdinalIgnoreCase)
                    ? SecurityProtocol.Plaintext
                    : SecurityProtocol.SaslPlaintext;
            }
        }

        public Task<IEnumerable<string>> GetColumnsAsync()
        {
            return Task.FromResult<IEnumerable<string>>(new[] { "Partition", "Offset", "Key", "Value", "Timestamp", "Headers" });
        }

        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }

        public IDataSource WithTable(string tableName)
        {
            return new KafkaDataSource(_context!, _bootstrapServers, tableName, _options, _consumer, _producer);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                _consumer?.Close();
                _consumer?.Dispose();
            }
            catch { }
            try
            {
                _producer?.Flush();
                _producer?.Dispose();
            }
            catch { }

            _consumer = null;
            _producer = null;
            return ValueTask.CompletedTask;
        }

        private static bool ShouldWrapProviderException(Exception ex) =>
            ex is KafkaException or InvalidOperationException;
    }
}
