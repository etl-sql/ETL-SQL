# Portal SMTP Connections
Register a mail relay in the portal's governed connection catalog inside an `EXECUTE portal` block. SMTP is an ordinary connector, so it uses the ordinary connector grammar — there is no separate SMTP statement family. Subscriptions and alerts reference the connection by alias (`AT <alias>`), so delivery never embeds mail credentials in scripts or job definitions.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE CONNECTION alias AS SMTP(
    HOST         = 'smtp.host',          -- required
    PORT         = 587,                  -- optional, default 587
    USERNAME     = 'user',               -- optional
    PASSWORD     = 'SECRET:name',        -- optional; must be a SECRET: reference
    DEFAULT_FROM = 'from@corp.com',      -- optional
    USE_SSL      = TRUE                  -- optional, default TRUE
  );

  DROP CONNECTION [IF EXISTS] alias;
END;
```

## The password is a reference, not a value

The catalog stores `SECRET:name` **references** and rejects literal credentials, so registering an authenticated relay is two steps: store the value in the portal secret store, then reference it by name.

```sql
-- 1. Store the credential (admin API, or the Secrets page in the portal UI)
--    PUT /api/admin/secrets/corporate_smtp_password   { "value": "..." }

-- 2. Reference it from the connection
EXECUTE portal BEGIN
  CREATE CONNECTION corporate AS SMTP(
    HOST         = 'smtp.corp.local',
    PORT         = 2525,
    USERNAME     = 'mailer',
    PASSWORD     = 'SECRET:corporate_smtp_password',
    DEFAULT_FROM = 'reports@corp.local',
    USE_SSL      = TRUE
  );
END;
```

The portal never holds the plaintext: the reference is passed through to the engine, which resolves it when the connection is opened.

## Examples
```sql
-- Minimal definition for an unauthenticated internal relay
EXECUTE portal BEGIN
  CREATE CONNECTION internal_relay AS SMTP(HOST = 'relay.corp.local');
END;

-- A subscription delivers through the named connection
EXECUTE portal BEGIN
  CREATE SUBSCRIPTION 'DailySales'
    FOR REPORT '/Finance/MonthlySales'
    DELIVER TO 'john.doe'
    SCHEDULE '0 8 * * MON'
    FORMAT PDF
    AT corporate;
END;

-- List configured connections (credential references are masked)
EXECUTE portal BEGIN
  SHOW SMTP CONNECTIONS INTO #smtp;
END;
SELECT * FROM #smtp;

-- Remove a connection
EXECUTE portal BEGIN
  DROP CONNECTION IF EXISTS corporate;
END;
```

## Notes
- Requires the Admin role on the portal connection.
- `HOST` is required; `PORT` defaults to 587 and `USE_SSL` defaults to TRUE.
- `PASSWORD` must be a `SECRET:name` reference. A literal value is refused by the catalog rather than stored — the credential lives in the secret store, and the connection entry only points at it.
- The alias is an identifier and is the name subscriptions and alerts use in their `AT <alias>` clause.
- Entries inherit the catalog's governance: per-connection use ACLs, ownership, an audit trail, and a usage ledger.
- `SHOW SMTP CONNECTIONS` currently lists the whole connection catalog rather than SMTP alone, and is being replaced by a filter over `eng.connections`.
- See: PORTAL_SUBSCRIPTION, PORTAL_ALERT, PORTAL_SHOW

### Migrating from `CREATE SMTP CONNECTION`
The Portal-only form is removed. It differed from the connector grammar in three ways at once, so the parser rejects it with the exact replacement rather than a generic syntax error:

| Retired | Canonical |
| :--- | :--- |
| `CREATE SMTP CONNECTION 'alias' WITH (...)` | `CREATE CONNECTION alias AS SMTP(...)` |
| string-literal alias | identifier |
| `FROM_ADDRESS` | `DEFAULT_FROM` |
| `PASSWORD = ENC:...` (encrypted value) | `PASSWORD = 'SECRET:name'` (reference) |
| `DROP SMTP CONNECTION 'alias'` | `DROP CONNECTION [IF EXISTS] alias` |

Existing passwords are **not** migrated: store each one in the secret store and re-reference it.

References:
- [Data Connectors](../../administration/platform/README.md)
- [Portal Admin Commands](README.md)
