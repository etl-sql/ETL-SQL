# eng.connection_config

`eng.connection_config` exposes redacted configuration options for active session connections.

## Query

```sql
SELECT connection_name, option, value
FROM eng.connection_config
WHERE connection_name = 'sales';
```

## Columns

| Column | Description |
| :--- | :--- |
| `connection_name` | Active connection name. |
| `option` | Connector configuration option name. |
| `value` | Redacted option value returned by the connector configuration surface. |

## Example

```sql
SELECT option, value
FROM eng.connection_config
WHERE connection_name = 'sales'
ORDER BY option;
```

## References

- [Engine Catalog](README.md)
- [Connectors](../connectors/README.md)
