# KAFKA
Connects to Apache Kafka message streams using the Confluent.Kafka driver. Supports publishing rows as JSON messages to a topic or consuming message batches from a topic.

Syntax:
  CREATE CONNECTION <name> AS KAFKA(
    BOOTSTRAP_SERVERS = 'localhost:9092',
    TOPIC             = 'topic_name',
    GROUP_ID          = 'etl-sql-group',
    AUTO_OFFSET_RESET = 'Earliest' | 'Latest',
    TIMEOUT_MS        = 5000,
    MAX_MESSAGES      = 1000,
    SASL_USERNAME     = 'username',
    SASL_PASSWORD     = '<password>',
    SASL_MECHANISM    = 'Plain' | 'ScramSha256' | 'ScramSha512',
    SECURITY_PROTOCOL = 'Plaintext' | 'SaslPlaintext' | 'SaslSsl' | 'Ssl'
  );

Options:
- **BOOTSTRAP_SERVERS** — comma-separated list of broker hosts (required)
- **TOPIC** — default topic name (required)
- **GROUP_ID** — consumer group identifier (default: 'etl-sql-group')
- **AUTO_OFFSET_RESET** — initial offset start: 'Earliest' or 'Latest' (default: 'Latest')
- **TIMEOUT_MS** — poll wait timeout in milliseconds (default: 5000)
- **MAX_MESSAGES** — maximum messages to consume in a batch (default: 1000)
- **SASL_USERNAME** — credentials user name for authentication
- **SASL_PASSWORD** — credentials password for authentication
- **SASL_MECHANISM** — SASL mechanism: Plain, ScramSha256, or ScramSha512 (default: Plain)
- **SECURITY_PROTOCOL** — security protocol: Plaintext, SaslPlaintext, SaslSsl, or Ssl (default: Plaintext)

### How Reading and Writing Works
- **Reading (Consuming)**: Reads messages from the specified topic. Since Kafka is a continuous stream, the connector consumes in a batch-bounded mode, stopping when it reaches `MAX_MESSAGES` or after `TIMEOUT_MS` milliseconds of no new messages.
- **Writing (Producing)**: Publishes every row from the source table as a JSON-serialized message to the target topic.

### Examples

```sql
-- Consume events from a topic
CREATE CONNECTION KafkaEvents AS KAFKA(
  BOOTSTRAP_SERVERS = 'kafka-broker.corp.local:9092',
  TOPIC             = 'telemetry',
  GROUP_ID          = 'etl-sql-telemetry-consumer',
  AUTO_OFFSET_RESET = 'Earliest',
  TIMEOUT_MS        = 10000,
  MAX_MESSAGES      = 5000
);

SELECT offset, key, value, timestamp
  INTO #telemetry_staging
  FROM KafkaEvents;

-- Publish events to another topic
CREATE CONNECTION KafkaAlerts AS KAFKA(
  BOOTSTRAP_SERVERS = 'kafka-broker.corp.local:9092',
  TOPIC             = 'alerts'
);

SELECT 'ALERT_CRITICAL' AS severity, 'CPU Usage high' AS message
  INTO KafkaAlerts
  FROM #telemetry_staging
  WHERE cpu_utilization > 0.90;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
