# CURRENT_TENANT

Returns the display name or identifier of the active tenant context for the current execution session.

## Syntax

```sql
CURRENT_TENANT()
```

## Returns

Returns the active tenant display name or slug as `STRING`. Returns `'default'` when executing in single-tenant, workstation, or standalone mode.

## Examples

```sql
DECLARE @tenant = CURRENT_TENANT();
PRINT 'Executing under tenant: ' + @tenant;
```

```sql
SELECT
  CURRENT_TENANT() AS TenantName,
  COUNT(*) AS OrderCount
FROM src.Orders;
```

## References

- [TENANT_ID](tenant_id.md)
- [IS_SANDBOX](is_sandbox.md)
- [Functions](../README.md)
