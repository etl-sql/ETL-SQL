# Report CLI, Hosting, and Preview

Reference for building, serving, and previewing `.rptsql` reports: the `etl-sql-report` CLI, multi-report
hosting, the ReportPlayer web dashboard and its API, VS Code preview, and the report linter rules. For
authoring `.rptsql` scripts, see the [Report-SQL guide](../../guides/report-sql.md).

## `etl-sql-report build`

Evaluates the script, builds a `ReportManifest`, and writes output files:

```sh
etl-sql-report build report.rptsql
etl-sql-report build report.rptsql --output out/dashboard.md
etl-sql-report build report.rptsql --format json
etl-sql-report build report.rptsql --format pdf
```

Output files produced:

| File | Description |
|------|-------------|
| `<script>.report.md` | GitHub Flavored Markdown document. Default when `--format md`. |
| `<script>.report.json` | Raw manifest JSON. Default when `--format json`. |
| `<script>.report.pdf` | Static PDF export via PDFsharp/MigraDoc. Charts render from ECharts/SVG, tables are capped for readability. Default when `--format pdf`. |
| `<script>.etlsnap` | Encrypted snapshot package (ZIP structure containing layout JSON and Arrow IPC tables). Always written alongside the report. |

Flags:

| Flag | Default | Description |
|------|---------|-------------|
| `--output`, `-o` | `<script>.report.<ext>` | Override the output file path. |
| `--format`, `-f` | `md` | Output format: `md`, `json`, or `pdf`. |
| `--mock` | `false` | Run evaluation in dry-run mock mode using stubbed connection data. |
| `--json` | `false` | Output build results and diagnostics in structured JSON format. |

### PDF export modes

Script-level report export supports an optional PDF mode selector:

```sql
EXPORT REPORT 'reports/sales.rptsql'
FORMAT PDF
TO 'out/sales.pdf'
WITH (PDF_MODE = STATIC);

EXPORT REPORT 'reports/sales.rptsql'
FORMAT PDF
TO 'out/sales.pdf'
WITH (
  PDF_MODE     = BROWSER,
  BROWSER_PATH = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
);
```

| Mode | Behavior |
|---|---|
| `STATIC` | Default. Uses the built-in PDFsharp/MigraDoc exporter; no browser is required. |
| `AUTO` | Uses a configured high-fidelity path when available, otherwise falls back to `STATIC` with a warning. |
| `HOSTED` | Reserved for Portal / `report serve` browser-backed export. Explicit mode fails if unavailable. |
| `BROWSER` | Uses an optional installed Chrome, Edge, or Chromium executable. No browser is bundled or required. |

`HOST` and `BROWSER_PATH` are accepted only with `FORMAT PDF`. `HOSTED` and `BROWSER` are opt-in
high-fidelity modes; `STATIC` remains the portable default.

## `etl-sql-report refresh`

Re-evaluates the script and updates the snapshot without writing a new report document:

```sh
etl-sql-report refresh report.rptsql
```

The snapshot is stored alongside the script as `<script>.etlsnap`. The ReportPlayer considers the
snapshot stale if the script file has been modified since the snapshot was built, or if the TTL
(default 24 h) has elapsed.

## `etl-sql-report serve`

Starts the web dashboard at `http://localhost:5200`:

```sh
# Single report
etl-sql-report serve report.rptsql

# Multi-report catalog (see reports.json below)
etl-sql-report serve --manifest reports.json

# Override the default port
etl-sql-report serve report.rptsql --port 8080

# Port 0 = OS-assigned (actual URL echoed as REPORT_URL=...)
etl-sql-report serve report.rptsql --port 0
```

Internally this launches `ETL-SQL.ReportPlayer` (the Kestrel ASP.NET server) and opens the browser after
2.5 s. Keep the process running; the dashboard is served for as long as the process is alive.

## Multi-report hosting

Multiple reports can be hosted together using a `reports.json` manifest file:

