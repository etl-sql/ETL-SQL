# eng.variables

`eng.variables` lists current session variables. Sensitive and secret values are masked unless the execution context explicitly allows showing passwords.

## Query

```sql
SELECT variable_name, value, data_type, scope, is_sensitive
FROM eng.variables
ORDER BY variable_name;
```

## Columns

| Column | Description |
| :--- | :--- |
| `variable_name` | Variable name. |
| `value` | Variable value, or masked text for sensitive values. |
| `data_type` | Runtime data type inferred from the value. |
| `scope` | Variable scope. Current rows are emitted as `Global`. |
| `is_sensitive` | `TRUE` when the variable metadata marks the value as sensitive or secret. |

## Example

```sql
SELECT variable_name, data_type
FROM eng.variables
WHERE is_sensitive = FALSE;
```

## References

- [Engine Catalog](README.md)
- [Variables and Parameters](../variables-parameters/README.md)
