# EXPORT
Writes data from a #temp table to a file or an external connection.

## Syntax
```text
-- Export to a flat file
EXPORT #orders TO 'output/orders.csv';

-- Export with options
EXPORT #orders TO 'output/orders.csv' WITH (
  DELIMITER = '|',
  HEADER    = ON,
  ENCODING  = 'UTF-8'
);

-- Export to a connection table
EXPORT #orders TO MyDB.dbo.OrdersArchive;

-- Export to SFTP
EXPORT #report TO SftpConn:'reports/daily.csv';
```

```sql
-- Create a portable dataset copy with a one-time transport password
EXPORT DATASET &sales
TO 'C:\Transfer\sales.parquet'
ENCRYPT = PASSWORD
PASSWORD = 'transport-secret';

-- Or encrypt the portable copy with a key file
EXPORT DATASET &sales
TO 'C:\Transfer\sales.parquet'
ENCRYPT = KEYFILE
KEYFILE = 'C:\Transfer\keys\dataset_transport.pub';

-- Recover a published Orchestrator bundle
EXPORT SCRIPT 'orch://finance-load@3/main.etlsql' TO 'C:\Recovered\finance-load';

-- Export a Report-SQL report to PDF with the default static renderer
EXPORT REPORT 'reports/sales.rptsql' FORMAT PDF TO 'out/sales.pdf';

-- Select the PDF renderer mode
EXPORT REPORT 'reports/sales.rptsql' FORMAT PDF TO 'out/sales.pdf'
WITH (PDF_MODE = AUTO);

EXPORT REPORT 'reports/sales.rptsql' FORMAT PDF TO 'out/sales.pdf'
WITH (
  PDF_MODE     = BROWSER,
  BROWSER_PATH = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
);
```

## Options
| Option | Values | Default |
|---|---|---|
| DELIMITER | any char | `,` |
| HEADER | ON \| OFF | ON |
| ENCODING | UTF-8, UTF-16, ASCII | UTF-8 |
| OVERWRITE | ON \| OFF | ON |
| APPEND | ON \| OFF | OFF |

## Report PDF Options
`EXPORT REPORT ... WITH (...)` options are valid only with `FORMAT PDF`.

| Option | Values | Default |
|---|---|---|
| PDF_MODE | STATIC \| AUTO \| HOSTED \| BROWSER | STATIC |
| HOST | report serve / ReportPortal URL for hosted export | none |
| BROWSER_PATH | installed Chrome, Edge, or Chromium executable path | none |

`STATIC` uses the built-in PDFsharp/MigraDoc exporter and requires no browser. `HOSTED` and `BROWSER` use an installed Chrome, Edge, or Chromium browser with the shared report runtime. `AUTO` tries the configured browser-backed path when `HOST` is available and falls back to `STATIC` with a warning.

## Notes
- For database destinations, the target table must exist unless the connection supports auto-create.
- SFTP, S3, and API connection types are supported as destinations.
- To control column order or filter rows before export, `SELECT ... INTO #subset` first.
- `EXPORT DATASET` is portal-only and requires dataset read access. It decrypts the managed cache and
  creates a portable copy using the supplied PASSWORD or KEYFILE transport credential.
- Dataset export credentials are operation-only and are never persisted. Output is committed atomically,
  so a failure preserves any existing destination.
- `EXPORT SCRIPT` preserves published bundle relative paths but does not decrypt or reveal secrets; recovered scripts may require credentials to be re-entered.
- `EXPORT PORTAL CONFIGURATION` is an administrative command run inside an `EXECUTE portal` block to export the portal's entire configuration schema (see: PORTAL_ADMIN).
- Explicit `PDF_MODE = HOSTED` and `PDF_MODE = BROWSER` require a `HOST` URL and a discoverable or configured installed browser; use `PDF_MODE = AUTO` to allow fallback to `STATIC`.
- See: CREATE CONNECTION, SELECT, PORTAL_ADMIN

References:
- [Grammar](../../guides/getting-started.md)
