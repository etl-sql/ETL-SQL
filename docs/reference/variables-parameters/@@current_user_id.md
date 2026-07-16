# @@CURRENT_USER_ID

Returns the stable, unique identifier of the current execution identity.

## Syntax
```sql
SELECT @@CURRENT_USER_ID;
```

## Remarks
- Returns the stable user ID (e.g. OIDC sub claim or database identity key) associated with the current execution identity.
- Useful when usernames are mutable but a stable reference is required for audit logs or RLS mapping.
- Reflects the impersonated identity if dynamic impersonation is active.

## Example
```sql
-- Join mapping table on stable user ID
SELECT o.*
FROM sales.orders o
JOIN security.user_mappings m ON o.RegionId = m.RegionId
WHERE m.UserId = @@CURRENT_USER_ID;
```

## See Also
- [User Manual](../../guides/getting-started.md)
- Related: [@@CURRENT_USER](@@current_user.md), [@@REAL_USER](@@real_user.md), [@@IS_ADMIN](@@is_admin.md)
