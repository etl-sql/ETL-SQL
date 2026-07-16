# @@IS_ADMIN

Returns whether the current execution identity has administrator privileges.

## Syntax
```sql
SELECT @@IS_ADMIN;
```

## Returns
- `1` (or `TRUE`) if the current user is a portal/engine administrator.
- `0` (or `FALSE`) if the user is a standard non-admin account.

## Remarks
- Used in RLS predicates to bypass row filters for administrators, allowing them to see all records without filtering.
- Useful in conditional logic to branch behavior based on administrative roles.

## Example
```sql
-- Bypass row-level security filters for admins
SELECT * FROM sales.orders
WHERE @@IS_ADMIN = 1 OR OwnerId = @@CURRENT_USER;
```

## See Also
- [User Manual](../../guides/getting-started.md)
- Related: [@@CURRENT_USER](@@current_user.md), [@@CURRENT_USER_ID](@@current_user_id.md), [@@REAL_USER](@@real_user.md)
