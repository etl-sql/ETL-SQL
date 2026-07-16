# @@CURRENT_USER

Returns the username of the current execution identity.

## Syntax
```sql
SELECT @@CURRENT_USER;
```

## Remarks
- If impersonation or `EXECUTE AS` is active, `@@CURRENT_USER` returns the identity of the impersonated user.
- To obtain the original logged-in user regardless of impersonation, use `@@REAL_USER`.
- Typically used in Row-Level Security (RLS) predicates to filter rows based on ownership or assignment.

## Example
```sql
-- Secure row filtering query
SELECT * FROM sales.orders
WHERE OwnerId = @@CURRENT_USER;
```

## See Also
- [User Manual](../../guides/getting-started.md)
- Related: [@@CURRENT_USER_ID](@@current_user_id.md), [@@REAL_USER](@@real_user.md), [@@IS_ADMIN](@@is_admin.md)
