# SMTP Connections and Subscriptions

## 7. SMTP Connections

SMTP connections are named credentials used by subscriptions to send email. Open **Admin → SMTP**.

### 7.1 Creating a Connection

| Field | Description |
| :--- | :--- |
| **Alias** | Unique name referenced by subscriptions (e.g. `corporate-smtp`) |
| **Host** | SMTP server hostname |
| **Port** | Typically `587` (STARTTLS) or `465` (SSL) |
| **Username** | Login for the SMTP server |
| **Password** | Stored encrypted via .NET Data Protection API — never stored in plaintext |
| **From Address** | The `From:` address on outgoing emails |
| **Use SSL** | Whether to use SSL/TLS |

### 7.2 Security Note

SMTP passwords are encrypted at rest using the .NET Data Protection API with the machine key. Moving the portal to a new host requires re-entering SMTP passwords because the encrypted values cannot be decrypted on a different machine without transferring the Data Protection key ring.

### 7.3 Scripted Management

SMTP connections can also be managed from an ETL-SQL script inside an `EXECUTE portal` block (Admin role required), which keeps mail configuration reproducible alongside the rest of a portal bootstrap script:

```sql
EXECUTE portal BEGIN
    CREATE SMTP CONNECTION 'corporate' WITH (
        HOST         = 'smtp.corp.example',
        PORT         = 587,
        USERNAME     = 'mailer',
        PASSWORD     = ENC:...,            -- expression position: ENC:/variables accepted
        FROM_ADDRESS = 'reports@corp.example',
        USE_SSL      = TRUE
    );
    SHOW SMTP CONNECTIONS;                 -- never returns passwords
    DROP SMTP CONNECTION 'corporate';
END;
```

The password travels once over the authenticated HTTPS channel and is stored encrypted exactly as if entered in **Admin → SMTP**; no SMTP secret is persisted in the script's execution history or portal audit log.

---

## 8. Subscriptions

Subscriptions are owned by individual users but visible and manageable by Admins in **Admin → Subscriptions**.

### 8.1 Subscription Formats

| Format | What is delivered |
| :--- | :--- |
| `Link` | Email containing a URL linking to the live report in the portal. Fastest; requires no attachment export. SMTP still needed for delivery. |
| `PDF` | Full rendered snapshot of all visuals as a PDF attachment |
| `CSV` | Raw data table as a CSV attachment |
| `Markdown` | Report content as a Markdown text attachment |

### 8.2 Schedules

| Schedule | Behaviour |
| :--- | :--- |
| `Daily` | Runs once per day at the configured `AtTime` |
| `Weekly` | Runs once per week at `AtTime` |
| `Monthly` | Runs on the first day of each month at `AtTime` |

Subscription jobs are handed to the **ETL-SQL Orchestrator** for scheduling. If the Orchestrator is not reachable, subscriptions are created in the database but jobs will not fire until the Orchestrator comes online.

The scheduled job itself is a **credential-free trigger**: the generated `.etlsql` script contains only the subscription ID — no SMTP credentials, recipients, or report parameters. When the trigger completes, the portal's trusted delivery executor re-checks the subscription owner's active state and current report permission, exports the report, and sends the email in-process. The SMTP credential is decrypted only for the duration of that delivery and is never written to disk. On startup the portal also rewrites any pre-upgrade subscription script that embedded credentials to the trigger form and removes generated scripts whose subscription no longer exists.

Because delivery happens in the portal, the **portal process must be running** for subscription email to be sent — the Orchestrator alone only fires the trigger.

### 8.3 Delivery Semantics

Subscription delivery is **at-most-once per recipient and scheduler trigger**. Recipient addresses
are normalized and deduplicated, then each recipient is claimed independently in a durable delivery
ledger keyed on `(subscription, trigger, recipient key)`. A repeated scheduler completion is
suppressed without re-sending recipients already claimed. Each attempt carries a `delivery-<id>`
that matches its audit correlation id, and records `Delivered`, `Failed`, `Denied`, or `Skipped`.

The portal never records `Delivered` unless the in-process delivery run reports success, so it errs toward recording a failure rather than a false success. The one boundary it cannot control is SMTP itself: if the SMTP server accepts a message but the connection then times out, the recipient may receive a copy that the portal records as `Failed` — at the wire that single case is at-least-once. The ledger makes every attempt and outcome observable so such cases are visible rather than silent.

An invalid or SMTP-rejected address fails only its recipient row; valid recipients continue. Logs
and audit details use a recipient fingerprint rather than the address. Authorized delivery history
retains the normalized address for diagnosis.

### 8.4 Delivery Failures

Each subscription tracks a `FailCount`, incremented by the portal's delivery executor when an export or send fails (with sanitized error detail in the audit log and the delivery ledger). A delivery that is **denied** — the owner was disabled or lost read permission on the report's folder — is recorded as `SUBSCRIPTION_DELIVERY_DENIED` in the audit log and is *not* counted or retried as a transient failure. Investigate via **Admin → Subscriptions → History** and correct the SMTP configuration, permissions, or report script before re-enabling.

The Admin subscription table shows active/paused state, the last successful delivery time or failure count, and provides:

