# Third-Party Notices

ETL-SQL is built with the help of open-source and third-party software.
We are grateful to these projects and their maintainers. This file records
third-party notices for direct runtime dependencies and bundled browser assets
used by ETL-SQL.

This notice file is informational and should be reviewed before each public
release. Package licenses can change between versions, and some packages may
have commercial, redistribution, export, trademark, or service-specific terms
outside the package license.

## Prominent Runtime Credits

These projects are visible parts of the ETL-SQL user experience and are good
candidates for an About screen or small product footer credit:

| Component | Used in | Credit text |
| :--- | :--- | :--- |
| Tabulator | Report table/grid views | Table views powered by Tabulator. |
| Spectre.Console | CLI/TUI terminal rendering | Terminal experience powered by Spectre.Console. |
| PDFsharp + MigraDoc | PDF report export | PDF export powered by PDFsharp and MigraDoc. |
| Svg.Skia | PDF chart rendering | SVG chart rasterization for PDF export. |

Avoid wording that implies these projects endorse ETL-SQL.

## Bundled Browser Assets

These files are redistributed with ETL-SQL report runtime assets. Preserve any
license banners in the bundled files when updating them.

| Component | Files | License | Project |
| :--- | :--- | :--- | :--- |
| Tabulator | `tabulator.min.js`, `tabulator.min.css` | MIT | https://tabulator.info/ |
| CodeMirror 6 packages (`@codemirror/state`, `@codemirror/view`, `@codemirror/commands`, `@codemirror/language`, `@codemirror/search`, `@codemirror/autocomplete`, `@codemirror/lint`) | `designer/codemirror/codemirror-bundle.min.js` | MIT | https://codemirror.net/ |
| Lezer `@lezer/highlight` | `designer/codemirror/codemirror-bundle.min.js` | MIT | https://lezer.codemirror.net/ |

Canonical source path:
`src/ETL-SQL.ReportRuntime/Resources/Shared/`

Generated sync outputs also appear under ReportPlayer, Portal, and the
VS Code extension media folder.

## Direct NuGet Runtime Dependencies

The following direct NuGet package references are used by ETL-SQL runtime
projects. License values are taken from local package metadata when available.

| Package | License | Notes |
| :--- | :--- | :--- |
| Apache.Arrow | Apache-2.0 or package license file | Columnar data support. |
| Apache.Avro | Apache-2.0 or package license file | Avro connector support. |
| Azure.Storage.Blobs | MIT | Azure Blob Storage connector support. |
| Cronos | MIT | Cron expression parsing for scheduling/orchestration. |
| Docker.DotNet | MIT or package license file | Docker integration. |
| ExcelDataReader | MIT | Excel connector support. |
| ExcelDataReader.DataSet | MIT | Excel connector DataSet integration. |
| FluentFTP | MIT | FTP connector support. |
| Google.Cloud.BigQuery.V2 | Apache-2.0 | BigQuery connector support. |
| MailKit | MIT | SMTP/email support. |
| Microsoft.AspNetCore.Authentication.JwtBearer | MIT | Portal authentication. |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | MIT | Portal identity storage. |
| Microsoft.Data.SqlClient | MIT | SQL Server connector support. |
| Microsoft.Data.Sqlite | MIT | SQLite and local storage support. |
| Microsoft.EntityFrameworkCore.Design | MIT | EF Core migrations/design-time tooling. |
| Microsoft.EntityFrameworkCore.Sqlite | MIT | SQLite EF Core provider. |
| Microsoft.Extensions.* | MIT | Configuration, dependency injection, hosting, logging, and options infrastructure. |
| MiniExcel | Apache-2.0 | Native .xlsx export (report export + dataset viewer). |
| MySqlConnector | MIT | MySQL and MariaDB connector support. |
| Neo4j.Driver | Apache-2.0 | Neo4j connector support. |
| Npgsql | PostgreSQL License | PostgreSQL connector support. |
| Npgsql.EntityFrameworkCore.PostgreSQL | PostgreSQL License | PostgreSQL EF Core provider for portal state (HA deployments). |
| OmniSharp.Extensions.LanguageServer | package license file | Language server protocol support. |
| Oracle.ManagedDataAccess.Core | package license file | Oracle connector support. Review Oracle redistribution terms before release. |
| Parquet.Net | MIT | Parquet connector support. |
| PgpCore | MIT | PGP encryption/decryption support. |
| Polly | BSD-3-Clause | Resilience policies. |
| PDFsharp-MigraDoc | MIT | PDF export. |
| SkiaSharp.NativeAssets.Linux | MIT | Native SkiaSharp library for Linux containers (PDF chart rendering). |
| Svg.Skia | MIT | SVG rasterization for PDF chart rendering. |
| Serilog and Serilog.* | Apache-2.0 | Structured logging. |
| Snappier | BSD-3-Clause | Snappy compression support. |
| Snowflake.Data | Apache-2.0 | Snowflake connector support. |
| Spectre.Console | MIT | Terminal UI rendering. |
| SSH.NET | MIT | SFTP/SSH connector support. |
| Swashbuckle.AspNetCore | MIT | OpenAPI/Swagger support. |
| System.CommandLine | MIT | CLI parsing. |
| System.Data.Odbc | MIT | ODBC connector support. |
| System.Linq.Async | MIT | Async LINQ helpers. |
| System.Security.Cryptography.ProtectedData | MIT | Protected-data encryption helpers. |
| Testcontainers.* | MIT | Containerized integration test support; not intended for runtime redistribution. |
| TextCopy | MIT | Clipboard integration. |

