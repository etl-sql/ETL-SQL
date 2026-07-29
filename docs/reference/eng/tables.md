# eng.tables

`eng.tables` lists table names discovered through active session connections.

## Query

```sql
SELECT connection_name, table_name, connector_type
FROM eng.tables
ORDER BY connection_name, table_name;
```

## Columns

| Column | Description |
| :--- | :--- |
| `connection_name` | Active connection name. |
| `table_name` | Table name reported by the connection. |
| `connector_type` | Runtime data source type backing the connection. |

## Example

```sql
SELECT table_name
FROM eng.tables
WHERE connection_name = 'sales';
```

## References

- [Engine Catalog](README.md)
- [Connectors](../connectors/README.md)
