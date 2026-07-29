# eng.connections

`eng.connections` lists active connections in the current execution session.

## Query

```sql
SELECT connection_name, connector_type, details
FROM eng.connections
ORDER BY connection_name;
```

## Columns

| Column | Description |
| :--- | :--- |
| `connection_name` | Active connection name. |
| `connector_type` | Runtime data source type backing the connection. |
| `details` | Connector-provided display details. |

## Example

```sql
SELECT connection_name
INTO #file_connections
FROM eng.connections
WHERE connector_type LIKE '%FlatFile%';
```

## References

- [Engine Catalog](README.md)
- [Connectors](../connectors/README.md)
