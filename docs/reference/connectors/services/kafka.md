# KAFKA

Connects to an Apache Kafka message-broker cluster using the Confluent.Kafka driver. `SELECT` pulls
messages from a topic as a table; `INSERT` publishes messages to a topic.

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `BOOTSTRAP_SERVERS` | List of broker host/port pairs (e.g. `localhost:9092,host2:9092`) | Yes (if connection string not provided) |
| `TOPIC` | Target topic name | Yes |
| `GROUP_ID` | Consumer group ID for tracking offsets (default: `etl-sql-group`) | No |
| `AUTO_OFFSET_RESET` | Offset position to start from if no committed offset exists (`Earliest`, `Latest`) | No |
| `TIMEOUT_MS` | Maximum duration in milliseconds to poll the topic for new messages (default: `5000`) | No |
| `MAX_MESSAGES` | Maximum number of messages to consume in a single batch (default: `1000`) | No |
| `SECURITY_PROTOCOL` | Security/transport protocol (`Plaintext`, `SaslPlaintext`, `SaslSsl`, `Ssl`) | No |
| `SASL_MECHANISM` | SASL authentication mechanism (`Plain`, `ScramSha256`, `ScramSha512`) | No |
| `SASL_USERNAME` | Username for SASL authentication | No |
| `SASL_PASSWORD` | Password for SASL authentication (supports `ENC:`) | No |

## Examples

```sql
-- Simple connection using connection string
CREATE CONNECTION kfk_simple AS KAFKA('localhost:9092', TOPIC='orders');

-- Authenticated connection using options
CREATE CONNECTION kfk_auth AS KAFKA(
    BOOTSTRAP_SERVERS = 'kafka-1.corp.local:9093,kafka-2.corp.local:9093',
    TOPIC             = 'customer-events',
    GROUP_ID          = 'etl-sql-sync',
    AUTO_OFFSET_RESET = 'Earliest',
    SECURITY_PROTOCOL = 'SaslSsl',
    SASL_MECHANISM    = 'ScramSha512',
    SASL_USERNAME     = 'etl_agent',
    SASL_PASSWORD     = ENC:U2FsdGVkX1+...,
    TIMEOUT_MS        = 3000,
    MAX_MESSAGES      = 5000
);

-- Read messages from a topic as a table
SELECT Key, Value, Partition, Offset, Timestamp FROM kfk_auth;

-- Publish a message to a topic
INSERT INTO kfk_simple (Key, Value) VALUES ('msg-100', '{"event": "UserSignup", "userId": 42}');
```

## References

- [Service Connectors](README.md)
- [Connectors](../README.md)
