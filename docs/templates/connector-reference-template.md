# CONNECTOR_NAME Connector

One-sentence description of the connector and its intended use.

## Syntax

```sql
CREATE CONNECTION name AS CONNECTOR_NAME(
  OPTION = 'value'
);
```

## Required Options

- **OPTION** - Description.

## Optional Options

- **OPTION** - Description, default, and valid values.

## Authentication

Describe each supported authentication pattern.

## Mutually Exclusive Options

Call out options that cannot be used together.

## Security Notes

Document path resolution, secret handling, encryption, redaction, and WHAT_IF behavior where relevant.

## Examples

```sql
CREATE CONNECTION source AS CONNECTOR_NAME(OPTION = 'value');

SELECT *
FROM source.TableName;
```

## Troubleshooting

- **Symptom** - Cause and fix.

## References

- [Connector Reference](../reference/connectors/README.md)

