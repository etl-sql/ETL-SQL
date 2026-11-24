# EXCEL

Reads and writes Microsoft Excel workbooks (`.xlsx`, `.xls`, `.xlsb`). Query or write the
connection's configured sheet using `xl_conn.FILE`, or target any specific worksheet by name on a
single connection using `xl_conn.<SheetName>` (for example, `SELECT * FROM xl_conn.Summary` or
`INSERT INTO xl_conn.Detail`). Columns are named from the header row, or `Column1`, `Column2`, …
when there is no header.

Aliases: `XLSX`, `XLS`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the workbook | Yes (structured) |
| `SHEET` | Target sheet name (default: first sheet; mutually exclusive with `SHEET_INDEX`) | No |
| `SHEET_INDEX` | 1-based worksheet position (mutually exclusive with `SHEET`). Reads only: a write with it fails; use `SHEET` to name the sheet to write | No |
| `START_ROW` | 1-based row number where the header or data begins, skipping banner rows without needing an explicit `RANGE` end cell. Reads only: a write with it fails | No |
| `HEADER` | `ON`/`OFF` — treat first row as column names (default: `ON`) | No |
| `RANGE` | Explicit cell range to read (e.g. `'A1:F500'`) | No |
| `STRICT_SCHEMA` | `ON`/`OFF` — enforce column count matching when a template schema is supplied (default: `OFF`) | No |
| `IGNORE_EXTRA_COLUMNS` | `ON`/`OFF` — omit source columns not present in the template schema | No |
| `NULL_MISSING_COLUMNS` | `ON`/`OFF` — include template columns missing from the source and fill with `NULL` | No |
| `MAP_BY_HEADER_NAME` | `ON`/`OFF` — align by case-insensitive header name instead of position; requires unique source headers | No |
| `COMPRESS` | `ON`/`OFF` — store the file as a ZIP archive (one entry); `.zip` is added to the path if it does not end in `.zip` (default: `OFF`) | No |
| `ENCRYPT` | `ON`/`OFF`/`PASSWORD`/`KEYFILE`/`MACHINE`/`PORTAL` — encrypt on write and decrypt on read (default: `OFF`). Adds `.pgp` when `COMPRESS=OFF` or `.zip` when `COMPRESS=ON` | No |
| `PASSWORD` | Password for AES encryption/decryption (required for `ENCRYPT=ON`/`PASSWORD` when `KEYFILE` is not set) | Conditional |
| `ALGORITHM` | Key-derivation hash for password encryption: `SHA2_256`/`SHA256` (default) or `SHA2_512`/`SHA512` (`MD5` and `SHA1` are rejected) | No |
| `KEYFILE` | Path to SSH key file for key-pair encryption/decryption | Conditional |
| `PASSPHRASE` | Passphrase for an encrypted `KEYFILE` | Conditional |
| `OVERWRITE_WORKBOOK` | `ON`/`OFF` — `OFF` (default) preserves other worksheets when writing to a named sheet in an existing `.xlsx` workbook; `ON` replaces the entire workbook with only the written sheet | No |
| `TRANSACTIONAL` | `ON`/`OFF` — stage the complete output beside the target and publish with one replacement rename (default: `OFF`) | No |

> [!NOTE]
> **Write-time compression and encryption:** When both `COMPRESS=ON` and `ENCRYPT=ON` are set,
> writes always compress (ZIP) first and then encrypt; reads reverse the order (decrypt, then
> unzip). `PASSWORD`, `KEYFILE` (+ optional `PASSPHRASE`), `ENCRYPT=MACHINE`, and `ENCRYPT=PORTAL`
> are alternative key sources: set one. If several are set, `MACHINE` wins over `KEYFILE`,
> `KEYFILE` over `PASSWORD`, and a `PASSWORD` option replaces the `ENCRYPT=PORTAL` key. Enabling encryption without a key source fails closed.

Excel uses the same schema-resilience contract as [FLATFILE](flatfile.md): `STRICT_SCHEMA=ON` fails on
unaccepted drift, `IGNORE_EXTRA_COLUMNS=ON` accepts surplus columns, `NULL_MISSING_COLUMNS=ON` fills
absent template columns with `NULL`, and `MAP_BY_HEADER_NAME=ON` aligns by unique source header names.
Use `EXPECT SCHEMA` after staging if the accepted temp-table shape is part of the pipeline contract.

## Authentication

Excel file operations use local file system permissions or storage connector credentials. For password-protected workbooks, supply `PASSWORD`.

## Examples

```sql
-- Specific sheet and range
CREATE CONNECTION xl_src AS EXCEL('C:\Reports\Q4.xlsx', SHEET='Summary', HEADER=ON, RANGE='A1:F500');

-- Second worksheet, skipping 4 banner rows so row 5 is the header
CREATE CONNECTION xl_messy AS EXCEL('C:\Reports\Monthly.xlsx', SHEET_INDEX=2, START_ROW=5, HEADER=ON);

-- Easy button: compress (ZIP) then encrypt on write, decrypt then unzip on read
CREATE CONNECTION xl_out AS EXCEL(PATH='C:\Secure\payroll.xlsx', COMPRESS=ON, ENCRYPT=ON, PASSWORD='SECRET:payroll/file_key');
INSERT INTO xl_out.FILE SELECT * FROM #payroll;
SELECT * FROM xl_out.FILE;
```

## Troubleshooting

- **Sheet Not Found**: Verify `SHEET` or 1-based `SHEET_INDEX` (they are mutually exclusive).
- **Header Row Detection**: Set `HEADER=ON` and optional 1-based `START_ROW` to skip banner rows above the header.

## References

- [File Connectors](README.md)
- [EXCEL_SHEET_LIST](../../functions/file-path/excel_sheet_list.md)
- [Connectors](../README.md)
- [FLATFILE](flatfile.md)
- [Transactional File Writes](transactional-writes.md)
