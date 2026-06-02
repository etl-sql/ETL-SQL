# EXPORT
Writes data from a #temp table to a file or an external connection.

## Syntax
```sql
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

`STATIC` uses the built-in PDFsharp/MigraDoc exporter and requires no browser. `AUTO`, `HOSTED`, and `BROWSER` are reserved for high-fidelity browser-backed export; `AUTO` falls back to `STATIC` until a high-fidelity path is configured.

## Notes
- For database destinations, the target table must exist unless the connection supports auto-create.
- SFTP, S3, and API connection types are supported as destinations.
- To control column order or filter rows before export, `SELECT ... INTO #subset` first.
- `EXPORT SCRIPT` preserves published bundle relative paths but does not decrypt or reveal secrets; recovered scripts may require credentials to be re-entered.
- Explicit `PDF_MODE = HOSTED` and `PDF_MODE = BROWSER` require the corresponding exporter implementation/configuration; use `PDF_MODE = AUTO` to allow fallback to `STATIC`.
- See: CREATE CONNECTION, SELECT

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