NuGet package details can be reviewed at:
`https://www.nuget.org/packages/<PackageName>`

## Direct npm Dependencies

The VS Code extension UI uses the following direct npm packages. License values
should be regenerated from npm package metadata before publishing the extension.

| Package | License | Notes |
| :--- | :--- | :--- |
| @tanstack/react-table | MIT | Table state/modeling for React UI. |
| @tailwindcss/vite | MIT | Tailwind CSS Vite integration. |
| @vscode/webview-ui-toolkit | MIT | VS Code webview UI components. |
| clsx | MIT | Conditional class name utility. |
| framer-motion | MIT | UI animation. |
| lucide-react | ISC | Icon components. |
| react | MIT | UI framework. |
| react-dom | MIT | React DOM rendering. |
| tailwind-merge | MIT | Tailwind class merge helper. |
| tailwindcss | MIT | CSS utility framework. |

npm package details can be reviewed at:
`https://www.npmjs.com/package/<PackageName>`

## Development and Test Dependencies

ETL-SQL also uses third-party development and test dependencies, including
BenchmarkDotNet, coverlet.collector, ESLint, jsdom, Microsoft.Playwright, Moq,
TypeScript, Vite, Vitest, xUnit, and VS Code test tooling. These should be
included in a generated dependency report for source distributions and CI
artifacts, but they are usually not shown in product UI acknowledgements.

Microsoft.Playwright (MIT) additionally downloads browser binaries at test time —
Chromium and its headless shell, which carry their own upstream licenses. Those
browsers are fetched into a per-machine cache by the opt-in browser test lane and
are never redistributed in ETL-SQL archives, installers, containers, or extensions.

## Release Checklist

Before shipping a public release:

1. Regenerate the dependency inventory:
   `node scripts/generate-third-party-inventory.js`
2. Confirm whether each listed dependency is redistributed, dynamically loaded,
   used only at build/test time, or only referenced as an optional connector.
3. Preserve license banners in bundled JavaScript and CSS files.
4. Include this file in binary archives, installers, containers, and extension
   packages.
5. Review packages with non-standard or file-based license metadata, especially
   Oracle.ManagedDataAccess.Core, Apache.Arrow, Apache.Avro,
   Docker.DotNet, and OmniSharp.Extensions.LanguageServer.
6. Add an About or Third-Party Notices link in the Portal, Report Player,
   TUI, CLI, and VS Code extension where practical.
