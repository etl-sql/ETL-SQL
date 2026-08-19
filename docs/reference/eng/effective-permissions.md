# eng.effective_permissions
<!-- ShowEffectivePermissionsStatement -->

`eng.effective_permissions` describes the computed access rights, roles, and administrative scopes for the active principal across governance resources.

## Query

```sql
SELECT principal_key, actor_identity, role, group_id, scope, can_create, can_mutate, can_execute, source
FROM eng.effective_permissions
ORDER BY scope, role;
```

## Columns

| Column | Description |
| :--- | :--- |
| `principal_key` | Unique key or identifier of the security principal. |
| `actor_identity` | Authenticated user, service account, or token identity. |
| `role` | Assigned administrative or catalog role. |
| `group_id` | Associated directory or OIDC group identifier. |
| `scope` | Resource scope or object boundary for the permission grant. |
| `can_create` | `TRUE` if the principal is authorized to create objects in this scope. |
| `can_mutate` | `TRUE` if the principal is authorized to modify or delete objects. |
| `can_execute` | `TRUE` if the principal is authorized to run scripts or jobs. |
| `source` | Authority source establishing the permission grant. |

## Example

```sql
SELECT scope, role, can_execute
FROM eng.effective_permissions
WHERE can_execute = TRUE;
```

## References

- [Engine Catalog](README.md)
