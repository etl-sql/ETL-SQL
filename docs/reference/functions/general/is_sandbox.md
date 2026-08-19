# IS_SANDBOX

Returns a boolean indicating whether the current execution session is running inside an isolated sandbox environment.

## Syntax

```sql
IS_SANDBOX()
```

## Returns

Returns `BOOLEAN` (`TRUE` if executing within a disposable container/sandbox runtime; `FALSE` if running locally or in-process).

## Examples

```sql
IF IS_SANDBOX()
BEGIN
  PRINT 'Running inside isolated sandbox environment.';
END
ELSE
BEGIN
  PRINT 'Running on local or shared host runtime.';
END;
```

```sql
SELECT
  IS_SANDBOX() AS InSandbox,
  CURRENT_TENANT() AS Tenant;
```

## References

- [CURRENT_TENANT](current_tenant.md)
- [TENANT_ID](tenant_id.md)
- [Functions](../README.md)
