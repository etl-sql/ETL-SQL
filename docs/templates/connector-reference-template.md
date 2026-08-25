# CONNECTOR_NAME Connector

> **Page-type: Reference — connector**
> Owns: both authentication patterns, mutually exclusive options, security notes, examples, and
> troubleshooting for one connector.
> Links to (does not restate): configuration pages for global timeout defaults.
> Required sections: Syntax, Required Options, Authentication, Mutually Exclusive Options,
> Security Notes, Examples, Troubleshooting, References.

One-sentence description of the connector and its intended use.

## Syntax

```sql
CREATE CONNECTION name AS CONNECTOR_NAME(
  OPTION = 'value'
);
```

## Required Options

- **OPTION** — Description.

## Optional Options

- **OPTION** — Description, default, and valid values.

## Authentication

Document **both** supported authentication patterns explicitly. For each one, show the full
`CREATE CONNECTION` syntax.

### Pattern A — Password

```sql
CREATE CONNECTION name AS CONNECTOR_NAME(
  HOST = 'host',
  PASSWORD = 'SECRET:MySecret'
);
```

### Pattern B — Key File / Certificate

```sql
CREATE CONNECTION name AS CONNECTOR_NAME(
  HOST = 'host',
  KEY_FILE = '/allowed/path/to/key.pem'
);
```

## Mutually Exclusive Options

Call out options that cannot be combined and describe what happens if they are.

- **PASSWORD** and **KEY_FILE** are mutually exclusive — use one per connection, not both.

## Security Notes

Document path resolution (`context.ResolvePath()` is always called before any file I/O),
secret handling (`SECRET:` / `ENC:` references), encryption, redaction, and WHAT_IF behavior
where relevant. Never log, print, or serialize connection strings or raw secret values.

## Examples

```sql
CREATE CONNECTION source AS CONNECTOR_NAME(OPTION = 'value');

SELECT *
FROM source.TableName;
```

## Troubleshooting

- **Symptom** — Cause and fix.

## References

- [Connector Reference](../reference/connectors/README.md)
- [Connector Standards](../architecture/standards/connectors-standards.md)
