# EXPORT

Creates portable copies of datasets, scripts, or reports, or exports the Portal configuration.

> [!NOTE]
> The `EXPORT` statement is **not** used to export query results or `#temp` tables directly to files. To export data to a delimited file (such as a CSV/TSV) or another database, define a connection using the `FLATFILE` or target database connector and execute an `INSERT INTO` statement.

## Syntax

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

-- Export Portal configuration (administrative statement)
EXECUTE portal_admin BEGIN
  EXPORT PORTAL CONFIGURATION TO 'portal_bootstrap.txt';
END;
```

## Correct Pattern: Exporting Query / Table Data to CSV

To write query results or `#temp` tables to a file, use the following connector pattern instead:

```sql
-- 1. Create a FLATFILE connection with formatting options
CREATE CONNECTION orders_csv AS FLATFILE(
  PATH      = 'output/orders.csv',
  DELIMITER = '|',
  HEADER    = ON,
  ENCODING  = UTF8
);

-- 2. Insert data from the query or temp table
INSERT INTO orders_csv 
SELECT * FROM #orders;
```

## Report PDF Options
`EXPORT REPORT ... WITH (...)` options are valid only with `FORMAT PDF`.

| Option | Values | Default | Description |
|---|---|---|---|
| `PDF_MODE` | `STATIC` \| `AUTO` \| `HOSTED` \| `BROWSER` | `STATIC` | Method of PDF rendering. |
| `HOST` | report serve / Portal URL | none | Portal URL for hosted export. |
| `BROWSER_PATH` | path to Chromium executable | none | Chrome, Edge, or Chromium executable path. |

`STATIC` uses the built-in PDFsharp/MigraDoc exporter and requires no browser. `HOSTED` and `BROWSER` use an installed Chrome, Edge, or Chromium browser with the shared report runtime. `AUTO` tries the configured browser-backed path when `HOST` is available and falls back to `STATIC` with a warning.

## Notes
- `EXPORT DATASET` is portal-only and requires dataset read access. It decrypts the managed cache and creates a portable copy using the supplied `PASSWORD` or `KEYFILE` transport credential.
- Dataset export credentials are operation-only and are never persisted. Output is committed atomically, so a failure preserves any existing destination.
- `EXPORT SCRIPT` preserves published bundle relative paths but does not decrypt or reveal secrets; recovered scripts may require credentials to be re-entered.
- `EXPORT PORTAL CONFIGURATION` is an administrative command run inside an `EXECUTE <portal_conn> BEGIN ... END` block to export the portal's entire configuration schema (see: PORTAL_ADMIN).
- Explicit `PDF_MODE = HOSTED` and `PDF_MODE = BROWSER` require a `HOST` URL and a discoverable or configured installed browser; use `PDF_MODE = AUTO` to allow fallback to `STATIC`.
- See also: [FLATFILE Connection Reference](../connectors/files/flatfile.md), [CREATE CONNECTION](../statements/README.md), [SELECT](../statements/README.md)

References:
- [Orchestrator Jobs](README.md)

