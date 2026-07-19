# EXCEL

Reads and writes Microsoft Excel workbooks (`.xlsx`, `.xls`, `.xlsb`). When querying an `EXCEL`
connection via `SELECT`, the table name is `FILE` and columns are named from the header row, or
`Column1`, `Column2`, … when there is no header.

Aliases: `XLSX`, `XLS`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the workbook | Yes (structured) |
| `SHEET` | Target sheet name (default: first sheet) | No |
| `HEADER` | `ON`/`OFF` — treat first row as column names (default: `ON`) | No |
| `RANGE` | Explicit cell range to read (e.g. `'A1:F500'`) | No |
| `STRICT_SCHEMA` | `ON`/`OFF` — enforce column count matching when a template schema is supplied (default: `OFF`) | No |
| `IGNORE_EXTRA_COLUMNS` | `ON`/`OFF` — omit source columns not present in the template schema | No |
| `NULL_MISSING_COLUMNS` | `ON`/`OFF` — include template columns missing from the source and fill with `NULL` | No |
| `MAP_BY_HEADER_NAME` | `ON`/`OFF` — align by case-insensitive header name instead of position; requires unique source headers | No |
| `COMPRESS` | `ON`/`OFF` — GZip the output file after writing | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

Excel uses the same schema-resilience contract as [FLATFILE](flatfile.md): `STRICT_SCHEMA=ON` fails on
unaccepted drift, `IGNORE_EXTRA_COLUMNS=ON` accepts surplus columns, `NULL_MISSING_COLUMNS=ON` fills
absent template columns with `NULL`, and `MAP_BY_HEADER_NAME=ON` aligns by unique source header names.
Use `EXPECT SCHEMA` after staging if the accepted temp-table shape is part of the pipeline contract.

## Examples

```sql
-- Specific sheet and range
CREATE CONNECTION xl_src AS EXCEL('C:\Reports\Q4.xlsx', SHEET='Summary', HEADER=ON, RANGE='A1:F500');

-- Write an encrypted workbook
CREATE CONNECTION xl_out AS EXCEL(PATH='C:\Secure\payroll.xlsx', ENCRYPT=ON, PASSWORD='safe_pass');
```

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [FLATFILE](flatfile.md)
