# eng.tenant_context
<!-- ShowTenantContextStatement -->

`eng.tenant_context` reports verified tenant identity, isolation mode, storage grants, and root path boundaries for the active execution environment.

## Query

```sql
SELECT tenant_id, run_id, is_sandboxed, storage_grants_count, capability_root
FROM eng.tenant_context;
```

## Columns

| Column | Description |
| :--- | :--- |
| `tenant_id` | Verified tenant identifier derived from server credentials. |
| `run_id` | Current execution or admission run ID. |
| `is_sandboxed` | `TRUE` if running inside a dedicated or hardened sandbox container. |
| `storage_grants_count` | Number of authorized storage path grants for this tenant. |
| `capability_root` | Root filesystem location for tenant capability mounts. |

## Example

```sql
SELECT tenant_id, is_sandboxed
FROM eng.tenant_context;
```

## References

- [Engine Catalog](README.md)
