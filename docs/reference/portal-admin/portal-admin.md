# Portal Service Administration
Issue service-level control commands to the portal process inside an `EXECUTE portal` block.

## Syntax
```sql
EXECUTE portal BEGIN
  RESTART PORTAL;
  SHUTDOWN PORTAL;
  EXPORT PORTAL CONFIGURATION TO '<path>';
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

-- Export all declarative configuration to a bootstrap script
EXECUTE portal BEGIN
  EXPORT PORTAL CONFIGURATION TO 'portal_bootstrap.txt';
END;
```

## Notes
- `RESTART PORTAL` requests a clean process stop. The portal will finish any response already in flight, then exit. The external supervisor — Windows Service manager or systemd — is responsible for restarting the process automatically. If no supervisor is configured, the process exits and does not come back online.
- `SHUTDOWN PORTAL` terminates the portal process without signaling the supervisor to restart. Use this for scheduled maintenance windows where you do not want the process to restart automatically.
- `EXPORT PORTAL CONFIGURATION TO '<path>'` writes the portal's entire declarative configuration as an idempotent, replayable bootstrap script by logical name (not database ID). It exports groups, users, memberships, folders, ACLs, SMTP connections, report publications, dataset metadata/grants, subscriptions, and alerts.
- Secrets are **never** exported: password-bearing fields carry `${...}` placeholders which you replace before import (ideally using `ENV('NAME')` or `ENC:` configurations).
- The exported configuration script is also available from `GET /api/admin/configuration/export` and records an `EXPORT_PORTAL_CONFIGURATION` audit event.
- Both service control and configuration export require the ADMIN portal role. Non-admin users receive a permission error.
- Service control commands require `Portal:AllowServiceControl = true` in the portal's `appsettings.json`. Issuing either command without this setting returns an error and has no effect.
- In-flight HTTP requests that have not yet received a response are dropped immediately when the shutdown sequence begins. Active refresh sessions and dataset evaluations are also interrupted.
- For safe restarts during active use, prefer scheduling a maintenance window and coordinating with active users before issuing these commands.
- See: PORTAL_USER, PORTAL_SHOW, EXPORT

References:
- [Data Connectors](../../guides/administration.md)
- [Grammar](../../guides/getting-started.md)

