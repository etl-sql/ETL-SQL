# Exporting and Subscriptions

> **Applies to:** Solo · Team · Enterprise · SaaS

Export report snapshots on-demand or configure scheduled email subscriptions that automatically deliver the latest data to any recipient.

---

## Manual Exports from the Portal UI

While viewing any report, click the **Export** menu (download icon in the report toolbar) to generate a snapshot:

| Format | Output | Best for |
| :--- | :--- | :--- |
| **PDF** | Rendered snapshot of all visuals, paginated | Sharing with stakeholders, archiving |
| **CSV** | Raw data for the focused visual | Downstream analysis in Excel / BI tools |
| **Excel** | Multi-sheet workbook, one tab per visual dataset | Analyst handoff |
| **Markdown** | Text rendering of table visuals | Embedding in wikis or ticketing systems |

> [!NOTE]
> CSV and Excel export the data rows that power the selected visual. Charts export the chart's source dataset, not the rendered image. Use PDF to capture charts as rendered.

---

## Scheduled Email Subscriptions

A subscription automatically runs a report on a schedule and emails the result. You can subscribe via the Portal UI or with a script inside `EXECUTE portal BEGIN ... END`.

### Subscribe via the Portal UI

1. Open a report.
2. Click the **Subscribe** button (envelope icon in the toolbar).
3. Fill in the subscription form:

| Field | Description |
| :--- | :--- |
| **Name** | Label for this subscription (shown in My Subscriptions) |
| **Schedule** | `Daily`, `Weekly`, or `Monthly` |
| **At Time** | Delivery time in 24-hour format (e.g. `08:00`) |
| **Format** | `PDF`, `CSV`, `Markdown`, or `Link` (URL only, no attachment) |
| **Recipient email** | Defaults to your account email; can be overridden |
| **Parameters** | If the report has parameters, set values that apply to every run |

4. Click **Save**.

> [!TIP]
> Choose **Link** format to send only a Portal URL — no file is generated and delivery is near-instant even for large reports.

### Subscribe via Script

Administrators and publishers can create, update, and remove subscriptions programmatically:

```sql
-- Create a daily PDF subscription delivered at 07:30
EXECUTE portal BEGIN
    CREATE SUBSCRIPTION morning_sales
    FOR REPORT 'Sales/DailyOverview'
    SCHEDULE '30 7 * * *'
    SEND TO 'sales-team@company.com'
    FORMAT PDF;
END;
```

```sql
-- Create a weekly CSV subscription with a parameter override
EXECUTE portal BEGIN
    CREATE SUBSCRIPTION weekly_emea_csv
    FOR REPORT 'Sales/RegionalBreakdown'
    SCHEDULE '0 6 * * 1'
    SEND TO 'emea-team@company.com'
    FORMAT CSV
    WITH PARAMETERS (@region = 'EMEA');
END;
```

```sql
-- Pause a subscription without deleting it
EXECUTE portal BEGIN
    ALTER SUBSCRIPTION morning_sales SET ACTIVE = FALSE;
END;
```

```sql
-- Remove a subscription permanently
EXECUTE portal BEGIN
    DROP SUBSCRIPTION morning_sales;
END;
```

---

## Relative Date Parameters in Subscriptions

Subscription parameters that use relative date expressions (`D-1`, `M-1`, `W-1`) are resolved at run time, not at the time you create the subscription. This means `D-1` always means "yesterday at run time".

| Expression | Resolves to at each run |
| :--- | :--- |
| `D-0` | Today at midnight |
| `D-1` | Yesterday at midnight |
| `D-7` | Seven days ago |
| `M-1` | First day of last month |
| `Y-1` | January 1 of last year |

Enter a fixed ISO date (`2026-01-01`) to pin a subscription to a specific date that never changes.

---

## Managing Your Subscriptions

Open **My Subscriptions** from the user menu (top-right corner). The list shows:

- Subscription name, schedule, and next run time
- Last delivery status and timestamp
- A compact parameter summary (e.g. `@region=EMEA  @start=D-7`)

Available actions per subscription:

| Action | Effect |
| :--- | :--- |
| **Pause / Resume** | Toggle without losing settings |
| **Edit Parameters** | Update saved parameter values |
| **History** | View last delivery attempts with timestamps and errors |
| **Delete** | Permanently remove and cancel any pending job |

---

## Related Guides

- [Browsing and Running Reports](browsing-and-running-reports.md) — catalog navigation and interactive parameters
- [Sharing and Embed Tokens](sharing-and-embed-tokens.md) — share report links externally
- [Job Orchestration](../../../administration/orchestration/README.md) — scheduled job management for administrators
