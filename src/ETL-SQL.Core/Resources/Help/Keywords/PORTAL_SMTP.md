# Portal SMTP Connections
Manage portal-stored SMTP credentials inside an `EXECUTE portal` block. Subscriptions and alerts reference these connections by alias (`AT <alias>`), so delivery never embeds mail credentials in scripts or job definitions.

## Syntax
```sql
EXECUTE portal BEGIN
  CREATE SMTP CONNECTION 'alias' WITH (
    HOST         = 'smtp.host',        -- required
    PORT         = 587,                -- optional, default 587
    USERNAME     = 'user',             -- optional
    PASSWORD     = ENC:...,            -- optional; expression (ENC:/variables OK)
    FROM_ADDRESS = 'from@corp.com',    -- optional
    USE_SSL      = TRUE                -- optional, default TRUE
  );

  SHOW SMTP CONNECTIONS [INTO #smtp];
  DROP SMTP CONNECTION 'alias';
END;
```

## Examples
```sql
-- Register the corporate mail relay with an encrypted password
EXECUTE portal BEGIN
  CREATE SMTP CONNECTION 'corporate' WITH (
    HOST         = 'smtp.corp.local',
    PORT         = 2525,
    USERNAME     = 'mailer',
    PASSWORD     = ENC:AQAAANCMnd8BFdERjHoAwE...,
    FROM_ADDRESS = 'reports@corp.local',
    USE_SSL      = TRUE
  );
END;

-- Minimal definition for an unauthenticated internal relay
EXECUTE portal BEGIN
  CREATE SMTP CONNECTION 'internal-relay' WITH (HOST = 'relay.corp.local');
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

-- List configured connections (passwords are never returned)
EXECUTE portal BEGIN
  SHOW SMTP CONNECTIONS INTO #smtp;
END;
SELECT * FROM #smtp;

-- Remove a connection
EXECUTE portal BEGIN
  DROP SMTP CONNECTION 'corporate';
END;
```

## Notes
- Requires the Admin role on the portal connection.
- `HOST` is required; `PORT` defaults to 587 and `USE_SSL` defaults to TRUE.
- `PASSWORD` is an expression position, so `ENC:` values and variables are accepted. The value is sent once over the authenticated HTTPS channel and stored encrypted by the portal; `SHOW SMTP CONNECTIONS` never includes it.
- The alias is the name subscriptions and alerts use in their `AT <alias>` clause.
- `DROP SMTP CONNECTION` resolves the alias case-insensitively and fails if no matching connection exists.
- See: PORTAL_SUBSCRIPTION, PORTAL_ALERT, PORTAL_SHOW

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
- [Grammar](../../../../../Docs/Reference/Grammar.md)
