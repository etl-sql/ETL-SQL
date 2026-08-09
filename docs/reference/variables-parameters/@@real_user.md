# @@REAL_USER

Returns the username of the actual authenticated session user.

## Syntax
```sql
SELECT @@REAL_USER;
```

## Remarks
- Always returns the original logged-in identity that initiated the session.
- Unlike `@@CURRENT_USER`, `@@REAL_USER` is unaffected by `EXECUTE AS` or dynamic impersonation.
- Designed for security auditing and tracking who performed an operation under impersonation.

## Example
```sql
-- Audit record logging both target identity and real executor
INSERT INTO audit.activity_logs (Action, TargetIdentity, ExecutorIdentity, OccurredAt)
VALUES ('ViewReport', @@CURRENT_USER, @@REAL_USER, GETDATE());
```

## See Also
- [User Manual](../../guides/onboarding/getting-started.md)
- Related: [@@CURRENT_USER](@@current_user.md), [@@CURRENT_USER_ID](@@current_user_id.md), [@@IS_ADMIN](@@is_admin.md)
