# Portal Service Administration
Issue service-level control commands to the portal process inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  RESTART PORTAL;
  SHUTDOWN PORTAL;
END;
```

## Examples
```sql
-- Request a clean restart of the portal service
EXECUTE portal BEGIN
  RESTART PORTAL;
END;

-- Shut down the portal process (no automatic restart)
EXECUTE portal BEGIN
  SHUTDOWN PORTAL;
END;
```

## Notes
- `RESTART PORTAL` requests a clean process stop. The portal will finish any response already in flight, then exit. The external supervisor — Windows Service manager or systemd — is responsible for restarting the process automatically. If no supervisor is configured, the process exits and does not come back online.
- `SHUTDOWN PORTAL` terminates the portal process without signaling the supervisor to restart. Use this for planned maintenance windows where you do not want the process to restart automatically.
- Both commands require `Portal:AllowServiceControl = true` in the portal's `appsettings.json`. Issuing either command without this setting returns an error and has no effect.
- Both commands require the ADMIN portal role. Non-admin users receive a permission error.
- In-flight HTTP requests that have not yet received a response are dropped immediately when the shutdown sequence begins. Active refresh sessions and dataset evaluations are also interrupted.
- For safe restarts during active use, prefer scheduling a maintenance window and coordinating with active users before issuing these commands.
- See: PORTAL_USER, PORTAL_SHOW

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
