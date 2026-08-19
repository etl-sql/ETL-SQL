# TENANT_ID

Returns the unique identifier of the active tenant context for the current execution session.

## Syntax

```sql
TENANT_ID()
```

## Returns

Returns the active tenant unique ID as `STRING`. Returns `'default'` when executing in single-tenant, workstation, or standalone mode.

## Examples

```sql
DECLARE @tid = TENANT_ID();
PRINT 'Active tenant ID: ' + @tid;
```

```sql
SELECT
  TENANT_ID() AS TenantId,
  CURRENT_TENANT() AS TenantName,
  COUNT(*) AS TotalRecords
FROM src.Customers;
```

## References

- [CURRENT_TENANT](current_tenant.md)
- [IS_SANDBOX](is_sandbox.md)
- [Functions](../README.md)
