# eng.capabilities
<!-- ShowCapabilitiesStatement -->

`eng.capabilities` lists server-provisioned runtime capabilities and bind-mounted assets available to the current session or sandbox execution.

## Query

```sql
SELECT name, size_bytes, mounted_path, is_available, last_modified_utc
FROM eng.capabilities
ORDER BY name;
```

## Columns

| Column | Description |
| :--- | :--- |
| `name` | The name or identifier of the provisioned capability. |
| `size_bytes` | The size of the capability payload in bytes. |
| `mounted_path` | Absolute filesystem path where the capability asset is mounted. |
| `is_available` | `TRUE` if the capability is accessible and active for the session. |
| `last_modified_utc` | Timestamp when the capability was created or last updated in UTC. |

## Example

```sql
SELECT name, mounted_path
FROM eng.capabilities
WHERE is_available = TRUE;
```

## References

- [Engine Catalog](README.md)
