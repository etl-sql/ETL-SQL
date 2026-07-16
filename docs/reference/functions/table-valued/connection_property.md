# CONNECTION_PROPERTY
Looks up an active connection by name and retrieves its metadata properties.

**Category:** System

## Syntax
```sql
CONNECTION_PROPERTY(conn_name, prop_name)
```

## Parameters
| Parameter | Type | Description |
| :--- | :--- | :--- |
| `conn_name` | `VARCHAR` / `STRING` | The name of the declared connection |
| `prop_name` | `VARCHAR` / `STRING` | The name of the property to retrieve (e.g. `Path`, `ConnectorType`, `Host`, `Database`, `User`) |

## Returns
`STRING` — The unmasked value of the connection property. To prevent accidental security leaks, sensitive credentials (like `PASSWORD`, `APIKEY`, `SECRET`, `TOKEN`, `KEYFILE`) are masked with `********`. Returns `NULL` if the connection or property does not exist.

## Example
```sql
SELECT CONNECTION_PROPERTY('my_mssql_conn', 'database'); -- → 'prod_db'
```

## See Also
- [Standard Library — §9.1 System Functions](../../../guides/getting-started.md#91-system-functions)
- Related: [`ENV`](../general/env.md)
