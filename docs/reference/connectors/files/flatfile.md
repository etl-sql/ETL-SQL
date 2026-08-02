# FLATFILE

General-purpose connector for delimited (CSV/TSV) and fixed-width text files — the most flexible
file-based connector. When querying a `FLATFILE` connection via `SELECT`, the table name is `FILE` and
columns are named from the header row, or `Column1`, `Column2`, … when there is no header.

Aliases: `CSV`, `TSV`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `PATH` | Absolute path to the file | Yes (structured) |
| `DELIMITER` | Column separator: `COMMA`, `PIPE`, `TAB`, `SEMICOLON`, `COLON`, `TILDE`, or a literal char (default: `COMMA`) | No |
| `ROW_DELIMITER` | Row separator: `LF`, `CR`, `CRLF`, or a literal char (default: `CRLF`) | No |
| `HEADER` | `ON`/`OFF` — treat first row as column names (default: `ON`) | No |
| `TEXT_QUALIFIER` | Quote character: `DOUBLEQUOTE`, `SINGLEQUOTE`, or a literal char | No |
| `ESCAPE_CHAR` | Character used to escape delimiters within fields (e.g. `'\\'`) | No |
| `ENCODING` | `UTF8`, `ANSI`, `UTF16`, `LATIN1`, `UNICODE` (default: `UTF8`) | No |
| `CULTURE` | Locale for date/number parsing (e.g. `en-US`, `de-DE`) | No |
| `NULL_AS` | How nulls are represented: `NULL`, `EMPTY`, `BACKSLASH_N` | No |
| `DATE_FORMAT` | Custom date parsing pattern (e.g. `'yyyy-MM-dd'`) | No |
| `START_AT` | 1-based line number to start reading | No |
| `END_AT` | 1-based line number to stop reading | No |
| `TRIM` | `ON`/`OFF` — remove leading/trailing whitespace from fields | No |
| `COUNT_AT_END` | `ON`/`OFF` — validate row count against a trailer record (default: `OFF`) | No |
| `STRICT_SCHEMA` | `ON`/`OFF` — enforce column count matching (default: `OFF`) | No |
| `IGNORE_EXTRA_COLUMNS` | `ON`/`OFF` — omit source columns not present in the template schema instead of appending them | No |
| `NULL_MISSING_COLUMNS` | `ON`/`OFF` — include template columns missing from the source and fill with `NULL` | No |
| `MAP_BY_HEADER_NAME` | `ON`/`OFF` — align by case-insensitive header name instead of position; requires unique source headers | No |
| `FORMAT` | `DELIMITED` (default) or `FIXED` | No |
| `TEMPLATE` | Name of a `#temp` table defining fixed-width offsets (required if `FORMAT=FIXED`) | Conditional |
| `COMPRESS` | `ON`/`OFF` — transparent GZip read/write (default: `OFF`) | No |
| `ENCRYPT` | `ON`/`OFF` — AES file encryption (default: `OFF`) | No |
| `PASSWORD` | Password for encryption/decryption (required if `ENCRYPT=ON`) | Conditional |
| `ALGORITHM` | Hash algorithm: `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` (default: `SHA2_256`) | No |
| `KEYFILE` | Path to private SSH key for key-pair encryption | Conditional |
| `PASSPHRASE` | Passphrase for the key file | Conditional |

> [!NOTE]
> When both `COMPRESS=ON` and `ENCRYPT=ON` are specified, the engine always applies compression first,
> then encryption — regardless of option order. Encryption maximises entropy, making subsequent
> compression ineffective.

Schema-resilient reads require a destination/template schema (such as the destination table schema
supplied during `BULK INSERT`). `STRICT_SCHEMA=ON` still fails on drift unless a resilience option
explicitly accepts it: `IGNORE_EXTRA_COLUMNS=ON` accepts surplus source columns, and
`NULL_MISSING_COLUMNS=ON` accepts missing source columns by filling template columns with `NULL`.
`MAP_BY_HEADER_NAME=ON` changes alignment only. When resilience changes the accepted shape, the
connector emits a diagnostic with ignored extra-column count, null-filled missing-column count, and
affected row count. Use `EXPECT SCHEMA` after loading into `#temp` when the downstream contract must be
asserted against the accepted temp-table shape.