```json
{
  "reports": [
    { "name": "sales",     "path": "reports/sales.rptsql",     "description": "Regional sales dashboard" },
    { "name": "inventory", "path": "reports/inventory.rptsql", "description": "Inventory levels by SKU" }
  ]
}
```

Start the server with `etl-sql-report serve --manifest reports.json`. The catalog page at
`http://localhost:5200` lists all reports; each is accessible at `http://localhost:5200/reports/<name>`.
API routes are prefixed per-report: `/reports/<name>/api/manifest`, `/reports/<name>/api/refresh`, etc.

## ReportPlayer web dashboard

The ReportPlayer is a lightweight ASP.NET Minimal API server that hosts the report as an interactive
dashboard.

**Endpoints (single-report mode):**

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | Serves the full dashboard HTML with pre-embedded manifest. |
| `GET` | `/api/manifest` | Returns the current `ReportManifest` as JSON. |
| `GET` | `/api/refresh` | Triggers a full rebuild and returns `{ rebuilt: true, visuals: N }`. |
| `POST` | `/api/parameter` | Updates one parameter and triggers a selective rebuild. Body: `{ "name": "@region", "value": "West" }`. |
| `POST` | `/api/parameters` | Updates multiple parameters in a single request. Body: `{ "params": [{ "name": "@region", "value": "West" }, ...] }`. |

**Endpoints (multi-report mode):** the same routes prefixed with `/reports/{name}` (`/reports/{name}/api/manifest`, `/api/refresh`, `/api/parameter`, `/api/parameters`), plus `GET /` (catalog) and `GET /reports/{name}` (dashboard).

**Selective refresh** — when a parameter changes, `DashboardService` checks which visuals depend on it
(by scanning the inline SELECT for variable references) and re-queries only those visuals. Unaffected
visuals keep their current data.

**Staleness banner** — if the manifest was built before the script file was last written, or more than
24 hours have passed, a yellow banner suggests `etl-sql-report refresh`. Hitting `/api/refresh` forces a
live rebuild without restarting the server.

**Dashboard rendering** — the VS Code WebviewPanel loads the bundled React app (`ui/dist/index.html`,
manifest injected as `window.__INITIAL_STATE__.messages`); the web ReportPlayer uses a single vanilla-JS
file (`wwwroot/report-runtime.js`). Both render charts with [Apache ECharts v5](https://echarts.apache.org/).

## VS Code preview

With a `.rptsql` file open, run **ETL-SQL: Preview Report** from the command palette or click the
`$(graph)` icon in the editor title bar. A webview panel opens beside the editor and auto-refreshes on
save.

| Setting | Default | Description |
|---------|---------|-------------|
| `etlsql.report.executable.path` | `etl-sql-report` | Full path to `etl-sql-report`. Leave empty to use `dotnet run` from the source tree in development. |
| `etlsql.report.autoOpenPreview` | `false` | Automatically open the Report Preview panel when opening an `.rptsql` file. |

## Linter rules (language server)

The language server checks `.rptsql` files automatically:

| Rule | Severity | Condition |
|------|----------|-----------|
| `VisualSourceExists` | Warning | `SOURCE = &dataset` (or `#table`) references a source not defined earlier in the script. |
| `VisualMappingColumnExists` | Warning | A `MAPPINGS` role references a column not returned by the `SOURCE` inline SELECT. |
| `PageVisualReferenced` | Warning | A `MAP` entry references a visual or container name not defined in the script. |
| `DatasetEncryptWithoutKey` | **Error** | `ENCRYPT = KEYFILE` without a `KEYFILE` clause, or `ENCRYPT = PASSWORD` without a `PASSWORD` clause. |
| `LayerOrder` | Warning | A visual references a dataset defined later in the script, or a page references a later visual. Forward references are not supported. |
| `InsertColumnCountMismatch` | Warning | `INSERT INTO` omits the column list and the SELECT provides fewer columns than the target table. |

## References

- [Report-SQL guide](../../guides/report-sql.md)
- [ReportManifest JSON schema](report-manifest.md)
- [Report runtime contract](report-runtime-contract.md)
- [Report objects](report/README.md) · [Visual types](visuals/README.md)