- **History** — recent delivery attempts with status, attempt time, duration, rows processed, and sanitized error text.
- **Pause / Resume** — stop or restart future deliveries without deleting the subscription.
- **Delete** — retire the subscription and remove its generated Orchestrator job.

Use the search box and status filter to isolate subscriptions by report, name, recipient, active/paused state, or delivery failure. Select rows on the current page to pause or resume multiple subscriptions together. Selection is page-local and is cleared when the filter or page changes.

### 8.5 Scripted Subscription Management

Administrators can create and modify subscriptions using ETL-SQL script syntax. This is useful for bulk setup, deployment automation, or version-controlling subscription configuration alongside report scripts.

#### CREATE SUBSCRIPTION

```sql
CREATE SUBSCRIPTION ['<name>']
FOR REPORT '<script-path>'
DELIVER TO '<email>' | GROUP '<group-name>'
SCHEDULE '<cron-expression>'
FORMAT PDF | CSV | BOTH
AT <smtp-alias>
[ PARAMETERS (
    @param1 = '<value>',
    @param2 = '<value>',
    ...
) ]
[ ENABLE | DISABLE ];
```

The optional `'<name>'` is a human-readable label shown in subscription lists. It is optional — if omitted the subscription is identified by its generated ID.

Parameter values are stored as strings and must be single-quoted. Use the report script's defaults when you want an unset parameter.
New subscriptions are enabled by default. Add `DISABLE` when reconstructing a paused subscription;
`ENABLE` is accepted when an explicit active state is useful in generated configuration.

When these statements are executed remotely through a `REPORTPORTAL` connection, `FORMAT PDF` and `FORMAT CSV` are supported. `FORMAT BOTH` and `DELIVER TO GROUP` are valid ETL-SQL syntax but are not yet supported by the portal connector — the remote call will fail at runtime. Use a single format and a named recipient address until portal support for multi-format delivery and group expansion ships.

**Examples:**

```sql
-- Daily sales report: always yesterday's data
CREATE SUBSCRIPTION 'DailySales'
FOR REPORT '/Reports/Sales/Daily'
DELIVER TO 'john@example.com'
SCHEDULE '0 6 * * *'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @start  = 'D-1',
    @end    = 'D',
    @region = 'All'
);

-- Monthly executive summary delivered to a group
CREATE SUBSCRIPTION 'MonthlyExec'
FOR REPORT '/Reports/Executive/MonthlySummary'
DELIVER TO GROUP 'Executives'
SCHEDULE '0 7 1 * *'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @period_start = 'M-1',
    @period_end   = 'ME-1'
);

-- Fixed date range for a one-time review
CREATE SUBSCRIPTION 'Q1Review'
FOR REPORT '/Reports/Finance/Quarterly'
DELIVER TO 'cfo@example.com'
SCHEDULE '0 8 * * 1'
FORMAT PDF
AT corporate-smtp
PARAMETERS (
    @start = '2026-01-01',
    @end   = '2026-03-31'
);
```

#### RELDATE parameter values

When a report uses `RELDATE` INPUT parameters, the subscription stores the expression string — not a resolved date. The engine resolves it fresh each time the subscription fires. See [`docs/reference/dates-times/reldate.md`](../../reference/functions/datetime/reldate.md) for the full expression reference.

Common expressions:

| Expression | Resolves to at run time |
| :--- | :--- |
| `'D'` | Today at midnight |
| `'D-1'` | Yesterday at midnight |
| `'W-1'` | Start of last week |
| `'ME-1'` | Last day of last month |
| `'M-1'` | First day of last month |
| `'QE-1'` | Last day of last quarter |
| `'Y-1'` | January 1 of last year |
| `'YE-1'` | December 31 of last year |
| `'N-2H'` | Exactly 2 hours before the run |

A fixed ISO date string (`'2026-01-01'`) can also be used to pin to a specific date.

#### LIST parameter values

Pass `LIST` parameters as a single quoted, comma-separated string. Wrap values containing commas in double quotes:

```sql
PARAMETERS (
    @regions = 'North,South,East',
    @brands  = '"Acme, Inc",Globex'
);
```

#### ALTER SUBSCRIPTION

Modify an existing subscription without recreating it:

```sql
ALTER SUBSCRIPTION <id> SET
    SCHEDULE = '<cron-expression>' |
    FORMAT = PDF | CSV | BOTH |
    SMTP = '<smtp-alias>' |
    ENABLE |
    DISABLE |
    PARAMETERS (
        @param1 = '<value>',
        ...
    );
```

The `PARAMETERS(...)` clause **replaces the full parameter set** for the subscription. To clear all parameters use `PARAMETERS ()` (empty). To leave parameters unchanged, omit the clause.

```sql
-- Change schedule only
ALTER SUBSCRIPTION 5 SET SCHEDULE = '0 8 * * 1-5';

-- Update parameters only
ALTER SUBSCRIPTION 5 SET
PARAMETERS (
    @start  = 'W-1',
    @end    = 'W',
    @region = 'North'
);

-- Pause a subscription
ALTER SUBSCRIPTION 6 SET DISABLE;
```

#### DROP SUBSCRIPTION

```sql
DROP SUBSCRIPTION <id>;
```

---

