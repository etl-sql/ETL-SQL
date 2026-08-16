# SEND EMAIL

Sends plain text or formatted HTML emails with optional file attachments via a configured `SMTP` connection. Ideal for automated pipeline reports, executive summaries, and operational error notifications.

---

## Syntax

### 1. Statement Form (Recommended)
```sql
SEND EMAIL
  TO '<recipient@example.com>'
  FROM '<sender@example.com>'
  SUBJECT '<subject_line>'
  BODY '<message_body>'
  [AT <smtp_connection_name>]
  [CC ['<cc1@example.com>', ...]]
  [BCC ['<bcc1@example.com>', ...]]
  [ATTACH ['<path/to/attachment.csv>', ...]];
```

### 2. Function Shorthand
```sql
SEND EMAIL(<smtp_connection>, '<to>', '<from>', '<subject>', '<body>' [, '<cc>', '<bcc>', '<attachment_path>']);
```

---

## Parameters & Clauses

- **`TO`** — Primary recipient email address (or comma-separated list of recipients).
- **`FROM`** — Sender email address.
- **`SUBJECT`** — Subject line string. Supports variable interpolation (`${@date}`).
- **`BODY`** — Message body text or HTML payload.
- **`AT`** — Active `SMTP` connection identifier.
- **`CC` / `BCC`** — Optional carbon-copy and blind carbon-copy email recipient lists.
- **`ATTACH`** — Optional list of local file paths on disk to attach to the outbound message.

---

## Examples

### 1. Simple Operational Notification

```sql
CREATE CONNECTION mailer AS SMTP(
    HOST = 'smtp.office365.com',
    PORT = 587,
    USERNAME = 'notifications@corp.com',
    PASSWORD = 'SECRET:SmtpMailerPassword',
    USE_SSL = TRUE
);

SEND EMAIL
  TO 'oncall@corp.com'
  FROM 'notifications@corp.com'
  SUBJECT 'ETL Pipeline Alert: Daily Ingestion Complete'
  BODY 'Nightly customer sync finished with 0 errors.'
  AT mailer;
```

### 2. Production ETL: Executive Daily Summary with Formatted HTML & Attached CSV

Calculate daily KPIs, generate an executive summary report, export raw transactions to CSV, and email to stakeholders:

```sql
CREATE CONNECTION dw     AS MSSQL(SERVER='dw.internal', DATABASE='analytics');
CREATE CONNECTION mailer AS SMTP(HOST='smtp.corp.internal', PORT=25);

DECLARE @today DATE = CAST(GETDATE() AS DATE);
DECLARE @total_orders INT;
DECLARE @total_rev DECIMAL;
DECLARE @attachment_path VARCHAR = 'C:\reports\daily_sales_' + CAST(@today AS VARCHAR) + '.csv';

-- 1. Calculate executive metrics
SELECT 
    COUNT(*)    AS total_orders,
    SUM(amount) AS total_revenue
INTO #kpis
FROM dw.dbo.FactOrders
WHERE order_date = @today;

SELECT total_orders  INTO @total_orders FROM #kpis;
SELECT total_revenue INTO @total_rev FROM #kpis;

-- 2. Export detailed line items for attachment
SELECT order_id, customer_name, amount, order_time 
INTO #export_view
FROM dw.dbo.FactOrders 
WHERE order_date = @today;

COPY FILE '#export_view' TO @attachment_path;

-- 3. Compose HTML body and send email with attachment
DECLARE @html_body VARCHAR = 
  '<h2>Daily Sales Performance Report</h2>' +
  '<p>Date: <b>' + CAST(@today AS VARCHAR) + '</b></p>' +
  '<ul>' +
  '  <li>Total Orders Processed: <b>' + CAST(@total_orders AS VARCHAR) + '</b></li>' +
  '  <li>Total Revenue: <b>$' + CAST(@total_rev AS VARCHAR) + '</b></li>' +
  '</ul>' +
  '<p>Please find the full itemized transactions attached.</p>';

SEND EMAIL
  TO 'finance-leadership@corp.com'
  FROM 'etl-reports@corp.com'
  SUBJECT 'Daily Sales KPI Summary - ' + CAST(@today AS VARCHAR)
  BODY @html_body
  AT mailer
  ATTACH [@attachment_path];

PRINT 'Daily executive report dispatched.';

-- 4. Cleanup attachment
DELETE FILE @attachment_path;
```

---

## References & Related Recipes

- [File Operations Reference](README.md)
- [SMTP Connector](../connectors/services/smtp.md)
- [SEND FILE](send-file.md)
- [ETL Cookbook: Automated Slack/Teams Alerting](../../cookbooks/etl/automated-slack-teams-alerting.md)
- [Syntax Index](../../syntax-index.md)
