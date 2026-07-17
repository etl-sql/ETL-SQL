# SHOW ACTIVE SESSIONS
Displays unrevoked, unexpired portal refresh sessions.

## Syntax
```sql
SHOW ACTIVE SESSIONS [INTO #table];
```

## Parameters
- **INTO #table** — Optional. Captures the result set into a temp table for programmatic use.

## Returns
A result set with session ID, user, start time, last activity, and expiration for each active session.

## Example
```sql
EXECUTE portal BEGIN
    SHOW ACTIVE SESSIONS;

    -- Capture and audit
    SHOW ACTIVE SESSIONS INTO #sess;
    SELECT SessionId, UserName, StartTime, Expiration FROM #sess;
END;
```

## Notes
- Must be executed within an `EXECUTE portal BEGIN...END` block.
- Shows only sessions that have not been revoked and have not expired.
- Useful for monitoring active portal users and auditing session lifetime.

## References
- [SHOW Commands](README.md)