## Examples

```sql
-- Pipe-delimited with explicit encoding
CREATE CONNECTION csv_in AS FLATFILE(PATH='C:\Data\employees.csv', HEADER=ON, DELIMITER='PIPE', ENCODING='UTF8');

-- Encrypted and GZip-compressed
CREATE CONNECTION secure_file AS FLATFILE(PATH='C:\Data\payroll.csv.gz', COMPRESS=ON, ENCRYPT=ON, PASSWORD='s3cr3t');

-- European locale with semicolon delimiter and custom date format
CREATE CONNECTION eu_data AS FLATFILE(PATH='C:\Data\german_sales.csv', DELIMITER='SEMICOLON', CULTURE='de-DE', DATE_FORMAT='dd.MM.yyyy');

-- Skip header and first 2 data rows, stop at row 1000
CREATE CONNECTION paged AS FLATFILE(PATH='C:\Data\big.csv', HEADER=ON, START_AT=3, END_AT=1000);
```

## Fixed-width layouts (`FORMAT = 'FIXED'`)

To read a fixed-width file, define a `TEMPLATE` table that specifies the width of each field; the engine
slices each line using the declared widths. `TEMPLATE` is mandatory when `FORMAT='FIXED'`, and the
engine raises an error if any column width cannot be determined.

Width resolution per column:

| Form | Physical slot width | Use when |
| :--- | :--- | :--- |
| `CHAR(N)` / `VARCHAR(N)` / `NVARCHAR(N)` | N characters | Character data — N is the exact field width |
| `INT(N)` / `BIGINT(N)` etc. | N+1 characters | Integer data — N significant digits; the extra slot holds the sign, giving the range −(10ⁿ−1) to (10ⁿ−1) |
| `INT(N,+)` | N characters | Positive-only integer — no sign slot is reserved, so the field is exactly N wide. Range 0 to (10ⁿ−1) |
| `INT(N,-)` | N+1 characters | Negative-only integer — the sign slot still holds the `-`. Range −(10ⁿ−1) to 0 |
| `/* @width: N */` | N characters (exact) | Any column where the type carries no natural length, or to override the type width |

```sql
-- 1. Define the layout
CREATE TABLE #EmpLayout (
    ID      INT          /* @width: 5 */,
    Name    VARCHAR(20),          -- width = 20 from VARCHAR length
    Dept    CHAR(3),              -- width = 3 from CHAR length
    Active  BIT          /* @width: 1 */
);

-- 2. Create the connection
CREATE CONNECTION fixed_emp AS FLATFILE('employees.dat', FORMAT='FIXED', TEMPLATE=#EmpLayout, HEADER=OFF, TRIM=ON);

-- 3. Query as normal
SELECT * FROM fixed_emp;
```

### Sign constraints

Mainframe and legacy fixed-width feeds often reserve no column for a sign because the field can
only ever be positive. Declaring `INT(N,+)` says so explicitly: the slot is exactly `N` characters
instead of `N+1`, and writing a negative value is an error rather than a silently truncated `-`.

```sql
CREATE TABLE #InvoiceLayout (
    InvoiceNo  INT(6,+),       -- 6 chars, positive only  (no sign slot)
    Adjustment INT(5,-),       -- 6 chars, negative only  (sign slot holds '-')
    Balance    INT(9),         -- 10 chars, either sign
    Customer   VARCHAR(30)
);
```

A value that breaks the constraint fails the row with a message naming the column and the declared
type. `SET SKIP_ERROR = ON` blanks the offending field instead, consistent with how width overflow
is handled.

> [!NOTE]
> `INT` without a precision parameter has no inherent length and raises a "Width not defined" error.
> Use `INT(N)`, `CHAR(N)`, `VARCHAR(N)`, or `/* @width: N */` for every column in a `FORMAT=FIXED` layout.

## References

- [File Connectors](README.md)
- [Connectors](../README.md)
- [Excel](excel.md) · [JSON](json.md) · [BULK INSERT](../../file-operations/bulk-insert.md)
