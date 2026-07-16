# CONNECTION_PROPERTY

Looks up an active connection by name and retrieves its metadata properties.

## Syntax

```sql
CONNECTION_PROPERTY(conn_name, prop_name)
```

## Parameters

- **conn_name** - Name of the declared connection.
- **prop_name** - Property name to retrieve, such as `Path`, `ConnectorType`, `Host`, `Database`, or `User`.

## Returns

Returns the connection property value as `STRING`, or `NULL` when the connection or property does not exist.

## Null Behavior

Returns `NULL` when either argument is `NULL`.

## Security Notes

Sensitive credential-like properties are masked with `********`, including `PASSWORD`, `APIKEY`, `SECRET`, `TOKEN`, and `KEYFILE`.

## Examples

```sql
SELECT CONNECTION_PROPERTY('my_mssql_conn', 'database') AS database_name;
```

```sql
SELECT CONNECTION_PROPERTY('landing_files', 'Path') AS landing_path;
```

## References

- [Standard Library](../standard-library.md)
- [ENV](../general/env.md)
